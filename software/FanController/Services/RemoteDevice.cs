////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// RemoteDevice.cs
////////////////////////////////////////////////////////////////////////////////////////

using FanController.Contracts;


namespace FanController.Services;

/// <summary>
/// One physical controller on a remote host, as reported by the host's device list.
/// Updated by the <see cref="ServiceItem"/> watcher; read by the UI.
/// </summary>
public sealed class RemoteDevice
{
    // Host the controller is attached to
    public ServiceItem Service { get; }

    // Identity (the ID never changes; name/version may change after a firmware update)
    public string Id { get; }
    public string Name { get; private set; }
    public string Version { get; private set; }

    // The controller is connected to the host (USB) and served by its daemon
    private volatile bool _isAvailable;
    public bool IsAvailable => _isAvailable;


    public RemoteDevice(ServiceItem service, DeviceSummary summary)
    {
        Service = service;
        Id = summary.Id;
        Name = summary.Name;
        Version = summary.Version;
        _isAvailable = summary.IsAvailable;
    }


    // Applies a new summary; returns true if anything changed
    internal bool Update(DeviceSummary summary)
    {
        bool changed = (Name != summary.Name) ||
                       (Version != summary.Version) ||
                       (_isAvailable != summary.IsAvailable);

        Name = summary.Name;
        Version = summary.Version;
        _isAvailable = summary.IsAvailable;
        return changed;
    }


    // The controller is absent from the host's device list; returns true if it was available
    internal bool MarkUnavailable()
    {
        if (!_isAvailable) return false;
        _isAvailable = false;
        return true;
    }
}

// END-OF-FILE
