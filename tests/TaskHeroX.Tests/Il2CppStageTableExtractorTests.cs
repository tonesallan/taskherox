using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppStageTableExtractorTests
{
    [Fact]
    public void TryExtract_PrefersGenericSingletonTypeInfoLikeLegacyTiAny()
    {
        const string dump = """
public class BalanceRoot
{
    public List<StageInfoData> stageInfoData; // 0x48
}
""";

        const string script = """
{
  "ScriptMetadata": [
    { "Name": "BalanceRoot_TypeInfo", "Address": "0x1111" },
    { "Name": "SingletonBase<BalanceRoot>_TypeInfo", "Address": "0x2222" }
  ]
}
""";

        bool ok = Il2CppStageTableExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse(script),
            out StageTableSymbols? symbols,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(symbols);
        Assert.Equal("BalanceRoot", symbols.DeclaringClass);
        Assert.Equal(0x2222L, symbols.BalanceTypeInfo);
        Assert.Equal(0x48L, symbols.StageListOffset);
        Assert.True(symbols.UsesGenericSingletonTypeInfo);
    }

    [Fact]
    public void TryExtract_FallsBackToConcreteTypeInfo()
    {
        const string dump = """
public class BalanceRoot
{
    public List<StageInfoData> stageInfoData; // 0x50
}
""";

        const string script = """
{
  "ScriptMetadata": [
    { "Name": "BalanceRoot_TypeInfo", "Address": 4660 }
  ]
}
""";

        bool ok = Il2CppStageTableExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse(script),
            out StageTableSymbols? symbols,
            out string? error);

        Assert.True(ok, error);
        Assert.NotNull(symbols);
        Assert.Equal(4660L, symbols.BalanceTypeInfo);
        Assert.Equal(0x50L, symbols.StageListOffset);
        Assert.False(symbols.UsesGenericSingletonTypeInfo);
    }

    [Fact]
    public void TryExtract_FailsClosedWhenStageInfoAnchorIsAmbiguous()
    {
        const string dump = """
public class First
{
    public List<StageInfoData> stageInfoData; // 0x40
}
public class Second
{
    public List<StageInfoData> stageInfoData; // 0x48
}
""";

        bool ok = Il2CppStageTableExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse("{}"),
            out StageTableSymbols? symbols,
            out string? error);

        Assert.False(ok);
        Assert.Null(symbols);
        Assert.Equal("stageInfoData anchor ambiguous (2)", error);
    }

    [Fact]
    public void TryExtract_FailsClosedOnAmbiguousGenericTypeInfo()
    {
        const string dump = """
public class BalanceRoot
{
    public List<StageInfoData> stageInfoData; // 0x48
}
""";

        const string script = """
{
  "ScriptMetadata": [
    { "Name": "First<BalanceRoot>_TypeInfo", "Address": 1 },
    { "Name": "Second<BalanceRoot>_TypeInfo", "Address": 2 }
  ]
}
""";

        bool ok = Il2CppStageTableExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse(script),
            out StageTableSymbols? symbols,
            out string? error);

        Assert.False(ok);
        Assert.Null(symbols);
        Assert.Equal("stage table generic TypeInfo ambiguous (2)", error);
    }

    [Fact]
    public void TryExtract_RejectsMissingTypeInfo()
    {
        const string dump = """
public class BalanceRoot
{
    public List<StageInfoData> stageInfoData; // 0x48
}
""";

        bool ok = Il2CppStageTableExtractor.TryExtract(
            Il2CppDumpParser.Parse(dump),
            Il2CppScriptIndex.Parse("{}"),
            out StageTableSymbols? symbols,
            out string? error);

        Assert.False(ok);
        Assert.Null(symbols);
        Assert.Equal("stage table TypeInfo missing for BalanceRoot", error);
    }
}
