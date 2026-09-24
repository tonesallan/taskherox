using System.Text.RegularExpressions;
using Iced.Intel;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Métodos de entrada/validação de stage resolvidos a partir do hub StageNode e do fluxo direto para
/// a classe estática de stage. Deliberadamente não contém um símbolo genérico <c>jgc</c>.
/// </summary>
public sealed record StageHubSymbols(
    long Jgk,
    long Jgq,
    long Jgd,
    long JgcType13,
    long? JgcType2);

/// <summary>
/// Porta a estratégia de <c>_stage_anchors</c>: StageNode é a âncora estável serializada pelo Unity;
/// seus CALL/JMP diretos para a classe UO reduzem o universo antes da seleção por assinatura.
/// Ambiguidades só são aceitas quando os candidatos têm exatamente a mesma forma de mnemônicos.
/// </summary>
public static class Il2CppStageHubExtractor
{
    private static readonly Regex StageCacheListType = new(
        @"^List<[\w\.]*\.?StageCache>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StageCacheDictionaryType = new(
        @"^Dictionary<int,\s*[\w\.]*\.?StageCache>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StageCacheParameter = new(
        @"^[\w\.]*\.?StageCache$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ActionBoolParameter = new(
        @"^Action<bool>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryExtract(
        IReadOnlyList<Il2CppDumpClass> classes,
        Il2CppScriptIndex script,
        Il2CppPeImage image,
        out StageHubSymbols? symbols,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(image);

        Il2CppDumpClass[] stageClasses = classes.Where(IsStageStaticClass).ToArray();
        if (stageClasses.Length != 1)
        {
            symbols = null;
            error = $"stage static class ambiguous ({stageClasses.Length})";
            return false;
        }

        Il2CppDumpClass[] stageNodes = classes
            .Where(klass => string.Equals(klass.Name, "StageNode", StringComparison.Ordinal) ||
                            klass.Name.StartsWith("StageNode.", StringComparison.Ordinal))
            .ToArray();
        if (stageNodes.Length == 0)
        {
            symbols = null;
            error = "StageNode anchor missing";
            return false;
        }

        Dictionary<long, string> uoMethods = stageClasses[0].Methods
            .GroupBy(method => method.Rva)
            .ToDictionary(group => group.Key, group => group.First().Signature);

        var callees = new Dictionary<long, string>();
        foreach (Il2CppDumpClass stageNode in stageNodes)
        {
            foreach (Il2CppDumpMethod method in stageNode.Methods)
            {
                foreach (Il2CppDirectFlow flow in Il2CppDisassembly.DirectFlows(image, script.Addresses, method.Rva))
                {
                    if (uoMethods.TryGetValue(flow.TargetRva, out string? signature))
                        callees[flow.TargetRva] = signature;
                }
            }
        }

        if (!TryPick(callees, "void", [new Regex("^int$", RegexOptions.CultureInvariant)],
                     "jgk", image, script.Addresses, out long jgk, out error) ||
            !TryPick(callees, "bool", [new Regex("^int$", RegexOptions.CultureInvariant)],
                     "jgq", image, script.Addresses, out long jgq, out error) ||
            !TryPick(callees, "void", [StageCacheParameter, ActionBoolParameter],
                     "jgd", image, script.Addresses, out long jgd, out error))
        {
            symbols = null;
            return false;
        }

        // Cross-check independente do legado: o predicado de stage liberado referencia Normal 1-1 (1101).
        if (!Il2CppDisassembly.ContainsImmediate(
                image, script.Addresses, jgq, 0x44D, Mnemonic.Cmp, Mnemonic.Mov))
        {
            symbols = null;
            error = "jgq missing 1101 cross-check";
            return false;
        }

        long[] validators = callees
            .Where(pair => IsStageValidatorSignature(pair.Value))
            .Select(pair => pair.Key)
            .Distinct()
            .OrderBy(rva => rva)
            .ToArray();

        var type13 = new List<long>();
        var type2 = new List<long>();
        foreach (long rva in validators)
        {
            IReadOnlySet<long> values = Il2CppDisassembly.DirectRegisterCompareImmediates(
                image, script.Addresses, rva);

            if (values.Contains(1) && values.Contains(3))
            {
                type13.Add(rva);
                continue;
            }

            if (values.Contains(2) && !values.Contains(1) && !values.Contains(3))
                type2.Add(rva);
        }

        long jgcType13;
        if (type13.Count == 1)
        {
            jgcType13 = type13[0];
        }
        else if (validators.Length == 1)
        {
            // Compatibilidade com builds antigas que tinham um único validador StageCache.
            jgcType13 = validators[0];
        }
        else
        {
            symbols = null;
            error = $"jgc type1/3 ambiguous: validators={validators.Length} classified={type13.Count}";
            return false;
        }

        long? jgcType2 = null;
        if (type2.Count == 1)
        {
            jgcType2 = type2[0];
        }
        else if (type2.Count > 1)
        {
            symbols = null;
            error = $"jgc type2 ambiguous ({type2.Count})";
            return false;
        }

        symbols = new StageHubSymbols(jgk, jgq, jgd, jgcType13, jgcType2);
        error = null;
        return true;
    }

