using System.Text.RegularExpressions;
using Iced.Intel;

namespace TaskHeroX.Core.Il2Cpp;

public sealed record Il2CppRuntimeRequiredAnchors(
    long UiManagerTypeInfo,
    long UiMainOffset,
    long Eby,
    long? Hgr,
    IReadOnlyList<long> Ynj);

/// <summary>
/// Anchors necessários por features que a UI considera "READY": ACTk e abertura do cubo/AutoFuse.
/// Porta as mesmas guardas fail-closed do extrator Python legado e nunca usa nomes de métodos
/// ofuscados como âncoras.
/// </summary>
public static class Il2CppRuntimeRequiredAnchorExtractor
{
    private static readonly Regex GenericUiManagerTypeInfo = new(
        @"^\w+<UIManager>_TypeInfo$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryExtract(
        IReadOnlyList<Il2CppDumpClass> classes,
        Il2CppScriptIndex script,
        Il2CppPeImage image,
        out Il2CppRuntimeRequiredAnchors? anchors,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(image);

        if (!TryExtractActk(classes, out long ynj, out error))
        {
            anchors = null;
            return false;
        }

        if (!TryExtractUiManagerTypeInfo(script, out long uiManagerTypeInfo, out error))
        {
            anchors = null;
            return false;
        }

        Il2CppDumpClass[] uiManagers = classes
            .Where(klass => string.Equals(klass.Name, "UIManager", StringComparison.Ordinal))
            .ToArray();
        if (uiManagers.Length != 1)
        {
            anchors = null;
            error = $"UIManager class ambiguous ({uiManagers.Length})";
            return false;
        }

        long[] uiMainOffsets = uiManagers[0].Fields
            .Where(field => !field.IsStatic &&
                            string.Equals(field.Name, "ui_main", StringComparison.Ordinal))
            .Select(field => field.Offset)
            .Distinct()
            .ToArray();
        if (uiMainOffsets.Length != 1 || uiMainOffsets[0] == 0)
        {
            anchors = null;
            error = $"UIManager.ui_main anchor ambiguous ({uiMainOffsets.Length})";
            return false;
        }

        if (!TryExtractEby(classes, script, image, out long eby, out error))
        {
            anchors = null;
            return false;
        }

        long? hgr = TryExtractHgr(classes, script, image);

        anchors = new Il2CppRuntimeRequiredAnchors(
            uiManagerTypeInfo,
            uiMainOffsets[0],
            eby,
            hgr,
            [ynj]);
        error = null;
        return true;
    }

    private static bool TryExtractActk(
        IReadOnlyList<Il2CppDumpClass> classes,
        out long ynj,
        out string? error)
    {
        Il2CppDumpClass[] detectors = classes
            .Where(klass => string.Equals(klass.Name, "InjectionDetector", StringComparison.Ordinal))
            .ToArray();

        if (detectors.Length != 1)
        {
            ynj = 0;
            error = $"InjectionDetector class ambiguous ({detectors.Length})";
            return false;
        }

        Il2CppDumpClass detector = detectors[0];

        // Mesmo shape-guard do Python: exatamente 8 métodos e exatamente 3 static void().
        // Se a classe mudar, não tentamos adivinhar o alvo que será patchado.
        if (detector.Methods.Count != 8)
        {
            ynj = 0;
            error = $"InjectionDetector shape changed ({detector.Methods.Count} methods)";
            return false;
        }

        long[] staticVoidNoArgs = detector.Methods
            .Where(method =>
                Il2CppMethodSignatureParser.TryParse(method.Signature, out Il2CppMethodSignatureInfo? parsed) &&
                parsed is not null &&
                parsed.IsStatic &&
                string.Equals(parsed.ReturnType, "void", StringComparison.Ordinal) &&
                parsed.ParameterTypes.Count == 0)
            .Select(method => method.Rva)
            .OrderBy(value => value)
            .ToArray();

        if (staticVoidNoArgs.Length != 3)
        {
            ynj = 0;
            error = $"InjectionDetector static void() shape changed ({staticVoidNoArgs.Length})";
            return false;
        }

        ynj = staticVoidNoArgs[^1];
        error = null;
        return ynj != 0;
    }

    private static bool TryExtractUiManagerTypeInfo(
        Il2CppScriptIndex script,
        out long typeInfo,
        out string? error)
    {
        long[] generic = script.MetadataAddresses
            .Where(pair => GenericUiManagerTypeInfo.IsMatch(pair.Key))
            .Select(pair => pair.Value)
            .Distinct()
            .ToArray();

        if (generic.Length == 1 && generic[0] != 0)
        {
            typeInfo = generic[0];
            error = null;
            return true;
        }

        if (generic.Length > 1)
        {
            typeInfo = 0;
            error = $"UIManager generic TypeInfo ambiguous ({generic.Length})";
            return false;
        }

        if (script.TryGetMetadataAddress("UIManager_TypeInfo", out long concrete) && concrete != 0)
        {
            typeInfo = concrete;
            error = null;
            return true;
        }

        typeInfo = 0;
        error = "UIManager TypeInfo missing";
        return false;
    }

