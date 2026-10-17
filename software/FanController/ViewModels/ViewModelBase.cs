////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// ViewModelBase.cs
////////////////////////////////////////////////////////////////////////////////////////

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Contracts;
using FanController.Localization;
using FanController.Services;
using Grpc.Core;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;


namespace FanController.ViewModels;

/// <summary>
/// Outcome of a command awaited in the background
/// </summary>
public enum CommandOutcome
{
    Succeeded,      // The device finished the command
    Failed,         // The device reported an error
    Unknown         // No final answer within the waiting time (e.g. queued behind a long scan)
}


/// <summary>
/// A command awaited in the background has finished (or its waiting time has elapsed)
/// </summary>
public sealed class CommandCompletedEventArgs : EventArgs
{
    public DeviceCommand Command { get; }
    public CommandOutcome Outcome { get; }
    public OperationResultDto? Result { get; }      // null: no final answer

    public CommandCompletedEventArgs(DeviceCommand command, CommandOutcome outcome, OperationResultDto? result)
    {
        Command = command;
        Outcome = outcome;
        Result = result;
    }
}


/// <summary>
/// How long the device's final answer is awaited
/// </summary>
public static class CommandTimeouts
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Configuration = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Restart = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SensorScan = TimeSpan.FromMinutes(2);

    // Fan scans run one fan after another
    public static TimeSpan FanScan(double minutesPerFan, int fanCount) =>
        TimeSpan.FromMinutes((minutesPerFan * Math.Max(1, fanCount)) + 1);
}


/// <summary>
/// Base class of the View Models working with the selected controller:
/// keeps the active device and its service, recreates the device-level
/// cancellation on every switch and provides safe call helpers.
/// </summary>
public abstract class ViewModelBase : ObservableObject, IDisposable
{
    // ViewModel-level cancellation token source
    protected readonly CancellationTokenSource ViewModelCts;

    // Device-level cancellation token source (created by SwitchActiveDevice)
    protected CancellationTokenSource? DeviceCts;

    // Selected device
    protected IManagedDeviceService? ActiveService { get; private set; }
    public string? SelectedDeviceId { get; private set; }

    // Raised on the UI thread when a command sent by SendTrackedCommandAsync finishes
    public event EventHandler<CommandCompletedEventArgs>? CommandCompleted;
    private volatile bool _disposed;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    protected ViewModelBase(CancellationToken parentToken)
    {
        ViewModelCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);

