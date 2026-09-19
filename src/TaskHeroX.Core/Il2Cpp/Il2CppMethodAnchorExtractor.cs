using System.Text.RegularExpressions;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Anchors de métodos que podem ser resolvidos apenas pela estrutura textual do dump.cs, sem
/// disassembly. Mantém os mesmos nomes de símbolos do extrator legado.
/// </summary>
public static class Il2CppMethodAnchorExtractor
{
    private static readonly Regex ItemInfoGetter = new(
        @"^static\s+ItemInfoData\s+[\w<>\.]+\(int\s+a\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MonsterDamageMethod = new(
        @"^(?:(?:override|virtual)\s+)?void\s+[\w<>\.]+\(DamageInfo\s+a,\s*bool\s+b\s*=\s*False\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Resolve o primeiro getter estático <c>ItemInfoData X(int a)</c>, equivalente ao anchor
    /// legado usado para <c>izb</c>.
    /// </summary>
    public static bool TryExtractItemInfoGetter(
        IReadOnlyList<Il2CppDumpClass> classes,
        out long rva)
    {
        ArgumentNullException.ThrowIfNull(classes);

        foreach (Il2CppDumpClass klass in classes)
        {
            foreach (Il2CppDumpMethod method in klass.Methods)
            {
                if (ItemInfoGetter.IsMatch(method.Signature))
                {
                    rva = method.Rva;
                    return true;
                }
            }
        }

        rva = 0;
        return false;
    }

    /// <summary>
    /// Resolve o método de dano de Monster pela assinatura estável
    /// <c>void X(DamageInfo a, bool b = False)</c>. Como o parser atual não preserva o comentário
    /// de vtable Slot, só aceita resultado único dentro de Monster; em ambiguidade falha fechado.
    /// </summary>
    public static bool TryExtractMonsterDamage(
        IReadOnlyList<Il2CppDumpClass> classes,
        out long rva,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);

        Il2CppDumpClass[] monsters = classes
            .Where(klass => string.Equals(klass.Name, "Monster", StringComparison.Ordinal))
            .ToArray();

        if (monsters.Length != 1)
        {
            rva = 0;
            error = $"Monster class ambiguous ({monsters.Length})";
            return false;
        }

        Il2CppDumpMethod[] matches = monsters[0].Methods
            .Where(method => MonsterDamageMethod.IsMatch(method.Signature))
            .ToArray();

        if (matches.Length != 1)
        {
            rva = 0;
            error = $"Monster damage method ambiguous ({matches.Length})";
            return false;
        }

        rva = matches[0].Rva;
        error = null;
        return true;
    }
}
