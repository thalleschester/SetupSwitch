namespace SetupSwitch.Core;

/// <summary>Mesmos valores de DISPLAYCONFIG_ROTATION.</summary>
public enum MonitorRotation
{
    Landscape = 1,
    Portrait = 2,
    LandscapeFlipped = 3,
    PortraitFlipped = 4,
}

/// <summary>Combinação de resolução e taxa de atualização aceita por um monitor.</summary>
public sealed record DisplayMode(int Width, int Height, int RefreshRate);

/// <summary>Como uma tela ligada deve ficar depois de aplicar o layout.</summary>
public sealed record MonitorSettings(
    string DevicePath, int X, int Y, int Width, int Height, double RefreshRate, MonitorRotation Rotation);

/// <summary>Um monitor conectado. <see cref="DevicePath"/> é o ID estável do hardware.</summary>
public sealed record MonitorInfo
{
    /// <summary>Número mostrado ao usuário, de acordo com a ordem salva.</summary>
    public int Number { get; init; }

    public required string Name { get; init; }
    public required string DevicePath { get; init; }
    public required string Connection { get; init; }
    public required bool IsActive { get; init; }
    public bool IsPrimary { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public double RefreshRate { get; init; }

    /// <summary>ID da origem de vídeo; telas com o mesmo valor estão replicadas.</summary>
    public uint? SourceId { get; init; }

    public MonitorRotation Rotation { get; init; } = MonitorRotation.Landscape;

    /// <summary>Nome da origem no Windows, usado para listar resoluções. Só existe em telas ligadas.</summary>
    public string? GdiName { get; init; }

    internal Native.CcdNative.Luid Adapter { get; init; }
    internal uint TargetId { get; init; }
}
