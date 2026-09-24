using System.Diagnostics;

namespace TaskHeroX.Core.Il2Cpp;

public sealed record Il2CppDumperInputs(
    string DumperPath,
    string GameAssemblyPath,
    string MetadataPath);

public sealed record Il2CppDumperArtifacts(
    IReadOnlyList<Il2CppDumpClass> Classes,
    Il2CppScriptIndex Script);

/// <summary>
/// Orquestra o Il2CppDumper de forma isolada. Ainda não é chamado pela resolução runtime do trainer;
/// serve como ponte testável entre os arquivos do jogo e os parsers C# já portados.
/// </summary>
public static class Il2CppDumperRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public static ProcessStartInfo CreateStartInfo(
        Il2CppDumperInputs inputs,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputs.DumperPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputs.GameAssemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputs.MetadataPath);

        string? workingDirectory = Path.GetDirectoryName(Path.GetFullPath(inputs.DumperPath));
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new ArgumentException("DumperPath must have a parent directory", nameof(inputs));

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(inputs.DumperPath),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["DOTNET_ROLL_FORWARD"] = "Major";
        startInfo.ArgumentList.Add(Path.GetFullPath(inputs.GameAssemblyPath));
        startInfo.ArgumentList.Add(Path.GetFullPath(inputs.MetadataPath));
        startInfo.ArgumentList.Add(Path.GetFullPath(outputDirectory));
        return startInfo;
    }

    public static async Task<Il2CppDumperArtifacts> RunAsync(
        Il2CppDumperInputs inputs,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ValidateInputFile(inputs.DumperPath, nameof(inputs.DumperPath));
        ValidateInputFile(inputs.GameAssemblyPath, nameof(inputs.GameAssemblyPath));
        ValidateInputFile(inputs.MetadataPath, nameof(inputs.MetadataPath));

        TimeSpan effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "timeout must be positive");

        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "TaskHeroX-il2cpp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            using var process = new Process
            {
                StartInfo = CreateStartInfo(inputs, tempDirectory),
                EnableRaisingEvents = true,
            };

            if (!process.Start())
                throw new InvalidOperationException("Il2CppDumper process did not start");

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(effectiveTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw new TimeoutException($"Il2CppDumper exceeded timeout {effectiveTimeout}");
            }
            catch
            {
                TryKill(process);
                throw;
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                throw new InvalidOperationException(
                    $"Il2CppDumper exited with code {process.ExitCode}: {TrimDiagnostic(details)}");
            }

            string dumpPath = Path.Combine(tempDirectory, "dump.cs");
            string scriptPath = Path.Combine(tempDirectory, "script.json");
            ValidateOutputFile(dumpPath);
            ValidateOutputFile(scriptPath);

            return new Il2CppDumperArtifacts(
                Il2CppDumpParser.ParseFile(dumpPath),
                Il2CppScriptIndex.ParseFile(scriptPath));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup: falha de limpeza não invalida artifacts já parseados.
            }
        }
    }

    private static void ValidateInputFile(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException($"Required IL2CPP input not found: {path}", path);
    }

    private static void ValidateOutputFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0)
            throw new InvalidDataException($"Il2CppDumper did not produce a valid {Path.GetFileName(path)}");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort termination only.
        }
    }

    private static string TrimDiagnostic(string value)
    {
        string text = (value ?? string.Empty).Trim();
        const int max = 1000;
        return text.Length <= max ? text : text[..max] + "…";
    }
}
