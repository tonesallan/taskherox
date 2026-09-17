using System.Globalization;
using System.Text.RegularExpressions;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Campo extraído de um <c>dump.cs</c> do Il2CppDumper.
/// </summary>
public sealed record Il2CppDumpField(string Type, string Name, long Offset, bool IsStatic);

/// <summary>
/// Método extraído de um <c>dump.cs</c>. O RVA vem do comentário imediatamente anterior ao método.
/// </summary>
public sealed record Il2CppDumpMethod(long Rva, string Signature, string Visibility);

/// <summary>
/// Representação mínima de uma classe do <c>dump.cs</c>, equivalente ao subconjunto usado pelo
/// extrator Python legado (_Klass / _a_parse_dump).
/// </summary>
public sealed class Il2CppDumpClass
{
    private readonly List<Il2CppDumpField> _fields = [];
    private readonly List<Il2CppDumpMethod> _methods = [];

    internal Il2CppDumpClass(string name, string declaration)
    {
        Name = name;
        Declaration = declaration;
    }

    public string Name { get; }
    public string Declaration { get; }
    public IReadOnlyList<Il2CppDumpField> Fields => _fields;
    public IReadOnlyList<Il2CppDumpMethod> Methods => _methods;

    internal void AddField(Il2CppDumpField field) => _fields.Add(field);
    internal void AddMethod(Il2CppDumpMethod method) => _methods.Add(method);
}

/// <summary>
/// Parser somente-texto para o formato <c>dump.cs</c> produzido pelo Il2CppDumper.
///
/// Este primeiro slice porta deliberadamente apenas o contrato já usado pelo extrator Python:
/// nome/declaração da classe, campos com offset e flag static, e métodos associados ao RVA do
/// comentário imediatamente anterior. Nenhuma heurística de símbolos de jogo é aplicada aqui.
/// </summary>
public static class Il2CppDumpParser
{
    private static readonly Regex RvaRegex = new(
        @"//\s*RVA:\s*0x([0-9A-Fa-f]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ClassRegex = new(
        @"^(?:\[.*\]\s*)?(?:public|private|internal|protected)?\s*(?:static\s+|sealed\s+|abstract\s+)*class\s+([^\s:/]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FieldRegex = new(
        @"^\s*(?:\[[^\]]*\]\s*)?(?:public|private|internal|protected)\s+(?:static\s+)?(?:readonly\s+)?(.+?)\s+([A-Za-z_<>][\w<>]*);\s*//\s*0x([0-9A-Fa-f]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MethodRegex = new(
        @"^\s*(?:\[[^\]]*\]\s*)?(public|private|internal|protected)\s+(.*?\S)\s*\{\s*\}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StaticFieldRegex = new(
        @"^\s*(?:\[[^\]]*\]\s*)?(?:public|private|internal|protected)\s+static\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<Il2CppDumpClass> ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    public static IReadOnlyList<Il2CppDumpClass> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        return Parse(reader);
    }

    public static IReadOnlyList<Il2CppDumpClass> Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var classes = new List<Il2CppDumpClass>();
        Il2CppDumpClass? current = null;
        long? pendingRva = null;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.AsSpan().TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                Match rvaMatch = RvaRegex.Match(line);
                if (rvaMatch.Success &&
                    long.TryParse(rvaMatch.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long rva))
                {
                    pendingRva = rva;
                    continue;
                }
            }

            if (StartsWithVisibility(line) && line.Contains(" class ", StringComparison.Ordinal))
            {
                Match classMatch = ClassRegex.Match(line);
                if (classMatch.Success)
                {
                    current = new Il2CppDumpClass(classMatch.Groups[1].Value, line);
                    classes.Add(current);
                    pendingRva = null;
                    continue;
                }
            }

            if (current is null)
                continue;

            Match fieldMatch = FieldRegex.Match(line);
            if (fieldMatch.Success && !fieldMatch.Groups[1].Value.Contains('('))
            {
                if (long.TryParse(fieldMatch.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long offset))
                {
                    current.AddField(new Il2CppDumpField(
                        fieldMatch.Groups[1].Value.Trim(),
                        fieldMatch.Groups[2].Value,
                        offset,
                        StaticFieldRegex.IsMatch(line)));
                }
                continue;
            }

            Match methodMatch = MethodRegex.Match(line);
            if (methodMatch.Success && pendingRva is long methodRva)
            {
                current.AddMethod(new Il2CppDumpMethod(
                    methodRva,
                    methodMatch.Groups[2].Value,
                    methodMatch.Groups[1].Value));
                pendingRva = null;
            }
        }

        return classes;
    }

    private static bool StartsWithVisibility(string line) =>
        line.StartsWith("public ", StringComparison.Ordinal) ||
        line.StartsWith("private ", StringComparison.Ordinal) ||
        line.StartsWith("internal ", StringComparison.Ordinal) ||
        line.StartsWith("protected ", StringComparison.Ordinal);
}
