using SetupSwitch.Core.Native;
using static SetupSwitch.Core.Native.CcdNative;

namespace SetupSwitch.Core;

/// <summary>Lê e aplica a topologia de telas pela API CCD do Windows.</summary>
public sealed partial class DisplayController(StateStore store)
{
    private StateStore Store => store;

    private const uint ApplyFlags = SdcApply | SdcSaveToDatabase;
    private const uint SuppliedFlags = SdcUseSuppliedDisplayConfig | SdcAllowChanges;

    private sealed record Snapshot(PathInfo[] Paths, ModeInfo[] Modes);

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var all = Query(QdcAllPaths);
        var active = Query(QdcOnlyActivePaths);

        var monitors = new List<MonitorInfo>();
        var seen = new HashSet<(Luid, uint)>();

        foreach (var path in all.Paths)
        {
            var target = path.TargetInfo;
            var activePath = FindActivePath(active.Paths, target.AdapterId, target.Id);
            if (target.TargetAvailable == 0 && activePath is null)
            {
                continue;
            }

            if (!seen.Add((target.AdapterId, target.Id)) || GetTargetName(target.AdapterId, target.Id) is not { } name)
            {
                continue;
            }

            if (name.DevicePath.Length == 0)
            {
                continue;
            }

            monitors.Add(Describe(name, target, activePath, active.Modes));
        }

