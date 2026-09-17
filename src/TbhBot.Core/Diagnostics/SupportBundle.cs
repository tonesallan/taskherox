using System.Text.Json;
using System.Text.RegularExpressions;
using TbhBot.Core;
using TaskHeroX.Core.Game;
using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Core.Diagnostics;

public sealed record SupportBundleDocument(
    string SchemaVersion,
    DateTimeOffset CreatedUtc,
    TaskHeroXDiagnosticInfo TaskHeroX,
    GameDiagnosticInfo Game,
    OffsetsDiagnosticInfo Offsets,
    CompatibilityDiagnosticInfo Compatibility,
    string[] LogTail);

public sealed record TaskHeroXDiagnosticInfo(string Version);

public sealed record GameDiagnosticInfo(
    bool Attached,
    int? ProcessId,
    string? BuildHash,
    string? ModuleBase,
    int? ModuleSize,
    string? ModuleName);

public sealed record OffsetsDiagnosticInfo(
    bool Loaded,
    string Source,
    int MinimumExtractorVersion,
    SymbolReadStatus[] CoreSymbols);

public sealed record SymbolReadStatus(string Name, bool Present);
public sealed record StatReadStatus(string Name, bool Readable);
public sealed record CapabilityStatus(string Name, string Status, string Detail);

public sealed record StageProgressDiagnostic(
    int? RuntimeMax,
    int? RuntimeCurrent,
    int? RuntimeWave,
    int? SaveMax,
    int? SaveCurrent,
    int? SaveWave);

public sealed record CompatibilityDiagnosticInfo(
    int ReadableStats,
    int ExpectedStats,
    StatReadStatus[] Stats,
    int StageFieldsRead,
    int ExpectedStageFields,
    int StageTableEntries,
    StageProgressDiagnostic Progress,
    int RuneEntries,
    int InventoryItems,
    bool PlayerSaveDataResolved,
    CapabilityStatus[] Capabilities);

/// <summary>
/// Coleta somente leituras já utilizadas pelo Compatibility Center. Não chama rotas de escrita,
/// dispatcher, navegação, automação nem mutações de save.
/// </summary>
public static class SupportBundleCollector
{
    public const string SchemaVersion = "taskherox.support-bundle/v1";

    private static readonly string[] CriticalSymbols =
    [
        "gra", "uo_ti", "uo_max", "uo_cur", "uo_wave", "bal_ti", "stage_off",
        "jgk", "jgd", "jgc", "jgc_type13", "jgc_type2",
    ];

