////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// IDeviceService.cs
////////////////////////////////////////////////////////////////////////////////////////

using ProtoBuf;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;


namespace FanController.Contracts;

////////////////////////////////////////////////////////////////////////////////////////
// Commands
// The ProtoInclude numbers are part of the wire format (CommandKind uses the same values).

[ProtoContract]
[ProtoInclude(1, typeof(SetActiveCommand))]
[ProtoInclude(2, typeof(FactoryResetCommand))]
[ProtoInclude(3, typeof(RebootCommand))]
[ProtoInclude(4, typeof(PrepareToHostShutdownCommand))]
[ProtoInclude(5, typeof(SetFullConfigurationCommand))]
[ProtoInclude(6, typeof(SetPartialConfigurationCommand))]
[ProtoInclude(7, typeof(SetSystemConfigurationCommand))]
[ProtoInclude(8, typeof(SetSystemBehaviorCommand))]
[ProtoInclude(9, typeof(ResetFanCommand))]
[ProtoInclude(10, typeof(RescanFanHardwareCommand))]
[ProtoInclude(11, typeof(RescanAllFanHardwareCommand))]
[ProtoInclude(12, typeof(RescanFanModelCommand))]
[ProtoInclude(13, typeof(RescanAllFanModelsCommand))]
[ProtoInclude(14, typeof(SetFanConfigurationCommand))]
[ProtoInclude(15, typeof(SetFanControlModeCommand))]
[ProtoInclude(16, typeof(SetFanOperationModeCommand))]
[ProtoInclude(17, typeof(SetFanControlBasisCommand))]
[ProtoInclude(18, typeof(SetFanControlValueCommand))]
[ProtoInclude(19, typeof(SetFanSubstitutionMatrixCommand))]
[ProtoInclude(20, typeof(ResetSensorCommand))]
[ProtoInclude(21, typeof(RescanSensorsCommand))]
[ProtoInclude(22, typeof(SetSensorConfigurationCommand))]
[ProtoInclude(23, typeof(AddSensorCommand))]
[ProtoInclude(24, typeof(DeleteSensorCommand))]
[ProtoInclude(25, typeof(DeleteSensorsCommand))]
public abstract record DeviceCommand
{
    // Not serialized (derived from the type)
    public abstract CommandKind Kind { get; }
}


//////////////////////////
// No-field commands

[ProtoContract]
public sealed record FactoryResetCommand : DeviceCommand
{
    public override CommandKind Kind => CommandKind.FactoryReset;
}

[ProtoContract]
public sealed record RebootCommand : DeviceCommand
{
    public override CommandKind Kind => CommandKind.Reboot;
}

[ProtoContract]
public sealed record PrepareToHostShutdownCommand : DeviceCommand
{
    public override CommandKind Kind => CommandKind.PrepareToHostShutdown;
}

[ProtoContract]
public sealed record RescanAllFanHardwareCommand : DeviceCommand
{
    public override CommandKind Kind => CommandKind.RescanAllFanHardware;
}

[ProtoContract]
public sealed record RescanAllFanModelsCommand : DeviceCommand
{
    public override CommandKind Kind => CommandKind.RescanAllFanModels;
}

[ProtoContract]
public sealed record RescanSensorsCommand : DeviceCommand
{
    public override CommandKind Kind => CommandKind.RescanSensors;
}


//////////////////////////
// Single-field commands

[ProtoContract]
public sealed record SetActiveCommand : DeviceCommand
{
    [ProtoMember(1)] public required bool Active { get; init; }
    public override CommandKind Kind => CommandKind.SetActive;
}


[ProtoContract]
public sealed record SetFullConfigurationCommand : DeviceCommand
{
    [ProtoMember(1)] public required DeviceFullConfig Config { get; init; }
    public override CommandKind Kind => CommandKind.SetFullConfiguration;
}


[ProtoContract]
public sealed record SetSystemConfigurationCommand : DeviceCommand
{
    [ProtoMember(1)] public required SystemConfigDto Config { get; init; }
    public override CommandKind Kind => CommandKind.SetSystemConfiguration;
}


[ProtoContract]
public sealed record SetSystemBehaviorCommand : DeviceCommand
{
    [ProtoMember(1)] public required SystemBehaviorDto Behavior { get; init; }
    public override CommandKind Kind => CommandKind.SetSystemBehavior;
}


[ProtoContract]
public sealed record ResetFanCommand : DeviceCommand
{
    [ProtoMember(1)] public required int FanIndex { get; init; }
    public override CommandKind Kind => CommandKind.ResetFan;
}


[ProtoContract]
public sealed record RescanFanHardwareCommand : DeviceCommand
{
    [ProtoMember(1)] public required int FanIndex { get; init; }
    public override CommandKind Kind => CommandKind.RescanFanHardware;
}


