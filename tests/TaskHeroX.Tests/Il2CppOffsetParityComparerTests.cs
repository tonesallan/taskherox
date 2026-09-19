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
            mismatch.GeneratedValue == "999" && mismatch.ExpectedValue == "25");
        Assert.Contains(report.Mismatches, mismatch =>
            mismatch.GeneratedKey == "jgc_type13" && mismatch.ExpectedKey == "jgc" &&
            mismatch.GeneratedValue == "777" && mismatch.ExpectedValue == "28");
    }

    [Fact]
    public void Compare_ReportsBuildScopedRuneOffsetMismatch()
    {
        Il2CppExtractedOffsets generated = CreateExpectedOffsets();
        generated.Symbols["PlayerSaveData.RuneSaveData"] = 999;

        Il2CppOffsetParityReport report = Il2CppOffsetParityComparer.Compare(
            generated,
            BuildExpectedJson());

        Assert.False(report.IsMatch);
        Assert.Contains(report.Mismatches, mismatch =>
            mismatch.GeneratedKey == "PlayerSaveData.RuneSaveData" &&
            mismatch.ExpectedKey == "PlayerSaveData.RuneSaveData" &&
            mismatch.GeneratedValue == "999" &&
            mismatch.ExpectedValue == "52");
    }

    [Fact]
    public void Compare_AcceptsCurrentSplitStageValidatorKeys()
    {
        Il2CppExtractedOffsets generated = CreateExpectedOffsets();
        generated.Symbols["jgc_type2"] = 29;

        string expected = BuildExpectedJson()
            .Replace("\"jgc\":28", "\"jgc_type13\":28,\"jgc_type2\":29", StringComparison.Ordinal)
            .Replace("\"inv_klass_ti\":30", "\"inv_klass_ti\":30,\"bau_ti\":null", StringComparison.Ordinal);

        Il2CppOffsetParityReport report = Il2CppOffsetParityComparer.Compare(generated, expected);

        Assert.True(report.IsMatch);
        Assert.Empty(report.Mismatches);
        Assert.DoesNotContain("jgc", generated.Symbols.Keys);
    }

    [Fact]
    public void Compare_AcceptsOlderHistoricalCacheWithoutStageValidator()
    {
        Il2CppExtractedOffsets generated = CreateExpectedOffsets();

        string expected = BuildExpectedJson()
            .Replace("\"jgc\":28,", string.Empty, StringComparison.Ordinal);

        Il2CppOffsetParityReport report = Il2CppOffsetParityComparer.Compare(generated, expected);

        Assert.True(report.IsMatch);
        Assert.Empty(report.Mismatches);
        Assert.Equal(28, generated.Symbols["jgc_type13"]);
        Assert.DoesNotContain("jgc", generated.Symbols.Keys);
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
        result.Symbols["uo_max"] = 31;
        result.Symbols["uo_cur"] = 32;
        result.Symbols["uo_wave"] = 33;
        result.Symbols["uimgr_ti"] = 34;
        result.Symbols["uimain"] = 35;
        result.Symbols["eby"] = 36;
        result.Symbols["hgr"] = 37;
        result.Ynj.Add(38);
        result.Symbols["cube_grade"] = 40;
        result.Symbols["cube_bers"] = 41;
        result.Symbols["cube_inlist"] = 42;
        result.Symbols["cube_active"] = 43;
        result.Symbols["cube_busy"] = 44;
        result.Symbols["cube_type"] = 45;
        result.Symbols["cube_lvrecipe"] = 46;
        result.Symbols["cube_level_off"] = 47;
        result.Symbols["cube_recipe"] = 48;
        result.Symbols["cube_beru"] = 49;
        result.Symbols["cube_resultevt"] = 50;
        result.Symbols["ilx"] = 51;
        result.Symbols["PlayerSaveData.RuneSaveData"] = 52;
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
  "PlayerSaveData.RuneSaveData":52,
  "uo_ti":19,
  "uo_dict":20,
  "uo_cur_cache":21,
  "uo_max":31,
  "uo_cur":32,
  "uo_wave":33,
  "bal_ti":22,
  "stage_off":23,
  "jgk":24,
  "jgq":25,
  "jgd":26,
  "jgc":28,
  "uimgr_ti":34,
  "uimain":35,
  "eby":36,
  "hgr":37,
  "cube_grade":40,
  "cube_bers":41,
  "cube_inlist":42,
  "cube_active":43,
  "cube_busy":44,
  "cube_type":45,
  "cube_lvrecipe":46,
  "cube_level_off":47,
  "cube_recipe":48,
  "cube_beru":49,
  "cube_resultevt":50,
  "ilx":51,
  "ynj":[38],
  "inv_class":"box",
  "ra_class":"move",
  "inv_klass_ti":30
}
""";
}
