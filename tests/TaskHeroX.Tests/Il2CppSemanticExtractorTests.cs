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
        Assert.Equal(0x1234, symbols.UoTypeInfo);
        Assert.Equal(0x20, symbols.UoDictionaryOffset);
        Assert.Equal(0x28, symbols.UoCurrentCacheOffset);
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
        Assert.Equal(0x38, symbols.UoDictionaryOffset);
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
}