    private static bool TryExtractEby(
        IReadOnlyList<Il2CppDumpClass> classes,
        Il2CppScriptIndex script,
        Il2CppPeImage image,
        out long eby,
        out string? error)
    {
        Il2CppDumpClass[] uiMainClasses = classes
            .Where(klass => string.Equals(klass.Name, "UI_Main", StringComparison.Ordinal))
            .ToArray();
        if (uiMainClasses.Length != 1)
        {
            eby = 0;
            error = $"UI_Main class ambiguous ({uiMainClasses.Length})";
            return false;
        }

        Il2CppDumpClass uiMain = uiMainClasses[0];

        long[] cubeOffsets = uiMain.Fields
            .Where(field => !field.IsStatic &&
                            string.Equals(field.Name, "button_Cube", StringComparison.Ordinal))
            .Select(field => field.Offset)
            .Distinct()
            .ToArray();
        if (cubeOffsets.Length != 1)
        {
            eby = 0;
            error = $"UI_Main.button_Cube anchor ambiguous ({cubeOffsets.Length})";
            return false;
        }

        long[] awakeMethods = uiMain.Methods
            .Where(method =>
                Il2CppMethodSignatureParser.TryParse(method.Signature, out Il2CppMethodSignatureInfo? parsed) &&
                parsed is not null &&
                !parsed.IsStatic &&
                string.Equals(parsed.MethodName, "Awake", StringComparison.Ordinal) &&
                parsed.ParameterTypes.Count == 0)
            .Select(method => method.Rva)
            .Distinct()
            .ToArray();
        if (awakeMethods.Length != 1)
        {
            eby = 0;
            error = $"UI_Main.Awake anchor ambiguous ({awakeMethods.Length})";
            return false;
        }

        bool armed = false;
        foreach (Instruction instruction in Il2CppDisassembly.Decode(image, script.Addresses, awakeMethods[0]))
        {
            if (!armed)
            {
                if (instruction.Mnemonic == Mnemonic.Mov &&
                    HasMemoryOperand(instruction) &&
                    !instruction.IsIPRelativeMemoryOperand &&
                    unchecked((long)instruction.MemoryDisplacement64) == cubeOffsets[0])
                {
                    armed = true;
                }
                continue;
            }

            if (instruction.Mnemonic != Mnemonic.Mov ||
                instruction.OpCount < 2 ||
                instruction.Op0Kind != OpKind.Register ||
                instruction.Op0Register != Register.R8 ||
                instruction.Op1Kind != OpKind.Memory ||
                !instruction.IsIPRelativeMemoryOperand)
                continue;

            long slot = checked((long)instruction.IPRelativeMemoryAddress);
            if (script.TryGetMethodAddress(slot, out long methodAddress) && methodAddress != 0)
            {
                eby = methodAddress;
                error = null;
                return true;
            }

            break;
        }

        eby = 0;
        error = "UI_Main button_Cube handler anchor missing";
        return false;
    }

    private static long? TryExtractHgr(
        IReadOnlyList<Il2CppDumpClass> classes,
        Il2CppScriptIndex script,
        Il2CppPeImage image)
    {
        Il2CppDumpClass? uiManager = classes
            .SingleOrDefault(klass => string.Equals(klass.Name, "UIManager", StringComparison.Ordinal));
        if (uiManager is null)
            return null;

        HashSet<long> candidates = uiManager.Methods
            .Where(method =>
                Il2CppMethodSignatureParser.TryParse(method.Signature, out Il2CppMethodSignatureInfo? parsed) &&
                parsed is not null &&
                !parsed.IsStatic &&
                string.Equals(parsed.ReturnType, "void", StringComparison.Ordinal) &&
                parsed.ParameterTypes.Count == 0)
            .Select(method => method.Rva)
            .ToHashSet();

        if (candidates.Count == 0)
            return null;

        var hits = new Dictionary<long, int>();
        foreach (Il2CppDumpClass guiding in classes.Where(klass =>
                     klass.Name.Contains("Guiding", StringComparison.Ordinal)))
        {
            foreach (Il2CppDumpMethod method in guiding.Methods)
            {
                foreach (Il2CppDirectFlow flow in Il2CppDisassembly.DirectFlows(
                             image, script.Addresses, method.Rva))
                {
                    if (flow.Kind != Mnemonic.Call || !candidates.Contains(flow.TargetRva))
                        continue;
                    hits[flow.TargetRva] = hits.GetValueOrDefault(flow.TargetRva) + 1;
                }
            }
        }

        if (hits.Count == 0)
            return null;

        var ordered = hits.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).ToArray();
        if (ordered.Length > 1 && ordered[0].Value <= ordered[1].Value)
            return null;

        return ordered[0].Key;
    }

    private static bool HasMemoryOperand(in Instruction instruction)
    {
        for (int operand = 0; operand < instruction.OpCount; operand++)
            if (instruction.GetOpKind(operand) == OpKind.Memory)
                return true;
        return false;
    }
}
