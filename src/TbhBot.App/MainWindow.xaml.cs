using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TbhBot.App.Services;
using TbhBot.App.Views;
using TbhBot.Core.Update;

namespace TbhBot.App;

public partial class MainWindow : Window
{
    private readonly EngineService _svc = new();
    private readonly Dictionary<RadioButton, (UserControl view, string title)> _tabs = new();

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
