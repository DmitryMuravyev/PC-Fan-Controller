////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// ServiceItem.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Client;
using FanController.Contracts;
using FanController.Settings;


namespace FanController.Services;

/// <summary>
/// Device service with a disposal token (cancelled when the service is disposed).
/// </summary>
public interface IManagedDeviceService : IDeviceService, IAsyncDisposable
{
    CancellationToken DisposalToken { get; }

    /// <summary>
    /// Sends a command and waits for its final operation result
    /// (null if none arrives within the timeout).
    /// <paramref name="onAccepted"/> is called once the service has accepted the command.
    /// </summary>
    Task<OperationResultDto?> ExecuteCommandAndWaitAsync(
        string deviceId,
        DeviceCommand command,
        TimeSpan timeout,
        CancellationToken ct,
        Action? onAccepted = null);
}


// Event args

public class ServiceReachabilityEventArgs : EventArgs
{
    public bool Reachable { get; }
    public ServiceReachabilityEventArgs(bool reachable) => Reachable = reachable;
}

public class DeviceEventArgs : EventArgs
{
    public RemoteDevice Device { get; }
    public DeviceEventArgs(RemoteDevice device) => Device = device;
}


/// <summary>
/// One configured daemon host and all controllers discovered on it.
/// Owns the gRPC channel to the host: streams reconnect automatically,
/// commands and queries are retried a limited number of times.
/// All events are raised on the thread the item was created on (the UI thread).
/// </summary>
public sealed class ServiceItem : IManagedDeviceService
{
    // Call attempts (first try included) on connection failures
    private const int CallMaxAttempts = 3;
    private static readonly TimeSpan DefaultReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ServiceInfoTimeout = TimeSpan.FromSeconds(10);

    // Configuration
    private volatile HostConfig _config;
    public HostConfig Config => _config;
    private readonly TimeSpan _reconnectDelay;

    // Thread / context the events are raised on
    private readonly SynchronizationContext? _syncContext;
    private readonly int _syncThreadId;

    // gRPC channel (guarded by _channelLock)
    private GrpcChannel? _channel;
    private IDeviceService? _client;
    private long _channelGeneration;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private sealed record ClientLease(IDeviceService Client, long Generation);

    private volatile bool _disposed;

    // Host watcher (device list stream)
    private CancellationTokenSource? _watcherCts;
    private Task? _watcherTask;

    // Cancelled on disposal; the token stays readable afterwards
    private readonly CancellationTokenSource _disposalCts = new();
    public CancellationToken DisposalToken { get; }

    // Controllers discovered on this host, by device ID
    private readonly Dictionary<string, RemoteDevice> _devices = new();
    private readonly object _devicesGate = new();

    // Snapshot of the discovered controllers
    public IReadOnlyList<RemoteDevice> Devices
    {
        get
        {
            lock (_devicesGate)
            {
                return _devices.Values.ToList();
            }
        }
    }

    // A stream to the host is delivering data
    private int _isReachable;   // 0 / 1, Interlocked
    public bool IsReachable => Volatile.Read(ref _isReachable) != 0;

    // The host's service uses this client's contract version
    // (assumed until the first check; checked on every new channel)
    private volatile bool _isCompatible = true;
    public bool IsCompatible => _isCompatible;

    // Last service information received from the host
    private volatile ServiceInfo? _serviceInfo;
    public ServiceInfo? ServiceInfo => _serviceInfo;


    // Events
    public event EventHandler? ServiceConfigChanged;

    // Compatibility or service information changed
    public event EventHandler? ServiceInfoChanged;
    public event EventHandler<ServiceReachabilityEventArgs>? ServiceReachabilityChanged;
    public event EventHandler<DeviceEventArgs>? DeviceAdded;
    public event EventHandler<DeviceEventArgs>? DeviceUpdated;
    public event EventHandler<DeviceEventArgs>? DeviceRemoved;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    public ServiceItem(HostConfig config, TimeSpan reconnectDelay)
    {
        _config = config;
        _reconnectDelay = (reconnectDelay > TimeSpan.Zero) ? reconnectDelay : DefaultReconnectDelay;

        _syncContext = SynchronizationContext.Current;
        _syncThreadId = Environment.CurrentManagedThreadId;

        DisposalToken = _disposalCts.Token;
    }


    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Cancels the watcher and all consumer CTS linked to DisposalToken
        _disposalCts.Cancel();

