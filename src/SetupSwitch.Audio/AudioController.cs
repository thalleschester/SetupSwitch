using System.Runtime.InteropServices;
using SetupSwitch.Audio.Native;
using static SetupSwitch.Audio.Native.CoreAudioNative;

namespace SetupSwitch.Audio;

public enum AudioFlow
{
    Output,
    Input,
}

/// <summary>Um dispositivo de áudio. <see cref="Id"/> é estável entre reinícios e reconexões.</summary>
public sealed record AudioDevice(
    string Id, string Name, AudioFlow Flow, bool IsDefault, bool IsDefaultCommunication, bool IsConnected = true);

/// <summary>Volume de 0 a 1 e se está no mudo.</summary>
public sealed record AudioVolume(float Level, bool Muted);

/// <summary>Quem é o padrão em cada função, para poder voltar exatamente ao que era.</summary>
public sealed record AudioDefaults(string? Console, string? Multimedia, string? Communications);

public sealed class AudioException(string message) : Exception(message);

/// <summary>Lista dispositivos e define o padrão do Windows para saída e entrada.</summary>
public sealed class AudioController : IDisposable
{
    private const int ElementNotFound = unchecked((int)0x80070490);

    private static readonly Role[] Roles = [Role.Console, Role.Multimedia, Role.Communications];

    private readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
    private NotificationClient? _notifications;

    /// <summary>
    /// Um dispositivo entrou, saiu ou o padrão mudou. Chega em outra thread, e o Windows
    /// pede que a resposta a este aviso não consulte os dispositivos dentro do próprio aviso.
    /// </summary>
    public event Action? DevicesChanged;

    public void StartWatching()
    {
        if (_notifications is not null)
        {
            return;
        }

        _notifications = new NotificationClient(() => DevicesChanged?.Invoke());
        Check("Acompanhar dispositivos", _enumerator.RegisterEndpointNotificationCallback(_notifications));
    }

    public void Dispose()
    {
        WatchVolumes([]);

        if (_notifications is not null)
        {
            _enumerator.UnregisterEndpointNotificationCallback(_notifications);
            _notifications = null;
        }
    }

