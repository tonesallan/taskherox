using TbhBot.Core.Il2Cpp;
using TbhBot.Core.Memory;

namespace TbhBot.Core.Game;

/// <summary>
/// Leitura/escrita de dados de save/runtime: progresso de estagio, nivel do cubo,
/// runas (client-side) e contagem de inventario. Porta stage_progress/set_maxstage,
/// cube_level/set_cube_level, read_runes/set_rune e read_inventory do tbh_core.py.
///
/// AVISO (do Python): escrever os ObscuredInt runtime (uo_max / cube bese) dispara o
/// honesty-check periodico do ACTk (~12s) e o jogo fecha sozinho, MAS o valor persiste
/// (auto-save) e recarrega limpo. Comportamento fiel ao original.
/// </summary>
public sealed class SaveData(
    MemoryAccess mem,
    SymbolTable sym,
    Il2CppResolver resolver)
{
    private const int CubeLevelOffFallback = 0x1CC;
    private int CubeLevelOff => (int)(sym.Has("cube_level_off") ? sym.Get("cube_level_off") : CubeLevelOffFallback);
    public const int StageMaxKey = 4310;
    public const int CubeMaxLevel = 100;

    private nint UoStaticFields()
        => sym.Has("uo_ti") ? resolver.StaticFields(sym.Get("uo_ti")) : 0;

    private nint CubeStaticFields()
    {
        long cs = sym.Get("cube_slot");
        if (cs == 0) { cs = resolver.ResolveCubeSlot(); if (cs != 0) sym["cube_slot"] = cs; }
        return cs != 0 ? resolver.StaticFields(cs) : 0;
    }

    // ---------------- PROGRESSO DE ESTAGIO ----------------

    /// <summary>
    /// (maxCompletedStage, currentStageKey, wave). A fonte runtime uo_* e autoritativa enquanto valida.
    /// CommonSaveData e apenas fallback: na build 139467f3ad72 foi observado que currentStageKey do save
    /// pode mudar antes do carregamento real da fase e depois voltar, portanto nao serve para confirmar
    /// navegacao concluida.
    /// </summary>
    public (int Max, int Cur, int Wave) StageProgress()
    {
        var src = StageProgressSources();
        int max = src.RuntimeMax > 0 ? src.RuntimeMax : src.SaveMax;
        int cur = src.RuntimeCur > 0 ? src.RuntimeCur : src.SaveCur;
        int wave = src.RuntimeWave >= 0 ? src.RuntimeWave : src.SaveWave;
        return (max, cur, wave);
    }

    /// <summary>
    /// Expoe as duas fontes de progresso para diagnostico. Runtime representa a fase realmente carregada;
    /// CommonSaveData pode refletir uma solicitacao/transicao ainda nao efetivada.
    /// </summary>
    public (int RuntimeMax, int RuntimeCur, int RuntimeWave, int SaveMax, int SaveCur, int SaveWave) StageProgressSources()
    {
        nint sf = UoStaticFields();
        int G(string key)
        {
            if (sf == 0) return -1;
            long o = sym.Get(key);
            if (o == 0) return -1;
            return ObscuredValue.ReadInt(mem, sf + (nint)o) ?? -1;
        }

        int runtimeMax = G("uo_max");
        int runtimeCur = G("uo_cur");
        int runtimeWave = G("uo_wave");
        int saveMax = -1, saveCur = -1, saveWave = -1;

        try
        {
            nint psd = resolver.ResolvePsd();
            if (psd != 0)
            {
                nint commonOff = (nint)sym.Get("psd_common_off", 0x10);
                nint csd = mem.ReadPtr(psd + commonOff);
                if (MemoryAccess.IsValidPointer(csd))
                {
                    long maxOff = sym.Get("commonsave_maxstage", 0x5C);
                    long curOff = sym.Get("commonsave_curstage", 0x64);
                    long waveOff = sym.Get("CommonSaveData.currentStageWave", 0x68);
                    if (maxOff != 0) saveMax = mem.ReadI32(csd + (nint)maxOff);
                    if (curOff != 0) saveCur = mem.ReadI32(csd + (nint)curOff);
                    if (waveOff != 0) saveWave = mem.ReadI32(csd + (nint)waveOff);
                }
            }
        }
        catch { /* diagnostico best-effort */ }

        return (runtimeMax, runtimeCur, runtimeWave, saveMax, saveCur, saveWave);
    }

    /// <summary>
    /// Desbloqueia estagios ate `value` (default 4310). Escreve o ObscuredInt runtime uo_max
    /// (autoritativo) + espelha no int do save (CommonSaveData.maxCompletedStage) por consistencia.
    /// </summary>
    public (bool Ok, int Value) SetMaxStage(int value = StageMaxKey)
    {
        nint sf = UoStaticFields();
        long off = sym.Get("uo_max");
        if (sf == 0 || off == 0) return (false, value);
        bool ok = ObscuredValue.WriteInt(mem, sf + (nint)off, value);
        try
        {
            nint psd = resolver.ResolvePsd();
            if (psd != 0)
            {
                nint csd = mem.ReadPtr(psd + 0x10);
                if (MemoryAccess.IsValidPointer(csd))
                    mem.Write<int>(csd + (nint)sym.Get("commonsave_maxstage", 0x5C), value);
            }
        }
        catch { }
        return (ok, value);
    }

    // ---------------- CUBO ----------------

    public int? CubeLevel()
    {
        nint sf = CubeStaticFields();
        return sf == 0 ? null : ObscuredValue.ReadInt(mem, sf + CubeLevelOff);
    }

    public (bool Ok, int Level) SetCubeLevel(int level = CubeMaxLevel)
    {
        nint sf = CubeStaticFields();
        if (sf == 0) return (false, level);
        int? cur = ObscuredValue.ReadInt(mem, sf + CubeLevelOff);
        if (cur is null or < 0 or > CubeMaxLevel + 50) return (false, level);
        return (ObscuredValue.WriteInt(mem, sf + CubeLevelOff, level), level);
    }

    // ---------------- RUNAS ----------------

    public Dictionary<int, int> ReadRunes()
    {
        var outd = new Dictionary<int, int>();
        nint psd = resolver.ResolvePsd();
        if (psd == 0) return outd;
        nint lst = mem.ReadPtr(psd + (nint)sym.Get("PlayerSaveData.RuneSaveData", GameConstants.RuneListOff));
        if (lst == 0) return outd;
        nint arr = mem.ReadPtr(lst + 0x10);
        uint size = mem.ReadU32(lst + 0x18);
        if (arr == 0 || size >= 100000) return outd;
        ulong[] elems = mem.ReadArray<ulong>(arr + 0x20, (int)size);
        foreach (ulong re in elems)
        {
            nint r = (nint)re;
            if (r == 0) continue;
            outd[mem.ReadI32(r + 0x10)] = mem.ReadI32(r + 0x14);
        }
        return outd;
    }

    public bool SetRune(int key, int level)
    {
        level = Math.Max(0, level);
        nint psd = resolver.ResolvePsd();
        if (psd == 0) return false;
        nint lst = mem.ReadPtr(psd + (nint)sym.Get("PlayerSaveData.RuneSaveData", GameConstants.RuneListOff));
        if (lst == 0) return false;
        nint arr = mem.ReadPtr(lst + 0x10);
        uint size = mem.ReadU32(lst + 0x18);
        if (arr == 0 || size == 0) return false;
        for (int i = 0; i < size; i++)
        {
            nint r = mem.ReadPtr(arr + 0x20 + i * 8);
            if (r != 0 && mem.ReadI32(r + 0x10) == key)
            {
                mem.Write<int>(r + 0x14, level);
                return true;
            }
        }
        return false;
    }

    // ---------------- INVENTARIO ----------------

    public int InventoryCount()
    {
        nint psd = resolver.ResolvePsd();
        if (psd == 0) return 0;
        nint invOff = (nint)sym.Get("inv_list_off", GameConstants.InvListOff);
        nint lst = mem.ReadPtr(psd + invOff);
        if (lst == 0) return 0;
        nint arr = mem.ReadPtr(lst + 0x10);
        uint size = mem.ReadU32(lst + 0x18);
        if (arr == 0 || size >= 500000) return 0;
        ulong[] elems = mem.ReadArray<ulong>(arr + 0x20, (int)size);
        int n = 0;
        foreach (ulong it in elems)
            if (it != 0) n++;
        return n;
    }
}
