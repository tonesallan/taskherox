using System.Text.Json;

namespace TaskHeroX.Core.Il2Cpp;

public sealed record Il2CppOffsetParityMismatch(
    string GeneratedKey,
    string ExpectedKey,
    string? GeneratedValue,
    string? ExpectedValue);

public sealed record Il2CppOffsetParityReport(
    IReadOnlyList<Il2CppOffsetParityMismatch> Mismatches)
{
    public bool IsMatch => Mismatches.Count == 0;
}

/// <summary>
/// Compara a saída C# com caches históricos já validados. O comparador conhece apenas aliases de
/// migração deliberados; em especial, o antigo <c>jgc</c> é comparado ao novo <c>jgc_type13</c>, mas
/// jamais é reintroduzido no resultado gerado.
/// </summary>
public static class Il2CppOffsetParityComparer
{
    private static readonly string[] NumericKeys =
    [
        "gra", "upd", "llx", "iw", "ilo", "ipu", "imx", "inf", "ili", "iog", "ioa", "ima",
        "iuw", "izb", "inv_slots_off", "stash_off", "inv_psd_off", "inv_list_off",
        "uo_ti", "uo_dict", "uo_cur_cache", "uo_max", "uo_cur", "uo_wave",
        "bal_ti", "stage_off", "jgk", "jgq", "jgd",
        "uimgr_ti", "uimain", "eby",
        "cube_grade", "cube_bers", "cube_inlist", "cube_active",
        "cube_busy", "cube_type", "cube_lvrecipe",
    ];

    public static Il2CppOffsetParityReport Compare(
        Il2CppExtractedOffsets generated,
        string expectedCacheJson)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(expectedCacheJson);

        using JsonDocument document = JsonDocument.Parse(expectedCacheJson);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("expected offset cache must be a JSON object");

        var mismatches = new List<Il2CppOffsetParityMismatch>();

        foreach (string key in NumericKeys)
            CompareNumeric(generated, root, key, key, requiredExpected: true, mismatches);

        // Caches historicos antigos usam "jgc"; o extrator Python mais recente ja usa
        // "jgc_type13"/"jgc_type2". Aceita ambos como referencia sem jamais reintroduzir
        // o simbolo generico no resultado C#.
        if (HasNonNullProperty(root, "jgc_type13"))
            CompareNumeric(generated, root, "jgc_type13", "jgc_type13", requiredExpected: true, mismatches);
        else if (HasNonNullProperty(root, "jgc"))
            CompareNumeric(generated, root, "jgc_type13", "jgc", requiredExpected: true, mismatches);
        // Alguns caches históricos mais antigos não registravam nenhum validator de entrada.
        // Nesse caso não há valor histórico contra o qual comparar; o resultado C# continua
        // obrigado a passar Il2CppOffsetCache.TryValidate antes de chegar a este comparador.

        if (HasNonNullProperty(root, "jgc_type2"))
            CompareNumeric(generated, root, "jgc_type2", "jgc_type2", requiredExpected: true, mismatches);

        CompareString(generated.InvClass, root, "inv_class", mismatches);
        CompareString(generated.RaClass, root, "ra_class", mismatches);
        CompareYnj(generated, root, mismatches);

        if (HasNonNullProperty(root, "hgr"))
            CompareNumeric(generated, root, "hgr", "hgr", requiredExpected: false, mismatches);

        // Caches historicos muito antigos podem nao registrar cube_level_off, mas o pipeline novo
        // exige que ele seja extraido antes de READY. Os demais extras de Cube sao comparados quando
        // a referencia os possui.
        foreach (string key in new[]
        {
            "cube_level_off", "cube_recipe", "cube_beru", "cube_resultevt", "ilx",
        })
        {
            if (HasNonNullProperty(root, key))
                CompareNumeric(generated, root, key, key, requiredExpected: false, mismatches);
        }

        // O singleton pode ser representado por uma das duas rotas. Compara somente a rota presente
        // no cache histórico, sem exigir que ambas existam simultaneamente.
        if (HasNonNullProperty(root, "inv_klass_ti"))
            CompareNumeric(generated, root, "inv_klass_ti", "inv_klass_ti", true, mismatches);
        if (HasNonNullProperty(root, "bau_ti"))
            CompareNumeric(generated, root, "bau_ti", "bau_ti", true, mismatches);

        return new Il2CppOffsetParityReport(mismatches);
    }

    private static void CompareYnj(
        Il2CppExtractedOffsets generated,
        JsonElement expectedRoot,
        List<Il2CppOffsetParityMismatch> mismatches)
    {
        if (!expectedRoot.TryGetProperty("ynj", out JsonElement expected) ||
            expected.ValueKind != JsonValueKind.Array)
        {
            mismatches.Add(new Il2CppOffsetParityMismatch(
                "ynj", "ynj",
                string.Join(",", generated.Ynj),
                null));
            return;
        }

        long[] expectedValues = expected.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out _))
            .Select(item => item.GetInt64())
            .ToArray();
        long[] generatedValues = [.. generated.Ynj];

        if (!generatedValues.SequenceEqual(expectedValues))
        {
            mismatches.Add(new Il2CppOffsetParityMismatch(
                "ynj", "ynj",
                string.Join(",", generatedValues),
                string.Join(",", expectedValues)));
        }
    }

    private static void CompareNumeric(
        Il2CppExtractedOffsets generated,
        JsonElement expectedRoot,
        string generatedKey,
        string expectedKey,
        bool requiredExpected,
        List<Il2CppOffsetParityMismatch> mismatches)
    {
        bool hasGenerated = generated.Symbols.TryGetValue(generatedKey, out long generatedValue);
        bool hasExpected = TryReadInt64(expectedRoot, expectedKey, out long expectedValue);

        if (!hasExpected && !requiredExpected)
            return;

        if (!hasGenerated || !hasExpected || generatedValue != expectedValue)
        {
            mismatches.Add(new Il2CppOffsetParityMismatch(
                generatedKey,
                expectedKey,
                hasGenerated ? generatedValue.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                hasExpected ? expectedValue.ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
        }
    }

    private static void CompareString(
        string? generatedValue,
        JsonElement expectedRoot,
        string expectedKey,
        List<Il2CppOffsetParityMismatch> mismatches)
    {
        string? expectedValue = expectedRoot.TryGetProperty(expectedKey, out JsonElement element) &&
                                element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

        if (!string.Equals(generatedValue, expectedValue, StringComparison.Ordinal))
        {
            mismatches.Add(new Il2CppOffsetParityMismatch(
                expectedKey,
                expectedKey,
                generatedValue,
                expectedValue));
        }
    }

    private static bool TryReadInt64(JsonElement root, string key, out long value)
    {
        if (root.TryGetProperty(key, out JsonElement element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt64(out value))
            return true;

        value = 0;
        return false;
    }

    private static bool HasNonNullProperty(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement element) &&
        element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
}
