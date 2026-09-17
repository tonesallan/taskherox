using TaskHeroX.Core;

namespace TaskHeroX.Core.Automation;

/// <summary>
/// AutomationLoop: UM unico loop de automacao. Portado do racional do _auto_loop (tbh_core.py ~1922):
/// "UM SO loop numa thread e UM dispatcher" — as duas threads antigas disputavam o dispatcher e faziam
/// o auto-box passar fome (starvation). Aqui cada tick:
///   1) aplica protecao/cheats conforme as flags de intencao do engine (ACTk/Godmode/Hitkill);
///   2) SE alguma automacao de acao estiver ligada (Autobox/Autostash/Autofuse), executa via
///      engine.Dispatcher na ordem de PRIORIDADE box -> stash -> fuse.
///
/// IMPORTANTE: as ACOES reais (abrir caixa, mover pro bau, fundir no cubo) dependem da Fase 3 —
/// o IMainThreadDispatcher e implementado pelo orquestrador. Enquanto Dispatcher.IsReady==false,
/// apenas logamos "dispatcher nao pronto" e seguimos (nao trava, não lança).
/// Tudo async e cancelavel (nada de Thread.Sleep; sempre await Task.Delay(.., ct)).
/// </summary>
public sealed class AutomationLoop(Engine engine)
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    public event Action<string>? Log;

    private bool _stageEntryBlockedLogged;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!engine.WdHold && engine.IsAttached && engine.Target.IsAlive())
                {
                    ApplyCheats();
                    RunActions();
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"auto: erro ({ex.Message}) — seguindo");
            }

            try
            {
                await Task.Delay(TickInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ApplyCheats()
    {
        engine.Cheats.SetActk(engine.WantActk);
        engine.Cheats.SetGodmode(engine.WantGodmode);

        var st = engine.WantStats;
        if (st.Count > 0) { try { WarnBool("stats", engine.Stats.ApplyStats(st), st.Count); } catch (Exception ex) { WarnOnce("stats", ex); } }
        var sg = engine.WantStage;
        if (sg.Count > 0) { try { WarnBool("stage", engine.Stats.ApplyStage(sg), sg.Count); } catch (Exception ex) { WarnOnce("stage", ex); } }
    }

    private readonly Dictionary<string, bool> _failing = new();

    private void WarnBool(string what, bool ok, int n)
    {
        if (_failing.TryGetValue(what, out bool was) && was == !ok) return;
        _failing[what] = !ok;
        Log?.Invoke(ok ? $"✔ {what}: aplicando {n} valor(es)"
                       : $"⚠ {what}: NAO resolvi o objeto no jogo ({n} valor(es) pendentes) — vou re-escanear");
    }

    private void WarnOnce(string what, Exception? ex)
    {
        bool bad = ex is not null;
        if (_failing.TryGetValue(what, out bool was) && was == bad) return;
        _failing[what] = bad;
        Log?.Invoke(bad ? $"⚠ não consegui aplicar {what}: {ex!.GetType().Name}: {ex.Message}"
                        : $"✔ {what} voltou a aplicar");
    }

    private void RunActions()
    {
        bool did = false;

        if (engine.WantAutobox && engine.AutoBox.OpenAll(() => engine.WantAutobox && engine.IsAttached, engine.AutoStash.InvFree))
            did = true;

        if (engine.WantAutostash && engine.AutoStash.MoveAllToStash(() => engine.WantAutostash && engine.IsAttached) > 0)
            did = true;

        if (engine.WantAutofuse && engine.AutoFuse.DoSynth(() => engine.WantAutofuse && engine.IsAttached))
            did = true;

        // AutoBoss/Evolution só precisam validar entrada de STAGETYPE=1. Builds antigas usam jgc;
        // a build 139467f3ad72 usa o candidate-A validado ao vivo apenas para type=1.
        // Type=2/type=3 continuam fora desse gate e não são liberados por inferência.
        bool stageEntryReady = engine.StageNav.CanValidateType1Entry;
        if (!stageEntryReady && (engine.WantAutoboss || engine.WantEvolve))
        {
            engine.WantAutoboss = false;
            engine.WantEvolve = false;
            if (!_stageEntryBlockedLogged)
            {
                _stageEntryBlockedLogged = true;
                Log?.Invoke("⚠ AutoBoss/Evolution bloqueados: validação de entrada type=1 ainda não está disponível para este build");
            }
        }
        else if (stageEntryReady)
        {
            _stageEntryBlockedLogged = false;
        }

        if (stageEntryReady && engine.WantAutoboss && !did &&
            engine.StageAutomation.AutoBoss(() => engine.WantAutoboss && engine.IsAttached))
            did = true;

        if (stageEntryReady && engine.WantEvolve && !did &&
            engine.StageAutomation.Evolve(() => engine.WantEvolve && engine.IsAttached))
            did = true;

        if (engine.WantAutostash && !did)
            engine.AutoStash.SortStep(2);
    }
}
