////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// HostSensorManager.cs
////////////////////////////////////////////////////////////////////////////////////////

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FanController.Contracts;

#if WINDOWS
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Gpu;
using LibreHardwareMonitor.Hardware.Storage;
#endif


namespace FanController.Service.Core;

/// <summary>
/// Host temperature sensor data reader
/// </summary>
internal sealed class HostSensorManager : IDisposable
{
    // Constants
    private static class SensorManagerConstants
    {
        public const int RequestActivityPeriodMs = 60000;       // Duration of automatic sensor polling
                                                                // following a request from external code.
        public const int MinUpdateIntervalMs = 500;             // Minimum sensor polling interval
        public const int HouseKeepingPeriodMs = 1000;           // Sensor automatic polling interval
        public const int HwmonTimeoutMs = 3000;                 // Sensor polling timeout for Linux (per hwmon device)
        public const int FastHardwareTimeoutMs = 3000;          // CPU, GPU, MB, RAM...
        public const int SlowHardwareTimeoutMs = 10000;         // Storage (buffer flushes)
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Inner Types

    // For using byte arrays as dictionary keys
    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if ((x is null) || (y is null)) return false;
            return x.AsSpan().SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj)
        {
            var hashCode = new HashCode();
            hashCode.AddBytes(obj);
            return hashCode.ToHashCode();
        }
    }


    /// <summary>
    /// Device object
    /// </summary>
    private sealed class HostDevice
    {
        public string Name = String.Empty;
        public string Info = String.Empty;
        public HostHardwareType Type = HostHardwareType.Unknown;
        public List<HostDevice> SubDevices = new();
        public List<HostSensor> Sensors = new();
    }


    /// <summary>
    /// Sensor object
    /// </summary>
    private sealed class HostSensor
    {
        public readonly byte[] ID;
        public string Name = String.Empty;
        public string Path = String.Empty;
        public string Info = String.Empty;
        public double Temperature = 0.0;
        public double? MaxTemperature = null;
        public double? CritTemperature = null;
        public bool IsValid = false;
        public bool ReportedUnavailable = false;

        public HostSensor(byte[] id)
        {
            ID = id;
        }
    }


    /// <summary>
    /// A blocking hardware read guarded by a timeout. A read that exceeds the timeout
    /// keeps running in the background, and no new read starts until it finishes
    /// (the hardware is never accessed concurrently). Failures are logged once.
    /// Not thread-safe: used under the update gate only.
    /// </summary>
    private sealed class GuardedRead
    {
        private readonly string _name;
        private readonly int _timeoutMs;
        private readonly ILogger _logger;

        private Task? _hungRead = null;
        private bool _failed = false;

        public GuardedRead(string name, int timeoutMs, ILogger logger)
        {
            _name = name;
            _timeoutMs = timeoutMs;
            _logger = logger;
        }

        // Returns true if the read completed successfully within the timeout
        public async Task<bool> RunAsync(Action read, CancellationToken token)
        {
            if (_hungRead is not null)
            {
                if (!_hungRead.IsCompleted) return false;

                _ = _hungRead.Exception;    // Observe a late failure
                _hungRead = null;
            }

            var readTask = Task.Run(read, CancellationToken.None);
            try
            {
                await readTask.WaitAsync(TimeSpan.FromMilliseconds(_timeoutMs), token);

                if (_failed)
                {
                    _failed = false;
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("{Name} hardware read restored.", _name);
                }
                return true;
            }
            catch (TimeoutException)
            {
                _hungRead = readTask;
                ReportFailure("timed out");
                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _hungRead = readTask;
                throw;
            }
            catch (Exception ex)
            {
                ReportFailure(ex.Message);
                return false;
            }
        }

        // Waits for a read left running (before closing the hardware)
        public bool WaitForHungRead(int timeoutMs)
        {
            try
            {
                return (_hungRead is null) || _hungRead.Wait(timeoutMs);
            }
            catch (AggregateException)
            {
                return true;    // Finished with an error
            }
        }

        private void ReportFailure(string reason)
        {
            if (_failed) return;
            _failed = true;

            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("{Name} hardware read failed: {Reason}", _name, reason);
        }
    }


