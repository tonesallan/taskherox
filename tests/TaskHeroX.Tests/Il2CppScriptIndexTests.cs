using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppScriptIndexTests
{
    [Fact]
    public void Parse_IndexesAddressesMetadataAndMetadataMethods()
    {
        const string json = """
{
  "Addresses": [4096, 8192, 4096, "0x3000"],
  "ScriptMetadata": [
    { "Name": "StageCache_TypeInfo", "Address": 111 },
    { "Name": "StageCache_TypeInfo", "Address": 999 },
    { "Name": "PlayerSaveData_TypeInfo", "Address": "0xDEAD" }
  ],
  "ScriptMetadataMethod": [
    { "Address": 700, "MethodAddress": 12288 },
    { "Address": 701, "MethodAddress": "0x4000" },
    { "Address": 700, "MethodAddress": 20480 }
  ]
}
""";

        Il2CppScriptIndex index = Il2CppScriptIndex.Parse(json);

        Assert.Equal([0x1000L, 0x2000L, 0x3000L], index.Addresses);

        Assert.True(index.TryGetMetadataAddress("StageCache_TypeInfo", out long stageTypeInfo));
        Assert.Equal(111, stageTypeInfo); // primeira ocorrência, como next(...) no legado.

        Assert.True(index.TryGetMetadataAddress("PlayerSaveData_TypeInfo", out long playerSaveData));
        Assert.Equal(0xDEAD, playerSaveData);

        Assert.True(index.TryGetMethodAddress(700, out long method700));
        Assert.Equal(0x5000, method700); // última ocorrência, como dict-comprehension no legado.

        Assert.True(index.TryGetMethodAddress(701, out long method701));
        Assert.Equal(0x4000, method701);
    }

    [Fact]
    public void Parse_MissingOrMalformedCollectionsDegradeToEmptyIndexes()
    {
        const string json = """
{
  "Addresses": ["bad", null],
  "ScriptMetadata": [
    { "Name": "MissingAddress" },
    { "Address": 123 }
  ],
  "ScriptMetadataMethod": [
    { "Address": 1 },
    { "MethodAddress": 2 }
  ]
}
""";

        Il2CppScriptIndex index = Il2CppScriptIndex.Parse(json);

        Assert.Empty(index.Addresses);
        Assert.Empty(index.MetadataAddresses);
        Assert.Empty(index.MetadataMethodAddresses);
    }
}
