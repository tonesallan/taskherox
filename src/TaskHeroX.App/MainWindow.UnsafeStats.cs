using System.Windows;
using TaskHeroX.App.Services;

namespace TaskHeroX.App;

public partial class MainWindow
{
    private bool _unsafeStats;

    private void OnUnsafeStats(object sender, RoutedEventArgs e)
    {
        if (!_unsafeStats)
        {
            var confirm = MessageBox.Show(
                "Ativar UNSAFE MODE para os stats?\n\n" +
                "Isso remove os limites seguros do TaskHeroX para os Neural Overrides. " +
                "Valores extremos podem causar comportamento incorreto, travamentos ou deixar a sessão instável.\n\n" +
                "O modo não fica salvo: ao reiniciar o TaskHeroX ele volta para SAFE MODE.\n\n" +
                "Continuar?",
                "TaskHeroX · Unsafe Stats",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        _unsafeStats = !_unsafeStats;
        StatSafety.UnsafeMode = _unsafeStats;

        UnsafeStatsBtn.Content = _unsafeStats ? "UNSAFE: ON" : "UNSAFE: OFF";
        UnsafeStatsBtn.Style = _unsafeStats ? (Style)FindResource("Accent.Button") : null;
        UnsafeStatsBtn.ToolTip = _unsafeStats
            ? "UNSAFE MODE ativo: limites seguros dos stats estão desativados. Use RESET STATS para restaurar a sessão."
            : "Desativa temporariamente os limites seguros dos Neural Overrides. Não persiste entre reinicializações.";

        if (_unsafeStats)
        {
            _svc.RaiseLog("UNSAFE MODE ativo · limites seguros dos stats desativados");
        }
        else
        {
            _svc.RaiseLog("SAFE MODE reativado · limites seguros voltaram; valores já aplicados não são restaurados automaticamente");
        }
    }
}
