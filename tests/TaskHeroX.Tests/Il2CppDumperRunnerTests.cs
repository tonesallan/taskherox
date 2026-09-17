using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppDumperRunnerTests
{
    [Fact]
    public void CreateStartInfo_PreservesLegacyInvocationContractWithoutShellQuoting()
    {
        string root = Path.Combine(Path.GetTempPath(), "Task Hero X Runner Test");
        var inputs = new Il2CppDumperInputs(
            Path.Combine(root, "tools", "Il2CppDumper", "Il2CppDumper.exe"),
            Path.Combine(root, "GameAssembly.dll"),
            Path.Combine(root, "TaskBarHero_Data", "il2cpp_data", "Metadata", "global-metadata.dat"));
        string output = Path.Combine(root, "dump output");

        System.Diagnostics.ProcessStartInfo info = Il2CppDumperRunner.CreateStartInfo(inputs, output);

        Assert.Equal(Path.GetFullPath(inputs.DumperPath), info.FileName);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(inputs.DumperPath)), info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.Equal("Major", info.Environment["DOTNET_ROLL_FORWARD"]);
        Assert.Equal(3, info.ArgumentList.Count);
        Assert.Equal(Path.GetFullPath(inputs.GameAssemblyPath), info.ArgumentList[0]);
        Assert.Equal(Path.GetFullPath(inputs.MetadataPath), info.ArgumentList[1]);
        Assert.Equal(Path.GetFullPath(output), info.ArgumentList[2]);
    }

    [Fact]
    public async Task RunAsync_FailsBeforeStartingWhenInputsAreMissing()
    {
        string missingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var inputs = new Il2CppDumperInputs(
            Path.Combine(missingRoot, "Il2CppDumper.exe"),
            Path.Combine(missingRoot, "GameAssembly.dll"),
            Path.Combine(missingRoot, "global-metadata.dat"));

        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            Il2CppDumperRunner.RunAsync(inputs));

        Assert.Contains("Required IL2CPP input not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_RejectsNonPositiveTimeoutBeforeProcessStart()
    {
        string root = Path.Combine(Path.GetTempPath(), "TaskHeroX-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string dumper = Path.Combine(root, "Il2CppDumper.exe");
            string assembly = Path.Combine(root, "GameAssembly.dll");
            string metadata = Path.Combine(root, "global-metadata.dat");
            await File.WriteAllTextAsync(dumper, "stub");
            await File.WriteAllTextAsync(assembly, "stub");
            await File.WriteAllTextAsync(metadata, "stub");

            var inputs = new Il2CppDumperInputs(dumper, assembly, metadata);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                Il2CppDumperRunner.RunAsync(inputs, TimeSpan.Zero));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
