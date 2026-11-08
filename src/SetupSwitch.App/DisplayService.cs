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

    /// <summary>Um dispositivo de áudio entrou, saiu ou o padrão mudou.</summary>
    public event Action? AudioChanged;

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

    /// <summary>Dispositivos conectados, com os favoritos primeiro.</summary>
    public IReadOnlyList<AudioDevice> GetAudioDevices(AudioFlow flow)
    {
        try
        {
            return Audio.GetDevices(flow).OrderByDescending(IsFavorite).ToList();
        }
        catch (AudioException error)
        {
            Failed?.Invoke(error.Message);
            return [];
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
            Failed?.Invoke($"Perfil {name} aplicado, com ressalvas:\n{string.Join("\n", problems)}");
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
            if (Audio.GetDevices(flow).FirstOrDefault(d => d.Id == id) is { } device)
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
