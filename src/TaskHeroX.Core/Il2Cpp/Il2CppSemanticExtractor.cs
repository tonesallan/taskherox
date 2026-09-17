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
/// Primeiro grupo semântico portado do extrator legado. Mantém comportamento fail-closed:
/// somente aceita a classe estática de stage quando ela é única e contém simultaneamente
/// List&lt;StageCache&gt; e Dictionary&lt;int, StageCache&gt; estáticos.
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
