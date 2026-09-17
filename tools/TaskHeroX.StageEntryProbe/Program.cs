using TaskHeroX.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string ExpectedBuild = "139467f3ad72";
const long CandidateA = 0x99E5E0; // decomp: rota que parece cobrir tipos 1/3 + checks de capacidade
const long CandidateB = 0x99E750; // decomp: rota que parece cobrir tipo 2 + checks de estado/recurso

bool deepCodeDump = args.Contains("--code-dump", StringComparer.OrdinalIgnoreCase);
bool calleeDump = args.Contains("--callee-dump", StringComparer.OrdinalIgnoreCase);
bool navDump = args.Contains("--nav-dump", StringComparer.OrdinalIgnoreCase);

var e = new Engine();
if (!e.Attach())
{
    Console.WriteLine("[FAIL] Task Bar Hero não está aberto ou o attach falhou.");
    return;
}

Console.WriteLine("TaskHeroX Stage Entry Probe — SOMENTE LEITURA");
Console.WriteLine($"pid={e.Target.ProcessId} build={e.BuildHash} offsets={e.OffsetsLoaded}");

if (!string.Equals(e.BuildHash, ExpectedBuild, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"[BLOCKED] build diferente do esperado ({ExpectedBuild}). Nenhuma hipótese de RVA será usada.");
    return;
}

static string Hex(byte[] bytes) => bytes.Length == 0 ? "(vazio)" : Convert.ToHexString(bytes);

void DumpCode(string name, long rva, int size)
{
    try
    {
        var addr = e.Target.ModuleBase + (nint)rva;
        var bytes = e.Memory.ReadBytes(addr, size);
        Console.WriteLine($"{name}: RVA=0x{rva:X} VA=0x{(long)addr:X}");
        Console.WriteLine($"  bytes[{bytes.Length}]={Hex(bytes)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{name}: [FAIL] {ex.GetType().Name}: {ex.Message}");
    }
}

void DumpCandidate(string name, long rva)
    => DumpCode(name, rva, deepCodeDump ? 768 : 48);

Console.WriteLine();
Console.WriteLine("== Candidatos stage-entry ==");
DumpCandidate("candidate-A", CandidateA);
DumpCandidate("candidate-B", CandidateB);
Console.WriteLine("NOTA: este probe NÃO chama candidate-A/B; apenas lê os bytes do GameAssembly.");
if (!deepCodeDump)
    Console.WriteLine("Use --code-dump para ampliar a leitura de código para 768 bytes por candidato, ainda sem executar nada.");

if (calleeDump)
{
    Console.WriteLine();
    Console.WriteLine("== Callees diretos do candidate-A — SOMENTE CÓDIGO ==");
    Console.WriteLine("Estes RVAs foram obtidos da desmontagem do candidate-A; o probe apenas lê 384 bytes de cada alvo.");
    foreach (var (name, rva) in new (string Name, long Rva)[]
    {
        ("A.type/getter?",          0x9AA4B0),
        ("A.required/getter?",      0x9AA740),
        ("A.manager/singleton?",    0xA939E0),
        ("A.resource-key/getter?",  0x9AA770),
        ("A.resource-count?",       0xA940B0),
        ("A.capacity-used?",        0x962D20),
        ("A.capacity-max?",         0x95E8B0),
    })
    {
        DumpCode(name, rva, 384);
    }
    Console.WriteLine("NOTA: nenhum desses métodos é chamado pelo probe; esta seção é leitura de código para análise offline.");
}

