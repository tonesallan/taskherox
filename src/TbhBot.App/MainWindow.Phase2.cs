using System.Windows;
using TbhBot.App.Views;

namespace TbhBot.App;

public partial class MainWindow
{
    private void OnOpenCompatibility(object sender, RoutedEventArgs e)
    {
        var w = new CompatibilityWindow(_svc) { Owner = this };

        // O diagnóstico é secundário e deve caber confortavelmente sem ocupar a tela toda.
        // Sobrescreve os mínimos maiores definidos na janela e adapta ao WorkArea do Windows.
        Rect work = SystemParameters.WorkArea;
        w.MinWidth = Math.Min(620, Math.Max(560, work.Width - 120));
        w.MinHeight = Math.Min(480, Math.Max(440, work.Height - 120));
        w.MaxWidth = Math.Max(w.MinWidth, work.Width - 40);
        w.MaxHeight = Math.Max(w.MinHeight, work.Height - 40);
        w.Width = Math.Max(w.MinWidth, Math.Min(720, work.Width * 0.72));
        w.Height = Math.Max(w.MinHeight, Math.Min(600, work.Height * 0.78));

        w.ShowDialog();
    }

    private async void OnRestoreSession(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Restaurar a sessão do TaskHeroX para um estado limpo?\n\n" +
            "Isto vai:\n" +
            "• desligar ACTk, God Mode e todas as automações;\n" +
            "• parar todos os overrides de stats e stage;\n" +
            "• voltar para SAFE MODE;\n" +
            "• restaurar os 25 stats capturados no início desta sessão, se o baseline estiver disponível.\n\n" +
            "Os campos Stage Override deixam de ser forçados, mas o TaskHeroX não reescreve dados de stage antigos porque o estágio atual pode ter mudado.",
            "TaskHeroX · Restore Session",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var engine = _svc.Engine;
        engine.WantActk = false;
        engine.WantGodmode = false;
        engine.WantAutobox = false;
        engine.WantAutostash = false;
        engine.WantAutofuse = false;
        engine.WantAutoboss = false;
        engine.WantEvolve = false;
        engine.WantWatchdog = false;
        engine.WantStats = new Dictionary<string, double>();
        engine.WantStage = new Dictionary<string, int>();
        engine.WdHold = false;

        bool cheatsOk = true;
        if (_svc.IsAttached)
        {
            try
            {
                engine.Cheats.SetActk(false);
                engine.Cheats.SetGodmode(false);
            }
            catch { cheatsOk = false; }
        }

        bool statsRestored = false;
        bool hadBaseline = _svc.IsAttached &&
                           _statBaselinePid == engine.Target.ProcessId &&
                           _statBaseline.Count == 25;

        if (hadBaseline)
        {
            var restore = new Dictionary<string, double>(_statBaseline, StringComparer.Ordinal);
            try { statsRestored = await Task.Run(() => _svc.IsAttached && engine.Stats.ApplyStats(restore)); }
            catch { statsRestored = false; }
        }

        _unsafeStats = false;
        Services.StatSafety.UnsafeMode = false;
        UnsafeStatsBtn.Content = "UNSAFE: OFF";
        UnsafeStatsBtn.Style = null;
        UnsafeStatsBtn.ToolTip = "Desativa temporariamente os limites seguros dos Neural Overrides. Não persiste entre reinicializações.";

        RecreateTrainerView();

        if (!_svc.IsAttached)
        {
            _svc.RaiseLog("RESTORE SESSION: intenções locais limpas · jogo não estava conectado");
            return;
        }

        string statResult = hadBaseline
            ? (statsRestored ? "stats restaurados" : "falha ao restaurar stats")
            : "sem baseline de stats";
        _svc.RaiseLog($"RESTORE SESSION concluído · {statResult} · cheats revertidos={(cheatsOk ? "sim" : "parcial")}");
    }
}
