namespace SetupSwitch.Core;

/// <summary>O que um perfil faz com as telas. Os valores são gravados no arquivo de perfis.</summary>
public enum ProfileScreens
{
    Keep = 0,
    Only = 1,
    Extend = 2,
    Saved = 3,
}

/// <summary>
/// Uma combinação de telas e áudio. Saída ou entrada nulas significam "não alterar".
/// Os nomes ficam guardados para o perfil continuar legível com o aparelho desconectado.
/// </summary>
public sealed record ProfileInfo(
    string Name,
    ProfileScreens Screens,
    string? ScreenDevicePath,
    string? AudioOutputId,
    string? AudioOutputName,
    string? AudioInputId,
    string? AudioInputName);

public sealed partial class DisplayController
{
    public IReadOnlyList<ProfileInfo> GetProfiles() => Store.LoadProfiles()
        .Select(p => p.Info)
        .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public IReadOnlyList<string> GetProfileNames() => GetProfiles().Select(p => p.Name).ToList();

    /// <param name="profile">Dados do perfil; um perfil com o mesmo nome é substituído.</param>
    /// <param name="captureLayout">Guarda o layout atual das telas, em vez de manter o já salvo.</param>
    /// <param name="replacing">Nome anterior, quando o perfil está sendo renomeado.</param>
    public void SaveProfile(ProfileInfo profile, bool captureLayout, string? replacing = null)
    {
        var profiles = Store.LoadProfiles();
        var existing = profiles.FirstOrDefault(p => SameName(p.Name, replacing ?? profile.Name));

        RestorePoint? layout = null;
        if (profile.Screens == ProfileScreens.Saved)
        {
            layout = captureLayout ? CaptureCurrent() : existing?.Config ?? CaptureCurrent();
        }

        profiles.RemoveAll(p => SameName(p.Name, profile.Name) || SameName(p.Name, replacing));
        profiles.Add(SavedProfile.From(profile, layout));
        Store.SaveProfiles(profiles);
    }

    public void DeleteProfile(string name)
    {
        var profiles = Store.LoadProfiles();
        profiles.RemoveAll(p => p.Name == name);
        Store.SaveProfiles(profiles);
    }

    /// <summary>Aplica a parte de telas do perfil.</summary>
    /// <returns>Falso se nada precisou mudar, e portanto não há o que desfazer.</returns>
    public bool ApplyProfileScreens(string name)
    {
        var profile = Store.LoadProfiles().FirstOrDefault(p => p.Name == name)
            ?? throw new DisplayConfigException($"Perfil não encontrado: {name}");

        switch (profile.Screens)
        {
            case ProfileScreens.Only:
                var monitors = GetMonitors();
                var monitor = monitors.FirstOrDefault(m => m.DevicePath == profile.ScreenDevicePath)
                    ?? throw new DisplayConfigException($"A tela do perfil {name} não está conectada.");

                if (monitor.IsActive && monitors.Count(m => m.IsActive) == 1)
                {
                    return false;
                }

                ShowOnly(monitor);
                return true;

            case ProfileScreens.Extend:
                Extend();
                return true;

            case ProfileScreens.Saved when profile.Config is not null:
                SaveCurrentState(validateOnly: false);
                ApplySaved(profile.Config, "Aplicar perfil");
                return true;

            default:
                return false;
        }
    }

    private static bool SameName(string name, string? other) =>
        string.Equals(name, other, StringComparison.CurrentCultureIgnoreCase);
}
