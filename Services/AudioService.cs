using System.Runtime.InteropServices;

namespace DynamicIsland;

/// <summary>Следит за громкостью устройства вывода по умолчанию (Core Audio).</summary>
[ComVisible(true)]
public sealed class AudioService : IMMNotificationClient, IAudioEndpointVolumeCallback
{
    const int ERender = 0, EMultimedia = 1, ClsctxAll = 23;

    IMMDeviceEnumerator? _enumerator;
    IAudioEndpointVolume? _volume;
    readonly object _sync = new();

    /// <summary>Уровень 0..1 и mute. Срабатывает в фоновом потоке.</summary>
    public event Action<float, bool>? Changed;

    public void Init()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _enumerator.RegisterEndpointNotificationCallback(this);
        Bind();
    }

    void Bind()
    {
        lock (_sync)
        {
            if (_volume != null)
            {
                try { _volume.UnregisterControlChangeNotify(this); } catch { }
                _volume = null;
            }
            if (_enumerator == null || _enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out var device) != 0 || device == null)
                return;

            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var obj) != 0) return;
            _volume = (IAudioEndpointVolume)obj;
            _volume.RegisterControlChangeNotify(this);
        }
    }

    int IAudioEndpointVolumeCallback.OnNotify(IntPtr data)
    {
        // AUDIO_VOLUME_NOTIFICATION_DATA: Guid (16) | BOOL bMuted (4) | float fMasterVolume (4) | ...
        var muted = Marshal.ReadInt32(data, 16) != 0;
        var level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
        Changed?.Invoke(level, muted);
        return 0;
    }

    int IMMNotificationClient.OnDefaultDeviceChanged(int flow, int role, string? id)
    {
        if (flow == ERender && role == EMultimedia) Task.Run(Bind);
        return 0;
    }

    int IMMNotificationClient.OnDeviceStateChanged(string? id, int state) => 0;
    int IMMNotificationClient.OnDeviceAdded(string? id) => 0;
    int IMMNotificationClient.OnDeviceRemoved(string? id) => 0;
    int IMMNotificationClient.OnPropertyValueChanged(string? id, PropertyKey key) => 0;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorComObject
{
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
}

[ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr data);
}

[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string? id, int state);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string? id);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string? id);
    [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string? id, PropertyKey key);
}

[StructLayout(LayoutKind.Sequential)]
public struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;
}
