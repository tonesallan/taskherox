using System.Text.RegularExpressions;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Assinatura normalizada de método do dump.cs. O formato corresponde ao subconjunto retornado por
/// <c>_a_sig</c> no extrator Python legado e é usado pelos próximos anchors de fluxo/disassembly.
/// </summary>
public sealed record Il2CppMethodSignatureInfo(
    bool IsStatic,
    string ReturnType,
    string MethodName,
    IReadOnlyList<string> ParameterTypes);

public static class Il2CppMethodSignatureParser
{
    private static readonly Regex SignatureRegex = new(
        @"^(?:(static)\s+)?(?:(virtual|override|abstract)\s+)?([\w<>,\.\[\]]+)\s+([\w<>\.]+)\(([^)]*)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string signature, out Il2CppMethodSignatureInfo? info)
    {
        ArgumentNullException.ThrowIfNull(signature);

        string normalized = signature.Replace("readonly ", string.Empty, StringComparison.Ordinal).Trim();
        Match match = SignatureRegex.Match(normalized);
        if (!match.Success)
        {
            info = null;
            return false;
        }

        List<string> parameterTypes = ParseParameterTypes(match.Groups[5].Value);
        info = new Il2CppMethodSignatureInfo(
            IsStatic: match.Groups[1].Success,
            ReturnType: match.Groups[3].Value,
            MethodName: match.Groups[4].Value,
            ParameterTypes: parameterTypes);
        return true;
    }

    private static List<string> ParseParameterTypes(string raw)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return args;

        int depth = 0;
        var current = new System.Text.StringBuilder();

        foreach (char ch in raw)
        {
            if (ch is '<' or '(' or '[')
                depth++;
            else if (ch is '>' or ')' or ']')
                depth--;

            if (ch == ',' && depth == 0)
            {
                args.Add(NormalizeParameter(current.ToString()));
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        args.Add(NormalizeParameter(current.ToString()));
        return args;
    }

    private static string NormalizeParameter(string parameter)
    {
        string withoutDefault = Regex.Replace(parameter.Trim(), @"\s*=.*$", string.Empty);
        int lastSpace = withoutDefault.LastIndexOf(' ');
        return lastSpace >= 0
            ? withoutDefault[..lastSpace].Trim()
            : withoutDefault.Trim();
    }
}
