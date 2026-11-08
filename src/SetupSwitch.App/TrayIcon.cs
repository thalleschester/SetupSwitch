using System.Drawing;
using Forms = System.Windows.Forms;

namespace SetupSwitch.App;

/// <summary>Ícone perto do relógio, com o menu de troca rápida.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly DisplayService _service;
    private readonly Action _open;
    private readonly Action _exit;
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(DisplayService service, Action open, Action exit)
    {
        _service = service;
        _open = open;
        _exit = exit;

        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => Rebuild(menu);

        _icon = new Forms.NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "SetupSwitch",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _open();
            }
        };
    }

    private void Rebuild(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add("Abrir SetupSwitch", null, (_, _) => _open());
        menu.Items.Add(new Forms.ToolStripSeparator());

        foreach (var monitor in _service.Controller.GetMonitors())
        {
            menu.Items.Add($"Somente {monitor.Number} - {_service.NameOf(monitor)}", null, (_, _) => _ = _service.ShowOnly(monitor));
        }

        menu.Items.Add("Estender", null, (_, _) => _ = _service.Extend());

        var profiles = _service.Controller.GetProfileNames();
        if (profiles.Count > 0)
        {
            var submenu = new Forms.ToolStripMenuItem("Perfis");
            foreach (var profile in profiles)
            {
                submenu.DropDownItems.Add(profile, null, (_, _) => _ = _service.ApplyProfile(profile));
            }

            menu.Items.Add(submenu);
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Desfazer última troca", null, (_, _) => _ = _service.Undo());
        menu.Items.Add("Sair", null, (_, _) => _exit());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
