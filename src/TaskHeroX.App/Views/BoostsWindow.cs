using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TaskHeroX.App.Services;

namespace TaskHeroX.App.Views;

/// <summary>
/// Boost Lab: multiplicadores relativos ao valor real capturado na sessão.
/// Evita substituir stats por números absolutos quando o usuário só quer 25/50% a mais.
/// </summary>
public sealed class BoostsWindow : Window
{
    private readonly EngineService _svc;
    private readonly BoostController _boosts;
    private readonly Dictionary<string, TextBlock> _factorLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _valueLabels = new(StringComparer.Ordinal);
    private TextBlock _xpLabel = null!;
    private TextBlock _xpValueLabel = null!;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _liveTimer;
    private int _liveRefreshBusy;

    public BoostsWindow(EngineService svc, BoostController boosts)
    {
        _svc = svc;
        _boosts = boosts;

        Title = "TaskHeroX · Boost Lab";
        Width = 760;
        Height = 650;
        MinWidth = 660;
        MinHeight = 520;
        MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 40);
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = B("Bg");
        Foreground = B("Fg");
        FontFamily = (FontFamily)Application.Current.FindResource("UI");

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _liveTimer.Tick += (_, _) => RefreshLiveValues();

        var root = new DockPanel { Margin = new Thickness(24) };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = "BOOST LAB",
            Foreground = B("Acc"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
        });
        title.Children.Add(new TextBlock
        {
            Text = "Relative performance multipliers",
            Foreground = Brushes.White,
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 3, 0, 0),
        });
        title.Children.Add(new TextBlock
        {
            Text = "Mostra o valor atual do jogo e atualiza em tempo real quando um boost é aplicado.",
            Foreground = B("Sub"),
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
        });
        header.Children.Add(title);

        var restore = new Button
        {
            Content = "RESTORE BOOSTS",
            Style = (Style)Application.Current.FindResource("Accent.Button"),
            Padding = new Thickness(14, 8, 14, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        restore.Click += async (_, _) =>
        {
            await _boosts.RestoreAllAsync();
            RefreshFactors();
            await RefreshLiveValuesAsync();
            SetStatus("Boosts restaurados para o baseline desta sessão.", true);
        };
        Grid.SetColumn(restore, 1);
        header.Children.Add(restore);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _status = new TextBlock
        {
            Foreground = B("Sub"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 14),
        };
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        var stack = new StackPanel();
        stack.Children.Add(BuildPresetsCard());
        stack.Children.Add(BuildMultipliersCard());
        stack.Children.Add(BuildGameSpeedCard());
        stack.Children.Add(new TextBlock
        {
            Text = "XP Gain usa StatType 47 (IncreaseExpAmount), observado como 1.0 no build validado. AdditionalExp (48) continua intocado até validação separada.",
            Foreground = B("Subtle"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 12),
        });

        root.Children.Add(new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        Content = root;
        Loaded += async (_, _) =>
        {
            _svc.StateChanged += OnStateChanged;
            RefreshFactors();
            RefreshConnection();
            await RefreshLiveValuesAsync();
            _liveTimer.Start();
        };
        Closed += (_, _) =>
        {
            _liveTimer.Stop();
            _svc.StateChanged -= OnStateChanged;
        };
    }

    private Border BuildPresetsCard()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "Presets rápidos",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        });
        body.Children.Add(new TextBlock
        {
            Text = "FARM prioriza velocidade/XP. BOSS SAFE prioriza dano moderado, HP e Armor. Nenhum preset ativa AutoBoss.",
            Foreground = B("Sub"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var farm = new Button { Content = "FARM", Style = (Style)Application.Current.FindResource("Accent.Button"), Margin = new Thickness(0, 0, 8, 0) };
        farm.Click += async (_, _) =>
        {
            bool ok = await _boosts.ApplyFarmPresetAsync();
            RefreshFactors();
            await RefreshLiveValuesAsync();
            SetStatus(ok ? "Preset FARM aplicado." : "Não foi possível aplicar FARM — confirme se o jogo está conectado.", ok);
        };
        row.Children.Add(farm);

        var boss = new Button { Content = "BOSS SAFE", Margin = new Thickness(0, 0, 8, 0) };
        boss.Click += async (_, _) =>
        {
            bool ok = await _boosts.ApplyBossSafePresetAsync();
            RefreshFactors();
            await RefreshLiveValuesAsync();
            SetStatus(ok ? "Preset BOSS SAFE aplicado." : "Não foi possível aplicar BOSS SAFE.", ok);
        };
        row.Children.Add(boss);

        var normal = new Button { Content = "NORMAL / x1" };
        normal.Click += async (_, _) =>
        {
            await _boosts.RestoreAllAsync();
            RefreshFactors();
            await RefreshLiveValuesAsync();
            SetStatus("Todos os multiplicadores voltaram para x1.", true);
        };
        row.Children.Add(normal);
        body.Children.Add(row);

        return Card(body);
    }

    private Border BuildMultipliersCard()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "Multipliers",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        });

        body.Children.Add(FactorRow("Attack Damage", "Dano base do herói.", [1.0, 1.25, 1.5, 2.0]));
        body.Children.Add(FactorRow("Attack Speed", "Velocidade de ataque. Mantém o limite seguro quando SAFE está ativo.", [1.0, 1.15, 1.25, 1.5]));
        body.Children.Add(FactorRow("Movement Speed", "Velocidade de movimento.", [1.0, 1.15, 1.25, 1.5]));
        body.Children.Add(FactorRow("Max Hp", "Vida máxima.", [1.0, 1.25, 1.5, 2.0]));
        body.Children.Add(FactorRow("Armor", "Armadura.", [1.0, 1.25, 1.5, 2.0]));
        body.Children.Add(XpFactorRow());

        return Card(body);
    }

    private Border FactorRow(string stat, string description, double[] factors)
    {
        var grid = BaseFactorGrid(stat, description, out TextBlock current, out TextBlock values, out StackPanel buttons);
        _factorLabels[stat] = current;
        _valueLabels[stat] = values;

        foreach (double factor in factors)
        {
            var b = new Button
            {
                Content = factor == 1.0 ? "x1" : $"x{factor:0.##}",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(4, 0, 0, 0),
            };
            b.Click += async (_, _) =>
            {
                bool ok = await _boosts.SetNamedFactorAsync(stat, factor);
                RefreshFactors();
                await RefreshLiveValuesAsync();
                SetStatus(ok ? $"{stat} ajustado para x{factor:0.##}." : $"Falha ao ajustar {stat}.", ok);
            };
            buttons.Children.Add(b);
        }
        return RowBorder(grid);
    }

    private Border XpFactorRow()
    {
        var grid = BaseFactorGrid("XP Gain", "StatType 47 · IncreaseExpAmount. Não altera AdditionalExp.", out _xpLabel, out _xpValueLabel, out StackPanel buttons);
        foreach (double factor in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var b = new Button
            {
                Content = factor == 1.0 ? "x1" : $"x{factor:0.##}",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(4, 0, 0, 0),
            };
            b.Click += async (_, _) =>
            {
                bool ok = await _boosts.SetXpFactorAsync(factor);
                RefreshFactors();
                await RefreshLiveValuesAsync();
                SetStatus(ok ? $"XP Gain ajustado para x{factor:0.##}." : "Falha ao resolver o StatType 47 nesta sessão.", ok);
            };
            buttons.Children.Add(b);
        }
        return RowBorder(grid);
    }

    private Grid BaseFactorGrid(string title, string description, out TextBlock current, out TextBlock values, out StackPanel buttons)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = title, Foreground = B("Fg"), FontSize = 13, FontWeight = FontWeights.SemiBold });
        current = new TextBlock
        {
            Text = "x1",
            Foreground = B("Acc"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 11,
            Margin = new Thickness(9, 1, 0, 0),
        };
        titleRow.Children.Add(current);
        left.Children.Add(titleRow);
        left.Children.Add(new TextBlock { Text = description, Foreground = B("Subtle"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 12, 0) });
        values = new TextBlock
        {
            Text = "Atual: --  ·  Base: --  ·  Alvo: --",
            Foreground = B("Sub"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 10,
            Margin = new Thickness(0, 5, 12, 0),
        };
        left.Children.Add(values);
        grid.Children.Add(left);

        buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    private Border BuildGameSpeedCard()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "Game Speed", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock
        {
            Text = "PENDING · a rota Unity timeScale ainda não foi resolvida de forma confiável para este build. O TaskHeroX não vai escrever um endereço estimado apenas para liberar este recurso.",
            Foreground = B("Amber"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });
        return Card(body);
    }

    private void RefreshFactors()
    {
        foreach (var (stat, label) in _factorLabels)
            label.Text = $"x{_boosts.GetNamedFactor(stat):0.##}";
        if (_xpLabel is not null) _xpLabel.Text = $"x{_boosts.XpFactor:0.##}";
    }

    private async void RefreshLiveValues() => await RefreshLiveValuesAsync();

    private async Task RefreshLiveValuesAsync()
    {
        if (Interlocked.Exchange(ref _liveRefreshBusy, 1) != 0) return;
        try
        {
            if (!_svc.IsAttached)
            {
                foreach (var label in _valueLabels.Values)
                    label.Text = "Atual: --  ·  Base: --  ·  Alvo: --";
                if (_xpValueLabel is not null)
                    _xpValueLabel.Text = "Atual: --  ·  Base: --  ·  Alvo: --";
                return;
            }

            var live = await Task.Run(() => _boosts.ReadCurrentValues());

            foreach (var (stat, label) in _valueLabels)
            {
                live.Named.TryGetValue(stat, out double currentValue);
                double? current = live.Named.ContainsKey(stat) ? currentValue : null;
                var state = _boosts.GetNamedState(stat);
                double? baseline = state.Baseline ?? current;
                double? target = state.Target ?? current;
                label.Text = $"Atual: {Fmt(current)}  ·  Base: {Fmt(baseline)}  ·  Alvo: {Fmt(target)}";
            }

            if (_xpValueLabel is not null)
            {
                var state = _boosts.GetXpState();
                double? baseline = state.Baseline ?? live.Xp;
                double? target = state.Target ?? live.Xp;
                _xpValueLabel.Text = $"Atual: {Fmt(live.Xp)}  ·  Base: {Fmt(baseline)}  ·  Alvo: {Fmt(target)}";
            }
        }
        catch { }
        finally
        {
            Volatile.Write(ref _liveRefreshBusy, 0);
        }
    }

    private static string Fmt(double? value)
        => value is double v && double.IsFinite(v)
            ? v.ToString("0.###", CultureInfo.CurrentCulture)
            : "--";

    private void OnStateChanged()
    {
        RefreshConnection();
        RefreshFactors();
        RefreshLiveValues();
    }

    private void RefreshConnection()
        => SetStatus(_svc.IsAttached ? "GAME DETECTED · boosts disponíveis" : "OFFLINE · abra o Taskbar Hero", _svc.IsAttached);

    private void SetStatus(string text, bool ok)
    {
        _status.Text = text;
        _status.Foreground = ok ? B("Acc") : B("Red");
    }

    private static Border RowBorder(UIElement child) => new()
    {
        Background = B("Card2"),
        BorderBrush = B("Stroke"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 10, 14, 10),
        Margin = new Thickness(0, 0, 0, 8),
        Child = child,
    };

    private static Border Card(UIElement child) => new()
    {
        Style = (Style)Application.Current.FindResource("Card.Border"),
        Child = child,
        Margin = new Thickness(0, 0, 0, 14),
    };

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
}
