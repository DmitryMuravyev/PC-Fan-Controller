////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// FanController.cs
////////////////////////////////////////////////////////////////////////////////////////

using System.Text;
using System.Threading.Channels;
using FanController.Contracts;
using FanController.Service.Core.Parsers;
using HidSharp;


namespace FanController.Service.Core;

/// <summary>
/// Represents a single physical fan controller connected to the host.
/// </summary>
public class FanController : IAsyncDisposable, IDisposable
{
    // Constants
    private static class FanControllerConstants
    {
        public const int OrdinaryOutgoingQueueSize = 128;      // Periodical requests queue max size
        public const int RetriesCount = 5;        // Request sending retries
        public const int ReconnectCount = 3;        // Reconnections caused by one unanswered request
                                                    // (RetriesCount sends each); then the request is rejected.
        public const int ReconnectStabilizationDelayMs = 1000;     // 1 second minimum needed Stabilization Delay after reconnection
        public const int StreamWriteTimeoutMs = 1000;     // HID report transmission timeout
        public const int ResponseTimeoutMs = 1000;     // Request response timeout
        public const int CompletionTimeoutMs = 3000;     // Tasks completion timeout
        public const int MinRequestIntervalMs = 25;       // Minimum interval between the sequential sending of requests from the queue
        public const int MinResponseToRequestIntervalMs = 5;        // Minimum interval between a response and the next request
        public const int HouseKeepingPeriodMs = 1000;     // Controller state snapshot update period
        public const int ReducedPollIntervalTicks = 10;       // Number of skipped polls of fan and sensor states when no clients are connected

        // Mask of state flags to ignore to avoid cluttering the log
        public const uint SystemStateIgnoreMask = 0;
        public const uint FanStateIgnoreMask = (uint)(Fan_Status.Fan_Status_PID_Action_Required |
                                                                 Fan_Status.Fan_Status_PID_Suspended |
                                                                 Fan_Status.Fan_Status_Spinning_Up);
        public const byte SensorStateIgnoreMask = 0;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Private properties

    // Logging
    private readonly ILogger<FanController> _logger;
    private IDisposable? Scope() =>
        _logger.BeginScope(new Dictionary<string, object> { ["SN"] = SN });

    // State & Synchronization

    private HidDevice? _device = null;
    private HidStream? _stream = null;
    private volatile bool _isConnected = false;
    private volatile bool _disposed = false;
    private volatile bool _isBusy = false;
    private volatile bool _inProgress = false;
    private volatile bool _isActive = false;

    // Controller's state snapshot
    private volatile SystemConfigDto _systemConfig = DtoDefaults.Controller.NewConfig();
    private volatile SystemStateDto _systemState = DtoDefaults.Controller.NewState();
    private volatile uint _systemStateFlags = 0;
    private volatile FanConfigDto[] _fanConfigs = [];
    private volatile FanStateDto[] _fanStates = [];
    private volatile uint[] _fanStateFlags = [];
    private volatile SubstitutionMatrixDto _fanSubstitutionMatrix = DtoDefaults.SubstitutionMatrix.NewMatrix();
    private volatile SensorConfigDto[] _sensorConfigs = [];
    private volatile SensorStateDto[] _sensorStates = [];
    private volatile byte[] _sensorStateFlags = [];

    private Task? _readTask = null;
    private Task? _writeTask = null;
    private Task? _hkTask = null;

    private CancellationTokenSource? _streamCTS = null;
    private CancellationTokenSource? _hkCTS = null;

    // Not disposed: they hold no wait handles, and late callers must not fail
    private readonly SemaphoreSlim _queueSignal = new(0);       // a request was queued
    private readonly SemaphoreSlim _hkSignal = new(0, 1);       // housekeeper early wake-up

    private readonly SemaphoreSlim _activationGate = new(1, 1); // serializes activate / deactivate / dispose
    private readonly object _enqueueLock = new();
    private bool _acceptingRequests = false;                    // guarded by _enqueueLock
    private volatile bool _draining = false;

    private readonly object _deviceLock = new();
    private readonly object _configLock = new();
    private readonly object _activeStateLock = new();

    // From the HID descriptor (known before the device is opened)
    private volatile int _outputReportLength = 0;
    private volatile int _inputReportLength = 0;

    // Commands queue (high priority)
    private readonly Channel<PendingRequest> _mandatoryRequestQueue =
        Channel.CreateUnbounded<PendingRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

    // Periodical requests queue (low priority)
    private readonly Channel<PendingRequest> _ordinaryRequestQueue =
        Channel.CreateBounded<PendingRequest>(
            new BoundedChannelOptions(FanControllerConstants.OrdinaryOutgoingQueueSize)
            {
                FullMode = BoundedChannelFullMode.Wait,     // Not DropWrite, because TryWrite will always return 'true'.
                                                            // In our case (using TryWrite instead of WriteAsync),
                                                            // this will lead to the correct result.
                SingleReader = true,
                SingleWriter = false
            });

    private PendingRequest? _currentOrdinaryRequest = null;
    private PendingRequest? _currentMandatoryRequest = null;

    private DataExchangeFlags _systemStateEF = new();
    private DataExchangeFlags _systemConfigEF = new();
    private DataExchangeFlags _fanCountEF = new();
    private DataExchangeFlags[] _fanConfigsEF = Array.Empty<DataExchangeFlags>();
    private DataExchangeFlags[] _fanStatesEF = Array.Empty<DataExchangeFlags>();
    private DataExchangeFlags _substitutionMatrixEF = new();
    private DataExchangeFlags _sensorCountEF = new();
    private DataExchangeFlags[] _sensorConfigsEF = Array.Empty<DataExchangeFlags>();
    private DataExchangeFlags[] _sensorTemperatureEF = Array.Empty<DataExchangeFlags>();
    private DataExchangeFlags[] _sensorStatesEF = Array.Empty<DataExchangeFlags>();

    // Poll skip counters (reduce frequency when no subscribers)
    private int _fanStatePollSkip = 0;
    private int _sensorStatePollSkip = 0;

    // 'Not configured' answers hold these config requests until the next polling cycle
    private volatile bool _holdSystemConfigRequests = false;
    private volatile bool _holdFanConfigRequests = false;
    private volatile bool _holdSensorConfigRequests = false;

    // Result of the last command
    private volatile OperationResultDto _lastOperationResult = new()
    {
        Result = OperationResult.OK,
        TargetObjectIndex = null,
        TargetObjectSN = null
    };


    ////////////////////////////////////////////////////////////////////////////////////////
    // Public properties

    public string Name { get; }
    public string SN { get; }
    public Version Release { get; }

    // Is the Controller should be connected
    public bool IsActive => _isActive;

    // Subscriber-aware flags (set by the manager)
    private volatile bool _hasFanStateSubscribers = false;
    private volatile bool _hasSensorStateSubscribers = false;

    public bool HasFanStateSubscribers
    {
        get => _hasFanStateSubscribers;
        set => _hasFanStateSubscribers = value;
    }

    public bool HasSensorStateSubscribers
    {
        get => _hasSensorStateSubscribers;
        set => _hasSensorStateSubscribers = value;
    }

    public event EventHandler<OverallStateDto>? StateChanged;
    public event EventHandler<FanControllerMessage>? MessageReceived;
    public Func<byte[], double?>? GetHostSensorTemperature { get; set; }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Snapshot public access

    private volatile int _fanCount = 0;
    private volatile int _sensorCount = 0;

    public int FanCount => _fanCount;
    public int SensorCount => _sensorCount;

    public bool IsAvailable => _device is not null;
    public bool IsConnected => _isConnected;
    public SystemConfigDto SystemConfig => _systemConfig;
    public SystemStateDto SystemState => _systemState;
    public SubstitutionMatrixDto FanSubstitutionMatrix => _fanSubstitutionMatrix;

    // Result of the last command (not of the background polling)
    public OperationResultDto LastOperationResult => _lastOperationResult;


    public FanConfigDto? FanConfig(int index) => ElementAt(_fanConfigs, index);

    public FanStateDto? FanState(int index) => ElementAt(_fanStates, index);

    public SensorStateDto? SensorState(int index) => ElementAt(_sensorStates, index);

    public SensorConfigDto? SensorConfig(int index) => ElementAt(_sensorConfigs, index);


