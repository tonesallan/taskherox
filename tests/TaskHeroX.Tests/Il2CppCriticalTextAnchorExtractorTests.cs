using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppCriticalTextAnchorExtractorTests
{
    [Fact]
    public void TryExtract_ResolvesCriticalStableSignatureAnchors()
    {
        const string dump = """
public class InputManager
{
    // RVA: 0x1000
    private void Update() { }
    // RVA: 0x1010
    public void Update() { }
}

public class StageBox
{
    // RVA: 0x1100
    public void click(PointerEventData.InputButton a) { }
}

public class mover : singleton<mover>
{
    // RVA: 0x1200
    public MoveResult move(MoveRequest a, Action<MoveResult> b) { }
}

public static class uw.Cube
{
    public static Dictionary<ERecipeType, List<uw>> recipes; // 0x20

    // RVA: 0x1300
    public static void setType(EItemSynthesisType a) { }
    // RVA: 0x1400
    public static bool setGrade(EGradeType a) { }
    // RVA: 0x1500
    private static void setRecipe(uw a) { }
    // RVA: 0x1600
    public static bool setLevelRecipe(uw a) { }
    // RVA: 0x1700
    private static void build(int a, CubeInData b) { }
    // RVA: 0x1800
    public static EAddCubeResult add(ESlotType a, int b) { }
    // RVA: 0x1900
    public static void synthesize() { }
    // RVA: 0x1A00
    private static InternalBucketCountResult bucketCount() { }
    [AsyncStateMachine(typeof(uw.Cube.<TriggerCurrentRecipeLogic>d__42))]
    // RVA: 0x1B00
    public static Task abc() { }
}
""";

        bool ok = Il2CppCriticalTextAnchorExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            out Il2CppCriticalTextAnchors? anchors,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(anchors);
        Assert.Equal(0x1000L, anchors.Symbols["upd"]);
        Assert.Equal(0x1100L, anchors.Symbols["llx"]);
        Assert.Equal(0x1200L, anchors.Symbols["iw"]);
        Assert.Equal("mover", anchors.MoveManagerClass);
        Assert.Equal(0x1300L, anchors.Symbols["ilo"]);
        Assert.Equal(0x1400L, anchors.Symbols["ili"]);
        Assert.Equal(0x1500L, anchors.Symbols["inf"]);
        Assert.Equal(0x1600L, anchors.Symbols["ima"]);
        Assert.Equal(0x1700L, anchors.Symbols["iog"]);
        Assert.Equal(0x1800L, anchors.Symbols["ioa"]);
        Assert.Equal(0x1900L, anchors.Symbols["ipu"]);
        Assert.Equal(0x1B00L, anchors.Symbols["imx"]);
    }

    [Fact]
    public void TryExtract_FailsClosedOnAmbiguousUpdate()
    {
        const string dump = """
public class InputManager
{
    // RVA: 0x1000
    private void Update() { }
}

public class InputManager
{
    // RVA: 0x1010
    private void Update() { }
}
""";

        bool ok = Il2CppCriticalTextAnchorExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            out Il2CppCriticalTextAnchors? anchors,
            out string? error);

        Assert.False(ok);
        Assert.Null(anchors);
        Assert.Equal("InputManager.Update anchor ambiguous (2): InputManager@0x1000, InputManager@0x1010", error);
    }

    [Fact]
    public void TryExtract_IgnoresPrivateMoveRequestDecoys()
    {
        const string dump = """
public class InputManager
{
    // RVA: 0x1000
    private void Update() { }
}

public class StageBox
{
    // RVA: 0x1100
    public void click(PointerEventData.InputButton a) { }
}

public class decoy : singleton<decoy>
{
    // RVA: 0x1150
    private MoveResult hidden(MoveRequest a, Action<MoveResult> b) { }
}

public class mover : singleton<mover>
{
    // RVA: 0x1200
    public MoveResult move(MoveRequest a, Action<MoveResult> b) { }
}

public static class uw.Cube
{
    public static Dictionary<ERecipeType, List<uw>> recipes; // 0x20

    // RVA: 0x1300
    public static void setType(EItemSynthesisType a) { }
    // RVA: 0x1400
    public static bool setGrade(EGradeType a) { }
    // RVA: 0x1500
    private static void setRecipe(uw a) { }
    // RVA: 0x1600
    public static bool setLevelRecipe(uw a) { }
    // RVA: 0x1700
    private static void build(int a, CubeInData b) { }
    // RVA: 0x1800
    public static EAddCubeResult add(ESlotType a, int b) { }
    // RVA: 0x1900
    public static void synthesize() { }
    // RVA: 0x1A00
    private static InternalBucketCountResult bucketCount() { }
    // RVA: 0x1B00
    public static Task TriggerCurrentRecipeLogic() { }
}
""";

        bool ok = Il2CppCriticalTextAnchorExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            out Il2CppCriticalTextAnchors? anchors,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(anchors);
        Assert.Equal(0x1200L, anchors.Symbols["iw"]);
        Assert.Equal("mover", anchors.MoveManagerClass);
    }

    [Fact]
    public void TryExtract_UsesFirstPublicMoveRequestMethodAndNearestPriorSingletonLikeLegacy()
    {
        const string dump = """
public class InputManager
{
    // RVA: 0x1000
    private void Update() { }
}

public class StageBox
{
    // RVA: 0x1100
    public void click(PointerEventData.InputButton a) { }
}

public class manager : singleton<manager>
{
}

public class rv
{
    // RVA: 0x1200
    public MoveResult first(MoveRequest a, Action<MoveResult> b) { }
    // RVA: 0x1210
    public MoveResult second(MoveRequest a, Action<MoveResult> b) { }
}

public static class uw.Cube
{
    public static Dictionary<ERecipeType, List<uw>> recipes; // 0x20

    // RVA: 0x1300
    public static void setType(EItemSynthesisType a) { }
    // RVA: 0x1400
    public static bool setGrade(EGradeType a) { }
    // RVA: 0x1500
    private static void setRecipe(uw a) { }
    // RVA: 0x1600
    public static bool setLevelRecipe(uw a) { }
    // RVA: 0x1700
    private static void build(int a, CubeInData b) { }
    // RVA: 0x1800
    public static EAddCubeResult add(ESlotType a, int b) { }
    // RVA: 0x1900
    public static void synthesize() { }
    // RVA: 0x1A00
    private static InternalBucketCountResult bucketCount() { }
    // RVA: 0x1B00
    public static Task TriggerCurrentRecipeLogic() { }
}
""";

        bool ok = Il2CppCriticalTextAnchorExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            out Il2CppCriticalTextAnchors? anchors,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(anchors);
        Assert.Equal(0x1200L, anchors.Symbols["iw"]);
        Assert.Equal("manager", anchors.MoveManagerClass);
    }

    [Fact]
    public void TryExtract_UsesFirstPublicCubeRecipeMatchLikeLegacy()
    {
        const string dump = """
public class InputManager
{
    // RVA: 0x1000
    private void Update() { }
}

public class StageBox
{
    // RVA: 0x1100
    public void click(PointerEventData.InputButton a) { }
}

public class mover : singleton<mover>
{
    // RVA: 0x1200
    public MoveResult move(MoveRequest a, Action<MoveResult> b) { }
}

public static class uw.Cube
{
    public static Dictionary<ERecipeType, List<uw>> recipes; // 0x20

    // RVA: 0x1300
    public static void setType(EItemSynthesisType a) { }
    // RVA: 0x1400
    public static bool setGrade(EGradeType a) { }
    // RVA: 0x1500
    private static void setRecipe(uw a) { }
    // RVA: 0x1600
    public static bool firstVariant(uw a) { }
    // RVA: 0x1610
    public static bool secondVariant(uw a) { }
    // RVA: 0x1700
    private static void build(int a, CubeInData b) { }
    // RVA: 0x1800
    public static EAddCubeResult add(ESlotType a, int b) { }
    // RVA: 0x1900
    public static void synthesize() { }
    // RVA: 0x1A00
    private static InternalBucketCountResult bucketCount() { }
    // RVA: 0x1B00
    public static Task TriggerCurrentRecipeLogic() { }
}
""";

        bool ok = Il2CppCriticalTextAnchorExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            out Il2CppCriticalTextAnchors? anchors,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(anchors);
        Assert.Equal(0x1600L, anchors.Symbols["ima"]);
    }

    [Fact]
    public void TryExtract_RejectsMoveManagerWithoutSelfGenericSingletonShape()
    {
        const string dump = """
public class InputManager
{
    // RVA: 0x1000
    private void Update() { }
}
public class StageBox
{
    // RVA: 0x1100
    public void click(PointerEventData.InputButton a) { }
}
public class mover
{
    // RVA: 0x1200
    public MoveResult move(MoveRequest a, Action<MoveResult> b) { }
}
""";

        bool ok = Il2CppCriticalTextAnchorExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            out Il2CppCriticalTextAnchors? anchors,
            out string? error);

        Assert.False(ok);
        Assert.Null(anchors);
        Assert.Equal("move-manager singleton missing before mover.move@0x1200", error);
    }
}