#if WINDOWS
    // For 'Computer' type objects
    // that combine fast or slow sensors.
    private enum ComputerType
    {
        Fast,
        Slow
    }

    // We'll use two Computer instances:
    // for fast hardware (CPU, GPU, MB, etc.)
    // and for slow hardware (HDD, flash drives, etc.)
    private sealed class ComputerInstance
    {
        public readonly Computer Computer;
        public readonly UpdateVisitor Visitor = new();
        public readonly GuardedRead Read;
        public readonly int TimeoutMs;
        public readonly ComputerType Type;

        // Sensors of this instance's hardware (filled by CollectSensorsWindows)
        public readonly List<(HostSensor Sensor, ISensor HwSensor)> Sensors = new();

        public bool IsOpen { get; private set; } = false;

        public ComputerInstance(Computer computer, int timeoutMs, ComputerType type, ILogger logger)
        {
            Computer = computer;
            TimeoutMs = timeoutMs;
            Type = type;
            Read = new GuardedRead(type.ToString(), timeoutMs, logger);
        }

        public void Open()
        {
            Computer.Open();
            IsOpen = true;
            Computer.Accept(Visitor);   // Initial readings
        }

        public void Close()
        {
            IsOpen = false;
            Computer.Close();
        }
    }


    /// <summary>
    /// Sensor readings updater
    /// </summary>
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware)
                subHardware.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

#else
    // Sensors of one hwmon device (read together, with their own timeout)
    private sealed class HwmonGroup
    {
        public readonly GuardedRead Read;
        public readonly List<HostSensor> Sensors = new();

        public HwmonGroup(string name, ILogger logger)
        {
            Read = new GuardedRead(name, SensorManagerConstants.HwmonTimeoutMs, logger);
        }
    }

#endif


    ////////////////////////////////////////////////////////////////////////////////////////
    // Private properties

    // State & Synchronization
    private int _disposed = 0;

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly CancellationToken _lifetimeToken;

    private long _lastRequestTick = 0;
    private long _lastUpdateTick = 0;

    private readonly ILogger<HostSensorManager> _logger;

    private readonly List<HostDevice> _hostDevices = new();
    private readonly Dictionary<byte[], HostSensor> _hostSensors = new(new ByteArrayComparer());

    private volatile HostShortSensorDto[] _sensorsSnapshot = [];

    private Task? _hkTask = null;

#if WINDOWS
    // Fast and slow Computer Instances
    private readonly ComputerInstance[] _computerInstances;
#else
    // hwmon devices
    private readonly List<HwmonGroup> _hwmonGroups = new();
#endif


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction / Disposal

    /// <summary>
    /// Scans the host hardware for temperature sensors.
    /// </summary>
    /// <param name="autoUpdate">Poll the sensors periodically while they are requested.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public HostSensorManager(bool autoUpdate, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<HostSensorManager>();
        _lifetimeToken = _lifetimeCts.Token;

#if WINDOWS
        // Fast instance: everything except storage and controller
        var fastComputer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsNetworkEnabled = true,
            IsPsuEnabled = true,
            IsBatteryEnabled = true,
            IsPowerMonitorEnabled = true,
            IsControllerEnabled = false,    // explicitly excluded
            IsStorageEnabled = false        // explicitly excluded
        };

        // Slow instance: storage & controller only
        // It seems that when the file system stuck
        // (e.g., while flushing buffers or something),
        // access to the controller hardware gets blocked as well.
        // I don't have a reliable way to hang the file system
        // for testing purposes, but that is the impression I got from the tests.
        //
        // TODO: Run more reliable tests and decide on the algorithm and timeouts.
        var slowComputer = new Computer
        {
            // everything else is false by default
            IsStorageEnabled = true,
            IsControllerEnabled = true
        };

        _computerInstances = new[]
        {
            new ComputerInstance(
                fastComputer,
                SensorManagerConstants.FastHardwareTimeoutMs,
                ComputerType.Fast,
                _logger),
            new ComputerInstance(
                slowComputer,
                SensorManagerConstants.SlowHardwareTimeoutMs,
                ComputerType.Slow,
                _logger)
        };