    public SensorStateDto? SensorState(byte[] serial)
    {
        lock (_configLock)
        {
            int? index = FindSensorIndex(serial);
            return index.HasValue ? ElementAt(_sensorStates, index.Value) : null;
        }
    }


    public SensorConfigDto? SensorConfig(byte[] serial)
    {
        lock (_configLock)
        {
            int? index = FindSensorIndex(serial);
            return index.HasValue ? ElementAt(_sensorConfigs, index.Value) : null;
        }
    }


    /// <summary>
    /// Full device configuration (null until all of it has been read from the device)
    /// </summary>
    public DeviceFullConfig? FullConfig()
    {
        lock (_configLock)
        {
            if (!_systemConfigEF.IsUpToDate ||
                !_substitutionMatrixEF.IsUpToDate ||
                !AllUpToDate(_fanCountEF, _fanConfigsEF) ||
                !AllUpToDate(_sensorCountEF, _sensorConfigsEF))
                return null;

            return new DeviceFullConfig()
            {
                SystemConfig = _systemConfig,
                Fans = _fanConfigs.ToArray(),           // Copies: the snapshot arrays are updated in place
                SubstitutionMatrix = _fanSubstitutionMatrix,
                Sensors = _sensorConfigs.ToArray()
            };
        }
    }


    // Snapshot array element (null if out of range or not received yet)
    private static T? ElementAt<T>(T?[] array, int index) where T : class =>
        ((index >= 0) && (index < array.Length)) ? array[index] : null;


    // A count and all its items are up to date (under _configLock)
    private static bool AllUpToDate(DataExchangeFlags countEF, DataExchangeFlags[] itemsEF) =>
        countEF.IsUpToDate && itemsEF.All(ef => ef.IsUpToDate);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction / Disposal

    /// <summary>
    /// Creates the controller object for a discovered HID device
    /// (the device is opened after activation).
    /// </summary>
    /// <param name="device">The HID device.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public FanController(HidDevice device, ILoggerFactory loggerFactory)
    {
        _device = device;
        Name = device.GetFriendlyName();
        SN = device.GetSerialNumber();
        Release = device.ReleaseNumber;
        _logger = loggerFactory.CreateLogger<FanController>();

        ReadReportLengths(device);

        if (_logger.IsEnabled(LogLevel.Trace))
            using (Scope()) _logger.LogTrace("Controller object created for device.");
    }


    // Disposal
    public async ValueTask DisposeAsync()
    {
        await _activationGate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;

            lock (_activeStateLock) { _isActive = false; }

            await StopHousekeeperAsync();

            // Send queued requests and close
            await FlushAndCloseAsync(
                TimeSpan.FromMilliseconds(FanControllerConstants.CompletionTimeoutMs));

            _mandatoryRequestQueue.Writer.TryComplete();
            _ordinaryRequestQueue.Writer.TryComplete();
        }
        finally { _activationGate.Release(); }

        if (_logger.IsEnabled(LogLevel.Trace))
            using (Scope()) _logger.LogTrace(
                "Controller object disposed for device.");

        GC.SuppressFinalize(this);
    }

    // Synchronous fallback for IDisposable consumers.
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();


    ////////////////////////////////////////////////////////////////////////////////////////
    // Device Communication

    // Read loop
    private async Task ReadLoop(HidStream stream, int reportLength, CancellationToken token)
    {
        using var scope = Scope();

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Read loop started.");

        var buffer = new byte[reportLength];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int count = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (count <= 0)
                    throw new IOException("Device disconnected.");

                // A report that can't be processed must not break the connection
                try
                {
                    ProcessIncomingReport(buffer, count);
                }
                catch (Exception ex)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                        _logger.LogError(
                            "Incoming report processing error: {Exception}",
                            ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (ObjectDisposedException)
            when (token.IsCancellationRequested)
        {
            // Stream disposed during shutdown
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError(
                    "Read loop exception: {Exception}",
                    ex.Message);

            _isConnected = false;
            TriggerHousekeeper();
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Read loop completed.");
    }


    // Write loop
    private async Task WriteLoop(HidStream stream, CancellationToken token)
    {
        using var scope = Scope();

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Write loop started.");

        long lastRequestTick = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                // First, a mandatory request (unless it is waiting for its retry time)
                PendingRequest? mandatoryRequest = Volatile.Read(ref _currentMandatoryRequest);
                if ((mandatoryRequest is null) &&
                    _mandatoryRequestQueue.Reader.TryRead(out mandatoryRequest))
                    Volatile.Write(ref _currentMandatoryRequest, mandatoryRequest);

                PendingRequest? currentRequest =
                    ((mandatoryRequest is not null) && mandatoryRequest.RetryDue) ? mandatoryRequest : null;

                // Then an ordinary one.
                // Ordinary queue will be ignored when we enter the Drain mode.
                if ((currentRequest is null) && !_draining)
                {
                    currentRequest = Volatile.Read(ref _currentOrdinaryRequest);
                    if ((currentRequest is null) &&
                        _ordinaryRequestQueue.Reader.TryRead(out currentRequest))
                        Volatile.Write(ref _currentOrdinaryRequest, currentRequest);
                }

                // Nothing to send now
                if (currentRequest is null)
                {
                    // While draining, the gate is closed, so the queues can only
                    // shrink from here: exit once no mandatory request is left.
                    if (_draining && (mandatoryRequest is null))
                    {
                        if (_logger.IsEnabled(LogLevel.Debug))
                            _logger.LogDebug("Mandatory request queue drained.");
                        break;
                    }

                    // Wait for a new request; a mandatory request
                    // waiting for its retry time is polled.
                    if (mandatoryRequest is not null)
                        await _queueSignal.WaitAsync(FanControllerConstants.MinRequestIntervalMs, token);
                    else
                        await _queueSignal.WaitAsync(token);
                    continue;
                }

                currentRequest.IncrementAttempts();

                if (_logger.IsEnabled(LogLevel.Trace))
                    _logger.LogTrace(
                        "Sending Request: '{Request}'",
                        currentRequest.Description());

                // Check the time since the previous request
                long remainingWait = FanControllerConstants.MinRequestIntervalMs -
                    (Environment.TickCount64 - lastRequestTick);
                if (remainingWait > 0)
                    await Task.Delay((int)remainingWait, token);

                currentRequest.ClearAnswerSignal();
                await stream.WriteAsync(currentRequest.Buffer, 0, currentRequest.Buffer.Length, token);

                lastRequestTick = Environment.TickCount64;

                // Wait for the answer if we have to.
                if (!currentRequest.Done)
                {
                    using var responseCts =
                        CancellationTokenSource.CreateLinkedTokenSource(token);
                    responseCts.CancelAfter(FanControllerConstants.ResponseTimeoutMs);

                    try
                    {
                        await currentRequest.WaitForAnswerAsync(responseCts.Token);
                        lastRequestTick = 0;    // Got response, so the device is ready for a new request.

                        // Additional wait for the minimum interval
                        // between the response and the next request
                        await Task.Delay(
                            FanControllerConstants.MinResponseToRequestIntervalMs,
                            token);
                    }
                    catch (OperationCanceledException)
                        when (!token.IsCancellationRequested)   // timeout, not shutdown
                    {
                        HandleNoAnswer(currentRequest);
                    }
                }

                // Clear current request when it is finished
                if (currentRequest.Done)
                {
                    if (currentRequest.IsMandatory)
                        Volatile.Write(ref _currentMandatoryRequest, null);
                    else
                        Volatile.Write(ref _currentOrdinaryRequest, null);
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* normal shutdown */
        }
        catch (ObjectDisposedException)
            when (token.IsCancellationRequested)
        {
            // Stream disposed during shutdown
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError(
                    "Write loop exception: {Exception}",
                    ex.Message);

            _isConnected = false;
            TriggerHousekeeper();
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Write loop completed.");
    }


    // No answer within the timeout (write loop).
    // After RetriesCount sends, a reconnection is requested (IOException),
    // up to ReconnectCount times; then the request is rejected
    // (a mandatory one keeps being retried, without further reconnections).
    private void HandleNoAnswer(PendingRequest request)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug(
                "No answer from device after {Attempts} attempt(s).",
                request.Attempts);

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace(
                "Failed Request: '{Request}'",
                request.Description());

        if (request.Attempts < FanControllerConstants.RetriesCount)
            return;     // retry the same request

        request.ResetAttempts();

        if (request.IncrementReconnectAttempts() < FanControllerConstants.ReconnectCount)
            throw new IOException("Device not responding.");

        request.SetRejected();
    }


    // Housekeeper loop
    private async Task HouseKeeperLoop(CancellationToken token)
    {
        using var scope = Scope();

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Housekeeper loop started.");

        while (!token.IsCancellationRequested)
        {
            try
            {
                bool reconnectNeeded;
                lock (_deviceLock)
                {
                    reconnectNeeded = (_device is not null) && !_isConnected;
                }

                if (reconnectNeeded)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Housekeeper initiated a reconnection to the device.");

                    CloseDevice();

                    // Allow USB stack and device firmware to stabilize
                    // before attempting to reopen
                    await Task.Delay(
                        FanControllerConstants.ReconnectStabilizationDelayMs,
                        token);

                    OpenDevice(token);
                }
                else if (_isConnected)
                {
                    StartPollingCycle();
                }

                // Sleep until next period or explicit wake
                await _hkSignal.WaitAsync(FanControllerConstants.HouseKeepingPeriodMs, token);
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                break;  /* normal shutdown */
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "Housekeeper exception: {Exception}",
                        ex.Message);

                try
                {
                    await Task.Delay(FanControllerConstants.HouseKeepingPeriodMs, token);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Housekeeper loop completed.");
    }


    // Starts a new update cycle of the state snapshot
    // (the rest of the data is requested by ProcessIncomingReport)
    private void StartPollingCycle()
    {
        // Fan and sensor states: every cycle if there are subscribers, reduced rate otherwise
        if (HasFanStateSubscribers || PollDue(ref _fanStatePollSkip))
            foreach (var fss in Volatile.Read(ref _fanStatesEF)) fss.CheckAndSetOutdated();

        if (HasSensorStateSubscribers || PollDue(ref _sensorStatePollSkip))
            foreach (var sss in Volatile.Read(ref _sensorStatesEF)) sss.CheckAndSetOutdated();

        foreach (var sts in Volatile.Read(ref _sensorTemperatureEF)) sts.CheckAndSetOutdated();

        _isBusy = false;

        // Config requests answered 'not configured' may be repeated once per cycle
        _holdSystemConfigRequests = false;
        _holdFanConfigRequests = false;
        _holdSensorConfigRequests = false;

        _systemStateEF.CheckAndSetOutdated();
        if (_systemStateEF.TryBeginRequest())
            RequestSystemState();
    }


    // Reduced-rate poll counter: true once every ReducedPollIntervalTicks + 1 calls
    private static bool PollDue(ref int skipCounter)
    {
        if (skipCounter > 0)
        {
            skipCounter--;
            return false;
        }

        skipCounter = FanControllerConstants.ReducedPollIntervalTicks;
        return true;
    }


    // Report lengths from the HID descriptor
    private void ReadReportLengths(HidDevice device)
    {
        try
        {
            _inputReportLength = device.GetMaxInputReportLength();
            _outputReportLength = device.GetMaxOutputReportLength();
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                using (Scope()) _logger.LogWarning(
                    "Report lengths unavailable: {Exception}",
                    ex.Message);
        }
    }


    // Open HID device (not after the housekeeper has been stopped)
    private void OpenDevice(CancellationToken token)
    {
        bool opened = false;

        lock (_deviceLock)
        {
            using var scope = Scope();

            try
            {
                if (token.IsCancellationRequested) return;

                if (_device is null)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                        _logger.LogError("Device unavailable.");
                    return;
                }

                if (_isConnected)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Device already opened.");
                    return;
                }

                if (_device.TryOpen(out HidStream stream))
                {
                    stream.ReadTimeout = Timeout.Infinite;
                    stream.WriteTimeout = FanControllerConstants.StreamWriteTimeoutMs;

                    ReadReportLengths(_device);

                    var cts = new CancellationTokenSource();
                    int inputReportLength = _inputReportLength;

                    _stream = stream;
                    _streamCTS = cts;
                    _readTask = Task.Run(() => ReadLoop(stream, inputReportLength, cts.Token));
                    _writeTask = Task.Run(() => WriteLoop(stream, cts.Token));

                    _isConnected = true;
                    opened = true;

                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Device opened.");
                }
                else
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                        _logger.LogError("Device opening error.");
                }
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "Device opening exception: {Exception}",
                        ex.Message);
            }
        }

