using TbhBot.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string ExpectedBuild = "139467f3ad72";
const long CandidateA = 0x99E5E0; // decomp: rota que parece cobrir tipos 1/3 + checks de capacidade
const long CandidateB = 0x99E750; // decomp: rota que parece cobrir tipo 2 + checks de estado/recurso

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

void DumpCandidate(string name, long rva)
{
    try
    {
        var addr = e.Target.ModuleBase + (nint)rva;
        var bytes = e.Memory.ReadBytes(addr, 48);
        Console.WriteLine($"{name}: RVA=0x{rva:X} VA=0x{(long)addr:X}");
        Console.WriteLine($"  bytes[48]={Hex(bytes)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{name}: [FAIL] {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine("== Candidatos stage-entry ==");
DumpCandidate("candidate-A", CandidateA);
DumpCandidate("candidate-B", CandidateB);
Console.WriteLine("NOTA: este probe NÃO chama candidate-A/B; apenas lê os bytes do GameAssembly.");

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
foreach (var key in new[] { cur, 3310, 4310 })
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
Console.WriteLine("== Estado do bloqueio ==");
Console.WriteLine($"jgc resolvido? {e.Symbols.Has("jgc")}");
Console.WriteLine("AutoBoss/Evolution DEVEM permanecer bloqueados até candidate-A/B terem semântica comprovada por tipo de stage.");
Console.WriteLine($"jogo vivo={e.Target.IsAlive()}");
