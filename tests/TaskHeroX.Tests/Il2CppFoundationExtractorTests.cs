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
    public static ObscuredInt max; // 0x50
    public static ObscuredInt cur; // 0x60
    public static ObscuredInt wave; // 0x70
    public static ObscuredInt extra1; // 0x80
    public static ObscuredInt extra2; // 0x90
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
    // RVA: 0x2400
    public static void install(StageCache cache) { }
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
    public List<RuneSaveData> RuneSaveData; // 0x98
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
    public int currentStageWave; // 0x6C
}

public class ItemInfoData
{
    public int ITEMTYPE; // 0x34
    public int GRADE; // 0x38
    public int ItemSynthesisType; // 0x48
    public int Level; // 0x6C
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

public class InputManager
{
    // RVA: 0x1800
    private void Update() { }
}

public class StageBox
{
    // RVA: 0x1900
    public void click(PointerEventData.InputButton a) { }
}

public class mover : singleton<mover>
{
    // RVA: 0x1A00
    public MoveResult move(MoveRequest a, Action<MoveResult> b) { }
}


public class InjectionDetector
{
    // RVA: 0x2600
    public InjectionDetector() { }
    // RVA: 0x2700
    public override void Dispose() { }
    // RVA: 0x2800
    public T Get<T>() { }
    // RVA: 0x2900
    public static InjectionDetector Instance() { }
    // RVA: 0x2A00
    public static void A() { }
    // RVA: 0x2B00
    public static void B(Action<string> callback) { }
    // RVA: 0x2C00
    public static void C() { }
    // RVA: 0x2D00
    public static void D() { }
}

public class UIManager
{
    public UI_Main ui_main; // 0xA8
    // RVA: 0x3000
    public void ClosePanel() { }
    // RVA: 0x3100
    public void Other() { }
}

public class UI_Main
{
    public Button button_Cube; // 0x88
    // RVA: 0x2E00
    private void Awake() { }
}

public class GuidingCube
{
    // RVA: 0x3200
    public void Step() { }
}

public class CubeButtonHandler
{
    // RVA: 0x2F00
    public static void Open(UI_Main ui) { }
}

public static class uw.Cube
{
    public static Action<ECubeSynthesisResult> resultEvent; // 0x20
    public static EGradeType grade; // 0xE0
    public static Dictionary<ERecipeType, List<uw>> recipes; // 0xF8
    public static Dictionary<ERecipeType, uw> currentByType; // 0x108
    public static List<CubeInData> inputs; // 0x118
    public static uw activeRecipe; // 0x158
    public static bool busy; // 0x160
    public static ObscuredInt cubeLevel; // 0x1E8
    public static SynthesisRecipeInfoData recipe; // 0x260
    public static EItemSynthesisType synthesisType; // 0x274
    public static uw levelRecipe; // 0x278
    // RVA: 0x1A80
    public static void selectRecipe(ERecipeType a) { }
    // RVA: 0x1B00
    public static void setType(EItemSynthesisType a) { }
    // RVA: 0x1C00
    public static bool setGrade(EGradeType a) { }
    // RVA: 0x1D00
    private static void setRecipe(uw a) { }
    // RVA: 0x1E00
    public static bool setLevelRecipe(uw a) { }
    // RVA: 0x1F00
    private static void build(int a, CubeInData b) { }
    // RVA: 0x2000
    public static EAddCubeResult add(ESlotType a, int b) { }
    // RVA: 0x2100
    public static void synthesize() { }
    // RVA: 0x2200
    private static InternalBucketCountResult bucketCount() { }
    [AsyncStateMachine(typeof(uw.Cube.<TriggerCurrentRecipeLogic>d__42))]
    // RVA: 0x2300
    public static Task abc() { }
}
""";

        const string scriptJson = """
{
  "Addresses": [4096,4352,4608,4864,5120,5376,5632,5888,6144,6400,6656,6912,7168,7424,7680,7936,8192,8448,8704,8960,9216,9472],
  "ScriptMetadata": [
    { "Name": "Uo_TypeInfo", "Address": "0x7000" },
    { "Name": "BalanceRoot_TypeInfo", "Address": "0x7100" },
    { "Name": "holder<BalanceRoot>_TypeInfo", "Address": "0x7200" },
    { "Name": "box_TypeInfo", "Address": "0x7300" },
    { "Name": "root<box>_TypeInfo", "Address": "0x7400" },
    { "Name": "holder<UIManager>_TypeInfo", "Address": "0x7500" }
  ],
  "ScriptMetadataMethod": [
    { "Address": "0x3800", "MethodAddress": "0x2F00" }
  ]
}
""";

        var functions = new Dictionary<long, byte[]>
        {
            [0x1000] = BuildCalls(0x1000, 0x1100, 0x1200, 0x1300, 0x1400, 0x1500),
            [0x1100] = BuildCalls(0x1100, 0x2400),
            [0x1200] = [0x8B,0x40,0x50, 0x3D, 0x4D, 0x04, 0x00, 0x00, 0xC3],
            [0x1300] = [0xC3],
            [0x1400] = [0x83, 0xF8, 0x01, 0x83, 0xF8, 0x03, 0xC3],
            [0x1500] = [0x83, 0xF8, 0x02, 0xC3],
            [0x1900] = BuildLlx(0x1900, 0x2500),
            [0x2400] = [0x89,0x48,0x60, 0x48,0x89,0x48,0x20, 0x89,0x48,0x70, 0xC3],
            [0x2E00] = BuildUiAwake(0x2E00, 0x88, 0x3800),
            [0x2F00] = [0xC3],
            [0x3000] = [0xC3],
            [0x3100] = [0xC3],
            [0x3200] = BuildCalls(0x3200, 0x3000),
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
        Assert.Equal(0x50L, offsets.Symbols["uo_max"]);
        Assert.Equal(0x60L, offsets.Symbols["uo_cur"]);
        Assert.Equal(0x70L, offsets.Symbols["uo_wave"]);
        Assert.Equal(0x7200L, offsets.Symbols["bal_ti"]);
        Assert.Equal(0x48L, offsets.Symbols["stage_off"]);
        Assert.Equal(0x88L, offsets.Symbols["inv_slots_off"]);
        Assert.Equal(0x90L, offsets.Symbols["stash_off"]);
        Assert.Equal(0x28L, offsets.Symbols["inv_psd_off"]);
        Assert.Equal(0xB0L, offsets.Symbols["inv_list_off"]);
        Assert.Equal(0x98L, offsets.Symbols["PlayerSaveData.RuneSaveData"]);
        Assert.Equal(0x10L, offsets.Symbols["itemsave_key"]);
        Assert.Equal(0x34L, offsets.Symbols["iteminfo_type"]);
        Assert.Equal(0x38L, offsets.Symbols["iteminfo_grade"]);
        Assert.Equal(0x48L, offsets.Symbols["iteminfo_synth"]);
        Assert.Equal(0x6CL, offsets.Symbols["iteminfo_level"]);
        Assert.Equal(0x7300L, offsets.Symbols["inv_klass_ti"]);
        Assert.Equal(0x7400L, offsets.Symbols["bau_ti"]);
        Assert.Equal("box", offsets.InvClass);
        Assert.Equal("mover", offsets.RaClass);
        Assert.Equal(0x1600L, offsets.Symbols["izb"]);
        Assert.Equal(0x1700L, offsets.Symbols["gra"]);
        Assert.Equal(0x1800L, offsets.Symbols["upd"]);
        Assert.Equal(0x1900L, offsets.Symbols["llx"]);
        Assert.Equal(0x1A00L, offsets.Symbols["iw"]);
        Assert.Equal(0x1B00L, offsets.Symbols["ilo"]);
        Assert.Equal(0x1C00L, offsets.Symbols["ili"]);
        Assert.Equal(0x1D00L, offsets.Symbols["inf"]);
        Assert.Equal(0x1E00L, offsets.Symbols["ima"]);
        Assert.Equal(0x1F00L, offsets.Symbols["iog"]);
        Assert.Equal(0x2000L, offsets.Symbols["ioa"]);
        Assert.Equal(0x2100L, offsets.Symbols["ipu"]);
        Assert.Equal(0x2300L, offsets.Symbols["imx"]);
        Assert.Equal(0x2500L, offsets.Symbols["iuw"]);
        Assert.Equal(0x1100L, offsets.Symbols["jgk"]);
        Assert.Equal(0x1200L, offsets.Symbols["jgq"]);
        Assert.Equal(0x1300L, offsets.Symbols["jgd"]);
        Assert.Equal(0x1400L, offsets.Symbols["jgc_type13"]);
        Assert.Equal(0x1500L, offsets.Symbols["jgc_type2"]);
        Assert.Equal(0x10L, offsets.Symbols["psd_common_off"]);
        Assert.Equal(0x60L, offsets.Symbols["commonsave_usestorage"]);
        Assert.Equal(0x68L, offsets.Symbols["commonsave_maxstage"]);
        Assert.Equal(0x64L, offsets.Symbols["commonsave_curstage"]);
        Assert.Equal(0x6CL, offsets.Symbols["CommonSaveData.currentStageWave"]);
        Assert.Equal(0x7500L, offsets.Symbols["uimgr_ti"]);
        Assert.Equal(0xA8L, offsets.Symbols["uimain"]);
        Assert.Equal(0x2F00L, offsets.Symbols["eby"]);
        Assert.Equal(0x3000L, offsets.Symbols["hgr"]);
        Assert.Equal([0x2D00L], offsets.Ynj);
        Assert.Equal(0xE0L, offsets.Symbols["cube_grade"]);
        Assert.Equal(0xF8L, offsets.Symbols["cube_bers"]);
        Assert.Equal(0x118L, offsets.Symbols["cube_inlist"]);
        Assert.Equal(0x158L, offsets.Symbols["cube_active"]);
        Assert.Equal(0x160L, offsets.Symbols["cube_busy"]);
        Assert.Equal(0x274L, offsets.Symbols["cube_type"]);
        Assert.Equal(0x278L, offsets.Symbols["cube_lvrecipe"]);
        Assert.Equal(0x1E8L, offsets.Symbols["cube_level_off"]);
        Assert.Equal(0x260L, offsets.Symbols["cube_recipe"]);
        Assert.Equal(0x108L, offsets.Symbols["cube_beru"]);
        Assert.Equal(0x20L, offsets.Symbols["cube_resultevt"]);
        Assert.Equal(0x1A80L, offsets.Symbols["ilx"]);

        Assert.True(Il2CppOffsetCache.TryValidate(offsets, out string? validationError), validationError);
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

    private static byte[] BuildUiAwake(long startRva, int buttonOffset, long metadataSlot)
    {
        var bytes = new List<byte>
        {
            0x48, 0x8B, 0x81,
        };
        bytes.AddRange(BitConverter.GetBytes(buttonOffset));
        bytes.AddRange([0x4C, 0x8B, 0x05]);
        long ripAfterSecondMov = startRva + bytes.Count + 4;
        int relative = checked((int)(metadataSlot - ripAfterSecondMov));
        bytes.AddRange(BitConverter.GetBytes(relative));
        bytes.Add(0xC3);
        return [.. bytes];
    }

    private static byte[] BuildLlx(long startRva, long iuwTarget)
    {
        var bytes = new List<byte>();
        bytes.Add(0xE8);
        int relative = checked((int)(iuwTarget - (startRva + 5)));
        bytes.AddRange(BitConverter.GetBytes(relative));
        bytes.AddRange([0x85, 0xC0, 0x7E, 0x00, 0xC3]);
        return [.. bytes];
    }

    private static byte[] BuildPe(IReadOnlyDictionary<long, byte[]> functions)
    {
        const int peOffset = 0x80;
        const int optionalHeaderSize = 0xF0;
        const int sectionTable = peOffset + 24 + optionalHeaderSize;
        const int rawOffset = 0x200;
        const int rawSize = 0x4000;
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
