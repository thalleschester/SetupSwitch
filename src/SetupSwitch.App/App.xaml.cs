using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SetupSwitch.App;

public partial class App : Application
{
    private const string InstanceName = @"Local\SetupSwitch.App";
    private const string ShowSignalName = @"Local\SetupSwitch.App.Show";

    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private DisplayService? _service;
    private HotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        _instance = new Mutex(initiallyOwned: true, InstanceName, out var isFirstInstance);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);

        if (!isFirstInstance)
        {
            // Abrir o programa de novo só traz a janela da instância que já está na bandeja.
            _showSignal.Set();
            Shutdown();
            return;
        }

        StartupRegistration.MigrateLegacyEntry();

        _service = new DisplayService();
        _service.Failed += message => MessageBox.Show(message, "SetupSwitch", MessageBoxButton.OK, MessageBoxImage.Warning);

        _hotkeys = new HotkeyManager(_service);
        _window = new MainWindow(_service, _hotkeys.Failed);
        _tray = new TrayIcon(_service, ShowMainWindow, ExitApplication);

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        WatchForShowSignal();

        if (!e.Args.Contains(StartupRegistration.MinimizedArgument))
        {
            ShowMainWindow();
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SetupSwitch");
        Directory.CreateDirectory(directory);

        var log = Path.Combine(directory, "erros.log");
        File.AppendAllText(log, $"{DateTime.Now:s} {e.Exception}{Environment.NewLine}");

        var message = $"Erro inesperado: {e.Exception.Message}{Environment.NewLine}Detalhes em {log}";
        MessageBox.Show(message, "SetupSwitch", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => _service?.NotifyChanged());

    private void WatchForShowSignal()
    {
        var signal = _showSignal!;
        var thread = new Thread(() =>
        {
            while (signal.WaitOne())
            {
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    private void ExitApplication()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _service?.Audio.Dispose();
        _window?.CloseForExit();
        Shutdown();
    }
}
