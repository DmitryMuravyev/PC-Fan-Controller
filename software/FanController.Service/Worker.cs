////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// Worker.cs
////////////////////////////////////////////////////////////////////////////////////////

using System.Runtime;
using System.Runtime.InteropServices;


namespace FanController.Service;

/// <summary>
/// Hosted service: logs the startup environment and periodically nudges the GC.
/// (The controllers are managed by FanControllerManager, a hosted service itself.)
/// </summary>
public class Worker : BackgroundService
{
    // Constants
    private static class WorkerConstants
    {
        public const int GCCollectionIntervalSec = 180;     // 3 min between forced GC collection runs
    }

    private readonly ILogger<Worker> _logger;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // BackgroundService overrides

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Service started on {OS}.",
                RuntimeInformation.OSDescription);

            _logger.LogInformation(
                "GC mode: Server = {IsServer}, Heap size limit = {Limit:N0} bytes.",
                GCSettings.IsServerGC,
                GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        }

        // Compact the heap after the startup allocation burst
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        // Keep alive until shutdown, with a periodic GC nudge.
        // A legacy from .NET 8: in .NET 10 the GC seems to work well on its own,
        // but the forced cleanup is kept just in case.
        using var gcTimer = new PeriodicTimer(
            TimeSpan.FromSeconds(WorkerConstants.GCCollectionIntervalSec));

        try
        {
            while (await gcTimer.WaitForNextTickAsync(stoppingToken))
            {
                long memBefore = GC.GetTotalMemory(false);

                GC.Collect(1, GCCollectionMode.Optimized, blocking: false);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    long memAfter = GC.GetTotalMemory(false);
                    _logger.LogDebug(
                        "GC nudge: {Before:N0} -> {After:N0} bytes, ({Freed:N0} freed).",
                        memBefore, memAfter, memBefore - memAfter);
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

}

// END-OF-FILE
