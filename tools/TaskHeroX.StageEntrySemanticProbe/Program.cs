using TbhBot.Core;
using TbhBot.Core.Game;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string ExpectedBuild = "139467f3ad72";
const long CandidateA = 0x99E5E0;
const int TestStage = 4310;
const int TormentSoulStone = 190004;

var e = new Engine();
e.Log += m => Console.WriteLine("  [engine] " + m);

if (!e.Attach())
{
    Console.WriteLine("[FAIL] Task Bar Hero não está aberto ou o attach falhou.");
    return;
}

Console.WriteLine("TaskHeroX Stage Entry Semantic Probe — TESTE CONTROLADO");
Console.WriteLine($"pid={e.Target.ProcessId} build={e.BuildHash} offsets={e.OffsetsLoaded}");
Console.WriteLine("Este teste executa SOMENTE candidate-A para o stage 4310 e restaura o hook do dispatcher em seguida.");
Console.WriteLine("Não navega, não chama jgd/jgk, não escreve progresso e não consome soulstone por intenção do probe.");

if (!string.Equals(e.BuildHash, ExpectedBuild, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"[BLOCKED] build diferente de {ExpectedBuild}.");
    return;
}

var table = e.StageNav.StageTable();
if (!table.TryGetValue(TestStage, out var info) || info.Type != 1)
{
    Console.WriteLine("[BLOCKED] stage 4310 não é type=1 nesta sessão.");
    return;
}

nint cache = e.StageNav.StageCache(TestStage);
if (cache == 0)
{
    Console.WriteLine("[BLOCKED] StageCache(4310) não foi resolvido.");
    return;
}

Dictionary<int, int> ReadCountsSafe()
{
    try { return e.Inventory.ReadCounts(); }
    catch { return new Dictionary<int, int>(); }
}

var beforeCounts = ReadCountsSafe();
int tormentBefore = beforeCounts.GetValueOrDefault(TormentSoulStone);
var beforeProgress = e.Save.StageProgress();

Console.WriteLine();
Console.WriteLine("== Pré-condições ==");
Console.WriteLine($"4310: type={info.Type} ss={info.Ss} cache=0x{(long)cache:X}");
Console.WriteLine($"Torment soulstone antes = {tormentBefore}");
Console.WriteLine($"progresso antes: max={beforeProgress.Max} cur={beforeProgress.Cur} wave={beforeProgress.Wave}");

if (tormentBefore != 0)
{
    Console.WriteLine("[BLOCKED] este teste foi desenhado apenas para o caminho 'falta soulstone'. Como a conta tem Torment soulstone, nenhuma função será chamada.");
    return;
}

if (e.Dispatcher is not RealDispatcher dispatcher)
{
    Console.WriteLine("[BLOCKED] dispatcher real indisponível.");
    return;
}

int? result = null;
try
{
    Console.WriteLine();
    Console.WriteLine("== Chamada única ==");
    result = dispatcher.Call((long)(e.Target.ModuleBase + (nint)CandidateA), cache);
    Console.WriteLine($"candidate-A(4310) => {(result is null ? "null" : result.Value.ToString())}");
}
finally
{
    dispatcher.Remove();
    Console.WriteLine("dispatcher removido/restaurado após a chamada.");
}

Thread.Sleep(150);
var afterCounts = ReadCountsSafe();
int tormentAfter = afterCounts.GetValueOrDefault(TormentSoulStone);
var afterProgress = e.Save.StageProgress();

Console.WriteLine();
Console.WriteLine("== Pós-condições ==");
Console.WriteLine($"Torment soulstone depois = {tormentAfter}");
Console.WriteLine($"progresso depois: max={afterProgress.Max} cur={afterProgress.Cur} wave={afterProgress.Wave}");
Console.WriteLine($"jogo vivo={e.Target.IsAlive()}");

bool resourceUnchanged = tormentAfter == tormentBefore;
bool stageUnchanged = afterProgress.Max == beforeProgress.Max && afterProgress.Cur == beforeProgress.Cur;
bool semanticOk = result == 2;

Console.WriteLine();
Console.WriteLine($"[{(semanticOk ? "PASS" : "FAIL")}] retorno esperado NeedSoulStone(2)");
Console.WriteLine($"[{(resourceUnchanged ? "PASS" : "FAIL")}] soulstone não mudou ({tormentBefore}->{tormentAfter})");
Console.WriteLine($"[{(stageUnchanged ? "PASS" : "FAIL")}] max/cur não mudaram ({beforeProgress.Max}/{beforeProgress.Cur} -> {afterProgress.Max}/{afterProgress.Cur})");
Console.WriteLine(semanticOk && resourceUnchanged && stageUnchanged && e.Target.IsAlive()
    ? "[PASS] caminho type=1/falta-soulstone validado sem efeito de gameplay observado."
    : "[FAIL] não considerar candidate-A validado ainda.");