        // Outside the lock: the handlers must not run under _deviceLock
        if (opened) StateUpdated();
    }


    // Close device
    private void CloseDevice()
    {
        HidStream? stream;
        Task? readTask;
        Task? writeTask;
        CancellationTokenSource? cts;

        lock (_deviceLock)
        {
            stream = _stream;
            if (stream is null) return;

            cts = _streamCTS;
            readTask = _readTask;
            writeTask = _writeTask;

            _stream = null;
            _streamCTS = null;
            _readTask = null;
            _writeTask = null;

            _isConnected = false;
        }

        StateUpdated();

        // Signal cancellation
        try { cts?.Cancel(); } catch { }

        // Flush stream
        try { stream.Flush(); } catch { }

        // Close the stream to unblock any pending synchronous I/O
        // (HidSharp's ReadAsync wraps a blocking Read)
        try { stream.Dispose(); } catch { }

        // Wait for tasks — they will exit promptly now that the
        // stream is disposed and the token is cancelled.
        // Note: a concurrent OpenDevice may already be creating new
        // tasks; that is safe — old tasks reference the old stream.
        try { readTask?.Wait(FanControllerConstants.CompletionTimeoutMs); } catch { }
        try { writeTask?.Wait(FanControllerConstants.CompletionTimeoutMs); } catch { }
        try { cts?.Dispose(); } catch { }

        if (_logger.IsEnabled(LogLevel.Information))
            using (Scope()) _logger.LogInformation("Device closed.");
    }


    // Housekeeper early wakeup trigger
    private void TriggerHousekeeper()
    {
        try { _hkSignal.Release(); }
        catch (SemaphoreFullException) { /* a wake-up is already pending */ }
    }


    // Stops the housekeeper and waits until it has finished: a reconnection
    // in progress must not reopen the device after the caller closes it.
    // (Every wait inside the housekeeper is bounded.)
    private async Task StopHousekeeperAsync()
    {
        CancellationTokenSource? cts;
        Task? task;

        lock (_activeStateLock)
        {
            cts = _hkCTS;
            _hkCTS = null;
            task = _hkTask;
            _hkTask = null;
        }

        // Never wait while holding _activeStateLock: the housekeeper calls
        // StateUpdated() from OpenDevice/CloseDevice.
        try { cts?.Cancel(); } catch { }

        if (task is not null)
        {
            try { await task; }
            catch { /* the housekeeper handles its own errors */ }
        }

        cts?.Dispose();
    }


    // Sends everything already queued, then closes the device.
    // Precondition: caller holds _activationGate and the housekeeper is stopped.
    private async Task FlushAndCloseAsync(TimeSpan timeout)
    {
        // Nothing new may enter the queues from this point
        lock (_enqueueLock) { _acceptingRequests = false; }

        // Let the running WriteLoop run the queues flush
        Task? writeTask;
        lock (_deviceLock) { writeTask = _writeTask; }

        if (writeTask is not null)
        {
            _draining = true;
            _queueSignal.Release();     // wake an idle WriteLoop

            var finished = await Task.WhenAny(writeTask, Task.Delay(timeout));
            if (finished != writeTask)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    using (Scope()) _logger.LogWarning(
                        "Queue Flush timed out after {Timeout}.",
                        timeout);
            }
            else if (_logger.IsEnabled(LogLevel.Debug))
                using (Scope()) _logger.LogDebug("Queue Flush completed.");
        }

        // Stop I/O
        // (no-op if never opened or already closed).
        CloseDevice();
        _draining = false;

        // Whatever is still queued could not be delivered
        // (timeout, device error, device never opened). Fail it so callers are released.
        RejectPending();
    }


    // Drain queues without sending
    private void RejectPending()
    {
        Interlocked.Exchange(ref _currentMandatoryRequest, null)?.SetRejected();
        Interlocked.Exchange(ref _currentOrdinaryRequest, null)?.SetRejected();

        while (_mandatoryRequestQueue.Reader.TryRead(out var r)) r.SetRejected();
        while (_ordinaryRequestQueue.Reader.TryRead(out var r)) r.SetRejected();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Report scheduling

    // Queues an ordinary request; returns false if it was dropped
    // (its data is then marked outdated to be requested again)
    private bool ScheduleOrdinaryRequest(
        Communication_Commands command,
        byte[]? payload,
        bool waitConfirmation,
        int? objectIndex = null,
        byte[]? objectSN = null,
        DataExchangeFlags? exchangeFlags = null,
        CommandTag commandTag = default)
    {
        var request = new PendingRequest(
            command,
            payload,
            _outputReportLength,
            waitConfirmation,
            false,
            objectIndex,
            objectSN,
            false,
            exchangeFlags,
            commandTag);

        bool accepting;
        bool queued;

        lock (_enqueueLock)
        {
            // We use BoundedChannelFullMode.Wait (not DropWrite,
            // because TryWrite will always return 'true').
            // In our case (using TryWrite instead of WriteAsync),
            // this will return 'true' only if the request
            // successfully queued.
            accepting = _acceptingRequests;
            queued = accepting && _ordinaryRequestQueue.Writer.TryWrite(request);
        }

        if (queued)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
                using (Scope()) _logger.LogTrace(
                    "Ordinary request '{Request}' queued.",
                    request.Description());
            _queueSignal.Release();
            return true;
        }

        exchangeFlags?.SetOutdated();

        if (accepting)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                using (Scope()) _logger.LogWarning(
                    "Ordinary request '{Request}' dropped (queue full).",
                    request.Description());
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
            using (Scope()) _logger.LogDebug(
                "Ordinary request '{Request}' dropped (requests not accepted).",
                request.Description());

        return false;
    }


    // Creates a mandatory request (not queued yet)
    private PendingRequest CreateMandatoryRequest(
        Communication_Commands command,
        byte[]? payload,
        bool waitCompletion,
        CommandTag commandTag,
        int? objectIndex = null,
        byte[]? objectSN = null,
        bool isLastOfCommand = true)
    {
        return new PendingRequest(
            command,
            payload,
            _outputReportLength,
            true,
            waitCompletion,
            objectIndex,
            objectSN,
            true,
            null,
            commandTag,
            isLastOfCommand);
    }


    // Queues mandatory requests as one sequence (all of them or none);
    // returns false if they were dropped
    private bool EnqueueMandatoryRequests(IReadOnlyList<PendingRequest> requests)
    {
        bool accepting;
        bool queued = true;

        lock (_enqueueLock)
        {
            // We are using an unbounded channel here,
            // but let's check the result just to be safe.
            accepting = _acceptingRequests;
            if (accepting)
                foreach (var request in requests)
                    queued &= _mandatoryRequestQueue.Writer.TryWrite(request);
        }

        if (accepting && queued)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
                using (Scope())
                    foreach (var request in requests)
                        _logger.LogTrace(
                            "Mandatory request '{Request}' queued.",
                            request.Description());

            _queueSignal.Release();
            return true;
        }

        if (_logger.IsEnabled(LogLevel.Warning))
            using (Scope()) _logger.LogWarning(
                accepting
                    ? "Mandatory request '{Request}' dropped (queue error)."
                    : "Mandatory request '{Request}' dropped (requests not accepted).",
                (requests.Count > 0) ? requests[0].Description() : "-");

        return false;
    }


    // Queues a single mandatory request; returns false if it was dropped
    private bool ScheduleMandatoryRequest(
        Communication_Commands command,
        byte[]? payload,
        bool waitCompletion,
        CommandTag commandTag,
        int? objectIndex = null,
        byte[]? objectSN = null)
    {
        return EnqueueMandatoryRequests(
            [CreateMandatoryRequest(command, payload, waitCompletion, commandTag, objectIndex, objectSN)]);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Event handling

    // Notify about state changes
    private void StateUpdated()
    {
        var overallState = new OverallStateDto()
        {
            IsActive = _isActive,
            IsConnected = _isConnected
        };
        StateChanged?.Invoke(this, overallState);
    }


    // Find Sensor index by its Serial Number (under _configLock)
    private int? FindSensorIndex(byte[]? serialNumber)
    {
        if (serialNumber is null) return null;

        var configs = _sensorConfigs;
        for (int i = 0; i < configs.Length; i++)
            if ((configs[i] is { } config) && config.SerialNumber.AsSpan().SequenceEqual(serialNumber))
                return i;

        return null;
    }


    // New data-exchange flags after an item count change
    private static DataExchangeFlags[] NewExchangeFlags(int count, bool requiresData = true)
    {
        var flags = new DataExchangeFlags[count];
        for (int i = 0; i < count; i++) flags[i] = new DataExchangeFlags(requiresData);
        return flags;
    }


    // Object description for log records (its configuration may not be read yet)
    private static string ObjectName(string kind, int index, string? name) =>
        $"{kind} #{index} ({name ?? "?"})";


    // Trace record of an indexed data report
    private void LogObjectReport(string report, int? index, bool accepted)
    {
        if (!_logger.IsEnabled(LogLevel.Trace)) return;

        if (accepted)
            _logger.LogTrace("Got {Report} report for #{Index}", report, index);
        else
            _logger.LogTrace("{Report} report ignored for #{Index}", report, index);
    }


    // Log State changes template
    private void LogFlagsCompareResult<TFlag>(
        string context,
        (LogLevel Level,
         IReadOnlyList<TFlag> Risen,
         IReadOnlyList<TFlag> Gone,
         IReadOnlyList<TFlag> Current) result)
            where TFlag : struct, Enum
    {
        if (!_logger.IsEnabled(result.Level)) return;
        if (result.Risen.Count == 0 && result.Gone.Count == 0) return;

        var sb = new StringBuilder();

        sb.Append(context);
        sb.AppendLine(" state changed:");

        if (result.Gone.Count > 0)
        {
            sb.AppendLine("\t[-] Cleared:");
            foreach (var flag in result.Gone)
                sb.Append("\t\t").AppendLine(flag.ToString());
        }

        if (result.Risen.Count > 0)
        {
            sb.AppendLine("\t[+] Set:");
            foreach (var flag in result.Risen)
                sb.Append("\t\t").AppendLine(flag.ToString());
        }

        if (result.Current.Count > 0)
        {
            sb.AppendLine("\t[=] Active:");
            foreach (var flag in result.Current)
                sb.Append("\t\t").AppendLine(flag.ToString());
        }
        else
        {
            sb.AppendLine("\t[=] Active: (none)");
        }

        // Use the non-generic overload to avoid boxing in the structured log call.
        // The message is fully pre-built in the StringBuilder, so there are
        // no structured logging placeholders to preserve here.
        _logger.Log(result.Level, "{Message}", sb.ToString());
    }


    // Incoming report processing (read loop).
    // Here and below, we should not use 'using (Scope())',
    // as this has already been done in the caller-Task (ReadLoop).
    private void ProcessIncomingReport(byte[] buffer, int length)
    {
        // Internal PendingRequest search (the firmware echoes the RequestID)
        static PendingRequest? CheckRequestRef(ref PendingRequest? requestRef, uint id)
        {
            PendingRequest? request = Volatile.Read(ref requestRef);
            if ((request is null) || (request.RequestId != id))
                return null;

            // The device answers: start the retry and reconnection counting anew
            // (in case we have to wait task completion).
            request.ResetAttempts(includingReconnects: true);
            return request;
        }

        //////////////////////////

        var inPacket = new CommunicationPacket(buffer);

        // Match response to one of pending requests
        PendingRequest? currentRequest =
            CheckRequestRef(ref _currentMandatoryRequest, inPacket.RequestID) ??
                CheckRequestRef(ref _currentOrdinaryRequest, inPacket.RequestID);

        // Dispatch by report type; the message for the subscribers is built under the lock
        FanControllerMessage? message = inPacket.ReportID switch
        {
            (byte)USB_HID_Reports.USB_HID_Report_System_State => ProcessSystemState(inPacket, currentRequest),
            (byte)USB_HID_Reports.USB_HID_Report_System_Config => ProcessSystemConfig(inPacket, currentRequest),
            (byte)USB_HID_Reports.USB_HID_Report_Fan_Count => ProcessFanCount(inPacket, currentRequest),
            (byte)USB_HID_Reports.USB_HID_Report_Fan_State => ProcessFanState(inPacket, currentRequest),
            (byte)USB_HID_Reports.USB_HID_Report_Fan_Config => ProcessFanConfig(inPacket, currentRequest),
            (byte)USB_HID_Reports.USB_HID_Report_Fan_Substitution_Matrix => ProcessSubstitutionMatrix(inPacket, currentRequest),
            (byte)USB_HID_Reports.USB_HID_Report_Sensor_Count => ProcessSensorCount(inPacket, currentRequest),
            (byte)USB_HID_Reports.USB_HID_Report_Sensor_State => ProcessSensorState(inPacket, currentRequest),
            (byte)USB_HID_Reports.USB_HID_Report_Sensor_Config => ProcessSensorConfig(inPacket, currentRequest),
            (byte)USB_HID_Reports.USB_HID_Report_Operation_Result => ProcessOperationResult(inPacket, currentRequest),
            _ => ProcessUnknownReport(inPacket, currentRequest)
        };

        if (message is not null)
            MessageReceived?.Invoke(this, message);

        lock (_configLock) ScheduleSnapshotRequests();
    }


    // System State
    private SystemStateDto ProcessSystemState(CommunicationPacket inPacket, PendingRequest? request)
    {
        var state = FanControllerProtocolTranslator.SystemStateHWToContract(
            inPacket.Data,
            out uint newStateFlags);

        _systemState = state;
        _inProgress = state.ConfigFlagAnyScanInProgress || state.ConfigFlagInitializationInProgress;

        // Apply a mask to avoid flooding the log
        newStateFlags &= ~FanControllerConstants.SystemStateIgnoreMask;

        // Log state changes
        if (_systemStateFlags != newStateFlags)
        {
            LogFlagsCompareResult(
                "System",
                FanControllerProtocolTranslator.SystemStateFlagsCompare(_systemStateFlags, newStateFlags));
            _systemStateFlags = newStateFlags;
        }

        lock (_configLock)
        {
            request?.SetCompleted(true);

            if (state.ActionFlagSystemConfigChanged) _systemConfigEF.CheckAndSetOutdated();

            if (state.ActionFlagFanConfigChanged)
            {
                _fanCountEF.CheckAndSetOutdated();
                foreach (var ef in _fanConfigsEF) ef.CheckAndSetOutdated();
            }

            if (state.ActionFlagSubstitutionMatrixChanged) _substitutionMatrixEF.CheckAndSetOutdated();

            if (state.ActionFlagSensorConfigChanged)
            {
                _sensorCountEF.CheckAndSetOutdated();
                foreach (var ef in _sensorConfigsEF) ef.CheckAndSetOutdated();
            }
        }

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Got System State report");

        return state;
    }


    // System Config
    private SystemConfigDto ProcessSystemConfig(CommunicationPacket inPacket, PendingRequest? request)
    {
        var config = FanControllerProtocolTranslator.SystemConfigHWToContract(inPacket.Data);

        lock (_configLock)
        {
            _systemConfig = config;
            request?.SetCompleted(true);
        }

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Got System Config report");

        return config;
    }


    // Fan Count
    private FanCountDto? ProcessFanCount(CommunicationPacket inPacket, PendingRequest? request)
    {
        int newFanCount = inPacket.Data[0];
        bool changed;

        lock (_configLock)
        {
            changed = newFanCount != FanCount;
            if (changed)
            {
                _fanCount = newFanCount;

                _fanConfigs = new FanConfigDto[newFanCount];
                Volatile.Write(ref _fanConfigsEF, NewExchangeFlags(newFanCount));

                _fanStates = new FanStateDto[newFanCount];
                Volatile.Write(ref _fanStatesEF, NewExchangeFlags(newFanCount));

                _fanStateFlags = new uint[newFanCount];

                // Data whose layout depends on the fan count is read again
                _fanSubstitutionMatrix = DtoDefaults.SubstitutionMatrix.NewMatrix();
                _substitutionMatrixEF.SetOutdated();
                foreach (var ef in _sensorConfigsEF) ef.SetOutdated();
                foreach (var ef in _sensorStatesEF) ef.SetOutdated();
            }

            request?.SetCompleted(true);
        }

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            if (changed)
                _logger.LogTrace("Got Fan Count report: {Count}", newFanCount);
            else
                _logger.LogTrace("Fan Count report ignored: {Count}", newFanCount);
        }

        return changed ? new FanCountDto { FanCount = newFanCount } : null;
    }


    // Fan # State
    private FanStateDto? ProcessFanState(CommunicationPacket inPacket, PendingRequest? request)
    {
        FanStateDto? state = null;
        int? index = request?.TargetObjectIndex;

        lock (_configLock)
        {
            if ((index is int fanIndex) && (fanIndex < FanCount))     // Check if got the acceptable report
            {
                state = FanControllerProtocolTranslator.FanStateHWToContract(
                    inPacket.Data,
                    fanIndex,
                    out uint newStateFlags);
                _fanStates[fanIndex] = state;

                // Apply a mask to avoid flooding the log
                newStateFlags &= ~FanControllerConstants.FanStateIgnoreMask;

                // Log state changes
                if (_fanStateFlags[fanIndex] != newStateFlags)
                {
                    LogFlagsCompareResult(
                        ObjectName("Fan", fanIndex, _fanConfigs[fanIndex]?.Name),
                        FanControllerProtocolTranslator.FanStateFlagsCompare(_fanStateFlags[fanIndex], newStateFlags));
                    _fanStateFlags[fanIndex] = newStateFlags;
                }
            }

            request?.SetCompleted(state is not null);
        }

        LogObjectReport("Fan State", index, state is not null);
        return state;
    }


    // Fan # Config
    private FanConfigDto? ProcessFanConfig(CommunicationPacket inPacket, PendingRequest? request)
    {
        FanConfigDto? config = null;
        int? index = request?.TargetObjectIndex;

        lock (_configLock)
        {
            if ((index is int fanIndex) && (fanIndex < FanCount))     // Check if got the acceptable report
            {
                config = FanControllerProtocolTranslator.FanConfigHWToContract(inPacket.Data, fanIndex);
                _fanConfigs[fanIndex] = config;
            }

            request?.SetCompleted(config is not null);
        }

        LogObjectReport("Fan Config", index, config is not null);
        return config;
    }


    // Substitution Matrix
    private SubstitutionMatrixDto ProcessSubstitutionMatrix(CommunicationPacket inPacket, PendingRequest? request)
    {
        SubstitutionMatrixDto matrix;

        lock (_configLock)
        {
            matrix = FanControllerProtocolTranslator.SubstitutionMatrixHWToContract(inPacket.Data, FanCount);
            _fanSubstitutionMatrix = matrix;
            request?.SetCompleted(true);
        }

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Got Substitution Matrix report");

        return matrix;
    }


    // Sensor Count
    private SensorCountDto? ProcessSensorCount(CommunicationPacket inPacket, PendingRequest? request)
    {
        int newSensorCount = inPacket.Data[0];
        bool changed;

        lock (_configLock)
        {
            changed = newSensorCount != SensorCount;
            if (changed)
            {
                _sensorCount = newSensorCount;

                _sensorConfigs = new SensorConfigDto[newSensorCount];
                Volatile.Write(ref _sensorConfigsEF, NewExchangeFlags(newSensorCount));

                Volatile.Write(ref _sensorTemperatureEF, NewExchangeFlags(newSensorCount, requiresData: false));

                _sensorStates = new SensorStateDto[newSensorCount];
                Volatile.Write(ref _sensorStatesEF, NewExchangeFlags(newSensorCount));

                _sensorStateFlags = new byte[newSensorCount];
            }

            request?.SetCompleted(true);
        }

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            if (changed)
                _logger.LogTrace("Got Sensor Count report: {Count}", newSensorCount);
            else
                _logger.LogTrace("Sensor Count report ignored: {Count}", newSensorCount);
        }

        return changed ? new SensorCountDto { SensorCount = newSensorCount } : null;
    }


    // Sensor #/######## State
    private SensorStateDto? ProcessSensorState(CommunicationPacket inPacket, PendingRequest? request)
    {
        SensorStateDto? state = null;
        int? index = request?.TargetObjectIndex;

        lock (_configLock)
        {
            if ((index is null) || (index.Value >= SensorCount))
                index = FindSensorIndex(request?.TargetObjectSN);

            if (index is int sensorIndex)
            {
                state = FanControllerProtocolTranslator.SensorStateHWToContract(
                    inPacket.Data,
                    sensorIndex,
                    FanCount,
                    out byte newStateFlags);
                _sensorStates[sensorIndex] = state;

                // Apply a mask to avoid flooding the log
                newStateFlags = (byte)(newStateFlags & ~FanControllerConstants.SensorStateIgnoreMask);

                // Log state changes
                if (_sensorStateFlags[sensorIndex] != newStateFlags)
                {
                    LogFlagsCompareResult(
                        ObjectName("Sensor", sensorIndex, _sensorConfigs[sensorIndex]?.Name),
                        FanControllerProtocolTranslator.SensorStateFlagsCompare(_sensorStateFlags[sensorIndex], newStateFlags));
                    _sensorStateFlags[sensorIndex] = newStateFlags;
                }
            }

            request?.SetCompleted(state is not null);
        }

        LogObjectReport("Sensor State", index, state is not null);
        return state;
    }


    // Sensor #/######## Config
    private SensorConfigDto? ProcessSensorConfig(CommunicationPacket inPacket, PendingRequest? request)
    {
        SensorConfigDto? config = null;
        int? index = request?.TargetObjectIndex;

        lock (_configLock)
        {
            if ((index is null) || (index.Value >= SensorCount))
                index = FindSensorIndex(request?.TargetObjectSN);

            if (index is int sensorIndex)
            {
                config = FanControllerProtocolTranslator.SensorConfigHWToContract(inPacket.Data, sensorIndex, FanCount);
                _sensorConfigs[sensorIndex] = config;
            }

            request?.SetCompleted(config is not null);
        }

        LogObjectReport("Sensor Config", index, config is not null);
        return config;
    }


    // Operation Result.
    // Only results of client commands are forwarded to the
    // subscribers (requests with a command tag).
    private OperationResultDto? ProcessOperationResult(CommunicationPacket inPacket, PendingRequest? request)
    {
        byte resultCode = inPacket.Data[0];
        var result = (Operation_Results)resultCode;

        lock (_configLock)
        {
            switch (result)
            {
                case Operation_Results.Response_Busy:
                    request?.SetRejected();
                    _isBusy = true;
                    break;

                case Operation_Results.Response_Request_Scheduled:
                case Operation_Results.Response_Request_Processing:
                    request?.SetConfirmed();
                    _isBusy = true;
                    break;

                case Operation_Results.Response_System_Not_Configured:
                    request?.SetCompleted();
                    _holdSystemConfigRequests = true;
                    break;

                case Operation_Results.Response_Fan_Not_Configured:
                    request?.SetCompleted();
                    _holdFanConfigRequests = true;
                    break;

                case Operation_Results.Response_Sensor_Not_Configured:
                    request?.SetCompleted();
                    _holdSensorConfigRequests = true;
                    break;

                default:    // OK, NACK, Done, errors: the request is finished
                    request?.SetCompleted();
                    break;
            }
        }

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace(
                "Request '{Request}' returned result: {Result}",
                request is null ? "Unknown" : request.Description(),
                result);

        // Only results of client commands are forwarded
        if ((request is null) || (request.CommandTag.Kind == CommandKind.None))
            return null;

        var operationResult = new OperationResultDto
        {
            Result = FanControllerProtocolTranslator.OperationResultHWToContract(resultCode),
            TargetObjectIndex = request.TargetObjectIndex,
            TargetObjectSN = request.TargetObjectSN,
            CommandId = request.CommandTag.Id,
            Command = request.CommandTag.Kind,
            IsFinal = request.IsLastOfCommand && request.Done
        };

        _lastOperationResult = operationResult;
        return operationResult;
    }


    // Unknown report
    private FanControllerMessage? ProcessUnknownReport(CommunicationPacket inPacket, PendingRequest? request)
    {
        if (_logger.IsEnabled(LogLevel.Warning))
            _logger.LogWarning(
                "Unknown report was received from the device. ID: {ID}",
                inPacket.ReportID);

        lock (_configLock) request?.SetCompleted();
        return null;
    }


    // Next is the algorithm for sequentially requesting data
    // to compile a snapshot of the controller's state.
    // Data is requested in a strictly defined order.
    // Appropriate checks are performed for requests
    // that cannot be executed if the device is busy.
    // (Called under _configLock after every incoming report.)
    private void ScheduleSnapshotRequests()
    {
        bool configRequestsAllowed = !_inProgress && !_isBusy;

        // System config request
        if (configRequestsAllowed && !_holdSystemConfigRequests &&
            _systemConfigEF.TryBeginRequest())
            RequestSystemConfig();

        // Fan count, fan configs and states, substitution matrix
        if (_fanCountEF.TryBeginRequest())
        {
            RequestFanCount();
        }
        else if (_fanCountEF.IsUpToDate)
        {
            for (int i = 0; i < FanCount; i++)
            {
                if (configRequestsAllowed && !_holdFanConfigRequests &&
                    _fanConfigsEF[i].TryBeginRequest())
                    RequestFanConfig((byte)i);

                if (_fanStatesEF[i].TryBeginRequest())
                    RequestFanState((byte)i);
            }

            if (configRequestsAllowed && _substitutionMatrixEF.TryBeginRequest())
                RequestSubstitutionMatrix();
        }

        // Update Host sensor readings
        if (!_inProgress && _sensorCountEF.IsUpToDate)
        {
            for (int i = 0; i < SensorCount; i++)
            {
                if (_sensorConfigsEF[i].IsUpToDate &&
                    (_sensorConfigs[i] is { Type: TSensorType.SW } sensorConfig) &&
                    _sensorTemperatureEF[i].IsRequestDue &&
                    (GetHostSensorTemperature?.Invoke(sensorConfig.SerialNumber) is double temperature) &&
                    _sensorTemperatureEF[i].TryBeginRequest())
                    RequestSensorTemperatureUpdate((byte)i, sensorConfig.SerialNumber, temperature);
            }
        }

        // Sensor count, sensor configs and states
        if (configRequestsAllowed)
        {
            if (_sensorCountEF.TryBeginRequest())
            {
                RequestSensorCount();
            }
            else if (_sensorCountEF.IsUpToDate && _fanCountEF.IsUpToDate)
            {
                for (int i = 0; i < SensorCount; i++)
                {
                    if (!_holdSensorConfigRequests && _sensorConfigsEF[i].TryBeginRequest())
                        RequestSensorConfig((byte)i);

                    if (_sensorStatesEF[i].TryBeginRequest())
                        RequestSensorState((byte)i);
                }
            }
        }
    }



    ////////////////////////////////////////////////////////////////////////////////////////
    // Public API

    /// <summary>
    /// Update HID Device (null if the device has disappeared)
    /// </summary>
    public void UpdateDevice(HidDevice? device)
    {
        lock (_deviceLock)
        {
            // Same device (HidSharp may return a new object for it)
            if (ReferenceEquals(device, _device) ||
                ((device is not null) && (_device is not null) && (device.DevicePath == _device.DevicePath)))
                return;

            _device = device;

            if (device is not null) ReadReportLengths(device);

            if (_logger.IsEnabled(LogLevel.Debug))
                using (Scope()) _logger.LogDebug("Underlying HID device updated.");
        }

        CloseDevice();
        TriggerHousekeeper();
    }


    /// <summary>
    /// Device activation
    /// </summary>
    public void Activate()
    {
        if (_disposed) return;

        _activationGate.Wait();
        try
        {
            if (_disposed) return;

            lock (_activeStateLock)
            {
                if (_isActive) return;
                _isActive = true;

                lock (_enqueueLock) { _acceptingRequests = true; }

                var cts = new CancellationTokenSource();
                _hkCTS = cts;
                _hkTask = Task.Run(() => HouseKeeperLoop(cts.Token));
            }

            if (_logger.IsEnabled(LogLevel.Information))
                using (Scope()) _logger.LogInformation("Device activated.");

            // Outside the lock: fires the event
            StateUpdated();
        }
        finally { _activationGate.Release(); }
    }


    /// <summary>
    /// Device deactivation (queued commands are sent first)
    /// </summary>
    public async Task DeactivateAsync()
    {
        if (_disposed) return;

        await _activationGate.WaitAsync();
        try
        {
            if (_disposed) return;

            lock (_activeStateLock)
            {
                if (!_isActive) return;
                _isActive = false;
            }

            await StopHousekeeperAsync();

            await FlushAndCloseAsync(
                TimeSpan.FromMilliseconds(
                    FanControllerConstants.CompletionTimeoutMs));

            if (_logger.IsEnabled(LogLevel.Information))
                using (Scope()) _logger.LogInformation("Device deactivated.");

            StateUpdated();
        }
        finally { _activationGate.Release(); }
    }


    /// <summary>
    /// Closes the enqueue gate, then enqueues the shutdown notification
    /// bypassing the gate (once, until UndoPrepareForShutdown).
    /// <para>
    /// Called directly — not through the normal scheduling path.
    /// </para>
    /// </summary>
    public void PrepareForShutdown()
    {
        if (!IsActive) return;

        lock (_enqueueLock)
        {
            if (!_acceptingRequests) return;    // Already prepared (or shutting down)
            _acceptingRequests = false;
        }

        var request = new PendingRequest(
            Communication_Commands.Command_Prepare_To_Host_Shutdown,
            null,
            _outputReportLength,
            waitConfirmation: true,
            waitCompletion: true,
            index: null,
            sn: null,
            isMandatory: true);

        if (_mandatoryRequestQueue.Writer.TryWrite(request))
        {
            if (_logger.IsEnabled(LogLevel.Information))
                using (Scope()) _logger.LogInformation(
                    "PrepareForShutdown command queued. Further command acceptance is blocked.");
            _queueSignal.Release();
        }
        else if (_logger.IsEnabled(LogLevel.Warning))
            using (Scope()) _logger.LogWarning(
                "PrepareForShutdown command could not be queued. Further command acceptance is blocked.");
    }


    /// <summary>
    /// Reopens the enqueue gate — used if shutdown was aborted.
    /// </summary>
    public void UndoPrepareForShutdown()
    {
        if (!IsActive) return;

        lock (_enqueueLock)
        {
            _acceptingRequests = true;
        }

        if (_logger.IsEnabled(LogLevel.Information))
            using (Scope()) _logger.LogInformation("Command acceptance is unblocked.");
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Snapshot requests (the scheduler only; under _configLock, except the System State)

    private void RequestSystemState() =>
        ScheduleOrdinaryRequest(
            Communication_Commands.Command_Get_System_State,
            null,
            true,
            exchangeFlags: _systemStateEF);

    private void RequestSystemConfig() =>
        ScheduleOrdinaryRequest(
            Communication_Commands.Command_Get_System_Config,
            null,
            true,
            exchangeFlags: _systemConfigEF);

    private void RequestFanCount() =>
        ScheduleOrdinaryRequest(
            Communication_Commands.Command_Get_Fan_Count,
            null,
            true,
            exchangeFlags: _fanCountEF);

    private void RequestFanConfig(byte index) =>
        ScheduleOrdinaryRequest(
            Communication_Commands.Command_Get_Fan_N_Config,
            [index],
            true,
            index,
            exchangeFlags: _fanConfigsEF[index]);

    private void RequestFanState(byte index) =>
        ScheduleOrdinaryRequest(
            Communication_Commands.Command_Get_Fan_N_State,
            [index],
            true,
            index,
            exchangeFlags: _fanStatesEF[index]);

    private void RequestSubstitutionMatrix() =>
        ScheduleOrdinaryRequest(
            Communication_Commands.Command_Get_Fan_Substitution_Matrix,
            null,
            true,
            exchangeFlags: _substitutionMatrixEF);

    private void RequestSensorCount() =>
        ScheduleOrdinaryRequest(
            Communication_Commands.Command_Get_Sensor_Count,
            null,
            true,
            exchangeFlags: _sensorCountEF);

    private void RequestSensorConfig(byte index) =>
        ScheduleOrdinaryRequest(
            Communication_Commands.Command_Get_Sensor_N_Config,
            [index],
            true,
            index,
            exchangeFlags: _sensorConfigsEF[index]);

    private void RequestSensorState(byte index) =>
        ScheduleOrdinaryRequest(
            Communication_Commands.Command_Get_Sensor_N_State,
            [index],
            true,
            index,
            exchangeFlags: _sensorStatesEF[index]);

    // Software sensor temperature. Unlike other commands, it is queued
    // as a standard request so that it is not blocked by long-running commands.
    private void RequestSensorTemperatureUpdate(byte index, byte[] serialNumber, double temperature) =>
        ScheduleOrdinaryRequest(
            Communication_Commands.Command_Update_Sensor_SN_Temperature,
            FanControllerProtocolTranslator.CreateHWUpdateSensorTemperatureRequest(serialNumber, temperature),
            true,
            index,
            serialNumber,
            exchangeFlags: _sensorTemperatureEF[index]);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Commands
    // Each returns false if the command could not be queued
    // (the controller is inactive or shutting down).
    // The tag labels the device's answers (operation results) for the client.

    /// <summary>
    /// Set System Config
    /// </summary>
    public bool SendSetSystemConfigCommand(SystemConfigDto config, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Set_System_Config,
            FanControllerProtocolTranslator.SystemConfigContractToHW(config),
            true,
            tag);


    /// <summary>
    /// Set System Behavior
    /// </summary>
    public bool SendSetSystemBehaviorCommand(SystemBehaviorDto behavior, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Set_Behavior_Config,
            FanControllerProtocolTranslator.SystemBehaviorContractToHW(behavior),
            true,
            tag);


    /// <summary>
    /// Factory Reset
    /// </summary>
    public bool SendFactoryResetCommand(CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Factory_Reset,
            null,
            false,
            tag);


    /// <summary>
    /// Reboot MCU
    /// </summary>
    public bool SendRebootCommand(CommandTag tag)
    {
        // Unlike other commands, let's queue this one
        // as a standard request so that it is not
        // blocked by other long-running commands.
        return ScheduleOrdinaryRequest(
            Communication_Commands.Command_Reboot,
            null,
            true,
            commandTag: tag);
    }


    /// <summary>
    /// Preparing to host shutdown (a client command;
    /// the service's own shutdown uses PrepareForShutdown)
    /// </summary>
    public bool SendPrepareToHostShutdownCommand(CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Prepare_To_Host_Shutdown,
            null,
            true,
            tag);


    // FANS BLOCK

    /// <summary>
    /// Rescan Fan Hardware
    /// </summary>
    public bool SendRescanFanHardwareCommand(byte index, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Rescan_Fan_Hardware,
            [index],
            true,
            tag,
            index);


    /// <summary>
    /// Rescan All Fans Hardware
    /// </summary>
    public bool SendRescanAllFansHardwareCommand(CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Rescan_Fan_Hardware,
            [0xFF],
            true,
            tag);


    /// <summary>
    /// Rescan Fan Model
    /// </summary>
    public bool SendRescanFanModelCommand(byte index, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Rescan_Fan_Model,
            [index],
            true,
            tag,
            index);


    /// <summary>
    /// Rescan All Fans Model
    /// </summary>
    public bool SendRescanAllFansModelCommand(CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Rescan_Fan_Model,
            [0xFF],
            true,
            tag);


    /// <summary>
    /// Set Fan Config
    /// </summary>
    public bool SendSetFanConfigCommand(byte index, FanConfigDto config, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Set_Fan_N_Config,
            FanControllerProtocolTranslator.FanConfigContractToHW(config, index),
            true,
            tag,
            index);


    /// <summary>
    /// Reset Fan
    /// </summary>
    public bool SendResetFanCommand(byte index, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Reset_Fan_N,
            [index],
            true,
            tag,
            index);


    /// <summary>
    /// Set Fan Control Mode
    /// </summary>
    public bool SendSetFanControlModeCommand(byte index, FanControlMode mode, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Set_Fan_N_Control_Mode,
            [index, FanControllerProtocolTranslator.ControlModeContractToHW(mode)],
            true,
            tag,
            index);


    /// <summary>
    /// Set Fan Operation Mode
    /// </summary>
    public bool SendSetFanOperationModeCommand(byte index, FanOperationMode mode, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Set_Fan_N_Operation_Mode,
            [index, FanControllerProtocolTranslator.OperationModeContractToHW(mode)],
            true,
            tag,
            index);


    /// <summary>
    /// Set Fan Control Basis
    /// </summary>
    public bool SendSetFanControlBasisCommand(byte index, double value, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Set_Fan_N_Control_Basis,
            FanControllerProtocolTranslator.CreateHWSetControlRequest(value, index),
            true,
            tag,
            index);


    /// <summary>
    /// Set Fan Control Value
    /// </summary>
    public bool SendSetFanControlValueCommand(byte index, double value, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Set_Fan_N_Control_Value,
            FanControllerProtocolTranslator.CreateHWSetControlRequest(value, index),
            true,
            tag,
            index);


    // SUBSTITUTION MATRIX BLOCK

    /// <summary>
    /// Set Substitution Matrix
    /// </summary>
    /// <exception cref="ArgumentException">The matrix is smaller than FanCount × FanCount.</exception>
    public bool SendSetSubstitutionMatrixCommand(SubstitutionMatrixDto matrix, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Set_Fan_Substitution_Matrix,
            FanControllerProtocolTranslator.SubstitutionMatrixContractToHW(matrix, FanCount),
            true,
            tag);


    // SENSORS BLOCK

    /// <summary>
    /// Rescan Sensors
    /// </summary>
    public bool SendRescanSensorsCommand(CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Rescan_Sensors,
            null,
            true,
            tag);


    /// <summary>
    /// Reset Sensor
    /// </summary>
    public bool SendResetSensorCommand(byte[] serialNumber, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Reset_Sensor_SN,
            serialNumber,
            true,
            tag,
            null,
            serialNumber);


    /// <summary>
    /// Set Sensor Config
    /// </summary>
    /// <exception cref="ArgumentException">The configuration doesn't match the fan count.</exception>
    public bool SendSetSensorConfigCommand(SensorConfigDto config, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Set_Sensor_Config,
            FanControllerProtocolTranslator.SensorConfigContractToHW(config, FanCount),
            true,
            tag,
            null,
            config.SerialNumber);


    /// <summary>
    /// Add Sensor
    /// </summary>
    /// <exception cref="ArgumentException">The configuration doesn't match the fan count.</exception>
    public bool SendAddSensorCommand(SensorConfigDto config, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Add_Sensor,
            FanControllerProtocolTranslator.SensorConfigContractToHW(config, FanCount),
            true,
            tag,
            null,
            config.SerialNumber);


    /// <summary>
    /// Delete Sensor
    /// </summary>
    public bool SendDeleteSensorCommand(byte[] serialNumber, CommandTag tag) =>
        ScheduleMandatoryRequest(
            Communication_Commands.Command_Remove_Sensor_SN,
            serialNumber,
            true,
            tag,
            null,
            serialNumber);


    /// <summary>
    /// Delete Sensors: rewrites the configuration without these sensors
    /// (false if the stored configuration is incomplete)
    /// </summary>
    public bool SendDeleteSensorsCommand(byte[][] serialNumbers, CommandTag tag)
    {
        var remainingSensors = new List<SensorConfigDto>();

        lock (_configLock)
        {
            if (!_sensorCountEF.IsUpToDate) return false;

            for (int i = 0; i < SensorCount; i++)
            {
                SensorConfigDto? sensor = _sensorConfigs[i];

                if ((sensor is not null) &&
                    serialNumbers.Any(sn => sn.AsSpan().SequenceEqual(sensor.SerialNumber)))
                    continue;   // deleted

                if ((sensor is null) || !_sensorConfigsEF[i].IsUpToDate)
                    return false;

                remainingSensors.Add(sensor);
            }
        }

        return SendSetPartialConfigCommand(null, null, null, remainingSensors.ToArray(), tag);
    }


    /// <summary>
    /// Set Partial Device Config: the parts not given are taken from
    /// the stored configuration (false if it is incomplete)
    /// </summary>
    /// <exception cref="ArgumentException">The configuration doesn't match the device or contains invalid items.</exception>
    public bool SendSetPartialConfigCommand(SystemConfigDto? systemConfig,
                                            FanConfigDto[]? fanConfigs,
                                            SubstitutionMatrixDto? matrix,
                                            SensorConfigDto[]? sensorConfigs,
                                            CommandTag tag)
    {
        DeviceFullConfig fullConfig;

        lock (_configLock)
        {
            systemConfig ??= _systemConfigEF.IsUpToDate ? _systemConfig : null;
            fanConfigs ??= AllUpToDate(_fanCountEF, _fanConfigsEF) ? _fanConfigs.ToArray() : null;
            matrix ??= _substitutionMatrixEF.IsUpToDate ? _fanSubstitutionMatrix : null;
            sensorConfigs ??= AllUpToDate(_sensorCountEF, _sensorConfigsEF) ? _sensorConfigs.ToArray() : null;

            if ((systemConfig is null) ||
                (fanConfigs is null) ||
                (matrix is null) ||
                (sensorConfigs is null))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    using (Scope()) _logger.LogWarning(
                        "The stored configuration is incomplete; the configuration update is not sent.");
                return false;
            }

            fullConfig = new DeviceFullConfig()
            {
                SystemConfig = systemConfig,
                Fans = fanConfigs,
                SubstitutionMatrix = matrix,
                Sensors = sensorConfigs
            };
        }

        return SendSetFullConfigCommand(fullConfig, tag);
    }


    /// <summary>
    /// Set Full Device Config. The whole sequence is queued at once, or nothing
    /// (an invalid item throws before anything is queued). All its requests carry
    /// the tag; the last one (Reload Config) finishes the command.
    /// </summary>
    /// <exception cref="ArgumentException">The configuration doesn't match the device or contains invalid items.</exception>
    public bool SendSetFullConfigCommand(DeviceFullConfig config, CommandTag tag)
    {
        int fanCount = FanCount;

        if (config.Fans.Length != fanCount)
            throw new ArgumentException(
                $"The configuration must contain {fanCount} fan(s).", nameof(config));

        var requests = new List<PendingRequest>
        {
            // Start new Config Page
            CreateMandatoryRequest(
                Communication_Commands.Command_Start_Clean_Config_Page,
                null,
                true,
                tag,
                isLastOfCommand: false),

            // Write System Config
            CreateMandatoryRequest(
                Communication_Commands.Command_Set_System_Config,
                FanControllerProtocolTranslator.SystemConfigContractToHW(config.SystemConfig),
                true,
                tag,
                isLastOfCommand: false)
        };

        // Write Fan configs
        for (byte i = 0; i < config.Fans.Length; i++)
            requests.Add(CreateMandatoryRequest(
                Communication_Commands.Command_Set_Fan_N_Config,
                FanControllerProtocolTranslator.FanConfigContractToHW(config.Fans[i], i),
                true,
                tag,
                i,
                isLastOfCommand: false));

        // Write Substitution Matrix
        requests.Add(CreateMandatoryRequest(
            Communication_Commands.Command_Set_Fan_Substitution_Matrix,
            FanControllerProtocolTranslator.SubstitutionMatrixContractToHW(config.SubstitutionMatrix, fanCount),
            true,
            tag,
            isLastOfCommand: false));

        // Write Sensor Configs
        foreach (var sensor in config.Sensors)
            requests.Add(CreateMandatoryRequest(
                Communication_Commands.Command_Set_Sensor_Config,
                FanControllerProtocolTranslator.SensorConfigContractToHW(sensor, fanCount),
                true,
                tag,
                null,
                sensor.SerialNumber,
                isLastOfCommand: false));

        // Finalize Config Page
        requests.Add(CreateMandatoryRequest(
            Communication_Commands.Command_Finalize_Config_Page,
            null,
            true,
            tag));  // isLastOfCommand: false


        // We no longer need this step; the configuration is now
        // loaded automatically after Command_Finalize_Config_Page.
        /*
        // Reload Config (finishes the command)
        requests.Add(CreateMandatoryRequest(
            Communication_Commands.Command_Reload_Config,
            null,
            true,
            tag));
        */

        return EnqueueMandatoryRequests(requests);
    }

}

// END-OF-FILE