[ProtoContract]
public sealed record RescanFanModelCommand : DeviceCommand
{
    [ProtoMember(1)] public required int FanIndex { get; init; }
    public override CommandKind Kind => CommandKind.RescanFanModel;
}


[ProtoContract]
public sealed record SetFanSubstitutionMatrixCommand : DeviceCommand
{
    [ProtoMember(1)] public required SubstitutionMatrixDto Matrix { get; init; }
    public override CommandKind Kind => CommandKind.SetFanSubstitutionMatrix;
}


[ProtoContract]
public sealed record ResetSensorCommand : DeviceCommand
{
    [ProtoMember(1)] public required byte[] SerialNumber { get; init; }
    public override CommandKind Kind => CommandKind.ResetSensor;
}


[ProtoContract]
public sealed record SetSensorConfigurationCommand : DeviceCommand
{
    [ProtoMember(1)] public required SensorConfigDto Config { get; init; }
    public override CommandKind Kind => CommandKind.SetSensorConfiguration;
}


[ProtoContract]
public sealed record AddSensorCommand : DeviceCommand
{
    [ProtoMember(1)] public required SensorConfigDto Config { get; init; }
    public override CommandKind Kind => CommandKind.AddSensor;
}


[ProtoContract]
public sealed record DeleteSensorCommand : DeviceCommand
{
    [ProtoMember(1)] public required byte[] SerialNumber { get; init; }
    public override CommandKind Kind => CommandKind.DeleteSensor;
}


//////////////////////////
// Multi-field commands

[ProtoContract]
public sealed record SetPartialConfigurationCommand : DeviceCommand
{
    [ProtoMember(1)] public required SystemConfigDto? SystemConfig { get; init; }
    [ProtoMember(2)] public required FanConfigDto[]? FanConfigs { get; init; }
    [ProtoMember(3)] public required SubstitutionMatrixDto? Matrix { get; init; }
    [ProtoMember(4)] public required SensorConfigDto[]? SensorConfigs { get; init; }
    public override CommandKind Kind => CommandKind.SetPartialConfiguration;
}


[ProtoContract]
public sealed record SetFanConfigurationCommand : DeviceCommand
{
    [ProtoMember(1)] public required int FanIndex { get; init; }
    [ProtoMember(2)] public required FanConfigDto Config { get; init; }
    public override CommandKind Kind => CommandKind.SetFanConfiguration;
}


[ProtoContract]
public sealed record SetFanControlModeCommand : DeviceCommand
{
    [ProtoMember(1)] public required int FanIndex { get; init; }
    [ProtoMember(2)] public required FanControlMode Mode { get; init; }
    public override CommandKind Kind => CommandKind.SetFanControlMode;
}


[ProtoContract]
public sealed record SetFanOperationModeCommand : DeviceCommand
{
    [ProtoMember(1)] public required int FanIndex { get; init; }
    [ProtoMember(2)] public required FanOperationMode Mode { get; init; }
    public override CommandKind Kind => CommandKind.SetFanOperationMode;
}


[ProtoContract]
public sealed record SetFanControlBasisCommand : DeviceCommand
{
    [ProtoMember(1)] public required int FanIndex { get; init; }
    [ProtoMember(2)] public required double Basis { get; init; }
    public override CommandKind Kind => CommandKind.SetFanControlBasis;
}


[ProtoContract]
public sealed record SetFanControlValueCommand : DeviceCommand
{
    [ProtoMember(1)] public required int FanIndex { get; init; }
    [ProtoMember(2)] public required double Value { get; init; }
    public override CommandKind Kind => CommandKind.SetFanControlValue;
}


[ProtoContract]
public sealed record DeleteSensorsCommand : DeviceCommand
{
    [ProtoMember(1)] public required byte[][] SerialNumbers { get; init; }
    public override CommandKind Kind => CommandKind.DeleteSensors;
}


////////////////////////////////////////////////////////////////////////////////////////
// Service Interface

// The single abstraction boundary between presentation and hardware.
// Every method uses only serializable types.
//
// Streams use IAsyncEnumerable — maps naturally to gRPC server streaming.
// Nullable query results are wrapped in Optional<T> because gRPC cannot
// return null directly as a top-level response message.

[Service]
public interface IDeviceService
{
    ////////////////////////////////////////////////////////////////////////////////////////
    // Queries

    /// <summary>
    /// Service identity and contract version (checked by the client on connection).
    /// </summary>
    [Operation]
    Task<ServiceInfo> GetServiceInfoAsync(
        EmptyRequest request,
        CallContext context = default);

