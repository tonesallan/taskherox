using System.Buffers.Binary;
using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppFoundationExtractorTests
{
    [Fact]
    public void TryExtract_ComposesPortedGroupsWithoutGenericJgc()
    {
        const string dump = """
public static class Uo
{
    public static List<StageCache> list; // 0x10
    public static Dictionary<int, StageCache> dict; // 0x18
    public static StageCache current; // 0x20
    // RVA: 0x1100
    public static void enter(int key) { }
    // RVA: 0x1200
    public static bool unlocked(int key) { }
    // RVA: 0x1300
    public static void boss(StageCache cache, Action<bool> callback) { }
    // RVA: 0x1400
    public static EStageEnterResultType validateBoss(StageCache cache) { }
    // RVA: 0x1500
    public static EStageEnterResultType validateType2(StageCache cache) { }
}

public class StageNode
{
    // RVA: 0x1000
    public void Click() { }
}

public class BalanceRoot
{
    public List<StageInfoData> stageInfoData; // 0x48
}

public class PlayerSaveData
{
    public CommonSaveData commonSaveData; // 0x10
    public List<ItemSaveData> itemSaveDatas; // 0xB0
    public List<InventorySaveData> inventory; // 0x88
    public List<StashSaveData> stash; // 0x90
}

public class box : root<box>
{
    private PlayerSaveData save; // 0x28
}

public class CommonSaveData
{
    public bool useStorage; // 0x60
    public int currentStageKey; // 0x64
    public int maxCompletedStage; // 0x68
}

public class ItemSaveData
{
    public int ItemKey; // 0x10
    public long ItemUniqueId; // 0x18
}

public class ItemHub
{
    // RVA: 0x1600
    public static ItemInfoData lookup(int a) { }
}

public class Monster : Enemy
{
    // RVA: 0x1700
    public virtual void hit(DamageInfo a, bool b = False) { }
}
""";

        const string scriptJson = """
{
  "Addresses": [4096, 4352, 4608, 4864, 5120, 5376, 5632, 5888, 6144],
  "ScriptMetadata": [
    { "Name": "Uo_TypeInfo", "Address": "0x7000" },
    { "Name": "BalanceRoot_TypeInfo", "Address": "0x7100" },
    { "Name": "holder<BalanceRoot>_TypeInfo", "Address": "0x7200" },
    { "Name": "box_TypeInfo", "Address": "0x7300" },
    { "Name": "root<box>_TypeInfo", "Address": "0x7400" }
  ]
}
""";

        var functions = new Dictionary<long, byte[]>
        {
            [0x1000] = BuildCalls(0x1000, 0x1100, 0x1200, 0x1300, 0x1400, 0x1500),
            [0x1100] = [0xC3],
            [0x1200] = [0x3D, 0x4D, 0x04, 0x00, 0x00, 0xC3],
            [0x1300] = [0xC3],
            [0x1400] = [0x83, 0xF8, 0x01, 0x83, 0xF8, 0x03, 0xC3],
            [0x1500] = [0x83, 0xF8, 0x02, 0xC3],
            [0x1600] = [0xC3],
            [0x1700] = [0xC3],
        };

        bool ok = Il2CppFoundationExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse(scriptJson),
            new Il2CppPeImage(BuildPe(functions)),
            out Il2CppExtractedOffsets? offsets,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(offsets);
        Assert.DoesNotContain("jgc", offsets.Symbols.Keys);
        Assert.Equal(0x7000L, offsets.Symbols["uo_ti"]);
        Assert.Equal(0x18L, offsets.Symbols["uo_dict"]);
        Assert.Equal(0x20L, offsets.Symbols["uo_cur_cache"]);
        Assert.Equal(0x7200L, offsets.Symbols["bal_ti"]);
        Assert.Equal(0x48L, offsets.Symbols["stage_off"]);
        Assert.Equal(0x88L, offsets.Symbols["inv_slots_off"]);
        Assert.Equal(0x90L, offsets.Symbols["stash_off"]);
        Assert.Equal(0x28L, offsets.Symbols["inv_psd_off"]);
        Assert.Equal(0xB0L, offsets.Symbols["inv_list_off"]);
        Assert.Equal(0x7300L, offsets.Symbols["inv_klass_ti"]);
        Assert.Equal(0x7400L, offsets.Symbols["bau_ti"]);
        Assert.Equal("box", offsets.InvClass);
        Assert.Equal(0x1600L, offsets.Symbols["izb"]);
        Assert.Equal(0x1700L, offsets.Symbols["gra"]);
        Assert.Equal(0x1100L, offsets.Symbols["jgk"]);
        Assert.Equal(0x1200L, offsets.Symbols["jgq"]);
        Assert.Equal(0x1300L, offsets.Symbols["jgd"]);
        Assert.Equal(0x1400L, offsets.Symbols["jgc_type13"]);
        Assert.Equal(0x1500L, offsets.Symbols["jgc_type2"]);
        Assert.Equal(0x10L, offsets.Symbols["psd_common_off"]);
        Assert.Equal(0x64L, offsets.Symbols["commonsave_curstage"]);

        // A composição já funciona, mas ainda não deve ser aceita como cache final enquanto faltarem
        // os demais símbolos críticos do legado.
        Assert.False(Il2CppOffsetCache.TryValidate(offsets, out string? validationError));
        Assert.Equal("missing critical symbol: upd", validationError);
    }

    private static byte[] BuildCalls(long startRva, params long[] targets)
    {
        var bytes = new List<byte>();
        long ip = startRva;
        foreach (long target in targets)
        {
            bytes.Add(0xE8);
            int relative = checked((int)(target - (ip + 5)));
            bytes.AddRange(BitConverter.GetBytes(relative));
            ip += 5;
        }
        bytes.Add(0xC3);
        return [.. bytes];
    }

    private static byte[] BuildPe(IReadOnlyDictionary<long, byte[]> functions)
    {
        const int peOffset = 0x80;
        const int optionalHeaderSize = 0xF0;
        const int sectionTable = peOffset + 24 + optionalHeaderSize;
        const int rawOffset = 0x200;
        const int rawSize = 0x1000;
        const uint virtualAddress = 0x1000;

        var data = new byte[rawOffset + rawSize];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x3C, 4), peOffset);
        data[peOffset] = (byte)'P';
        data[peOffset + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 6, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 20, 2), optionalHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionTable + 8, 4), rawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionTable + 12, 4), virtualAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionTable + 16, 4), rawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionTable + 20, 4), rawOffset);

        foreach ((long rva, byte[] code) in functions)
        {
            int offset = checked(rawOffset + (int)(rva - virtualAddress));
            Buffer.BlockCopy(code, 0, data, offset, code.Length);
        }
        return data;
    }
}
