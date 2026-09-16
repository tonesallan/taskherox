using TbhBot.Core.Il2Cpp;
using TaskHeroX.Core.Memory;

namespace TbhBot.Core.Game;

/// <summary>
/// Navegação de estágios — base do modo Evolução / Auto-boss (porta de stage_table / _stage_cache /
/// goto_stage / enter_boss do tbh_core.py). São 120 estágios; o progresso é UM int (maxCompletedStage)
/// e a corrente de entrada é a mesma NextStageKey que o jogo usa. Aqui só o transporte:
///  - <see cref="StageTable"/>: lê a tabela VIVA {key -> {next,type,ss,lvl}} (cacheada, é estática).
///  - <see cref="StageCache"/>: pega o StageCache* de um key pelo Dictionary&lt;int,StageCache&gt; do jogo.
///  - <see cref="GoToStage"/>: jgk (passo final de entrada de estágio NORMAL), na main-thread.
///  - <see cref="EnterBoss"/>: o PAR jgd+jgk que reproduz o CLIQUE do x-10.
/// </summary>
public sealed class StageNav(MemoryAccess mem, SymbolTable sym, Il2CppResolver resolver, RealDispatcher disp)
{
    private readonly MemoryAccess _mem = mem;
    private readonly SymbolTable _sym = sym;
    private readonly Il2CppResolver _resolver = resolver;
    private readonly RealDispatcher _disp = disp;
    public Action<string>? Log;

    // Builds 139467f3ad72 e c265dc8bc7aa: o antigo jgc foi dividido. O candidate-A abaixo foi validado AO VIVO apenas
    // para STAGETYPE=1: Success(0) e NeedSoulStone(2), sem consumo de soulstone nem mudança de max/cur.
    // Não reutilizamos a chave ambígua "jgc" e NÃO habilitamos type=2/type=3 por inferência.
    private bool IsValidatedBuild139467 =>
        _sym.Get("uo_ti") == 0x5F4C658 &&
        _sym.Get("stage_off") == 0x88 &&
        _sym.Get("jgk") == 0x9A01A0 &&
        _sym.Get("jgd") == 0x99E9E0;

    private bool IsValidatedBuildC265 =>
        _sym.Get("uo_ti") == 0x5F5C9C8 &&
        _sym.Get("stage_off") == 0x88 &&
        _sym.Get("jgk") == 0x9ABC50 &&
        _sym.Get("jgd") == 0x9AA370;

    private long SplitType1ValidatorRva =>
        IsValidatedBuild139467 ? 0x99E5E0 :
        IsValidatedBuildC265   ? 0x9A9F20 :
        0;

    // Guard de build por tuple de símbolos estruturais. Todos estes valores pertencem à build
    // 139467f3ad72; se qualquer um mudar, o split validator fica automaticamente indisponível.
    private bool HasValidatedSplitType1Route =>
        SplitType1ValidatorRva != 0;

    /// <summary>
    /// True quando existe uma rota validada para checar entrada de boss STAGETYPE=1.
    /// Builds antigas continuam usando jgc; builds split validadas usam somente o candidate-A correspondente.
    /// </summary>
    public bool CanValidateType1Entry => _sym.Get("jgc") != 0 || HasValidatedSplitType1Route;

    /// <summary>True somente quando a sessão usa uma rota split type=1 previamente validada.</summary>
    public bool UsesSplitType1Validator => _sym.Get("jgc") == 0 && HasValidatedSplitType1Route;

    // A tabela de estágios é estática no jogo -> cacheia depois de ler cheia (>=100 keys, como o Python).
    private Dictionary<int, StageInfo>? _stageTbl;

    private nint Base => _mem.Target.ModuleBase;

    /// <summary>
    /// {stageKey -> {next,type,ss,lvl}} lido da tabela VIVA do jogo (120 estágios). Cacheado.
    /// bal (singleton da tabela balanceada) = deref duplo do static_fields de bal_ti; a lista fica em +stage_off,
    /// e cada StageInfoData tem key@0x30, type@0x40, lvl@0x50, ss@0x94, next@0xA0.
    /// </summary>
    public Dictionary<int, StageInfo> StageTable()
    {
        if (_stageTbl is { } cached) return cached;

        long ti = _sym.Get("bal_ti"), off = _sym.Get("stage_off");
        var empty = new Dictionary<int, StageInfo>();
        if (ti == 0 || off == 0) return empty;

        // klass=[base+ti]; bal=[[klass+0xB8]] (StaticFields já faz [[base+ti]+0xB8]; falta o último deref).
        nint sf = _resolver.StaticFields(ti);
        if (sf == 0) return empty;
        nint bal = _mem.ReadPtr(sf);
        if (!MemoryAccess.IsValidPointer(bal)) return empty;

        nint lst = _mem.ReadPtr(bal + (nint)off);
        if (!MemoryAccess.IsValidPointer(lst)) return empty;
        nint arr = _mem.ReadPtr(lst + 0x10);
        uint n = _mem.ReadU32(lst + 0x18);
        if (!MemoryAccess.IsValidPointer(arr) || n == 0 || n > 500) return empty;

        var t = new Dictionary<int, StageInfo>();
        // BATCH: os elementos são ponteiros contíguos a partir de arr+0x20.
        ulong[] elems = _mem.ReadArray<ulong>(arr + 0x20, (int)n);
        foreach (ulong e in elems)
        {
            nint o = (nint)e;
            if (!MemoryAccess.IsValidPointer(o)) continue;
            int k = _mem.ReadI32(o + 0x30);
            if (k == 0) continue;
            t[k] = new StageInfo(
                Next:  _mem.ReadI32(o + 0xA0),
                Type:  _mem.ReadI32(o + 0x40),
                Ss:    _mem.ReadI32(o + 0x94),
                Lvl:   _mem.ReadI32(o + 0x50),
                Waves: _mem.ReadI32(o + 0x54));   // WaveAmount — usado p/ detectar "estágio limpo" na evolução
        }
        if (t.Count >= 100) _stageTbl = t;   // só cacheia quando veio íntegra (evita fixar uma leitura parcial no boot)
        return t;
    }

