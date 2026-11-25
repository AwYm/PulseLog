using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace HeartRateLogger;

public sealed record HeartRateMonitorInfo(
    string Name,
    ulong? Address,
    string? DeviceId,
    bool AdvertisesHeartRateService,
    short? SignalStrength,
    BluetoothAddressType AddressType = BluetoothAddressType.Unspecified)
{
    public string DisplayName
    {
        get
        {
            string kind = AdvertisesHeartRateService ? "Heart rate" : DeviceId is not null ? "Paired LE" : "Nearby LE";
            return SignalStrength is short signal
                ? $"{Name}   ·   {kind}   ·   {signal} dBm"
                : $"{Name}   ·   {kind}";
        }
    }

    public override string ToString() => DisplayName;
}

public sealed record HeartRateReading(
    DateTimeOffset Timestamp,
    int BeatsPerMinute,
    bool? SensorContact,
    int? EnergyExpended,
    IReadOnlyList<double> RrIntervalsMilliseconds);

public sealed class HeartRateMonitorClient : IDisposable
{
    private static readonly Guid HeartRateService = new("0000180d-0000-1000-8000-00805f9b34fb");
    private static readonly Guid HeartRateMeasurement = new("00002a37-0000-1000-8000-00805f9b34fb");
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(18);

    private readonly object gate = new();
    private CancellationTokenSource? pendingConnection;
    private MonitorConnection? startingConnection;
    private MonitorConnection? connection;
    private bool disposed;

    public event Action<HeartRateReading>? ReadingReceived;
    public event Action? Disconnected;

    public bool IsConnected
    {
        get
        {
            lock (gate)
                return connection is not null;
        }
    }

    public static async Task<IReadOnlyList<HeartRateMonitorInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        BluetoothAdapter adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (adapter is null || !adapter.IsLowEnergySupported)
            throw new InvalidOperationException("Windows couldn't find a Bluetooth Low Energy adapter. Check Bluetooth in Windows Settings.");

