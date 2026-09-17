using System.IO;
using System.Text.Json;

namespace TaskHeroX.App.Services;

/// <summary>Um perfil salvo do usuário: switches (proteção/automação), filtros do fuse e stats/campos forçados.</summary>
public sealed class Profile
{
    public Dictionary<string, bool> Switches { get; set; } = new();
    public int FuseGrade { get; set; } = 2;
    public List<int> FuseTypes { get; set; } = [0, 1, 2];
    public Dictionary<string, double> Stats { get; set; } = new();
    public Dictionary<string, int> Stage { get; set; } = new();
}

/// <summary>
/// Persistência dos perfis em <c>profiles.json</c> na pasta do executável.
/// Se a pasta não for gravável, usa <c>%APPDATA%/TaskHeroX</c>.
/// Perfis do antigo <c>%APPDATA%/tbh_bot</c> continuam sendo detectados e migrados.
/// </summary>
public sealed class ProfileStore
{
    private const string FileName = "profiles.json";
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static string ExeDir
    {
        get
        {
            var p = Environment.ProcessPath;
            var d = string.IsNullOrEmpty(p) ? null : Path.GetDirectoryName(p);
            return string.IsNullOrEmpty(d) ? AppContext.BaseDirectory : d;
        }
    }

    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskHeroX");

    /// <summary>Pasta histórica usada pelo projeto anterior.</summary>
    public static string LegacyDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tbh_bot");

    private static string ExeFile => Path.Combine(ExeDir, FileName);
    private static string AppDataFile => Path.Combine(AppDataDir, FileName);
    private static string LegacyFile => Path.Combine(LegacyDir, FileName);

    public string ResolvedPath { get; private set; } = ExeFile;
    public Action<string>? Log;

    public Dictionary<string, Profile> Load()
    {
        var besideExe = TryRead(ExeFile);
        if (besideExe is not null) { ResolvedPath = ExeFile; return besideExe; }

        var appData = TryRead(AppDataFile);
        if (appData is not null) { ResolvedPath = AppDataFile; return appData; }

        // Último fallback: projeto antigo. Save() grava no destino TaskHeroX preferido.
        var legacy = TryRead(LegacyFile);
        if (legacy is not null)
        {
            Save(legacy);
            Log?.Invoke("perfis herdados do tbh_bot foram migrados para TaskHeroX");
            return legacy;
        }

        ResolvedPath = ExeFile;
        return new();
    }

    private static Dictionary<string, Profile>? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var all = JsonSerializer.Deserialize<Dictionary<string, Profile>>(File.ReadAllText(path));
            return all is { Count: > 0 } ? all : null;
        }
        catch { return null; }
    }

    public void Save(Dictionary<string, Profile> all)
    {
        if (TryWrite(ExeDir, all))
        {
            ResolvedPath = ExeFile;
            BackupLegacyFile();
            return;
        }

        if (TryWrite(AppDataDir, all))
        {
            ResolvedPath = AppDataFile;
            BackupLegacyFile();
            Log?.Invoke($"pasta do exe não é gravável — perfis salvos em {AppDataFile}");
        }
        else
        {
            Log?.Invoke("não consegui salvar os perfis (sem permissão de escrita nos destinos TaskHeroX)");
        }
    }

    private void BackupLegacyFile()
    {
        try
        {
            if (!File.Exists(LegacyFile)) return;
            File.Move(LegacyFile, LegacyFile + ".bak", overwrite: true);
            Log?.Invoke("arquivo de perfis legado preservado como profiles.json.bak");
        }
        catch { }
    }

    private static bool TryWrite(string dir, Dictionary<string, Profile> all)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, FileName), JsonSerializer.Serialize(all, Opt));
            return true;
        }
        catch { return false; }
    }
}
