using System.Text;
using System.Text.Json;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Resultado intermediário da extração antes de ser persistido como offsets_&lt;hash&gt;.json.
/// </summary>
public sealed class Il2CppExtractedOffsets
{
    public Dictionary<string, long> Symbols { get; } = new(StringComparer.Ordinal);
    public List<long> Ynj { get; } = [];
    public string? InvClass { get; set; }
    public string? RaClass { get; set; }
}

/// <summary>
/// Validação fail-closed e serialização determinística do cache de offsets.
/// O formato permanece compatível com SymbolTable.LoadOffsetsJson e com o cache plano legado.
/// </summary>
public static class Il2CppOffsetCache
{
    public const int ExtractorVersion = SymbolTable.MinExtractVer;

    private static readonly string[] CriticalNumericSymbols =
    [
        "gra", "upd", "llx", "iw", "ilo", "ipu", "imx", "inf", "ili", "iog", "ioa",
        "ima", "iuw", "izb",
        "inv_psd_off", "inv_slots_off", "stash_off", "inv_list_off", "PlayerSaveData.RuneSaveData",
        "itemsave_key", "iteminfo_type", "iteminfo_grade", "iteminfo_synth", "iteminfo_level",
        "psd_common_off", "commonsave_usestorage", "commonsave_maxstage", "commonsave_curstage",
        "CommonSaveData.currentStageWave",
        "uo_ti", "uo_dict", "uo_max", "uo_cur", "uo_wave", "bal_ti", "stage_off", "jgk", "jgd",
        "uimgr_ti", "uimain", "eby",
        "cube_grade", "cube_bers", "cube_inlist", "cube_active",
        "cube_busy", "cube_type", "cube_lvrecipe", "cube_level_off",
    ];

    public static bool TryValidate(Il2CppExtractedOffsets offsets, out string? error)
    {
        ArgumentNullException.ThrowIfNull(offsets);

        // O símbolo genérico antigo é semanticamente ambíguo. O pipeline novo nunca deve produzi-lo.
        if (offsets.Symbols.ContainsKey("jgc"))
        {
            error = "generic jgc is forbidden";
            return false;
        }

        foreach (string key in CriticalNumericSymbols)
        {
            if (!offsets.Symbols.TryGetValue(key, out long value) || value == 0)
            {
                error = $"missing critical symbol: {key}";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(offsets.RaClass))
        {
            error = "missing critical symbol: ra_class";
            return false;
        }

        if (offsets.Ynj.Count == 0 || offsets.Ynj.Any(value => value == 0))
        {
            error = "missing critical symbol: ynj";
            return false;
        }

        bool hasInventorySingleton =
            (offsets.Symbols.TryGetValue("inv_klass_ti", out long invKlassTi) && invKlassTi != 0) ||
            (offsets.Symbols.TryGetValue("bau_ti", out long bauTi) && bauTi != 0);

        if (!hasInventorySingleton)
        {
            error = "missing inventory singleton: inv_klass_ti or bau_ti";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Valida bytes de cache antes de qualquer carga no SymbolTable. Reusa exatamente o mesmo
    /// contrato READY do resultado extraido e tambem exige a versao atual do cache em disco/feed.
    /// </summary>
    public static bool TryValidateSerialized(ReadOnlyMemory<byte> json, out string? error)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "cache root must be an object";
                return false;
            }

            if (!doc.RootElement.TryGetProperty("_ver", out JsonElement versionElement) ||
                !versionElement.TryGetInt32(out int version) ||
                version < ExtractorVersion)
            {
                error = $"cache version must be >= {ExtractorVersion}";
                return false;
            }

            var offsets = new Il2CppExtractedOffsets();
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Number:
                        if (!string.Equals(property.Name, "_ver", StringComparison.Ordinal) &&
                            property.Value.TryGetInt64(out long number))
                            offsets.Symbols[property.Name] = number;
                        break;
                    case JsonValueKind.Array when string.Equals(property.Name, "ynj", StringComparison.Ordinal):
                        foreach (JsonElement item in property.Value.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out long ynj))
                                offsets.Ynj.Add(ynj);
                        break;
                    case JsonValueKind.String when string.Equals(property.Name, "inv_class", StringComparison.Ordinal):
                        offsets.InvClass = property.Value.GetString();
                        break;
                    case JsonValueKind.String when string.Equals(property.Name, "ra_class", StringComparison.Ordinal):
                        offsets.RaClass = property.Value.GetString();
                        break;
                }
            }

            return TryValidate(offsets, out error);
        }
        catch (JsonException ex)
        {
            error = $"invalid cache json: {ex.Message}";
            return false;
        }
    }

    public static string Serialize(Il2CppExtractedOffsets offsets)
    {
        ArgumentNullException.ThrowIfNull(offsets);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("_ver", ExtractorVersion);

            foreach ((string key, long value) in offsets.Symbols.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (string.Equals(key, "jgc", StringComparison.Ordinal))
                    throw new InvalidOperationException("generic jgc is forbidden");
                writer.WriteNumber(key, value);
            }

            if (offsets.Ynj.Count > 0)
            {
                writer.WritePropertyName("ynj");
                writer.WriteStartArray();
                foreach (long value in offsets.Ynj)
                    writer.WriteNumberValue(value);
                writer.WriteEndArray();
            }

            if (!string.IsNullOrWhiteSpace(offsets.InvClass))
                writer.WriteString("inv_class", offsets.InvClass);

            if (!string.IsNullOrWhiteSpace(offsets.RaClass))
                writer.WriteString("ra_class", offsets.RaClass);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
