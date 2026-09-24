using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Materializa o pacote Windows self-contained do Il2CppDumper que o build embute no Core.
/// O pacote precisa carregar o próprio runtime (single-file grande ou coreclr/hostfxr ao lado);
/// assim o fallback não depende de uma instalação global do .NET na máquina do usuário.
/// </summary>
public static class Il2CppBundledDumperPackage
{
    public const string ResourcePrefix = "TaskHeroX.Core.Tools.Il2CppDumperPackage.";
    private const long MinimumSingleFileBytes = 5_000_000;

    public static string Materialize(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);

        Assembly assembly = typeof(Il2CppBundledDumperPackage).Assembly;
        string[] resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resources.Length == 0)
            throw new InvalidDataException("pacote Il2CppDumper self-contained nao foi embutido");

        foreach (string resourceName in resources)
        {
            string fileName = resourceName[ResourcePrefix.Length..];
            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
                throw new InvalidDataException($"nome de recurso Il2CppDumper invalido: {resourceName}");

            using Stream? source = assembly.GetManifestResourceStream(resourceName);
            if (source is null || source.Length == 0)
                throw new InvalidDataException($"recurso Il2CppDumper vazio: {resourceName}");

            string destination = Path.Combine(directory, fileName);
            using FileStream target = new(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            source.CopyTo(target);
        }

        string exePath = Path.Combine(directory, "Il2CppDumper.exe");
        string configPath = Path.Combine(directory, "config.json");
        if (!File.Exists(exePath) || !File.Exists(configPath))
            throw new InvalidDataException("pacote Il2CppDumper incompleto (exe/config ausente)");

        // O pacote oficial vem com RequireAnyKey=true para uso interativo. O fallback roda sem console
        // e com stdout/stderr redirecionados; portanto sempre força false antes de iniciar o processo.
        JsonObject? config = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject;
        if (config is null)
            throw new InvalidDataException("config.json do Il2CppDumper invalido");
        config["RequireAnyKey"] = false;
        File.WriteAllText(
            configPath,
            config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        bool runtimeIsBundled =
            new FileInfo(exePath).Length >= MinimumSingleFileBytes ||
            File.Exists(Path.Combine(directory, "coreclr.dll")) ||
            File.Exists(Path.Combine(directory, "hostfxr.dll"));

        if (!runtimeIsBundled)
            throw new InvalidDataException(
                "Il2CppDumper embutido parece framework-dependent; runtime self-contained ausente");

        return exePath;
    }
}
