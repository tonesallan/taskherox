using System.Diagnostics;
using TbhBot.Core;
using TaskHeroX.Core.Game;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string ExpectedBuild = "c265dc8bc7aa";
const int StartStage = 3309;
const int BossStage = 3310;
const int ExpectedForwardStage = 4101;
const int HellSoulStone = 190003;

var e = new Engine();
e.Log += m => Console.WriteLine("  [engine] " + m);

if (!e.Attach())
{
    Console.WriteLine("[FAIL] Task Bar Hero não está aberto ou o attach falhou.");
    return;
}

Console.WriteLine("TaskHeroX Evolution Smoke Probe — UM CRUZAMENTO DE BOSS");
Console.WriteLine($"pid={e.Target.ProcessId} build={e.BuildHash} offsets={e.OffsetsLoaded}");
Console.WriteLine("O probe usa HELL 3-9 como ponto de partida, espera a fase limpar, chama Evolution uma vez para cruzar HELL 3-10 e tenta restaurar a fase original no final.");
Console.WriteLine("Se o boss morrer, UMA Hell soulstone pode ser consumida pelo jogo.");
Console.WriteLine("Ctrl+C solicita cancelamento limpo; o dispatcher é removido e a fase original é restaurada em finally quando possível.");

if (!string.Equals(e.BuildHash, ExpectedBuild, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"[BLOCKED] build diferente de {ExpectedBuild}.");
    return;
}

if (!e.StageNav.CanValidateType1Entry || !e.StageNav.UsesSplitType1Validator)
{
    Console.WriteLine("[BLOCKED] rota type=1 validada não está disponível nesta sessão.");
    return;
}

var table = e.StageNav.StageTable();
if (!table.TryGetValue(StartStage, out var startInfo) || startInfo.Type != 0 || startInfo.Next != BossStage)
{
    Console.WriteLine("[BLOCKED] HELL 3-9 não corresponde ao caminho normal esperado 3309 -> 3310.");
    return;
}
if (!table.TryGetValue(BossStage, out var bossInfo) || bossInfo.Type != 1 || bossInfo.Next != ExpectedForwardStage || bossInfo.Ss != HellSoulStone)
{
    Console.WriteLine("[BLOCKED] HELL 3-10 não corresponde ao caminho type=1 esperado 3310 -> 4101.");
    return;
}
if (startInfo.Waves <= 0)
{
    Console.WriteLine("[BLOCKED] WaveAmount de 3309 inválido.");
    return;
}

Dictionary<int, int> Counts()
{
    try { return e.Inventory.ReadCounts(); }
    catch { return new Dictionary<int, int>(); }
}

var original = e.Save.StageProgress();
var sources = e.Save.StageProgressSources();
var beforeCounts = Counts();
int hellBefore = beforeCounts.GetValueOrDefault(HellSoulStone);
int boxesBefore = e.AutoBox.IuwCount(2) ?? -1;

Console.WriteLine();
Console.WriteLine("== Pré-condições ==");
Console.WriteLine($"fase original: max={original.Max} cur={original.Cur} wave={original.Wave}");
Console.WriteLine($"fontes: runtime cur={sources.RuntimeCur} wave={sources.RuntimeWave} · save cur={sources.SaveCur} wave={sources.SaveWave}");
Console.WriteLine($"3309: waves={startInfo.Waves} next={startInfo.Next}");
Console.WriteLine($"3310: type={bossInfo.Type} next={bossInfo.Next} ss={bossInfo.Ss}");
Console.WriteLine($"Hell soulstone={hellBefore} · ACTBOSS boxes={boxesBefore}");

if (sources.RuntimeCur <= 0 || !table.ContainsKey(sources.RuntimeCur))
{
    Console.WriteLine($"[BLOCKED] runtime de estágio inválido: {sources.RuntimeCur}.");
    return;
}

if (sources.SaveCur > 0 && sources.RuntimeCur != sources.SaveCur)
{
    Console.WriteLine($"[INFO] runtime/save divergem ({sources.RuntimeCur}/{sources.SaveCur}); runtime será usado como fonte autoritativa.");
}
if (hellBefore <= 0)
{
    Console.WriteLine("[BLOCKED] nenhuma Hell soulstone disponível.");
    return;
}
if (original.Cur <= 0 || !table.ContainsKey(original.Cur))
{
    Console.WriteLine("[BLOCKED] fase original não pôde ser validada para restauração.");
    return;
}

int keepRunning = 1;
Console.CancelKeyPress += (_, ev) =>
{
    ev.Cancel = true;
    Interlocked.Exchange(ref keepRunning, 0);
    Console.WriteLine("\n[cancel] encerrando o smoke de forma limpa...");
};

bool evolveResult = false;
int stageAfterEvolve = 0;
int waveAtTrigger = -1;

