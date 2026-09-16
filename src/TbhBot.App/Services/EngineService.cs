using System.Windows;
using TbhBot.App.Views;
using TbhBot.Core;
using TbhBot.Core.Automation;

namespace TbhBot.App.Services;

/// <summary>
/// Dono do <see cref="Engine"/> para a UI. Attacha/reataca ao jogo em background (equivalente ao watchdog
/// + reconnect do painel Python) e roda o único <see cref="AutomationLoop"/> que lê as flags Want* do engine.
/// Notifica a UI no thread da UI via <see cref="StateChanged"/>.
/// </summary>
public sealed class EngineService
{
    public Engine Engine { get; } = new();
    public bool IsAttached => Engine.IsAttached && Engine.Target.IsAlive();

    /// <summary>Disparado (no thread da UI) quando conecta/desconecta.</summary>
    public event Action? StateChanged;

    /// <summary>Log textual do engine/automação para a barra de status.</summary>
    public event Action<string>? Log;

    private CancellationTokenSource? _cts;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        Engine.Log += OnLog;

        var loop = new AutomationLoop(Engine);
        loop.Log += OnLog;

        var wd = new WatchdogService(this);
        wd.Log += OnLog;

        _ = ConnectLoopAsync(_cts.Token);
        _ = loop.RunAsync(_cts.Token);
        _ = wd.RunAsync(_cts.Token);        // Auto-restart: dono ÚNICO do relançamento/popup/reentrada
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        Engine.Dispose();
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        bool last = false;
        while (!ct.IsCancellationRequested)
        {
            bool now = IsAttached;
            if (!now)
            {
                try { now = Engine.Attach(); } catch { now = false; }
            }
            if (now != last)
            {
                last = now;
                Post(() => StateChanged?.Invoke());
            }

            // Build desconhecida: tenta buscar offsets no feed público do TaskHeroX uma vez por hash.
            if (now && !Engine.OffsetsLoaded && Engine.BuildHash is { Length: > 0 } h && _feedTried != h)
            {
                _feedTried = h;
                _ = FetchOffsetsAsync(h, ct);
            }
            try { await Task.Delay(1000, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private string? _feedTried;

    private async Task FetchOffsetsAsync(string hash, CancellationToken ct)
    {
        var path = await TaskHeroX.Core.Update.OffsetsFeed.TryFetchAsync(hash, ct).ConfigureAwait(false);
        if (path is null || ct.IsCancellationRequested) return;
        if (Engine.LoadOffsetsFrom(path)) Post(() => StateChanged?.Invoke());
    }

    /// <summary>
    /// Inicia o jogo como um launcher de desktop: primeiro tenta o caminho salvo/instalação Steam
    /// descoberta automaticamente; se não localizar, abre o Game Launcher para o usuário escolher
    /// EXE/atalho ou usar os fallbacks oficiais da Steam.
    /// </summary>
    public void LaunchGame()
    {
        Post(() =>
        {
            if (IsAttached) return;

            var launcher = new GameLauncherService();
            if (launcher.TryLaunchInstalled(out string msg))
            {
                RaiseLog($"launcher: {msg}");
                return;
            }

            RaiseLog($"launcher: {msg}");
            var w = new GameLauncherWindow(launcher)
            {
                Owner = Application.Current?.MainWindow,
            };
            w.ShowDialog();
        });
    }

    private void OnLog(string msg) { LogToFile(msg); Post(() => Log?.Invoke(msg)); }

    public void RaiseLog(string msg) { LogToFile(msg); Post(() => Log?.Invoke(msg)); }

    private static readonly object _logLock = new();

    /// <summary>Arquivo de histórico da sessão do TaskHeroX.</summary>
    public static string SessionLogPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskHeroX",
        "session.log");

    /// <summary>
    /// Retorna apenas o final do log para diagnóstico. Falhas de leitura são best-effort e nunca
    /// interferem no trainer. A redação de dados pessoais é aplicada pelo SupportBundle antes do export.
    /// </summary>
    public static IReadOnlyList<string> ReadSessionLogTail(int maxLines = 100)
    {
        if (maxLines <= 0) return [];
        try
        {
            lock (_logLock)
            {
                if (!System.IO.File.Exists(SessionLogPath)) return [];
                return System.IO.File.ReadLines(SessionLogPath).TakeLast(maxLines).ToArray();
            }
        }
        catch
        {
            return [];
        }
    }

    private static void LogToFile(string msg)
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(SessionLogPath)!;
            System.IO.Directory.CreateDirectory(dir);
            lock (_logLock)
                System.IO.File.AppendAllText(SessionLogPath,
                    $"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}");
        }
        catch { }
    }

    private static void Post(Action a)
    {
        var app = Application.Current;
        if (app is null) return;
        if (app.Dispatcher.CheckAccess()) a();
        else app.Dispatcher.BeginInvoke(a);
    }
}
