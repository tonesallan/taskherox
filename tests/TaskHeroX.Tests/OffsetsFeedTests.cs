using System.Text;
using TaskHeroX.Core.Il2Cpp;
using TaskHeroX.Core.Update;

namespace TaskHeroX.Tests;

public sealed class OffsetsFeedTests
{
    [Fact]
    public async Task TryPublishValidatedCacheAsync_ReplacesFinalOnlyWithReadyCache()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TaskHeroX-feed-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            string path = Path.Combine(dir, "offsets_test.json");
            await File.WriteAllTextAsync(path, "old-cache");

            byte[] ready = Encoding.UTF8.GetBytes(Il2CppOffsetCache.Serialize(CreateValidOffsets()));

            Assert.True(await OffsetsFeed.TryPublishValidatedCacheAsync(path, ready));
            Assert.Equal(ready, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TryPublishValidatedCacheAsync_InvalidBodyPreservesExistingCache()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TaskHeroX-feed-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            string path = Path.Combine(dir, "offsets_test.json");
            byte[] original = Encoding.UTF8.GetBytes("known-good-existing-cache");
            await File.WriteAllBytesAsync(path, original);

            Assert.False(await OffsetsFeed.TryPublishValidatedCacheAsync(
                path,
                Encoding.UTF8.GetBytes("{\"_ver\":9,\"gra\":1}")));

            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TryPublishValidatedCacheAsync_CancellationPreservesExistingCache()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TaskHeroX-feed-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            string path = Path.Combine(dir, "offsets_test.json");
            byte[] original = Encoding.UTF8.GetBytes("known-good-existing-cache");
            await File.WriteAllBytesAsync(path, original);

            byte[] ready = Encoding.UTF8.GetBytes(Il2CppOffsetCache.Serialize(CreateValidOffsets()));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                OffsetsFeed.TryPublishValidatedCacheAsync(path, ready, cts.Token));

            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static Il2CppExtractedOffsets CreateValidOffsets()
    {
        var offsets = new Il2CppExtractedOffsets { RaClass = "MoveItemsManager" };

        foreach (string key in new[]
        {
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
        })
            offsets.Symbols[key] = 1;

        offsets.Symbols["inv_klass_ti"] = 2;
        offsets.Ynj.Add(0x1234);
        return offsets;
    }
}
