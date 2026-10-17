////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// FanControllerManager.cs
////////////////////////////////////////////////////////////////////////////////////////

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FanController.Contracts;
using Grpc.Core;
using HidSharp;
using HidSharp.Utility;
using ProtoBuf.Grpc;


namespace FanController.Service.Core;

/// <summary>
/// Combined Interface: IDeviceService + Host shutdown logic Interface
/// </summary>
public interface IManagedDeviceService : IDeviceService, IAsyncDisposable
{
    void PrepareToHostShutdown(string deviceId);
    void PrepareAllDevicesForShutdown();
    void UndoPrepareToHostShutdown(string deviceId);
}


/// <summary>
/// Manages one or more physical fan controllers connected to the host.
/// </summary>
public class FanControllerManager : IManagedDeviceService, IHostedService, IAsyncDisposable, IDisposable
{
    // Some constants
    private static class FanControllerManagerConstants
    {
        // STMicroelectronics VID with the PID of its Custom HID examples (shared by
        // many boards), hence the string checks below.
        // TODO: Replace with a dedicated ID (e.g. pid.codes, VID 0x1209).
        public const int VendorId = 0x0483;                         // 1155
        public const int ProductId = 0x5750;                        // 22352
        public const string ManufacturerName = "Dmitry Muravyev";   // USBD_MANUFACTURER_STRING
        public const string ProductName = "Fan Controller";         // USBD_PRODUCT_STRING_FS
        public const int MinSupportedMajorVersion = 1;              // Supported firmware generation
        public const int MaxSupportedMajorVersion = 1;              // --//--
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Private properties

    // Runtime management
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<FanControllerManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private int _disposed = 0;
    private readonly object _lock = new();
    private readonly object _refreshLock = new();               // serializes RefreshDevices

    // Not disposed: late gRPC calls still read their tokens
    private readonly CancellationTokenSource _serviceCts = new();
    private readonly CancellationTokenSource _streamCts = new();

    // Disappeared devices are kept (unavailable), so they can reconnect
    private readonly Dictionary<string, FanController> _controllers = new();
    private readonly HostSensorManager _hostSensorManager;

    // Channels for broadcasting changes to all watchers
    private readonly List<Channel<DeviceSummaryListResponse>> _deviceListWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceOperationResult>>> _operationResultWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceOverallState>>> _overallStateWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceSystemState>>> _systemStateWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceSystemConfig>>> _systemConfigWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceFanCount>>> _fanCountWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceFanState>>> _fanStateWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceFanConfig>>> _fanConfigWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceFanSubstitutionMatrix>>> _substitutionMatrixWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceSensorCount>>> _sensorCountWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceSensorState>>> _sensorStateWatchers = new();
    private readonly Dictionary<string, List<Channel<DeviceSensorConfig>>> _sensorConfigWatchers = new();


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction / Disposal

    /// <summary>
    /// Creates the manager: scans the host sensors and the connected
    /// fan controllers, and starts monitoring the USB device list.
    /// </summary>
    public FanControllerManager(
        IHostApplicationLifetime lifetime,
        ILogger<FanControllerManager> logger,
        ILoggerFactory loggerFactory)
    {
        _lifetime = lifetime;
        _lifetime.ApplicationStopping.Register(OnApplicationStopping);
        _logger = logger;
        _loggerFactory = loggerFactory;
        _hostSensorManager = new HostSensorManager(true, _loggerFactory);

        // Debug
#if DEBUG
        HidSharpDiagnostics.EnableTracing = true;
        HidSharpDiagnostics.PerformStrictChecks = true;
#else
        HidSharpDiagnostics.EnableTracing = false;
        HidSharpDiagnostics.PerformStrictChecks = false;
#endif

        // Start USB enumeration (HidSharp DeviceList monitoring)
        DeviceList.Local.Changed += OnDeviceListChanged;

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Manager object created.");

        RefreshDevices();
    }


    // Async disposal
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // No new controllers from here on: stop the USB monitoring
        // and wait for a refresh that may still be running
        DeviceList.Local.Changed -= OnDeviceListChanged;
        lock (_refreshLock) { }

        // Signal all active streams and pending operations to stop.
        // _streamCts may already be cancelled via OnApplicationStopping(), so
        // Cancel here covers the case where DisposeAsync is called directly
        // (e.g. in tests or manual disposal) without going through host shutdown.
        if (!_streamCts.IsCancellationRequested)
            _streamCts.Cancel();

        // Now cancel everything including pending commands
        if (!_serviceCts.IsCancellationRequested)
            _serviceCts.Cancel();

        List<FanController> controllers;
        lock (_lock)
        {
            controllers = _controllers.Values.ToList();
            _controllers.Clear();
        }

        // Parallel: total time ≈ slowest device, not the sum.
        await Task.WhenAll(controllers.Select(c =>
        {
            c.StateChanged -= OnControllerStateChanged;
            c.MessageReceived -= OnControllerMessage;
            return c.DisposeAsync().AsTask();
        }));

