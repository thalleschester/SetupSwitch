using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SetupSwitch.App;

/// <summary>Atalhos globais; funcionam mesmo com a janela fechada na bandeja.</summary>
public sealed class HotkeyManager : IDisposable
{
    public const int MaxMonitors = 9;

    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModNoRepeat = 0x4000;
    private const int MessageOnlyWindow = -3;

    private readonly HwndSource _window;
    private readonly Dictionary<int, Func<Task>> _actions = [];

    public HotkeyManager(DisplayService service)
    {
        _window = new HwndSource(new HwndSourceParameters("SetupSwitchHotkeys")
        {
            WindowStyle = 0,
            ParentWindow = new IntPtr(MessageOnlyWindow),
        });
        _window.AddHook(OnMessage);

        for (var number = 1; number <= MaxMonitors; number++)
        {
            var index = number - 1;
            Register($"Ctrl+Alt+{number}", '0' + (uint)number, () =>
            {
                var monitors = service.Controller.GetMonitors();
                return index < monitors.Count ? service.ShowOnly(monitors[index]) : Task.CompletedTask;
            });
        }

        Register("Ctrl+Alt+E", 'E', service.Extend);
        Register("Ctrl+Alt+0", '0', service.Undo);
    }

    /// <summary>Atalhos que outro programa já estava usando.</summary>
    public List<string> Failed { get; } = [];

    private void Register(string label, uint key, Func<Task> action)
    {
        var id = _actions.Count + 1;
        if (RegisterHotKey(_window.Handle, id, ModControl | ModAlt | ModNoRepeat, key))
        {
            _actions[id] = action;
        }
        else
        {
            Failed.Add(label);
        }
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            _ = action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(_window.Handle, id);
        }

        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
