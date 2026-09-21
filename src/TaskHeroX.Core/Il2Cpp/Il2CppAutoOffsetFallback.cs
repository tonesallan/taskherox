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

        string? expectedMetadataHash = BuildInfo.FileSha256(metadataPath);
        if (string.IsNullOrWhiteSpace(expectedMetadataHash))
            return new(null, "global-metadata.dat hash indisponivel");

        string toolDir = Path.Combine(
            Path.GetTempPath(),
            "TaskHeroX-dumper-" + Guid.NewGuid().ToString("N"));
        string tempCache = Path.GetFullPath(cachePath) + ".tmp-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(toolDir);

        try
        {
            string dumperPath = Il2CppBundledDumperPackage.Materialize(toolDir);

            var inputs = new Il2CppDumperInputs(
                dumperPath,
                Path.GetFullPath(gameAssemblyPath),
                metadataPath);

            Il2CppAutoOffsetPipelineResult result = await Il2CppAutoOffsetPipeline.RunAsync(
                inputs,
                timeout,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            // Evita publicar offsets se qualquer input IL2CPP mudou enquanto o dumper estava
            // em execucao. GameAssembly usa a identidade de build existente; metadata usa SHA-256
            // completo porque alteracoes fora dos primeiros 2 MB tambem invalidam o par.
            if (!InputsStillMatch(
                    expectedHash,
                    gameAssemblyPath,
                    expectedMetadataHash,
                    metadataPath,
                    out string? inputError))
            {
                return new(null, inputError);
            }

            string fullCachePath = Path.GetFullPath(cachePath);
            string cacheDirectory = Path.GetDirectoryName(fullCachePath)!;
            Directory.CreateDirectory(cacheDirectory);

            await File.WriteAllTextAsync(
                tempCache,
                result.CacheJson,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            byte[] serialized = await File.ReadAllBytesAsync(tempCache, cancellationToken)
                .ConfigureAwait(false);
            if (!Il2CppOffsetCache.TryValidateSerialized(serialized, out string? validationError))
                return new(null, $"cache gerado foi rejeitado no gate READY: {validationError}");

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

    internal static bool InputsStillMatch(
        string expectedAssemblyHash,
        string gameAssemblyPath,
        string expectedMetadataHash,
        string metadataPath,
        out string? error)
    {
        string? currentAssemblyHash = BuildInfo.DllHash(gameAssemblyPath);
        if (!string.Equals(currentAssemblyHash, expectedAssemblyHash, StringComparison.OrdinalIgnoreCase))
        {
            error = $"build mudou durante a extracao ({currentAssemblyHash ?? "?"})";
            return false;
        }

        string? currentMetadataHash = BuildInfo.FileSha256(metadataPath);
        if (!string.Equals(currentMetadataHash, expectedMetadataHash, StringComparison.OrdinalIgnoreCase))
        {
            error = $"global-metadata.dat mudou durante a extracao ({currentMetadataHash ?? "?"})";
            return false;
        }

        error = null;
        return true;
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
