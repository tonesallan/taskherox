using TaskHeroX.Core;
using TaskHeroX.Core.Game;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string ExpectedBuild = "c265dc8bc7aa";
const long CandidateA = 0x9A9F20;

bool successMode = args.Contains("--success", StringComparer.OrdinalIgnoreCase);
bool productionRoute = args.Contains("--production-route", StringComparer.OrdinalIgnoreCase);
int testStage = successMode ? 3310 : 4310;
int soulStoneKey = successMode ? 190003 : 190004;
string soulStoneName = successMode ? "Hell" : "Torment";
int expectedResult = successMode ? 0 : 2;
string expectedName = successMode ? "Success" : "NeedSoulStone";

var e = new Engine();
e.Log += m => Console.WriteLine("  [engine] " + m);

if (!e.Attach())
{
    Console.WriteLine("[FAIL] Task Bar Hero não está aberto ou o attach falhou.");
    return;
}

Console.WriteLine("TaskHeroX Stage Entry Semantic Probe — TESTE CONTROLADO");
Console.WriteLine($"pid={e.Target.ProcessId} build={e.BuildHash} offsets={e.OffsetsLoaded}");
Console.WriteLine($"modo={(successMode ? "success/type=1" : "missing-soulstone/type=1")} stage={testStage} rota={(productionRoute ? "StageNav.CanEnter" : "candidate-A direto")}");
Console.WriteLine("Executa uma única validação e restaura o hook do dispatcher em seguida.");
Console.WriteLine("Não navega, não chama jgd/jgk, não escreve progresso e não consome soulstone por intenção do probe.");

if (!string.Equals(e.BuildHash, ExpectedBuild, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"[BLOCKED] build diferente de {ExpectedBuild}.");
    return;
}

var table = e.StageNav.StageTable();
if (!table.TryGetValue(testStage, out var info) || info.Type != 1 || info.Ss != soulStoneKey)
{
    Console.WriteLine($"[BLOCKED] stage {testStage} não é type=1 nesta sessão.");
    return;
}

nint cache = e.StageNav.StageCache(testStage);
if (cache == 0)
{
    Console.WriteLine($"[BLOCKED] StageCache({testStage}) não foi resolvido.");
    return;
}

if (productionRoute)
{
    Console.WriteLine($"production route ready={e.StageNav.CanValidateType1Entry} split={e.StageNav.UsesSplitType1Validator}");
    if (!e.StageNav.CanValidateType1Entry || !e.StageNav.UsesSplitType1Validator)
    {
        Console.WriteLine("[BLOCKED] a rota de produção type=1 não está marcada como validada nesta sessão.");
        return;
    }
}

Dictionary<int, int> ReadCountsSafe()
{
    try { return e.Inventory.ReadCounts(); }
    catch { return new Dictionary<int, int>(); }
}

var beforeCounts = ReadCountsSafe();
int stoneBefore = beforeCounts.GetValueOrDefault(soulStoneKey);
var beforeProgress = e.Save.StageProgress();
int invFreeBefore = -1;
int stashFreeBefore = -1;
try
{
    invFreeBefore = e.AutoStash.InvFree();
    (_, stashFreeBefore) = e.AutoStash.SlotCounts();
}
catch { }

Console.WriteLine();
Console.WriteLine("== Pré-condições ==");
Console.WriteLine($"{testStage}: type={info.Type} ss={info.Ss} cache=0x{(long)cache:X}");
Console.WriteLine($"{soulStoneName} soulstone antes = {stoneBefore}");
Console.WriteLine($"inventário livre={invFreeBefore} baú livre={stashFreeBefore}");
Console.WriteLine($"progresso antes: max={beforeProgress.Max} cur={beforeProgress.Cur} wave={beforeProgress.Wave}");

if (!successMode && stoneBefore != 0)
{
    Console.WriteLine("[BLOCKED] o teste NeedSoulStone exige contagem zero. Nenhuma função será chamada.");
    return;
}

if (successMode)
{
    if (stoneBefore <= 0)
    {
        Console.WriteLine("[BLOCKED] o teste Success exige pelo menos uma Hell soulstone. Nenhuma função será chamada.");
        return;
    }
    if (stashFreeBefore == 0 || invFreeBefore == 0)
    {
        Console.WriteLine("[BLOCKED] sem capacidade livre observável; não testar o ramo Success nesta condição.");
        return;
    }
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
    if (productionRoute)
    {
        result = e.StageNav.CanEnter(testStage);
        Console.WriteLine($"StageNav.CanEnter({testStage}) => {(result is null ? "null" : result.Value.ToString())}");
    }
    else
    {
        result = dispatcher.Call((long)(e.Target.ModuleBase + (nint)CandidateA), cache);
        Console.WriteLine($"candidate-A({testStage}) => {(result is null ? "null" : result.Value.ToString())}");
    }
}
finally
{
    dispatcher.Remove();
    Console.WriteLine("dispatcher removido/restaurado após a chamada.");
}

Thread.Sleep(150);
var afterCounts = ReadCountsSafe();
int stoneAfter = afterCounts.GetValueOrDefault(soulStoneKey);
var afterProgress = e.Save.StageProgress();

Console.WriteLine();
Console.WriteLine("== Pós-condições ==");
Console.WriteLine($"{soulStoneName} soulstone depois = {stoneAfter}");
Console.WriteLine($"progresso depois: max={afterProgress.Max} cur={afterProgress.Cur} wave={afterProgress.Wave}");
Console.WriteLine($"jogo vivo={e.Target.IsAlive()}");

bool resourceUnchanged = stoneAfter == stoneBefore;
bool stageUnchanged = afterProgress.Max == beforeProgress.Max && afterProgress.Cur == beforeProgress.Cur;
bool semanticOk = result == expectedResult;

Console.WriteLine();
Console.WriteLine($"[{(semanticOk ? "PASS" : "FAIL")}] retorno esperado {expectedName}({expectedResult})");
Console.WriteLine($"[{(resourceUnchanged ? "PASS" : "FAIL")}] soulstone não mudou ({stoneBefore}->{stoneAfter})");
Console.WriteLine($"[{(stageUnchanged ? "PASS" : "FAIL")}] max/cur não mudaram ({beforeProgress.Max}/{beforeProgress.Cur} -> {afterProgress.Max}/{afterProgress.Cur})");
Console.WriteLine(semanticOk && resourceUnchanged && stageUnchanged && e.Target.IsAlive()
    ? $"[PASS] caminho type=1/{expectedName} validado sem efeito de gameplay observado{(productionRoute ? " pela rota de produção" : "")} ."
    : "[FAIL] não considerar este ramo validado ainda.");
