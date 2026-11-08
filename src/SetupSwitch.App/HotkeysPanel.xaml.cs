using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SetupSwitch.App;

/// <summary>Lista de ações com o campo onde o usuário grava o atalho de cada uma.</summary>
public partial class HotkeysPanel : UserControl
{
    private HotkeyManager? _hotkeys;
    private bool _capturing;

    public HotkeysPanel()
    {
        InitializeComponent();
    }

    public void Attach(HotkeyManager hotkeys)
    {
        _hotkeys = hotkeys;
        hotkeys.BindingsChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        // Remontar no meio da gravação tiraria o foco do campo.
        if (_capturing || _hotkeys is null)
        {
            return;
        }

        Rows.Children.Clear();
        foreach (var binding in _hotkeys.Bindings)
        {
            Rows.Children.Add(CreateRow(binding));
        }
    }

    private Grid CreateRow(HotkeyBinding binding)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = binding.Action.Label, TextTrimming = TextTrimming.CharacterEllipsis });
        if (binding.InUseElsewhere)
        {
            var warning = new TextBlock { Text = "Em uso por outro programa", FontSize = 12 };
            warning.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
            label.Children.Add(warning);
        }

        var box = new TextBox
        {
            Text = HotkeyManager.Display(binding.Gesture),
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        AutomationProperties.SetName(box, $"Atalho {binding.Action.Label}");
        AutomationProperties.SetAutomationId(box, $"hotkey:{binding.Action.Id}");

        box.GotKeyboardFocus += (_, _) =>
        {
            _capturing = true;
            _hotkeys!.Suspend();
            box.Text = "Pressione as teclas...";
            StatusText.Text = "";
        };
        box.LostKeyboardFocus += (_, _) => EndCapture();
        box.PreviewKeyDown += (_, e) => OnCaptureKey(binding, box, e);

        var reset = new Button
        {
            Content = "Padrão",
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = !binding.IsDefault,
            ToolTip = binding.Action.DefaultGesture is null
                ? "Esta ação não tem atalho de fábrica"
                : $"Voltar para {HotkeyManager.Display(binding.Action.DefaultGesture)}",
        };
        AutomationProperties.SetName(reset, $"Padrão {binding.Action.Label}");
        reset.Click += (_, _) => _hotkeys!.Set(binding.Action.Id, null);

        Grid.SetColumn(box, 1);
        Grid.SetColumn(reset, 2);
        row.Children.Add(label);
        row.Children.Add(box);
        row.Children.Add(reset);
        return row;
    }

    private void OnCaptureKey(HotkeyBinding binding, TextBox box, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                Keyboard.ClearFocus();
                return;

            case Key.Back or Key.Delete when Keyboard.Modifiers == ModifierKeys.None:
                Save(binding, "");
                return;
        }

        if (HotkeyManager.FromKeys(Keyboard.Modifiers, key) is not { } gesture)
        {
            // Mostra o que já está pressionado enquanto falta a tecla principal.
            box.Text = Keyboard.Modifiers == ModifierKeys.None
                ? "Use Ctrl, Alt ou Windows junto"
                : HotkeyManager.Display(FormatModifiers(Keyboard.Modifiers)) + "+...";
            return;
        }

        var owner = _hotkeys!.Bindings.FirstOrDefault(b => b.Gesture == gesture && b.Action.Id != binding.Action.Id);
        if (owner is not null)
        {
            box.Text = "Pressione as teclas...";
            ShowStatus($"{HotkeyManager.Display(gesture)} já é o atalho de \"{owner.Action.Label}\".", caution: true);
            return;
        }

        Save(binding, gesture);
    }

    private static string FormatModifiers(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return string.Join("+", parts);
    }

    private void Save(HotkeyBinding binding, string gesture)
    {
        _hotkeys!.Set(binding.Action.Id, gesture);

        // Sair do campo encerra a gravação e reativa os atalhos, inclusive o novo.
        Keyboard.ClearFocus();

        var saved = _hotkeys.Bindings.FirstOrDefault(b => b.Action.Id == binding.Action.Id);
        if (saved?.InUseElsewhere == true)
        {
            ShowStatus($"{HotkeyManager.Display(gesture)} já está em uso por outro programa. Escolha outra combinação.", caution: true);
        }
        else
        {
            ShowStatus(gesture.Length == 0
                ? $"Atalho de \"{binding.Action.Label}\" removido."
                : $"Atalho de \"{binding.Action.Label}\": {HotkeyManager.Display(gesture)}.", caution: false);
        }
    }

    private void EndCapture()
    {
        if (!_capturing)
        {
            return;
        }

        _capturing = false;
        _hotkeys!.Resume();
    }

    private void ShowStatus(string text, bool caution)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty,
            caution ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush");
    }
}
