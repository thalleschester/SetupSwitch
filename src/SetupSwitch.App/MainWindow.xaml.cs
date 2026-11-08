using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SetupSwitch.Core;

namespace SetupSwitch.App;

public partial class MainWindow : Window
{
    private const double CanvasPadding = 16;
    private const double InactiveStripHeight = 76;

    private static readonly (MonitorRotation Value, string Label)[] Rotations =
    [
        (MonitorRotation.Landscape, "Paisagem"),
        (MonitorRotation.Portrait, "Retrato"),
        (MonitorRotation.LandscapeFlipped, "Paisagem invertida"),
        (MonitorRotation.PortraitFlipped, "Retrato invertido"),
    ];

    private readonly DisplayService _service;
    private readonly Dictionary<MonitorDraft, Border> _visuals = [];

    private List<MonitorDraft> _drafts = [];
    private IReadOnlyList<DisplayMode> _modes = [];
    private MonitorDraft? _selected;
    private bool _dirty;
    private bool _filling;
    private bool _exiting;

    private double _scale = 1;
    private double _originX;
    private double _originY;

    private MonitorDraft? _dragged;
    private Point _dragStart;
    private int _dragStartX;
    private int _dragStartY;

    public MainWindow(DisplayService service, HotkeyManager hotkeys)
    {
        _service = service;
        InitializeComponent();

        ApplyButton.Style = TryFindResource("AccentButtonStyle") as Style ?? ApplyButton.Style;
        RotationBox.ItemsSource = Rotations.Select(r => r.Label).ToList();
        ConfirmBox.IsChecked = service.Settings.ConfirmChanges;
        StartupBox.IsChecked = StartupRegistration.IsEnabled;
        HotkeysTab.Attach(hotkeys);

        AudioTab.Attach(service);
        ProfilesTab.Attach(service);
        service.Changed += OnServiceChanged;
        Reload();
    }

    private bool IsMirrored
    {
        get
        {
            var active = _drafts.Where(d => d.Monitor.IsActive).ToList();
            return active.Count >= 2 && active.Select(d => d.Monitor.SourceId).Distinct().Count() == 1;
        }
    }

    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Fechar só esconde: a bandeja e os atalhos continuam ativos.
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnServiceChanged()
    {
        if (!_dirty && _dragged is null)
        {
            Reload();
        }
    }

    private void Reload()
    {
        var selectedPath = _selected?.Monitor.DevicePath;

        _drafts = _service.Controller.GetMonitors().Select(m => new MonitorDraft(m)).ToList();
        _selected = _drafts.FirstOrDefault(d => d.Monitor.DevicePath == selectedPath)
            ?? _drafts.FirstOrDefault(d => d.IsPrimary)
            ?? _drafts.FirstOrDefault();

        SetDirty(false);
        BuildQuickActions();
        Redraw();
        FillDetails();
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        ApplyButton.IsEnabled = dirty;
        DiscardButton.IsEnabled = dirty;
        StatusText.Text = dirty ? "Há alterações que ainda não foram aplicadas." : "";
    }

    private void BuildQuickActions()
    {
        QuickPanel.Children.Clear();

        var active = _drafts.Where(d => d.Monitor.IsActive).ToList();
        var extended = active.Count == _drafts.Count && _drafts.Count > 1 && !IsMirrored;

        foreach (var draft in _drafts)
        {
            var monitor = draft.Monitor;
            var current = active.Count == 1 && monitor.IsActive;
            AddQuickAction($"Somente {monitor.Number}", _service.NameOf(monitor), current, () => _service.ShowOnly(monitor));
        }

        AddQuickAction("Estender", "Todas as telas", extended, _service.Extend);
    }

    private void AddQuickAction(string title, string subtitle, bool current, Func<Task> action)
    {
        var button = new Button
        {
            Width = 170,
            Height = 60,
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = subtitle, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            },
        };

        if (current && TryFindResource("AccentButtonStyle") is Style accent)
        {
            button.Style = accent;
        }