    public static SupportBundleDocument Collect(
        Engine engine,
        string appVersion,
        IEnumerable<string>? logTail = null,
        DateTimeOffset? createdUtc = null)
    {
        ArgumentNullException.ThrowIfNull(engine);

        string[] rawLogs = (logTail ?? []).TakeLast(100).ToArray();
        string[] logs = rawLogs.Select(SupportBundleRedactor.RedactText).ToArray();

        // Engine.IsAttached sozinho pode permanecer true por alguns instantes depois que o processo
        // do jogo encerra, porque o handle/base da sessão anterior só são substituídos no próximo attach.
        // O bundle deve refletir o estado efetivo da conexão, igual ao EngineService.
        bool attached = engine.IsAttached && engine.Target.IsAlive();

        var statsStatus = GameConstants.Stats.Keys
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(name => new StatReadStatus(name, false))
            .ToArray();

        int readableStats = 0;
        int stageFieldsRead = 0;
        int stageTableEntries = 0;
        int runeEntries = 0;
        int inventoryItems = -1;
        bool psdResolved = false;
        var progress = new StageProgressDiagnostic(null, null, null, null, null, null);

        if (attached)
        {
            try
            {
                var stats = engine.Stats.ReadStats();
                statsStatus = GameConstants.Stats.Keys
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .Select(name => new StatReadStatus(name, stats.ContainsKey(name)))
                    .ToArray();
                readableStats = statsStatus.Count(x => x.Readable);
            }
            catch { }

            try { stageFieldsRead = engine.Stats.ReadStage().Count; } catch { }
            try { stageTableEntries = engine.StageNav.StageTable().Count; } catch { }

            try
            {
                var p = engine.Save.StageProgressSources();
                progress = new StageProgressDiagnostic(
                    NullIfNegative(p.RuntimeMax),
                    NullIfNegative(p.RuntimeCur),
                    NullIfNegative(p.RuntimeWave),
                    NullIfNegative(p.SaveMax),
                    NullIfNegative(p.SaveCur),
                    NullIfNegative(p.SaveWave));
            }
            catch { }

            try { runeEntries = engine.Save.ReadRunes().Count; } catch { }
            try { inventoryItems = engine.Save.InventoryCount(); } catch { }
            try { psdResolved = engine.Resolver.ResolvePsd() != 0; } catch { }
        }

        var symbols = CriticalSymbols
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(name => new SymbolReadStatus(
                name,
                attached && engine.Symbols is not null && engine.Symbols.Has(name)))
            .ToArray();

        bool editorReady = readableStats == GameConstants.Stats.Count &&
                           stageFieldsRead == GameConstants.StageFields.Count;
        bool progressReady = progress.RuntimeCurrent > 0 || progress.SaveCurrent > 0;
        bool saveReady = progressReady && runeEntries > 0 && inventoryItems >= 0 && psdResolved;
        bool type1Ready = attached && engine.StageNav is not null && engine.StageNav.CanValidateType1Entry;
        bool genericJgc = attached && engine.Symbols is not null && engine.Symbols.Has("jgc");
        bool splitType1 = attached && engine.StageNav is not null && engine.StageNav.UsesSplitType1Validator;

        var capabilities = new[]
        {
            new CapabilityStatus(
                "memory-editor",
                editorReady ? "ready" : "degraded",
                $"stats={readableStats}/{GameConstants.Stats.Count}; stage-fields={stageFieldsRead}/{GameConstants.StageFields.Count}"),
            new CapabilityStatus(
                "save-reads",
                saveReady ? "ready" : "degraded",
                $"progress={(progressReady ? "resolved" : "failed")}; runes={runeEntries}; inventory={inventoryItems}; psd={(psdResolved ? "resolved" : "failed")}"),
            new CapabilityStatus(
                "stage-entry.type1",
                type1Ready ? "ready" : "blocked",
                type1Ready
                    ? (splitType1 ? "validated split type-1 route" : "legacy generic jgc route")
                    : "no validated type-1 validator route"),
            new CapabilityStatus(
                "stage-entry.generic-jgc",
                genericJgc ? "resolved" : "unresolved",
                genericJgc
                    ? "generic jgc symbol is available"
                    : "generic jgc is absent; validated split routes remain separate"),
            new CapabilityStatus(
                "stage-entry.type2",
                genericJgc ? "legacy-generic" : "blocked",
                genericJgc
                    ? "handled only by the legacy generic validator"
                    : "no split type-2 production route is exposed"),
            new CapabilityStatus(
                "stage-entry.type3",
                genericJgc ? "legacy-generic" : "blocked",
                genericJgc
                    ? "handled only by the legacy generic validator"
                    : "no split type-3 production route is exposed"),
        };

        return new SupportBundleDocument(
            SchemaVersion,
            createdUtc ?? DateTimeOffset.UtcNow,
            new TaskHeroXDiagnosticInfo(appVersion),
            new GameDiagnosticInfo(
                attached,
                attached && engine.Target.ProcessId > 0 ? engine.Target.ProcessId : null,
                engine.BuildHash,
                attached && engine.Target.ModuleBase != 0 ? $"0x{engine.Target.ModuleBase:X}" : null,
                attached && engine.Target.ModuleSize > 0 ? engine.Target.ModuleSize : null,
                string.IsNullOrWhiteSpace(engine.Target.ModulePath) ? null : Path.GetFileName(engine.Target.ModulePath)),
            new OffsetsDiagnosticInfo(
                engine.OffsetsLoaded,
                DetectOffsetsSource(engine, rawLogs),
                SymbolTable.MinExtractVer,
                symbols),
            new CompatibilityDiagnosticInfo(
                readableStats,
                GameConstants.Stats.Count,
                statsStatus,
                stageFieldsRead,
                GameConstants.StageFields.Count,
                stageTableEntries,
                progress,
                runeEntries,
                inventoryItems,
                psdResolved,
                capabilities),
            logs);
    }

    private static string DetectOffsetsSource(Engine engine, IReadOnlyList<string> rawLogs)
    {
        if (!engine.OffsetsLoaded) return "not-loaded";
        if (engine.BuildHash is { Length: > 0 } hash && GameConstants.KnownBuilds.ContainsKey(hash))
            return "known-build";

        for (int i = rawLogs.Count - 1; i >= 0; i--)
        {
            string line = rawLogs[i];
            if (line.Contains("baixados do feed", StringComparison.OrdinalIgnoreCase)) return "feed";
            if (line.Contains("offsets carregados do cache", StringComparison.OrdinalIgnoreCase)) return "cache";
            if (line.Contains("offsets embutidos", StringComparison.OrdinalIgnoreCase)) return "embedded";
        }

        return "loaded";
    }

    private static int? NullIfNegative(int value) => value < 0 ? null : value;
}

public static partial class SupportBundleRedactor
{
    [GeneratedRegex(@"(?i)([A-Z]:[\\/]Users[\\/])[^\\/\r\n]+")]
    private static partial Regex WindowsUserPathRegex();

    [GeneratedRegex(@"(?<!\d)7656119\d{10}(?!\d)")]
    private static partial Regex SteamIdRegex();

    [GeneratedRegex(@"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?i)\b(steam(?:[_ -]?id)?|account(?:[_ -]?id)?)\s*[:=]\s*[^\s,;]+")]
    private static partial Regex NamedAccountIdRegex();

    public static string RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        string redacted = WindowsUserPathRegex().Replace(text, "$1<redacted>");
        redacted = SteamIdRegex().Replace(redacted, "<steam-id-redacted>");
        redacted = EmailRegex().Replace(redacted, "<email-redacted>");
        redacted = NamedAccountIdRegex().Replace(redacted, "$1=<redacted>");
        return redacted;
    }
}

public static class SupportBundleSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize(SupportBundleDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Options);
    }
}
