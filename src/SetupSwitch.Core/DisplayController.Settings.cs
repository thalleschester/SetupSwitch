using System.Runtime.InteropServices;
using static SetupSwitch.Core.Native.CcdNative;

namespace SetupSwitch.Core;

public sealed partial class DisplayController
{
    /// <summary>Resoluções e taxas aceitas por uma tela ligada, sempre em formato paisagem.</summary>
    public IReadOnlyList<DisplayMode> GetModes(MonitorInfo monitor)
    {
        if (monitor.GdiName is not { } gdiName)
        {
            return [];
        }

        var modes = new HashSet<DisplayMode>();
        var mode = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>() };

        for (var index = 0; EnumDisplaySettings(gdiName, index, ref mode); index++)
        {
            if (mode.BitsPerPel < 32)
            {
                continue;
            }

            var (width, height) = ((int)mode.PelsWidth, (int)mode.PelsHeight);
            modes.Add(new DisplayMode(Math.Max(width, height), Math.Min(width, height), (int)mode.DisplayFrequency));
        }

        return modes
            .OrderByDescending(m => m.Width * m.Height)
            .ThenByDescending(m => m.RefreshRate)
            .ToList();
    }

    /// <summary>Aplica posição, resolução, taxa e orientação das telas já ligadas.</summary>
    public void ApplySettings(IReadOnlyList<MonitorSettings> settings, bool validateOnly = false)
    {
        SaveCurrentState(validateOnly);

        var active = Query(QdcOnlyActivePaths);
        var paths = active.Paths;
        var modes = active.Modes;

        for (var i = 0; i < paths.Length; i++)
        {
            var devicePath = DevicePathOf(paths[i]);
            var setting = settings.FirstOrDefault(s => s.DevicePath == devicePath);
            if (setting is null)
            {
                continue;
            }

            ref var mode = ref modes[paths[i].SourceInfo.ModeInfoIdx];
            var refresh = paths[i].TargetInfo.RefreshRate;
            var currentRefresh = refresh.Denominator == 0 ? 0 : (double)refresh.Numerator / refresh.Denominator;

            var sizeChanged = mode.SourceWidth != setting.Width || mode.SourceHeight != setting.Height;
            var refreshChanged = Math.Abs(currentRefresh - setting.RefreshRate) > 0.01;
            var rotationChanged = paths[i].TargetInfo.Rotation != (int)setting.Rotation;

            mode.SourceX = setting.X;
            mode.SourceY = setting.Y;
            mode.SourceWidth = (uint)setting.Width;
            mode.SourceHeight = (uint)setting.Height;
            paths[i].TargetInfo.Rotation = (int)setting.Rotation;

            if (sizeChanged || refreshChanged || rotationChanged)
            {
                // Sem modo de saída definido, o Windows escolhe o que combina com a nova resolução.
                paths[i].TargetInfo.ModeInfoIdx = PathModeIdxInvalid;
            }

            if (refreshChanged)
            {
                paths[i].TargetInfo.RefreshRate = new Rational
                {
                    Numerator = (uint)Math.Round(setting.RefreshRate),
                    Denominator = 1,
                };
            }
        }

        Check("Aplicar layout", SetDisplayConfig(
            (uint)paths.Length, paths, (uint)modes.Length, modes,
            SdcUseSuppliedDisplayConfig | SdcAllowChanges | (validateOnly ? SdcValidate : ApplyFlags)));
    }
}
