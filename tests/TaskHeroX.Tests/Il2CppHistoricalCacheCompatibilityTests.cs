using System.Text;
using System.Text.Json;
using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppHistoricalCacheCompatibilityTests
{
    private static readonly string[] HistoricalHashes =
    [
        "139467f3ad72",
        "535f977c07ca",
        "9655ccb67d45",
        "a8b994ee3986",
        "c824ed7a2bb1",
        "d2651aeb57f0",
        "f2be0e20ad2b",
        "c265dc8bc7aa",
    ];

    private static readonly string[] ParityNumericKeys =
    [
        "gra", "upd", "llx", "iw", "ilo", "ipu", "imx", "inf", "ili", "iog", "ioa", "ima",
        "iuw", "izb", "inv_slots_off", "stash_off", "inv_psd_off", "inv_list_off",
        "uo_ti", "uo_dict", "uo_cur_cache", "uo_max", "uo_cur", "uo_wave",
        "bal_ti", "stage_off", "jgk", "jgq", "jgd",
    ];

    [Fact]
    public void EmbeddedHistoricalCachesRemainLoadableAndParityComparable()
    {
        var assembly = typeof(SymbolTable).Assembly;
        string[] resources = assembly.GetManifestResourceNames();

        foreach (string hash in HistoricalHashes)
        {
            string resource = Assert.Single(
                resources,
                name => name.EndsWith($"offsets_{hash}.json", StringComparison.OrdinalIgnoreCase));

            using Stream stream = Assert.IsAssignableFrom<Stream>(
                assembly.GetManifestResourceStream(resource));
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string json = reader.ReadToEnd();

            var table = new SymbolTable();
            using var cacheStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            Assert.True(table.LoadOffsetsJson(cacheStream, requireVersion: false));

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.True(root.TryGetProperty("inv_slots_off", out JsonElement invElement));
            Assert.True(root.TryGetProperty("stash_off", out JsonElement stashElement));
            Assert.Equal(8, stashElement.GetInt64() - invElement.GetInt64());

            var generated = new Il2CppExtractedOffsets
            {
                InvClass = root.GetProperty("inv_class").GetString(),
                RaClass = root.GetProperty("ra_class").GetString(),
            };

            foreach (string key in ParityNumericKeys)
            {
                Assert.True(root.TryGetProperty(key, out JsonElement value), $"{hash}: missing {key}");
                generated.Symbols[key] = value.GetInt64();
            }

            if (root.TryGetProperty("inv_klass_ti", out JsonElement invTi) &&
                invTi.ValueKind == JsonValueKind.Number)
                generated.Symbols["inv_klass_ti"] = invTi.GetInt64();

            if (root.TryGetProperty("bau_ti", out JsonElement bauTi) &&
                bauTi.ValueKind == JsonValueKind.Number)
                generated.Symbols["bau_ti"] = bauTi.GetInt64();

            if (root.TryGetProperty("jgc_type13", out JsonElement type13) &&
                type13.ValueKind == JsonValueKind.Number)
                generated.Symbols["jgc_type13"] = type13.GetInt64();
            else if (root.TryGetProperty("jgc", out JsonElement historicalJgc) &&
                     historicalJgc.ValueKind == JsonValueKind.Number)
                generated.Symbols["jgc_type13"] = historicalJgc.GetInt64();
            else
                generated.Symbols["jgc_type13"] = 1; // no historical value to compare

            if (root.TryGetProperty("jgc_type2", out JsonElement type2) &&
                type2.ValueKind == JsonValueKind.Number)
                generated.Symbols["jgc_type2"] = type2.GetInt64();

            Il2CppOffsetParityReport report = Il2CppOffsetParityComparer.Compare(generated, json);

            Assert.True(
                report.IsMatch,
                $"{hash}: " + string.Join(
                    "; ",
                    report.Mismatches.Select(m =>
                        $"{m.GeneratedKey}={m.GeneratedValue ?? "—"} != {m.ExpectedKey}={m.ExpectedValue ?? "—"}")));
            Assert.DoesNotContain("jgc", generated.Symbols.Keys);
        }
    }
}
