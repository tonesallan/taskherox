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
        @"^Action<\w+>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SimpleWordType = new(
        @"^\w+$",
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

        // O legado usa re.search(), portanto escolhe o PRIMEIRO metodo publico com a assinatura
        // MoveRequest + Action<T> na ordem textual do dump. Em seguida, procura a ultima classe
        // self-generic singleton declarada antes desse metodo; ela e o ra_class. Reproduzimos essa
        // semantica explicitamente em vez de exigir unicidade global, que nao existe nos builds atuais.
        (Il2CppDumpClass Class, Il2CppDumpMethod Method, Il2CppMethodSignatureInfo Parsed)? moveMatch = null;
        int moveClassIndex = -1;

        for (int classIndex = 0; classIndex < classes.Count && moveMatch is null; classIndex++)
        {
            Il2CppDumpClass klass = classes[classIndex];
            foreach (Il2CppDumpMethod method in klass.Methods)
            {
                Il2CppMethodSignatureInfo? parsed = Parse(method.Signature);
                if (parsed is null ||
                    !string.Equals(method.Visibility, "public", StringComparison.Ordinal) ||
                    parsed.IsStatic ||
                    !SimpleWordType.IsMatch(parsed.ReturnType) ||
                    parsed.ParameterTypes.Count != 2 ||
                    parsed.ParameterTypes[0] != "MoveRequest" ||
                    !ActionType.IsMatch(parsed.ParameterTypes[1]))
                    continue;

                moveMatch = (klass, method, parsed);
                moveClassIndex = classIndex;
                break;
            }
        }

        if (moveMatch is null)
        {
            anchors = null;
            error = "move-manager method missing";
            return false;
        }

        Il2CppDumpClass? moveManagerClass = null;
        for (int i = 0; i <= moveClassIndex; i++)
        {
            if (IsSelfGenericSingleton(classes[i]))
                moveManagerClass = classes[i];
        }

        if (moveManagerClass is null)
        {
            anchors = null;
            error = $"move-manager singleton missing before {moveMatch.Value.Class.Name}.{moveMatch.Value.Parsed.MethodName}@0x{moveMatch.Value.Method.Rva:X}";
            return false;
        }

        result.Symbols["iw"] = moveMatch.Value.Method.Rva;
        result.MoveManagerClass = moveManagerClass.Name;

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

        // O Python legado usa re.search() dentro do segmento da classe Cube: ele escolhe o
        // PRIMEIRO metodo que casa com cada assinatura, preservando tambem a visibilidade.
        if (!PickFirstCube(cube, "public", parsed => parsed.IsStatic && parsed.ReturnType == "void" &&
                                        parsed.ParameterTypes.SequenceEqual(["EItemSynthesisType"]), "ilo", result, out error) ||
            !PickFirstCube(cube, "public", parsed => parsed.IsStatic && parsed.ReturnType == "bool" &&
                                        parsed.ParameterTypes.SequenceEqual(["EGradeType"]), "ili", result, out error) ||
            !PickFirstCube(cube, "private", parsed => parsed.IsStatic && parsed.ReturnType == "void" &&
                                         parsed.ParameterTypes.SequenceEqual([recipeType]), "inf", result, out error) ||
            !PickFirstCube(cube, "public", parsed => parsed.IsStatic && parsed.ReturnType == "bool" &&
                                        parsed.ParameterTypes.SequenceEqual([recipeType]), "ima", result, out error) ||
            !PickFirstCube(cube, "private", parsed => parsed.IsStatic && parsed.ReturnType == "void" &&
                                         parsed.ParameterTypes.SequenceEqual(["int", "CubeInData"]), "iog", result, out error) ||
            !PickFirstCube(cube, "public", parsed => parsed.IsStatic && parsed.ReturnType == "EAddCubeResult" &&
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

    private static bool PickFirstCube(
        Il2CppDumpClass cube,
        string visibility,
        Func<Il2CppMethodSignatureInfo, bool> predicate,
        string symbol,
        Il2CppCriticalTextAnchors result,
        out string? error)
    {
        foreach (Il2CppDumpMethod method in cube.Methods)
        {
            if (!string.Equals(method.Visibility, visibility, StringComparison.Ordinal))
                continue;

            if (Parse(method.Signature) is Il2CppMethodSignatureInfo parsed && predicate(parsed))
            {
                result.Symbols[symbol] = method.Rva;
                error = null;
                return true;
            }
        }

        error = $"{symbol} anchor missing";
        return false;
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
