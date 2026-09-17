using System.Text.RegularExpressions;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Símbolos estáticos de stage resolvidos apenas pela estrutura do dump/script, sem disassembly
/// e sem qualquer alteração de runtime.
/// </summary>
public sealed record StageStaticSymbols(
    long UoTypeInfo,
    long UoDictionaryOffset,
    long? UoCurrentCacheOffset);

/// <summary>
/// Offsets de slots de inventário/stash encontrados semanticamente em PlayerSaveData (ou classe
/// equivalente), sem depender de nomes de campos ofuscados.
/// </summary>
public sealed record InventorySlotOffsets(
    long InventorySlotsOffset,
    long StashSlotsOffset,
    string DeclaringClass);

/// <summary>
/// Grupos semânticos portados do extrator legado. Mantém comportamento fail-closed e não altera
/// qualquer rota runtime existente.
/// </summary>
public static class Il2CppSemanticExtractor
{
    private static readonly Regex StageCacheListType = new(
        @"^List<[\w\.]*\.?StageCache>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StageCacheDictionaryType = new(
        @"^Dictionary<int,\s*[\w\.]*\.?StageCache>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StageCacheType = new(
        @"^[\w\.]*\.?StageCache$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InventorySaveListType = new(
        @"^List<[\w\.]*\.?InventorySaveData>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StashSaveListType = new(
        @"^List<[\w\.]*\.?StashSaveData>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryExtractStageStaticSymbols(
        IReadOnlyList<Il2CppDumpClass> classes,
        Il2CppScriptIndex script,
        out StageStaticSymbols? symbols,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(script);

        Il2CppDumpClass[] candidates = classes
            .Where(IsStageStaticClass)
            .ToArray();

        if (candidates.Length != 1)
        {
            symbols = null;
            error = $"stage static class ambiguous ({candidates.Length})";
            return false;
        }

        Il2CppDumpClass stageClass = candidates[0];
        if (!script.TryGetMetadataAddress(stageClass.Name + "_TypeInfo", out long typeInfo))
        {
            symbols = null;
            error = $"missing TypeInfo for {stageClass.Name}";
            return false;
        }

        Il2CppDumpField? dictionaryField = stageClass.Fields
            .FirstOrDefault(field => field.IsStatic && StageCacheDictionaryType.IsMatch(field.Type));

        if (dictionaryField is null)
        {
            symbols = null;
            error = "stage dictionary field missing";
            return false;
        }

        Il2CppDumpField? currentCacheField = stageClass.Fields
            .FirstOrDefault(field => field.IsStatic && StageCacheType.IsMatch(field.Type));

        symbols = new StageStaticSymbols(
            typeInfo,
            dictionaryField.Offset,
            currentCacheField?.Offset);
        error = null;
        return true;
    }

    public static bool TryExtractInventorySlotOffsets(
        IReadOnlyList<Il2CppDumpClass> classes,
        out InventorySlotOffsets? offsets,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);

        var inventoryMatches = classes
            .SelectMany(klass => klass.Fields
                .Where(field => !field.IsStatic && InventorySaveListType.IsMatch(field.Type))
                .Select(field => (Class: klass, Field: field)))
            .ToArray();

        if (inventoryMatches.Length != 1)
        {
            offsets = null;
            error = $"inventory slot field ambiguous ({inventoryMatches.Length})";
            return false;
        }

        var stashMatches = classes
            .SelectMany(klass => klass.Fields
                .Where(field => !field.IsStatic && StashSaveListType.IsMatch(field.Type))
                .Select(field => (Class: klass, Field: field)))
            .ToArray();

        if (stashMatches.Length != 1)
        {
            offsets = null;
            error = $"stash slot field ambiguous ({stashMatches.Length})";
            return false;
        }

        if (!ReferenceEquals(inventoryMatches[0].Class, stashMatches[0].Class))
        {
            offsets = null;
            error = "inventory and stash fields belong to different classes";
            return false;
        }

        offsets = new InventorySlotOffsets(
            inventoryMatches[0].Field.Offset,
            stashMatches[0].Field.Offset,
            inventoryMatches[0].Class.Name);
        error = null;
        return true;
    }

    private static bool IsStageStaticClass(Il2CppDumpClass klass)
    {
        if (!klass.Declaration.Contains(" static class ", StringComparison.Ordinal))
            return false;

        bool hasList = klass.Fields.Any(field =>
            field.IsStatic && StageCacheListType.IsMatch(field.Type));

        bool hasDictionary = klass.Fields.Any(field =>
            field.IsStatic && StageCacheDictionaryType.IsMatch(field.Type));

        return hasList && hasDictionary;
    }
}
