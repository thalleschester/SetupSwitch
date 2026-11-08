using System.Windows.Threading;
using SetupSwitch.Audio;
using SetupSwitch.Core;

namespace SetupSwitch.App;

/// <summary>Ponto único para trocar as telas: uma troca por vez, sempre com a confirmação.</summary>
public sealed class DisplayService
{
    private const int AudioWaitAttempts = 10;

    private readonly DispatcherTimer _audioRefresh = new() { Interval = TimeSpan.FromMilliseconds(300) };

    private bool _busy;
    private ConfirmWindow? _confirmation;

    public DisplayService()
    {
        Store = new StateStore();
        Controller = new DisplayController(Store);
        Settings = Store.LoadSettings();

        // Uma troca gera vários avisos seguidos; o temporizador junta todos em uma atualização.
        var dispatcher = Dispatcher.CurrentDispatcher;
        _audioRefresh.Tick += (_, _) =>
        {
            _audioRefresh.Stop();
            AudioChanged?.Invoke();
        };
        Audio.DevicesChanged += () => dispatcher.BeginInvoke(() =>
        {
            _audioRefresh.Stop();
            _audioRefresh.Start();
        });
        Audio.VolumeChanged += (id, volume) => dispatcher.BeginInvoke(() => VolumeChanged?.Invoke(id, volume));
        Audio.StartWatching();
    }

    public StateStore Store { get; }
    public DisplayController Controller { get; }
    public AppSettings Settings { get; }
    public AudioController Audio { get; } = new();

    public bool IsBusy => _busy || _confirmation is not null;

    /// <summary>A configuração das telas mudou, por este app ou por fora dele.</summary>
    public event Action? Changed;

    public event Action<string>? Failed;

    /// <summary>
    /// Algo deu parcialmente certo. Não usa caixa de mensagem: perfis costumam ser
    /// aplicados por atalho, com a janela fechada, e uma caixa travaria tudo até ser fechada.
    /// </summary>
    public event Action<string>? Warned;

    /// <summary>Um dispositivo de áudio entrou, saiu ou o padrão mudou.</summary>
    public event Action? AudioChanged;

    /// <summary>O volume ou o mudo de um dispositivo mudou fora do app.</summary>
    public event Action<string, AudioVolume>? VolumeChanged;

    /// <summary>Um perfil foi criado, renomeado ou excluído.</summary>
    public event Action? ProfilesChanged;

    public void NotifyProfilesChanged() => ProfilesChanged?.Invoke();

    public void NotifyChanged()
    {
        if (!_busy)
        {
            Changed?.Invoke();
        }
    }

    public string NameOf(MonitorInfo monitor) =>
        Settings.Aliases.TryGetValue(monitor.DevicePath, out var alias) ? alias : monitor.Name;