    /// <summary>
    /// StageCache* do stageKey, pelo Dictionary&lt;int,StageCache&gt; do próprio jogo (0 se não existe).
    /// sf = static_fields de uo_ti (deref duplo, = _uo_sf); dict em +uo_dict; entries em dict+0x18 (n em +0x20),
    /// cada entrada de 0x18 bytes com key@+8 e value(StageCache*)@+0x10.
    /// </summary>
    public nint StageCache(int key)
    {
        long tiUo = _sym.Get("uo_ti"), off = _sym.Get("uo_dict");
        if (tiUo == 0 || off == 0) return 0;

        nint sf = _resolver.StaticFields(tiUo);   // = _uo_sf(): [[base+uo_ti]+0xB8]
        if (sf == 0) return 0;
        nint d = _mem.ReadPtr(sf + (nint)off);
        if (!MemoryAccess.IsValidPointer(d)) return 0;

        nint ent = _mem.ReadPtr(d + 0x18);
        uint n = _mem.ReadU32(d + 0x20);
        if (!MemoryAccess.IsValidPointer(ent) || n == 0 || n > 4096) return 0;

        for (uint i = 0; i < n; i++)
        {
            nint a = ent + 0x20 + (nint)(i * 0x18);
            if (_mem.ReadU32(a + 8) == (uint)key) return _mem.ReadPtr(a + 0x10);
        }
        return 0;
    }

    /// <summary>
    /// Vai pra um estágio NORMAL. jgk é o passo final de entrada dos dois caminhos do jogo — precisa da
    /// main-thread (Call/cmd12). Guarda contra key inexistente: sem o StageCache, jgk lança KeyNotFound.
    /// </summary>
    public bool GoToStage(int key)
    {
        long jgk = _sym.Get("jgk");
        if (jgk == 0) return false;
        if (StageCache(key) == 0) return false;                 // key inexistente -> jgk crasharia
        _disp.Call((long)(Base + (nint)jgk), (nint)key);        // rcx = key
        return true;
    }

    /// <summary>
    /// Valida se um stage pode ser acessado. Builds antigas usam jgc:
    /// 0=Success, 1=EndStage, 2=NeedSoulStone, 3=NeedChestSpace, 4=Failed.
    /// Nas builds 139467f3ad72 e c265dc8bc7aa o antigo jgc está dividido; TaskHeroX usa o candidate-A somente para type=1,
    /// pois Success(0) e NeedSoulStone(2) foram validados ao vivo sem efeitos de gameplay observados.
    /// Type=2/type=3 continuam sem rota split exposta. null = rota não validada / key inexistente.
    /// </summary>
    public int? CanEnter(int key)
    {
        nint c = StageCache(key);
        if (c == 0) return null;

        long legacyJgc = _sym.Get("jgc");
        if (legacyJgc != 0)
            return _disp.Call((long)(Base + (nint)legacyJgc), c);

        if (!HasValidatedSplitType1Route) return null;
        var table = StageTable();
        if (!table.TryGetValue(key, out var info) || info.Type != 1) return null;

        return _disp.Call((long)(Base + (nint)SplitType1ValidatorRva), c);
    }

    /// <summary>
    /// Entra num ACTBOSS (x-10) reproduzindo o CLIQUE: jgd(cache,FAKE) + jgk(key).
    /// O PAR é obrigatório: o Action&lt;bool&gt; que o clique passa NÃO é cosmético — ele chama o jgk. jgd sozinho
    /// reservaria a soulstone (beyt) e NÃO carregaria a fase (cliente meio-feito); jgk sozinho não reserva a
    /// pedra nem grava o ponto de retorno (beyq). A pedra só é COBRADA quando o boss morre -> entrar é reversível.
    /// </summary>
    public bool EnterBoss(int key)
    {
        long jgd = _sym.Get("jgd"), jgk = _sym.Get("jgk");
        if (jgd == 0 || jgk == 0) return false;

        nint c = StageCache(key);
        if (c == 0) return false;

        bool callbackDrivenC265 =
            _sym.Get("uo_ti") == 0x5F5C9C8 &&
            _sym.Get("stage_off") == 0x88 &&
            _sym.Get("jgd") == 0x9AA370 &&
            _sym.Get("jgk") == 0x9ABC50;

        if (callbackDrivenC265)
        {
            // cmd13 cria/reutiliza o Action<bool> real do StageNode e
            // jvd chama hzs(true), que por sua vez chama jgk.
            return _disp.Command(13, c);
        }

        // Builds antigos preservam a rota já validada.
        _disp.Command(13, c);
        Thread.Sleep(150);
        _disp.Call((long)(Base + (nint)jgk), (nint)key);
        return true;
    }
}

/// <summary>Uma linha da tabela de estágios: NextStageKey, tipo (1=x-10/boss), soulstone, nível recomendado e nº de waves.</summary>
public sealed record StageInfo(int Next, int Type, int Ss, int Lvl, int Waves = 0);
