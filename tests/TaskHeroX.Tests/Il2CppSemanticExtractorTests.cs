using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppSemanticExtractorTests
{
    [Fact]
    public void TryExtractStageStaticSymbols_ResolvesUniqueStructuralStageClass()
    {
        const string dump = """
public static class zz
{
    public static List<StageCache> a; // 0x10
    public static Dictionary<int, StageCache> b; // 0x20
    public static StageCache c; // 0x28
}

public class Other
{
    public static Dictionary<int, StageCache> x; // 0x40
}
""";

        const string script = """
{
  "ScriptMetadata": [
    { "Name": "zz_TypeInfo", "Address": "0x1234" }
  ]
}
""";

        IReadOnlyList<Il2CppDumpClass> classes = Il2CppDumpParser.Parse(dump);
        Il2CppScriptIndex index = Il2CppScriptIndex.Parse(script);

        bool ok = Il2CppSemanticExtractor.TryExtractStageStaticSymbols(
            classes,
            index,
            out StageStaticSymbols? symbols,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(symbols);
        Assert.Equal(0x1234L, symbols.UoTypeInfo);
        Assert.Equal(0x20L, symbols.UoDictionaryOffset);
        Assert.Equal(0x28L, symbols.UoCurrentCacheOffset);
    }

    [Fact]
    public void TryExtractStageStaticSymbols_AcceptsQualifiedStageCacheTypes()
    {
        const string dump = """
public static class aa
{
    public static List<Game.Data.StageCache> list; // 0x30
    public static Dictionary<int, Game.Data.StageCache> dict; // 0x38
}
""";

        const string script = """
{
  "ScriptMetadata": [
    { "Name": "aa_TypeInfo", "Address": 9000 }
  ]
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractStageStaticSymbols(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse(script),
            out StageStaticSymbols? symbols,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(symbols);
        Assert.Equal(0x38L, symbols.UoDictionaryOffset);
        Assert.Null(symbols.UoCurrentCacheOffset);
    }

    [Fact]
    public void TryExtractStageStaticSymbols_RejectsAmbiguousCandidates()
    {
        const string dump = """
public static class aa
{
    public static List<StageCache> a; // 0x10
    public static Dictionary<int, StageCache> b; // 0x20
}
public static class bb
{
    public static List<StageCache> a; // 0x30
    public static Dictionary<int, StageCache> b; // 0x40
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractStageStaticSymbols(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse("{}"),
            out StageStaticSymbols? symbols,
            out string? error);

        Assert.False(ok);
        Assert.Null(symbols);
        Assert.Equal("stage static class ambiguous (2)", error);
    }

    [Fact]
    public void TryExtractStageStaticSymbols_RejectsMissingTypeInfo()
    {
        const string dump = """
public static class aa
{
    public static List<StageCache> a; // 0x10
    public static Dictionary<int, StageCache> b; // 0x20
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractStageStaticSymbols(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse("{}"),
            out StageStaticSymbols? symbols,
            out string? error);

        Assert.False(ok);
        Assert.Null(symbols);
        Assert.Equal("missing TypeInfo for aa", error);
    }

    [Fact]
    public void TryExtractInventorySlotOffsets_ResolvesInventoryAndStashFromSameClass()
    {
        const string dump = """
public class PlayerSaveData
{
    public List<InventorySaveData> aaa; // 0x88
    public List<StashSaveData> bbb; // 0x90
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventorySlotOffsets(
            Il2CppDumpParser.Parse(dump),
            out InventorySlotOffsets? offsets,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(offsets);
        Assert.Equal(0x88L, offsets.InventorySlotsOffset);
        Assert.Equal(0x90L, offsets.StashSlotsOffset);
        Assert.Equal("PlayerSaveData", offsets.DeclaringClass);
    }

    [Fact]
    public void TryExtractInventorySlotOffsets_AcceptsQualifiedSaveDataTypes()
    {
        const string dump = """
public class SaveRoot
{
    public List<Game.Save.InventorySaveData> first; // 0xA0
    public List<Game.Save.StashSaveData> second; // 0xA8
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventorySlotOffsets(
            Il2CppDumpParser.Parse(dump),
            out InventorySlotOffsets? offsets,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(offsets);
        Assert.Equal(0xA0L, offsets.InventorySlotsOffset);
        Assert.Equal(0xA8L, offsets.StashSlotsOffset);
    }

    [Fact]
    public void TryExtractInventorySlotOffsets_PrefersUniqueClassContainingInventoryAndStash()
    {
        const string dump = """
public class DecoySaveData
{
    public List<InventorySaveData> inventory; // 0x40
}

public class PlayerSaveData
{
    public List<InventorySaveData> inventory; // 0x88
    public List<StashSaveData> stash; // 0x90
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventorySlotOffsets(
            Il2CppDumpParser.Parse(dump),
            out InventorySlotOffsets? offsets,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(offsets);
        Assert.Equal(0x88L, offsets.InventorySlotsOffset);
        Assert.Equal(0x90L, offsets.StashSlotsOffset);
        Assert.Equal("PlayerSaveData", offsets.DeclaringClass);
    }

    [Fact]
    public void TryExtractInventorySlotOffsets_RejectsMultiplePairedOwners()
    {
        const string dump = """
public class SaveRootA
{
    public List<InventorySaveData> inventory; // 0x80
    public List<StashSaveData> stash; // 0x88
}

public class SaveRootB
{
    public List<InventorySaveData> inventory; // 0x90
    public List<StashSaveData> stash; // 0x98
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventorySlotOffsets(
            Il2CppDumpParser.Parse(dump),
            out InventorySlotOffsets? offsets,
            out string? error);

        Assert.False(ok);
        Assert.Null(offsets);
        Assert.Equal("inventory/stash owner ambiguous (2)", error);
    }

    [Fact]
    public void TryExtractInventorySlotOffsets_RejectsAmbiguousInventoryFields()
    {
        const string dump = """
public class SaveRoot
{
    public List<InventorySaveData> first; // 0x88
    public List<InventorySaveData> duplicate; // 0x90
    public List<StashSaveData> stash; // 0x98
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventorySlotOffsets(
            Il2CppDumpParser.Parse(dump),
            out InventorySlotOffsets? offsets,
            out string? error);

        Assert.False(ok);
        Assert.Null(offsets);
        Assert.Equal("inventory slot field ambiguous (2)", error);
    }

    [Fact]
    public void TryExtractInventorySlotOffsets_RejectsFieldsFromDifferentClasses()
    {
        const string dump = """
public class InventoryRoot
{
    public List<InventorySaveData> inventory; // 0x88
}
public class StashRoot
{
    public List<StashSaveData> stash; // 0x90
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventorySlotOffsets(
            Il2CppDumpParser.Parse(dump),
            out InventorySlotOffsets? offsets,
            out string? error);

        Assert.False(ok);
        Assert.Null(offsets);
        Assert.Equal("inventory and stash fields belong to different classes", error);
    }

    [Fact]
    public void TryExtractInventoryRootSymbols_ResolvesConcreteAndGenericTypeInfo()
    {
        const string dump = """
public class PlayerSaveData
{
    public List<ItemSaveData> itemSaveDatas; // 0xB0
}

public class bao : nr<bao>
{
    private PlayerSaveData data; // 0x28
}
""";

        const string script = """
{
  "ScriptMetadata": [
    { "Name": "bao_TypeInfo", "Address": "0x5100" },
    { "Name": "nr<bao>_TypeInfo", "Address": "0x5200" }
  ]
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventoryRootSymbols(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse(script),
            out InventoryRootSymbols? symbols,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(symbols);
        Assert.Equal("bao", symbols.InventoryClass);
        Assert.Equal(0x28L, symbols.PlayerSaveDataOffset);
        Assert.Equal(0xB0L, symbols.ItemSaveDataListOffset);
        Assert.Equal(0x5100L, symbols.InventoryClassTypeInfo);
        Assert.Equal(0x5200L, symbols.GenericSingletonTypeInfo);
    }

    [Fact]
    public void TryExtractInventoryRootSymbols_AcceptsConcreteTypeInfoOnly()
    {
        const string dump = """
public class PlayerSaveData
{
    public List<Game.Save.ItemSaveData> items; // 0xA8
}
public class box : baseSingleton<box>
{
    private Game.Save.PlayerSaveData save; // 0x30
}
""";

        const string script = """
{
  "ScriptMetadata": [
    { "Name": "box_TypeInfo", "Address": 7000 }
  ]
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventoryRootSymbols(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse(script),
            out InventoryRootSymbols? symbols,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(symbols);
        Assert.Equal(0x30L, symbols.PlayerSaveDataOffset);
        Assert.Equal(0xA8L, symbols.ItemSaveDataListOffset);
        Assert.Equal(7000L, symbols.InventoryClassTypeInfo);
        Assert.Null(symbols.GenericSingletonTypeInfo);
    }

    [Fact]
    public void TryExtractInventoryRootSymbols_RejectsNonSingletonOwner()
    {
        const string dump = """
public class PlayerSaveData
{
    public List<ItemSaveData> items; // 0xA8
}
public class box
{
    private PlayerSaveData save; // 0x28
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventoryRootSymbols(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse("{}"),
            out InventoryRootSymbols? symbols,
            out string? error);

        Assert.False(ok);
        Assert.Null(symbols);
        Assert.Equal("inventory owner box is not a self-generic singleton", error);
    }

    [Fact]
    public void TryExtractInventoryRootSymbols_RejectsAmbiguousGenericTypeInfo()
    {
        const string dump = """
public class PlayerSaveData
{
    public List<ItemSaveData> items; // 0xA8
}
public class box : root<box>
{
    private PlayerSaveData save; // 0x28
}
""";

        const string script = """
{
  "ScriptMetadata": [
    { "Name": "first<box>_TypeInfo", "Address": 1 },
    { "Name": "second<box>_TypeInfo", "Address": 2 }
  ]
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventoryRootSymbols(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse(script),
            out InventoryRootSymbols? symbols,
            out string? error);

        Assert.False(ok);
        Assert.Null(symbols);
        Assert.Equal("generic inventory TypeInfo ambiguous (2)", error);
    }

    [Fact]
    public void TryExtractInventoryRootSymbols_RejectsMissingTypeInfo()
    {
        const string dump = """
public class PlayerSaveData
{
    public List<ItemSaveData> items; // 0xA8
}
public class box : root<box>
{
    private PlayerSaveData save; // 0x28
}
""";

        bool ok = Il2CppSemanticExtractor.TryExtractInventoryRootSymbols(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse("{}"),
            out InventoryRootSymbols? symbols,
            out string? error);

        Assert.False(ok);
        Assert.Null(symbols);
        Assert.Equal("inventory TypeInfo missing", error);
    }
}