        lock (_lock)
        {
            CompleteWatchers(_deviceListWatchers);
            CompleteWatchers(_operationResultWatchers);
            CompleteWatchers(_overallStateWatchers);
            CompleteWatchers(_systemStateWatchers);
            CompleteWatchers(_systemConfigWatchers);
            CompleteWatchers(_fanCountWatchers);
            CompleteWatchers(_fanStateWatchers);
            CompleteWatchers(_fanConfigWatchers);
            CompleteWatchers(_substitutionMatrixWatchers);
            CompleteWatchers(_sensorCountWatchers);
            CompleteWatchers(_sensorStateWatchers);
            CompleteWatchers(_sensorConfigWatchers);
        }

        _hostSensorManager.Dispose();

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Manager object disposed.");

        GC.SuppressFinalize(this);
    }


    // Synchronous fallback for IDisposable consumers.
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();


    // Ends the watchers' streams (under _lock)
    private static void CompleteWatchers<T>(List<Channel<T>> watchers)
    {
        foreach (var ch in watchers)
            ch.Writer.TryComplete();
        watchers.Clear();
    }

    private static void CompleteWatchers<T>(Dictionary<string, List<Channel<T>>> watcherDict)
    {
        foreach (var list in watcherDict.Values)
            foreach (var ch in list)
                ch.Writer.TryComplete();
        watcherDict.Clear();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // IHostedService
    // The host creates the manager at startup and stops it on shutdown.
    // The active gRPC streams are already cancelled by OnApplicationStopping(),
    // so the gRPC server can stop without waiting for them.

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;


    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Service stopping.");

        // Let the controllers send their remaining critical requests, then release them
        PrepareAllDevicesForShutdown();
        await DisposeAsync();

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Service stopped.");
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Callbacks

    // Cancel streams before shutting the service down
    private void OnApplicationStopping()
    {
        if (_logger.IsEnabled(LogLevel.Trace)) 
            _logger.LogTrace("The manager will be stopped.");

        // Cancel all active streams immediately
        // This causes ReadAllAsync(effectiveCt) to throw ->
        // streams exit -> Kestrel sees no active streams ->
        // drains instantly.
        _streamCts.Cancel();
    }


    // Update USB device list
    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        if (_logger.IsEnabled(LogLevel.Trace)) 
            _logger.LogTrace("USB device list changed.");

        RefreshDevices();
    }


    // FanController Overall State changed event handling
    private void OnControllerStateChanged(object? sender, OverallStateDto data)
    {
        if (sender is not FanController ctrl) return;

        BroadcastDeviceProperty(
            ctrl.SN,
            new DeviceOverallState
            {
                Id = ctrl.SN,
                OverallState = data
            },
            _overallStateWatchers);
    }


