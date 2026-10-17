////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// PendingRequest.cs
////////////////////////////////////////////////////////////////////////////////////////

using FanController.Service.Core.Parsers;
using FanController.Contracts;


namespace FanController.Service.Core;

/// <summary>
/// The client command a request belongs to (default: not a client command)
/// </summary>
public readonly record struct CommandTag(Guid Id, CommandKind Kind);


/// <summary>
/// A request or command sent to the device: its report, the expected answers
/// (confirmation / completion) and the retry state.
/// </summary>
public sealed class PendingRequest
{
    // Constants
    private static class PendingRequestConstants
    {
        public const int MinRequestPeriodMs = 50;               // Minimum interval between request repeats (for confirmation/completion awaiting)
        public const int MaxRequestPeriodMs = 1000;             // Maximum interval between request repeats (for confirmation/completion awaiting)
        public const int RequestPeriodIncrementStepMs = 100;    // Request repeats interval increment (for confirmation/completion awaiting)
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Report building

    // Sequential request number (shared by all controllers)
    private static uint _requestIndex = 0;


    /// <summary>
    /// Builds an output report of <paramref name="length"/> bytes
    /// (an empty array if the length is not known).
    /// </summary>
    public static byte[] BuildReport(byte reportId, byte commandCode, byte[]? payload, int length, uint requestId)
    {
        if (length == 0)
            return Array.Empty<byte>();

        var buffer = new byte[length];
        var outPacket = new CommunicationPacket(buffer)
        {
            ReportID = reportId,
            RequestID = requestId,
            Code = commandCode
        };

        if (payload is not null)
            outPacket.Data = payload;

        return buffer;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Private state

    // Answer flags (set by the read loop, read by the write loop)
    private volatile bool _confirmed = false;
    private volatile bool _completed = false;
    private volatile bool _rejected = false;

    // Retry interval management
    private long _lastAnswerTick = 0;       // Environment.TickCount64
    private long _requestPeriod = PendingRequestConstants.MinRequestPeriodMs;

    // Sends without an answer
    private int _attempts = 0;

    // Reconnections caused by this request
    private int _reconnectAttempts = 0;

    // Pulsed on every answer: wakes up the write loop waiting for it
    private readonly SemaphoreSlim _answerSignal = new(0, 1);
    private readonly object _signalGate = new();


    ////////////////////////////////////////////////////////////////////////////////////////
    // Public properties

    public Communication_Commands Command { get; }
    public uint RequestId { get; }
    public byte[] Buffer { get; }
    public bool IsMandatory { get; }
    public bool WaitConfirmation { get; }
    public bool WaitCompletion { get; }
    public int? TargetObjectIndex { get; }
    public byte[]? TargetObjectSN { get; }

    // Client command this request belongs to, and whether it is the command's last request
    public CommandTag CommandTag { get; }
    public bool IsLastOfCommand { get; }

    // Snapshot element updated by the answer (if any)
    public DataExchangeFlags? ExchangeFlags { get; }

    // Sends without an answer (reset by any answer)
    public int Attempts => Volatile.Read(ref _attempts);

    // Reconnections caused by this request (reset by any answer)
    public int ReconnectAttempts => Volatile.Read(ref _reconnectAttempts);


    /// <summary>
    /// The request is finished: completed, or confirmed when no completion is awaited,
    /// or no answer is awaited at all. A rejected ordinary request is finished too
    /// (its data is requested again in the next cycle). A rejected mandatory request
    /// is not: it is retried until the device accepts it, also after reconnections.
    /// </summary>
    public bool Done =>
        _completed ||
        (_confirmed && !WaitCompletion) ||
        (!WaitConfirmation && !WaitCompletion) ||
        (!IsMandatory && _rejected);


    // Enough time has elapsed since the last answer to send the request again
    public bool RetryDue
    {
        get
        {
            long lastAnswerTick = Volatile.Read(ref _lastAnswerTick);
            return (lastAnswerTick == 0) ||
                   ((Environment.TickCount64 - lastAnswerTick) >= Volatile.Read(ref _requestPeriod));
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public PendingRequest(
        Communication_Commands command,
        byte[]? payload,
        int fullLength,
        bool waitConfirmation,
        bool waitCompletion,
        int? index,
        byte[]? sn,
        bool isMandatory = false,
        DataExchangeFlags? exchangeFlags = null,
        CommandTag commandTag = default,
        bool isLastOfCommand = true)
    {
        Command = command;
        RequestId = Interlocked.Increment(ref _requestIndex);
        Buffer = BuildReport(
            (byte)USB_HID_Reports.USB_HID_Report_Host_Command,
            (byte)Command,
            payload,
            fullLength,
            RequestId);
        IsMandatory = isMandatory;
        ExchangeFlags = exchangeFlags;
        CommandTag = commandTag;
        IsLastOfCommand = isLastOfCommand;
        WaitConfirmation = waitConfirmation;
        WaitCompletion = waitCompletion;
        TargetObjectIndex = index;
        TargetObjectSN = sn;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Private logic

    private void SignalAnswer()
    {
        // The read loop and the shutdown / reconnect path may signal concurrently
        lock (_signalGate)
        {
            if (_answerSignal.CurrentCount == 0)
                _answerSignal.Release();
        }
    }


    // Updates the timestamp of the last answer; the retry period grows with every answer
    private void UpdatePeriod()
    {
        Volatile.Write(ref _lastAnswerTick, Environment.TickCount64);

        if (_confirmed || _rejected)
        {
            long period = Math.Min(
                Volatile.Read(ref _requestPeriod) + PendingRequestConstants.RequestPeriodIncrementStepMs,
                PendingRequestConstants.MaxRequestPeriodMs);
            Volatile.Write(ref _requestPeriod, period);
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Public API

    public string Description()
    {
        string description = $"{{{Command}";
        if (TargetObjectIndex.HasValue) description += $", Target index: {TargetObjectIndex.Value}";
        if (TargetObjectSN is not null) description += $", Target SN: {Convert.ToHexString(TargetObjectSN)}";
        return description + "}";
    }


    public int IncrementAttempts() => Interlocked.Increment(ref _attempts);

    public int IncrementReconnectAttempts() => Interlocked.Increment(ref _reconnectAttempts);

    public void ResetAttempts(bool includingReconnects = false)
    {
        Volatile.Write(ref _attempts, 0);
        if (includingReconnects) Volatile.Write(ref _reconnectAttempts, 0);
    }

    // Waits for the next answer (confirmation, completion or rejection)
    public Task WaitForAnswerAsync(CancellationToken ct) => _answerSignal.WaitAsync(ct);

    // Discards an answer signal left over from a previous attempt
    public void ClearAnswerSignal()
    {
        while (_answerSignal.Wait(0)) { }
    }


    public void SetConfirmed()
    {
        UpdatePeriod();
        _confirmed = true;
        SignalAnswer();
    }


    public void SetCompleted(bool gotData = false)
    {
        _completed = true;
        ExchangeFlags?.GotAnswer(gotData);
        SignalAnswer();
    }


    // The device is busy (retry later), or the request can't be delivered (shutdown / reconnect)
    public void SetRejected()
    {
        UpdatePeriod();
        _rejected = true;
        ExchangeFlags?.SetOutdated();
        SignalAnswer();
    }
}

// END-OF-FILE
