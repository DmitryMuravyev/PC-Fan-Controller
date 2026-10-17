////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// App.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FanController.Services;
using FanController.Settings;
using FanController.ViewModels;
using FanController.Views;


namespace FanController;

/// <summary>
/// Application: creates the settings store, the host list and the main window,
/// and releases them (streams, gRPC channels) on exit.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _appCts = new();
    private ServiceList? _serviceList;
    private MainWindowViewModel? _mainViewModel;

    // The cleanup runs once, whichever way the application is closed
    private Task? _cleanupTask;
    private bool _cleanupCompleted = false;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Initialization

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }


    public override void OnFrameworkInitializationCompleted()
    {
        var settingsStore = new JsonSettingsStore();
        settingsStore.Load();

        // Owns all gRPC channels and host watchers. Created on the UI thread:
        // its services raise their events on it.
        _serviceList = new ServiceList(settingsStore, ReconnectDelay, _appCts.Token);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainViewModel = new MainWindowViewModel(_serviceList, settingsStore, _appCts.Token);

            var mainWindow = new MainWindow { DataContext = _mainViewModel };
            mainWindow.Closing += OnMainWindowClosing;
            desktop.MainWindow = mainWindow;

            // OS-initiated shutdown (session end, Cmd+Q, ...)
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Shutdown

    // Closing the main window: postponed until the cleanup has finished
    private async void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_cleanupCompleted || (sender is not Window window)) return;

        e.Cancel = true;
        await CleanupOnceAsync();
        window.Close();
    }


    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_cleanupCompleted) return;

        e.Cancel = true;
        await CleanupOnceAsync();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }


    private Task CleanupOnceAsync() => _cleanupTask ??= CleanupAsync();


    private async Task CleanupAsync()
    {
        try
        {
            // Cancel all streams first: unblocks any pending MoveNextAsync
            _appCts.Cancel();

            _mainViewModel?.Dispose();

            if (_serviceList is not null)
            {
                var dispose = _serviceList.DisposeAsync().AsTask();
                if (await Task.WhenAny(dispose, Task.Delay(CleanupTimeout)) != dispose)
                    Debug.WriteLine("[App] Cleanup timed out.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Cleanup failed: {ex}");
        }
        finally
        {
            _cleanupCompleted = true;
        }
    }

}

// END-OF-FILE
