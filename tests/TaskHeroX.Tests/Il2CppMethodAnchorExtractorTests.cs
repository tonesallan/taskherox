using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppMethodAnchorExtractorTests
{
    [Fact]
    public void TryExtractItemInfoGetter_ReturnsFirstMatchingStaticGetter()
    {
        const string dump = """
public class First
{
    // RVA: 0x1111 Offset: 0x1111 VA: 0x1111
    public static ItemInfoData abc(int a) { }
}
public class Second
{
    // RVA: 0x2222 Offset: 0x2222 VA: 0x2222
    public static ItemInfoData def(int a) { }
}
""";

        bool ok = Il2CppMethodAnchorExtractor.TryExtractItemInfoGetter(
            Il2CppDumpParser.Parse(dump),
            out long rva);

        Assert.True(ok);
        Assert.Equal(0x1111L, rva);
    }

    [Fact]
    public void TryExtractItemInfoGetter_RejectsDifferentSignature()
    {
        const string dump = """
public class First
{
    // RVA: 0x1111 Offset: 0x1111 VA: 0x1111
    public static ItemInfoData abc(long a) { }
}
""";

        bool ok = Il2CppMethodAnchorExtractor.TryExtractItemInfoGetter(
            Il2CppDumpParser.Parse(dump),
            out long rva);

        Assert.False(ok);
        Assert.Equal(0, rva);
    }

    [Fact]
    public void TryExtractMonsterDamage_ResolvesUniqueDamageInfoSignature()
    {
        const string dump = """
public class Monster : Enemy
{
    // RVA: 0xABCD Offset: 0xABCD VA: 0xABCD Slot: 45
    public override void qwe(DamageInfo a, bool b = False) { }

    // RVA: 0xBCDE Offset: 0xBCDE VA: 0xBCDE
    public void Other() { }
}
""";

        bool ok = Il2CppMethodAnchorExtractor.TryExtractMonsterDamage(
            Il2CppDumpParser.Parse(dump),
            out long rva,
            out string? error);

        Assert.True(ok, error);
        Assert.Equal(0xABCDL, rva);
    }

    [Fact]
    public void TryExtractMonsterDamage_AcceptsVirtualSignature()
    {
        const string dump = """
public class Monster : Enemy
{
    // RVA: 0x1234 Offset: 0x1234 VA: 0x1234
    public virtual void xyz(DamageInfo a, bool b = False) { }
}
""";

        bool ok = Il2CppMethodAnchorExtractor.TryExtractMonsterDamage(
            Il2CppDumpParser.Parse(dump),
            out long rva,
            out string? error);

        Assert.True(ok, error);
        Assert.Equal(0x1234L, rva);
    }

    [Fact]
    public void TryExtractMonsterDamage_FailsClosedOnAmbiguousMatches()
    {
        const string dump = """
public class Monster : Enemy
{
    // RVA: 0x1111 Offset: 0x1111 VA: 0x1111
    public override void one(DamageInfo a, bool b = False) { }
    // RVA: 0x2222 Offset: 0x2222 VA: 0x2222
    public virtual void two(DamageInfo a, bool b = False) { }
}
""";

        bool ok = Il2CppMethodAnchorExtractor.TryExtractMonsterDamage(
            Il2CppDumpParser.Parse(dump),
            out long rva,
            out string? error);

        Assert.False(ok);
        Assert.Equal(0, rva);
        Assert.Equal("Monster damage method ambiguous (2)", error);
    }

    [Fact]
    public void TryExtractMonsterDamage_RejectsMissingMonsterClass()
    {
        bool ok = Il2CppMethodAnchorExtractor.TryExtractMonsterDamage(
            Il2CppDumpParser.Parse("public class Other { }"),
            out long rva,
            out string? error);

        Assert.False(ok);
        Assert.Equal(0, rva);
        Assert.Equal("Monster class ambiguous (0)", error);
    }
}
