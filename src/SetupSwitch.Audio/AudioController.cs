using System.Runtime.InteropServices;
using SetupSwitch.Audio.Native;
using static SetupSwitch.Audio.Native.CoreAudioNative;

namespace SetupSwitch.Audio;

public enum AudioFlow
{
    Output,
    Input,
}

/// <summary>Um dispositivo de áudio conectado. <see cref="Id"/> é estável entre reinícios.</summary>
public sealed record AudioDevice(
    string Id, string Name, AudioFlow Flow, bool IsDefault, bool IsDefaultCommunication);

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

    /// <summary>Só dispositivos conectados: o Windows guarda muitos registros antigos e repetidos.</summary>
    public IReadOnlyList<AudioDevice> GetDevices(AudioFlow flow)
    {
        var defaults = GetDefaults(flow);

        Check("Listar dispositivos", _enumerator.EnumAudioEndpoints(ToDataFlow(flow), DeviceStateActive, out var collection));
        Check("Listar dispositivos", collection.GetCount(out var count));

        var devices = new List<AudioDevice>();
        for (uint index = 0; index < count; index++)
        {
            Check("Ler dispositivo", collection.Item(index, out var device));
            Check("Ler dispositivo", device.GetId(out var id));
            devices.Add(new AudioDevice(id, ReadName(device), flow, id == defaults.Console, id == defaults.Communications));
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
        Check("Ler nome do dispositivo", device.OpenPropertyStore(StorageRead, out var properties));

        var key = FriendlyName;
        Check("Ler nome do dispositivo", properties.GetValue(ref key, out var value));
        try
        {
            return Marshal.PtrToStringUni(value.Pointer) ?? "Dispositivo sem nome";
        }
        finally
        {
            PropVariantClear(ref value);
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