    [Operation]
    Task<DeviceSummaryListResponse> GetDeviceListAsync(
        EmptyRequest request,
        CallContext context = default);

    [Operation]
    Task<HostDeviceListResponse> GetHostDeviceListAsync(
        EmptyRequest request,
        CallContext context = default);

    [Operation]
    Task<HostSensorListResponse> GetHostSensorListAsync(
        EmptyRequest request,
        CallContext context = default);


    // Nullable results use Optional<T> — gRPC messages cannot be null

    [Operation]
    Task<Optional<DeviceOverallState>> GetDeviceOverallStateAsync(
        DeviceIdRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceOperationResult>> GetDeviceOperationResultAsync(
        DeviceIdRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceFullConfig>> GetDeviceFullConfigAsync(
        DeviceIdRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceSystemState>> GetDeviceSystemStateAsync(
        DeviceIdRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceSystemConfig>> GetDeviceSystemConfigAsync(
        DeviceIdRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceFanCount>> GetDeviceFanCountAsync(
        DeviceIdRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceFanState>> GetDeviceFanStateAsync(
        DeviceFanRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceFanConfig>> GetDeviceFanConfigAsync(
        DeviceFanRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceFanSubstitutionMatrix>> GetDeviceFanSubstitutionMatrixAsync(
        DeviceIdRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceSensorCount>> GetDeviceSensorCountAsync(
        DeviceIdRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceSensorState>> GetDeviceSensorStateAsync(
        DeviceSensorRequest request,
        CallContext context = default);

    [Operation]
    Task<Optional<DeviceSensorConfig>> GetDeviceSensorConfigAsync(
        DeviceSensorRequest request,
        CallContext context = default);

    ////////////////////////////////////////////////////////////////////////////////////////
    // Commands endpoint

    /// <summary>
    /// Queues a command. Fails with NotFound (unknown device), InvalidArgument,
    /// FailedPrecondition (not accepted) or Unimplemented (unknown command).
    /// The device's answers arrive as operation results carrying the request's CommandId.
    /// </summary>
    [Operation]
    Task ExecuteCommandAsync(
        ExecuteCommandRequest request,
        CallContext context = default);

    ////////////////////////////////////////////////////////////////////////////////////////
    // Streams (server -> client push)
    // Each stream yields the current state first, then every change.

    /// <summary>
    /// Device list changes (device plugged/unplugged).
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceSummaryListResponse> WatchDeviceListAsync(
        EmptyRequest request,
        CallContext context = default);

    /// <summary>
    /// Device overall state changes (activated/deactivated, connected/disconnected).
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceOverallState> WatchDeviceOverallStateAsync(
        DeviceIdRequest request,
        CallContext context = default);

    /// <summary>
    /// Results of the commands sent to the device (by any client).
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceOperationResult> WatchDeviceOperationResultAsync(
        DeviceIdRequest request,
        CallContext context = default);

    /// <summary>
    /// System State updates of a specific device.
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceSystemState> WatchDeviceSystemStateAsync(
        DeviceIdRequest request,
        CallContext context = default);

    /// <summary>
    /// System Config updates of a specific device.
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceSystemConfig> WatchDeviceSystemConfigAsync(
        DeviceIdRequest request,
        CallContext context = default);

    /// <summary>
    /// Fan Count updates of a specific device.
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceFanCount> WatchDeviceFanCountAsync(
        DeviceIdRequest request,
        CallContext context = default);

    /// <summary>
    /// Fan State updates of a specific device.
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceFanState> WatchDeviceFanStateAsync(
        DeviceIdRequest request,
        CallContext context = default);

    /// <summary>
    /// Fan Config updates of a specific device.
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceFanConfig> WatchDeviceFanConfigAsync(
        DeviceIdRequest request,
        CallContext context = default);

    /// <summary>
    /// Substitution Matrix updates of a specific device.
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceFanSubstitutionMatrix> WatchDeviceFanSubstitutionMatrixAsync(
        DeviceIdRequest request,
        CallContext context = default);

    /// <summary>
    /// Sensor Count updates of a specific device.
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceSensorCount> WatchDeviceSensorCountAsync(
        DeviceIdRequest request,
        CallContext context = default);

    /// <summary>
    /// Sensor State updates of a specific device.
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceSensorState> WatchDeviceSensorStateAsync(
        DeviceIdRequest request,
        CallContext context = default);

    /// <summary>
    /// Sensor Config updates of a specific device.
    /// </summary>
    [Operation]
    IAsyncEnumerable<DeviceSensorConfig> WatchDeviceSensorConfigAsync(
        DeviceIdRequest request,
        CallContext context = default);
}

// END-OF-FILE
