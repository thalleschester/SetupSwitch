using System.Runtime.InteropServices;

namespace SetupSwitch.Core.Native;

/// <summary>Declarações da API CCD (Connecting and Configuring Displays) do Windows.</summary>
internal static class CcdNative
{
    public const int ErrorSuccess = 0;
    public const int ErrorInsufficientBuffer = 122;

    public const uint QdcAllPaths = 0x1;
    public const uint QdcOnlyActivePaths = 0x2;

    public const uint PathActive = 0x1;
    public const uint PathModeIdxInvalid = 0xFFFFFFFF;

    public const int ModeTypeSource = 1;
    public const int ModeTypeTarget = 2;

    public const uint SdcTopologyClone = 0x2;
    public const uint SdcUseSuppliedDisplayConfig = 0x20;
    public const uint SdcValidate = 0x40;
    public const uint SdcApply = 0x80;
    public const uint SdcSaveToDatabase = 0x200;
    public const uint SdcAllowChanges = 0x400;

    private const int DeviceInfoGetSourceName = 1;
    private const int DeviceInfoGetTargetName = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid : IEquatable<Luid>
    {
        public uint LowPart;
        public int HighPart;

        public readonly bool Equals(Luid other) => LowPart == other.LowPart && HighPart == other.HighPart;
        public override readonly bool Equals(object? obj) => obj is Luid other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(LowPart, HighPart);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public int OutputTechnology;
        public int Rotation;
        public int Scaling;
        public Rational RefreshRate;
        public int ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo
    {
        public PathSourceInfo SourceInfo;
        public PathTargetInfo TargetInfo;
        public uint Flags;
    }

    /// <summary>DISPLAYCONFIG_MODE_INFO. A união só é mapeada no que o app usa.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct ModeInfo
    {
        [FieldOffset(0)] public int InfoType;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid AdapterId;

        // Union: modo de origem (InfoType == ModeTypeSource)
        [FieldOffset(16)] public uint SourceWidth;
        [FieldOffset(20)] public uint SourceHeight;
        [FieldOffset(24)] public int SourcePixelFormat;
        [FieldOffset(28)] public int SourceX;
        [FieldOffset(32)] public int SourceY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public int Type;
        public int Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public int OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    public static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref DevMode mode);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetSourceDeviceName(ref SourceDeviceName request);

    public static string? GetSourceName(Luid adapter, uint sourceId)
    {
        var request = new SourceDeviceName
        {
            Header = new DeviceInfoHeader
            {
                Type = DeviceInfoGetSourceName,
                Size = Marshal.SizeOf<SourceDeviceName>(),
                AdapterId = adapter,
                Id = sourceId,
            },
            GdiName = string.Empty,
        };

        return GetSourceDeviceName(ref request) == ErrorSuccess ? request.GdiName : null;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPaths,
        [Out] PathInfo[] paths,
        ref uint numModes,
        [Out] ModeInfo[] modes,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPaths,
        [In] PathInfo[]? paths,
        uint numModes,
        [In] ModeInfo[]? modes,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetTargetDeviceName(ref TargetDeviceName request);

    public static (string Name, string DevicePath)? GetTargetName(Luid adapter, uint targetId)
    {
        var request = new TargetDeviceName
        {
            Header = new DeviceInfoHeader
            {
                Type = DeviceInfoGetTargetName,
                Size = Marshal.SizeOf<TargetDeviceName>(),
                AdapterId = adapter,
                Id = targetId,
            },
            FriendlyName = string.Empty,
            DevicePath = string.Empty,
        };

        return GetTargetDeviceName(ref request) == ErrorSuccess
            ? (request.FriendlyName, request.DevicePath)
            : null;
    }
}
