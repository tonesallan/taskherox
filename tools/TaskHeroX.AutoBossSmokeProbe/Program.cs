using TaskHeroX.Core;
using TaskHeroX.Core.Game;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string ExpectedBuild = "c265dc8bc7aa";
const int HellSoulStone = 190003;
const int TormentSoulStone = 190004;
const int HellBoss = 3310;

var e = new Engine();
e.Log += m => Console.WriteLine("  [engine] " + m);

if (!e.Attach())
{
    Console.WriteLine("[FAIL] Task Bar Hero não está aberto ou o attach falhou.");
    return;
}

Console.WriteLine("TaskHeroX AutoBoss Smoke Probe — UMA ÚNICA EXECUÇÃO");
Console.WriteLine($"pid={e.Target.ProcessId} build={e.BuildHash} offsets={e.OffsetsLoaded}");
Console.WriteLine("Este teste chama StageAutomation.AutoBoss uma única vez.");
Console.WriteLine("Se o boss Hell 3310 morrer, UMA Hell soulstone pode ser consumida pelo jogo.");
Console.WriteLine("Ctrl+C solicita cancelamento limpo; o dispatcher é removido em finally.");

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
if (!table.TryGetValue(HellBoss, out var hellInfo) || hellInfo.Type != 1 || hellInfo.Ss != HellSoulStone)
{
    Console.WriteLine("[BLOCKED] stage 3310 não é type=1 nesta sessão.");
    return;
}

Dictionary<int, int> Counts()
{
    try { return e.Inventory.ReadCounts(); }
    catch { return new Dictionary<int, int>(); }
}

var beforeCounts = Counts();
int hellBefore = beforeCounts.GetValueOrDefault(HellSoulStone);
int tormentBefore = beforeCounts.GetValueOrDefault(TormentSoulStone);
var beforeProgress = e.Save.StageProgress();
var sources = e.Save.StageProgressSources();
int boxesBefore = e.AutoBox.IuwCount(2) ?? -1;

Console.WriteLine();
Console.WriteLine("== Pré-condições ==");
Console.WriteLine($"Hell soulstone={hellBefore} · Torment soulstone={tormentBefore}");
Console.WriteLine($"progresso: max={beforeProgress.Max} cur={beforeProgress.Cur} wave={beforeProgress.Wave}");
Console.WriteLine($"fontes: runtime cur={sources.RuntimeCur} wave={sources.RuntimeWave} · save cur={sources.SaveCur} wave={sources.SaveWave}");
Console.WriteLine($"ACTBOSS boxes={boxesBefore}");

if (sources.RuntimeCur <= 0 || !table.ContainsKey(sources.RuntimeCur))
{
    Console.WriteLine($"[BLOCKED] runtime de estágio inválido: {sources.RuntimeCur}.");
    return;
}

if (sources.SaveCur > 0 && sources.RuntimeCur != sources.SaveCur)
{
    Console.WriteLine($"[INFO] runtime/save divergem ({sources.RuntimeCur}/{sources.SaveCur}); runtime será usado como fonte autoritativa.");
}
if (tormentBefore != 0)
{
    Console.WriteLine("[BLOCKED] Torment soulstone precisa estar em 0 para este smoke escolher deterministicamente o boss Hell 3310.");
    return;
}
if (hellBefore <= 0)
{
    Console.WriteLine("[BLOCKED] nenhuma Hell soulstone disponível.");
    return;
}
if (beforeProgress.Cur == HellBoss)
{
    Console.WriteLine("[BLOCKED] já está dentro do boss 3310.");
    return;
}

int keepRunning = 1;
Console.CancelKeyPress += (_, ev) =>
{
    ev.Cancel = true;
    Interlocked.Exchange(ref keepRunning, 0);
    Console.WriteLine("\n[cancel] encerrando o smoke de forma limpa...");
};

bool result = false;
try
{
    Console.WriteLine();
    Console.WriteLine("== AutoBoss one-shot ==");
    result = e.StageAutomation.AutoBoss(() => Volatile.Read(ref keepRunning) == 1 && e.Target.IsAlive());
    Console.WriteLine($"StageAutomation.AutoBoss => {result}");
}
finally
{
    if (e.Dispatcher is RealDispatcher dispatcher)
    {
        dispatcher.Remove();
        Console.WriteLine("dispatcher removido/restaurado.");
    }
}

Thread.Sleep(250);
var afterCounts = Counts();
int hellAfter = afterCounts.GetValueOrDefault(HellSoulStone);
var afterProgress = e.Save.StageProgress();
int boxesAfter = e.AutoBox.IuwCount(2) ?? -1;

Console.WriteLine();
Console.WriteLine("== Pós-condições ==");
Console.WriteLine($"Hell soulstone: {hellBefore} -> {hellAfter}");
Console.WriteLine($"progresso: max={afterProgress.Max} cur={afterProgress.Cur} wave={afterProgress.Wave}");
Console.WriteLine($"ACTBOSS boxes: {boxesBefore} -> {boxesAfter}");
Console.WriteLine($"jogo vivo={e.Target.IsAlive()}");

if (Volatile.Read(ref keepRunning) == 0)
{
    Console.WriteLine("[CANCELLED] teste interrompido pelo usuário; não usar como validação final.");
    return;
}

bool stoneSane = hellAfter == hellBefore || hellAfter == hellBefore - 1;
bool returned = afterProgress.Cur != HellBoss;
bool boxSane = boxesBefore < 0 || boxesAfter < 0 || boxesAfter >= boxesBefore;

Console.WriteLine();
Console.WriteLine($"[{(stoneSane ? "PASS" : "FAIL")}] variação de Hell soulstone plausível");
Console.WriteLine($"[{(returned ? "PASS" : "FAIL")}] não permaneceu preso no boss 3310");
Console.WriteLine($"[{(boxSane ? "PASS" : "FAIL")}] contagem ACTBOSS não regrediu");
Console.WriteLine(result && stoneSane && returned && boxSane && e.Target.IsAlive()
    ? "[PASS] AutoBoss one-shot concluiu um ciclo real com a rota type=1 de produção."
    : "[WARN] ciclo não confirmou kill completo; revisar o log antes de liberar a automação.");
