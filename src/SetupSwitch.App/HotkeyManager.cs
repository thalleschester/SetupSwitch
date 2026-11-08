using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace SetupSwitch.App;

/// <summary>Uma ação que pode ter atalho, com o atalho que vem de fábrica.</summary>
public sealed record HotkeyAction(string Id, string Label, string? DefaultGesture, Func<Task> Run);

/// <summary>Estado de um atalho: o que está valendo e se o Windows aceitou registrar.</summary>
public sealed record HotkeyBinding(HotkeyAction Action, string? Gesture, bool IsDefault, bool InUseElsewhere);

/// <summary>
/// Atalhos globais personalizáveis; funcionam mesmo com a janela fechada na bandeja.
/// Os atalhos escolhidos ficam em AppSettings.Hotkeys, pelo ID da ação; texto vazio desliga o atalho.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;
    private const uint ModNoRepeat = 0x4000;
    private const int MessageOnlyWindow = -3;

    private readonly DisplayService _service;
    private readonly HwndSource _window;
    private readonly Dictionary<int, Func<Task>> _registered = [];
    private bool _suspended;

    public HotkeyManager(DisplayService service)
    {
        _service = service;
        _window = new HwndSource(new HwndSourceParameters("SetupSwitchHotkeys")
        {
            WindowStyle = 0,
            ParentWindow = new IntPtr(MessageOnlyWindow),
        });
        _window.AddHook(OnMessage);

        // Telas e perfis mudam a lista de ações, e com ela os atalhos.
        service.Changed += Apply;
        service.ProfilesChanged += Apply;
        Apply();
    }

    public IReadOnlyList<HotkeyBinding> Bindings { get; private set; } = [];

    /// <summary>A lista de atalhos ou o resultado do registro mudou.</summary>
    public event Action? BindingsChanged;

    /// <summary>Muda o atalho de uma ação; nulo volta ao de fábrica, texto vazio desliga.</summary>
    public void Set(string actionId, string? gesture)
    {
        if (gesture is null)
        {
            _service.Settings.Hotkeys.Remove(actionId);
        }
        else
        {
            _service.Settings.Hotkeys[actionId] = gesture;
        }

        _service.Store.SaveSettings(_service.Settings);
        Apply();
    }

    /// <summary>Solta os atalhos enquanto um novo está sendo digitado, senão ele seria executado.</summary>
    public void Suspend()
    {
        _suspended = true;
        UnregisterAll();
    }

    public void Resume()
    {
        _suspended = false;
        Apply();
    }

    private void Apply()
    {
        UnregisterAll();

        var bindings = new List<HotkeyBinding>();
        foreach (var action in BuildActions())
        {
            var custom = _service.Settings.Hotkeys.TryGetValue(action.Id, out var saved);
            var gesture = custom ? saved : action.DefaultGesture;
            if (string.IsNullOrEmpty(gesture))
            {
                bindings.Add(new HotkeyBinding(action, null, !custom, false));
                continue;
            }

            var inUse = !_suspended && !Register(gesture, action.Run);
            bindings.Add(new HotkeyBinding(action, gesture, !custom, inUse));
        }

        Bindings = bindings;
        BindingsChanged?.Invoke();
    }

    private IEnumerable<HotkeyAction> BuildActions()
    {
        foreach (var monitor in _service.Controller.GetMonitors())
        {
            var target = monitor;
            var key = monitor.Number <= 9 ? $"Ctrl+Alt+D{monitor.Number}" : null;
            yield return new HotkeyAction($"only:{monitor.DevicePath}",
                $"Somente {monitor.Number} - {_service.NameOf(monitor)}", key, () => _service.ShowOnly(target));
        }

        yield return new HotkeyAction("extend", "Estender", "Ctrl+Alt+E", _service.Extend);
        yield return new HotkeyAction("undo", "Desfazer a última troca de telas", "Ctrl+Alt+D0", _service.Undo);

        foreach (var name in _service.Controller.GetProfileNames())
        {
            var profile = name;
            yield return new HotkeyAction(ProfileActionId(profile), $"Perfil {profile}", null,
                () => _service.ApplyProfile(profile));
        }
    }

    public static string ProfileActionId(string profileName) => $"profile:{profileName}";

    private bool Register(string gesture, Func<Task> action)
    {
        if (Parse(gesture) is not { } parsed)
        {
            return false;
        }

        var id = _registered.Count + 1;
        if (!RegisterHotKey(_window.Handle, id, parsed.Modifiers | ModNoRepeat, parsed.Key))
        {
            return false;
        }

        _registered[id] = action;
        return true;
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
        {
            UnregisterHotKey(_window.Handle, id);
        }

        _registered.Clear();
    }

    /// <summary>Monta o texto do atalho a partir das teclas pressionadas.</summary>
    /// <returns>Nulo se ainda faltar a tecla principal ou se a combinação não servir como atalho.</returns>
    public static string? FromKeys(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            return null;
        }

        // Sem Ctrl, Alt ou Windows, a tecla deixaria de funcionar normalmente nos outros programas.
        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (!isFunctionKey && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Texto para mostrar: "Ctrl+Alt+D1" vira "Ctrl+Alt+1".</summary>
    public static string Display(string? gesture)
    {
        if (string.IsNullOrEmpty(gesture))
        {
            return "Sem atalho";
        }

        return string.Join("+", gesture.Split('+').Select(part => part switch
        {
            { Length: 2 } when part[0] == 'D' && char.IsDigit(part[1]) => part[1..],
            _ when part.StartsWith("NumPad", StringComparison.Ordinal) => "Num " + part[6..],
            _ => part,
        }));
    }

    private static (uint Modifiers, uint Key)? Parse(string gesture)
    {
        var parts = gesture.Split('+');
        if (!Enum.TryParse<Key>(parts[^1], out var key))
        {
            return null;
        }

        uint modifiers = 0;
        foreach (var part in parts[..^1])
        {
            modifiers |= part switch
            {
                "Ctrl" => ModControl,
                "Alt" => ModAlt,
                "Shift" => ModShift,
                "Win" => ModWin,
                _ => 0,
            };
        }

        return (modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            _ = action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _service.Changed -= Apply;
        _service.ProfilesChanged -= Apply;
        UnregisterAll();
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