        return ApplyNumbering(monitors);
    }

    /// <summary>Deixa somente o monitor informado ligado, como tela principal.</summary>
    public void ShowOnly(MonitorInfo monitor, bool validateOnly = false)
    {
        var all = Query(QdcAllPaths);
        var paths = BuildExtendedPaths(all, [monitor]);
        Change(paths, all.Modes, validateOnly);
        VerifyOrRestore([monitor], validateOnly);
    }

    /// <summary>Estende a área de trabalho para todos os monitores disponíveis.</summary>
    /// <remarks>Uma TV desligada pode continuar listada; nesse caso estende nas telas que responderem.</remarks>
    public void Extend(bool validateOnly = false)
    {
        var monitors = GetMonitors();
        var all = Query(QdcAllPaths);
        var paths = BuildExtendedPaths(all, monitors);
        Change(paths, all.Modes, validateOnly);

        if (!validateOnly)
        {
            WaitForMonitors(monitors);
            ArrangeExtended();
        }
    }

    /// <summary>Liga exatamente as telas informadas, estendendo a área de trabalho entre elas.</summary>
    public void ShowMonitors(IReadOnlyList<MonitorInfo> monitors, bool validateOnly = false)
    {
        var all = Query(QdcAllPaths);
        var paths = BuildExtendedPaths(all, monitors);
        Change(paths, all.Modes, validateOnly);
        VerifyOrRestore(monitors, validateOnly);

        if (!validateOnly)
        {
            ArrangeExtended();
        }
    }

    /// <summary>Replica a imagem pelo Duplicar do Windows; desfaz se alguma tela ficar de fora.</summary>
    /// <remarks>
    /// O conjunto de telas vem da última réplica salva pelo Windows, e montar a réplica
    /// diretamente com telas de resoluções diferentes foi recusado (erro 87) nos testes.
    /// </remarks>
    public void Mirror(bool validateOnly = false)
    {
        var monitors = GetMonitors();
        SaveCurrentState(validateOnly);

        Check("Replicar", SetDisplayConfig(0, null, 0, null,
            SdcTopologyClone | (validateOnly ? SdcValidate : SdcApply)));

        if (!validateOnly && WaitForMonitors(monitors) is { Count: > 0 } missing)
        {
            Restore();
            throw new DisplayConfigException(
                $"O Duplicar do Windows deixou de fora: {string.Join(", ", missing)}. "
                + "A configuração anterior foi restaurada.");
        }
    }

    /// <summary>Volta para a configuração anterior à última troca.</summary>
    public void Restore()
    {
        var point = store.LoadRestorePoint()
            ?? throw new DisplayConfigException("Ainda não existe configuração salva para restaurar.");

        ApplySaved(point, "Restaurar configuração");
    }

    private void ApplySaved(RestorePoint point, string action)
    {
        Check(action, ApplySnapshot(Remap(point, GetMonitors()), ApplyFlags));
    }

    private static int ApplySnapshot(Snapshot snapshot, uint flags) => SetDisplayConfig(
        (uint)snapshot.Paths.Length, snapshot.Paths,
        (uint)snapshot.Modes.Length, snapshot.Modes,
        SdcUseSuppliedDisplayConfig | SdcAllowChanges | flags);

    private void Change(PathInfo[] paths, ModeInfo[] modes, bool validateOnly)
    {
        SaveCurrentState(validateOnly);

        // Índices de modo inválidos deixam o Windows escolher a resolução de cada monitor.
        Check("Aplicar configuração", SetDisplayConfig(
            (uint)paths.Length, paths, (uint)modes.Length, modes,
            SuppliedFlags | (validateOnly ? SdcValidate : ApplyFlags)));
    }

    private void SaveCurrentState(bool validateOnly)
    {
        if (validateOnly)
        {
            return;
        }

        store.SaveRestorePoint(CaptureCurrent());

        var monitors = GetMonitors().Where(m => m.IsActive).ToList();
        if (monitors.Select(m => m.SourceId).Distinct().Count() >= 2)
        {
            store.SaveExtendedLayout(monitors.ToDictionary(m => m.DevicePath, m => new LayoutPosition(m.X, m.Y)));
        }
    }

    private static RestorePoint CaptureCurrent() => Capture(Query(QdcOnlyActivePaths));

    private static RestorePoint Capture(Snapshot active)
    {
        return new RestorePoint(
            RestorePoint.Encode(active.Paths),
            RestorePoint.Encode(active.Modes),
            active.Paths.Select(ToSavedTarget).ToList());
    }

    private static SavedTarget ToSavedTarget(PathInfo path)
    {
        var target = path.TargetInfo;
        var name = GetTargetName(target.AdapterId, target.Id);
        return new SavedTarget(target.AdapterId.LowPart, target.AdapterId.HighPart, target.Id, name?.DevicePath ?? "");
    }

    /// <summary>Recoloca os monitores na posição salva; quem não tem posição vai para a direita.</summary>
    private void ArrangeExtended()
    {
        var layout = store.LoadExtendedLayout();
        var active = Query(QdcOnlyActivePaths);
        var modes = active.Modes;

        var nextX = 0;
        foreach (var path in active.Paths)
        {
            if (DevicePathOf(path) is { } devicePath && layout.TryGetValue(devicePath, out var position))
            {
                ref var mode = ref modes[path.SourceInfo.ModeInfoIdx];
                nextX = Math.Max(nextX, position.X + (int)mode.SourceWidth);
            }
        }

        foreach (var path in active.Paths)
        {
            ref var mode = ref modes[path.SourceInfo.ModeInfoIdx];
            if (DevicePathOf(path) is { } devicePath && layout.TryGetValue(devicePath, out var position))
            {
                mode.SourceX = position.X;
                mode.SourceY = position.Y;
            }
            else
            {
                mode.SourceX = nextX;
                mode.SourceY = 0;
                nextX += (int)mode.SourceWidth;
            }
        }

        MoveOriginToPrimary(active.Paths, modes);
        CloseHorizontalGaps(active.Paths, modes);

        Check("Organizar telas", SetDisplayConfig(
            (uint)active.Paths.Length, active.Paths,
            (uint)modes.Length, modes,
            SdcUseSuppliedDisplayConfig | SdcAllowChanges | ApplyFlags));
    }

    /// <summary>A tela principal do Windows é sempre a que está na posição 0,0.</summary>
    private static void MoveOriginToPrimary(PathInfo[] paths, ModeInfo[] modes)
    {
        var sources = paths.Select(p => p.SourceInfo.ModeInfoIdx).Distinct().ToList();
        if (sources.Any(i => modes[i].SourceX == 0 && modes[i].SourceY == 0))
        {
            return;
        }

        var anchor = sources.OrderBy(i => modes[i].SourceX).First();
        var (shiftX, shiftY) = (modes[anchor].SourceX, modes[anchor].SourceY);
        foreach (var index in sources)
        {
            modes[index].SourceX -= shiftX;
            modes[index].SourceY -= shiftY;
        }
    }

    /// <summary>Ao deixar uma tela do meio de fora, aproxima as outras para não sobrar buraco.</summary>
    private static void CloseHorizontalGaps(PathInfo[] paths, ModeInfo[] modes)
    {
        var sources = paths.Select(p => p.SourceInfo.ModeInfoIdx).Distinct()
            .OrderBy(i => modes[i].SourceX).ToList();

        for (var i = 1; i < sources.Count; i++)
        {
            var previous = modes[sources[i - 1]];
            var gap = modes[sources[i]].SourceX - (previous.SourceX + (int)previous.SourceWidth);
            if (gap <= 0)
            {
                continue;
            }

            // Mantém a tela principal em 0,0 deslocando o lado em que ela não está.
            var primaryIsAhead = sources.Skip(i).Any(s => modes[s].SourceX == 0 && modes[s].SourceY == 0);
            foreach (var index in primaryIsAhead ? sources.Take(i) : sources.Skip(i))
            {
                modes[index].SourceX += primaryIsAhead ? gap : -gap;
            }
        }
    }

    private static string? DevicePathOf(PathInfo path) =>
        GetTargetName(path.TargetInfo.AdapterId, path.TargetInfo.Id)?.DevicePath;

    private static PathInfo[] BuildExtendedPaths(Snapshot all, IEnumerable<MonitorInfo> monitors)
    {
        var usedSources = new HashSet<(Luid, uint)>();
        var paths = new List<PathInfo>();

        foreach (var monitor in monitors)
        {
            // Mantém a origem atual quando possível, para reduzir a troca de janelas de lugar.
            var candidates = PathsFor(all, monitor)
                .OrderBy(p => p.SourceInfo.Id == monitor.SourceId ? 0 : 1);

            var found = false;
            foreach (var candidate in candidates)
            {
                if (usedSources.Add((candidate.SourceInfo.AdapterId, candidate.SourceInfo.Id)))
                {
                    paths.Add(Activate(candidate));
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new DisplayConfigException($"A placa de vídeo não tem saída livre para {monitor.Name}.");
            }
        }

        return paths.ToArray();
    }

    private static IEnumerable<PathInfo> PathsFor(Snapshot all, MonitorInfo monitor) =>
        all.Paths.Where(p => p.TargetInfo.Id == monitor.TargetId && p.TargetInfo.AdapterId.Equals(monitor.Adapter));

    private static PathInfo Activate(PathInfo path)
    {
        path.Flags |= PathActive;
        path.SourceInfo.ModeInfoIdx = PathModeIdxInvalid;
        path.TargetInfo.ModeInfoIdx = PathModeIdxInvalid;
        return path;
    }

    /// <summary>Desfaz a troca se alguma tela pedida não ligou, para nunca sobrar um estado parcial.</summary>
    private void VerifyOrRestore(IReadOnlyList<MonitorInfo> expected, bool validateOnly)
    {
        if (validateOnly)
        {
            return;
        }

        var missing = WaitForMonitors(expected);
        if (missing.Count > 0)
        {
            Restore();
            throw new DisplayConfigException(
                $"Não foi possível ligar: {string.Join(", ", missing)}. Confira se a tela está ligada e na entrada certa. "
                + "A configuração anterior foi restaurada.");
        }
    }

    /// <summary>Espera a troca assentar e devolve o nome das telas que não ligaram.</summary>
    private List<string> WaitForMonitors(IReadOnlyList<MonitorInfo> expected)
    {
        var wanted = expected.Select(m => m.DevicePath).ToHashSet();
        List<string> missing = [];

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var active = GetMonitors().Where(m => m.IsActive).Select(m => m.DevicePath).ToHashSet();
            missing = expected.Where(m => !active.Contains(m.DevicePath)).Select(m => m.Name).ToList();

            if (wanted.SetEquals(active))
            {
                break;
            }

            Thread.Sleep(300);
        }

        return missing;
    }

    private static Snapshot Remap(RestorePoint point, IReadOnlyList<MonitorInfo> monitors)
    {
        var paths = point.DecodePaths();
        var modes = point.DecodeModes();
        var adapters = new Dictionary<Luid, Luid>();
        var targetIds = new Dictionary<uint, uint>();

        for (var i = 0; i < paths.Length; i++)
        {
            var saved = point.Targets[i];
            var monitor = monitors.FirstOrDefault(m => m.DevicePath == saved.DevicePath)
                ?? throw new DisplayConfigException($"Monitor da configuração salva não encontrado: {saved.DevicePath}");

            adapters[paths[i].TargetInfo.AdapterId] = monitor.Adapter;
            targetIds[paths[i].TargetInfo.Id] = monitor.TargetId;

            paths[i].TargetInfo.AdapterId = monitor.Adapter;
            paths[i].TargetInfo.Id = monitor.TargetId;
            paths[i].SourceInfo.AdapterId = monitor.Adapter;
        }

        for (var i = 0; i < modes.Length; i++)
        {
            if (modes[i].InfoType == ModeTypeTarget && targetIds.TryGetValue(modes[i].Id, out var targetId))
            {
                modes[i].Id = targetId;
            }

            if (adapters.TryGetValue(modes[i].AdapterId, out var adapter))
            {
                modes[i].AdapterId = adapter;
            }
        }

        return new Snapshot(paths, modes);
    }

    private IReadOnlyList<MonitorInfo> ApplyNumbering(List<MonitorInfo> monitors)
    {
        var numbering = store.LoadNumbering();
        return monitors
            .OrderBy(m =>
            {
                var index = numbering.IndexOf(m.DevicePath);
                return index >= 0 ? index : int.MaxValue;
            })
            .ThenBy(m => m.TargetId)
            .Select((m, index) => m with { Number = index + 1 })
            .ToList();
    }

    private static PathInfo? FindActivePath(PathInfo[] activePaths, Luid adapter, uint targetId)
    {
        foreach (var path in activePaths)
        {
            if (path.TargetInfo.Id == targetId && path.TargetInfo.AdapterId.Equals(adapter))
            {
                return path;
            }
        }

        return null;
    }

    private static MonitorInfo Describe(
        (string Name, string DevicePath) name,
        PathTargetInfo target,
        PathInfo? activePath,
        ModeInfo[] activeModes)
    {
        var monitor = new MonitorInfo
        {
            Name = name.Name.Length > 0 ? name.Name : "Monitor sem nome",
            DevicePath = name.DevicePath,
            Connection = ConnectionName(target.OutputTechnology),
            IsActive = activePath is not null,
            Adapter = target.AdapterId,
            TargetId = target.Id,
        };

        if (activePath is not { } path || path.SourceInfo.ModeInfoIdx == PathModeIdxInvalid)
        {
            return monitor;
        }

        var mode = activeModes[path.SourceInfo.ModeInfoIdx];
        var refresh = path.TargetInfo.RefreshRate;

        return monitor with
        {
            IsPrimary = mode.SourceX == 0 && mode.SourceY == 0,
            Width = (int)mode.SourceWidth,
            Height = (int)mode.SourceHeight,
            X = mode.SourceX,
            Y = mode.SourceY,
            RefreshRate = refresh.Denominator == 0 ? 0 : (double)refresh.Numerator / refresh.Denominator,
            SourceId = path.SourceInfo.Id,
            Rotation = (MonitorRotation)path.TargetInfo.Rotation,
            GdiName = GetSourceName(path.SourceInfo.AdapterId, path.SourceInfo.Id),
        };
    }

    private static string ConnectionName(int outputTechnology) => outputTechnology switch
    {
        0 => "VGA",
        4 => "DVI",
        5 => "HDMI",
        10 => "DisplayPort",
        11 => "DisplayPort interna",
        _ => "Outra",
    };

    private static Snapshot Query(uint flags)
    {
        while (true)
        {
            Check("Ler configuração", GetDisplayConfigBufferSizes(flags, out var numPaths, out var numModes));

            var paths = new PathInfo[numPaths];
            var modes = new ModeInfo[numModes];
            var result = QueryDisplayConfig(flags, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);

            // A topologia pode mudar entre as duas chamadas; nesse caso é só ler de novo.
            if (result == ErrorInsufficientBuffer)
            {
                continue;
            }

            Check("Ler configuração", result);
            return new Snapshot(paths[..(int)numPaths], modes[..(int)numModes]);
        }
    }

    private static void Check(string action, int result)
    {
        if (result != ErrorSuccess)
        {
            throw new DisplayConfigException(action, result);
        }
    }
}
