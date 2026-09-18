using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppAutoOffsetFallbackTests
{
    [Fact]
    public void BundledDumperResources_AreEmbedded()
    {
        var assembly = typeof(Il2CppAutoOffsetFallback).Assembly;
        string[] resources = assembly.GetManifestResourceNames();

        Assert.Contains("TaskHeroX.Core.Tools.Il2CppDumper.exe", resources);
        Assert.Contains("TaskHeroX.Core.Tools.Il2CppDumper.config.json", resources);

        using Stream exe = Assert.IsAssignableFrom<Stream>(
            assembly.GetManifestResourceStream("TaskHeroX.Core.Tools.Il2CppDumper.exe"));
        using Stream config = Assert.IsAssignableFrom<Stream>(
            assembly.GetManifestResourceStream("TaskHeroX.Core.Tools.Il2CppDumper.config.json"));

        Assert.True(exe.Length > 0);
        Assert.True(config.Length > 0);
    }

    [Fact]
    public async Task TryGenerateAsync_RejectsHashMismatchBeforeRunningDumper()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TaskHeroX-fallback-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            string gameAssembly = Path.Combine(dir, "GameAssembly.dll");
            string cache = Path.Combine(dir, "cache", "offsets_deadbeef0000.json");
            await File.WriteAllBytesAsync(gameAssembly, [1, 2, 3, 4, 5]);

            Il2CppAutoOffsetFallbackResult result =
                await Il2CppAutoOffsetFallback.TryGenerateAsync(
                    "deadbeef0000",
                    gameAssembly,
                    cache);

            Assert.False(result.Success);
            Assert.Contains("build mudou antes da extracao", result.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(cache));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TryGenerateAsync_RejectsMissingMetadataWithoutPersistingCache()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TaskHeroX-fallback-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            string gameAssembly = Path.Combine(dir, "GameAssembly.dll");
            string cache = Path.Combine(dir, "cache", "offsets_test.json");
            await File.WriteAllBytesAsync(gameAssembly, [10, 20, 30, 40, 50]);

            string hash = Assert.IsType<string>(BuildInfo.DllHash(gameAssembly));

            Il2CppAutoOffsetFallbackResult result =
                await Il2CppAutoOffsetFallback.TryGenerateAsync(
                    hash,
                    gameAssembly,
                    cache);

            Assert.False(result.Success);
            Assert.Equal("global-metadata.dat nao encontrado", result.Error);
            Assert.False(File.Exists(cache));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
