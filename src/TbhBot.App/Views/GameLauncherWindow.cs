using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TbhBot.App.Services;

namespace TbhBot.App.Views;

/// <summary>
/// Janela de localização/inicialização do jogo inspirada no fluxo do WeMod:
/// tenta descobrir a instalação, permite selecionar EXE/atalho e oferece Steam como fallback.
/// </summary>
public sealed class GameLauncherWindow : Window
{
    private readonly GameLauncherService _launcher;
    private readonly TextBlock _target;
    private readonly TextBlock _status;
    private readonly Button _play;

    public GameLauncherWindow(GameLauncherService launcher)
    {
        _launcher = launcher;

        Title = "TaskHeroX · Game Launcher";
        Width = 680;
        Height = 520;
        MinWidth = 600;
        MinHeight = 460;
        MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 40);
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = B("Bg");
        Foreground = B("Fg");
        FontFamily = (FontFamily)Application.Current.FindResource("UI");

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock
        {
            Text = "GAME LAUNCHER",
            Foreground = B("Acc"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
        });
        header.Children.Add(new TextBlock
        {
            Text = "Localizar e iniciar Taskbar Hero",
            Foreground = Brushes.White,
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 3, 0, 0),
        });
        header.Children.Add(new TextBlock
        {
            Text = "O TaskHeroX procura a instalação da Steam automaticamente. Se não encontrar, você pode apontar o executável ou um atalho.",
            Foreground = B("Sub"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });
        root.Children.Add(header);

        _status = new TextBlock
        {
            Foreground = B("Sub"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 14),
        };
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);

        var stack = new StackPanel();

        var detectedBody = new StackPanel();
        detectedBody.Children.Add(new TextBlock
        {
            Text = "Instalação detectada",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        });
        _target = new TextBlock
        {
            Text = "--",
            Foreground = B("Sub"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12),
        };
        detectedBody.Children.Add(_target);

        var detectedButtons = new StackPanel { Orientation = Orientation.Horizontal };
        _play = new Button
        {
            Content = "JOGAR AGORA",
            Style = (Style)Application.Current.FindResource("Accent.Button"),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _play.Click += (_, _) => PlayPreferred();
        detectedButtons.Children.Add(_play);

        var forget = new Button { Content = "ESQUECER CAMINHO" };
        forget.Click += (_, _) =>
        {
            _launcher.ForgetTarget();
            RefreshTarget();
            SetStatus("Caminho salvo removido. A próxima busca tentará a Steam novamente.", true);
        };
        detectedButtons.Children.Add(forget);
        detectedBody.Children.Add(detectedButtons);
        stack.Children.Add(Card(detectedBody));

        var locateBody = new StackPanel();
        locateBody.Children.Add(new TextBlock
        {
            Text = "Não encontrou automaticamente?",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        });
        locateBody.Children.Add(new TextBlock
        {
            Text = "Selecione TaskBarHero.exe ou um atalho .lnk/.url que abra o jogo. O TaskHeroX salva somente esse caminho local.",
            Foreground = B("Sub"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 12),
        });
        var choose = new Button { Content = "SELECIONAR EXECUTÁVEL / ATALHO", HorizontalAlignment = HorizontalAlignment.Left };
        choose.Click += (_, _) => ChooseTarget();
        locateBody.Children.Add(choose);
        stack.Children.Add(Card(locateBody));

        var steamBody = new StackPanel();
        steamBody.Children.Add(new TextBlock
        {
            Text = "Steam",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        });
        steamBody.Children.Add(new TextBlock
        {
            Text = "Use estes atalhos se o jogo ainda não estiver instalado ou se você preferir abrir diretamente pela Steam.",
            Foreground = B("Sub"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 12),
        });
        var steamRow = new WrapPanel();
        var runSteam = new Button { Content = "ABRIR PELA STEAM", Margin = new Thickness(0, 0, 8, 8) };
        runSteam.Click += (_, _) =>
        {
            bool ok = _launcher.TryLaunchSteam(out string msg);
            SetStatus(msg, ok);
            if (ok) Close();
        };
        steamRow.Children.Add(runSteam);

        var install = new Button { Content = "INSTALAR PELA STEAM", Margin = new Thickness(0, 0, 8, 8) };
        install.Click += (_, _) =>
        {
            bool ok = _launcher.TryInstallFromSteam(out string msg);
            SetStatus(msg, ok);
        };
        steamRow.Children.Add(install);

        var store = new Button { Content = "PÁGINA NA STEAM", Margin = new Thickness(0, 0, 8, 8) };
        store.Click += (_, _) =>
        {
            bool ok = _launcher.TryOpenSteamStore(out string msg);
            SetStatus(msg, ok);
        };
        steamRow.Children.Add(store);
        steamBody.Children.Add(steamRow);
        stack.Children.Add(Card(steamBody));

        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        Content = root;
        Loaded += (_, _) => RefreshTarget();
    }

    private void RefreshTarget()
    {
        string? target = _launcher.FindPreferredTarget();
        bool found = !string.IsNullOrWhiteSpace(target);
        _target.Text = found ? target : "Nenhuma instalação ou atalho configurado.";
        _target.Foreground = found ? B("Acc") : B("Sub");
        _play.IsEnabled = found;
        SetStatus(found ? "READY · instalação localizada" : "GAME NOT FOUND · escolha um caminho ou use a Steam", found);
    }

    private void PlayPreferred()
    {
        bool ok = _launcher.TryLaunchInstalled(out string msg);
        SetStatus(msg, ok);
        if (ok) Close();
    }

    private void ChooseTarget()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecione Taskbar Hero ou um atalho",
            Filter = "Executável ou atalho|*.exe;*.lnk;*.url|Executável (*.exe)|*.exe|Atalho do Windows (*.lnk)|*.lnk|Atalho da Internet (*.url)|*.url|Todos os arquivos|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dlg.ShowDialog(this) != true) return;

        string ext = System.IO.Path.GetExtension(dlg.FileName);
        if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Selecione um arquivo .exe, .lnk ou .url.", false);
            return;
        }

        _launcher.SaveTarget(dlg.FileName);
        RefreshTarget();
        SetStatus("Caminho salvo. Use JOGAR AGORA para testar.", true);
    }

    private void SetStatus(string text, bool ok)
    {
        _status.Text = text;
        _status.Foreground = ok ? B("Acc") : B("Red");
    }

    private static Border Card(UIElement child) => new()
    {
        Style = (Style)Application.Current.FindResource("Card.Border"),
        Child = child,
        Margin = new Thickness(0, 0, 0, 14),
    };

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
}