    // FanController message handling (got some data)
    private void OnControllerMessage(object? sender, FanControllerMessage msg)
    {
        if (sender is not FanController ctrl) return;

        if (_logger.IsEnabled(LogLevel.Trace)) 
            _logger.LogTrace("Got message from device {Id}: {Type}.",
                ctrl.SN,
                msg.GetType().Name);

        switch (msg)
        {
            case OperationResultDto opResult:

                BroadcastDeviceProperty(
                    ctrl.SN,
                    new DeviceOperationResult
                    {
                        Id = ctrl.SN,
                        OperationResult = opResult
                    },
                    _operationResultWatchers);
                break;


            case SystemStateDto sysState:

                BroadcastDeviceProperty(
                    ctrl.SN,
                    new DeviceSystemState
                    {
                        Id = ctrl.SN,
                        SystemState = sysState
                    },
                    _systemStateWatchers);
                break;


            case SystemConfigDto sysConfig:

                BroadcastDeviceProperty(
                    ctrl.SN,
                    new DeviceSystemConfig
                    {
                        Id = ctrl.SN,
                        SystemConfig = sysConfig
                    },
                    _systemConfigWatchers);
                break;


            case FanCountDto fanCount:

                BroadcastDeviceProperty(
                    ctrl.SN,
                    new DeviceFanCount
                    {
                        Id = ctrl.SN,
                        FanCount = fanCount.FanCount
                    },
                    _fanCountWatchers);
                break;


            case FanStateDto fanState:

                BroadcastDeviceProperty(
                    ctrl.SN,
                    new DeviceFanState
                    {
                        Id = ctrl.SN,
                        FanState = fanState
                    },
                    _fanStateWatchers);
                break;


            case FanConfigDto fanConfig:

                BroadcastDeviceProperty(
                    ctrl.SN,
                    new DeviceFanConfig
                    {
                        Id = ctrl.SN,
                        FanCount = ctrl.FanCount,
                        FanConfig = fanConfig
                    },
                    _fanConfigWatchers);

                break;


            case SubstitutionMatrixDto matrix:

                BroadcastDeviceProperty(
                    ctrl.SN,
                    new DeviceFanSubstitutionMatrix
                    {
                        Id = ctrl.SN,
                        FanCount = ctrl.FanCount,
                        Matrix = matrix
                    },
                    _substitutionMatrixWatchers);
                break;


            case SensorCountDto sensorCount:

                BroadcastDeviceProperty(
                    ctrl.SN,
                    new DeviceSensorCount
                    {
                        Id = ctrl.SN,
                        SensorCount = sensorCount.SensorCount
                    },
                    _sensorCountWatchers);
                break;


            case SensorStateDto sensorState:
                BroadcastDeviceProperty(
                    ctrl.SN,
                    new DeviceSensorState
                    {
                        Id = ctrl.SN,
                        SensorState = sensorState
                    },
                    _sensorStateWatchers);

                break;


            case SensorConfigDto sensorConfig:
                BroadcastDeviceProperty(
                    ctrl.SN,
                    new DeviceSensorConfig
                    {
                        Id = ctrl.SN,
                        SensorCount = ctrl.SensorCount,
                        SensorConfig = sensorConfig
                    },
                    _sensorConfigWatchers);

                break;


            default:
                break;
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Device list management

    // Check if the device is a supported fan controller
    private bool IsTargetDevice(HidDevice device)
    {
        string manufacturer;
        string product;
        try
        {
            manufacturer = device.GetManufacturer();
            product = device.GetProductName();
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug(
                    "Cannot read USB device strings. Skipped. Exception: {Exception}",
                    ex.Message);
            return false;
        }

        if (!string.Equals(manufacturer, FanControllerManagerConstants.ManufacturerName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(product, FanControllerManagerConstants.ProductName, StringComparison.OrdinalIgnoreCase))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug(
                    "Not a Fan Controller: '{Manufacturer}' / '{Product}'. Skipped.",
                    manufacturer,
                    product);
            return false;
        }

        Version version = device.ReleaseNumber;
        if ((version.Major < FanControllerManagerConstants.MinSupportedMajorVersion) ||
            (version.Major > FanControllerManagerConstants.MaxSupportedMajorVersion))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(
                    "Unsupported Fan Controller version found: {Version}.",
                    version.ToString());
            return false;
        }

        return true;
    }


    // USB device enumeration
    // Controllers are updated and activated outside _lock: closing a device waits
    // for its read loop, which may be delivering a message (and needs _lock).
    private void RefreshDevices()
    {
        lock (_refreshLock)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            List<HidDevice> hidDevices;
            try
            {
                hidDevices = DeviceList.Local
                    .GetHidDevices(FanControllerManagerConstants.VendorId, FanControllerManagerConstants.ProductId)
                    .Where(IsTargetDevice)
                    .ToList();
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "USB device enumeration error: {Exception}",
                        ex.Message);
                return;
            }

            var updates = new List<(FanController Controller, HidDevice? Device)>();
            var created = new List<FanController>();

            lock (_lock)
            {
                if (_disposed != 0) return;

                var foundIds = new HashSet<string>();

                foreach (var hid in hidDevices)
                {
                    string id;
                    try
                    {
                        id = hid.GetSerialNumber();
                    }
                    catch (Exception ex)
                    {
                        if (_logger.IsEnabled(LogLevel.Error))
                            _logger.LogError(
                                "Cannot read USB device serial number. Skipped. Exception: {Exception}",
                                ex.Message);
                        continue;
                    }

                    if (!foundIds.Add(id))
                    {
                        if (_logger.IsEnabled(LogLevel.Warning))
                            _logger.LogWarning(
                                "Another device with serial number {Id} found. Skipped.",
                                id);
                        continue;
                    }

                    if (_controllers.TryGetValue(id, out var existing))
                    {
                        // Update underlying HID device reference
                        updates.Add((existing, hid));
                    }
                    else if (CreateController(hid) is { } ctrl)
                    {
                        // New device discovered
                        _controllers[id] = ctrl;
                        created.Add(ctrl);
                    }
                }

                // Mark disappeared devices (kept, so they can reconnect)
                foreach (var (id, ctrl) in _controllers)
                    if (!foundIds.Contains(id))
                        updates.Add((ctrl, null));
            }

            foreach (var (ctrl, device) in updates)
                ctrl.UpdateDevice(device);

            // Auto-activate every discovered controller
            foreach (var ctrl in created)
                ctrl.Activate();
        }

