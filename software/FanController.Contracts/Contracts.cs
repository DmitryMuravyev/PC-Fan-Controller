////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// Contracts.cs
////////////////////////////////////////////////////////////////////////////////////////

using ProtoBuf;


namespace FanController.Contracts;

/// <summary>
/// Contract version shared by the service and the client
/// </summary>
public static class ContractInfo
{
    // Incremented on every incompatible change of the contract (see DTOs.cs, wire format rules)
    public const int ApiVersion = 1;
}


////////////////////////////////////////////////////////////////////////////////////////
// Requests

// No-field requests

[ProtoContract]
public sealed record EmptyRequest;


// Single-field (Device ID) requests

[ProtoContract]
public sealed record DeviceIdRequest
{
    [ProtoMember(1)] public required string DeviceId { get; init; }
}


// Multi-field requests

[ProtoContract]
public sealed record DeviceFanRequest
{
    [ProtoMember(1)] public required string DeviceId { get; init; }
    [ProtoMember(2)] public required int FanIndex { get; init; }
}


[ProtoContract]
public sealed record DeviceSensorRequest
{
    [ProtoMember(1)] public required string DeviceId { get; init; }
    [ProtoMember(2)] public required int SensorIndex { get; init; }
}


[ProtoContract]
public sealed record ExecuteCommandRequest
{
    [ProtoMember(1)] public required string DeviceId { get; init; }
    [ProtoMember(2)] public required DeviceCommand Command { get; init; }

    // Set by the client; returned in the command's operation results
    [ProtoMember(3)] public Guid CommandId { get; init; }       // Guid.Empty: not correlated
}


// Wraps a nullable gRPC response value.
// gRPC cannot return null as a top-level message,
// so we'll use this instead of T?.
[ProtoContract]
public sealed record Optional<T>
{
    [ProtoMember(1)] public bool HasValue { get; init; }
    [ProtoMember(2)] public T? Value { get; init; }

    public static Optional<T> Of(T value) => new() { HasValue = true, Value = value };
    public static Optional<T> Empty() => new() { HasValue = false };

    // Convenience implicit conversions so call sites stay clean
    // (null converts to Empty)
    public static implicit operator Optional<T>(T? value) => (value is null) ? Empty() : Of(value);
    public static implicit operator T?(Optional<T> opt) => opt.HasValue ? opt.Value : default;
}


////////////////////////////////////////////////////////////////////////////////////////
// Responses

/// <summary>
/// Service identity and contract version
/// </summary>
[ProtoContract]
public sealed record ServiceInfo
{
    [ProtoMember(1)] public required int ApiVersion { get; init; }
    [ProtoMember(2)] public required string ServiceVersion { get; init; }
}


// List response wrappers
// Raw arrays and IReadOnlyList<T> cannot be top-level gRPC return types.
// Every collection must be wrapped in a [ProtoContract] message.

[ProtoContract]
public sealed class DeviceSummaryListResponse
{
    [ProtoMember(1)] public List<DeviceSummary> Devices { get; init; } = new();
}


[ProtoContract]
public sealed class HostDeviceListResponse
{
    [ProtoMember(1)] public List<HostDeviceDto> Devices { get; init; } = new();
}


[ProtoContract]
public sealed class HostSensorListResponse
{
    [ProtoMember(1)] public List<HostShortSensorDto> Sensors { get; init; } = new();
}

// END-OF-FILE