#endif

        InitializeCMS();
        CollectSensors();

        if (autoUpdate)
            _hkTask = Task.Run(() => HouseKeeperLoop(_lifetimeToken));

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Sensor Manager object created.");

        // Show scan result in trace log
        PrintHardware();
        PrintSnapshots();
    }


    // Disposal
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _lifetimeCts.Cancel();

        try
        {
            _hkTask?.Wait(SensorManagerConstants.SlowHardwareTimeoutMs);
        }
        catch { /* the housekeeper handles its own errors */ }

        _hkTask = null;

        // Wait for a running update; the gate stays taken, so no update starts afterwards
        if (!_updateGate.Wait(SensorManagerConstants.SlowHardwareTimeoutMs) &&
            _logger.IsEnabled(LogLevel.Warning))
            _logger.LogWarning("Sensor update still running on disposal.");

        DeinitializeCMS();

        _lifetimeCts.Dispose();

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Sensor Manager object disposed.");
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Sensor management

    // Housekeeper loop
    private async Task HouseKeeperLoop(CancellationToken token)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Housekeeper started.");

        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(SensorManagerConstants.HouseKeepingPeriodMs));

        try
        {
            while (await timer.WaitForNextTickAsync(token))
                await TryUpdateAsync(periodical: true);
        }
        catch (OperationCanceledException) { /* normal shutdown */ }

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Housekeeper completed.");
    }


    // Init Computer Management System
    private void InitializeCMS()
    {
#if WINDOWS
        // TODO: Handle added/removed hardware (Computer.HardwareAdded / HardwareRemoved)
        foreach (var instance in _computerInstances)
        {
            try
            {
                instance.Open();
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "Error opening {Type} instance: {Exception}",
                        instance.Type,
                        ex.Message);

                try { instance.Close(); } catch { /* partially opened */ }
            }
        }
#endif
    }


    // Deinit Computer Management System
    private void DeinitializeCMS()
    {
#if WINDOWS
        foreach (var instance in _computerInstances)
        {
            if (!instance.IsOpen) continue;

            // Closing the hardware under a hung read could block as well
            if (!instance.Read.WaitForHungRead(instance.TimeoutMs))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning(
                        "{Type} instance is left open: its hardware read is still running.",
                        instance.Type);
                continue;
            }

            try
            {
                instance.Close();
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "Error closing {Type} instance: {Exception}",
                        instance.Type,
                        ex.Message);
            }
        }
