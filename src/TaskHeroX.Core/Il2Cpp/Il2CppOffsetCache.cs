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
        "ima", "iuw", "izb", "inv_slots_off", "stash_off",
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
