using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SetupSwitch.Core;

namespace SetupSwitch.App;

/// <summary>Mostra por alguns segundos o número da tela no canto de cada monitor.</summary>
public sealed class IdentifyWindow : Window
{
    private const int SizeInPixels = 320;
    private const int MarginInPixels = 60;

    private readonly MonitorInfo _monitor;

    private IdentifyWindow(MonitorInfo monitor)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = new SolidColorBrush(Color.FromRgb(0, 120, 212));

        Content = new Viewbox
        {
            Margin = new Thickness(30),
            Child = new TextBlock { Text = monitor.Number.ToString(), Foreground = Brushes.White },
        };

        SourceInitialized += (_, _) => PlaceOnMonitor();
    }

    public static void ShowAll(IEnumerable<MonitorInfo> monitors)
    {
        var windows = monitors.Where(m => m.IsActive).Select(m => new IdentifyWindow(m)).ToList();
        windows.ForEach(w => w.Show());

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            windows.ForEach(w => w.Close());
        };
        timer.Start();
    }

    /// <summary>Posiciona em pixels reais, pois cada tela pode ter uma escala diferente.</summary>
    private void PlaceOnMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, IntPtr.Zero,
            _monitor.X + MarginInPixels, _monitor.Y + MarginInPixels,
            SizeInPixels, SizeInPixels, NoActivate | NoZOrder);
    }

    private const uint NoZOrder = 0x4;
    private const uint NoActivate = 0x10;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
