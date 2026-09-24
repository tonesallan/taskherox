using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskHeroX.App.Services;
using TaskHeroX.App.Views;
using TaskHeroX.Core.Update;

namespace TaskHeroX.App;

public partial class MainWindow : Window
{
    private readonly EngineService _svc = new();
    private readonly Dictionary<RadioButton, (UserControl view, string title)> _tabs = new();

    // Snapshot VOLÁTIL dos stats no começo de cada processo do jogo.
    // Serve para o botão RESET STATS desfazer testes extremos/overrides sem depender de valores hardcoded.
    private Dictionary<string, double> _statBaseline = new();
    private int _statBaselinePid;
    private int _statBaselineCapturePid;
    private bool _clampingStatText;

    public MainWindow()
    {
        InitializeComponent();

        _svc.StateChanged += UpdateConn;
        _svc.Log += m => StatusMini.Text = m;

        Register(NavTrainer,   new TrainerView(_svc),   "Dashboard");
        Register(NavInventory, new InventoryView(_svc), "Inventory");
        Register(NavMarket,    new MarketView(_svc),    "Market Intel");
        Register(NavRunes,     new RunesView(_svc),     "Runes");
        Register(NavStages,    new StagesView(_svc),    "Stage Navigator");

        // Guardrails globais dos campos de stats. O PreviewTextInput impede digitação fora do limite
        // ANTES do TextChanged do TrainerView poder escrever na memória; o handler de paste faz o mesmo
        // para Ctrl+V. TextChanged fica como rede de segurança para profiles/alterações programáticas.
        AddHandler(TextBox.PreviewTextInputEvent, new TextCompositionEventHandler(OnStatPreviewTextInput), true);
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(OnStatTextChanged), true);
        DataObject.AddPastingHandler(this, OnStatPaste);

