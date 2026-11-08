using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SetupSwitch.Audio;

namespace SetupSwitch.App;

/// <summary>Aba Áudio: um cartão por dispositivo conectado, com o ativo em destaque.</summary>
public partial class AudioPanel : UserControl
{
    private const double RowHeight = 62;
    private const string StarOutline = "";
    private const string StarFilled = "";
    private const string Pencil = "";

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons");

    private DisplayService? _service;
    private bool _editing;

    public AudioPanel()
    {
        InitializeComponent();
    }

    public void Attach(DisplayService service)
    {
        _service = service;
        service.AudioChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        // Atualizar agora tiraria da tela o campo em que o apelido está sendo digitado.
        if (_editing || _service is null)
        {
            return;
        }

        Fill(OutputPanel, AudioFlow.Output);
        Fill(InputPanel, AudioFlow.Input);
    }

    private void Fill(Panel panel, AudioFlow flow)
    {
        panel.Children.Clear();

        var devices = _service!.GetAudioDevices(flow);
        if (devices.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Nenhum dispositivo conectado.",
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            });
            return;
        }

        foreach (var device in devices)
        {
            panel.Children.Add(CreateRow(device));
        }
    }

    private Grid CreateRow(AudioDevice device)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var select = CreateSelectButton(device);
        row.Children.Add(select);

        var favorite = _service!.IsFavorite(device);
        AddIconButton(row, 1, favorite ? StarFilled : StarOutline, favorite ? "Tirar dos favoritos" : "Favoritar", () =>
        {
            _service.ToggleFavorite(device);
            Refresh();
        });

        AddIconButton(row, 2, Pencil, "Dar apelido", () => BeginRename(row, select, device));
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
        };

        if (device.IsDefault && TryFindResource("AccentButtonStyle") is Style accent)
        {
            button.Style = accent;
        }

        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => _service.SetAudioDefault(device);
        return button;
    }

    private static string Describe(AudioDevice device, string name)
    {
        var state = (device.IsDefault, device.IsDefaultCommunication) switch
        {
            (true, true) => "Ativo",
            (true, false) => "Ativo, menos em chamadas",
            (false, true) => "Ativo só em chamadas",
            _ => "",
        };

        // Com apelido, o nome original continua visível para identificar o aparelho.
        var original = name == device.Name ? "" : device.Name;
        return string.Join(" · ", new[] { state, original }.Where(s => s.Length > 0));
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
}
