using System.Text.RegularExpressions;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Anchor da tabela global de estágios usada pelo extrator legado: a classe que contém o campo
/// <c>List&lt;StageInfoData&gt; stageInfoData</c>, o offset desse campo e o TypeInfo do singleton.
/// </summary>
public sealed record StageTableSymbols(
    string DeclaringClass,
    long BalanceTypeInfo,
    long StageListOffset,
    bool UsesGenericSingletonTypeInfo);

/// <summary>
/// Porta a parte puramente dump/script de <c>_stage_anchors</c> responsável por <c>bal_ti</c> e
/// <c>stage_off</c>. Não lê GameAssembly.dll e não altera a resolução runtime existente.
/// </summary>
public static class Il2CppStageTableExtractor
{
    public static bool TryExtract(
        IReadOnlyList<Il2CppDumpClass> classes,
        Il2CppScriptIndex script,
        out StageTableSymbols? symbols,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(script);

        var matches = classes
            .SelectMany(klass => klass.Fields
                .Where(field =>
                    string.Equals(field.Name, "stageInfoData", StringComparison.Ordinal) &&
                    string.Equals(field.Type, "List<StageInfoData>", StringComparison.Ordinal))
                .Select(field => (Class: klass, Field: field)))
            .ToArray();

        if (matches.Length != 1)
        {
            symbols = null;
            error = $"stageInfoData anchor ambiguous ({matches.Length})";
            return false;
        }

        Il2CppDumpClass declaringClass = matches[0].Class;
        string escapedClass = Regex.Escape(declaringClass.Name);
        Regex genericTypeInfoPattern = new(
            @"^[\w\.]+<" + escapedClass + @">_TypeInfo$",
            RegexOptions.CultureInvariant);

        var genericMatches = script.MetadataAddresses
            .Where(entry => genericTypeInfoPattern.IsMatch(entry.Key))
            .ToArray();

        if (genericMatches.Length > 1)
        {
            symbols = null;
            error = $"stage table generic TypeInfo ambiguous ({genericMatches.Length})";
            return false;
        }

        if (genericMatches.Length == 1)
        {
            symbols = new StageTableSymbols(
                declaringClass.Name,
                genericMatches[0].Value,
                matches[0].Field.Offset,
                UsesGenericSingletonTypeInfo: true);
            error = null;
            return true;
        }

        if (!script.TryGetMetadataAddress(declaringClass.Name + "_TypeInfo", out long concreteTypeInfo))
        {
            symbols = null;
            error = $"stage table TypeInfo missing for {declaringClass.Name}";
            return false;
        }

        symbols = new StageTableSymbols(
            declaringClass.Name,
            concreteTypeInfo,
            matches[0].Field.Offset,
            UsesGenericSingletonTypeInfo: false);
        error = null;
        return true;
    }
}
