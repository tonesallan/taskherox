using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TbhBot.App.Services;

/// <summary>
/// Localiza/inicia o Taskbar Hero sem depender de um caminho fixo.
/// Prioridade: caminho escolhido pelo usuário -> instalação Steam descoberta por appmanifest.
/// Também oferece os protocolos oficiais steam://run e steam://install como fallback.
/// </summary>
public sealed class GameLauncherService
{
    public const int SteamAppId = 3678970;
    public const string SteamStoreUrl = "https://store.steampowered.com/app/3678970/";

    private sealed class LauncherSettings
    {
        public string? GameTarget { get; set; }
    }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskHeroX",
        "launcher.json");

    public string? SavedTarget => LoadSettings().GameTarget;

    public string? FindPreferredTarget()
    {
        string? saved = SavedTarget;
        if (IsLaunchableFile(saved)) return saved;

        string? steam = DiscoverSteamGameExecutable();
        if (IsLaunchableFile(steam))
        {
            SaveTarget(steam!);
            return steam;
        }

        return null;
    }

    public bool TryLaunchInstalled(out string message)
    {
        string? target = FindPreferredTarget();
        if (target is null)
        {
            message = "Taskbar Hero não foi localizado automaticamente.";
            return false;
        }

        if (TryShellStart(target, out string error))
        {
            message = $"Jogo iniciado por {target}";
            return true;
        }

        message = $"Não foi possível abrir o alvo salvo: {error}";
        return false;
    }

    public bool TryLaunchTarget(string target, out string message)
    {
        if (!IsLaunchableFile(target))
        {
            message = "O arquivo selecionado não existe mais.";
            return false;
        }

        SaveTarget(target);
        if (TryShellStart(target, out string error))
        {
            message = $"Jogo iniciado por {target}";
            return true;
        }

        message = error;
        return false;
    }

    public bool TryLaunchSteam(out string message)
    {
        if (TryShellStart($"steam://run/{SteamAppId}", out string error))
        {
            message = "Solicitação de inicialização enviada para a Steam.";
            return true;
        }

        message = $"Não foi possível abrir a Steam: {error}";
        return false;
    }

    public bool TryInstallFromSteam(out string message)
    {
        if (TryShellStart($"steam://install/{SteamAppId}", out string error))
        {
            message = "Solicitação de instalação enviada para a Steam.";
            return true;
        }

        message = $"Não foi possível abrir a Steam: {error}";
        return false;
    }

    public bool TryOpenSteamStore(out string message)
    {
        if (TryShellStart(SteamStoreUrl, out string error))
        {
            message = "Página do Taskbar Hero aberta na Steam.";
            return true;
        }

        message = $"Não foi possível abrir a página da Steam: {error}";
        return false;
    }

    public void SaveTarget(string target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                new LauncherSettings { GameTarget = target },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void ForgetTarget()
    {
        try
        {
            if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
        }
        catch { }
    }

    public string? DiscoverSteamGameExecutable()
    {
        foreach (string library in SteamLibraries())
        {
            try
            {
                string steamapps = Path.Combine(library, "steamapps");
                string manifest = Path.Combine(steamapps, $"appmanifest_{SteamAppId}.acf");
                if (!File.Exists(manifest)) continue;

                string text = File.ReadAllText(manifest);
                Match m = Regex.Match(text, "\\\"installdir\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                string installDir = m.Groups[1].Value.Replace("\\\\", "\\");
                string gameDir = Path.Combine(steamapps, "common", installDir);
                if (!Directory.Exists(gameDir)) continue;

                foreach (string name in new[] { "TaskBarHero.exe", "TaskbarHero.exe", "Task Bar Hero.exe" })
                {
                    string exact = Path.Combine(gameDir, name);
                    if (File.Exists(exact)) return exact;
                }

                string? fallback = Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(p =>
                    {
                        string n = Path.GetFileName(p);
                        return !n.Contains("UnityCrashHandler", StringComparison.OrdinalIgnoreCase) &&
                               !n.Contains("unins", StringComparison.OrdinalIgnoreCase) &&
                               !n.Contains("crash", StringComparison.OrdinalIgnoreCase);
                    });
                if (fallback is not null) return fallback;
            }
            catch { }
        }

        return null;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddRegistryPath(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddRegistryPath(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        AddRegistryPath(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(pf86)) roots.Add(Path.Combine(pf86, "Steam"));

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Where(Directory.Exists))
        {
            libraries.Add(root);
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            try
            {
                string text = File.ReadAllText(vdf);
                foreach (Match m in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(p)) libraries.Add(p);
                }
            }
            catch { }
        }

        return libraries;
    }

    private static void AddRegistryPath(HashSet<string> paths, RegistryKey root, string keyPath, string valueName)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(keyPath);
            if (key?.GetValue(valueName) is string p && !string.IsNullOrWhiteSpace(p))
                paths.Add(p.Replace('/', Path.DirectorySeparatorChar));
        }
        catch { }
    }

    private static LauncherSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new LauncherSettings();
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsPath)) ?? new LauncherSettings();
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    private static bool IsLaunchableFile(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
           (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".url", StringComparison.OrdinalIgnoreCase));

    private static bool TryShellStart(string target, out string error)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
