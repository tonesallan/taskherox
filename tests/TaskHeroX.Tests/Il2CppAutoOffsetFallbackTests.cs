using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppAutoOffsetFallbackTests
{
    [Fact]
    public void BundledDumperPackage_IsEmbeddedAndSelfContained()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "TaskHeroX-bundled-dumper-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            string exe = Il2CppBundledDumperPackage.Materialize(dir);
            string config = Path.Combine(dir, "config.json");

            Assert.True(File.Exists(exe));
            Assert.True(File.Exists(config));

            bool runtimeBundled =
                new FileInfo(exe).Length >= 5_000_000 ||
                File.Exists(Path.Combine(dir, "coreclr.dll")) ||
                File.Exists(Path.Combine(dir, "hostfxr.dll"));

            Assert.True(runtimeBundled, "bundled Il2CppDumper must carry its own runtime");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task BundledDumper_StartsWithoutGlobalDotNetRuntime()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string dir = Path.Combine(
            Path.GetTempPath(),
            "TaskHeroX-bundled-dumper-start-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            string exe = Il2CppBundledDumperPackage.Materialize(dir);
            string fakeDotnet = Path.Combine(dir, "no-global-dotnet");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["DOTNET_ROOT"] = fakeDotnet;
            psi.Environment["DOTNET_ROOT_X64"] = fakeDotnet;
            psi.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            psi.ArgumentList.Add("--help");

            using var process = new System.Diagnostics.Process { StartInfo = psi };
            Assert.True(process.Start());

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(cts.Token);

            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            string combined = stdout + Environment.NewLine + stderr;

            Assert.Equal(0, process.ExitCode);
            Assert.Contains("usage:", combined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("You must install or update .NET", combined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Microsoft.NETCore.App", combined, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
