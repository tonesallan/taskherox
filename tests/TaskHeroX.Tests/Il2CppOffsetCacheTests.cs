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

        offsets = CreateValidOffsets();
        offsets.Symbols.Remove("cube_grade");

        ok = Il2CppOffsetCache.TryValidate(offsets, out error);

        Assert.False(ok);
        Assert.Equal("missing critical symbol: cube_grade", error);
    }

    [Fact]
    public void TryValidateSerialized_UsesTheSameReadyContract()
    {
        Il2CppExtractedOffsets offsets = CreateValidOffsets();
        string ready = Il2CppOffsetCache.Serialize(offsets);

        Assert.True(
            Il2CppOffsetCache.TryValidateSerialized(System.Text.Encoding.UTF8.GetBytes(ready), out string? readyError),
            readyError);

        byte[] partial = System.Text.Encoding.UTF8.GetBytes(
            $"{{\"_ver\":{SymbolTable.MinExtractVer},\"gra\":1}}");
        Assert.False(Il2CppOffsetCache.TryValidateSerialized(partial, out string? partialError));
        Assert.Equal("missing critical symbol: upd", partialError);

        byte[] old = System.Text.Encoding.UTF8.GetBytes(ready.Replace(
            $"\"_ver\":{SymbolTable.MinExtractVer}",
            $"\"_ver\":{SymbolTable.MinExtractVer - 1}",
            StringComparison.Ordinal));
        Assert.False(Il2CppOffsetCache.TryValidateSerialized(old, out string? oldError));
        Assert.Equal($"cache version must be >= {SymbolTable.MinExtractVer}", oldError);

        byte[] genericJgc = System.Text.Encoding.UTF8.GetBytes(ready.Replace(
            "{",
            "{\"jgc\":4660,",
            StringComparison.Ordinal));
        Assert.False(Il2CppOffsetCache.TryValidateSerialized(genericJgc, out string? jgcError));
        Assert.Equal("generic jgc is forbidden", jgcError);
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
        Assert.Equal([0x1234L, 0x1000L, 0x2000L], table.Ynj);
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
