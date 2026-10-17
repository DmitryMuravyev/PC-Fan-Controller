////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// AppSettings.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;


namespace FanController.Settings;

public static class SettingsDefaults
{
    public const string DefaultDaemonName = "Local Host";
    public const string DefaultDaemonAddress = "127.0.0.1";
    public const int DefaultDaemonPort = 51515;
}


/// <summary>
/// Root settings model, serialized to / from settings.json.
/// Add new setting groups here as nested classes.
/// </summary>
public sealed class AppSettings
{
    // Configured daemon hosts (by default: the local one)
    public List<HostConfig> Hosts { get; set; } = new()
    {
        new HostConfig
        {
            Id = Guid.NewGuid(),
            Name = SettingsDefaults.DefaultDaemonName,
            Host = SettingsDefaults.DefaultDaemonAddress,
            Port = SettingsDefaults.DefaultDaemonPort
        }
    };

    // Controller to select automatically on start (device ID)
    public string LastSelectedDevice { get; set; } = String.Empty;

    // Future setting groups go here, e.g.:
    // public AppearanceSettings Appearance { get; set; } = new();
    // public NotificationSettings Notifications { get; set; } = new();
}

// END-OF-FILE
