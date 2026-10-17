////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// HostConfig.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;


namespace FanController.Settings;

/// <summary>
/// Connection parameters of one remote daemon host.
/// </summary>
public sealed class HostConfig
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }

    /// <summary>
    /// True if this host is configured with the given endpoint
    /// (surrounding spaces are ignored, host names compare case-insensitively).
    /// </summary>
    public bool HasEndpoint(string host, int port) =>
        (Port == port) &&
        String.Equals(Host.Trim(), host.Trim(), StringComparison.OrdinalIgnoreCase);
}

// END-OF-FILE
