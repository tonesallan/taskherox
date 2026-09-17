using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskHeroX.App.Services;
using TaskHeroX.App.Views;

namespace TaskHeroX.App;

/// <summary>
/// Tamanho inicial compacto + controles completos do chrome customizado.
/// Mantido separado do MainWindow principal para não misturar layout com lógica do trainer.
/// </summary>
public partial class MainWindow
{
    private Button? _maximizeButton;
    private Button? _boostButton;
    private BoostController? _boostController;
    private BoostsWindow? _boostWindow;
    private bool _windowLayoutInitialized;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        ApplyCompactWindowBounds();
        EnsureMaximizeButton();

        _boostController ??= new BoostController(_svc);
        EnsureBoostButton();

        StateChanged -= OnMainWindowStateChanged;
        StateChanged += OnMainWindowStateChanged;
        UpdateMaximizeButton();

        if (!_windowLayoutInitialized)
        {
            _windowLayoutInitialized = true;
            Closed += (_, _) =>
            {
                try { _boostWindow?.Close(); } catch { }
                _boostController?.Dispose();
            };
        }
    }

    private void ApplyCompactWindowBounds()
    {
        if (WindowState != WindowState.Normal) return;

        Rect work = SystemParameters.WorkArea;

        // Em telas 1366x768 o tamanho antigo 1260x900 ocupava praticamente tudo e
        // ultrapassava a altura útil. O novo tamanho continua responsivo em telas menores.
        MinWidth = Math.Min(960, Math.Max(760, work.Width - 80));
        MinHeight = Math.Min(560, Math.Max(480, work.Height - 80));

        Width = Math.Max(MinWidth, Math.Min(1140, work.Width * 0.86));
        Height = Math.Max(MinHeight, Math.Min(650, work.Height * 0.88));

        Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
        Top = work.Top + Math.Max(0, (work.Height - Height) / 2);
    }

    private void EnsureBoostButton()
    {
        if (_boostButton is not null) return;
        if (ResetStatsBtn.Parent is not StackPanel chromeBar) return;

        int compatIndex = -1;
        for (int i = 0; i < chromeBar.Children.Count; i++)
        {
            if (chromeBar.Children[i] is Button b && string.Equals(b.Content?.ToString(), "COMPAT", StringComparison.Ordinal))
            {
                compatIndex = i;
                break;
            }
        }

        _boostButton = new Button
        {
            Content = "BOOSTS",
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Abre o Boost Lab: multiplicadores relativos, XP Gain e presets seguros.",
        };
        _boostButton.Click += (_, _) => OpenBoostLab();

        chromeBar.Children.Insert(compatIndex >= 0 ? compatIndex : 0, _boostButton);
    }

    private void OpenBoostLab()
    {
        if (_boostController is null) return;

        if (_boostWindow is { IsVisible: true })
        {
            _boostWindow.Activate();
            return;
        }

        _boostWindow = new BoostsWindow(_svc, _boostController) { Owner = this };
        _boostWindow.Closed += (_, _) => _boostWindow = null;
        _boostWindow.Show();
    }

    private void EnsureMaximizeButton()
    {
        if (_maximizeButton is not null) return;
        if (ResetStatsBtn.Parent is not StackPanel chromeBar) return;

        int resetIndex = chromeBar.Children.IndexOf(ResetStatsBtn);
        if (resetIndex < 0) return;

        _maximizeButton = new Button
        {
            Style = (Style)FindResource("ChromeButton"),
            Tag = Geometry.Parse("M1,1 H10 V10 H1 Z"),
            ToolTip = "Maximizar",
        };
        _maximizeButton.Click += (_, _) =>
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        };

        // Ordem visual padrão do Windows: minimizar | maximizar/restaurar | fechar.
        // ResetStats é seguido hoje por minimizar e fechar, então inserimos antes do fechar.
        int insertAt = Math.Min(resetIndex + 2, chromeBar.Children.Count);
        chromeBar.Children.Insert(insertAt, _maximizeButton);
    }

    private void OnMainWindowStateChanged(object? sender, EventArgs e) => UpdateMaximizeButton();

    private void UpdateMaximizeButton()
    {
        if (_maximizeButton is null) return;

        bool maximized = WindowState == WindowState.Maximized;
        _maximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
        _maximizeButton.Tag = Geometry.Parse(maximized
            ? "M3,1 H10 V8 H8 M8,3 H1 V10 H8"
            : "M1,1 H10 V10 H1 Z");
    }
}
