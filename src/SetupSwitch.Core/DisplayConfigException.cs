namespace SetupSwitch.Core;

public sealed class DisplayConfigException : Exception
{
    public int? ErrorCode { get; }

    public DisplayConfigException(string message) : base(message)
    {
    }

    public DisplayConfigException(string action, int errorCode)
        : base($"{action}: {Describe(errorCode)} (código {errorCode})")
    {
        ErrorCode = errorCode;
    }

    private static string Describe(int errorCode) => errorCode switch
    {
        5 => "acesso negado; a sessão não está no console",
        31 => "o driver de vídeo recusou a configuração",
        50 => "configuração não suportada por este hardware",
        87 => "parâmetros inválidos",
        1610 => "o Windows não encontrou uma configuração viável",
        _ => "erro do Windows",
    };
}