        await StopWatcherAsync();
        await InvalidateChannelAsync(removeDevices: true);

        // The locks and _disposalCts are not disposed on purpose: consumer streams
        // may still be finishing and use them, and DisposalToken must stay readable.
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Configuration

    /// <summary>
    /// Applies new connection parameters. An endpoint change drops the channel and
    /// the discovered devices; the streams then reconnect to the new endpoint.
    /// </summary>
    public async Task UpdateConfigAsync(HostConfig newConfig)
    {
        HostConfig oldConfig = _config;

        // Set before the invalidation, so the reconnecting streams use the new endpoint
        _config = newConfig;
        Post(() => ServiceConfigChanged?.Invoke(this, EventArgs.Empty));

        if (!oldConfig.HasEndpoint(newConfig.Host, newConfig.Port))
            await InvalidateChannelAsync(removeDevices: true);
    }


    /// <summary>
    /// Drops the current channel: the streams reconnect, the devices are kept.
    /// </summary>
    public Task ReconnectAsync() => InvalidateChannelAsync();


    ////////////////////////////////////////////////////////////////////////////////////////
    // Event and state helpers

    // Runs the action on the captured context: inline on its thread, posted otherwise
    private void Post(Action action)
    {
        if ((_syncContext is null) || (Environment.CurrentManagedThreadId == _syncThreadId))
            action();
        else
            _syncContext.Post(static state => ((Action)state!).Invoke(), action);
    }


    private void SetReachability(bool reachable)
    {
        int value = reachable ? 1 : 0;
        if (Interlocked.Exchange(ref _isReachable, value) != value)
            Post(() => ServiceReachabilityChanged?.Invoke(this, new ServiceReachabilityEventArgs(reachable)));
    }


    private void SetCompatibility(bool compatible, ServiceInfo? info)
    {
        bool becameIncompatible = _isCompatible && !compatible;
        bool changed = (_isCompatible != compatible);
        _isCompatible = compatible;

        if ((info is not null) && (info != _serviceInfo))
        {
            _serviceInfo = info;
            changed = true;
        }

        if (!changed) return;

        // An incompatible host serves no data: its devices are unavailable
        if (becameIncompatible)
        {
            List<RemoteDevice> unavailable;
            lock (_devicesGate)
            {
                unavailable = _devices.Values.Where(device => device.MarkUnavailable()).ToList();
            }

            foreach (var device in unavailable)
                Post(() => DeviceUpdated?.Invoke(this, new DeviceEventArgs(device)));
        }

        Post(() => ServiceInfoChanged?.Invoke(this, EventArgs.Empty));
    }


    /// <summary>
    /// Merges a device list snapshot. Devices absent from it stay known
    /// and are marked unavailable. Returns the new and the changed devices.
    /// </summary>
    private (List<RemoteDevice> Added, List<RemoteDevice> Updated) MergeDevices(
        DeviceSummaryListResponse incoming)
    {
        var added = new List<RemoteDevice>();
        var updated = new List<RemoteDevice>();
        var incomingIds = new HashSet<string>();

        lock (_devicesGate)
        {
            foreach (var summary in incoming.Devices)
            {
                // Duplicate entries are ignored
                if (!incomingIds.Add(summary.Id)) continue;

                if (_devices.TryGetValue(summary.Id, out var device))
                {
                    if (device.Update(summary)) updated.Add(device);
                }
                else
                {
                    device = new RemoteDevice(this, summary);
                    _devices[summary.Id] = device;
                    added.Add(device);
                }
            }

            foreach (var device in _devices.Values)
            {
                if (!incomingIds.Contains(device.Id) && device.MarkUnavailable())
                    updated.Add(device);
            }
        }

        return (added, updated);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Channel management

    private async Task<ClientLease> GetClientAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _channelLock.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_client is not null) return new ClientLease(_client, _channelGeneration);

