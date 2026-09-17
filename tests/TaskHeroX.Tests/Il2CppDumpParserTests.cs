using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppDumpParserTests
{
    [Fact]
    public void Parse_ExtractsClassFieldsAndMethods()
    {
        const string dump = """
// Namespace: Game.Data
public class InventorySaveData // TypeDefIndex: 1234
{
    // Fields
    public int ItemCount; // 0x10
    private static readonly System.String Cache; // 0x0

    // Methods
    // RVA: 0x1234 Offset: 0x1234 VA: 0x180001234
    public void Refresh() { }
    // RVA: 0x5678 Offset: 0x5678 VA: 0x180005678
    private static int Count(System.Int32 value) { }
}
""";

        IReadOnlyList<Il2CppDumpClass> classes = Il2CppDumpParser.Parse(dump);

        Il2CppDumpClass klass = Assert.Single(classes);
        Assert.Equal("InventorySaveData", klass.Name);
        Assert.Contains("public class InventorySaveData", klass.Declaration, StringComparison.Ordinal);

        Assert.Collection(
            klass.Fields,
            field =>
            {
                Assert.Equal("int", field.Type);
                Assert.Equal("ItemCount", field.Name);
                Assert.Equal(0x10, field.Offset);
                Assert.False(field.IsStatic);
            },
            field =>
            {
                Assert.Equal("System.String", field.Type);
                Assert.Equal("Cache", field.Name);
                Assert.Equal(0, field.Offset);
                Assert.True(field.IsStatic);
            });

        Assert.Collection(
            klass.Methods,
            method =>
            {
                Assert.Equal(0x1234, method.Rva);
                Assert.Equal("void Refresh()", method.Signature);
            },
            method =>
            {
                Assert.Equal(0x5678, method.Rva);
                Assert.Equal("static int Count(System.Int32 value)", method.Signature);
            });
    }

    [Fact]
    public void Parse_DoesNotLeakPendingRvaAcrossClassDeclarations()
    {
        const string dump = """
public class First
{
    // RVA: 0x1111 Offset: 0x1111 VA: 0x180001111
}
public class Second
{
    public void NoRva() { }
    // RVA: 0x2222 Offset: 0x2222 VA: 0x180002222
    public void WithRva() { }
}
""";

        IReadOnlyList<Il2CppDumpClass> classes = Il2CppDumpParser.Parse(dump);

        Assert.Equal(2, classes.Count);
        Assert.Empty(classes[0].Methods);
        Il2CppDumpMethod method = Assert.Single(classes[1].Methods);
        Assert.Equal(0x2222, method.Rva);
        Assert.Equal("void WithRva()", method.Signature);
    }

    [Fact]
    public void Parse_SkipsMalformedHexWithoutThrowing()
    {
        const string dump = """
public class Sample
{
    public int Bad; // 0xGG
    // RVA: 0xNOTHEX Offset: 0x0 VA: 0x0
    public void BadMethod() { }
}
""";

        Il2CppDumpClass klass = Assert.Single(Il2CppDumpParser.Parse(dump));

        Assert.Empty(klass.Fields);
        Assert.Empty(klass.Methods);
    }
}
