using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth.Advertisement;

namespace WinDrop.Ble;

/// <summary>
/// Sends the BLE advert that makes Apple devices switch AirDrop on (owly's
/// BLE-1). owly sends it through BlueZ; WSL has no Bluetooth, so the BlueZ
/// shim in WSL hands the bytes to us and Windows' own adapter sends them.
/// </summary>
public sealed class BleAdvertiser : IDisposable
{
    public const ushort AppleCompanyId = 0x004C;

    private BluetoothLEAdvertisementPublisher? _publisher;

    public event Action<string>? Log;

    public BluetoothLEAdvertisementPublisherStatus Status =>
        _publisher?.Status ?? BluetoothLEAdvertisementPublisherStatus.Created;

    /// <summary>Starts advertising <paramref name="data"/> as Apple manufacturer data, replacing any previous advert.</summary>
    public void Start(ushort companyId, byte[] data)
    {
        Stop();
        var adv = new BluetoothLEAdvertisement();
        adv.ManufacturerData.Add(new BluetoothLEManufacturerData(companyId, data.AsBuffer()));
        var publisher = new BluetoothLEAdvertisementPublisher(adv);
        publisher.StatusChanged += (_, e) =>
            Log?.Invoke($"BLE: {e.Status}{(e.Error != Windows.Devices.Bluetooth.BluetoothError.Success ? $" ({e.Error})" : "")}");
        publisher.Start();
        _publisher = publisher;
        Log?.Invoke($"BLE: adverteren {companyId:X4} {Convert.ToHexString(data)}");
    }

    public void Stop()
    {
        if (_publisher is null) return;
        if (_publisher.Status is BluetoothLEAdvertisementPublisherStatus.Started or BluetoothLEAdvertisementPublisherStatus.Waiting)
            _publisher.Stop();
        _publisher = null;
    }

    public void Dispose() => Stop();
}
