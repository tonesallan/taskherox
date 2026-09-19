using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using TaskHeroX.Core;
using TaskHeroX.Core.Diagnostics;
using TaskHeroX.Core.Game;
using TaskHeroX.Core.Il2Cpp;
using TaskHeroX.Core.Memory;
using TaskHeroX.Core.Update;

namespace TaskHeroX.Tests;

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
    public void SymbolTable_RequireVersion_RejectsPreCubeLayoutV8Cache()
    {
        using var oldCache = new MemoryStream(
            Encoding.UTF8.GetBytes("""{"_ver":8,"gra":123}"""));
        using var currentCache = new MemoryStream(
            Encoding.UTF8.GetBytes(
                "{\"_ver\":" + SymbolTable.MinExtractVer + ",\"gra\":123}"));

        var oldTable = new SymbolTable();
        var currentTable = new SymbolTable();

        Assert.False(oldTable.LoadOffsetsJson(oldCache, requireVersion: true));
        Assert.True(currentTable.LoadOffsetsJson(currentCache, requireVersion: true));
        Assert.Equal(9, SymbolTable.MinExtractVer);
    }

    [Fact]
    public void Engine_LoadOffsetsFrom_RejectsIncompleteV9WithoutMutatingLiveSymbols()
    {
        string path = Path.Combine(Path.GetTempPath(), $"taskherox_partial_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $"{{\"_ver\":{SymbolTable.MinExtractVer},\"gra\":123}}");

        try
        {
            using var engine = new Engine();
            var symbols = new SymbolTable();
            SetBackingField(engine, "Symbols", symbols);

            Assert.False(engine.LoadOffsetsFrom(path, source: "feed"));
            Assert.False(symbols.Has("gra"));
            Assert.False(engine.OffsetsLoaded);
            Assert.Null(engine.OffsetsSource);
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

    [Fact]
    public void SupportBundleCollector_Offline_IsBestEffortAndDeterministic()
    {
        using var engine = new Engine();
        var at = new DateTimeOffset(2026, 9, 16, 1, 30, 0, TimeSpan.Zero);

        var bundle = SupportBundleCollector.Collect(
            engine,
            "0.1.1",
            [@"01:29:59  cache C:\Users\TONES\AppData\Roaming\TaskHeroX"],
            at);

        Assert.Equal(SupportBundleCollector.SchemaVersion, bundle.SchemaVersion);
        Assert.Equal(at, bundle.CreatedUtc);
        Assert.False(bundle.Game.Attached);
        Assert.Equal("not-loaded", bundle.Offsets.Source);
        Assert.Equal(25, bundle.Compatibility.Stats.Length);
        Assert.All(bundle.Compatibility.Stats, s => Assert.False(s.Readable));
        Assert.DoesNotContain("TONES", bundle.LogTail.Single());
    }

    [Fact]
    public void SupportBundleCollector_UsesCanonicalEngineOffsetSource()
    {
        using var engine = new Engine();
        SetBackingField(engine, "OffsetsLoaded", true);
        SetBackingField(engine, "OffsetsSource", Engine.AutoExtractOffsetsSource);

        var bundle = SupportBundleCollector.Collect(
            engine,
            "0.1.1",
            [],
            new DateTimeOffset(2026, 9, 18, 15, 45, 0, TimeSpan.Zero));

        Assert.True(bundle.Offsets.Loaded);
        Assert.Equal(Engine.AutoExtractOffsetsSource, bundle.Offsets.Source);
    }

    [Fact]
    public void SupportBundleCollector_StaleAttachWithDeadProcess_ReportsOffline()
    {
        var engine = new Engine();

        SetBackingField(engine.Target, "Handle", (nint)1);
        SetBackingField(engine.Target, "ProcessId", int.MaxValue);
        SetBackingField(engine.Target, "ModuleBase", (nint)0x1234);
        SetBackingField(engine.Target, "ModuleSize", 4096);
        SetBackingField(engine.Target, "ModulePath", @"C:\Games\TaskBarHero\GameAssembly.dll");
        SetBackingField(engine, "Memory", (MemoryAccess)RuntimeHelpers.GetUninitializedObject(typeof(MemoryAccess)));

        Assert.True(engine.IsAttached);
        Assert.False(engine.Target.IsAlive());

        var bundle = SupportBundleCollector.Collect(
            engine,
            "0.1.1",
            [],
            new DateTimeOffset(2026, 9, 16, 1, 31, 0, TimeSpan.Zero));

        Assert.False(bundle.Game.Attached);
        Assert.Null(bundle.Game.ProcessId);
        Assert.Null(bundle.Game.ModuleBase);
        Assert.Null(bundle.Game.ModuleSize);
        Assert.Equal(0, bundle.Compatibility.ReadableStats);
        Assert.All(bundle.Compatibility.Stats, s => Assert.False(s.Readable));
    }

    [Fact]
    public void SupportBundleRedactor_RemovesPersonalIdentifiers()
    {
        const string input = @"C:\Users\TONES\Desktop\x.txt steamid=123456 email tones@example.com 76561198012345678";

        string redacted = SupportBundleRedactor.RedactText(input);

        Assert.DoesNotContain("TONES", redacted);
        Assert.DoesNotContain("123456", redacted);
        Assert.DoesNotContain("tones@example.com", redacted);
        Assert.DoesNotContain("76561198012345678", redacted);
        Assert.Contains("<redacted>", redacted);
        Assert.Contains("<email-redacted>", redacted);
        Assert.Contains("<steam-id-redacted>", redacted);
    }

    [Fact]
    public void SupportBundleSerializer_UsesStableSchemaField()
    {
        using var engine = new Engine();
        var bundle = SupportBundleCollector.Collect(
            engine,
            "0.1.1",
            [],
            new DateTimeOffset(2026, 9, 16, 1, 30, 0, TimeSpan.Zero));

        string json = SupportBundleSerializer.Serialize(bundle);

        Assert.Contains("\"schemaVersion\": \"taskherox.support-bundle/v1\"", json);
        Assert.Contains("\"version\": \"0.1.1\"", json);
        Assert.Contains("\"createdUtc\": \"2026-09-16T01:30:00+00:00\"", json);
    }

    [Fact]
    public void Engine_AutoExtractOffsetsSource_IsStableAsciiToken()
    {
        Assert.Equal("auto-extract-csharp", Engine.AutoExtractOffsetsSource);
        Assert.All(Engine.AutoExtractOffsetsSource, ch => Assert.InRange((int)ch, 0, 127));
    }

    private static void SetBackingField<TTarget, TValue>(TTarget target, string propertyName, TValue value)
        where TTarget : class
    {
        var field = typeof(TTarget).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }
}