#endif
    }


    // Request Signal
    private void SignalRequest()
    {
        Volatile.Write(ref _lastRequestTick, Environment.TickCount64);
        _ = TryUpdateAsync(periodical: false);
    }


    // Core Update Scheduler (never throws)
    private async Task TryUpdateAsync(bool periodical)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        long now = Environment.TickCount64;
        long lastRequest = Volatile.Read(ref _lastRequestTick);

        bool requestsActive = (lastRequest > 0) &&
            ((now - lastRequest) < SensorManagerConstants.RequestActivityPeriodMs);
        bool minIntervalPassed =
            (now - Volatile.Read(ref _lastUpdateTick)) >= SensorManagerConstants.MinUpdateIntervalMs;

        if (!minIntervalPassed ||
            (periodical && !requestsActive)) return;

        // Another update is running
        if (!_updateGate.Wait(0)) return;

        try
        {
            await UpdateReadingsAsync(_lifetimeToken);
            Volatile.Write(ref _lastUpdateTick, Environment.TickCount64);
        }
        catch (OperationCanceledException) { /* disposal */ }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError(ex, "Sensor update failed.");
        }
        finally
        {
            _updateGate.Release();
        }
    }


    // Applies a new reading (under _stateLock). Availability changes of a single sensor
    // are logged here; a failed/timed out group is logged by its GuardedRead.
    private void ApplyReading(HostSensor sensor, double? value, bool groupRead)
    {
        if (value.HasValue)
        {
            sensor.Temperature = value.Value;
            sensor.IsValid = true;

            if (sensor.ReportedUnavailable)
            {
                sensor.ReportedUnavailable = false;
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Sensor '{Name}' is available again.", sensor.Name);
            }
            return;
        }

        if (groupRead && sensor.IsValid)
        {
            sensor.ReportedUnavailable = true;
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(
                    "Sensor '{Name}' ({Path}) is unavailable.",
                    sensor.Name,
                    sensor.Path);
        }

        sensor.IsValid = false;
    }


#if WINDOWS
    // Parallel Instance Update: each instance with its own timeout
    private async Task UpdateReadingsAsync(CancellationToken token)
    {
        await Task.WhenAll(_computerInstances.Select(i => UpdateInstanceAsync(i, token)));

        lock (_stateLock) UpdateSensorStateSnapshot();
    }


    // Update single Computer instance
    private async Task UpdateInstanceAsync(ComputerInstance instance, CancellationToken token)
    {
        // Use the visitor to update ALL hardware and SubHardware recursively
        bool isInstanceValid = instance.IsOpen &&
            await instance.Read.RunAsync(() => instance.Computer.Accept(instance.Visitor), token);

        lock (_stateLock)
        {
            foreach (var (sensor, hwSensor) in instance.Sensors)
                ApplyReading(sensor, isInstanceValid ? hwSensor.Value : null, isInstanceValid);
        }
    }

#else
    // Parallel hwmon device update: each device with its own timeout
    private async Task UpdateReadingsAsync(CancellationToken token)
    {
        await Task.WhenAll(_hwmonGroups.Select(g => UpdateGroupAsync(g, token)));

        lock (_stateLock) UpdateSensorStateSnapshot();
    }


    // Update single hwmon device
    private async Task UpdateGroupAsync(HwmonGroup group, CancellationToken token)
    {
        var readings = new double?[group.Sensors.Count];

        bool isGroupValid = await group.Read.RunAsync(() =>
        {
            for (int i = 0; i < readings.Length; i++)
                readings[i] = ReadHwmonTemperature(group.Sensors[i].Path);
        }, token);

        lock (_stateLock)
        {
            for (int i = 0; i < readings.Length; i++)
                ApplyReading(group.Sensors[i], isGroupValid ? readings[i] : null, isGroupValid);
        }
    }

#endif


    // Single method for all operating systems
    private void CollectSensors()
    {
        lock (_stateLock)
        {
            _hostDevices.Clear();
            _hostSensors.Clear();

#if WINDOWS
            CollectSensorsWindows();
#else
            CollectSensorsLinux();
#endif

            UpdateSensorStateSnapshot();
        }
    }


