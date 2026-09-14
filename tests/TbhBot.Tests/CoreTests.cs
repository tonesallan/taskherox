using System.Text;
using TbhBot.Core.Game;
using TbhBot.Core.Il2Cpp;
using TbhBot.Core.Update;

namespace TbhBot.Tests;

public class CoreTests
{
    // (1) ObscuredValue: EncodeHidden -> Decode devolve o value original (round-trip),
    // pra varias combinacoes value/key, incluindo negativos e o 4310 (max stage).
    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 1u)]
    [InlineData(4310, 0u)]
    [InlineData(4310, 0xDEADBEEFu)]
    [InlineData(-1, 0u)]
    [InlineData(-1, 0xFFFFFFFFu)]
    [InlineData(-4310, 12345u)]
    [InlineData(int.MaxValue, 0x1u)]
    [InlineData(int.MinValue, 0x7FFFFFFFu)]
    [InlineData(123456789, 0xABCDEF01u)]
    [InlineData(-987654321, 0x13579BDFu)]
    public void ObscuredValue_EncodeDecode_RoundTrips(int value, uint key)
    {
        uint hidden = ObscuredValue.EncodeHidden(value, key);
        Assert.Equal(value, ObscuredValue.Decode(hidden, key));
    }

    // (2) GameConstants: 25 stats, 18 campos de fase, e o build 2c430296063a com Gra==0xC1F730.
    [Fact]
    public void GameConstants_Tables_HaveExpectedShape()
    {
        Assert.Equal(25, GameConstants.Stats.Count);
        Assert.Equal(18, GameConstants.StageFields.Count);

        Assert.True(GameConstants.KnownBuilds.ContainsKey("2c430296063a"));
        Assert.Equal(0xC1F730, GameConstants.KnownBuilds["2c430296063a"].Gra);
    }

    // (3) SymbolTable.LoadKnownBuild popula o dict a partir do build conhecido.
    [Fact]
    public void SymbolTable_LoadKnownBuild_PopulatesOffsets()
    {
        var sym = new SymbolTable();

        Assert.True(sym.LoadKnownBuild("2c430296063a"));
        Assert.Equal(0x5DD2A30, sym.Get("cube_slot"));
        Assert.Equal(0x6F65F0, sym.Ynj[0]);
    }

    // (4) SymbolTable.LoadOffsetsJson le o cache no formato do Python (dict plano + ynj + inv_class).
    [Fact]
    public void SymbolTable_LoadOffsetsJson_ReadsFlatCache()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tbh_offsets_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"gra":123,"ynj":[9],"inv_class":"x","uo_max":80}""");

        try
        {
            var sym = new SymbolTable();
            Assert.True(sym.LoadOffsetsJson(path));
            Assert.Equal(123, sym.Get("gra"));
            Assert.Equal(9, sym.Ynj[0]);
            Assert.Equal("x", sym.InvClass);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AutoUpdate_TryParseSha256_AcceptsStandardSidecar()
    {
        const string hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        Assert.True(AutoUpdate.TryParseSha256($"{hash}  TaskHeroX-v0.2.0-win-x64.zip\n", out string parsed));
        Assert.Equal(hash, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015a")]
    public void AutoUpdate_TryParseSha256_RejectsMalformed(string text)
    {
        Assert.False(AutoUpdate.TryParseSha256(text, out _));
    }

    [Fact]
    public void AutoUpdate_Sha256Matches_UsesKnownVector()
    {
        byte[] abc = Encoding.ASCII.GetBytes("abc");
        const string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        Assert.True(AutoUpdate.Sha256Matches(abc, expected));
        Assert.False(AutoUpdate.Sha256Matches(Encoding.ASCII.GetBytes("abd"), expected));
    }

    [Fact]
    public void AutoUpdate_CompareVersions_HandlesReleaseTags()
    {
        Assert.True(AutoUpdate.CompareVersions("v0.2.0", "0.1.0") > 0);
        Assert.Equal(0, AutoUpdate.CompareVersions("v1.0.0", "1.0.0"));
        Assert.True(AutoUpdate.CompareVersions("1.0.0", "1.1.0") < 0);
    }
}
