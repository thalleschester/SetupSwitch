using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SetupSwitch.Audio;
using SetupSwitch.Core;

namespace SetupSwitch.App;

/// <summary>Aba Perfis: lista à esquerda e editor de combinações à direita.</summary>
public partial class ProfilesPanel : UserControl
{
    private const string KeepKey = "";
    private const string ExtendKey = "extend";
    private const string SavedKey = "saved";
    private const string OnlyPrefix = "only:";

    private DisplayService? _service;
    private string? _editing;

    public ProfilesPanel()
    {
        InitializeComponent();
    }

    public void Attach(DisplayService service)
    {
        _service = service;
        ApplyButton.Style = TryFindResource("AccentButtonStyle") as Style ?? ApplyButton.Style;

        service.AudioChanged += RefreshChoices;
        service.Warned += message => StatusText.Text = message;
        service.Changed += RefreshChoices;

        RefreshList();
        LoadEditor(null);
    }

    private void RefreshList(string? select = null)
    {
        var profiles = _service!.Controller.GetProfiles();

        ProfileList.Items.Clear();
        foreach (var profile in profiles)
        {
            var item = new ListBoxItem { Tag = profile, Content = CreateRow(profile) };
            AutomationProperties.SetName(item, profile.Name);
            ProfileList.Items.Add(item);

            if (profile.Name == select)
            {
                ProfileList.SelectedItem = item;
            }
        }

        EmptyHint.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private StackPanel CreateRow(ProfileInfo profile)
    {
        var parts = new[]
        {
            $"Telas: {DescribeScreens(profile)}",
            $"Saída: {NameOfDevice(profile.AudioOutputId, profile.AudioOutputName)}",
            $"Entrada: {NameOfDevice(profile.AudioInputId, profile.AudioInputName)}",
        };

        var row = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold });
        row.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", parts),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return row;
    }

    private string DescribeScreens(ProfileInfo profile) => profile.Screens switch
    {
        ProfileScreens.Only => _service!.Controller.GetMonitors().FirstOrDefault(m => m.DevicePath == profile.ScreenDevicePath)
            is { } monitor ? $"somente {_service.NameOf(monitor)}" : "tela desconectada",
        ProfileScreens.Extend => "estender",
        ProfileScreens.Saved => "layout salvo",
        _ => "não alterar",
    };

    private string NameOfDevice(string? id, string? name)
    {
        if (id is null)
        {
            return "não alterar";
        }

        return _service!.Settings.Aliases.TryGetValue(id, out var alias) ? alias : name ?? "desconhecido";
    }

    /// <summary>Remonta as opções sem perder o que está escolhido no editor.</summary>
    private void RefreshChoices()
    {
        var profile = (ProfileList.SelectedItem as ListBoxItem)?.Tag as ProfileInfo;

        FillScreens(ScreensBox, Selected(ScreensBox) ?? KeyOf(profile));
        FillDevices(OutputBox, AudioFlow.Output, Selected(OutputBox) ?? profile?.AudioOutputId ?? KeepKey, profile?.AudioOutputName);
        FillDevices(InputBox, AudioFlow.Input, Selected(InputBox) ?? profile?.AudioInputId ?? KeepKey, profile?.AudioInputName);
    }

    private static string KeyOf(ProfileInfo? profile) => profile?.Screens switch
    {
        ProfileScreens.Only => OnlyPrefix + profile.ScreenDevicePath,
        ProfileScreens.Extend => ExtendKey,
        ProfileScreens.Saved => SavedKey,
        _ => KeepKey,
    };

    private void FillScreens(ComboBox box, string selected)
    {
        var choices = new List<Choice> { new(KeepKey, "Não alterar") };
        choices.AddRange(_service!.Controller.GetMonitors()
            .Select(m => new Choice(OnlyPrefix + m.DevicePath, $"Somente {m.Number} - {_service.NameOf(m)}")));
        choices.Add(new Choice(ExtendKey, "Estender (todas as telas)"));
        choices.Add(new Choice(SavedKey, "Layout salvo no perfil"));

        Fill(box, choices, selected);
    }

    private void FillDevices(ComboBox box, AudioFlow flow, string selected, string? savedName)
    {
        var choices = new List<Choice> { new(KeepKey, "Não alterar") };
        choices.AddRange(_service!.GetAudioDevices(flow, includeDisconnected: true)
            .Select(d => new Choice(d.Id, d.IsConnected ? _service.NameOf(d) : $"{_service.NameOf(d)} (desconectado)")));

        // Um aparelho desligado continua na lista, para o perfil não perder a escolha.
        if (selected != KeepKey && choices.All(c => c.Key != selected))
        {
            choices.Add(new Choice(selected, $"{savedName ?? "Dispositivo"} (desconectado)"));
        }

        Fill(box, choices, selected);
    }

    private static void Fill(ComboBox box, List<Choice> choices, string selected)
    {
        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(c => c.Key == selected) ?? choices[0];
    }

    private static string? Selected(ComboBox box) => (box.SelectedItem as Choice)?.Key;

    private void LoadEditor(ProfileInfo? profile)
    {
        _editing = profile?.Name;

        EditorTitle.Text = profile is null ? "Novo perfil" : "Editar perfil";
        NameBox.Text = profile?.Name ?? "";
        RecaptureBox.IsChecked = false;
        StatusText.Text = "";

        ScreensBox.SelectedItem = null;
        OutputBox.SelectedItem = null;
        InputBox.SelectedItem = null;
        FillScreens(ScreensBox, KeyOf(profile));
        FillDevices(OutputBox, AudioFlow.Output, profile?.AudioOutputId ?? KeepKey, profile?.AudioOutputName);
        FillDevices(InputBox, AudioFlow.Input, profile?.AudioInputId ?? KeepKey, profile?.AudioInputName);

        UpdateButtons();
        UpdateRecapture();
    }

    private void UpdateButtons()
    {
        var hasName = NameBox.Text.Trim().Length > 0;
        SaveButton.IsEnabled = hasName;
        ApplyButton.IsEnabled = _editing is not null;
        DeleteButton.IsEnabled = _editing is not null;
    }

    private void UpdateRecapture()
    {
        // Em um perfil novo o layout sempre é guardado; a opção só faz sentido ao editar.
        var savedLayout = Selected(ScreensBox) == SavedKey;
        RecaptureBox.Visibility = savedLayout && _editing is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((ProfileList.SelectedItem as ListBoxItem)?.Tag is ProfileInfo profile)
        {
            LoadEditor(profile);
        }
    }

    private void OnScreensChanged(object sender, SelectionChangedEventArgs e) => UpdateRecapture();

    private void OnNameChanged(object sender, TextChangedEventArgs e) => UpdateButtons();

    private void OnNew(object sender, RoutedEventArgs e)
    {
        ProfileList.SelectedItem = null;
        LoadEditor(null);
        NameBox.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var profile = ReadEditor(name);
        var capture = _editing is null || RecaptureBox.IsChecked == true;

        _service!.Controller.SaveProfile(profile, capture, _editing);

        // O atalho do perfil acompanha o novo nome.
        var hotkeys = _service.Settings.Hotkeys;
        if (_editing is not null && _editing != name
            && hotkeys.Remove(HotkeyManager.ProfileActionId(_editing), out var gesture))
        {
            hotkeys[HotkeyManager.ProfileActionId(name)] = gesture;
            _service.Store.SaveSettings(_service.Settings);
        }

        _service.NotifyProfilesChanged();
        RefreshList(name);
        LoadEditor(profile);
        StatusText.Text = "Perfil salvo.";
    }

    private ProfileInfo ReadEditor(string name)
    {
        var screens = Selected(ScreensBox) ?? KeepKey;
        var mode = screens switch
        {
            ExtendKey => ProfileScreens.Extend,
            SavedKey => ProfileScreens.Saved,
            KeepKey => ProfileScreens.Keep,
            _ => ProfileScreens.Only,
        };

        var path = mode == ProfileScreens.Only ? screens[OnlyPrefix.Length..] : null;
        var (outputId, outputName) = ReadDevice(OutputBox, AudioFlow.Output);
        var (inputId, inputName) = ReadDevice(InputBox, AudioFlow.Input);

        return new ProfileInfo(name, mode, path, outputId, outputName, inputId, inputName);
    }

    private (string? Id, string? Name) ReadDevice(ComboBox box, AudioFlow flow)
    {
        var key = Selected(box);
        if (string.IsNullOrEmpty(key))
        {
            return (null, null);
        }

        // Guarda o nome real, sem apelido nem a marca de desconectado.
        var device = _service!.GetAudioDevices(flow, includeDisconnected: true).FirstOrDefault(d => d.Id == key);
        var previous = (ProfileList.SelectedItem as ListBoxItem)?.Tag as ProfileInfo;
        var saved = flow == AudioFlow.Output ? previous?.AudioOutputName : previous?.AudioInputName;
        return (key, device?.Name ?? saved);
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_editing is { } name)
        {
            await _service!.ApplyProfile(name);
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_editing is not { } name)
        {
            return;
        }

        var answer = MessageBox.Show($"Excluir o perfil \"{name}\"?", "SetupSwitch",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            _service!.Controller.DeleteProfile(name);
            if (_service.Settings.Hotkeys.Remove(HotkeyManager.ProfileActionId(name)))
            {
                _service.Store.SaveSettings(_service.Settings);
            }

            _service.NotifyProfilesChanged();
            RefreshList();
            LoadEditor(null);
        }
    }

    private sealed record Choice(string Key, string Label)
    {
        public override string ToString() => Label;
    }
}
