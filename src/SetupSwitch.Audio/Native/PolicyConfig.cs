using System.Runtime.InteropServices;
using static SetupSwitch.Audio.Native.CoreAudioNative;

namespace SetupSwitch.Audio.Native;

/// <summary>
/// Único ponto que usa a interface IPolicyConfig, que NÃO é documentada pela Microsoft.
/// O Windows não tem API pública para trocar o dispositivo padrão. Se uma atualização
/// quebrar a troca, a correção fica restrita a este arquivo.
/// </summary>
internal static class PolicyConfig
{
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient
    {
    }

    /// <remarks>A ordem dos métodos importa: ela define a posição de cada um na tabela COM.</remarks>
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, Role role);
    }

    public static int SetDefaultEndpoint(string deviceId, Role role)
    {
        var client = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            return client.SetDefaultEndpoint(deviceId, role);
        }
        finally
        {
            Marshal.ReleaseComObject(client);
        }
    }
}
