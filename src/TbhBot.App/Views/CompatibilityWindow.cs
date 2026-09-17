using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TbhBot.App.Services;
using TaskHeroX.Core.Diagnostics;

namespace TbhBot.App.Views;

/// <summary>
/// Compatibility Center: diagnóstico somente-leitura do build atual.
/// Não instala patches, não altera stats/save e não chama automações.
/// </summary>
public sealed class CompatibilityWindow : Window
{
    private readonly EngineService _svc;
    private readonly Dictionary<string, TextBlock> _values = new(StringComparer.Ordinal);
    private readonly Button _refresh;
    private readonly Button _export;
    private readonly TextBlock _summary;

    public CompatibilityWindow(EngineService svc)
    {
        _svc = svc;
        Title = "TaskHeroX · Compatibility Center";
        Width = 780;
        Height = 760;
        MinWidth = 680;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("Bg");
        Foreground = B("Fg");
        FontFamily = (FontFamily)Application.Current.FindResource("UI");

        var root = new DockPanel { Margin = new Thickness(28) };

        var head = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = "COMPATIBILITY CENTER",
            Foreground = B("Acc"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
        });
        title.Children.Add(new TextBlock
        {
            Text = "Build & semantic diagnostics",
            Foreground = Brushes.White,
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 3, 0, 0),
        });
        Grid.SetColumn(title, 0);
        head.Children.Add(title);

        _export = new Button
        {
            Content = "EXPORT BUNDLE",
            Style = (Style)Application.Current.FindResource("Accent.Button"),
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _export.Click += async (_, _) => await ExportSupportBundleAsync();
        Grid.SetColumn(_export, 1);
        head.Children.Add(_export);

        _refresh = new Button
        {
            Content = "REFRESH",
            Style = (Style)Application.Current.FindResource("Accent.Button"),
            Padding = new Thickness(16, 8, 16, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _refresh.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(_refresh, 2);
        head.Children.Add(_refresh);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        _summary = new TextBlock
        {
            Text = "Aguardando diagnóstico...",
            Foreground = B("Sub"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);

        var stack = new StackPanel();
        stack.Children.Add(Card("Runtime", new[]
        {
            ("Process", "process"),
            ("Build hash", "build"),
            ("GameAssembly", "module"),
            ("Offsets", "offsets"),
        }));
        stack.Children.Add(Card("Critical symbols", new[]
        {
            ("Core offset set", "symbols"),
            ("Stage entry / type-1", "jgc"),
        }));
        stack.Children.Add(Card("Semantic reads", new[]
        {
            ("Stats route", "stats"),
            ("Stage route", "stage"),
            ("Stage progress", "progress"),
            ("Runes", "runes"),
            ("Inventory", "inventory"),
            ("PlayerSaveData", "psd"),
        }));
        stack.Children.Add(Card("Feature gate", new[]
        {
            ("Memory editor", "gate-editor"),
            ("Save reads", "gate-save"),
            ("AutoBoss / Evolution", "gate-stage-entry"),
        }));

        var note = new TextBlock
        {
            Text = "Compatibility Center é somente-leitura. EXPORT BUNDLE cria um JSON de suporte com schema versionado e remove caminhos de usuário, Steam IDs e e-mails do log antes de salvar.",
            Foreground = B("Subtle"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 2, 2, 12),
        };
        stack.Children.Add(note);

        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack,
        });

        Content = root;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private Border Card(string title, IEnumerable<(string Label, string Key)> rows)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Foreground = B("Acc"),
            FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        foreach (var (label, key) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var l = new TextBlock
            {
                Text = label,
                Foreground = B("Sub"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(l, 0);
            grid.Children.Add(l);

            var v = new TextBlock
            {
                Text = "—",
                Foreground = B("Fg"),
                FontFamily = (FontFamily)Application.Current.FindResource("Mono"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            };
            _values[key] = v;
            Grid.SetColumn(v, 1);
            grid.Children.Add(v);
            stack.Children.Add(grid);
        }

        return new Border
        {
            Style = (Style)Application.Current.FindResource("Card.Border"),
            Child = stack,
            Margin = new Thickness(0, 0, 0, 14),
        };
    }

    private async Task RefreshAsync()
    {
        _refresh.IsEnabled = false;
        try
        {
            if (!_svc.IsAttached)
            {
                Set("process", "OFFLINE", false);
                Set("build", "—", null);
                Set("module", "—", null);
                Set("offsets", "NOT LOADED", false);
                Set("symbols", "jogo não conectado", false);
                Set("jgc", "não testado", null);
                Set("stats", "não testado", null);
                Set("stage", "não testado", null);
                Set("progress", "não testado", null);
                Set("runes", "não testado", null);
                Set("inventory", "não testado", null);
                Set("psd", "não testado", null);
                Set("gate-editor", "BLOCKED", false);
                Set("gate-save", "BLOCKED", false);
                Set("gate-stage-entry", "BLOCKED", false);
                _summary.Text = "Abra o Taskbar Hero e aguarde o TaskHeroX conectar para executar o diagnóstico. O Support Bundle ainda pode ser exportado offline.";
                return;
            }

            var e = _svc.Engine;
            string[] critical = ["gra", "uo_ti", "uo_max", "uo_cur", "uo_wave", "bal_ti", "stage_off"];
            string[] missing = critical.Where(k => !e.Symbols.Has(k)).ToArray();
            bool symbolsOk = missing.Length == 0;
            bool stageEntryType1 = e.StageNav.CanValidateType1Entry;
            bool splitType1 = e.StageNav.UsesSplitType1Validator;

            Set("process", $"ATTACHED · PID {e.Target.ProcessId}", true);
            Set("build", e.BuildHash ?? "unknown", e.BuildHash is { Length: > 0 });
            Set("module", $"0x{e.Target.ModuleBase:X}", e.Target.ModuleBase != 0);
            Set("offsets", e.OffsetsLoaded ? "READY" : "AOB / PARTIAL", e.OffsetsLoaded);
            Set("symbols", symbolsOk ? $"READY · {critical.Length}/{critical.Length}" : $"MISSING: {string.Join(", ", missing)}", symbolsOk);
            Set("jgc", stageEntryType1
                ? (splitType1 ? "VALIDATED · split type=1" : "RESOLVED · legacy jgc")
                : "UNRESOLVED · type=1 validator unavailable", stageEntryType1 ? true : null);

            var diag = await Task.Run(() => RunReadDiagnostics(e));

            Set("stats", $"{diag.Stats}/25", diag.Stats == 25);
            Set("stage", $"{diag.Stage}/18", diag.Stage == 18);
            Set("progress", diag.ProgressText, diag.ProgressOk);
            Set("runes", $"{diag.Runes} definitions/save entries", diag.Runes > 0);
            Set("inventory", $"{diag.Inventory} items", diag.Inventory >= 0);
            Set("psd", diag.Psd ? "RESOLVED" : "FAILED", diag.Psd);

            bool editor = diag.Stats == 25 && diag.Stage == 18;
            bool save = diag.ProgressOk && diag.Runes > 0 && diag.Inventory >= 0 && diag.Psd;
            Set("gate-editor", editor ? "READY" : "DEGRADED", editor);
            Set("gate-save", save ? "READY" : "DEGRADED", save);
            Set("gate-stage-entry", stageEntryType1 ? "READY · type=1 validated" : "BLOCKED · type=1 unresolved", stageEntryType1);

            int okCount = new[] { e.OffsetsLoaded, symbolsOk, editor, save }.Count(x => x);
            _summary.Text = stageEntryType1
                ? $"Core compatibility: {okCount}/4 · AutoBoss/Evolution type-1 gate ready{(splitType1 ? " via validated split route" : "")}."
                : $"Core compatibility: {okCount}/4 · editor/save routes can work, but AutoBoss/Evolution remain a separate unresolved gate.";
        }
        catch (Exception ex)
        {
            _summary.Text = $"Falha no diagnóstico: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            _refresh.IsEnabled = true;
        }
    }

    private async Task ExportSupportBundleAsync()
    {
        _export.IsEnabled = false;
        try
        {
            DateTimeOffset createdUtc = DateTimeOffset.UtcNow;
            string version = typeof(CompatibilityWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            IReadOnlyList<string> logTail = EngineService.ReadSessionLogTail();

            var bundle = await Task.Run(() => SupportBundleCollector.Collect(
                _svc.Engine,
                version,
                logTail,
                createdUtc));
            string json = SupportBundleSerializer.Serialize(bundle);

            var dialog = new SaveFileDialog
            {
                Title = "Export TaskHeroX Support Bundle",
                FileName = $"TaskHeroX-support-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                DefaultExt = ".json",
                AddExtension = true,
                Filter = "TaskHeroX support bundle (*.json)|*.json|JSON (*.json)|*.json",
            };

            if (dialog.ShowDialog(this) != true) return;

            await File.WriteAllTextAsync(dialog.FileName, json, new UTF8Encoding(false));
            string name = Path.GetFileName(dialog.FileName);
            _summary.Text = $"Support Bundle exportado: {name}";
            _svc.RaiseLog($"support bundle exportado ({name})");
        }
        catch (Exception ex)
        {
            _summary.Text = $"Falha ao exportar Support Bundle: {ex.GetType().Name}: {ex.Message}";
            _svc.RaiseLog($"support bundle falhou: {ex.GetType().Name}");
        }
        finally
        {
            _export.IsEnabled = true;
        }
    }

    private static ReadDiag RunReadDiagnostics(TaskHeroX.Core.Engine e)
    {
        int stats = 0, stage = 0, runes = 0, inventory = -1;
        bool progressOk = false, psd = false;
        string progressText = "FAILED";

        try { stats = e.Stats.ReadStats().Count; } catch { }
        try { stage = e.Stats.ReadStage().Count; } catch { }
        try
        {
            var (max, cur, wave) = e.Save.StageProgress();
            progressOk = max is >= 1101 and <= 4310 && cur > 0 && wave >= 0;
            progressText = $"max={max} · cur={cur} · wave={wave}";
        }
        catch { }
        try { runes = e.Save.ReadRunes().Count; } catch { }
        try { inventory = e.Save.InventoryCount(); } catch { }
        try { psd = e.Resolver.ResolvePsd() != 0; } catch { }

        return new ReadDiag(stats, stage, runes, inventory, progressOk, progressText, psd);
    }

    private readonly record struct ReadDiag(
        int Stats,
        int Stage,
        int Runes,
        int Inventory,
        bool ProgressOk,
        string ProgressText,
        bool Psd);

    private void Set(string key, string text, bool? ok)
    {
        if (!_values.TryGetValue(key, out var v)) return;
        v.Text = text;
        v.Foreground = ok switch
        {
            true => B("Acc"),
            false => B("Red"),
            _ => B("Amber"),
        };
    }

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
}
