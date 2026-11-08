using Microsoft.Win32;

namespace SetupSwitch.App;

/// <summary>Liga e desliga o início junto com o Windows, só para o usuário atual.</summary>
public static class StartupRegistration
{
    public const string MinimizedArgument = "--minimizado";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SetupSwitch";
    private const string LegacyValueName = "Display" + "Manager";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
    }

    /// <summary>Troca a entrada do nome antigo, que apontaria para um executável que não existe mais.</summary>
    public static void MigrateLegacyEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(LegacyValueName) is null)
        {
            return;
        }

        key.DeleteValue(LegacyValueName);
        Set(true);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {MinimizedArgument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