            var socketsHandler = new SocketsHttpHandler
            {
                // HTTP/2 keep-alive PINGs detect a dead connection, also while streams are active
                KeepAlivePingDelay = TimeSpan.FromSeconds(10),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,

                EnableMultipleHttp2Connections = false,
                ConnectTimeout = TimeSpan.FromSeconds(10),
            };

            // No gRPC retry policy: retries are handled by StreamAsync / ExecuteAsync.
            // Cancellation through the token is reported as OperationCanceledException
            // (not as RpcException with StatusCode.Cancelled).
            var options = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                HttpHandler = socketsHandler,
                ThrowOperationCanceledOnCancellation = true
            };

            HostConfig config = _config;
            var channel = GrpcChannel.ForAddress(BuildAddress(config.Host, config.Port), options);
            var client = channel.CreateGrpcService<IDeviceService>();

            // A new channel is used only if the host's contract version matches
            try
            {
                await CheckCompatibilityAsync(client, ct);
            }
            catch
            {
                channel.Dispose();
                throw;
            }

            _channel = channel;
            _client = client;
            _channelGeneration++;
            return new ClientLease(_client, _channelGeneration);
        }
        finally { _channelLock.Release(); }
    }


    // Checks the contract version of the host's service.
    // Throws FailedPrecondition if it doesn't match (connection errors are rethrown as they are).
    private async Task CheckCompatibilityAsync(IDeviceService client, CancellationToken ct)
    {
        ServiceInfo info;
        try
        {
            info = await client.GetServiceInfoAsync(
                new EmptyRequest(),
                new CallContext(new CallOptions(
                    deadline: DateTime.UtcNow.Add(ServiceInfoTimeout),
                    cancellationToken: ct)));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
        {
            // A service older than the version check
            SetCompatibility(false, null);
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "The host runs an older, incompatible service version."));
        }

        bool compatible = (info.ApiVersion == ContractInfo.ApiVersion);
        SetCompatibility(compatible, info);

        if (!compatible)
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"Incompatible service API version {info.ApiVersion} (expected {ContractInfo.ApiVersion}), " +
                $"service {info.ServiceVersion}."));
    }


    // UriBuilder also puts IPv6 literals in brackets ("::1" → "[::1]")
    private static Uri BuildAddress(string host, int port) =>
        new UriBuilder(Uri.UriSchemeHttp, host.Trim(), port).Uri;


    /// <summary>
    /// Drops the current channel (the next call creates a new one).
    /// With <paramref name="failedGeneration"/> the channel is dropped only if it is
    /// still that generation (another stream may have replaced it already).
    /// </summary>
    private async Task InvalidateChannelAsync(
        bool removeDevices = false,
        long? failedGeneration = null,
        CancellationToken ct = default)
    {
        GrpcChannel? channelToDispose = null;
        List<RemoteDevice>? removedDevices = null;

        await _channelLock.WaitAsync(ct);
        try
        {
            if ((failedGeneration is not null) &&
                ((_channel is null) || (_channelGeneration != failedGeneration))) return;

            channelToDispose = _channel;
            _channel = null;
            _client = null;

            Debug.WriteLine($"[ServiceItem] Invalidated channel generation {_channelGeneration}.");

            if (removeDevices)
            {
                lock (_devicesGate)
                {
                    removedDevices = _devices.Values.ToList();
                    _devices.Clear();
                }
            }
        }
        finally { _channelLock.Release(); }

        // Potentially slow work outside the lock

        if (removedDevices is not null)
        {
            foreach (var device in removedDevices)
                Post(() => DeviceRemoved?.Invoke(this, new DeviceEventArgs(device)));
        }

        if (channelToDispose is not null)
        {
            try
            {
                await channelToDispose.ShutdownAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceItem] Channel shutdown failed: {ex.Message}");
            }
            finally
            {
                channelToDispose.Dispose();
            }

            SetReachability(false);
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Host watcher

    public void StartWatcher(CancellationToken parentCt)
    {
        if ((_watcherCts is not null) || (_watcherTask is not null)) return;

        _watcherCts = CancellationTokenSource.CreateLinkedTokenSource(parentCt, DisposalToken);
        _watcherTask = WatchHostAsync(_watcherCts.Token);
    }


    // Cancels the watcher and waits for it to finish
    public async Task StopWatcherAsync()
    {
        if ((_watcherCts is null) || (_watcherTask is null)) return;

        _watcherCts.Cancel();

        try
        {
            await _watcherTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            _watcherCts.Dispose();
            _watcherCts = null;
            _watcherTask = null;
        }
    }


    // Watches the host's device list (StreamAsync handles the reconnection)
    private async Task WatchHostAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var list in WatchDeviceListAsync(
                new EmptyRequest(),
                new CallContext(new CallOptions(cancellationToken: ct)))
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                var (added, updated) = MergeDevices(list);

                foreach (var device in added)
                    Post(() => DeviceAdded?.Invoke(this, new DeviceEventArgs(device)));

                foreach (var device in updated)
                    Post(() => DeviceUpdated?.Invoke(this, new DeviceEventArgs(device)));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the application exits or the host is removed
        }
        catch (Exception ex)
        {
            // Should only represent an unexpected programming / runtime error
            Debug.WriteLine($"[ServiceItem] Watcher for '{Config.Name}' stopped: {ex}");
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Resilience helpers

    /// <summary>
    /// Runs a unary call with up to <see cref="CallMaxAttempts"/> attempts, then rethrows.
    /// Commands are retried only on Unavailable (the request can't have reached the host);
    /// other errors may mean it was already executed.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(
        Func<IDeviceService, CallContext, Task<T>> call,
        bool isCommand,
        CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var lease = await GetClientAsync(ct);
            try
            {
                return await call(lease.Client, new CallContext(new CallOptions(cancellationToken: ct)));
            }
            catch (RpcException ex)
                when ((attempt < CallMaxAttempts) &&
                      !ct.IsCancellationRequested &&
                      (isCommand ? (ex.StatusCode == StatusCode.Unavailable) : IsConnectivityError(ex)))
            {
                Debug.WriteLine(
                    $"[ServiceItem] Call failed (attempt {attempt}/{CallMaxAttempts}): " +
                    $"{ex.StatusCode}: {ex.Status.Detail}");

                await DelayAsync(_reconnectDelay, ct);
            }
        }
    }


    private Task<T> QueryAsync<T>(
        Func<IDeviceService, CallContext, Task<T>> call,
        CancellationToken ct) =>
        ExecuteAsync(call, isCommand: false, ct);


    /// <summary>
    /// Wraps a server stream with automatic reconnection. Only connection errors
    /// mark the host unreachable and reset the channel; a completed stream or a
    /// call-level error is retried later on the same channel.
    /// </summary>
    private async IAsyncEnumerable<T> StreamAsync<T>(
        Func<IDeviceService, CallContext, IAsyncEnumerable<T>> createStream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Phase 1: connect (no yield here, so try/catch is allowed)

            IAsyncEnumerator<T>? enumerator = null;
            long channelGeneration = 0;

            try
            {
                var lease = await GetClientAsync(ct);
                var context = new CallContext(new CallOptions(cancellationToken: ct));
                enumerator = createStream(lease.Client, context).GetAsyncEnumerator(ct);
                channelGeneration = lease.Generation;
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (ObjectDisposedException)
            {
                // The service has been disposed
                yield break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceItem] Stream '{typeof(T).Name}' setup failed: {ex.Message}");
            }

            if (enumerator is null)
            {
                await DelayAsync(_reconnectDelay, ct);
                continue;
            }

            // Phase 2: stream loop (yield return is outside every try/catch)

            while (!ct.IsCancellationRequested)
            {
                bool hasNext = false;
                bool reconnect = false;
                bool cancelled = false;

                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (Exception ex)
                    when (ct.IsCancellationRequested &&
                          (ex is OperationCanceledException or RpcException { StatusCode: StatusCode.Cancelled }))
                {
                    // Our own cancellation (device switch, shutdown): not a connection problem
                    cancelled = true;
                }
                catch (OperationCanceledException)
                {
                    // The call was cancelled by a channel shutdown (reconnect / endpoint change)
                    reconnect = true;
                }
                catch (RpcException ex) when (IsConnectivityError(ex))
                {
                    Debug.WriteLine($"[ServiceItem] Stream '{typeof(T).Name}' disconnected: {ex.StatusCode}");
                    reconnect = true;

                    // Reset the dead channel (independently of this stream's cancellation),
                    // so GetClientAsync creates a fresh one
                    await InvalidateChannelAsync(false, channelGeneration, CancellationToken.None);
                    SetReachability(false);
                }
                catch (Exception ex)
                {
                    // Call-level error (e.g. the requested device is gone): retry later
                    Debug.WriteLine($"[ServiceItem] Stream '{typeof(T).Name}' error: {ex.Message}");
                    reconnect = true;
                }

                if (cancelled)
                {
                    await DisposeEnumeratorAsync(enumerator);
                    yield break;
                }

                if (reconnect || !hasNext) break;

                SetReachability(true);

                yield return enumerator.Current;
            }

            // Phase 3: cleanup and retry

            await DisposeEnumeratorAsync(enumerator);

            if (ct.IsCancellationRequested) yield break;

            await DelayAsync(_reconnectDelay, ct);
        }
    }


    private static async Task DisposeEnumeratorAsync<T>(IAsyncEnumerator<T> enumerator)
    {
        try
        {
            await enumerator.DisposeAsync();
        }
        catch
        {
            // The connection may already be broken
        }
    }


    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown / device switch
        }
    }


    private static bool IsConnectivityError(RpcException ex) =>
        ex.StatusCode is
            StatusCode.Unavailable or
            StatusCode.Internal or
            StatusCode.Unknown or
            StatusCode.DeadlineExceeded or
            StatusCode.Cancelled;


    ////////////////////////////////////////////////////////////////////////////////////////
    // IDeviceService: queries

    public Task<ServiceInfo> GetServiceInfoAsync(
        EmptyRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetServiceInfoAsync(request, ctx), context.CancellationToken);

    public Task<DeviceSummaryListResponse> GetDeviceListAsync(
        EmptyRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceListAsync(request, ctx), context.CancellationToken);

    public Task<HostDeviceListResponse> GetHostDeviceListAsync(
        EmptyRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetHostDeviceListAsync(request, ctx), context.CancellationToken);

    public Task<HostSensorListResponse> GetHostSensorListAsync(
        EmptyRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetHostSensorListAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceOverallState>> GetDeviceOverallStateAsync(
        DeviceIdRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceOverallStateAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceSystemState>> GetDeviceSystemStateAsync(
        DeviceIdRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceSystemStateAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceSystemConfig>> GetDeviceSystemConfigAsync(
        DeviceIdRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceSystemConfigAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceFanCount>> GetDeviceFanCountAsync(
        DeviceIdRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceFanCountAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceFanState>> GetDeviceFanStateAsync(
        DeviceFanRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceFanStateAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceFanConfig>> GetDeviceFanConfigAsync(
        DeviceFanRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceFanConfigAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceFanSubstitutionMatrix>> GetDeviceFanSubstitutionMatrixAsync(
        DeviceIdRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceFanSubstitutionMatrixAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceSensorCount>> GetDeviceSensorCountAsync(
        DeviceIdRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceSensorCountAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceSensorState>> GetDeviceSensorStateAsync(
        DeviceSensorRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceSensorStateAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceSensorConfig>> GetDeviceSensorConfigAsync(
        DeviceSensorRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceSensorConfigAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceFullConfig>> GetDeviceFullConfigAsync(
        DeviceIdRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceFullConfigAsync(request, ctx), context.CancellationToken);

    public Task<Optional<DeviceOperationResult>> GetDeviceOperationResultAsync(
        DeviceIdRequest request, CallContext context = default) =>
        QueryAsync((c, ctx) => c.GetDeviceOperationResultAsync(request, ctx), context.CancellationToken);


    ////////////////////////////////////////////////////////////////////////////////////////
    // IDeviceService: commands

    public Task ExecuteCommandAsync(
        ExecuteCommandRequest request, CallContext context = default) =>
        ExecuteAsync(
            async (c, ctx) =>
            {
                await c.ExecuteCommandAsync(request, ctx);
                return true;
            },
            isCommand: true,
            context.CancellationToken);


    /// <summary>
    /// Sends a command and waits for its final operation result (or the first failure
    /// of a multi-step command). Returns null if none arrives within the timeout.
    /// Throws like <see cref="ExecuteCommandAsync"/> if the command is not accepted.
    /// </summary>
    public async Task<OperationResultDto?> ExecuteCommandAndWaitAsync(
        string deviceId,
        DeviceCommand command,
        TimeSpan timeout,
        CancellationToken ct,
        Action? onAccepted = null)
    {
        var commandId = Guid.NewGuid();

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct, DisposalToken);
        waitCts.CancelAfter(timeout);

        await using var results = WatchDeviceOperationResultAsync(
                new DeviceIdRequest { DeviceId = deviceId },
                new CallContext(new CallOptions(cancellationToken: waitCts.Token)))
            .GetAsyncEnumerator(waitCts.Token);

        try
        {
            // The first item is the current result: from here on the service-side
            // watcher is registered, so the command's results can't be missed
            if (!await results.MoveNextAsync()) return null;

            await ExecuteCommandAsync(
                new ExecuteCommandRequest
                {
                    DeviceId = deviceId,
                    Command = command,
                    CommandId = commandId
                },
                new CallContext(new CallOptions(cancellationToken: waitCts.Token)));

            onAccepted?.Invoke();

            while (await results.MoveNextAsync())
            {
                var result = results.Current.OperationResult;
                if ((result.CommandId == commandId) && (result.IsFinal || IsFailureResult(result.Result)))
                    return result;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout (or the service was disposed)
        }

        return null;
    }


    /// <summary>
    /// Answers that end a command unsuccessfully
    /// (Busy / Scheduled / Processing only mean "not finished yet").
    /// </summary>
    public static bool IsFailureResult(OperationResult result) =>
        result is not (OperationResult.OK or
                       OperationResult.Busy or
                       OperationResult.RequestScheduled or
                       OperationResult.RequestProcessing or
                       OperationResult.RequestDone);


    ////////////////////////////////////////////////////////////////////////////////////////
    // IDeviceService: streams

    public IAsyncEnumerable<DeviceSummaryListResponse> WatchDeviceListAsync(
        EmptyRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceListAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceOperationResult> WatchDeviceOperationResultAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceOperationResultAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceOverallState> WatchDeviceOverallStateAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceOverallStateAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceSystemState> WatchDeviceSystemStateAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceSystemStateAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceSystemConfig> WatchDeviceSystemConfigAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceSystemConfigAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceFanCount> WatchDeviceFanCountAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceFanCountAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceFanState> WatchDeviceFanStateAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceFanStateAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceFanConfig> WatchDeviceFanConfigAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceFanConfigAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceFanSubstitutionMatrix> WatchDeviceFanSubstitutionMatrixAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceFanSubstitutionMatrixAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceSensorState> WatchDeviceSensorStateAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceSensorStateAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceSensorConfig> WatchDeviceSensorConfigAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceSensorConfigAsync(request, ctx), context.CancellationToken);

    public IAsyncEnumerable<DeviceSensorCount> WatchDeviceSensorCountAsync(
        DeviceIdRequest request, CallContext context = default) =>
        StreamAsync((c, ctx) => c.WatchDeviceSensorCountAsync(request, ctx), context.CancellationToken);

}

// END-OF-FILE