        BroadcastDeviceList();
    }


    // New controller object (null if the device can't be read)
    private FanController? CreateController(HidDevice hid)
    {
        try
        {
            var ctrl = new FanController(hid, _loggerFactory);
            ctrl.StateChanged += OnControllerStateChanged;
            ctrl.MessageReceived += OnControllerMessage;
            ctrl.GetHostSensorTemperature = id => _hostSensorManager.GetTemperatureForSensorID(id);
            return ctrl;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError(
                    "Cannot create a controller for the USB device. Skipped. Exception: {Exception}",
                    ex.Message);
            return null;
        }
    }


    // Build actual device list
    // TODO: Replace IReadOnlyList with array
    private List<DeviceSummary> BuildDeviceList()
    {
        // Must be called under _lock
        return _controllers.Values.Select(c => new DeviceSummary
        {
            Id = c.SN,
            Name = c.Name,
            Version = c.Release.ToString(),
            IsAvailable = c.IsAvailable
        }).ToList();
    }


    // Send to all subscribers
    private void BroadcastDeviceList()
    {
        DeviceSummaryListResponse snapshot;
        List<Channel<DeviceSummaryListResponse>> watchers;

        lock (_lock)
        {
            snapshot = new DeviceSummaryListResponse
            {
                Devices = BuildDeviceList()
            };
            watchers = new List<Channel<DeviceSummaryListResponse>>(_deviceListWatchers);
        }

        foreach (var ch in watchers)
            ch.Writer.TryWrite(snapshot);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Subscriber-dependent optimization

    // Called inside _lock whenever a watcher for fan state is added/removed.
    // Reads _fanStateWatchers and updates ctrl.HasFanStateSubscribers
    // so that FanController could determine the minimum required
    // data request rate.
    private void UpdateFanStateSubscriberCount(string deviceId)
    {
        int count = GetWatcherCount(_fanStateWatchers, deviceId);
        if (_controllers.TryGetValue(deviceId, out var ctrl))
            ctrl.HasFanStateSubscribers = count > 0;
    }


    // Called inside _lock whenever a watcher for sensor state is added/removed.
    private void UpdateSensorStateSubscriberCount(string deviceId)
    {
        int count = GetWatcherCount(_sensorStateWatchers, deviceId);
        if (_controllers.TryGetValue(deviceId, out var ctrl))
            ctrl.HasSensorStateSubscribers = count > 0;
    }


    // Helper
    private static int GetWatcherCount<T>(
        Dictionary<string, List<Channel<T>>> dict,
        string deviceId) =>
        dict.TryGetValue(deviceId, out var list) ? list.Count : 0;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Generic query/streams helpers (IDeviceService)

    // Getting the device property.
    // For the moment the 'Func factory' is always synchronous.
    private Task<TState?> GetDevicePropertyAsync<TState>(
        string deviceId,
        Func<FanController, TState?> factory,
        CancellationToken ct = default) where TState : class
    {
        // Early exit if already cancelled
        // Combined check — either the client or the service can cancel
        // For non-async methods we have to return Task.FromCanceled()
        if (_serviceCts.IsCancellationRequested || ct.IsCancellationRequested)
            return Task.FromCanceled<TState?>(
                _serviceCts.IsCancellationRequested ? _serviceCts.Token : ct);

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Host requested the property of type: {Type} from the device: {Id}.",
                typeof(TState).Name,
                deviceId);

        FanController? ctrl;
        lock (_lock)
        {
            _controllers.TryGetValue(deviceId, out ctrl);
        }

        // Outside _lock: the factory reads the controller's own snapshot
        return Task.FromResult(ctrl is null ? null : factory(ctrl));
    }


    // For future use. Do not delete!
    private async Task<TState?> GetDevicePropertyWithAwaitAsync<TState>(
        string deviceId,
        Func<FanController, Task<TState?>> factory,
        CancellationToken ct = default) where TState : class
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct, _serviceCts.Token);

        // For async methods we have to use ThrowIfCancellationRequested()
        ct.ThrowIfCancellationRequested();
        _serviceCts.Token.ThrowIfCancellationRequested();

        FanController? ctrl;
        lock (_lock)
        {
            _controllers.TryGetValue(deviceId, out ctrl);
        }

        if (ctrl is null) return null;

        return await factory(ctrl).WaitAsync(linked.Token);
    }


    // Single unified streams helper
    private async IAsyncEnumerable<TState> WatchDevicePropertyAsync<TState>(
        string deviceId,
        Dictionary<string, List<Channel<TState>>> watcherDict,
        Func<FanController, IEnumerable<TState>> stateFactory,
        [EnumeratorCancellation] CancellationToken ct,
        Action<string>? subscriberCountChanged = null)
    {
        // For async methods we have to use ThrowIfCancellationRequested()
        _streamCts.Token.ThrowIfCancellationRequested();
        ct.ThrowIfCancellationRequested();

        // Link caller token with service lifetime
        // "using" is for managing the lifetime of linked cts
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct, _streamCts.Token);
        var effectiveCt = linked.Token;

        var channel = Channel.CreateBounded<TState>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

        lock (_lock)
        {
            if (!watcherDict.TryGetValue(deviceId, out var list))
            {
                list = new List<Channel<TState>>();
                watcherDict[deviceId] = list;
            }
            list.Add(channel);

            if (_logger.IsEnabled(LogLevel.Debug)) 
                _logger.LogDebug(
                    "Watcher added for deviceID: {Id}, type={Type}, total={Count}.",
                    deviceId,
                    typeof(TState).Name,
                    list.Count);

            // Push ALL current states immediately if controller exists
            if (_controllers.TryGetValue(deviceId, out var ctrl))
            {
                foreach (var state in stateFactory(ctrl))
                    channel.Writer.TryWrite(state);
            }

            // Invoke callback to update subscriber count
            subscriberCountChanged?.Invoke(deviceId);
        }

        try
        {
            await foreach (var item in channel.Reader
                .ReadAllAsync(effectiveCt)   // service lifetime + client token
                .ConfigureAwait(false))      // no .WithCancellation — effectiveCt is already correct
            {
                yield return item;
            }
        }
        finally
        {
            lock (_lock)
            {
                if (watcherDict.TryGetValue(deviceId, out var list))
                {
                    list.Remove(channel);

                    if (_logger.IsEnabled(LogLevel.Debug)) 
                        _logger.LogDebug(
                            "Watcher removed for deviceID: {Id}, type={Type}, remaining={Count}.", 
                            deviceId, 
                            typeof(TState).Name,
                            list.Count);

                    if (list.Count == 0)
                        watcherDict.Remove(deviceId);
                }

                // ... remove channel ...
                subscriberCountChanged?.Invoke(deviceId);
            }
        }
    }


    // Generic broadcast
    private void BroadcastDeviceProperty<TState>(
        string deviceId,
        TState state,
        Dictionary<string, List<Channel<TState>>> watcherDict)
    {
        List<Channel<TState>>? watchers;

        lock (_lock)
        {
            watcherDict.TryGetValue(deviceId, out watchers);
            if (watchers is not null)
                watchers = new List<Channel<TState>>(watchers);
        }

        if (watchers is not null)
        {
            if (_logger.IsEnabled(LogLevel.Trace)) 
                _logger.LogTrace(
                    "Property of type: {Type} from the device: {Id} broadcasted to host(s).",
                    typeof(TState).Name,
                    deviceId);

            foreach (var ch in watchers)
                ch.Writer.TryWrite(state);
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // IDeviceService implementation
    ////////////////////////////////////////////////////////////////////////////////////////
    // IDeviceService - Queries

    public Task<ServiceInfo> GetServiceInfoAsync(
        EmptyRequest request,
        CallContext context = default)
    {
        // For async methods we have to use ThrowIfCancellationRequested()
        _serviceCts.Token.ThrowIfCancellationRequested();
        context.CancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ServiceInfo
        {
            ApiVersion = ContractInfo.ApiVersion,
            ServiceVersion = typeof(FanControllerManager).Assembly.GetName().Version?.ToString(3) ?? "-"
        });
    }


    public Task<DeviceSummaryListResponse> GetDeviceListAsync(
        EmptyRequest request,
        CallContext context = default)
    {
        // For async methods we have to use ThrowIfCancellationRequested()
        _serviceCts.Token.ThrowIfCancellationRequested();
        context.CancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult(
                new DeviceSummaryListResponse
                {
                    Devices = BuildDeviceList()
                });
        }
    }


    public Task<HostDeviceListResponse> GetHostDeviceListAsync(
        EmptyRequest request,
        CallContext context = default)
    {
        // For async methods we have to use ThrowIfCancellationRequested()
        _serviceCts.Token.ThrowIfCancellationRequested();
        context.CancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new HostDeviceListResponse
            {
                Devices = _hostSensorManager.GetHostDevices().ToList()
            });
    }


    public Task<HostSensorListResponse> GetHostSensorListAsync(
        EmptyRequest request,
        CallContext context = default)
    {
        // For async methods we have to use ThrowIfCancellationRequested()
        _serviceCts.Token.ThrowIfCancellationRequested();
        context.CancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new HostSensorListResponse
            {
                Sensors = _hostSensorManager.GetHostSensorStates().ToList()
            });
    }


    public async Task<Optional<DeviceOperationResult>> GetDeviceOperationResultAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl => new DeviceOperationResult
        {
            Id = request.DeviceId,
            OperationResult = ctrl.LastOperationResult
        }, context.CancellationToken);

        // Here and below T? implicitly converts to Optional<T>
        return result is null ? Optional<DeviceOperationResult>.Empty() : result;
    }


    public async Task<Optional<DeviceOverallState>> GetDeviceOverallStateAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl => new DeviceOverallState
        {
            Id = request.DeviceId,
            OverallState = new OverallStateDto
            {
                IsActive = ctrl.IsActive,
                IsConnected = ctrl.IsConnected
            }
        }, context.CancellationToken);

        return result is null ? Optional<DeviceOverallState>.Empty() : result;
    }


    public async Task<Optional<DeviceSystemState>> GetDeviceSystemStateAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl => new DeviceSystemState
        {
            Id = request.DeviceId,
            SystemState = ctrl.SystemState
        }, context.CancellationToken);

        return result is null ? Optional<DeviceSystemState>.Empty() : result;
    }


    public async Task<Optional<DeviceSystemConfig>> GetDeviceSystemConfigAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl => new DeviceSystemConfig
        {
            Id = request.DeviceId,
            SystemConfig = ctrl.SystemConfig
        }, context.CancellationToken);

        return result is null ? Optional<DeviceSystemConfig>.Empty() : result;
    }


    public async Task<Optional<DeviceFanCount>> GetDeviceFanCountAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl => new DeviceFanCount
        {
            Id = request.DeviceId,
            FanCount = ctrl.FanCount
        }, context.CancellationToken);

        return result is null ? Optional<DeviceFanCount>.Empty() : result;
    }


    public async Task<Optional<DeviceFanState>> GetDeviceFanStateAsync(
        DeviceFanRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl =>
        {
            var state = ctrl.FanState(request.FanIndex);
            return (state is not null)
                ? new DeviceFanState
                {
                    Id = request.DeviceId,
                    FanState = state
                }
                : null;
        }, context.CancellationToken);

        return result is null ? Optional<DeviceFanState>.Empty() : result;
    }


    public async Task<Optional<DeviceFanConfig>> GetDeviceFanConfigAsync(
        DeviceFanRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl =>
        {
            var config = ctrl.FanConfig(request.FanIndex);
            return (config is not null)
                ? new DeviceFanConfig
                {
                    Id = request.DeviceId,
                    FanCount = ctrl.FanCount,
                    FanConfig = config
                }
                : null;
        }, context.CancellationToken);

        return result is null ? Optional<DeviceFanConfig>.Empty() : result;
    }


    public async Task<Optional<DeviceFanSubstitutionMatrix>> GetDeviceFanSubstitutionMatrixAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl => new DeviceFanSubstitutionMatrix
        {
            Id = request.DeviceId,
            FanCount = ctrl.FanCount,
            Matrix = ctrl.FanSubstitutionMatrix
        }, context.CancellationToken);

        return result is null ? Optional<DeviceFanSubstitutionMatrix>.Empty() : result;
    }


    public async Task<Optional<DeviceSensorCount>> GetDeviceSensorCountAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl => new DeviceSensorCount
        {
            Id = request.DeviceId,
            SensorCount = ctrl.SensorCount
        }, context.CancellationToken);

        return result is null ? Optional<DeviceSensorCount>.Empty() : result;
    }


    public async Task<Optional<DeviceSensorState>> GetDeviceSensorStateAsync(
        DeviceSensorRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl =>
        {
            var state = ctrl.SensorState(request.SensorIndex);
            return (state is not null)
                ? new DeviceSensorState
                {
                    Id = request.DeviceId,
                    SensorState = state
                }
                : null;
        }, context.CancellationToken);

        return result is null ? Optional<DeviceSensorState>.Empty() : result;
    }


    public async Task<Optional<DeviceSensorConfig>> GetDeviceSensorConfigAsync(
        DeviceSensorRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl =>
        {
            var config = ctrl.SensorConfig(request.SensorIndex);
            return (config is not null)
                ? new DeviceSensorConfig
                {
                    Id = request.DeviceId,
                    SensorCount = ctrl.SensorCount,
                    SensorConfig = config
                }
                : null;
        }, context.CancellationToken);

        return result is null ? Optional<DeviceSensorConfig>.Empty() : result;
    }


    public async Task<Optional<DeviceFullConfig>> GetDeviceFullConfigAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        var result = await GetDevicePropertyAsync(request.DeviceId, ctrl =>
        {
            return ctrl.FullConfig();
        }, context.CancellationToken);

        return result is null ? Optional<DeviceFullConfig>.Empty() : result;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // IDeviceService - Streams

    public async IAsyncEnumerable<DeviceSummaryListResponse> WatchDeviceListAsync(
        EmptyRequest request,
        CallContext context = default)
    {
        // For async methods we have to use ThrowIfCancellationRequested()
        _streamCts.Token.ThrowIfCancellationRequested();
        context.CancellationToken.ThrowIfCancellationRequested();

        // Link caller token with service lifetime (cancelled on ApplicationStopping)
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken, _streamCts.Token);

        var channel = Channel.CreateBounded<DeviceSummaryListResponse>(
            new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest });

        DeviceSummaryListResponse initialSnapshot;
        lock (_lock)
        {
            _deviceListWatchers.Add(channel);
            initialSnapshot = new DeviceSummaryListResponse
            {
                Devices = BuildDeviceList()
            };
        }

        try
        {
            // Yield current state immediately
            yield return initialSnapshot;

            await foreach (var snapshot in channel.Reader
                .ReadAllAsync(linked.Token)
                .ConfigureAwait(false))
            {
                yield return snapshot;
            }
        }
        finally
        {
            lock (_lock)
            {
                _deviceListWatchers.Remove(channel);
            }
        }
    }


    public IAsyncEnumerable<DeviceOverallState> WatchDeviceOverallStateAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _overallStateWatchers,
            ctrl => [new DeviceOverallState
            {
                Id = request.DeviceId,
                OverallState = new OverallStateDto
                {
                    IsActive = ctrl.IsActive,
                    IsConnected = ctrl.IsConnected
                }
            }], context.CancellationToken);
    }


    public IAsyncEnumerable<DeviceOperationResult> WatchDeviceOperationResultAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _operationResultWatchers,
            ctrl => [new DeviceOperationResult
            {
                Id = request.DeviceId,
                OperationResult = ctrl.LastOperationResult
            }], context.CancellationToken);
    }


    public IAsyncEnumerable<DeviceSystemState> WatchDeviceSystemStateAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _systemStateWatchers,
            ctrl => [new DeviceSystemState
            {
                Id = request.DeviceId,
                SystemState = ctrl.SystemState
            }], context.CancellationToken);
    }


    public IAsyncEnumerable<DeviceSystemConfig> WatchDeviceSystemConfigAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _systemConfigWatchers,
            ctrl => [new DeviceSystemConfig
            {
                Id = request.DeviceId,
                SystemConfig = ctrl.SystemConfig
            }], context.CancellationToken);
    }


    public IAsyncEnumerable<DeviceFanCount> WatchDeviceFanCountAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _fanCountWatchers,
            ctrl => [new DeviceFanCount
        {
            Id = request.DeviceId,
            FanCount = ctrl.FanCount
        }], context.CancellationToken);
    }


    public IAsyncEnumerable<DeviceFanState> WatchDeviceFanStateAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _fanStateWatchers,
            ctrl =>
            {
                var results = new List<DeviceFanState>();
                for (int i = 0; i < ctrl.FanCount; i++)
                {
                    var state = ctrl.FanState(i);
                    if (state is not null)
                    {
                        results.Add(new DeviceFanState
                        {
                            Id = request.DeviceId,
                            FanState = state
                        });
                    }
                }

                return results;
            },
            context.CancellationToken,
            UpdateFanStateSubscriberCount);
    }


    public IAsyncEnumerable<DeviceFanConfig> WatchDeviceFanConfigAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _fanConfigWatchers,
            ctrl =>
            {
                var results = new List<DeviceFanConfig>();
                for (int i = 0; i < ctrl.FanCount; i++)
                {
                    var config = ctrl.FanConfig(i);
                    if (config is not null)
                    {
                        results.Add(new DeviceFanConfig
                        {
                            Id = request.DeviceId,
                            FanCount = ctrl.FanCount,
                            FanConfig = config
                        });
                    }
                }
                return results;
            }, context.CancellationToken);
    }


    public IAsyncEnumerable<DeviceFanSubstitutionMatrix> WatchDeviceFanSubstitutionMatrixAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _substitutionMatrixWatchers,
            ctrl => [new DeviceFanSubstitutionMatrix
            {
                Id = request.DeviceId,
                FanCount = ctrl.FanCount,
                Matrix = ctrl.FanSubstitutionMatrix
            }], context.CancellationToken);
    }



    public IAsyncEnumerable<DeviceSensorCount> WatchDeviceSensorCountAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _sensorCountWatchers,
            ctrl => [new DeviceSensorCount
        {
            Id = request.DeviceId,
            SensorCount = ctrl.SensorCount
        }], context.CancellationToken);
    }


    public IAsyncEnumerable<DeviceSensorState> WatchDeviceSensorStateAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _sensorStateWatchers,
            ctrl =>
            {
                var results = new List<DeviceSensorState>();
                for (int i = 0; i < ctrl.SensorCount; i++)
                {
                    var state = ctrl.SensorState(i);
                    if (state is not null)
                    {
                        results.Add(new DeviceSensorState
                        {
                            Id = request.DeviceId,
                            SensorState = state
                        });
                    }
                }

                return results;
            },
            context.CancellationToken,
            UpdateSensorStateSubscriberCount);
    }


    public IAsyncEnumerable<DeviceSensorConfig> WatchDeviceSensorConfigAsync(
        DeviceIdRequest request,
        CallContext context = default)
    {
        return WatchDevicePropertyAsync(
            request.DeviceId,
            _sensorConfigWatchers,
            ctrl =>
            {
                var results = new List<DeviceSensorConfig>();
                for (int i = 0; i < ctrl.SensorCount; i++)
                {
                    var config = ctrl.SensorConfig(i);
                    if (config is not null)
                    {
                        results.Add(new DeviceSensorConfig
                        {
                            Id = request.DeviceId,
                            SensorCount = ctrl.SensorCount,
                            SensorConfig = config
                        });
                    }
                }
                return results;
            }, context.CancellationToken);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // IDeviceService - Commands

    public async Task ExecuteCommandAsync(
        ExecuteCommandRequest request,
        CallContext context = default)
    {
        // Check both client cancellation and service lifetime
        var ct = context.CancellationToken;
        ct.ThrowIfCancellationRequested();
        _serviceCts.Token.ThrowIfCancellationRequested();

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace(
                "Got command for a device {Id}: {Type}.",
                request.DeviceId,
                request.Command.GetType().Name);

        FanController? ctrl;
        lock (_lock)
        {
            _controllers.TryGetValue(request.DeviceId, out ctrl);
        }

        if (ctrl is null)
            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Device '{request.DeviceId}' not found."));

        var tag = new CommandTag(request.CommandId, request.Command.Kind);

        bool queued;
        try
        {
            queued = await ExecuteDeviceCommandAsync(ctrl, request.Command, tag);
        }
        catch (ArgumentException ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(
                    "Invalid command {Type} for a device {Id}: {Message}",
                    request.Command.GetType().Name,
                    request.DeviceId,
                    ex.Message);

            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        if (!queued)
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "The command was not accepted: the device is inactive or shutting down, " +
                "or its configuration has not been read yet."));
    }


    // Applies a command to the controller; false if the controller did not accept it
    /// <exception cref="ArgumentException">Invalid command arguments.</exception>
    private async Task<bool> ExecuteDeviceCommandAsync(FanController ctrl, DeviceCommand command, CommandTag tag)
    {
        switch (command)
        {
            // System commands

            case SetActiveCommand cmd:
                if (cmd.Active)
                    ctrl.Activate();
                else
                    await ctrl.DeactivateAsync();

                BroadcastDeviceList();
                return true;

            case FactoryResetCommand:
                return ctrl.SendFactoryResetCommand(tag);

            case RebootCommand:
                return ctrl.SendRebootCommand(tag);

            case PrepareToHostShutdownCommand:
                return ctrl.SendPrepareToHostShutdownCommand(tag);

            case SetSystemBehaviorCommand cmd:
                return ctrl.SendSetSystemBehaviorCommand(cmd.Behavior, tag);

            case SetSystemConfigurationCommand cmd:
                return ctrl.SendSetSystemConfigCommand(cmd.Config, tag);

            case SetFullConfigurationCommand cmd:
                return ctrl.SendSetFullConfigCommand(cmd.Config, tag);

            case SetPartialConfigurationCommand cmd:
                return ctrl.SendSetPartialConfigCommand(
                    cmd.SystemConfig,
                    cmd.FanConfigs,
                    cmd.Matrix,
                    cmd.SensorConfigs,
                    tag);

            // Fan commands

            case RescanFanHardwareCommand cmd:
                return ctrl.SendRescanFanHardwareCommand(FanIndex(ctrl, cmd.FanIndex), tag);

            case RescanAllFanHardwareCommand:
                return ctrl.SendRescanAllFansHardwareCommand(tag);

            case RescanFanModelCommand cmd:
                return ctrl.SendRescanFanModelCommand(FanIndex(ctrl, cmd.FanIndex), tag);

            case RescanAllFanModelsCommand:
                return ctrl.SendRescanAllFansModelCommand(tag);

            case SetFanConfigurationCommand cmd:
                return ctrl.SendSetFanConfigCommand(FanIndex(ctrl, cmd.FanIndex), cmd.Config, tag);

            case ResetFanCommand cmd:
                return ctrl.SendResetFanCommand(FanIndex(ctrl, cmd.FanIndex), tag);

            case SetFanControlModeCommand cmd:
                return ctrl.SendSetFanControlModeCommand(FanIndex(ctrl, cmd.FanIndex), cmd.Mode, tag);

            case SetFanOperationModeCommand cmd:
                return ctrl.SendSetFanOperationModeCommand(FanIndex(ctrl, cmd.FanIndex), cmd.Mode, tag);

            case SetFanControlBasisCommand cmd:
                return ctrl.SendSetFanControlBasisCommand(FanIndex(ctrl, cmd.FanIndex), cmd.Basis, tag);

            case SetFanControlValueCommand cmd:
                return ctrl.SendSetFanControlValueCommand(FanIndex(ctrl, cmd.FanIndex), cmd.Value, tag);

            case SetFanSubstitutionMatrixCommand cmd:
                return ctrl.SendSetSubstitutionMatrixCommand(cmd.Matrix, tag);

            // Sensor commands

            case RescanSensorsCommand:
                return ctrl.SendRescanSensorsCommand(tag);

            case ResetSensorCommand cmd:
                return ctrl.SendResetSensorCommand(cmd.SerialNumber, tag);

            case SetSensorConfigurationCommand cmd:
                return ctrl.SendSetSensorConfigCommand(cmd.Config, tag);

            case AddSensorCommand cmd:
                return ctrl.SendAddSensorCommand(cmd.Config, tag);

            case DeleteSensorCommand cmd:
                return ctrl.SendDeleteSensorCommand(cmd.SerialNumber, tag);

            case DeleteSensorsCommand cmd:
                return ctrl.SendDeleteSensorsCommand(cmd.SerialNumbers, tag);

            default:
                throw new RpcException(new Status(
                    StatusCode.Unimplemented,
                    $"Unknown command: {command.GetType().Name}"));
        }
    }


    // Fan index of a command, checked against the device's fan count
    private static byte FanIndex(FanController ctrl, int index) =>
        ((index >= 0) && (index < ctrl.FanCount))
            ? (byte)index
            : throw new ArgumentOutOfRangeException(
                nameof(index), index, $"The device has {ctrl.FanCount} fan(s).");


    ////////////////////////////////////////////////////////////////////////////////////////
    // IManagedDeviceService implementation

    /// <summary>
    /// Prepare the device with specified ID to the service shutdown
    /// </summary>
    /// <param name="deviceId"></param>
    public void PrepareToHostShutdown(string deviceId)
    {
        FanController? ctrl;
        lock (_lock)
        {
            _controllers.TryGetValue(deviceId, out ctrl);
        }

        if (ctrl is null)
        {
            if (_logger.IsEnabled(LogLevel.Warning)) 
                _logger.LogWarning(
                    "PrepareToHostShutdown: device '{Id}' not found.", 
                    deviceId);
            return;
        }

        ctrl.PrepareForShutdown();
    }


    /// <summary>
    /// Prepare all available devices to the service shutdown
    /// </summary>
    public void PrepareAllDevicesForShutdown()
    {
        List<FanController> controllers;
        lock (_lock)
        {
            controllers = _controllers.Values.ToList();
        }

        if (controllers.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Information)) 
                _logger.LogInformation("PrepareAllDevicesForShutdown: no devices.");
            return;
        }

        if (_logger.IsEnabled(LogLevel.Information)) 
            _logger.LogInformation(
                "Preparing {Count} device(s) for host shutdown.", 
                controllers.Count);

        foreach (var ctrl in controllers)
        {
            ctrl.PrepareForShutdown();
        }
    }


    /// <summary>
    /// Reopens the request gate on the specified device.
    /// Call if shutdown was aborted and normal operation should resume.
    /// </summary>
    /// <param name="deviceId"></param>
    public void UndoPrepareToHostShutdown(string deviceId)
    {
        FanController? ctrl;
        lock (_lock)
        {
            _controllers.TryGetValue(deviceId, out ctrl);
        }

        ctrl?.UndoPrepareForShutdown();
    }
}

// END-OF-FILE