    private static bool TryPick(
        IReadOnlyDictionary<long, string> callees,
        string returnType,
        IReadOnlyList<Regex> parameterTypes,
        string label,
        Il2CppPeImage image,
        IReadOnlyList<long> addresses,
        out long rva,
        out string? error)
    {
        var hits = new List<long>();
        foreach ((long candidateRva, string signature) in callees)
        {
            if (!Il2CppMethodSignatureParser.TryParse(signature, out Il2CppMethodSignatureInfo? parsed) ||
                parsed is null || !parsed.IsStatic ||
                !string.Equals(parsed.ReturnType, returnType, StringComparison.Ordinal) ||
                parsed.ParameterTypes.Count != parameterTypes.Count)
                continue;

            bool allMatch = true;
            for (int i = 0; i < parameterTypes.Count; i++)
            {
                if (!parameterTypes[i].IsMatch(parsed.ParameterTypes[i]))
                {
                    allMatch = false;
                    break;
                }
            }
            if (allMatch)
                hits.Add(candidateRva);
        }

        if (hits.Count == 1)
        {
            rva = hits[0];
            error = null;
            return true;
        }

        if (hits.Count > 1)
        {
            IReadOnlyList<Mnemonic> firstShape = Il2CppDisassembly.MnemonicShape(image, addresses, hits[0]);
            bool clones = hits.Skip(1).All(candidate =>
                firstShape.SequenceEqual(Il2CppDisassembly.MnemonicShape(image, addresses, candidate)));
            if (clones)
            {
                rva = hits.Min();
                error = null;
                return true;
            }
        }

        rva = 0;
        error = $"{label} ambiguous ({hits.Count})";
        return false;
    }

    private static bool IsStageValidatorSignature(string signature)
    {
        if (!Il2CppMethodSignatureParser.TryParse(signature, out Il2CppMethodSignatureInfo? parsed) || parsed is null)
            return false;

        return parsed.IsStatic &&
               string.Equals(parsed.ReturnType, "EStageEnterResultType", StringComparison.Ordinal) &&
               parsed.ParameterTypes.Count == 1 &&
               StageCacheParameter.IsMatch(parsed.ParameterTypes[0]);
    }

    private static bool IsStageStaticClass(Il2CppDumpClass klass) =>
        klass.Declaration.Contains(" static class ", StringComparison.Ordinal) &&
        klass.Fields.Any(field => field.IsStatic && StageCacheListType.IsMatch(field.Type)) &&
        klass.Fields.Any(field => field.IsStatic && StageCacheDictionaryType.IsMatch(field.Type));
}
