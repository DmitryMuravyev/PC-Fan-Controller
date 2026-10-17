////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// DataExchangeFlags.cs
////////////////////////////////////////////////////////////////////////////////////////

namespace FanController.Service.Core;

/// <summary>
/// Update state of one element of the controller state snapshot.
/// All elements are requested sequentially in a strictly defined order, and an
/// element that is up to date (or already requested) is not requested again until
/// the next update cycle. The state changes atomically: Outdated → Requested → UpToDate.
/// </summary>
public sealed class DataExchangeFlags
{
    // Constants
    private static class DataExchangeConstants
    {
        public const long RequestTimeoutMs = 10000;     // A request without an answer for this long is considered lost
    }


    private enum ExchangeState
    {
        Outdated,       // Data needed, no request running
        Requested,      // Request running (expires after RequestTimeoutMs)
        UpToDate        // Data received
    }


    // Any answer counts (e.g. for data sent to the device), not only one with data
    private readonly bool _answerIsEnough;

    private int _state = (int)ExchangeState.Outdated;      // int for Interlocked
    private long _requestTick = 0;                          // Environment.TickCount64

    private ExchangeState State => (ExchangeState)Volatile.Read(ref _state);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public DataExchangeFlags(bool requiresData = true)
    {
        _answerIsEnough = !requiresData;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // State
    // Note: IsUpToDate and IsRequestDue are not opposites;
    // while a request is running, both are false.

    public bool IsUpToDate => State == ExchangeState.UpToDate;

    // A request may start now (TryBeginRequest would succeed)
    public bool IsRequestDue => State == ExchangeState.Outdated;

    public bool IsRequestRunning => State == ExchangeState.Requested;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Public API

    /// <summary>
    /// Atomically claims the request of an outdated element.
    /// Returns true if the caller must send the request.
    /// </summary>
    public bool TryBeginRequest()
    {
        if (State != ExchangeState.Outdated)
            return false;

        // Set before the claim, so an expiry check never sees a new claim with an old tick
        Volatile.Write(ref _requestTick, Environment.TickCount64);

        return Interlocked.CompareExchange(
            ref _state,
            (int)ExchangeState.Requested,
            (int)ExchangeState.Outdated) == (int)ExchangeState.Outdated;
    }


    /// <summary>
    /// Starts a new update cycle: the element becomes outdated, unless its request
    /// is still running (a request without an answer expires after a timeout).
    /// </summary>
    public void CheckAndSetOutdated()
    {
        if ((Volatile.Read(ref _state) == (int)ExchangeState.Requested) &&
            ((Environment.TickCount64 - Volatile.Read(ref _requestTick)) < DataExchangeConstants.RequestTimeoutMs))
            return;

        Volatile.Write(ref _state, (int)ExchangeState.Outdated);
    }


    public void SetOutdated() =>
        Volatile.Write(ref _state, (int)ExchangeState.Outdated);


    public void GotData() =>
        Volatile.Write(ref _state, (int)ExchangeState.UpToDate);


    /// <summary>
    /// Applies an answer: up to date if it carries the data (or any answer is enough),
    /// outdated otherwise (requested again).
    /// </summary>
    public void GotAnswer(bool dataReceived)
    {
        if (_answerIsEnough || dataReceived)
            GotData();
        else
            SetOutdated();
    }
}

// END-OF-FILE
