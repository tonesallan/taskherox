using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace TbhBot.Core.Update;

/// <summary>
/// Auto-update via GitHub Releases.
///
/// Cada release TaskHeroX pode carregar os offsets do build novo embutidos
/// (Offsets/offsets_&lt;hash&gt;.json), permitindo restaurar compatibilidade sem
/// exigir ferramentas de desenvolvimento na maquina do usuario.
/// </summary>
public sealed class AutoUpdate
{
    public const string Repo = "tonesallan/taskherox";

    /// <summary>
    /// Versao deste build. Vem do assembly (&lt;Version&gt; do Directory.Build.props).
    /// </summary>
    public static readonly string CurrentVersion = ResolveVersion();

    private static string ResolveVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AutoUpdate).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                      ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private const string UserAgent = "TaskHeroX-updater";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        c.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        return c;
    }

    public static int[] VerTuple(string? s)
    {
        var trimmed = (s ?? "").TrimStart('v', 'V').Trim();
        if (trimmed.Length == 0) return [0];
        var parts = trimmed.Split('.');
        var outv = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var digits = new string([.. parts[i].Where(char.IsDigit)]);
            outv[i] = digits.Length > 0 ? int.Parse(digits) : 0;
        }
        return outv;
    }

    public static int CompareVersions(string a, string b)
    {
        int[] ta = VerTuple(a), tb = VerTuple(b);
        int n = Math.Max(ta.Length, tb.Length);
        for (int i = 0; i < n; i++)
        {
            int x = i < ta.Length ? ta[i] : 0;
            int y = i < tb.Length ? tb[i] : 0;
            if (x != y) return x < y ? -1 : 1;
        }
        return 0;
    }

    /// <summary>
    /// Consulta releases/latest do TaskHeroX. Retorna Available=true somente
    /// quando existe versao maior e um asset .zip publicavel.
    /// </summary>
    public async Task<(bool Available, string Tag, string Url)> CheckAsync(
        string currentVersion, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{Repo}/releases/latest";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
            if (CompareVersions(tag, currentVersion) <= 0)
                return (false, tag, "");

            if (root.TryGetProperty("assets", out var assets) &&
                assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    var dl = a.TryGetProperty("browser_download_url", out var b) ? (b.GetString() ?? "") : "";
                    if (dl.Length > 0)
                        return (true, tag, dl);
                }
            }
        }
        catch
        {
            // Sem rede / rate-limit / release ausente -> segue sem update.
        }
        return (false, "", "");
    }

    public async Task<(string NewExe, string Exe, string ExeDir)> DownloadAndStageAsync(
        string url, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string exe = CurrentExePath();
        string exeDir = Path.GetDirectoryName(exe) ?? Directory.GetCurrentDirectory();

        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;

        using var buf = new MemoryStream();
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var chunk = new byte[65536];
            long read = 0;
            int got;
            while ((got = await src.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false)) > 0)
            {
                buf.Write(chunk, 0, got);
                read += got;
                if (progress is not null && total > 0)
                    progress.Report(Math.Min((double)read / total, 1.0));
            }
        }

        buf.Position = 0;
        using var zip = new ZipArchive(buf, ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e => IsPanelExe(e.Name))
            ?? throw new InvalidOperationException("zip do release nao contem TaskHeroX.exe");

        byte[] data;
        await using (var es = entry.Open())
        using (var ms = new MemoryStream())
        {
            await es.CopyToAsync(ms, ct).ConfigureAwait(false);
            data = ms.ToArray();
        }

        if (data.Length < 1_000_000)
            throw new InvalidOperationException($"exe baixado pequeno demais ({data.Length} bytes)");

        string newExe = exe + ".new.exe";
        await File.WriteAllBytesAsync(newExe, data, ct).ConfigureAwait(false);
        return (newExe, exe, exeDir);
    }

    private static bool IsPanelExe(string name)
    {
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("TaskHeroX.exe", StringComparison.OrdinalIgnoreCase)) return true;
        // Compatibilidade temporaria durante a migracao do nome interno dos projetos.
        if (name.Equals("TBH_Panel.exe", StringComparison.OrdinalIgnoreCase)) return true;
        return name.StartsWith("TbhBot", StringComparison.OrdinalIgnoreCase);
    }

    private const string UpdaterBat =
        "@echo off\r\n" +
        "setlocal\r\n" +
        "set \"EXE=%~1\"\r\n" +
        "set \"NEW=%~2\"\r\n" +
        "set PID=%~3\r\n" +
        ":wait\r\n" +
        "tasklist /FI \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul\r\n" +
        "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto wait )\r\n" +
        ":movetry\r\n" +
        "move /y \"%NEW%\" \"%EXE%\" >nul 2>&1\r\n" +
        "if errorlevel 1 ( timeout /t 1 /nobreak >nul & goto movetry )\r\n" +
        "start \"\" \"%EXE%\"\r\n" +
        "del \"%~f0\"\r\n";

    public void LaunchUpdater(string newExe, string exe, string exeDir)
    {
        string bat = Path.Combine(exeDir, "_taskherox_update.bat");
        File.WriteAllText(bat, UpdaterBat, System.Text.Encoding.ASCII);

        int pid = Environment.ProcessId;
        var psi = new ProcessStartInfo
        {
            FileName = "cmd",
            WorkingDirectory = exeDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(bat);
        psi.ArgumentList.Add(exe);
        psi.ArgumentList.Add(newExe);
        psi.ArgumentList.Add(pid.ToString());
        Process.Start(psi);
    }

    private static string CurrentExePath()
    {
        var p = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(p)) return p;
        return Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "TaskHeroX.exe");
    }
}