        Show(NavTrainer);
        _svc.Start();
        _svc.RaiseLog($"TaskHeroX iniciado · perfis: {ProfileStore.ExeDir}");
        UpdateConn();
        Closed += (_, _) => _svc.Stop();
        _ = CheckUpdateAsync();
    }

    // ══════════════════ TASKHEROX AUTO-UPDATE ══════════════════
    private readonly AutoUpdate _upd = new();
    private string _updUrl = "", _updTag = "";
    private bool _updBusy;

    private async Task CheckUpdateAsync()
    {
        var (available, tag, url) = await _upd.CheckAsync(AutoUpdate.CurrentVersion);
        if (!available) return;
        _updUrl = url; _updTag = tag;
        RefreshBanner();
    }

    private void RefreshBanner()
    {
        if (_updBusy) return;
        bool unknownBuild = _svc.IsAttached && !_svc.Engine.OffsetsLoaded;
        bool hasUpdate = _updUrl.Length > 0;
        if (!unknownBuild && !hasUpdate) { UpdateBanner.Visibility = Visibility.Collapsed; return; }

        string build = _svc.Engine.BuildHash is { Length: >= 7 } h ? h[..7] : "?";
        if (unknownBuild)
        {
            UpdateTitle.Text = "Build do jogo ainda não validada pelo TaskHeroX";
            UpdateDesc.Text = hasUpdate
                ? $"A {_updTag} pode incluir offsets para o build {build}. Recursos dependentes de offsets ficam limitados até a validação."
                : $"Build {build} desconhecido. O TaskHeroX manterá apenas recursos que conseguirem se validar com segurança.";
        }
        else
        {
            UpdateTitle.Text = $"TaskHeroX {_updTag} disponível";
            UpdateDesc.Text  = $"Versão atual: {AutoUpdate.CurrentVersion}. Atualizações podem incluir compatibilidade com novos builds do jogo.";
        }
        UpdateBtn.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        if (_updBusy || _updUrl.Length == 0) return;
        _updBusy = true;
        UpdateBtn.IsEnabled = false;
        try
        {
            var prog = new Progress<double>(p => UpdateBtn.Content = $"DOWNLOADING {p * 100:0}%");
            var (newExe, exe, dir) = await _upd.DownloadAndStageAsync(_updUrl, prog);
            UpdateBtn.Content = "RESTARTING...";
            _upd.LaunchUpdater(newExe, exe, dir);
            Close();
        }
        catch (Exception ex)
        {
            UpdateBtn.Content = "UPDATE TASKHEROX";
            UpdateBtn.IsEnabled = true;
            _updBusy = false;
            UpdateDesc.Text = $"Falhou: {ex.Message} — baixe manualmente em github.com/{AutoUpdate.Repo}/releases";
        }
    }

    private void Register(RadioButton nav, UserControl view, string title)
    {
        view.Visibility = Visibility.Collapsed;
        ContentHost.Children.Add(view);
        _tabs[nav] = (view, title);
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb) Show(rb);
    }

    private void Show(RadioButton nav)
    {
        foreach (var (r, (view, title)) in _tabs)
        {
            bool on = r == nav;
            view.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) PageTitle.Text = title;
        }
    }

    private void UpdateConn()
    {
        bool ok = _svc.IsAttached;
        Particles.Connected = ok;
        ResetStatsBtn.IsEnabled = ok;
        var acc = (Brush)FindResource("Acc");
        var red = (Brush)FindResource("Red");
        if (ok)
        {
            var e = _svc.Engine;
            string build = e.BuildHash is { Length: >= 7 } h ? h[..7] : "?";
            ConnLabel.Text = "GAME DETECTED";
            ConnLabel.Foreground = acc;
            ConnDot.Fill = acc;
            ConnDotGlow.Color = Color.FromRgb(0xFF, 0x7A, 0x18);
            ConnDetails.Text = $"BUILD {build}  ·  OFFSETS {(e.OffsetsLoaded ? "READY" : "AOB ONLY")}";
            LaunchBtn.Content = "ENGINE ATTACHED";
            LaunchBtn.IsEnabled = false;
            EnsureStatBaseline();
        }
        else
        {
            ConnLabel.Text = "OFFLINE";
            ConnLabel.Foreground = red;
            ConnDot.Fill = red;
            ConnDotGlow.Color = Color.FromRgb(0xFF, 0x2A, 0x55);
            ConnDetails.Text = "Taskbar Hero não detectado";
            LaunchBtn.Content = "LAUNCH GAME";
            LaunchBtn.IsEnabled = true;
        }
        RefreshBanner();
    }

    // Captura os valores reais assim que um NOVO processo do jogo é detectado, antes de o usuário
    // começar a testar overrides. O snapshot é descartado automaticamente quando o PID muda.
    private void EnsureStatBaseline()
    {
        if (!_svc.IsAttached) return;
        int pid = _svc.Engine.Target.ProcessId;
        if (_statBaselinePid == pid || _statBaselineCapturePid == pid) return;
        _statBaselineCapturePid = pid;
        _ = CaptureStatBaselineAsync(pid);
    }

    private async Task CaptureStatBaselineAsync(int pid)
    {
        try
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(attempt == 0 ? 120 : 350);
                if (!_svc.IsAttached || _svc.Engine.Target.ProcessId != pid) return;

                Dictionary<string, double> stats;
                try { stats = await Task.Run(() => _svc.Engine.Stats.ReadStats()); }
                catch { stats = new(); }

                if (stats.Count == 25)
                {
                    _statBaseline = new Dictionary<string, double>(stats, StringComparer.Ordinal);
                    _statBaselinePid = pid;
                    _svc.RaiseLog($"baseline de stats capturado · PID {pid} · 25/25");
                    return;
                }
            }
            _svc.RaiseLog("não consegui capturar o baseline de stats desta sessão");
        }
        finally
        {
            if (_statBaselineCapturePid == pid) _statBaselineCapturePid = 0;
        }
    }

    private async void OnResetStats(object sender, RoutedEventArgs e)
    {
        if (!_svc.IsAttached) return;
        int pid = _svc.Engine.Target.ProcessId;

        if (_statBaselinePid != pid || _statBaseline.Count != 25)
        {
            MessageBox.Show(
                "O TaskHeroX ainda não possui um snapshot confiável dos stats originais desta sessão.\n\n" +
                "Reinicie o jogo e aguarde o TaskHeroX conectar antes de aplicar novos overrides.",
                "TaskHeroX · Reset Stats", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                "Restaurar os 25 stats para os valores capturados quando esta sessão do jogo foi conectada?\n\n" +
                "Todos os Neural Overrides de stats serão desligados.",
                "TaskHeroX · Reset Stats", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        ResetStatsBtn.IsEnabled = false;
        _svc.Engine.WantStats = new Dictionary<string, double>();
        var restore = new Dictionary<string, double>(_statBaseline, StringComparer.Ordinal);

        bool ok = false;
        try { ok = await Task.Run(() => _svc.IsAttached && _svc.Engine.Stats.ApplyStats(restore)); }
        catch { ok = false; }

        if (ok)
        {
            RecreateTrainerView();
            _svc.RaiseLog("RESET STATS concluído · 25 stats restaurados e overrides desligados");
        }
        else
        {
            _svc.RaiseLog("RESET STATS falhou · não consegui escrever o baseline no jogo");
        }
        ResetStatsBtn.IsEnabled = _svc.IsAttached;
    }

    // Recria só a aba Dashboard para limpar visualmente os botões Forced e os campos de override.
    private void RecreateTrainerView()
    {
        if (!_tabs.TryGetValue(NavTrainer, out var old)) return;
        bool visible = old.view.Visibility == Visibility.Visible;
        ContentHost.Children.Remove(old.view);
        var fresh = new TrainerView(_svc) { Visibility = visible ? Visibility.Visible : Visibility.Collapsed };
        ContentHost.Children.Add(fresh);
        _tabs[NavTrainer] = (fresh, "Dashboard");
    }

    // ══════════════════ SAFE STAT INPUT ══════════════════

    private void OnStatPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.OriginalSource is not TextBox tb || !TryStatForTextBox(tb, out string stat, out var limit)) return;
        string proposed = ProposedText(tb, e.Text);
        if (!TryParsedOutOfRange(proposed, limit, out double value)) return;

        e.Handled = true;
        _svc.RaiseLog($"{stat}: {value:g6} bloqueado · limite seguro máximo {limit.DisplayMax}");
    }

    private void OnStatPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.OriginalSource is not TextBox tb || !TryStatForTextBox(tb, out string stat, out var limit)) return;
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)) return;
        string pasted = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
        string proposed = ProposedText(tb, pasted);
        if (!TryParsedOutOfRange(proposed, limit, out double value)) return;

        e.CancelCommand();
        _svc.RaiseLog($"{stat}: colagem {value:g6} bloqueada · limite seguro máximo {limit.DisplayMax}");
    }

    private void OnStatTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_clampingStatText || e.OriginalSource is not TextBox tb ||
            !TryStatForTextBox(tb, out string stat, out var limit)) return;

        tb.ToolTip ??= StatSafety.Tooltip(stat);
        if (!double.TryParse(tb.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            !double.IsFinite(value)) return;
        if (value >= limit.Min && value <= limit.Max) return;

        double clamped = Math.Clamp(value, limit.Min, limit.Max);
        try
        {
            _clampingStatText = true;
            tb.Text = clamped.ToString("0.###", CultureInfo.InvariantCulture);
            tb.CaretIndex = tb.Text.Length;
        }
        finally { _clampingStatText = false; }
        _svc.RaiseLog($"{stat}: valor ajustado para {clamped:g6} pelo limite seguro do TaskHeroX");
    }

    private static bool TryParsedOutOfRange(string raw, StatSafety.Limit limit, out double value)
    {
        if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            !double.IsFinite(value)) return false; // permite estados intermediários como "1e" durante digitação
        return value < limit.Min || value > limit.Max;
    }

    private static string ProposedText(TextBox tb, string inserted)
    {
        int start = Math.Clamp(tb.SelectionStart, 0, tb.Text.Length);
        int len = Math.Clamp(tb.SelectionLength, 0, tb.Text.Length - start);
        return tb.Text.Remove(start, len).Insert(start, inserted);
    }

    private static bool TryStatForTextBox(TextBox tb, out string stat, out StatSafety.Limit limit)
    {
        stat = string.Empty;
        limit = default;
        DependencyObject? cur = tb;
        for (int depth = 0; cur is not null && depth < 14; depth++)
        {
            if (cur is Border b && FindStatLabel(b) is string found && StatSafety.TryGet(found, out limit))
            {
                stat = found;
                return true;
            }
            cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
        }
        return false;
    }

    private static string? FindStatLabel(DependencyObject root)
    {
        int n;
        try { n = VisualTreeHelper.GetChildrenCount(root); }
        catch { return null; }
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock t && StatSafety.TryGet(t.Text, out _)) return t.Text;
            var nested = FindStatLabel(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void OnLaunchGame(object sender, RoutedEventArgs e) => _svc.LaunchGame();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) is not null) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d is not null) { if (d is T t) return t; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d); }
        return null;
    }
}
