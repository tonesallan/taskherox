using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace TbhBot.Core.Update;

/// <summary>
/// Auto-update via GitHub Releases.
///
/// Releases oficiais precisam publicar o ZIP e um sidecar <c>.sha256</c> com o
/// mesmo nome. O ZIP so e aberto depois que o SHA-256 baixado do release foi
/// validado, evitando executar um pacote corrompido ou alterado em transito.
/// </summary>
public sealed class AutoUpdate
{
    public const string Repo = "tonesallan/taskherox";

    /// <summary>
    /// Versao deste build. Vem do assembly (&lt;Version&gt; do Directory.Build.props)
    /// ou da versao injetada pelo workflow de release.
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
    private const long MaxUpdateZipBytes = 512L * 1024 * 1024;
    private const int MaxChecksumTextBytes = 16 * 1024;

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
    /// Aceita o formato padrao de checksum (<c>hash  arquivo.zip</c>) ou apenas
    /// os 64 caracteres hexadecimais. Outros formatos sao rejeitados.
    /// </summary>
    public static bool TryParseSha256(string? text, out string sha256)
    {
        sha256 = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string first = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        if (first.Length != 64 || first.Any(c => !Uri.IsHexDigit(c))) return false;

        sha256 = first.ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// Utilitario testavel para a mesma validacao criptografica usada no updater.
    /// </summary>
    public static bool Sha256Matches(ReadOnlySpan<byte> data, string expectedSha256)
    {
        if (!TryParseSha256(expectedSha256, out string normalized)) return false;
        byte[] expected;
        try { expected = Convert.FromHexString(normalized); }
        catch { return false; }

        byte[] actual = SHA256.HashData(data);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Consulta releases/latest do TaskHeroX. Available=true somente quando existe
    /// versao maior com um ZIP TaskHeroX e o sidecar correspondente .sha256.
    /// Release sem checksum nao e oferecido ao usuario.
    /// </summary>
    public async Task<(bool Available, string Tag, string Url, string Sha256Url)> CheckAsync(
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
                return (false, tag, "", "");

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return (false, tag, "", "");

            var found = new List<(string Name, string Url)>();
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                string dl = a.TryGetProperty("browser_download_url", out var b) ? (b.GetString() ?? "") : "";
                if (name.Length > 0 && dl.Length > 0) found.Add((name, dl));
            }

            var zip = found.FirstOrDefault(a =>
                a.Name.StartsWith("TaskHeroX-", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(zip.Name))
                return (false, tag, "", "");

            string checksumName = zip.Name + ".sha256";
            var checksum = found.FirstOrDefault(a =>
                a.Name.Equals(checksumName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(checksum.Name))
                return (false, tag, "", "");

            return (true, tag, zip.Url, checksum.Url);
        }
        catch
        {
            // Sem rede / rate-limit / release ausente -> segue sem update.
        }
        return (false, "", "", "");
    }

    public async Task<(string NewExe, string Exe, string ExeDir)> DownloadAndStageAsync(
        string url,
        string sha256Url,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        string exe = CurrentExePath();
        string exeDir = Path.GetDirectoryName(exe) ?? Directory.GetCurrentDirectory();
        string expectedSha256 = await DownloadExpectedSha256Async(sha256Url, ct).ConfigureAwait(false);

        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;
        if (total > MaxUpdateZipBytes)
            throw new InvalidDataException($"pacote de update excede o limite de {MaxUpdateZipBytes / 1024 / 1024} MB");

        using var buf = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var chunk = new byte[65536];
            long read = 0;
            int got;
            while ((got = await src.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false)) > 0)
            {
                read += got;
                if (read > MaxUpdateZipBytes)
                    throw new InvalidDataException($"pacote de update excede o limite de {MaxUpdateZipBytes / 1024 / 1024} MB");

                buf.Write(chunk, 0, got);
                hash.AppendData(chunk, 0, got);
                if (progress is not null && total > 0)
                    progress.Report(Math.Min((double)read / total, 1.0));
            }
        }

        string actualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!FixedTimeHashEquals(actualSha256, expectedSha256))
            throw new InvalidDataException(
                $"falha de integridade SHA-256: esperado {expectedSha256}, recebido {actualSha256}");

        // So abre/processa o ZIP depois da verificacao criptografica.
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

    private static async Task<string> DownloadExpectedSha256Async(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidDataException("release sem checksum SHA-256");

        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long len = resp.Content.Headers.ContentLength ?? 0;
        if (len > MaxChecksumTextBytes)
            throw new InvalidDataException("arquivo SHA-256 invalido: tamanho excessivo");

        string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (text.Length > MaxChecksumTextBytes || !TryParseSha256(text, out string expected))
            throw new InvalidDataException("arquivo SHA-256 invalido ou malformado");
        return expected;
    }

    private static bool FixedTimeHashEquals(string actualHex, string expectedHex)
    {
        try
        {
            byte[] actual = Convert.FromHexString(actualHex);
            byte[] expected = Convert.FromHexString(expectedHex);
            return actual.Length == expected.Length &&
                   CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
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