    public void SetAlias(MonitorInfo monitor, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias) || alias.Trim() == monitor.Name)
        {
            Settings.Aliases.Remove(monitor.DevicePath);
        }
        else
        {
            Settings.Aliases[monitor.DevicePath] = alias.Trim();
        }

        Store.SaveSettings(Settings);
    }

    public string NameOf(AudioDevice device) =>
        Settings.Aliases.TryGetValue(device.Id, out var alias) ? alias : device.Name;

    public void SetAlias(AudioDevice device, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias) || alias.Trim() == device.Name)
        {
            Settings.Aliases.Remove(device.Id);
        }
        else
        {
            Settings.Aliases[device.Id] = alias.Trim();
        }

        Store.SaveSettings(Settings);
    }

    public bool IsFavorite(AudioDevice device) => Settings.AudioFavorites.Contains(device.Id);

    public void ToggleFavorite(AudioDevice device)
    {
        if (!Settings.AudioFavorites.Remove(device.Id))
        {
            Settings.AudioFavorites.Add(device.Id);
        }

        Store.SaveSettings(Settings);
    }

    /// <summary>Conectados primeiro, depois favoritos, depois por nome.</summary>
    /// <param name="includeDisconnected">
    /// Inclui o que já passou pelo PC. O Windows guarda um registro por porta ou driver, então
    /// os desconectados aparecem um por nome, e não aparecem se o mesmo nome estiver conectado.
    /// </param>
    public IReadOnlyList<AudioDevice> GetAudioDevices(AudioFlow flow, bool includeDisconnected = false)
    {
        try
        {
            var devices = Audio.GetDevices(flow, includeDisconnected);
            var connected = devices.Where(d => d.IsConnected).ToList();
            var connectedNames = connected.Select(d => d.Name).ToHashSet();

            var past = devices
                .Where(d => !d.IsConnected && !connectedNames.Contains(d.Name))
                .GroupBy(d => d.Name)
                .Select(group => group
                    .OrderByDescending(d => IsFavorite(d) || Settings.Aliases.ContainsKey(d.Id))
                    .First());

            return connected.Concat(past)
                .OrderByDescending(d => d.IsConnected)
                .ThenByDescending(IsFavorite)
                .ThenBy(d => NameOf(d), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (AudioException error)
        {
            Failed?.Invoke(error.Message);
            return [];
        }
    }

    public AudioVolume? GetVolume(string deviceId)
    {
        try
        {
            return Audio.GetVolume(deviceId);
        }
        catch (AudioException)
        {
            return null;
        }
    }

    public void SetVolume(string deviceId, float level) => TryAudio(() => Audio.SetVolume(deviceId, level));

    public void SetMute(string deviceId, bool muted) => TryAudio(() => Audio.SetMute(deviceId, muted));

    public void WatchVolumes(IEnumerable<string> deviceIds) => TryAudio(() => Audio.WatchVolumes(deviceIds));

    private void TryAudio(Action action)
    {
        try
        {
            action();
        }
        catch (AudioException error)
        {
            Failed?.Invoke(error.Message);
        }
    }

    /// <summary>Sem confirmação: a troca é instantânea e não deixa ninguém sem imagem.</summary>
    public void SetAudioDefault(AudioDevice device)
    {
        try
        {
            Audio.SetDefault(device);
        }
        catch (AudioException error)
        {
            Failed?.Invoke(error.Message);
        }
    }

    public void SetConfirmChanges(bool enabled)
    {
        Settings.ConfirmChanges = enabled;
        Store.SaveSettings(Settings);
    }

    public Task ShowOnly(MonitorInfo monitor) =>
        Run($"Somente {NameOf(monitor)}", () => Controller.ShowOnly(monitor));

    public Task ShowMonitors(IReadOnlyList<MonitorInfo> monitors) =>
        Run("Mudar telas ligadas", () => Controller.ShowMonitors(monitors));

    public Task Extend() => Run("Estender", () => Controller.Extend());

    public Task ApplySettings(IReadOnlyList<MonitorSettings> settings) =>
        Run("Aplicar alterações", () => Controller.ApplySettings(settings));

    /// <summary>Troca telas e depois áudio; a saída da TV pode demorar a aparecer após a troca.</summary>
    public async Task ApplyProfile(string name)
    {
        var profile = Controller.GetProfiles().FirstOrDefault(p => p.Name == name);
        if (IsBusy || profile is null)
        {
            return;
        }

        var outputBefore = Audio.GetDefaults(AudioFlow.Output);
        var inputBefore = Audio.GetDefaults(AudioFlow.Input);
        var screensChanged = false;
        var problems = new List<string>();

        var applied = await Execute(() =>
        {
            screensChanged = Controller.ApplyProfileScreens(name);
            ApplyProfileDevice(AudioFlow.Output, profile.AudioOutputId, profile.AudioOutputName, screensChanged, problems);
            ApplyProfileDevice(AudioFlow.Input, profile.AudioInputId, profile.AudioInputName, screensChanged, problems);
        });

        if (!applied)
        {
            return;
        }

        if (problems.Count > 0)
        {
            Warned?.Invoke($"Perfil {name} aplicado, com ressalvas:\n{string.Join("\n", problems)}");
        }

        // Só áudio não precisa de confirmação: não há risco de ficar sem imagem.
        if (!screensChanged || !Settings.ConfirmChanges || await Confirm($"Perfil {name}"))
        {
            return;
        }

        await Execute(() =>
        {
            Controller.Restore();
            if (profile.AudioOutputId is not null)
            {
                Audio.Restore(AudioFlow.Output, outputBefore);
            }

            if (profile.AudioInputId is not null)
            {
                Audio.Restore(AudioFlow.Input, inputBefore);
            }
        });
    }

    private void ApplyProfileDevice(AudioFlow flow, string? id, string? name, bool waitForDevice, List<string> problems)
    {
        if (id is null)
        {
            return;
        }

        var attempts = waitForDevice ? AudioWaitAttempts : 1;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            // O mesmo aparelho ganha outro registro em outra porta USB; o nome continua igual.
            var connected = Audio.GetDevices(flow);
            var match = connected.FirstOrDefault(d => d.Id == id)
                ?? connected.FirstOrDefault(d => name is not null && d.Name == name);

            if (match is { } device)
            {
                try
                {
                    Audio.SetDefault(device);
                }
                catch (AudioException error)
                {
                    problems.Add($"{device.Name}: {error.Message}");
                }

                return;
            }

            Thread.Sleep(300);
        }

        problems.Add($"{name ?? "Dispositivo"} não está conectado.");
    }

    /// <summary>Atalho de emergência: cancela a confirmação aberta ou desfaz a última troca.</summary>
    public async Task Undo()
    {
        if (_confirmation is not null)
        {
            _confirmation.Revert();
            return;
        }

        if (!_busy)
        {
            await Execute(() => Controller.Restore());
        }
    }

    private async Task Run(string label, Action change)
    {
        if (IsBusy || !await Execute(change))
        {
            return;
        }

        if (Settings.ConfirmChanges && !await Confirm(label))
        {
            await Execute(() => Controller.Restore());
        }
    }

    private async Task<bool> Confirm(string label)
    {
        _confirmation = new ConfirmWindow(label);
        var keep = await _confirmation.WaitForAnswer();
        _confirmation = null;
        return keep;
    }

    private async Task<bool> Execute(Action action)
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        try
        {
            await Task.Run(action);
            return true;
        }
        catch (Exception error) when (error is DisplayConfigException or AudioException)
        {
            Failed?.Invoke(error.Message);
            return false;
        }
        finally
        {
            _busy = false;
            Changed?.Invoke();
        }
    }
}