if (navDump)
{
    Console.WriteLine();
    Console.WriteLine("== Navegação atual — SOMENTE CÓDIGO ==");
    Console.WriteLine("Lê 1024 bytes de jgd/jgk/jgq para investigar a entrada intermitente. Nenhuma função é chamada.");
    DumpCode("jgd / boss setup", 0x99E9E0, 1024);
    DumpCode("jgk / stage navigation", 0x9A01A0, 1024);
    DumpCode("jgq / unlocked check", 0x9A0E50, 1024);

    Console.WriteLine();
    Console.WriteLine("== CommonSaveData — SOMENTE LEITURA ==");
    try
    {
        nint psd = e.Resolver.ResolvePsd();
        nint common = psd != 0 ? e.Memory.ReadPtr(psd + 0x10) : 0;
        if (common == 0)
        {
            Console.WriteLine("CommonSaveData: não resolvido");
        }
        else
        {
            Console.WriteLine($"maxCompletedStage={e.Memory.ReadI32(common + 0x5C)}");
            Console.WriteLine($"lastClearedStageKey={e.Memory.ReadI32(common + 0x60)}");
            Console.WriteLine($"currentStageKey(save)={e.Memory.ReadI32(common + 0x64)}");
            Console.WriteLine($"currentStageWave(save)={e.Memory.ReadI32(common + 0x68)}");
            Console.WriteLine($"prevNormalStageKey={e.Memory.ReadI32(common + 0x6C)}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"CommonSaveData: [FAIL] {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine("== Progresso atual ==");
var (mx, cur, wave) = e.Save.StageProgress();
Console.WriteLine($"max={mx} cur={cur} wave={wave}");

Console.WriteLine();
Console.WriteLine("== Tabela viva de stages ==");
var table = e.StageNav.StageTable();
Console.WriteLine($"entries={table.Count}");

foreach (var group in table.OrderBy(kv => kv.Key).GroupBy(kv => kv.Value.Type).OrderBy(g => g.Key))
{
    Console.WriteLine($"type={group.Key} count={group.Count()}");
    foreach (var kv in group.Take(6))
    {
        var cache = e.StageNav.StageCache(kv.Key);
        Console.WriteLine($"  key={kv.Key} next={kv.Value.Next} ss={kv.Value.Ss} lvl={kv.Value.Lvl} waves={kv.Value.Waves} cache=0x{(long)cache:X}");
    }
}

Console.WriteLine();
Console.WriteLine("== Stages relevantes ==");
foreach (var key in new[] { cur, 3309, 3310, 4101, 4309, 4310 }.Distinct())
{
    if (key <= 0) continue;
    if (!table.TryGetValue(key, out var info))
    {
        Console.WriteLine($"key={key}: ausente da StageTable");
        continue;
    }

    var cache = e.StageNav.StageCache(key);
    Console.WriteLine($"key={key}: type={info.Type} next={info.Next} ss={info.Ss} lvl={info.Lvl} waves={info.Waves} cache=0x{(long)cache:X}");
}

Console.WriteLine();
Console.WriteLine("== Escopo real usado pelo TaskHeroX ==");
int[] autoBossKeys = [3310, 4310];
bool autoBossAllType1 = autoBossKeys.All(k => table.TryGetValue(k, out var info) && info.Type == 1);
Console.WriteLine($"AutoBoss: 3310/4310 são type=1? {autoBossAllType1}");
Console.WriteLine("Evolution: a validação CanEnter só é usada quando o PRÓXIMO stage é type=1; stages normais type=0 seguem por GoToStage.");
Console.WriteLine("Conclusão de escopo: candidate-B/type=2 não é requisito para AutoBoss/Evolution; a confiabilidade de jgd/jgk ainda está em revisão.");

Console.WriteLine();
Console.WriteLine("== Snapshot somente leitura de recursos ==");
try
{
    var counts = e.Inventory.ReadCounts();
    Console.WriteLine($"soulstone 190003 (Hell)    = {counts.GetValueOrDefault(190003)}");
    Console.WriteLine($"soulstone 190004 (Torment) = {counts.GetValueOrDefault(190004)}");
}
catch (Exception ex)
{
    Console.WriteLine($"inventory: [FAIL] {ex.GetType().Name}: {ex.Message}");
}

try
{
    int invFree = e.AutoStash.InvFree();
    var (invOccupied, stashFree) = e.AutoStash.SlotCounts();
    Console.WriteLine($"inventário: ocupados={invOccupied} livres={invFree}");
    Console.WriteLine($"baú: livres={stashFree}");
}
catch (Exception ex)
{
    Console.WriteLine($"slots: [FAIL] {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("== Estado da rota ==");
Console.WriteLine($"jgc resolvido? {e.Symbols.Has("jgc")}");
Console.WriteLine($"type1 ready={e.StageNav.CanValidateType1Entry} split={e.StageNav.UsesSplitType1Validator}");
Console.WriteLine("PR permanece em draft até a navegação jgd/jgk ficar confiável nos smokes de AutoBoss/Evolution.");
Console.WriteLine($"jogo vivo={e.Target.IsAlive()}");
