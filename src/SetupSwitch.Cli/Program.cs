using System.Globalization;
using System.Text;
using SetupSwitch.Core;

Console.OutputEncoding = Encoding.UTF8;

const string Usage = """
    SetupSwitchCli - controle dos monitores pelo terminal

    Uso:
      SetupSwitchCli listar               Mostra os monitores e o estado atual
      SetupSwitchCli somente <tela>       Deixa ligada apenas uma tela (número ou nome)
      SetupSwitchCli ativar <telas...>    Liga só as telas informadas, estendidas
      SetupSwitchCli estender             Estende para todas as telas
      SetupSwitchCli replicar             Replica a imagem em todas as telas
      SetupSwitchCli resolucao <tela> <largura> <altura> [Hz]
      SetupSwitchCli modos <tela>         Lista resoluções e taxas (só telas ligadas)
      SetupSwitchCli audio listar            Mostra saídas e entradas de áudio
      SetupSwitchCli audio saida <nome>      Define a saída padrão
      SetupSwitchCli audio entrada <nome>    Define a entrada padrão
      SetupSwitchCli restaurar            Volta para a configuração anterior
      SetupSwitchCli numerar <telas...>   Define a ordem dos números (1, 2, 3...)

    Opções de somente, estender e replicar:
      --validar                              Só pergunta ao Windows se é possível, sem alterar nada
      --testar <segundos>                    Aplica e volta sozinho depois do tempo informado
    """;

var store = new StateStore();
var controller = new DisplayController(store);

try
{
    return Run(args);
}
catch (DisplayConfigException error)
{
    Console.Error.WriteLine($"Erro: {error.Message}");
    return 1;
}

int Run(string[] arguments)
{
    var options = arguments.ToList();

    // Alguns ambientes repassam o caminho do próprio executável como primeiro argumento.
    if (options.Count > 0 && IsOwnExecutable(options[0]))
    {
        options.RemoveAt(0);
    }

    var validateOnly = options.Remove("--validar");
    var testSeconds = TakeTestSeconds(options);

    if (options.Count == 0)
    {
        Console.WriteLine(Usage);
        return 0;
    }

    var command = options[0].ToLowerInvariant();
    var values = options.Skip(1).ToList();

    switch (command)
    {
        case "audio":
            return SetupSwitch.Cli.AudioCommands.Run(values, testSeconds);

        case "listar":
            PrintMonitors();
            return 0;

        case "somente" when values.Count == 1:
            var monitor = FindMonitor(values[0]);
            return Change($"Somente {monitor.Name}", validateOnly, testSeconds,
                () => controller.ShowOnly(monitor, validateOnly));

        case "ativar" when values.Count > 0:
            var chosen = values.Select(FindMonitor).ToList();
            return Change("Ativar", validateOnly, testSeconds, () => controller.ShowMonitors(chosen, validateOnly));

        case "resolucao" when values.Count is 3 or 4:
            var target = FindMonitor(values[0]);
            var settings = controller.GetMonitors().Where(m => m.IsActive).Select(m => new MonitorSettings(
                m.DevicePath, m.X, m.Y,
                m.DevicePath == target.DevicePath ? int.Parse(values[1]) : m.Width,
                m.DevicePath == target.DevicePath ? int.Parse(values[2]) : m.Height,
                m.DevicePath == target.DevicePath && values.Count == 4 ? double.Parse(values[3]) : m.RefreshRate,
                m.Rotation)).ToList();
            return Change("Resolução", validateOnly, testSeconds, () => controller.ApplySettings(settings, validateOnly));

        case "modos" when values.Count == 1:
            foreach (var mode in controller.GetModes(FindMonitor(values[0])))
            {
                Console.WriteLine($"  {mode.Width}x{mode.Height} @ {mode.RefreshRate}Hz");
            }

            return 0;

        case "estender":
            return Change("Estender", validateOnly, testSeconds, () => controller.Extend(validateOnly));

        case "replicar":
            // A validação cobre o Duplicar do Windows, mas não prova que todas as telas entram nele.
            return Change(validateOnly ? "Replicar (sem garantia de incluir todas as telas)" : "Replicar",
                validateOnly, testSeconds, () => controller.Mirror(validateOnly));

        case "restaurar":
            controller.Restore();
            Console.WriteLine("Configuração anterior restaurada.");
            PrintMonitors();
            return 0;

        case "numerar" when values.Count > 0:
            store.SaveNumbering(values.Select(v => FindMonitor(v).DevicePath).ToList());
            PrintMonitors();
            return 0;

        default:
            Console.Error.WriteLine($"Comando não reconhecido: {string.Join(" ", options)}");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

// Compara só o nome: o mesmo arquivo pode chegar por caminho longo ou curto (8.3).
bool IsOwnExecutable(string argument) =>
    string.Equals(Path.GetFileName(argument), Path.GetFileName(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase)
    && File.Exists(argument);

int? TakeTestSeconds(List<string> options)
{
    var index = options.IndexOf("--testar");
    if (index < 0)
    {
        return null;
    }

    if (index + 1 >= options.Count || !int.TryParse(options[index + 1], out var seconds) || seconds < 1)
    {
        throw new DisplayConfigException("--testar precisa de um número de segundos.");
    }

    options.RemoveRange(index, 2);
    return seconds;
}

int Change(string label, bool validateOnly, int? testSeconds, Action action)
{
    try
    {
        action();

        if (validateOnly)
        {
            Console.WriteLine($"{label}: o Windows aceita esta configuração. Nada foi alterado.");
            return 0;
        }

        Console.WriteLine($"{label}: aplicado.");
        PrintMonitors();

        if (testSeconds is { } seconds)
        {
            Console.WriteLine($"Voltando à configuração anterior em {seconds}s...");
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }

        return 0;
    }
    finally
    {
        // Roda também em caso de erro: o modo de teste nunca pode deixar a troca aplicada.
        if (testSeconds is not null && !validateOnly)
        {
            controller.Restore();
            Console.WriteLine("Configuração anterior restaurada.");
            PrintMonitors();
        }
    }
}

MonitorInfo FindMonitor(string query)
{
    var monitors = controller.GetMonitors();

    if (int.TryParse(query, out var number))
    {
        return number >= 1 && number <= monitors.Count
            ? monitors[number - 1]
            : throw new DisplayConfigException($"Não existe a tela {number}. Há {monitors.Count} tela(s).");
    }

    var matches = monitors.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    return matches.Count == 1
        ? matches[0]
        : throw new DisplayConfigException(matches.Count == 0
            ? $"Nenhum monitor com o nome \"{query}\"."
            : $"Mais de um monitor com o nome \"{query}\".");
}

void PrintMonitors()
{
    var monitors = controller.GetMonitors();
    for (var i = 0; i < monitors.Count; i++)
    {
        var monitor = monitors[i];
        var state = monitor.IsActive
            ? string.Create(CultureInfo.InvariantCulture,
                $"{monitor.Width}x{monitor.Height} @ {monitor.RefreshRate:0.##}Hz, posição {monitor.X},{monitor.Y}, origem {monitor.SourceId}")
            : "desligada";
        var primary = monitor.IsPrimary ? " [principal]" : "";
        Console.WriteLine($"  {i + 1}. {monitor.Name} ({monitor.Connection}): {state}{primary}");
    }
}
