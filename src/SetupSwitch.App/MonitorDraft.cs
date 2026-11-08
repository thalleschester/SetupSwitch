using System.Drawing;
using SetupSwitch.Core;

namespace SetupSwitch.App;

/// <summary>Estado editável de uma tela, antes de aplicar as alterações.</summary>
public sealed class MonitorDraft(MonitorInfo monitor)
{
    public MonitorInfo Monitor { get; private set; } = monitor;
    public int X { get; set; } = monitor.X;
    public int Y { get; set; } = monitor.Y;
    public int LandscapeWidth { get; set; } = Math.Max(monitor.Width, monitor.Height);
    public int LandscapeHeight { get; set; } = Math.Min(monitor.Width, monitor.Height);
    public double RefreshRate { get; set; } = monitor.RefreshRate;
    public MonitorRotation Rotation { get; set; } = monitor.Rotation;
    public bool IsPrimary { get; set; } = monitor.IsPrimary;

    private bool IsPortrait => Rotation is MonitorRotation.Portrait or MonitorRotation.PortraitFlipped;

    public int Width => IsPortrait ? LandscapeHeight : LandscapeWidth;
    public int Height => IsPortrait ? LandscapeWidth : LandscapeHeight;
    public Rectangle Bounds => new(X, Y, Width, Height);

    public MonitorSettings ToSettings(int originX, int originY) =>
        new(Monitor.DevicePath, X - originX, Y - originY, Width, Height, RefreshRate, Rotation);
}