#if WINDOWS
    // Collect from all instances and merge results transparently
    private void CollectSensorsWindows()
    {
        foreach (var instance in _computerInstances)
        {
            if (!instance.IsOpen) continue;

            foreach (IHardware hardware in instance.Computer.Hardware)
            {
                HostDevice? device = CollectDevice(hardware, instance);
                if (device is not null) _hostDevices.Add(device);
            }
        }
    }


    // Recursive traversal of the device tree
    private HostDevice? CollectDevice(IHardware hardware, ComputerInstance instance)
    {
        List<HostSensor> foundSensors = CollectTemperatureSensors(hardware.Sensors, instance);
        List<HostDevice> foundSubdevices = new();

        foreach (IHardware subHw in hardware.SubHardware)
        {
            HostDevice? subDev = CollectDevice(subHw, instance);
            if (subDev is not null) foundSubdevices.Add(subDev);
        }

        if ((foundSensors.Count > 0) || (foundSubdevices.Count > 0))
        {
            HostDevice currentDevice = new();
            currentDevice.Name = hardware.Name;
            currentDevice.Info = hardware.GetReport();
            currentDevice.Type = HardwareTypeToContract(hardware.HardwareType);
            currentDevice.Sensors.AddRange(foundSensors);
            currentDevice.SubDevices.AddRange(foundSubdevices);

            return currentDevice;
        }

        return null;
    }


    // Collect temperature sensors of one hardware item
    private List<HostSensor> CollectTemperatureSensors(ISensor[] sensors, ComputerInstance instance)
    {
        List<HostSensor> foundSensors = new();
        foreach (ISensor sensor in sensors)
        {
            if ((sensor.SensorType != SensorType.Temperature) ||
                !sensor.Value.HasValue) continue;

            string identifier = sensor.Identifier.ToString();
            byte[] id = Get8ByteFnvHash(GetStableSensorKey(sensor, identifier));

            // Guard against duplicate IDs across groups (should not happen in practice)
            if (_hostSensors.ContainsKey(id))
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "Duplicate sensor ID for '{Name}'.",
                        sensor.Name);
                continue;
            }

            // Create new sensor
            HostSensor newSensor = new HostSensor(id);
            newSensor.Name = sensor.Name;
            newSensor.IsValid = true;
            newSensor.Path = identifier;
            newSensor.Temperature = (double)sensor.Value.Value;
            newSensor.Info = string.Join(", ", sensor.Parameters.Select(p => $"{p.Name} = {p.Value}"));

            foreach (var p in sensor.Parameters)
            {
                if (p.Name.Contains("TjMax", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("TjunctionMax", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("TemperatureMax", StringComparison.OrdinalIgnoreCase))
                {
                    newSensor.CritTemperature = p.Value;
                    break;
                }
            }

            foundSensors.Add(newSensor);
            _hostSensors.Add(newSensor.ID, newSensor);
            instance.Sensors.Add((newSensor, sensor));
        }

        return foundSensors;
    }


    // Sensor ID source. LibreHardwareMonitor identifiers contain enumeration indexes
    // (/nvme/1/..., /gpu-nvidia/0/...) that change when devices are added or removed,
    // so disks and GPUs are identified by their own properties. Other hardware keeps
    // the LibreHardwareMonitor identifier (its index doesn't depend on other devices).
    private static string GetStableSensorKey(ISensor sensor, string identifier)
    {
        IHardware hardware = sensor.Hardware;

        string? hardwareKey = hardware switch
        {
            StorageDevice { Storage.SerialNumber: { } serial } storage when !string.IsNullOrWhiteSpace(serial)
                => $"storage:{storage.Storage.Model?.Trim()}:{serial.Trim()}",
            GenericGpu { DeviceId: { } deviceId } when !string.IsNullOrWhiteSpace(deviceId)
                => $"gpu:{deviceId.ToLowerInvariant()}",
            _ => null
        };

        if (hardwareKey is null)
            return identifier;

        // The sensor's own part of the identifier ("/temperature/N")
        string hardwareIdentifier = hardware.Identifier.ToString();
        string sensorPart = identifier.StartsWith(hardwareIdentifier, StringComparison.Ordinal)
            ? identifier[hardwareIdentifier.Length..]
            : identifier;

        return hardwareKey + sensorPart;
    }


    // LibreHardwareMonitor hardware type to contract
    private static HostHardwareType HardwareTypeToContract(HardwareType type)
    {
        return type switch
        {
            HardwareType.Cpu => HostHardwareType.CPU,
            HardwareType.Motherboard => HostHardwareType.Motherboard,
            HardwareType.Memory => HostHardwareType.Memory,
            HardwareType.GpuNvidia => HostHardwareType.GPU,
            HardwareType.GpuIntel => HostHardwareType.GPU,
            HardwareType.GpuAmd => HostHardwareType.GPU,
            HardwareType.Storage => HostHardwareType.HDD,
            HardwareType.SuperIO => HostHardwareType.Controller,
            HardwareType.EmbeddedController => HostHardwareType.Controller,
            HardwareType.Network => HostHardwareType.Network,
            HardwareType.Psu => HostHardwareType.PSU,
            HardwareType.PowerMonitor => HostHardwareType.PSU,
            HardwareType.Battery => HostHardwareType.Battery,
            HardwareType.Cooler => HostHardwareType.Cooler,
            _ => HostHardwareType.Unknown    // Safe fallback for unknown hardware types
        };
    }

