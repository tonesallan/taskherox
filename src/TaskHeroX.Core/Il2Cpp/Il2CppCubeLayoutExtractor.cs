using System.Text.RegularExpressions;

namespace TaskHeroX.Core.Il2Cpp;

public sealed record Il2CppCubeLayoutSymbols(
    long Grade,
    long RecipeLists,
    long InputList,
    long ActiveRecipe,
    long Busy,
    long SynthesisType,
    long LevelRecipe,
    long CubeLevel,
    IReadOnlyDictionary<string, long> OptionalSymbols);

/// <summary>
/// Porta a descoberta de layout de &lt;ofuscado&gt;.Cube do extrator Python legado.
/// Os nomes dos campos sao ofuscados; a ancora estavel e o tipo estatico + ordem por offset.
/// Nenhum default de build conhecido e aceito aqui: layout ambiguo/incompleto falha fechado.
/// </summary>
public static partial class Il2CppCubeLayoutExtractor
{
    [GeneratedRegex(@"^Dictionary<ERecipeType, List<(\w+)>>$", RegexOptions.CultureInvariant)]
    private static partial Regex RecipeListRegex();

    public static bool TryExtract(
        IReadOnlyList<Il2CppDumpClass> classes,
        out Il2CppCubeLayoutSymbols? symbols,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);

        Il2CppDumpClass? cube = classes.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Split('.').Last(), "Cube", StringComparison.Ordinal) &&
            candidate.Fields.Any(field =>
                string.Equals(field.Type, "List<CubeInData>", StringComparison.Ordinal)))
            ?? classes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "uu.Cube", StringComparison.Ordinal));

        if (cube is null)
        {
            symbols = null;
            error = "Cube class anchor missing";
            return false;
        }

        string recipeType = "uw";
        foreach (Il2CppDumpField field in cube.Fields)
        {
            Match match = RecipeListRegex().Match(field.Type);
            if (match.Success)
            {
                recipeType = match.Groups[1].Value;
                break;
            }
        }

        bool TryStaticByType(string type, int index, out long value)
        {
            long[] matches = cube.Fields
                .Where(field => field.IsStatic && string.Equals(field.Type, type, StringComparison.Ordinal))
                .OrderBy(field => field.Offset)
                .Select(field => field.Offset)
                .ToArray();

            if (index >= 0 && index < matches.Length)
            {
                value = matches[index];
                return true;
            }

            value = 0;
            return false;
        }

        var required = new (string Key, string Type, int Index)[]
        {
            ("cube_grade", "EGradeType", 0),
            ("cube_bers", $"Dictionary<ERecipeType, List<{recipeType}>>", 0),
            ("cube_inlist", "List<CubeInData>", 0),
            ("cube_active", recipeType, 0),
            ("cube_busy", "bool", 0),
            ("cube_type", "EItemSynthesisType", 0),
            ("cube_lvrecipe", recipeType, 1),
        };

        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach ((string key, string type, int index) in required)
        {
            if (!TryStaticByType(type, index, out long value) || value == 0)
            {
                symbols = null;
                error = $"Cube layout missing or ambiguous: {key}";
                return false;
            }
            values[key] = value;
        }

        long[] obscuredInts = cube.Fields
            .Where(field => field.IsStatic && string.Equals(field.Type, "ObscuredInt", StringComparison.Ordinal))
            .Select(field => field.Offset)
            .ToArray();

        if (obscuredInts.Length != 1 || obscuredInts[0] == 0)
        {
            symbols = null;
            error = $"Cube layout requires exactly one static ObscuredInt, found {obscuredInts.Length}";
            return false;
        }

        var optional = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach ((string key, string type, int index) in new (string, string, int)[]
        {
            ("cube_recipe", "SynthesisRecipeInfoData", 0),
            ("cube_beru", $"Dictionary<ERecipeType, {recipeType}>", 0),
            ("cube_resultevt", "Action<ECubeSynthesisResult>", 0),
        })
        {
            if (TryStaticByType(type, index, out long value) && value != 0)
                optional[key] = value;
        }

        long[] ilxCandidates = cube.Methods
            .Where(method =>
            {
                Il2CppMethodSignature? parsed = Il2CppMethodSignatureParser.TryParse(method.Signature);
                return parsed is not null &&
                       parsed.IsStatic &&
                       string.Equals(parsed.ReturnType, "void", StringComparison.Ordinal) &&
                       parsed.ParameterTypes.SequenceEqual(["ERecipeType"], StringComparer.Ordinal);
            })
            .Select(method => method.Rva)
            .Where(rva => rva != 0)
            .OrderBy(rva => rva)
            .ToArray();

        if (ilxCandidates.Length > 0)
            optional["ilx"] = ilxCandidates[0];

        symbols = new Il2CppCubeLayoutSymbols(
            values["cube_grade"],
            values["cube_bers"],
            values["cube_inlist"],
            values["cube_active"],
            values["cube_busy"],
            values["cube_type"],
            values["cube_lvrecipe"],
            obscuredInts[0],
            optional);
        error = null;
        return true;
    }
}
