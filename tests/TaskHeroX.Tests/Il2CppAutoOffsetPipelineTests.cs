using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppAutoOffsetPipelineTests
{
    [Fact]
    public void FinalizeOffsets_ValidatesAndSerializesCriticalContract()
    {
        Il2CppExtractedOffsets offsets = CreateValidOffsets();
        offsets.Symbols["a_extra"] = 11;
        offsets.Symbols["z_extra"] = 99;
        offsets.Ynj.AddRange([0x1000, 0x2000]);
        offsets.InvClass = "InventoryRoot";

        Il2CppAutoOffsetPipelineResult result = Il2CppAutoOffsetPipeline.FinalizeOffsets(offsets);

        Assert.Same(offsets, result.Offsets);
        Assert.Contains($"\"_ver\":{SymbolTable.MinExtractVer}", result.CacheJson, StringComparison.Ordinal);
        Assert.Contains("\"inv_class\":\"InventoryRoot\"", result.CacheJson, StringComparison.Ordinal);
        Assert.Contains("\"ra_class\":\"MoveManager\"", result.CacheJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"jgc\"", result.CacheJson, StringComparison.Ordinal);
        Assert.True(result.CacheJson.IndexOf("\"a_extra\"", StringComparison.Ordinal) <
                    result.CacheJson.IndexOf("\"z_extra\"", StringComparison.Ordinal));
    }

    [Fact]
    public void FinalizeOffsets_RejectsIncompleteExtractionBeforeSerialization()
    {
        var offsets = new Il2CppExtractedOffsets
        {
            RaClass = "MoveManager",
        };
        offsets.Symbols["gra"] = 1;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Il2CppAutoOffsetPipeline.FinalizeOffsets(offsets));

        Assert.Contains("missing or invalid critical symbol", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalizeOffsets_RejectsGenericJgcEvenWhenCriticalContractIsComplete()
    {
        Il2CppExtractedOffsets offsets = CreateValidOffsets();
        offsets.Symbols["jgc"] = 0x1234;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Il2CppAutoOffsetPipeline.FinalizeOffsets(offsets));

        Assert.Contains("generic jgc is forbidden", exception.Message, StringComparison.Ordinal);
    }

    private static Il2CppExtractedOffsets CreateValidOffsets()
    {
        var offsets = new Il2CppExtractedOffsets
        {
            RaClass = "MoveManager",
        };

        foreach (string key in new[]
        {
            "gra", "upd", "llx", "iw", "ilo", "ipu", "imx", "inf", "ili", "iog", "ioa", "ima",
            "iuw", "izb",
            "inv_psd_off", "inv_slots_off", "stash_off", "inv_list_off", "PlayerSaveData.RuneSaveData",
            "itemsave_key", "iteminfo_type", "iteminfo_grade", "iteminfo_synth", "iteminfo_level",
            "psd_common_off", "commonsave_usestorage", "commonsave_maxstage", "commonsave_curstage",
            "CommonSaveData.currentStageWave",
            "uo_ti", "uo_dict", "uo_max", "uo_cur", "uo_wave", "bal_ti", "stage_off", "jgk", "jgd",
            "uimgr_ti", "uimain", "eby",
            "cube_grade", "cube_bers", "cube_inlist", "cube_active",
            "cube_busy", "cube_type", "cube_lvrecipe", "cube_level_off",
        })
        {
            offsets.Symbols[key] = 1;
        }

        offsets.Symbols["inv_klass_ti"] = 2;
        offsets.Ynj.Add(0x1234);
        return offsets;
    }
}
