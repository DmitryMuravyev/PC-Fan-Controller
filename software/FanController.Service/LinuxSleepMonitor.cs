////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// LinuxSleepMonitor.cs
////////////////////////////////////////////////////////////////////////////////////////

using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;
using FanController.Service.Core;


namespace FanController.Service;

/// <summary>
/// Handles system sleep on Linux (systemd-logind, D-Bus): the controllers are prepared
/// for the host shutdown on suspend / hibernation and re-activated on resume.
/// A "delay" inhibitor lock holds the sleep back until the controllers are notified
/// (at most InhibitDelayMaxSec of logind.conf, 5 s by default).
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSleepMonitor : IHostedService
{
    private const string LoginService = "org.freedesktop.login1";
    private const string LoginPath = "/org/freedesktop/login1";
    private const string LoginManagerInterface = "org.freedesktop.login1.Manager";

    // Well below logind's default InhibitDelayMaxSec (5 s)
    private static readonly TimeSpan SuspendTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly FanControllerManager _manager;
    private readonly ILogger<LinuxSleepMonitor> _logger;

    private DBusConnection? _connection;
    private IDisposable? _signalWatcher;

    // Inhibitor lock: an open file descriptor, closing it releases the lock
    private SafeFileHandle? _inhibitLock;
    private readonly object _inhibitLockGate = new();


    public LinuxSleepMonitor(FanControllerManager manager, ILogger<LinuxSleepMonitor> logger)
    {
        _manager = manager;
        _logger = logger;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // IHostedService

    // Never fails the service start: without logind, sleep is just not handled
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            string address = DBusAddress.System
                ?? throw new InvalidOperationException("The system bus address is unknown.");

            var connection = new DBusConnection(address);
            _connection = connection;
            await connection.ConnectAsync();

            _signalWatcher = await connection.WatchSignalAsync(
                LoginService,
                LoginPath,
                LoginManagerInterface,
                "PrepareForSleep",
                static (Message message, object? state) => message.GetBodyReader().ReadBool(),
                OnPrepareForSleep,
                ObserverFlags.None,
                emitOnCapturedContext: false);

            await TakeInhibitLockAsync();

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Sleep monitoring started (systemd-logind).");
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(
                    "Sleep monitoring is not available, the controllers won't be notified on sleep: {Exception}",
                    ex.Message);

            Cleanup();
        }
    }


    public Task StopAsync(CancellationToken cancellationToken)
    {
        Cleanup();
        return Task.CompletedTask;
    }


    private void Cleanup()
    {
        _signalWatcher?.Dispose();
        _signalWatcher = null;

        ReleaseInhibitLock();

        _connection?.Dispose();
        _connection = null;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Sleep handling

    // PrepareForSleep(true) before the sleep, PrepareForSleep(false) after the resume
    private void OnPrepareForSleep(Notification<bool> notification)
    {
        if (!notification.HasValue)
        {
            // Connection closed or failed (the observer disposal on stop has no exception)
            if ((notification.Exception is not null) && _logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(
                    "Sleep monitoring stopped: {Exception}",
                    notification.Exception.Message);
            return;
        }

        if (notification.Value)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Power event: Suspend.");

            _manager.SuspendDevices(SuspendTimeout);

            // The system may sleep now
            ReleaseInhibitLock();
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Power event: Resume.");

            _manager.ResumeDevices();

            // For the next sleep
            _ = TakeInhibitLockSafeAsync();
        }
    }


    // Inhibit("sleep", who, why, "delay") returns the lock as a file descriptor
    private async Task TakeInhibitLockAsync()
    {
        var connection = _connection;
        if (connection is null) return;

        MessageBuffer message;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(
                destination: LoginService,
                path: LoginPath,
                @interface: LoginManagerInterface,
                member: "Inhibit",
                signature: "ssss");
            writer.WriteString("sleep");
            writer.WriteString("Fan Controller Service");
            writer.WriteString("Notifying the fan controllers");
            writer.WriteString("delay");
            message = writer.CreateMessage();
        }

        var handle = await connection.CallMethodAsync(
            message,
            static (Message reply, object? state) => reply.GetBodyReader().ReadHandle<SafeFileHandle>(),
            null);

        SafeFileHandle? previous;
        lock (_inhibitLockGate)
        {
            previous = _inhibitLock;
            _inhibitLock = handle;
        }

        previous?.Dispose();
    }


    private async Task TakeInhibitLockSafeAsync()
    {
        try
        {
            await TakeInhibitLockAsync();
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(
                    "Cannot take the sleep inhibitor lock, the next sleep may not wait for the controllers: {Exception}",
                    ex.Message);
        }
    }


    private void ReleaseInhibitLock()
    {
        SafeFileHandle? handle;
        lock (_inhibitLockGate)
        {
            handle = _inhibitLock;
            _inhibitLock = null;
        }

        handle?.Dispose();
    }

}

// END-OF-FILE
