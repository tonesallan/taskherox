using System.Text.RegularExpressions;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Anchors críticos que o legado resolvia por nomes de classe/assinatura preservados no dump.cs.
/// Esta etapa não usa nomes de métodos ofuscados e falha fechado quando a assinatura deixa de ser única.
/// </summary>
public sealed class Il2CppCriticalTextAnchors
{
    public Dictionary<string, long> Symbols { get; } = new(StringComparer.Ordinal);
    public string? MoveManagerClass { get; internal set; }
}

public static class Il2CppCriticalTextAnchorExtractor
{
    private static readonly Regex ShortRecipeType = new(
        @"^Dictionary<ERecipeType,\s*List<([a-z]{1,4})>>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ActionType = new(
        @"^Action<[^>]+>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryExtract(
        IReadOnlyList<Il2CppDumpClass> classes,
        out Il2CppCriticalTextAnchors? anchors,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);
        var result = new Il2CppCriticalTextAnchors();

        Il2CppDumpClass[] inputManagers = classes
            .Where(klass =>
                string.Equals(klass.Name, "InputManager", StringComparison.Ordinal) ||
                klass.Name.EndsWith(".InputManager", StringComparison.Ordinal))
            .ToArray();

        var updateCandidates = inputManagers
            .SelectMany(klass => klass.Methods
                .Where(method => string.Equals(method.Visibility, "private", StringComparison.Ordinal) &&
                                 Parse(method.Signature) is Il2CppMethodSignatureInfo parsed &&
                                 !parsed.IsStatic &&
                                 parsed.ReturnType == "void" &&
                                 parsed.MethodName == "Update" &&
                                 parsed.ParameterTypes.Count == 0)
                .Select(method => (Class: klass, Method: method)))
            .ToArray();

        if (updateCandidates.Length != 1)
        {
            anchors = null;
            string details = string.Join(
                ", ",
                updateCandidates.Select(candidate =>
                    $"{candidate.Class.Name}@0x{candidate.Method.Rva:X}"));
            error = $"InputManager.Update anchor ambiguous ({updateCandidates.Length})" +
                    (details.Length > 0 ? $": {details}" : string.Empty);
            return false;
        }
        result.Symbols["upd"] = updateCandidates[0].Method.Rva;

        Il2CppDumpClass[] stageBoxes = classes
            .Where(klass => string.Equals(klass.Name, "StageBox", StringComparison.Ordinal))
            .ToArray();
        if (stageBoxes.Length != 1 ||
            !TryUniqueMethod(stageBoxes[0].Methods,
                parsed => !parsed.IsStatic && parsed.ReturnType == "void" && parsed.ParameterTypes.Count == 1 &&
                          parsed.ParameterTypes[0] == "PointerEventData.InputButton",
                out long llx))
        {
            anchors = null;
            error = "StageBox click anchor ambiguous";
            return false;
        }
        result.Symbols["llx"] = llx;

        var moveMatches = classes
            .SelectMany(klass => klass.Methods
                .Select(method => (Class: klass, Method: method, Parsed: Parse(method.Signature)))
                .Where(item => item.Parsed is not null &&
                               string.Equals(item.Method.Visibility, "public", StringComparison.Ordinal) &&
                               !item.Parsed.IsStatic &&
                               item.Parsed.ParameterTypes.Count == 2 &&
                               item.Parsed.ParameterTypes[0] == "MoveRequest" &&
                               ActionType.IsMatch(item.Parsed.ParameterTypes[1])))
            .ToArray();
        if (moveMatches.Length != 1)
        {
            anchors = null;
            string details = string.Join(
                ", ",
                moveMatches.Select(item =>
                    $"{item.Class.Name}.{item.Parsed!.MethodName}@0x{item.Method.Rva:X}"));
            error = $"move-manager method ambiguous ({moveMatches.Length})" +
                    (details.Length > 0 ? $": {details}" : string.Empty);
            return false;
        }

        string moveClass = moveMatches[0].Class.Name;
        if (!IsSelfGenericSingleton(moveMatches[0].Class))
        {
            anchors = null;
            error = $"move-manager {moveClass} is not a self-generic singleton";
            return false;
        }
        result.Symbols["iw"] = moveMatches[0].Method.Rva;
        result.MoveManagerClass = moveClass;

        Il2CppDumpClass[] cubeClasses = classes
            .Where(klass => string.Equals(klass.Name, "Cube", StringComparison.Ordinal) ||
                            klass.Name.EndsWith(".Cube", StringComparison.Ordinal))
            .ToArray();
        if (cubeClasses.Length != 1)
        {
            anchors = null;
            error = $"Cube class ambiguous ({cubeClasses.Length})";
            return false;
        }

