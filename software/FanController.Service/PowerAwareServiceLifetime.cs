////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// PowerAwareServiceLifetime.cs
////////////////////////////////////////////////////////////////////////////////////////

using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using FanController.Service.Core;


namespace FanController.Service;

/// <summary>
/// Windows service lifetime that also handles system power events:
/// the controllers are prepared for the host shutdown on suspend / hibernation
/// (also the Fast Startup "shutdown") and re-activated on resume.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerAwareServiceLifetime : WindowsServiceLifetime
{
    // Windows doesn't wait long for the suspend notification to be handled
    private static readonly TimeSpan SuspendTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly FanControllerManager _manager;
    private readonly ILogger _logger;


    public PowerAwareServiceLifetime(
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory,
        IOptions<HostOptions> optionsAccessor,
        IOptions<WindowsServiceLifetimeOptions> windowsServiceOptionsAccessor,
        FanControllerManager manager)
        : base(environment, applicationLifetime, loggerFactory, optionsAccessor, windowsServiceOptionsAccessor)
    {
        _manager = manager;
        _logger = loggerFactory.CreateLogger<PowerAwareServiceLifetime>();

        // Must be set before the service is started
        CanHandlePowerEvent = true;
    }


    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Power event: {Status}.", powerStatus);

        switch (powerStatus)
        {
            case PowerBroadcastStatus.Suspend:
                _manager.SuspendDevices(SuspendTimeout);
                break;

            // ResumeAutomatic always comes; ResumeSuspend follows if a user is present
            case PowerBroadcastStatus.ResumeAutomatic:
            case PowerBroadcastStatus.ResumeSuspend:
                _manager.ResumeDevices();
                break;
        }

        return base.OnPowerEvent(powerStatus);
    }

}

// END-OF-FILE
