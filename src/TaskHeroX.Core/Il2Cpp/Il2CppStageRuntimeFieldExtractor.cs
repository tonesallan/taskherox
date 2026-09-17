using Iced.Intel;
using System.Text.RegularExpressions;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Offsets runtime dos três ObscuredInt de progresso/posição usados pela classe estática de stage.
/// A resolução replica a estratégia semântica do extrator legado e nunca depende da posição dos
/// campos quando o layout contém ObscuredInt adicionais.
/// </summary>
public sealed record StageRuntimeFieldSymbols(
    long UoMaxOffset,
    long UoCurrentOffset,
    long UoWaveOffset);

public static class Il2CppStageRuntimeFieldExtractor
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

    public static bool TryExtract(
        IReadOnlyList<Il2CppDumpClass> classes,
        Il2CppScriptIndex script,
        Il2CppPeImage image,
        StageStaticSymbols stageStatic,
        StageHubSymbols stageHub,
        out StageRuntimeFieldSymbols? symbols,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stageStatic);
        ArgumentNullException.ThrowIfNull(stageHub);

        Il2CppDumpClass[] stageClasses = classes.Where(IsStageStaticClass).ToArray();
        if (stageClasses.Length != 1)
        {
            symbols = null;
            error = $"stage static class ambiguous ({stageClasses.Length})";
            return false;
        }

        Il2CppDumpClass stageClass = stageClasses[0];
        long[] obscuredOffsets = stageClass.Fields
            .Where(field => field.IsStatic &&
                            string.Equals(field.Type, "ObscuredInt", StringComparison.Ordinal))
            .Select(field => field.Offset)
            .ToArray();

        if (obscuredOffsets.Length < 3)
        {
            symbols = null;
            error = $"stage ObscuredInt fields insufficient ({obscuredOffsets.Length})";
            return false;
        }

        var candidates = obscuredOffsets.ToHashSet();

        HashSet<long> maxHits = ReferencedCandidateOffsets(
            Il2CppDisassembly.Decode(image, script.Addresses, stageHub.Jgq),
            candidates);

        long maxOffset;
        if (maxHits.Count == 1)
        {
            maxOffset = maxHits.Single();
        }
        else if (obscuredOffsets.Length == 3)
        {
            // Compatibilidade controlada com o layout histórico exato max/current/wave.
            maxOffset = obscuredOffsets[0];
        }
        else
        {
            symbols = null;
            error = $"uo_max ambiguous: obscured={obscuredOffsets.Length} hits={maxHits.Count}";
            return false;
        }

        var semanticPairs = new HashSet<(long Current, long Wave)>();

        if (stageStatic.UoCurrentCacheOffset is long currentCacheOffset)
        {
            Dictionary<long, string> uoMethods = stageClass.Methods
                .GroupBy(method => method.Rva)
                .ToDictionary(group => group.Key, group => group.First().Signature);

            long[] helpers = Il2CppDisassembly.DirectFlows(
                    image, script.Addresses, stageHub.Jgk)
                .Select(flow => flow.TargetRva)
                .Where(target => uoMethods.TryGetValue(target, out string? signature) &&
                                 IsStaticVoidStageCache(signature))
                .Distinct()
                .OrderBy(rva => rva)
                .ToArray();

            foreach (long helper in helpers)
            {
                IReadOnlyList<Instruction> instructions =
                    Il2CppDisassembly.Decode(image, script.Addresses, helper);

                int[] cacheWrites = instructions
                    .Select((instruction, index) => (instruction, index))
                    .Where(pair => WrittenCandidateOffsets(pair.instruction, [currentCacheOffset])
                        .Contains(currentCacheOffset))
                    .Select(pair => pair.index)
                    .ToArray();

                foreach (int cacheIndex in cacheWrites)
                {
                    var before = new List<(int Index, long Offset)>();
                    var after = new List<(int Index, long Offset)>();

                    for (int index = 0; index < instructions.Count; index++)
                    {
                        foreach (long offset in WrittenCandidateOffsets(instructions[index], candidates))
                        {
                            if (index < cacheIndex)
                                before.Add((index, offset));
                            else if (index > cacheIndex)
                                after.Add((index, offset));
                        }
                    }

                    if (before.Count == 0 || after.Count == 0)
                        continue;

                    (int currentIndex, long currentOffset) = before.MaxBy(item => item.Index);
                    if (cacheIndex - currentIndex > 12)
                        continue;

                    (int _, long waveOffset) = after.MinBy(item => item.Index);
                    if (currentOffset == waveOffset)
                        continue;

                    semanticPairs.Add((currentOffset, waveOffset));
                }
            }
        }

        long current;
        long wave;
        if (semanticPairs.Count == 1)
        {
            (current, wave) = semanticPairs.Single();
        }
        else if (obscuredOffsets.Length == 3)
        {
            long[] remaining = obscuredOffsets.Where(offset => offset != maxOffset).ToArray();
            if (remaining.Length != 2)
            {
                symbols = null;
                error = $"uo_cur/uo_wave historical fallback invalid ({remaining.Length})";
                return false;
            }

            current = remaining[0];
            wave = remaining[1];
        }
        else
        {
            symbols = null;
            error = $"uo_cur/uo_wave ambiguous: pairs={semanticPairs.Count}";
            return false;
        }

        if (maxOffset == current || maxOffset == wave || current == wave)
        {
            symbols = null;
            error = "stage runtime offsets collided";
            return false;
        }

        symbols = new StageRuntimeFieldSymbols(maxOffset, current, wave);
        error = null;
        return true;
    }

    private static bool IsStaticVoidStageCache(string signature)
    {
        if (!Il2CppMethodSignatureParser.TryParse(
                signature, out Il2CppMethodSignatureInfo? parsed) || parsed is null)
            return false;

        return parsed.IsStatic &&
               string.Equals(parsed.ReturnType, "void", StringComparison.Ordinal) &&
               parsed.ParameterTypes.Count == 1 &&
               StageCacheParameter.IsMatch(parsed.ParameterTypes[0]);
    }

    private static HashSet<long> ReferencedCandidateOffsets(
        IReadOnlyList<Instruction> instructions,
        IReadOnlySet<long> candidates)
    {
        var result = new HashSet<long>();
        foreach (Instruction instruction in instructions)
        {
            if (!TryGetMemoryOffset(instruction, out long offset))
                continue;
            if (candidates.Contains(offset))
                result.Add(offset);
        }
        return result;
    }

    private static HashSet<long> WrittenCandidateOffsets(
        Instruction instruction,
        IReadOnlySet<long> candidates)
    {
        var result = new HashSet<long>();

        if (!instruction.Mnemonic.ToString().StartsWith("Mov", StringComparison.Ordinal) ||
            instruction.OpCount == 0 ||
            instruction.GetOpKind(0) != OpKind.Memory ||
            !TryGetMemoryOffset(instruction, out long offset))
            return result;

        if (candidates.Contains(offset))
            result.Add(offset);

        return result;
    }

    private static bool TryGetMemoryOffset(Instruction instruction, out long offset)
    {
        offset = 0;

        bool hasMemoryOperand = false;
        for (int operand = 0; operand < instruction.OpCount; operand++)
        {
            if (instruction.GetOpKind(operand) == OpKind.Memory)
            {
                hasMemoryOperand = true;
                break;
            }
        }

        if (!hasMemoryOperand)
            return false;

        if (instruction.MemoryBase is Register.RSP or Register.ESP or Register.SP or
            Register.RBP or Register.EBP or Register.BP)
            return false;

        offset = unchecked((long)instruction.MemoryDisplacement64);
        return true;
    }

    private static bool IsStageStaticClass(Il2CppDumpClass klass) =>
        klass.Declaration.Contains(" static class ", StringComparison.Ordinal) &&
        klass.Fields.Any(field => field.IsStatic && StageCacheListType.IsMatch(field.Type)) &&
        klass.Fields.Any(field => field.IsStatic && StageCacheDictionaryType.IsMatch(field.Type));
}