        // Texts built in code follow a runtime language change
        LocalizationService.Instance.CultureChanged += OnLocalizationCultureChanged;
    }


    /// <summary>
    /// Override in derived classes to unsubscribe events or dispose children.
    /// Always call base.Dispose(disposing).
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        LocalizationService.Instance.CultureChanged -= OnLocalizationCultureChanged;

        DeviceCts?.Cancel();
        DeviceCts?.Dispose();
        ViewModelCts.Cancel();
        ViewModelCts.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Device switching

    /// <summary>
    /// Called by the parent ViewModel when the selected device changes:
    /// cancels the previous device's streams, clears the state and starts the new ones.
    /// </summary>
    public void SetActiveDevice(string? deviceId, IManagedDeviceService? service)
    {
        // Nothing to do if the same device was reselected
        if ((deviceId == SelectedDeviceId) && (service == ActiveService)) return;

        string? oldDeviceId = SelectedDeviceId;
        Debug.WriteLine($"[{GetType().Name}] Selected device changed: {oldDeviceId ?? "-"} -> {deviceId ?? "-"}");

        DeviceCts?.Cancel();
        DeviceCts?.Dispose();
        DeviceCts = null;

        SelectedDeviceId = deviceId;
        ActiveService = service;

        ClearState();
        SwitchActiveDevice(deviceId, oldDeviceId);
    }


    /// <summary>
    /// Clears all displayed state when the device is deselected or switched.
    /// </summary>
    protected abstract void ClearState();


    /// <summary>
    /// Starts watching the relevant streams of the new device (if any).
    /// </summary>
    protected abstract void SwitchActiveDevice(string? newDeviceId, string? oldDeviceId);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Localization

    private void OnLocalizationCultureChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        if (Dispatcher.UIThread.CheckAccess())
            OnCultureChanged();
        else
            Dispatcher.UIThread.Post(OnCultureChanged);
    }


    /// <summary>
    /// Called on the UI thread after the UI language has been changed.
    /// Override to rebuild the texts built in code (Strings.X); bindings
    /// to LocalizationService ({lz:Localize}) refresh themselves.
    /// </summary>
    protected virtual void OnCultureChanged() { }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Call helpers

    /// <summary>
    /// Helper for fire-and-forget async watch loops with error handling.
    /// </summary>
    protected async void Watch(Func<Task> watchAction)
    {
        try
        {
            await watchAction();
        }
        catch (OperationCanceledException) { /* normal */ }
        catch (Exception e)
        {
            Debug.WriteLine($"{GetType().Name} watch exception: {e}");
        }
    }


    /// <summary>
    /// Executes a command against the active device, swallowing the expected failure
    /// modes: cancellation, connectivity loss and a disposed service (host removed,
    /// device switched or ViewModel disposed mid-call).
    /// </summary>
    /// <returns>True if the action completed successfully; false otherwise (already logged).</returns>
    protected async Task<bool> TryExecuteAsync(
        Func<Task> action,
        [CallerMemberName] string? caller = null)
    {
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException)
        {
            // ViewModel disposed or device switched mid-call — not a user-facing error
            return false;
        }
        catch (ObjectDisposedException)
        {
            // The device service was disposed (host removed or application exiting)
            return false;
        }
        catch (RpcException ex)
        {
            // Device may have disconnected (e.g. reboot, unplug) — expected in many cases
            Debug.WriteLine($"[{caller}] {ex.StatusCode}: {ex.Status.Detail}");
            return false;
        }
    }


    /// <summary>
    /// Executes a query against the active device, returning <paramref name="fallback"/>
    /// if the call is cancelled, fails with an RPC error or the service is disposed.
    /// </summary>
    protected async Task<T> TryExecuteAsync<T>(
        Func<Task<T>> action,
        T fallback,
        [CallerMemberName] string? caller = null)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            return fallback;
        }
        catch (ObjectDisposedException)
        {
            return fallback;
        }
        catch (RpcException ex)
        {
            Debug.WriteLine($"[{caller}] {ex.StatusCode}: {ex.Status.Detail}");
            return fallback;
        }
    }


    /// <summary>
    /// Sends a command to the active device. Returns false if it was not accepted
    /// (already logged). The device's final answer is awaited in the background and
    /// reported by <see cref="CommandCompleted"/>, unless the device is switched meanwhile.
    /// </summary>
    protected Task<bool> SendTrackedCommandAsync(
        DeviceCommand command,
        TimeSpan timeout,
        [CallerMemberName] string? caller = null)
    {
        string? deviceId = SelectedDeviceId;
        IManagedDeviceService? service = ActiveService;
        if ((deviceId is null) || (service is null)) return Task.FromResult(false);

        CancellationToken token = DeviceCts?.Token ?? ViewModelCts.Token;
        var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = TrackCommandAsync(service, deviceId, command, timeout, token, accepted, caller);
        return accepted.Task;
    }


    // Background part of SendTrackedCommandAsync (never throws)
    private async Task TrackCommandAsync(
        IManagedDeviceService service,
        string deviceId,
        DeviceCommand command,
        TimeSpan timeout,
        CancellationToken token,
        TaskCompletionSource<bool> accepted,
        string? caller)
    {
        OperationResultDto? result = null;
        try
        {
            result = await service.ExecuteCommandAndWaitAsync(
                deviceId, command, timeout, token,
                onAccepted: () => accepted.TrySetResult(true));
        }
        catch (OperationCanceledException) { /* device switched or ViewModel disposed */ }
        catch (ObjectDisposedException) { /* service disposed */ }
        catch (RpcException ex)
        {
            Debug.WriteLine($"[{caller}] {ex.StatusCode}: {ex.Status.Detail}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{caller}] {ex}");
        }

        // Not accepted: the caller gets false. Device switched: nothing to report.
        if (accepted.TrySetResult(false) || token.IsCancellationRequested || _disposed) return;

        CommandOutcome outcome =
            (result is null) ? CommandOutcome.Unknown :
            ServiceItem.IsFailureResult(result.Result) ? CommandOutcome.Failed :
            CommandOutcome.Succeeded;

        Debug.WriteLine($"[{caller}] {command.Kind}: {outcome} ({result?.Result.ToString() ?? "no answer"})");

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
                CommandCompleted?.Invoke(this, new CommandCompletedEventArgs(command, outcome, result));
        });
    }

}

// END-OF-FILE