        Il2CppDumpClass cube = cubeClasses[0];
        string[] recipeTypes = cube.Fields
            .Select(field => ShortRecipeType.Match(field.Type))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (recipeTypes.Length != 1)
        {
            anchors = null;
            error = $"recipe type ambiguous ({recipeTypes.Length})";
            return false;
        }
        string recipeType = recipeTypes[0];

        if (!PickCube(cube, parsed => parsed.IsStatic && parsed.ReturnType == "void" &&
                                   parsed.ParameterTypes.SequenceEqual(["EItemSynthesisType"]), "ilo", result, out error) ||
            !PickCube(cube, parsed => parsed.IsStatic && parsed.ReturnType == "bool" &&
                                   parsed.ParameterTypes.SequenceEqual(["EGradeType"]), "ili", result, out error) ||
            !PickCube(cube, parsed => parsed.IsStatic && parsed.ReturnType == "void" &&
                                   parsed.ParameterTypes.SequenceEqual([recipeType]), "inf", result, out error) ||
            !PickCube(cube, parsed => parsed.IsStatic && parsed.ReturnType == "bool" &&
                                   parsed.ParameterTypes.SequenceEqual([recipeType]), "ima", result, out error) ||
            !PickCube(cube, parsed => parsed.IsStatic && parsed.ReturnType == "void" &&
                                   parsed.ParameterTypes.SequenceEqual(["int", "CubeInData"]), "iog", result, out error) ||
            !PickCube(cube, parsed => parsed.IsStatic && parsed.ReturnType == "EAddCubeResult" &&
                                   parsed.ParameterTypes.SequenceEqual(["ESlotType", "int"]), "ioa", result, out error))
        {
            anchors = null;
            return false;
        }

        var ipuCandidates = new List<long>();
        for (int i = 0; i + 1 < cube.Methods.Count; i++)
        {
            Il2CppMethodSignatureInfo? current = Parse(cube.Methods[i].Signature);
            Il2CppMethodSignatureInfo? next = Parse(cube.Methods[i + 1].Signature);
            if (current is not null && next is not null &&
                current.IsStatic && current.ReturnType == "void" && current.ParameterTypes.Count == 0 &&
                next.IsStatic && next.ReturnType.EndsWith("BucketCountResult", StringComparison.Ordinal))
                ipuCandidates.Add(cube.Methods[i].Rva);
        }
        if (ipuCandidates.Count != 1)
        {
            anchors = null;
            error = $"ipu anchor ambiguous ({ipuCandidates.Count})";
            return false;
        }
        result.Symbols["ipu"] = ipuCandidates[0];

        Il2CppDumpMethod[] triggerCandidates = cube.Methods
            .Where(method => Parse(method.Signature)?.MethodName == "TriggerCurrentRecipeLogic")
            .ToArray();
        if (triggerCandidates.Length != 1)
        {
            anchors = null;
            error = $"imx TriggerCurrentRecipeLogic anchor ambiguous ({triggerCandidates.Length})";
            return false;
        }
        result.Symbols["imx"] = triggerCandidates[0].Rva;

        anchors = result;
        error = null;
        return true;
    }

    private static bool PickCube(
        Il2CppDumpClass cube,
        Func<Il2CppMethodSignatureInfo, bool> predicate,
        string symbol,
        Il2CppCriticalTextAnchors result,
        out string? error)
    {
        Il2CppDumpMethod[] matches = cube.Methods
            .Where(method => Parse(method.Signature) is Il2CppMethodSignatureInfo parsed && predicate(parsed))
            .ToArray();
        if (matches.Length != 1)
        {
            error = $"{symbol} anchor ambiguous ({matches.Length})";
            return false;
        }
        result.Symbols[symbol] = matches[0].Rva;
        error = null;
        return true;
    }

    private static bool TryUniqueMethod(
        IReadOnlyList<Il2CppDumpMethod> methods,
        Func<Il2CppMethodSignatureInfo, bool> predicate,
        out long rva)
    {
        long[] matches = methods
            .Where(method => Parse(method.Signature) is Il2CppMethodSignatureInfo parsed && predicate(parsed))
            .Select(method => method.Rva)
            .ToArray();
        if (matches.Length == 1)
        {
            rva = matches[0];
            return true;
        }
        rva = 0;
        return false;
    }

    private static Il2CppMethodSignatureInfo? Parse(string signature) =>
        Il2CppMethodSignatureParser.TryParse(signature, out Il2CppMethodSignatureInfo? parsed) ? parsed : null;

    private static bool IsSelfGenericSingleton(Il2CppDumpClass klass) =>
        Regex.IsMatch(
            klass.Declaration,
            @":\s*[\w\.]+<" + Regex.Escape(klass.Name) + @">(?:\s|$)",
            RegexOptions.CultureInvariant);
}
