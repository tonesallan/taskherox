using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppDataFieldExtractorTests
{
    [Fact]
    public void Extract_ResolvesLegacyNamedAliases()
    {
        const string dump = """
public class SynthesisRecipeInfoData
{
    public int MinResultLevel; // 0x10
    public int MaxResultLevel; // 0x14
}
public class ItemInfoData
{
    public int ITEMTYPE; // 0x20
    public int GRADE; // 0x24
    public int ItemSynthesisType; // 0x28
    public int Level; // 0x2C
}
public class PlayerSaveData
{
    public CommonSaveData commonSaveData; // 0x30
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
""";

        IReadOnlyDictionary<string, long> result = Il2CppDataFieldExtractor.Extract(
            Il2CppDumpParser.Parse(dump));

        Assert.Equal(0x10L, result["recipe_minlvl"]);
        Assert.Equal(0x14L, result["recipe_maxlvl"]);
        Assert.Equal(0x20L, result["iteminfo_type"]);
        Assert.Equal(0x24L, result["iteminfo_grade"]);
        Assert.Equal(0x28L, result["iteminfo_synth"]);
        Assert.Equal(0x2CL, result["iteminfo_level"]);
        Assert.Equal(0x30L, result["psd_common_off"]);
        Assert.Equal(0x60L, result["commonsave_usestorage"]);
        Assert.Equal(0x64L, result["commonsave_curstage"]);
        Assert.Equal(0x68L, result["commonsave_maxstage"]);
        Assert.Equal(0x10L, result["itemsave_key"]);
        Assert.Equal(0x18L, result["itemsave_uid"]);
    }

    [Fact]
    public void Extract_ProvidesClearDataFieldCoverageAndRejectsObfuscatedNames()
    {
        const string dump = """
public class PlayerSaveData
{
    public List<RuneSaveData> RuneSaveData; // 0x80
    public int clear_name; // 0x84
    public int abc; // 0x88
    public int abcdef2; // 0x8C
    public static int StaticField; // 0x90
}
public class NotTracked
{
    public int VisibleField; // 0x20
}
""";

        IReadOnlyDictionary<string, long> result = Il2CppDataFieldExtractor.Extract(
            Il2CppDumpParser.Parse(dump));

        Assert.Equal(0x80L, result["PlayerSaveData.RuneSaveData"]);
        Assert.Equal(0x84L, result["PlayerSaveData.clear_name"]);
        Assert.DoesNotContain("PlayerSaveData.abc", result.Keys);
        Assert.DoesNotContain("PlayerSaveData.abcdef2", result.Keys);
        Assert.DoesNotContain("PlayerSaveData.StaticField", result.Keys);
        Assert.DoesNotContain("NotTracked.VisibleField", result.Keys);
    }

    [Fact]
    public void Extract_OmitsMissingNamedFieldsWithoutFailing()
    {
        const string dump = """
public class ItemSaveData
{
    public int ItemKey; // 0x10
}
""";

        IReadOnlyDictionary<string, long> result = Il2CppDataFieldExtractor.Extract(
            Il2CppDumpParser.Parse(dump));

        Assert.Equal(0x10L, result["itemsave_key"]);
        Assert.DoesNotContain("itemsave_uid", result.Keys);
        Assert.DoesNotContain("recipe_minlvl", result.Keys);
    }

    [Fact]
    public void Extract_MatchesLegacyDuplicateClassAndFieldSelection()
    {
        const string dump = """
public class ItemSaveData
{
    public int ItemKey; // 0x10
}
public class ItemSaveData
{
    public int ItemKey; // 0x30
    public int ItemKey; // 0x34
}
""";

        IReadOnlyDictionary<string, long> result = Il2CppDataFieldExtractor.Extract(
            Il2CppDumpParser.Parse(dump));

        // O legado cria byname por dict-comprehension (última classe vence). O alias nomeado usa
        // next(...), então pega o primeiro campo da classe escolhida; a cobertura Classe.Campo percorre
        // todos os campos e sobrescreve a mesma chave, portanto o último campo duplicado vence.
        Assert.Equal(0x30L, result["itemsave_key"]);
        Assert.Equal(0x34L, result["ItemSaveData.ItemKey"]);
    }
}