try
{
    Console.WriteLine();
    Console.WriteLine("== Preparação ==");

    if (original.Cur != StartStage)
    {
        if (!e.StageNav.GoToStage(StartStage))
        {
            Console.WriteLine("[FAIL] não consegui solicitar navegação para HELL 3-9.");
            return;
        }

        var enterWatch = Stopwatch.StartNew();
        while (e.Save.StageProgress().Cur != StartStage && enterWatch.ElapsedMilliseconds < 10_000)
        {
            if (Volatile.Read(ref keepRunning) == 0 || !e.Target.IsAlive()) return;
            Thread.Sleep(100);
        }
        if (e.Save.StageProgress().Cur != StartStage)
        {
            var s = e.Save.StageProgressSources();
            Console.WriteLine($"[FAIL] navegação para 3309 não confirmou no runtime; runtime={s.RuntimeCur} save={s.SaveCur}.");
            return;
        }
    }

    Console.WriteLine("HELL 3-9 carregado no runtime. Aguardando última wave...");

    var clearWatch = Stopwatch.StartNew();
    while (clearWatch.ElapsedMilliseconds < 240_000)
    {
        if (Volatile.Read(ref keepRunning) == 0 || !e.Target.IsAlive()) return;
        var p = e.Save.StageProgress();
        if (p.Cur != StartStage)
        {
            var s = e.Save.StageProgressSources();
            Console.WriteLine($"[FAIL] saiu de 3309 antes do trigger da Evolution; runtime={s.RuntimeCur} save={s.SaveCur}.");
            return;
        }
        if (p.Wave >= startInfo.Waves - 1)
        {
            waveAtTrigger = p.Wave;
            break;
        }
        Thread.Sleep(50);
    }

    if (waveAtTrigger < startInfo.Waves - 1)
    {
        Console.WriteLine($"[FAIL] timeout aguardando limpeza de 3309; wave={e.Save.StageProgress().Wave}/{startInfo.Waves}.");
        return;
    }

    Console.WriteLine($"wave pronta: {waveAtTrigger}/{startInfo.Waves - 1} (WaveAmount={startInfo.Waves})");
    Console.WriteLine();
    Console.WriteLine("== Evolution one-shot ==");
    evolveResult = e.StageAutomation.Evolve(() => Volatile.Read(ref keepRunning) == 1 && e.Target.IsAlive());
    stageAfterEvolve = e.Save.StageProgress().Cur;
    Console.WriteLine($"StageAutomation.Evolve => {evolveResult}");
    Console.WriteLine($"fase imediatamente após Evolution={stageAfterEvolve}");
}
finally
{
    if (e.Target.IsAlive() && original.Cur > 0 && e.Save.StageProgress().Cur != original.Cur)
    {
        try
        {
            Console.WriteLine($"restaurando fase original {original.Cur}...");
            e.StageNav.GoToStage(original.Cur);
            var restoreWatch = Stopwatch.StartNew();
            while (e.Save.StageProgress().Cur != original.Cur && restoreWatch.ElapsedMilliseconds < 8_000)
                Thread.Sleep(100);
            Console.WriteLine($"fase após restauração={e.Save.StageProgress().Cur}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] restauração da fase original falhou: {ex.Message}");
        }
    }

    if (e.Dispatcher is RealDispatcher dispatcher)
    {
        dispatcher.Remove();
        Console.WriteLine("dispatcher removido/restaurado.");
    }
}

Thread.Sleep(250);
var afterCounts = Counts();
int hellAfter = afterCounts.GetValueOrDefault(HellSoulStone);
int boxesAfter = e.AutoBox.IuwCount(2) ?? -1;
var finalProgress = e.Save.StageProgress();

Console.WriteLine();
Console.WriteLine("== Pós-condições ==");
Console.WriteLine($"Hell soulstone: {hellBefore} -> {hellAfter}");
Console.WriteLine($"ACTBOSS boxes: {boxesBefore} -> {boxesAfter}");
Console.WriteLine($"fase final: max={finalProgress.Max} cur={finalProgress.Cur} wave={finalProgress.Wave}");
Console.WriteLine($"jogo vivo={e.Target.IsAlive()}");

if (Volatile.Read(ref keepRunning) == 0)
{
    Console.WriteLine("[CANCELLED] teste interrompido pelo usuário; não usar como validação final.");
    return;
}

bool bossEvidence = (boxesBefore >= 0 && boxesAfter > boxesBefore) || hellAfter == hellBefore - 1;
bool forwardObserved = stageAfterEvolve == ExpectedForwardStage || bossEvidence;
bool restored = finalProgress.Cur == original.Cur;
bool stoneSane = hellAfter == hellBefore || hellAfter == hellBefore - 1;

Console.WriteLine();
Console.WriteLine($"[{(evolveResult ? "PASS" : "FAIL")}] Evolution retornou sucesso");
Console.WriteLine($"[{(forwardObserved ? "PASS" : "FAIL")}] cruzamento 3309 -> 3310 -> 4101 confirmado por estágio/drop");
Console.WriteLine($"[{(stoneSane ? "PASS" : "FAIL")}] variação de Hell soulstone plausível");
Console.WriteLine($"[{(restored ? "PASS" : "FAIL")}] fase original restaurada ({original.Cur})");
Console.WriteLine(evolveResult && forwardObserved && stoneSane && restored && e.Target.IsAlive()
    ? "[PASS] Evolution one-shot cruzou um boss type=1 pela rota de produção e restaurou a sessão."
    : "[WARN] Evolution não ficou totalmente validada; revisar o log antes de liberar a automação.");
