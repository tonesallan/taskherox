using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppMethodSignatureParserTests
{
    [Fact]
    public void TryParse_ExtractsStaticReturnNameAndParameterTypes()
    {
        bool ok = Il2CppMethodSignatureParser.TryParse(
            "static bool abc(int a, Dictionary<int, List<StageCache>> b, bool c = False)",
            out Il2CppMethodSignatureInfo? info);

        Assert.True(ok);
        Assert.NotNull(info);
        Assert.True(info.IsStatic);
        Assert.Equal("bool", info.ReturnType);
        Assert.Equal("abc", info.MethodName);
        Assert.Equal(["int", "Dictionary<int, List<StageCache>>", "bool"], info.ParameterTypes);
    }

    [Fact]
    public void TryParse_AcceptsVirtualAndReadonlyLikeLegacyParser()
    {
        bool ok = Il2CppMethodSignatureParser.TryParse(
            "virtual readonly ItemInfoData Get(int key)",
            out Il2CppMethodSignatureInfo? info);

        Assert.True(ok);
        Assert.NotNull(info);
        Assert.False(info.IsStatic);
        Assert.Equal("ItemInfoData", info.ReturnType);
        Assert.Equal("Get", info.MethodName);
        Assert.Equal(["int"], info.ParameterTypes);
    }

    [Fact]
    public void TryParse_PreservesRefModifierInParameterTypeLikeLegacyParser()
    {
        bool ok = Il2CppMethodSignatureParser.TryParse(
            "void Run(ref int value, int[] values)",
            out Il2CppMethodSignatureInfo? info);

        Assert.True(ok);
        Assert.NotNull(info);
        Assert.Equal(["ref int", "int[]"], info.ParameterTypes);
    }

    [Fact]
    public void TryParse_RejectsUnsupportedSignatureShape()
    {
        Assert.False(Il2CppMethodSignatureParser.TryParse(
            "operator +(Thing a, Thing b)",
            out Il2CppMethodSignatureInfo? info));
        Assert.Null(info);
    }
}