        AutomationProperties.SetName(button, title);
        button.Click += async (_, _) => await action();
        QuickPanel.Children.Add(button);
    }

    private void Redraw()
    {
        LayoutCanvas.Children.Clear();
        _visuals.Clear();

        var width = LayoutCanvas.ActualWidth;
        var height = LayoutCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var active = _drafts.Where(d => d.Monitor.IsActive).ToList();
        var inactive = _drafts.Where(d => !d.Monitor.IsActive).ToList();
        var areaHeight = height - (inactive.Count > 0 ? InactiveStripHeight : 0);

        ComputeScale(active, width, areaHeight);

        foreach (var draft in active)
        {
            var visual = CreateVisual(draft);
            LayoutCanvas.Children.Add(visual);
            _visuals[draft] = visual;
            PlaceVisual(draft);
        }

        var left = CanvasPadding;
        foreach (var draft in inactive)
        {
            var visual = CreateVisual(draft);
            visual.Width = 130;
            visual.Height = InactiveStripHeight - 16;
            visual.Opacity = 0.6;
            Canvas.SetLeft(visual, left);
            Canvas.SetTop(visual, height - InactiveStripHeight + 8);
            LayoutCanvas.Children.Add(visual);
            _visuals[draft] = visual;
            left += 142;
        }

        UpdateHint(active.Count);
        UpdateSelection();
    }

    private void ComputeScale(List<MonitorDraft> active, double width, double height)
    {
        if (active.Count == 0)
        {
            return;
        }

        var left = active.Min(d => d.X);
        var top = active.Min(d => d.Y);
        var right = active.Max(d => d.X + d.Width);
        var bottom = active.Max(d => d.Y + d.Height);

        var usableWidth = width - (2 * CanvasPadding);
        var usableHeight = height - (2 * CanvasPadding);
        _scale = Math.Min(usableWidth / (right - left), usableHeight / (bottom - top));

        _originX = ((width - ((right - left) * _scale)) / 2) - (left * _scale);
        _originY = ((height - ((bottom - top) * _scale)) / 2) - (top * _scale);
    }

    private Border CreateVisual(MonitorDraft draft)
    {
        var monitor = draft.Monitor;
        var content = new Grid();
        content.Children.Add(new TextBlock
        {
            Text = monitor.Number.ToString(),
            FontSize = monitor.IsActive ? 30 : 20,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = monitor.IsActive ? VerticalAlignment.Center : VerticalAlignment.Top,
            Margin = new Thickness(0, monitor.IsActive ? 0 : 4, 0, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = _service.NameOf(monitor) + (draft.IsPrimary ? " (principal)" : ""),
            FontSize = 12,
            Margin = new Thickness(6, 0, 6, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        });

        var visual = new Border { Child = content, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2) };
        visual.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        visual.MouseLeftButtonDown += (_, e) => OnVisualPressed(draft, visual, e);
        visual.MouseMove += (_, e) => OnVisualMoved(draft, e);
        visual.MouseLeftButtonUp += (_, _) => OnVisualReleased(visual);
        return visual;
    }

    private void PlaceVisual(MonitorDraft draft)
    {
        var visual = _visuals[draft];
        visual.Width = draft.Width * _scale;
        visual.Height = draft.Height * _scale;
        Canvas.SetLeft(visual, _originX + (draft.X * _scale));
        Canvas.SetTop(visual, _originY + (draft.Y * _scale));
    }

    private void UpdateSelection()
    {
        foreach (var (draft, visual) in _visuals)
        {
            visual.SetResourceReference(Border.BorderBrushProperty,
                draft == _selected ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush");
        }
    }

    private void UpdateHint(int activeCount) => LayoutHint.Text =
        IsMirrored ? "As telas estão replicadas. Use Estender para organizá-las."
        : activeCount > 1 ? "Arraste as telas para reorganizar."
        : "Só uma tela ligada. Ligue outra para organizar.";

    private void OnVisualPressed(MonitorDraft draft, Border visual, MouseButtonEventArgs e)
    {
        SelectDraft(draft);

        var canDrag = draft.Monitor.IsActive && !IsMirrored && _drafts.Count(d => d.Monitor.IsActive) > 1;
        if (canDrag)
        {
            _dragged = draft;
            _dragStart = e.GetPosition(LayoutCanvas);
            _dragStartX = draft.X;
            _dragStartY = draft.Y;
            visual.CaptureMouse();
        }
    }

    private void OnVisualMoved(MonitorDraft draft, MouseEventArgs e)
    {
        if (_dragged != draft)
        {
            return;
        }

        var position = e.GetPosition(LayoutCanvas);
        draft.X = _dragStartX + (int)((position.X - _dragStart.X) / _scale);
        draft.Y = _dragStartY + (int)((position.Y - _dragStart.Y) / _scale);
        PlaceVisual(draft);
    }

    private void OnVisualReleased(Border visual)
    {
        if (_dragged is not { } draft)
        {
            return;
        }

        visual.ReleaseMouseCapture();
        _dragged = null;

        var moved = draft.X != _dragStartX || draft.Y != _dragStartY;
        if (moved)
        {
            SnapDraft(draft);
            SetDirty(true);
            Redraw();
        }
    }

    private void SnapDraft(MonitorDraft draft)
    {
        var others = _drafts
            .Where(d => d != draft && d.Monitor.IsActive)
            .Select(d => d.Bounds)
            .ToList();

        var snapped = LayoutSnapper.Snap(draft.Bounds, others);
        draft.X = snapped.X;
        draft.Y = snapped.Y;
    }

    private void SelectDraft(MonitorDraft draft)
    {
        _selected = draft;
        UpdateSelection();
        FillDetails();
    }

    private void FillDetails()
    {
        _filling = true;
        try
        {
            DetailsPanel.Visibility = _selected is null ? Visibility.Collapsed : Visibility.Visible;
            if (_selected is not { } draft)
            {
                return;
            }

            var monitor = draft.Monitor;
            var mirrored = IsMirrored;

            DetailsTitle.Text = $"{monitor.Number}. {_service.NameOf(monitor)}";
            DetailsInfo.Text = monitor.IsActive
                ? $"{monitor.Name}, {monitor.Connection}"
                : $"{monitor.Name}, {monitor.Connection}. Tela desligada.";

            AliasBox.Text = _service.NameOf(monitor);
            NumberBox.ItemsSource = Enumerable.Range(1, _drafts.Count).ToList();
            NumberBox.SelectedItem = monitor.Number;

            ActiveOptions.Visibility = monitor.IsActive ? Visibility.Visible : Visibility.Collapsed;
            ActiveOptions.IsEnabled = !mirrored;

            var activeCount = _drafts.Count(d => d.Monitor.IsActive);
            ToggleButton.Content = monitor.IsActive ? "Desligar esta tela" : "Ligar esta tela";
            ToggleButton.IsEnabled = !mirrored && !(monitor.IsActive && activeCount == 1);

            if (monitor.IsActive)
            {
                _modes = _service.Controller.GetModes(monitor);
                RotationBox.SelectedIndex = Array.FindIndex(Rotations, r => r.Value == draft.Rotation);
                PrimaryBox.IsChecked = draft.IsPrimary;
                PrimaryBox.IsEnabled = !draft.IsPrimary;
                FillResolutions(draft);
            }
        }
        finally
        {
            _filling = false;
        }
    }

    private void FillResolutions(MonitorDraft draft)
    {
        var resolutions = _modes.Select(m => (m.Width, m.Height)).Distinct().ToList();
        if (!resolutions.Contains((draft.LandscapeWidth, draft.LandscapeHeight)))
        {
            resolutions.Insert(0, (draft.LandscapeWidth, draft.LandscapeHeight));
        }

        ResolutionBox.ItemsSource = resolutions.Select(r => new ResolutionItem(r.Width, r.Height)).ToList();
        ResolutionBox.SelectedItem = new ResolutionItem(draft.LandscapeWidth, draft.LandscapeHeight);
        FillRefreshRates(draft);
    }

    private void FillRefreshRates(MonitorDraft draft)
    {
        var rates = _modes
            .Where(m => m.Width == draft.LandscapeWidth && m.Height == draft.LandscapeHeight)
            .Select(m => m.RefreshRate)
            .Distinct()
            .ToList();

        var current = (int)Math.Round(draft.RefreshRate);
        if (!rates.Contains(current))
        {
            rates.Insert(0, current);
        }

        RefreshBox.ItemsSource = rates.Select(r => new RefreshItem(r)).ToList();
        RefreshBox.SelectedItem = new RefreshItem(current);
    }

    private void OnResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is not { } draft || ResolutionBox.SelectedItem is not ResolutionItem item)
        {
            return;
        }

        draft.LandscapeWidth = item.Width;
        draft.LandscapeHeight = item.Height;

        var rates = _modes.Where(m => m.Width == item.Width && m.Height == item.Height).Select(m => m.RefreshRate).ToList();
        if (rates.Count > 0 && !rates.Contains((int)Math.Round(draft.RefreshRate)))
        {
            draft.RefreshRate = rates.Max();
        }

        OnDraftResized(draft);
    }

    private void OnRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is not { } draft || RefreshBox.SelectedItem is not RefreshItem item)
        {
            return;
        }

        draft.RefreshRate = item.Rate;
        SetDirty(true);
    }

    private void OnRotationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is not { } draft || RotationBox.SelectedIndex < 0)
        {
            return;
        }

        draft.Rotation = Rotations[RotationBox.SelectedIndex].Value;
        OnDraftResized(draft);
    }

    private void OnDraftResized(MonitorDraft draft)
    {
        SnapDraft(draft);
        SetDirty(true);
        Redraw();

        _filling = true;
        FillRefreshRates(draft);
        _filling = false;
    }

    private void OnPrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } draft)
        {
            return;
        }

        _drafts.ForEach(d => d.IsPrimary = d == draft);
        PrimaryBox.IsEnabled = false;
        SetDirty(true);
        Redraw();
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        var primary = _drafts.First(d => d.IsPrimary);
        var settings = _drafts
            .Where(d => d.Monitor.IsActive)
            .Select(d => d.ToSettings(primary.X, primary.Y))
            .ToList();

        SetDirty(false);
        await _service.ApplySettings(settings);
    }

    private void OnDiscard(object sender, RoutedEventArgs e) => Reload();

    private async void OnToggleMonitor(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } draft)
        {
            return;
        }

        var monitors = _drafts
            .Where(d => d == draft ? !d.Monitor.IsActive : d.Monitor.IsActive)
            .Select(d => d.Monitor)
            .ToList();

        SetDirty(false);
        await _service.ShowMonitors(monitors);
    }

    private void OnIdentify(object sender, RoutedEventArgs e) =>
        IdentifyWindow.ShowAll(_drafts.Select(d => d.Monitor));

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void OnAliasCommitted(object sender, RoutedEventArgs e) => SaveAlias();

    private void OnAliasKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveAlias();
        }
    }

    private void SaveAlias()
    {
        if (_selected is not { } draft || AliasBox.Text == _service.NameOf(draft.Monitor))
        {
            return;
        }

        _service.SetAlias(draft.Monitor, AliasBox.Text);
        BuildQuickActions();
        Redraw();
        FillDetails();
    }

    private void OnNumberChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is not { } draft || NumberBox.SelectedItem is not int number)
        {
            return;
        }

        var order = _drafts.Select(d => d.Monitor.DevicePath).ToList();
        order.Remove(draft.Monitor.DevicePath);
        order.Insert(number - 1, draft.Monitor.DevicePath);
        _service.Store.SaveNumbering(order);

        if (_dirty)
        {
            StatusText.Text = "Número salvo. As outras alterações continuam pendentes.";
        }
        else
        {
            Reload();
        }
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e) =>
        _service.SetConfirmChanges(ConfirmBox.IsChecked == true);

    private void OnStartupClicked(object sender, RoutedEventArgs e) =>
        StartupRegistration.Set(StartupBox.IsChecked == true);

    private sealed record ResolutionItem(int Width, int Height)
    {
        public override string ToString() => $"{Width} × {Height}";
    }

    private sealed record RefreshItem(int Rate)
    {
        public override string ToString() => $"{Rate} Hz";
    }
}