#else
    // Collect from hwmon subsystem
    private void CollectSensorsLinux()
    {
        const string basePath = "/sys/class/hwmon";

        if (!Directory.Exists(basePath))
        {
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError("hwmon subsystem not found.");
            return;
        }

        string[] hwmonFolders;
        try
        {
            hwmonFolders = Directory
                .GetDirectories(basePath, "hwmon*", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.Length)
                .ThenBy(f => f, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError(
                    "Error reading '{Path}': {Exception}",
                    basePath,
                    ex.Message);
            return;
        }

        foreach (string folderPath in hwmonFolders)
        {
            try
            {
                CollectHwmonDevice(folderPath);
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "Error reading '{Path}': {Exception}",
                        folderPath,
                        ex.Message);
            }
        }
    }


    // Collect the temperature inputs of one hwmon device
    private void CollectHwmonDevice(string folderPath)
    {
        const string inputSuffix = "_input";

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace(
                "Parsing hwmon folder: {Path}",
                folderPath);

        string driverName = ReadSysfsText(Path.Combine(folderPath, "name")) is { Length: > 0 } name
            ? name
            : Path.GetFileName(folderPath);

        string? ownerPath = ResolveHwmonOwnerPath(folderPath);

        // temp1 ... temp9, temp10 ...
        string[] temperatureFiles = Directory
            .GetFiles(folderPath, "temp*" + inputSuffix, SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.Length)
            .ThenBy(f => f, StringComparer.Ordinal)
            .ToArray();

        HostDevice? device = null;
        HwmonGroup? group = null;

        foreach (string file in temperatureFiles)
        {
            double? temperature = ReadHwmonTemperature(file);
            if (!temperature.HasValue) continue;

            string filePrefix = file[..^inputSuffix.Length];       // ".../tempN"
            string channel = Path.GetFileName(filePrefix);          // "tempN"

            // Stable across reboots (hwmonN depends on the driver load order)
            byte[] id = Get8ByteFnvHash($"{driverName}|{ownerPath ?? folderPath}|{channel}");

            if (_hostSensors.ContainsKey(id))
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "Duplicate sensor ID for '{Path}'.",
                        file);
                continue;
            }

            // Create new sensor
            var newSensor = new HostSensor(id)
            {
                Name = ReadSysfsText(filePrefix + "_label") is { Length: > 0 } label
                    ? label
                    : ((temperatureFiles.Length > 1) ? $"{driverName} {channel}" : driverName),
                Path = file,
                Temperature = temperature.Value,
                MaxTemperature = ReadHwmonTemperature(filePrefix + "_max"),
                CritTemperature = ReadHwmonTemperature(filePrefix + "_crit"),
                IsValid = true
            };

            if ((device is null) || (group is null))
            {
                device = new HostDevice
                {
                    Name = driverName,
                    Info = $"Driver: {driverName}\nDevice: {ownerPath ?? "-"}\nhwmon: {folderPath}",
                    Type = HwmonDriverToContract(driverName)
                };
                group = new HwmonGroup(driverName, _logger);

                _hostDevices.Add(device);
                _hwmonGroups.Add(group);
            }

            device.Sensors.Add(newSensor);
            group.Sensors.Add(newSensor);
            _hostSensors.Add(newSensor.ID, newSensor);
        }
    }


    // The device owning a hwmon folder: the real sysfs path of hwmonN without its
    // "hwmon/hwmonN" tail (e.g. /sys/devices/pci0000:00/0000:00:18.3)
    private static string? ResolveHwmonOwnerPath(string folderPath)
    {
        try
        {
            string realPath = new DirectoryInfo(folderPath)
                .ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? folderPath;

            string? owner = Path.GetDirectoryName(realPath);
            if ((owner is not null) && (Path.GetFileName(owner) == "hwmon"))
                owner = Path.GetDirectoryName(owner);

            return owner;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }


    // Reads a short sysfs text file (null if missing or unreadable)
    private static string? ReadSysfsText(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }


    // Reads a hwmon temperature (millidegrees in the file)
    private static double? ReadHwmonTemperature(string path) =>
        long.TryParse(ReadSysfsText(path), out long millidegrees) ? millidegrees / 1000.0 : null;


    // hwmon driver name to contract (common drivers only)
    private static HostHardwareType HwmonDriverToContract(string driverName)
    {
        string name = driverName.ToLowerInvariant();

        return name switch
        {
            "coretemp" or "k10temp" or "k8temp" or "zenpower" or "cpu_thermal" or "via_cputemp"
                => HostHardwareType.CPU,
            "amdgpu" or "radeon" or "nouveau" or "i915" or "xe"
                => HostHardwareType.GPU,
            "nvme" or "drivetemp"
                => HostHardwareType.HDD,
            "jc42" or "spd5118"
                => HostHardwareType.Memory,
            "acpitz"
                => HostHardwareType.Motherboard,
            "dell_smm" or "thinkpad" or "applesmc"
                => HostHardwareType.Controller,
            _ when name.StartsWith("pch_")
                => HostHardwareType.Motherboard,
            _ when name.StartsWith("nct") || name.StartsWith("it8") ||
                   name.StartsWith("f71") || name.StartsWith("w83")
                => HostHardwareType.Controller,     // Super I/O
            _ when name.StartsWith("bat")
                => HostHardwareType.Battery,
            _ when name.StartsWith("iwlwifi") || name.StartsWith("r8169") || name.StartsWith("mt7")
                => HostHardwareType.Network,
            _ when name.StartsWith("corsairpsu")
                => HostHardwareType.PSU,
            _ => HostHardwareType.Unknown
        };
    }

#endif


    // Device list with the current readings
    private HostDeviceDto[] BuildDevicesSnapshot()
    {
        lock (_stateLock)
            return _hostDevices.Select(MapDeviceToDto).ToArray();
    }


    // Update the sensor readings snapshot for transmission to clients
    private void UpdateSensorStateSnapshot()
    {
        _sensorsSnapshot = _hostSensors.Values
            .Select(sensor => new HostShortSensorDto
            {
                Id = sensor.ID,
                Temperature = sensor.Temperature,
                IsValid = sensor.IsValid
            })
            .ToArray();

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Sensor readings snapshot updated.");
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Utilities

    // Get hash ID from string (path or other identifier)
    // (Fowler–Noll–Vo hash function:
    // https://en.wikipedia.org/wiki/Fowler%E2%80%93Noll%E2%80%93Vo_hash_function)
    private static byte[] Get8ByteFnvHash(string input)
    {
        const ulong fnvPrime = 0x00000100000001B3;
        ulong hash = 0xcbf29ce484222325;

        foreach (byte b in Encoding.UTF8.GetBytes(input))
        {
            hash ^= b;
            hash *= fnvPrime;
        }

        var id = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(id, hash);
        return id;
    }


    // HostDeviceDto from HostDevice construction
    private static HostDeviceDto MapDeviceToDto(HostDevice device)
    {
        // Convert Sensors List to array of records
        var sensorsSnapshot = device.Sensors
            .Select(sensor => new HostSensorDto()
            {
                Id = sensor.ID,
                Name = sensor.Name,
                Path = sensor.Path,
                Info = sensor.Info,
                Temperature = sensor.Temperature,
                MaxTemperature = sensor.MaxTemperature,
                CritTemperature = sensor.CritTemperature,
                IsValid = sensor.IsValid
            })
            .ToArray();

        // Recursive conversion
        var subDevicesSnapshot = device.SubDevices
            .Select(MapDeviceToDto)
            .ToArray();

        return new HostDeviceDto()
        {
            Name = device.Name,
            Info = device.Info,
            Type = device.Type,
            SubDevices = subDevicesSnapshot,
            Sensors = sensorsSnapshot
        };
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Public API

    /// <summary>
    /// Get host's device list (with the current readings)
    /// </summary>
    public HostDeviceDto[] GetHostDevices()
    {
        SignalRequest();
        return BuildDevicesSnapshot();
    }


    /// <summary>
    /// Get host's sensor list with readings
    /// </summary>
    public HostShortSensorDto[] GetHostSensorStates()
    {
        SignalRequest();
        return _sensorsSnapshot;
    }


    /// <summary>
    /// Get specified sensor readings (null if unknown or unavailable)
    /// </summary>
    public double? GetTemperatureForSensorID(byte[] id)
    {
        SignalRequest();

        lock (_stateLock)
        {
            return (_hostSensors.TryGetValue(id, out HostSensor? sensor) && sensor.IsValid)
                ? sensor.Temperature
                : null;
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Debug Helpers

    // Log Hardware list
    private void PrintHardware()
    {
        if (!_logger.IsEnabled(LogLevel.Debug)) return;

        var sb = new StringBuilder();
        sb.AppendLine("Device list:");
        PrintHardwareRecursive(sb, _hostDevices, 1);

        _logger.LogDebug("{Message}", sb.ToString());
    }


    // Append Device + Sensor list
    private static void PrintHardwareRecursive(StringBuilder sb, List<HostDevice> devices, int level)
    {
        foreach (var d in devices)
        {
            AppendIndented(sb, level, $"Device : {d.Name}");
            AppendIndented(sb, level + 1, $"Type   : {d.Type}");

            foreach (var s in d.Sensors)
            {
                AppendIndented(sb, level + 1, $"Sensor : {s.Name} [{Convert.ToHexString(s.ID)}]");
                AppendIndented(sb, level + 2, $"Path   : {s.Path}");
                AppendIndented(sb, level + 2, $"Temp   : {s.Temperature:F1} °C");

                if (s.MaxTemperature.HasValue)
                    AppendIndented(sb, level + 2, $"Max    : {s.MaxTemperature:F1} °C");

                if (s.CritTemperature.HasValue)
                    AppendIndented(sb, level + 2, $"Crit   : {s.CritTemperature:F1} °C");
            }

            PrintHardwareRecursive(sb, d.SubDevices, level + 1);
        }
    }


    // Indented append
    private static void AppendIndented(StringBuilder sb, int level, string text) =>
        sb.Append('\t', level).AppendLine(text);


    // Log snapshot records (devices/sensors and sensor readings)
    private void PrintSnapshots()
    {
        if (!_logger.IsEnabled(LogLevel.Trace)) return;

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        _logger.LogTrace("=== Devices Snapshot ===\n{Json}",
            JsonSerializer.Serialize(BuildDevicesSnapshot(), options));

        _logger.LogTrace("=== Sensors Snapshot ===\n{Json}",
            JsonSerializer.Serialize(_sensorsSnapshot, options));
    }
}

// END-OF-FILE
