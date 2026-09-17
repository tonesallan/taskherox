namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Resultado end-to-end do pipeline C# antes de qualquer integração com a resolução runtime.
/// </summary>
public sealed record Il2CppAutoOffsetPipelineResult(
    Il2CppExtractedOffsets Offsets,
    string CacheJson);

/// <summary>
/// Pipeline opt-in: Il2CppDumper -> parsers -> extração semântica -> validação crítica -> JSON
/// determinístico. Esta classe não grava cache e não altera SymbolTable/Engine automaticamente.
/// </summary>
public static class Il2CppAutoOffsetPipeline
{
    public static async Task<Il2CppAutoOffsetPipelineResult> RunAsync(
        Il2CppDumperInputs inputs,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        Il2CppDumperArtifacts artifacts = await Il2CppDumperRunner.RunAsync(
            inputs,
            timeout,
            cancellationToken).ConfigureAwait(false);

        Il2CppPeImage image = Il2CppPeImage.FromFile(inputs.GameAssemblyPath);
        return BuildFromArtifacts(artifacts, image);
    }

    /// <summary>
    /// Ponto determinístico/testável do pipeline. Recebe artifacts já produzidos pelo dumper e o PE
    /// correspondente, sem executar processo externo. Útil para fixtures conhecidas e CI.
    /// </summary>
    public static Il2CppAutoOffsetPipelineResult BuildFromArtifacts(
        Il2CppDumperArtifacts artifacts,
        Il2CppPeImage image)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(image);

        if (!Il2CppFoundationExtractor.TryExtract(
                artifacts.Classes,
                artifacts.Script,
                image,
                out Il2CppExtractedOffsets? offsets,
                out string? extractionError) || offsets is null)
        {
            throw new InvalidDataException(
                "IL2CPP auto-offset extraction failed: " + (extractionError ?? "unknown extraction error"));
        }

        if (!Il2CppOffsetCache.TryValidate(offsets, out string? validationError))
        {
            throw new InvalidDataException(
                "IL2CPP auto-offset validation failed: " + (validationError ?? "unknown validation error"));
        }

        string cacheJson = Il2CppOffsetCache.Serialize(offsets);
        return new Il2CppAutoOffsetPipelineResult(offsets, cacheJson);
    }
}
