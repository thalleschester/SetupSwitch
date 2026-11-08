using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SetupSwitch.Audio;

namespace SetupSwitch.App;

/// <summary>
/// Aba Áudio: cartões dos dispositivos conectados, com volume, e abaixo os que já
/// passaram pelo PC, que podem receber apelido e entrar em perfis.
/// </summary>
public partial class AudioPanel : UserControl
{
    private const double RowHeight = 62;
    private const string StarOutline = "";
    private const string StarFilled = "";
    private const string Pencil = "";
    private const string Speaker = "";
    private const string SpeakerMuted = "";
    private const string Microphone = "";
    private const string MicrophoneMuted = "";

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons");

    private readonly Dictionary<string, VolumeControls> _volumes = [];

    private DisplayService? _service;
    private bool _editing;
    private bool _syncingVolume;

    public AudioPanel()
    {
        InitializeComponent();
    }

    public void Attach(DisplayService service)
    {
        _service = service;
        service.AudioChanged += Refresh;
        service.VolumeChanged += OnVolumeChanged;
        Refresh();
    }

    private void Refresh()
    {
        // Atualizar agora tiraria da tela o campo em que o apelido está sendo digitado.
        if (_editing || _service is null)
        {
            return;
        }

        _volumes.Clear();
        Fill(AudioFlow.Output, OutputPanel, OutputHistory, OutputHistoryPanel);
        Fill(AudioFlow.Input, InputPanel, InputHistory, InputHistoryPanel);
        _service.WatchVolumes(_volumes.Keys);
    }

    private void Fill(AudioFlow flow, Panel connectedPanel, Expander history, Panel historyPanel)
    {
        var devices = _service!.GetAudioDevices(flow, includeDisconnected: true);
        var connected = devices.Where(d => d.IsConnected).ToList();
        var past = devices.Where(d => !d.IsConnected).ToList();

        connectedPanel.Children.Clear();
        historyPanel.Children.Clear();

        if (connected.Count == 0)
        {
            connectedPanel.Children.Add(Hint("Nenhum dispositivo conectado."));
        }

        foreach (var device in connected)
        {
            connectedPanel.Children.Add(CreateRow(device));
        }

        foreach (var device in past)
        {
            historyPanel.Children.Add(CreateRow(device));
        }

        history.Header = $"Já conectados antes ({past.Count})";
        history.Visibility = past.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
    };

    private Grid CreateRow(AudioDevice device)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var select = CreateSelectButton(device);
        row.Children.Add(select);

        var favorite = _service!.IsFavorite(device);
        AddIconButton(row, 1, favorite ? StarFilled : StarOutline, favorite ? "Tirar dos favoritos" : "Favoritar", () =>
        {
            _service.ToggleFavorite(device);
            Refresh();
        });

        AddIconButton(row, 2, Pencil, "Dar apelido", () => BeginRename(row, select, device));

        if (device.IsConnected && CreateVolumeBar(device) is { } volume)
        {
            Grid.SetRow(volume, 1);
            Grid.SetColumnSpan(volume, 3);
            row.Children.Add(volume);
        }