    private sealed class NotificationClient(Action changed) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string id, uint state) => Notify();

        public int OnDeviceAdded(string id) => Notify();

        public int OnDeviceRemoved(string id) => Notify();

        public int OnDefaultDeviceChanged(DataFlow flow, Role role, string? id) => Notify();

        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;

        private int Notify()
        {
            changed();
            return 0;
        }
    }

    /// <param name="includeDisconnected">
    /// Inclui tudo que já foi conectado ao PC. O Windows guarda esses registros, às vezes repetidos.
    /// </param>
    public IReadOnlyList<AudioDevice> GetDevices(AudioFlow flow, bool includeDisconnected = false)
    {
        var defaults = GetDefaults(flow);
        var mask = includeDisconnected ? DeviceStateAll : DeviceStateActive;

        Check("Listar dispositivos", _enumerator.EnumAudioEndpoints(ToDataFlow(flow), mask, out var collection));
        Check("Listar dispositivos", collection.GetCount(out var count));

        var devices = new List<AudioDevice>();
        for (uint index = 0; index < count; index++)
        {
            Check("Ler dispositivo", collection.Item(index, out var device));
            Check("Ler dispositivo", device.GetId(out var id));
            Check("Ler dispositivo", device.GetState(out var state));
            devices.Add(new AudioDevice(id, ReadName(device), flow, id == defaults.Console, id == defaults.Communications,
                state == DeviceStateActive));
        }

        return devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public AudioDefaults GetDefaults(AudioFlow flow) => new(
        GetDefaultId(flow, Role.Console),
        GetDefaultId(flow, Role.Multimedia),
        GetDefaultId(flow, Role.Communications));

    /// <summary>Torna o dispositivo padrão em todas as funções, inclusive a de chamadas.</summary>
    public void SetDefault(AudioDevice device)
    {
        foreach (var role in Roles)
        {
            SetRole(device.Id, role);
        }

        Verify(device.Flow, new AudioDefaults(device.Id, device.Id, device.Id));
    }

    public void Restore(AudioFlow flow, AudioDefaults defaults)
    {
        SetIfPresent(defaults.Console, Role.Console);
        SetIfPresent(defaults.Multimedia, Role.Multimedia);
        SetIfPresent(defaults.Communications, Role.Communications);
        Verify(flow, defaults);
    }

    private static void SetIfPresent(string? deviceId, Role role)
    {
        if (deviceId is not null)
        {
            SetRole(deviceId, role);
        }
    }

    private static void SetRole(string deviceId, Role role)
    {
        var result = PolicyConfig.SetDefaultEndpoint(deviceId, role);
        if (result != 0)
        {
            throw new AudioException($"O Windows recusou a troca do dispositivo (código 0x{result:X8}).");
        }
    }

    /// <summary>O Windows pode aceitar a chamada e não trocar; por isso o resultado é conferido.</summary>
    private void Verify(AudioFlow flow, AudioDefaults expected)
    {
        var actual = GetDefaults(flow);
        if (actual != expected)
        {
            throw new AudioException("O Windows aceitou o comando, mas o dispositivo padrão não mudou.");
        }
    }

    private string? GetDefaultId(AudioFlow flow, Role role)
    {
        var result = _enumerator.GetDefaultAudioEndpoint(ToDataFlow(flow), role, out var device);
        if (result == ElementNotFound)
        {
            return null;
        }

        Check("Ler dispositivo padrão", result);
        Check("Ler dispositivo padrão", device.GetId(out var id));
        return id;
    }

    private static string ReadName(IMMDevice device)
    {
        // Registros antigos às vezes não têm nome; isso não deve impedir a listagem.
        var key = FriendlyName;
        if (device.OpenPropertyStore(StorageRead, out var properties) != 0
            || properties.GetValue(ref key, out var value) != 0)
        {
            return "Dispositivo sem nome";
        }

        try
        {
            return value.Pointer == IntPtr.Zero
                ? "Dispositivo sem nome"
                : Marshal.PtrToStringUni(value.Pointer) ?? "Dispositivo sem nome";
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    /// <summary>
    /// Marca as mudanças de volume feitas por esta instância, para não reagir a elas mesmas.
    /// É única por instância: o app precisa enxergar mudanças feitas por outro processo.
    /// </summary>
    private readonly Guid _ownContext = Guid.NewGuid();

    private readonly Dictionary<string, (IAudioEndpointVolume Volume, VolumeCallback Callback)> _watched = [];

    /// <summary>O volume ou o mudo de um dispositivo mudou por fora do app, como pelas teclas de volume.</summary>
    public event Action<string, AudioVolume>? VolumeChanged;

    public AudioVolume GetVolume(string deviceId)
    {
        var volume = OpenVolume(deviceId);
        Check("Ler volume", volume.GetMasterVolumeLevelScalar(out var level));
        Check("Ler volume", volume.GetMute(out var muted));
        return new AudioVolume(level, muted);
    }

    /// <param name="level">De 0 a 1.</param>
    public void SetVolume(string deviceId, float level)
    {
        var context = _ownContext;
        Check("Mudar volume", OpenVolume(deviceId).SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref context));
    }

    public void SetMute(string deviceId, bool muted)
    {
        var context = _ownContext;
        Check("Mudar mudo", OpenVolume(deviceId).SetMute(muted, ref context));
    }

    /// <summary>Acompanha o volume só destes dispositivos; os anteriores deixam de ser acompanhados.</summary>
    public void WatchVolumes(IEnumerable<string> deviceIds)
    {
        var wanted = deviceIds.ToHashSet();

        foreach (var id in _watched.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            var (volume, callback) = _watched[id];
            volume.UnregisterControlChangeNotify(callback);
            _watched.Remove(id);
        }

        foreach (var id in wanted.Where(id => !_watched.ContainsKey(id)))
        {
            var volume = OpenVolume(id);
            var callback = new VolumeCallback(id, this);
            if (volume.RegisterControlChangeNotify(callback) == 0)
            {
                _watched[id] = (volume, callback);
            }
        }
    }

    private IAudioEndpointVolume OpenVolume(string deviceId)
    {
        if (_watched.TryGetValue(deviceId, out var watched))
        {
            return watched.Volume;
        }

        Check("Abrir dispositivo", _enumerator.GetDevice(deviceId, out var device));

        var interfaceId = AudioEndpointVolumeId;
        Check("Abrir controle de volume", device.Activate(ref interfaceId, ClsctxAll, IntPtr.Zero, out var volume));
        return (IAudioEndpointVolume)volume;
    }

    private sealed class VolumeCallback(string deviceId, AudioController owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data)
        {
            var context = Marshal.PtrToStructure<Guid>(data);
            if (context != owner._ownContext)
            {
                var muted = Marshal.ReadInt32(data, 16) != 0;
                var level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
                owner.VolumeChanged?.Invoke(deviceId, new AudioVolume(level, muted));
            }

            return 0;
        }
    }

    private static DataFlow ToDataFlow(AudioFlow flow) =>
        flow == AudioFlow.Output ? DataFlow.Render : DataFlow.Capture;

    private static void Check(string action, int result)
    {
        if (result != 0)
        {
            throw new AudioException($"{action}: erro do Windows 0x{result:X8}.");
        }
    }
}
