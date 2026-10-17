////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// FanViewModel.cs
////////////////////////////////////////////////////////////////////////////////////////


using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Contracts;
using Grpc.Core;
using ProtoBuf.Grpc;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static FanController.ViewModels.FanItem;


namespace FanController.ViewModels;

/// <summary>
/// Fans Tab contents View Model
/// </summary>
public partial class FanViewModel : ViewModelBase
{
    private volatile bool _disposed = false;

    // Serializes concurrent fan count / config / state stream handlers
    private readonly SemaphoreSlim _snapshotLock = new(1, 1);

    private int _fanCount = 0;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Observable state

    public ObservableCollection<FanItem> Fans { get; } = new();

    // True when no fans or all fans are selected: scan commands then target all fans at once.
    [ObservableProperty]
    private bool _scanAll = true;

    [ObservableProperty]
    private bool _selectionNotEmpty = false;

    [ObservableProperty]
    private int _selectedFansCount = 0;

    // Bound to the "select all" CheckBox in the table header
    private bool _allFansSelected = false;
    public bool AllFansSelected
    {
        get => _allFansSelected;
        set
        {
            foreach (var fan in Fans) fan.SetSelectedSilently(value);
            CheckSelectedFans();
        }
    }

    // Fan highlighted in the table (shown in the status panels)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentFan))]
    private FanItem? _currentFan;

    public bool HasCurrentFan => CurrentFan is not null;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    public FanViewModel(CancellationToken parentToken)
        : base(parentToken)
    {
    }


    // Disposal
    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        ClearFans();

        // Base disposes DeviceCts, ViewModelCts, etc.
        base.Dispose(disposing);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModelBase overrides

    protected override void ClearState()
    {
        // Fan items (and their throttles) belong to the previous device
        ClearFans();
    }


    protected override void SwitchActiveDevice(
        string? newDeviceId,
        string? oldDeviceId)
    {
        if (newDeviceId is null || ActiveService is null) return;

        DeviceCts = CancellationTokenSource
            .CreateLinkedTokenSource(ViewModelCts.Token, ActiveService.DisposalToken);

        Watch(() => WatchFanCountAsync(newDeviceId, DeviceCts.Token));
        Watch(() => WatchFanConfigAsync(newDeviceId, DeviceCts.Token));
        Watch(() => WatchFanStateAsync(newDeviceId, DeviceCts.Token));
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Stream watchers

    /*
    private async Task WatchOperationResultAsync(string deviceId, CancellationToken ct)
    {
        await foreach (var result in Service.WatchDeviceOperationResultAsync(deviceId, ct))
        {
            if ((SelectedDeviceId == result.Id) && (result.OperationResult.TargetObjectIndex is int index))
            {
                Debug.WriteLine($"Fan: {index}, operation RESULT: {result.OperationResult.ToString()}");
            }
        }
    }
    */

    private async Task WatchFanCountAsync(
        string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceFanCountAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if (SelectedDeviceId != result.Id) continue;

            await _snapshotLock.WaitAsync(ct);
            try
            {
                CheckFanCount(result.FanCount, ct);
            }
            finally { _snapshotLock.Release(); }
        }
    }

