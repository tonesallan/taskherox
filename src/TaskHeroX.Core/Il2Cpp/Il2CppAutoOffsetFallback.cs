using System.Reflection;
using System.Text;

namespace TaskHeroX.Core.Il2Cpp;

public sealed record Il2CppAutoOffsetFallbackResult(
    string? CachePath,
    string? Error)
{
    public bool Success => !string.IsNullOrWhiteSpace(CachePath);
}

/// <summary>
/// Fallback fail-closed para build desconhecido. Materializa o Il2CppDumper embutido em uma pasta
/// temporaria, executa o pipeline C# ja validado e persiste somente um cache versionado que passa
/// novamente por SymbolTable(requireVersion: true).
///
/// Esta classe trabalha apenas com arquivos do jogo no disco; nao escreve memoria do processo.
/// </summary>
public static class Il2CppAutoOffsetFallback
{
    private const string DumperResource = "TaskHeroX.Core.Tools.Il2CppDumper.exe";
    private const string ConfigResource = "TaskHeroX.Core.Tools.Il2CppDumper.config.json";

    public static async Task<Il2CppAutoOffsetFallbackResult> TryGenerateAsync(
        string expectedHash,
        string gameAssemblyPath,
        string cachePath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
            return new(null, "build hash ausente");
        if (string.IsNullOrWhiteSpace(gameAssemblyPath) || !File.Exists(gameAssemblyPath))
            return new(null, "GameAssembly.dll nao encontrado");
        if (string.IsNullOrWhiteSpace(cachePath))
            return new(null, "cache path ausente");

        string? currentHash = BuildInfo.DllHash(gameAssemblyPath);
        if (!string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            return new(null, $"build mudou antes da extracao ({currentHash ?? "?"})");

        string gameDir = Path.GetDirectoryName(Path.GetFullPath(gameAssemblyPath))!;
        string metadataPath = Path.Combine(
            gameDir,
            "TaskBarHero_Data",
            "il2cpp_data",
            "Metadata",
            "global-metadata.dat");

        if (!File.Exists(metadataPath))
            return new(null, "global-metadata.dat nao encontrado");

        string toolDir = Path.Combine(
            Path.GetTempPath(),
            "TaskHeroX-dumper-" + Guid.NewGuid().ToString("N"));
        string tempCache = Path.GetFullPath(cachePath) + ".tmp-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(toolDir);

        try
        {
            string dumperPath = Path.Combine(toolDir, "Il2CppDumper.exe");
            string configPath = Path.Combine(toolDir, "config.json");

            ExtractResource(DumperResource, dumperPath);
            ExtractResource(ConfigResource, configPath);

            var inputs = new Il2CppDumperInputs(
                dumperPath,
                Path.GetFullPath(gameAssemblyPath),
                metadataPath);

            Il2CppAutoOffsetPipelineResult result = await Il2CppAutoOffsetPipeline.RunAsync(
                inputs,
                timeout,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            // Evita publicar offsets de um build enquanto os arquivos do jogo foram atualizados
            // durante a extracao.
            currentHash = BuildInfo.DllHash(gameAssemblyPath);
            if (!string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                return new(null, $"build mudou durante a extracao ({currentHash ?? "?"})");

            string fullCachePath = Path.GetFullPath(cachePath);
            string cacheDirectory = Path.GetDirectoryName(fullCachePath)!;
            Directory.CreateDirectory(cacheDirectory);

            await File.WriteAllTextAsync(
                tempCache,
                result.CacheJson,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            var probe = new SymbolTable();
            if (!probe.LoadOffsetsJson(tempCache, requireVersion: true))
                return new(null, "cache gerado foi rejeitado pelo SymbolTable");

            if (probe.Has("jgc"))
                return new(null, "cache gerado contem generic jgc");

            File.Move(tempCache, fullCachePath, overwrite: true);
            tempCache = string.Empty;

            return new(fullCachePath, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(null, ex.Message);
        }
        finally
        {
            TryDeleteFile(tempCache);
            TryDeleteDirectory(toolDir);
        }
    }

    private static void ExtractResource(string resourceName, string destination)
    {
        Assembly assembly = typeof(Il2CppAutoOffsetFallback).Assembly;
        using Stream? source = assembly.GetManifestResourceStream(resourceName);
        if (source is null)
            throw new InvalidDataException($"recurso embutido ausente: {resourceName}");

        using FileStream target = new(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        source.CopyTo(target);

        if (target.Length == 0)
            throw new InvalidDataException($"recurso embutido vazio: {resourceName}");
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
