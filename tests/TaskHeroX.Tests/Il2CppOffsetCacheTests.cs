using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppOffsetCacheTests
{
    [Fact]
    public void TryValidate_AcceptsLegacyCriticalContractWithoutGenericJgc()
    {
        Il2CppExtractedOffsets offsets = CreateValidOffsets();

        bool ok = Il2CppOffsetCache.TryValidate(offsets, out string? error);

        Assert.True(ok, error);
    }

    [Fact]
    public void TryValidate_RejectsGenericJgc()
    {
        Il2CppExtractedOffsets offsets = CreateValidOffsets();
        offsets.Symbols["jgc"] = 0x1234;

        bool ok = Il2CppOffsetCache.TryValidate(offsets, out string? error);

        Assert.False(ok);
        Assert.Equal("generic jgc is forbidden", error);
    }

    [Fact]
    public void TryValidate_RejectsMissingInventorySingleton()
    {
        Il2CppExtractedOffsets offsets = CreateValidOffsets();
        offsets.Symbols.Remove("inv_klass_ti");

        bool ok = Il2CppOffsetCache.TryValidate(offsets, out string? error);

        Assert.False(ok);
        Assert.Equal("missing inventory singleton: inv_klass_ti or bau_ti", error);
    }

    [Fact]
    public void TryValidate_RejectsMissingRuntimeRequiredAnchors()
    {
        Il2CppExtractedOffsets offsets = CreateValidOffsets();
        offsets.Symbols.Remove("eby");

        bool ok = Il2CppOffsetCache.TryValidate(offsets, out string? error);

        Assert.False(ok);
        Assert.Equal("missing critical symbol: eby", error);

        offsets = CreateValidOffsets();
        offsets.Ynj.Clear();

        ok = Il2CppOffsetCache.TryValidate(offsets, out error);

        Assert.False(ok);
        Assert.Equal("missing critical symbol: ynj", error);
    }

    [Fact]
    public void Serialize_IsDeterministicAndLoadableBySymbolTable()
    {
        Il2CppExtractedOffsets offsets = CreateValidOffsets();
        offsets.Symbols["z_extra"] = 99;
        offsets.Symbols["a_extra"] = 11;
        offsets.Ynj.AddRange([0x1000, 0x2000]);
        offsets.InvClass = "InventoryConcrete";

        string first = Il2CppOffsetCache.Serialize(offsets);
        string second = Il2CppOffsetCache.Serialize(offsets);

        Assert.Equal(first, second);
        Assert.Contains($"\"_ver\":{SymbolTable.MinExtractVer}", first, StringComparison.Ordinal);
        Assert.True(first.IndexOf("\"a_extra\"", StringComparison.Ordinal) <
                    first.IndexOf("\"z_extra\"", StringComparison.Ordinal));
        Assert.DoesNotContain("\"jgc\"", first, StringComparison.Ordinal);

        var table = new SymbolTable();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(first));
        Assert.True(table.LoadOffsetsJson(stream, requireVersion: true));
        Assert.Equal(11, table.Get("a_extra"));
        Assert.Equal([0x1000L, 0x2000L], table.Ynj);
        Assert.Equal("InventoryConcrete", table.InvClass);
        Assert.Equal("MoveItemsManager", table.RaClass);
    }

    private static Il2CppExtractedOffsets CreateValidOffsets()
    {
        var offsets = new Il2CppExtractedOffsets
        {
            RaClass = "MoveItemsManager",
        };

        foreach (string key in new[]
        {
            "gra", "upd", "llx", "iw", "ilo", "ipu", "imx", "inf", "ili", "iog", "ioa",
            "ima", "iuw", "izb", "inv_slots_off", "stash_off",
            "uimgr_ti", "uimain", "eby",
        })
        {
            offsets.Symbols[key] = 1;
        }

        offsets.Symbols["inv_klass_ti"] = 2;
        offsets.Ynj.Add(0x1234);
        return offsets;
    }
}
