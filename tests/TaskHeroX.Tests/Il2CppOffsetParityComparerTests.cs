using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppOffsetParityComparerTests
{
    [Fact]
    public void Compare_AcceptsHistoricalJgcAsAliasForGeneratedType13()
    {
        Il2CppExtractedOffsets generated = CreateExpectedOffsets();
        string expected = BuildExpectedJson();

        Il2CppOffsetParityReport report = Il2CppOffsetParityComparer.Compare(generated, expected);

        Assert.True(report.IsMatch);
        Assert.Empty(report.Mismatches);
        Assert.DoesNotContain("jgc", generated.Symbols.Keys);
        Assert.Equal(28, generated.Symbols["jgc_type13"]);
    }

    [Fact]
    public void Compare_ReportsSemanticMismatchWithGeneratedAndHistoricalKeys()
    {
        Il2CppExtractedOffsets generated = CreateExpectedOffsets();
        generated.Symbols["jgq"] = 999;
        generated.Symbols["jgc_type13"] = 777;

        Il2CppOffsetParityReport report = Il2CppOffsetParityComparer.Compare(
            generated,
            BuildExpectedJson());

        Assert.False(report.IsMatch);
        Assert.Contains(report.Mismatches, mismatch =>
            mismatch.GeneratedKey == "jgq" && mismatch.ExpectedKey == "jgq" &&
            mismatch.GeneratedValue == "999" && mismatch.ExpectedValue == "27");
        Assert.Contains(report.Mismatches, mismatch =>
            mismatch.GeneratedKey == "jgc_type13" && mismatch.ExpectedKey == "jgc" &&
            mismatch.GeneratedValue == "777" && mismatch.ExpectedValue == "28");
    }

    [Fact]
    public void Compare_UsesOnlyInventorySingletonRoutePresentInHistoricalCache()
    {
        Il2CppExtractedOffsets generated = CreateExpectedOffsets();
        generated.Symbols.Remove("inv_klass_ti");
        generated.Symbols["bau_ti"] = 31;

        string expected = BuildExpectedJson().Replace(
            "\"inv_klass_ti\":30",
            "\"inv_klass_ti\":null,\"bau_ti\":31",
            StringComparison.Ordinal);

        Il2CppOffsetParityReport report = Il2CppOffsetParityComparer.Compare(generated, expected);

        Assert.True(report.IsMatch);
    }

    private static Il2CppExtractedOffsets CreateExpectedOffsets()
    {
        var result = new Il2CppExtractedOffsets
        {
            InvClass = "box",
            RaClass = "move",
        };

        string[] keys =
        [
            "gra", "upd", "llx", "iw", "ilo", "ipu", "imx", "inf", "ili", "iog", "ioa", "ima",
            "iuw", "izb", "inv_slots_off", "stash_off", "inv_psd_off", "inv_list_off",
            "uo_ti", "uo_dict", "uo_cur_cache", "bal_ti", "stage_off", "jgk", "jgq", "jgd",
        ];

        for (int index = 0; index < keys.Length; index++)
            result.Symbols[keys[index]] = index + 1;

        result.Symbols["jgc_type13"] = 28;
        result.Symbols["inv_klass_ti"] = 30;
        return result;
    }

    private static string BuildExpectedJson() => """
{
  "gra":1,
  "upd":2,
  "llx":3,
  "iw":4,
  "ilo":5,
  "ipu":6,
  "imx":7,
  "inf":8,
  "ili":9,
  "iog":10,
  "ioa":11,
  "ima":12,
  "iuw":13,
  "izb":14,
  "inv_slots_off":15,
  "stash_off":16,
  "inv_psd_off":17,
  "inv_list_off":18,
  "uo_ti":19,
  "uo_dict":20,
  "uo_cur_cache":21,
  "bal_ti":22,
  "stage_off":23,
  "jgk":24,
  "jgq":25,
  "jgd":26,
  "jgc":28,
  "inv_class":"box",
  "ra_class":"move",
  "inv_klass_ti":30
}
""";
}