    private async Task WatchFanConfigAsync(
        string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceFanConfigAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if (SelectedDeviceId != result.Id) continue;

            await _snapshotLock.WaitAsync(ct);
            try
            {
                CheckFanCount(result.FanCount, ct);

                if (result.FanConfig.Index < _fanCount)
                {
                    FanItem fanToUpdate = Fans[result.FanConfig.Index];
                    fanToUpdate.FanConfig = result.FanConfig;
                }
            }
            finally { _snapshotLock.Release(); }
        }
    }

    private async Task WatchFanStateAsync(
        string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceFanStateAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if (SelectedDeviceId != result.Id) continue;

            await _snapshotLock.WaitAsync(ct);
            try
            {
                if (result.FanState.Index < _fanCount)
                {
                    FanItem fanToUpdate = Fans[result.FanState.Index];
                    fanToUpdate.FanState = result.FanState;
                }
            }
            finally { _snapshotLock.Release(); }
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModel logic

    private void CheckFanCount(int fanCount, CancellationToken deviceToken)
    {
        if (_fanCount == fanCount) return;

        ClearFans();
        _fanCount = fanCount;
        for (int i = 0; i < _fanCount; i++)
            Fans.Add(new FanItem(i, this, deviceToken));
    }


    private void ClearFans()
    {
        CurrentFan = null;

        var oldFans = Fans.ToList();
        Fans.Clear();
        foreach (var fan in oldFans) fan.Dispose();

        _fanCount = 0;
        CheckSelectedFans();
    }


    /// <summary>
    /// Recalculates the selection-dependent properties.
    /// Called by <see cref="FanItem"/> when its selection changes.
    /// </summary>
    public void CheckSelectedFans()
    {
        int count = Fans.Count(fan => fan.Selected);

        SelectedFansCount = count;
        SelectionNotEmpty = (count > 0);
        _allFansSelected = (count > 0) && (count == Fans.Count);
        ScanAll = _allFansSelected || !SelectionNotEmpty;
        OnPropertyChanged(nameof(AllFansSelected));
    }


    /// <summary>
    /// Runs the given command against every selected fan and reports overall success,
    /// same aggregation pattern as the per-fan branches of ScanHardware/ScanModels.
    /// </summary>
    /// <param name="action"></param>
    private async Task<bool> ForSelectedAsync(Func<FanItem, Task<bool>> action)
    {
        // Snapshot: the list may be rebuilt (device switch) between awaits
        string? deviceId = SelectedDeviceId;
        var selectedFans = Fans.Where(fan => fan.Selected).ToList();

        bool overallResult = true;
        foreach (var fan in selectedFans)
        {
            if (SelectedDeviceId != deviceId) return false;
            overallResult &= await action(fan);
        }
        return overallResult;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Device commands

    public Task<bool> ResetState() =>
        ForSelectedAsync(fan => fan.ResetState());

    public Task<bool> SetControlMode(FanControlMode mode) =>
        ForSelectedAsync(fan => fan.SetControlMode(mode));

    public Task<bool> SetOperationMode(FanOperationMode mode) =>
        ForSelectedAsync(fan => fan.SetOperationMode(mode));

    public Task<bool> SetPowerProfile(FanPowerProfile profile) =>
        ForSelectedAsync(fan => fan.SetPowerProfile(profile));


    public Task<bool> ScanHardware()
    {
        if (!ScanAll)
        {
            // The selected fans are scanned one after another
            var timeout = CommandTimeouts.FanScan(FanItemConstants.HardwareScanMaxDurationMinutes, SelectedFansCount);
            return ForSelectedAsync(fan => fan.RescanHardware(timeout));
        }

        return SendTrackedCommandAsync(
            new RescanAllFanHardwareCommand(),
            CommandTimeouts.FanScan(FanItemConstants.HardwareScanMaxDurationMinutes, Fans.Count));
    }


    public Task<bool> ScanModels()
    {
        if (!ScanAll)
        {
            var timeout = CommandTimeouts.FanScan(FanItemConstants.ModelScanMaxDurationMinutes, SelectedFansCount);
            return ForSelectedAsync(fan => fan.RescanModel(timeout));
        }

        return SendTrackedCommandAsync(
            new RescanAllFanModelsCommand(),
            CommandTimeouts.FanScan(FanItemConstants.ModelScanMaxDurationMinutes, Fans.Count));
    }


    /// <summary>
    /// Sends a single-fan command to the active device. With <paramref name="trackTimeout"/>
    /// the device's answer is awaited in the background (reported by CommandCompleted).
    /// </summary>
    public Task<bool> SendFanCommandAsync(DeviceCommand command, TimeSpan? trackTimeout = null)
    {
        if (trackTimeout is TimeSpan timeout)
            return SendTrackedCommandAsync(command, timeout);

        if ((SelectedDeviceId is null) || (ActiveService is null))
            return Task.FromResult(false);

        return TryExecuteAsync(() => ActiveService.ExecuteCommandAsync(
            new ExecuteCommandRequest
            {
                DeviceId = SelectedDeviceId,
                Command = command
            },
            new CallContext(new CallOptions(
                cancellationToken: ViewModelCts.Token))));
    }


    /// <summary>
    /// Throttled single-fan command apply (invoked by the <see cref="FanItem"/> slider throttles).
    /// <paramref name="onSending"/> runs on the UI thread right before the command is sent.
    /// </summary>
    public async Task ApplyFanCommandAsync(
        DeviceCommand command,
        Action onSending,
        CancellationToken token)
    {
        string? deviceId = null;
        IDeviceService? service = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            deviceId = SelectedDeviceId;
            service = ActiveService;
            onSending();
        });

        if ((deviceId is null) || (service is null)) return;

        // Service call — on thread pool
        await TryExecuteAsync(() => service.ExecuteCommandAsync(
            new ExecuteCommandRequest
            {
                DeviceId = deviceId,
                Command = command
            },
            new CallContext(new CallOptions(cancellationToken: token))));
    }

}

// END-OF-FILE
