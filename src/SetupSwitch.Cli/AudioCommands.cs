using SetupSwitch.Audio;

namespace SetupSwitch.Cli;

/// <summary>Comandos "audio listar", "audio saida" e "audio entrada".</summary>
internal static class AudioCommands
{
    public static int Run(IReadOnlyList<string> values, int? testSeconds)
    {
        var audio = new AudioController();

        try
        {
            return values switch
            {
                ["listar"] => List(audio),
                ["saida", var query] => Change(audio, AudioFlow.Output, query, testSeconds),
                ["entrada", var query] => Change(audio, AudioFlow.Input, query, testSeconds),
                _ => Fail("Uso: audio listar | audio saida <dispositivo> | audio entrada <dispositivo>"),
            };
        }
        catch (AudioException error)
        {
            return Fail($"Erro: {error.Message}");
        }
    }

    private static int List(AudioController audio)
    {
        Print(audio, AudioFlow.Output);
        Print(audio, AudioFlow.Input);
        return 0;
    }

    private static int Change(AudioController audio, AudioFlow flow, string query, int? testSeconds)
    {
        var device = Find(audio, flow, query);
        var previous = audio.GetDefaults(flow);

        try
        {
            audio.SetDefault(device);
            Console.WriteLine($"{device.Name}: agora é o padrão.");
            Print(audio, flow);

            if (testSeconds is { } seconds)
            {
                Console.WriteLine($"Voltando ao dispositivo anterior em {seconds}s...");
                Thread.Sleep(TimeSpan.FromSeconds(seconds));
            }

            return 0;
        }
        finally
        {
            if (testSeconds is not null)
            {
                audio.Restore(flow, previous);
                Console.WriteLine("Dispositivo anterior restaurado.");
                Print(audio, flow);
            }
        }
    }

    private static AudioDevice Find(AudioController audio, AudioFlow flow, string query)
    {
        var devices = audio.GetDevices(flow);

        if (int.TryParse(query, out var number))
        {
            return number >= 1 && number <= devices.Count
                ? devices[number - 1]
                : throw new AudioException($"Não existe o dispositivo {number}. Há {devices.Count}.");
        }

        var matches = devices.Where(d => d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new AudioException(matches.Count == 0
                ? $"Nenhum dispositivo conectado com o nome \"{query}\"."
                : $"Mais de um dispositivo com o nome \"{query}\".");
    }

    private static void Print(AudioController audio, AudioFlow flow)
    {
        Console.WriteLine(flow == AudioFlow.Output ? "Saída:" : "Entrada:");

        var devices = audio.GetDevices(flow);
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            var marks = (device.IsDefault ? " [padrão]" : "") + (device.IsDefaultCommunication ? " [chamadas]" : "");
            Console.WriteLine($"  {i + 1}. {device.Name}{marks}");
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
