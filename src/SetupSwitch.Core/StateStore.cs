using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SetupSwitch.Core.Native;

namespace SetupSwitch.Core;

/// <summary>Arquivos de estado em %LOCALAPPDATA%\SetupSwitch.</summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string LegacyDirectoryName = "Display" + "Manager";

    private readonly string _directory;

    public StateStore(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory();
    }

    private static string DefaultDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(root, "SetupSwitch");
        var legacy = Path.Combine(root, LegacyDirectoryName);

        // Quem usava o programa com o nome antigo mantém perfis, apelidos e opções.
        if (Directory.Exists(legacy) && !Directory.Exists(current))
        {
            Directory.Move(legacy, current);
        }

        return current;
    }

    private string RestorePointPath => Path.Combine(_directory, "ultima-configuracao.json");
    private string LayoutPath => Path.Combine(_directory, "layout-estendido.json");
    private string NumberingPath => Path.Combine(_directory, "numeracao.json");
    private string ProfilesPath => Path.Combine(_directory, "perfis.json");
    private string SettingsPath => Path.Combine(_directory, "opcoes.json");

    internal void SaveRestorePoint(RestorePoint point) => Write(RestorePointPath, point);
    internal RestorePoint? LoadRestorePoint() => Read<RestorePoint>(RestorePointPath);

    internal void SaveExtendedLayout(Dictionary<string, LayoutPosition> layout) => Write(LayoutPath, layout);
    internal Dictionary<string, LayoutPosition> LoadExtendedLayout() =>
        Read<Dictionary<string, LayoutPosition>>(LayoutPath) ?? [];

    internal void SaveProfiles(List<SavedProfile> profiles) => Write(ProfilesPath, profiles);
    internal List<SavedProfile> LoadProfiles() => Read<List<SavedProfile>>(ProfilesPath) ?? [];

    public void SaveSettings(AppSettings settings) => Write(SettingsPath, settings);
    public AppSettings LoadSettings() => Read<AppSettings>(SettingsPath) ?? new AppSettings();

    public void SaveNumbering(IReadOnlyList<string> devicePaths) => Write(NumberingPath, devicePaths);
    public List<string> LoadNumbering() => Read<List<string>>(NumberingPath) ?? [];

    private void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(_directory);

        // Grava em arquivo temporário para uma queda no meio não corromper o estado.
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static T? Read<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;
}

/// <summary>Preferências do aplicativo.</summary>
public sealed class AppSettings
{
    /// <summary>Pede confirmação depois de cada troca e reverte se não houver resposta.</summary>
    public bool ConfirmChanges { get; set; } = true;

    /// <summary>Apelidos de monitores e dispositivos de áudio, pelo ID de cada um.</summary>
    public Dictionary<string, string> Aliases { get; set; } = [];

    /// <summary>Dispositivos de áudio favoritos, pelo ID do Windows.</summary>
    public List<string> AudioFavorites { get; set; } = [];
}

internal sealed record LayoutPosition(int X, int Y);

/// <remarks>Arquivos antigos só têm Name e Config, por isso o padrão de Screens é Saved.</remarks>
internal sealed record SavedProfile(
    string Name,
    RestorePoint? Config,
    ProfileScreens Screens = ProfileScreens.Saved,
    string? ScreenDevicePath = null,
    string? AudioOutputId = null,
    string? AudioOutputName = null,
    string? AudioInputId = null,
    string? AudioInputName = null)
{
    [JsonIgnore]
    public ProfileInfo Info => new(
        Name, Screens, ScreenDevicePath, AudioOutputId, AudioOutputName, AudioInputId, AudioInputName);

    public static SavedProfile From(ProfileInfo info, RestorePoint? layout) => new(
        info.Name, layout, info.Screens, info.ScreenDevicePath,
        info.AudioOutputId, info.AudioOutputName, info.AudioInputId, info.AudioInputName);
}

internal sealed record SavedTarget(uint AdapterLow, int AdapterHigh, uint TargetId, string DevicePath);

/// <summary>Configuração ativa capturada, incluindo os IDs para remapear adaptadores.</summary>
/// <remarks>O LUID do adaptador muda a cada boot; por isso o monitor é reencontrado pelo DevicePath.</remarks>
internal sealed record RestorePoint(string Paths, string Modes, List<SavedTarget> Targets)
{
    public static string Encode<T>(T[] items) where T : unmanaged =>
        Convert.ToBase64String(MemoryMarshal.AsBytes(items.AsSpan()));

    public static T[] Decode<T>(string encoded) where T : unmanaged =>
        MemoryMarshal.Cast<byte, T>(Convert.FromBase64String(encoded)).ToArray();

    public CcdNative.PathInfo[] DecodePaths() => Decode<CcdNative.PathInfo>(Paths);
    public CcdNative.ModeInfo[] DecodeModes() => Decode<CcdNative.ModeInfo>(Modes);
}