        var advertisements = new Dictionary<ulong, NearbyAdvertisement>();
        var stopped = new TaskCompletionSource<BluetoothError>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            AllowExtendedAdvertisements = adapter.IsExtendedAdvertisingSupported
        };
        watcher.Received += OnAdvertisement;
        watcher.Stopped += OnStopped;

        using var pairingDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IReadOnlyList<HeartRateMonitorInfo> paired;
        try
        {
            watcher.Start();
            pairingDeadline.CancelAfter(ScanDuration);
            Task<IReadOnlyList<HeartRateMonitorInfo>> pairedTask = FindPairedDevicesAsync(pairingDeadline.Token);
            Task scan = Task.Delay(ScanDuration, cancellationToken);
            if (await Task.WhenAny(scan, stopped.Task).ConfigureAwait(false) == stopped.Task)
            {
                BluetoothError error = await stopped.Task.ConfigureAwait(false);
                throw new InvalidOperationException(DescribeScanError(error));
            }

            await scan.ConfigureAwait(false);
            paired = await pairedTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            pairingDeadline.Cancel();
            watcher.Received -= OnAdvertisement;
            watcher.Stopped -= OnStopped;
            watcher.Stop();
        }

        Dictionary<ulong, HeartRateMonitorInfo> results;
        lock (advertisements)
        {
            results = advertisements
                .Where(entry => entry.Value.AdvertisesHeartRate || entry.Value.Connectable)
                .ToDictionary(entry => entry.Key, entry => entry.Value.ToMonitor(entry.Key));
        }

        var otherPairedDevices = new List<HeartRateMonitorInfo>();
        foreach (HeartRateMonitorInfo device in paired)
        {
            if (device.Address is not ulong address)
            {
                otherPairedDevices.Add(device);
                continue;
            }

            results[address] = results.TryGetValue(address, out HeartRateMonitorInfo? nearby)
                ? nearby with { Name = device.Name, DeviceId = device.DeviceId }
                : device;
        }

        return results.Values.Concat(otherPairedDevices)
            .OrderByDescending(device => device.AdvertisesHeartRateService)
            .ThenByDescending(device => device.DeviceId is not null)
            .ThenByDescending(device => device.SignalStrength)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        void OnAdvertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            ulong address = args.BluetoothAddress;
            if (address == 0)
                return;

            string name = args.Advertisement.LocalName;
            bool hasHeartRate = args.Advertisement.ServiceUuids.Contains(HeartRateService);
            lock (advertisements)
            {
                advertisements.TryGetValue(address, out NearbyAdvertisement? previous);
                advertisements[address] = new NearbyAdvertisement(
                    string.IsNullOrWhiteSpace(name) ? previous?.Name : name,
                    args.BluetoothAddressType,
                    args.RawSignalStrengthInDBm,
                    hasHeartRate || previous?.AdvertisesHeartRate == true,
                    args.IsConnectable || previous?.Connectable == true);
            }
        }

        void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
            => stopped.TrySetResult(args.Error);
    }

    public async Task ConnectAsync(HeartRateMonitorInfo monitor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = attempt.Token;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (connection is not null || pendingConnection is not null)
                throw new InvalidOperationException("Disconnect the current monitor before starting another connection.");

            pendingConnection = attempt;
            attempt.CancelAfter(ConnectionTimeout);
        }

        BluetoothLEDevice? device = null;
        GattDeviceService? service = null;
        MonitorConnection? candidate = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (monitor.DeviceId is string id)
                device = await BluetoothLEDevice.FromIdAsync(id).AsTask(token);

            if (device is null && monitor.Address is ulong address)
            {
                device = monitor.AddressType == BluetoothAddressType.Unspecified
                    ? await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(token)
                    : await BluetoothLEDevice.FromBluetoothAddressAsync(address, monitor.AddressType).AsTask(token);
            }

            if (device is null)
                throw new InvalidOperationException("Windows couldn't open this device. Wake the monitor and scan again.");

            GattCharacteristic measurement;
            (service, measurement) = await FindMeasurementAsync(device, token);
            candidate = new MonitorConnection(this, device, service, measurement);
            device = null;
            service = null;
            lock (gate)
            {
                CheckConnectionAttempt(attempt, token);
                // A notification can arrive before Windows finishes the subscription call.
                startingConnection = candidate;
            }
            candidate.AttachHandlers();

            var descriptor = measurement.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            GattCommunicationStatus status = await measurement.WriteClientCharacteristicConfigurationDescriptorAsync(descriptor)
                .AsTask(token);
            if (status != GattCommunicationStatus.Success)
                throw new InvalidOperationException(DescribeGattError(status, "subscribe to heart-rate readings"));

            lock (gate)
            {
                CheckConnectionAttempt(attempt, token);
                if (candidate.Device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                    throw new InvalidOperationException("The monitor disconnected during setup. Wake it and try again.");

                connection = candidate;
                startingConnection = null;
                pendingConnection = null;
                candidate = null;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && IsPendingConnection(attempt))
        {
            throw new TimeoutException("The monitor didn't respond in time. Keep it close and awake, then try again.");
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(pendingConnection, attempt))
                {
                    startingConnection = null;
                    pendingConnection = null;
                }
            }

            candidate?.Dispose();
            service?.Dispose();
            device?.Dispose();
        }
    }

    public void Disconnect() => ReleaseConnection(disposeClient: false);

    public void Dispose() => ReleaseConnection(disposeClient: true);

    internal static bool TryParseMeasurement(ReadOnlySpan<byte> packet, DateTimeOffset timestamp, out HeartRateReading reading)
    {
        reading = null!;
        if (packet.Length < 2)
            return false;

        byte flags = packet[0];
        int offset = 1;
        int bpm;
        if ((flags & 0x01) != 0)
        {
            if (packet.Length < offset + sizeof(ushort))
                return false;

            bpm = BinaryPrimitives.ReadUInt16LittleEndian(packet[offset..]);
            offset += sizeof(ushort);
        }
        else
        {
            bpm = packet[offset++];
        }

        bool? contact = (flags & 0x04) != 0 ? (flags & 0x02) != 0 : null;
        int? energy = null;
        if ((flags & 0x08) != 0)
        {
            if (packet.Length < offset + sizeof(ushort))
                return false;

            energy = BinaryPrimitives.ReadUInt16LittleEndian(packet[offset..]);
            offset += sizeof(ushort);
        }

        IReadOnlyList<double> intervals = Array.Empty<double>();
        if ((flags & 0x10) != 0)
        {
            int remaining = packet.Length - offset;
            if (remaining < sizeof(ushort) || remaining % sizeof(ushort) != 0)
                return false;

            var parsed = new double[remaining / sizeof(ushort)];
            for (int i = 0; i < parsed.Length; i++, offset += sizeof(ushort))
                parsed[i] = BinaryPrimitives.ReadUInt16LittleEndian(packet[offset..]) * (1000.0 / 1024.0);

            intervals = Array.AsReadOnly(parsed);
        }

        reading = new HeartRateReading(timestamp, bpm, contact, energy, intervals);
        return true;
    }

    private static async Task<IReadOnlyList<HeartRateMonitorInfo>> FindPairedDevicesAsync(CancellationToken token)
    {
        const string addressProperty = "System.Devices.Aep.DeviceAddress";
        try
        {
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                [addressProperty],
                DeviceInformationKind.AssociationEndpoint).AsTask(token).ConfigureAwait(false);

            return devices.Select(device =>
            {
                ulong? address = null;
                if (device.Properties.TryGetValue(addressProperty, out object? value) && value is string addressText &&
                    ulong.TryParse(addressText.Replace(":", "").Replace("-", ""), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out ulong parsedAddress))
                {
                    address = parsedAddress;
                }

                string name = string.IsNullOrWhiteSpace(device.Name) ? FallbackName(address) : device.Name;
                return new HeartRateMonitorInfo(name, address, device.Id, false, null);
            }).ToArray();
        }
        catch (Exception error) when (error is OperationCanceledException or UnauthorizedAccessException or COMException)
        {
            // The advertisement scan can still discover monitors when paired-device lookup is unavailable.
            return Array.Empty<HeartRateMonitorInfo>();
        }
    }

    private static async Task<(GattDeviceService Service, GattCharacteristic Measurement)> FindMeasurementAsync(
        BluetoothLEDevice device, CancellationToken token)
    {
        GattDeviceServicesResult result = await device.GetGattServicesForUuidAsync(HeartRateService, BluetoothCacheMode.Uncached)
            .AsTask(token).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException(DescribeGattError(result.Status, "read the device's Heart Rate Service"));

        IReadOnlyList<GattDeviceService> services = result.Services;
        int selectedService = -1;
        try
        {
            if (services.Count == 0)
                throw new InvalidOperationException("This device doesn't expose the standard Bluetooth Heart Rate Service.");

            GattCommunicationStatus status = GattCommunicationStatus.Success;
            for (int index = 0; index < services.Count; index++)
            {
                GattDeviceService service = services[index];
                GattCharacteristicsResult characteristics = await service.GetCharacteristicsForUuidAsync(
                    HeartRateMeasurement, BluetoothCacheMode.Uncached).AsTask(token).ConfigureAwait(false);
                if (characteristics.Status != GattCommunicationStatus.Success)
                {
                    status = characteristics.Status;
                    continue;
                }

                const GattCharacteristicProperties updates = GattCharacteristicProperties.Notify | GattCharacteristicProperties.Indicate;
                GattCharacteristic? measurement = characteristics.Characteristics
                    .FirstOrDefault(characteristic => (characteristic.CharacteristicProperties & updates) != 0);
                if (measurement is not null)
                {
                    selectedService = index;
                    return (service, measurement);
                }
            }

            if (status != GattCommunicationStatus.Success)
                throw new InvalidOperationException(DescribeGattError(status, "find the Heart Rate Measurement characteristic"));

            throw new InvalidOperationException("The device's Heart Rate Service doesn't send heart-rate updates.");
        }
        finally
        {
            for (int index = 0; index < services.Count; index++)
            {
                if (index != selectedService)
                    services[index].Dispose();
            }
        }
    }

    private static string DescribeScanError(BluetoothError error) => error switch
    {
        BluetoothError.RadioNotAvailable or BluetoothError.DisabledByUser => "Bluetooth is off or unavailable. Turn it on in Windows Settings.",
        BluetoothError.DisabledByPolicy or BluetoothError.ConsentRequired => "Windows is blocking Bluetooth scanning. Check Bluetooth permissions in Windows Settings.",
        _ => $"Windows stopped the Bluetooth scan ({error}). Try scanning again."
    };

    private static string DescribeGattError(GattCommunicationStatus status, string action) => status switch
    {
        GattCommunicationStatus.Unreachable => "The monitor is out of reach or busy. Wake it, keep it close, and disconnect it from other apps if needed.",
        GattCommunicationStatus.AccessDenied => "Windows denied access to the monitor. If it supports pairing, pair it in Windows Bluetooth Settings and try again.",
        _ => $"Windows couldn't {action} ({status})."
    };

    private static string FallbackName(ulong? address) => address is ulong value
        ? $"BLE device {value:X12}"
        : "Paired Bluetooth device";

    private bool IsPendingConnection(CancellationTokenSource attempt)
    {
        lock (gate)
            return ReferenceEquals(pendingConnection, attempt);
    }

    // Call while holding gate so a canceled attempt cannot publish a connection.
    private void CheckConnectionAttempt(CancellationTokenSource attempt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(pendingConnection, attempt))
            throw new OperationCanceledException("The connection was canceled.", token);
    }

    private void ReleaseConnection(bool disposeClient)
    {
        CancellationTokenSource? pending;
        MonitorConnection? active;
        lock (gate)
        {
            disposed |= disposeClient;
            pending = pendingConnection;
            pendingConnection = null;
            startingConnection = null;
            active = connection;
            connection = null;
        }

        try
        {
            pending?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The connection attempt completed before its cancellation was delivered.
        }
        finally
        {
            active?.Dispose();
        }
    }

    private void OnReading(MonitorConnection source, GattValueChangedEventArgs args)
    {
        lock (gate)
        {
            if (!ReferenceEquals(connection, source) && !ReferenceEquals(startingConnection, source))
                return;
        }

        using DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
        var packet = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(packet);
        if (TryParseMeasurement(packet, args.Timestamp, out HeartRateReading reading))
            ReadingReceived?.Invoke(reading);
    }

    private void OnConnectionStatusChanged(MonitorConnection source)
    {
        lock (gate)
        {
            if (!ReferenceEquals(connection, source) || source.Device.ConnectionStatus != BluetoothConnectionStatus.Disconnected)
                return;

            connection = null;
        }

        source.Dispose();
        Disconnected?.Invoke();
    }

    private sealed record NearbyAdvertisement(
        string? Name, BluetoothAddressType AddressType, short SignalStrength, bool AdvertisesHeartRate, bool Connectable)
    {
        public HeartRateMonitorInfo ToMonitor(ulong address) =>
            new(Name ?? FallbackName(address), address, null, AdvertisesHeartRate, SignalStrength, AddressType);
    }

    private sealed class MonitorConnection(
        HeartRateMonitorClient client, BluetoothLEDevice device, GattDeviceService service, GattCharacteristic measurement) : IDisposable
    {
        public BluetoothLEDevice Device { get; } = device;

        public void AttachHandlers()
        {
            Device.ConnectionStatusChanged += OnStatusChanged;
            measurement.ValueChanged += OnMeasurement;
        }

        public void Dispose()
        {
            measurement.ValueChanged -= OnMeasurement;
            Device.ConnectionStatusChanged -= OnStatusChanged;
            service.Dispose();
            Device.Dispose();
        }

        private void OnStatusChanged(BluetoothLEDevice sender, object args) => client.OnConnectionStatusChanged(this);

        private void OnMeasurement(GattCharacteristic sender, GattValueChangedEventArgs args) => client.OnReading(this, args);
    }
}
