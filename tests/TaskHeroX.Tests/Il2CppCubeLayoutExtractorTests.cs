using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppCubeLayoutExtractorTests
{
    [Fact]
    public void TryExtract_PortsLegacyStaticTypeAndOrderHeuristic()
    {
        const string dump = """
public static class xy.Cube
{
    public static Action<ECubeSynthesisResult> evt; // 0x20
    public static EGradeType grade; // 0xE0
    public static Dictionary<ERecipeType, List<xy>> recipes; // 0xF8
    public static Dictionary<ERecipeType, xy> currentByType; // 0x108
    public static List<CubeInData> inputs; // 0x118
    public static xy active; // 0x158
    public static bool busy; // 0x160
    public static ObscuredInt level; // 0x1E8
    public static SynthesisRecipeInfoData recipe; // 0x260
    public static EItemSynthesisType synthesisType; // 0x274
    public static xy levelRecipe; // 0x278
    // RVA: 0x4100
    public static void choose(ERecipeType value) { }
}
""";

        bool ok = Il2CppCubeLayoutExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            out Il2CppCubeLayoutSymbols? symbols,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(symbols);
        Assert.Equal(0xE0, symbols.Grade);
        Assert.Equal(0xF8, symbols.RecipeLists);
        Assert.Equal(0x118, symbols.InputList);
        Assert.Equal(0x158, symbols.ActiveRecipe);
        Assert.Equal(0x160, symbols.Busy);
        Assert.Equal(0x274, symbols.SynthesisType);
        Assert.Equal(0x278, symbols.LevelRecipe);
        Assert.Equal(0x1E8, symbols.CubeLevel);
        Assert.Equal(0x260, symbols.OptionalSymbols["cube_recipe"]);
        Assert.Equal(0x108, symbols.OptionalSymbols["cube_beru"]);
        Assert.Equal(0x20, symbols.OptionalSymbols["cube_resultevt"]);
        Assert.Equal(0x4100, symbols.OptionalSymbols["ilx"]);
    }

    [Fact]
    public void TryExtract_FailsClosedWhenCubeLevelIsAmbiguous()
    {
        const string dump = """
public static class xy.Cube
{
    public static EGradeType grade; // 0xE0
    public static Dictionary<ERecipeType, List<xy>> recipes; // 0xF8
    public static List<CubeInData> inputs; // 0x118
    public static xy active; // 0x158
    public static bool busy; // 0x160
    public static ObscuredInt levelA; // 0x1E8
    public static ObscuredInt levelB; // 0x1F8
    public static EItemSynthesisType synthesisType; // 0x274
    public static xy levelRecipe; // 0x278
}
""";

        bool ok = Il2CppCubeLayoutExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            out Il2CppCubeLayoutSymbols? symbols,
            out string? error);

        Assert.False(ok);
        Assert.Null(symbols);
        Assert.Contains("exactly one static ObscuredInt", error, StringComparison.Ordinal);
    }
}