        return row;
    }

    private Button CreateSelectButton(AudioDevice device)
    {
        var name = _service!.NameOf(device);
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });

        var description = Describe(device, name);
        if (description.Length > 0)
        {
            content.Children.Add(new TextBlock { Text = description, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        var button = new Button
        {
            Height = RowHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = content,
            // Um aparelho desconectado não pode virar o padrão, mas aceita apelido e favorito.
            IsEnabled = device.IsConnected,
        };

        if (device.IsDefault && device.IsConnected && TryFindResource("AccentButtonStyle") is Style accent)
        {
            button.Style = accent;
        }

        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => _service.SetAudioDefault(device);
        return button;
    }

    private static string Describe(AudioDevice device, string name)
    {
        var state = (device.IsConnected, device.IsDefault, device.IsDefaultCommunication) switch
        {
            (false, _, _) => "Desconectado",
            (true, true, true) => "Ativo",
            (true, true, false) => "Ativo, menos em chamadas",
            (true, false, true) => "Ativo só em chamadas",
            _ => "",
        };

        // Com apelido, o nome original continua visível para identificar o aparelho.
        var original = name == device.Name ? "" : device.Name;
        return string.Join(" · ", new[] { state, original }.Where(s => s.Length > 0));
    }

    private Grid? CreateVolumeBar(AudioDevice device)
    {
        if (_service!.GetVolume(device.Id) is not { } volume)
        {
            return null;
        }

        var bar = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

        var mute = new ToggleButton
        {
            Width = 44,
            Height = 34,
            Padding = new Thickness(0),
            Content = new TextBlock { FontFamily = IconFont, FontSize = 16 },
        };
        AutomationProperties.SetName(mute, $"Mudo {device.Name}");

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            SmallChange = 1,
            LargeChange = 10,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };
        AutomationProperties.SetName(slider, $"Volume {device.Name}");

        var percent = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };

        Grid.SetColumn(slider, 1);
        Grid.SetColumn(percent, 2);
        bar.Children.Add(mute);
        bar.Children.Add(slider);
        bar.Children.Add(percent);

        var controls = new VolumeControls(device.Flow, mute, slider, percent);
        _volumes[device.Id] = controls;
        Show(controls, volume);

        slider.ValueChanged += (_, e) =>
        {
            percent.Text = $"{Math.Round(e.NewValue)}%";
            if (!_syncingVolume)
            {
                _service.SetVolume(device.Id, (float)(e.NewValue / 100));
            }
        };

        // Checked e Unchecked cobrem mouse, teclado e leitores de tela; Click não cobre todos.
        void OnMuteToggled(object sender, RoutedEventArgs e)
        {
            if (_syncingVolume)
            {
                return;
            }

            var muted = mute.IsChecked == true;
            _service.SetMute(device.Id, muted);
            Show(controls, new AudioVolume((float)(slider.Value / 100), muted));
        }

        mute.Checked += OnMuteToggled;
        mute.Unchecked += OnMuteToggled;

        return bar;
    }

    /// <summary>Atualiza a barra sem gerar uma nova mudança de volume.</summary>
    private void Show(VolumeControls controls, AudioVolume volume)
    {
        _syncingVolume = true;
        try
        {
            controls.Slider.Value = Math.Round(volume.Level * 100);
            controls.Percent.Text = $"{Math.Round(volume.Level * 100)}%";
            controls.Mute.IsChecked = volume.Muted;
            controls.Mute.ToolTip = volume.Muted ? "Tirar do mudo" : "Colocar no mudo";
            ((TextBlock)controls.Mute.Content).Text = controls.Flow == AudioFlow.Output
                ? volume.Muted ? SpeakerMuted : Speaker
                : volume.Muted ? MicrophoneMuted : Microphone;
        }
        finally
        {
            _syncingVolume = false;
        }
    }

    private void OnVolumeChanged(string deviceId, AudioVolume volume)
    {
        // Durante o arraste a barra manda; o aviso que chega depois só repetiria o mesmo valor.
        if (_volumes.TryGetValue(deviceId, out var controls) && !controls.Slider.IsMouseCaptureWithin)
        {
            Show(controls, volume);
        }
    }

    private static void AddIconButton(Grid row, int column, string icon, string tooltip, Action action)
    {
        var button = new Button
        {
            Width = 44,
            Height = RowHeight,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(0),
            ToolTip = tooltip,
            Content = new TextBlock { Text = icon, FontFamily = IconFont, FontSize = 16 },
        };

        AutomationProperties.SetName(button, tooltip);
        Grid.SetColumn(button, column);
        button.Click += (_, _) => action();
        row.Children.Add(button);
    }

    private void BeginRename(Grid row, Button select, AudioDevice device)
    {
        _editing = true;

        var box = new TextBox
        {
            Text = _service!.NameOf(device),
            Height = RowHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(box, "Apelido");

        void Finish(bool save)
        {
            if (!_editing)
            {
                return;
            }

            _editing = false;
            if (save)
            {
                _service.SetAlias(device, box.Text);
            }

            Refresh();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Finish(save: e.Key == Key.Enter);
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Finish(save: true);

        row.Children.Remove(select);
        row.Children.Add(box);
        box.Focus();
        box.SelectAll();
    }

    private sealed record VolumeControls(AudioFlow Flow, ToggleButton Mute, Slider Slider, TextBlock Percent);
}
