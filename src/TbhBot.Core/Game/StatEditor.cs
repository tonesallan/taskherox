using TaskHeroX.Core.Il2Cpp;
using TaskHeroX.Core.Memory;

namespace TaskHeroX.Core.Game;

/// <summary>
/// Editor de stats e dados do estágio.
///
/// Builds antigos:
///   AOB -> chain antiga.
///
/// Builds novos:
///   Stats: we.uu -> wg -> bam -> Dictionary<StatType,float>
///   Stage: uo_cur -> bal_ti -> stage_off -> StageInfoData.
///
/// Mantém fallback antigo para compatibilidade.
/// </summary>
public sealed class StatEditor(
    MemoryAccess mem,
    MemoryScanner scan,
    SymbolTable sym,
    Il2CppResolver resolver)
{
    private List<nint>? _stageSlots;
    private nint _stageSlot;
    private nint _statsSlot;

    private nint _bam;

    // StatType confirmado pelo dump da build 139467f3ad72.
    // Mantemos os mesmos 25 nomes que a interface já utiliza.
    private static readonly Dictionary<string, int> StatTypeKeys =
        new(StringComparer.Ordinal)
        {
            ["Attack Damage"]          = 1,
            ["Attack Speed"]           = 2,
            ["Critical Chance"]        = 3,
            ["Critical Damage"]        = 4,

            ["Cooldown Reduction"]     = 10,

            ["Cast Speed"]             = 49,

            ["Physical Damage"]        = 24,
            ["Fire Damage"]            = 25,
            ["Cold Damage"]            = 26,
            ["Lightning Damage"]       = 27,
            ["Chaos Damage"]           = 28,

            ["Max Hp"]                 = 5,
            ["Armor"]                  = 6,

            ["Dodge Chance"]           = 16,
            ["Block Chance"]           = 17,

            ["All Element Resistance"] = 52,

            ["Hp Regen /Sec"]          = 23,

            ["Dmg Absorption"]         = 40,
            ["Dmg Reduction"]          = 34,

            ["Movement Speed"]         = 7,

            ["Area of Effect %"]       = 8,
            ["Area of Effect Damage"]  = 55,

            ["Add HP/Kill"]            = 58,
            ["Life Leech"]             = 21,
            ["Skill Heal"]             = 50,
        };

    // ============================================================
    // STATS — NOVA ROTA
    // ============================================================

    /// <summary>
    /// Procura uma Entry de Dictionary&lt;StatType,float&gt;.
    ///
    /// Dictionary:
    ///   +0x18 entries[]
    ///   +0x20 count
    ///
    /// Entry:
    ///   +0x00 hashCode
    ///   +0x04 next
    ///   +0x08 StatType
    ///   +0x0C float
    /// </summary>
    private bool TryFindStatEntry(
        nint dict,
        int key,
        out nint entry)
    {
        entry = 0;

        if (!MemoryAccess.IsValidPointer(dict))
            return false;

        nint entries = mem.ReadPtr(dict + 0x18);

        if (!MemoryAccess.IsValidPointer(entries))
            return false;

        uint count = mem.ReadU32(dict + 0x20);
        uint capacity = mem.ReadU32(entries + 0x18);

        if (count == 0 ||
            count > 256 ||
            capacity == 0 ||
            capacity > 512)
            return false;

        int n = (int)capacity;

        for (int i = 0; i < n; i++)
        {
            nint a = entries + (nint)(0x20 + i * 0x10);

            int hash = mem.ReadI32(a);
            int k = mem.ReadI32(a + 0x08);

            if (hash < 0)
                continue;

            if (k != key)
                continue;

            entry = a;
            return true;
        }

        return false;
    }

    private bool LooksLikeStatsDictionary(nint dict)
    {
        if (!MemoryAccess.IsValidPointer(dict))
            return false;

        uint count = mem.ReadU32(dict + 0x20);

        // Build atual possui 65 StatTypes (0..64).
        // Deixa alguma folga para updates futuros.
        if (count is < 50 or > 128)
            return false;

        if (!TryFindStatEntry(dict, 1, out nint e1))
            return false;

        if (!TryFindStatEntry(dict, 2, out nint e2))
            return false;

        if (!TryFindStatEntry(dict, 64, out _))
            return false;

        float attack = mem.Read<float>(e1 + 0x0C);
        float speed  = mem.Read<float>(e2 + 0x0C);

        return float.IsFinite(attack) &&
               float.IsFinite(speed) &&
               attack >= 0 &&
               speed >= 0;
    }

    /// <summary>
    /// Resolve:
    ///
    /// AOB PSTAT
    /// -> TypeInfo we.uu
    /// -> static_fields
    /// -> +0x38 wg
    /// -> +0x10 bam
    ///
    /// Não hardcoda o RVA 0x5F4BAB8: usa o próprio AOB para
    /// continuar funcionando quando o TypeInfo mudar de RVA.
    /// </summary>
    private nint ResolveBam()
    {
        if (_bam != 0)
        {
            nint current =
                mem.ReadPtr(_bam + 0x20);

            if (LooksLikeStatsDictionary(current))
                return _bam;

            _bam = 0;
        }

        foreach (nint m in scan.FindAllAob(GameConstants.AobPstat))
        {
            uint rel = mem.ReadU32(m + 3);

            nint typeInfoSlot =
                m + 7 + (nint)rel;

            nint klass =
                mem.ReadPtr(typeInfoSlot);

            if (!MemoryAccess.IsValidPointer(klass))
                continue;

            nint sf =
                mem.ReadPtr(
                    klass +
                    GameConstants.StaticFieldsOff);

            if (!MemoryAccess.IsValidPointer(sf))
                continue;

            // we.uu static_fields +0x38 = wg ativo
            nint wg =
                mem.ReadPtr(sf + 0x38);

            if (!MemoryAccess.IsValidPointer(wg))
                continue;

            // wn.<bam> backing field = +0x10
            nint bam =
                mem.ReadPtr(wg + 0x10);

            if (!MemoryAccess.IsValidPointer(bam))
                continue;

            // bam+0x20 = Dictionary<StatType,float>
            // correspondente aos valores efetivos.
            nint effective =
                mem.ReadPtr(bam + 0x20);

            if (!LooksLikeStatsDictionary(effective))
                continue;

            _bam = bam;
            return bam;
        }

        return 0;
    }

    private Dictionary<string, double> ReadStatsDictionary()
    {
        var result =
            new Dictionary<string, double>();

        nint bam = ResolveBam();

        if (bam == 0)
            return result;

        // +0x18 e +0x20 são os dois caches encontrados.
        // +0x20 contém os valores efetivos (ex.: penalty de resistência).
        nint dict =
            mem.ReadPtr(bam + 0x20);

        if (!LooksLikeStatsDictionary(dict))
            return result;

        foreach (var (name, key) in StatTypeKeys)
        {
            if (!TryFindStatEntry(dict, key, out nint entry))
                continue;

            float value =
                mem.Read<float>(entry + 0x0C);

            if (!float.IsFinite(value))
                continue;

            result[name] = value;
        }

        return result;
    }

    private bool ApplyStatsDictionary(
        IReadOnlyDictionary<string, double> stats)
    {
        nint bam = ResolveBam();

        if (bam == 0)
            return false;

        bool touched = false;

        foreach (var (name, value) in stats)
        {
            if (!StatTypeKeys.TryGetValue(name, out int key))
                continue;

            float v = (float)value;

            if (!float.IsFinite(v))
                continue;

            bool wroteThisStat = false;

            // Atualiza os dois caches do bam.
            // O jogo recalcula esses valores; o AutomationLoop já reaplica
            // WantStats continuamente, como acontecia no editor antigo.
            foreach (int dictOff in new[] { 0x18, 0x20 })
            {
                nint dict =
                    mem.ReadPtr(bam + dictOff);

                if (!TryFindStatEntry(
                        dict,
                        key,
                        out nint entry))
                    continue;

                if (mem.Write<float>(
                        entry + 0x0C,
                        v))
                {
                    wroteThisStat = true;
                }
            }

            if (!wroteThisStat)
                return false;

            touched = true;
        }

        return touched;
    }

    // ============================================================
    // STATS — FALLBACK ANTIGO
    // ============================================================

    private nint StatsObjOld()
    {
        if (_statsSlot == 0)
            _statsSlot =
                scan.FindAob(GameConstants.AobPstat);

        nint slot = _statsSlot;

        if (slot == 0)
            return 0;

        uint rel =
            mem.ReadU32(slot + 3);

        nint p =
            (nint)mem.ReadU64(
                slot + 7 + (nint)rel);

        foreach (int off in GameConstants.PStatChain)
        {
            if (!MemoryAccess.IsValidPointer(p))
                return 0;

            p =
                (nint)mem.ReadU64(p + off);
        }

        return MemoryAccess.IsValidPointer(p)
            ? p
            : 0;
    }

    public Dictionary<string, double> ReadStats()
    {
        var modern =
            ReadStatsDictionary();

        if (modern.Count == StatTypeKeys.Count)
            return modern;

        // Fallback para builds antigos.
        var old =
            new Dictionary<string, double>();

        nint p = StatsObjOld();

        if (p == 0)
            return modern;

        foreach (var (name, (off, typ)) in GameConstants.Stats)
        {
            old[name] =
                typ == 'd'
                    ? mem.Read<double>(p + off)
                    : mem.Read<float>(p + off);
        }

        return old;
    }

    public bool ApplyStats(
        IReadOnlyDictionary<string, double> stats)
    {
        if (stats.Count == 0)
            return true;

        if (ApplyStatsDictionary(stats))
            return true;

        // Fallback antigo.
        nint p = StatsObjOld();

        if (p == 0)
        {
            _statsSlot = 0;
            return false;
        }

        foreach (var (name, val) in stats)
        {
            if (!GameConstants.Stats.TryGetValue(
                    name,
                    out var t))
                continue;

            if (t.Type == 'd')
                mem.Write<double>(
                    p + t.Off,
                    val);
            else
                mem.Write<float>(
                    p + t.Off,
                    (float)val);
        }

        return true;
    }

    // ============================================================
    // STAGE — NOVA ROTA
    // ============================================================

    /// <summary>
    /// Resolve o StageInfoData atual por:
    ///
    /// uo_cur -> StageKey atual
    /// bal_ti -> balance singleton
    /// stage_off -> List<StageInfoData>
    ///
    /// É a mesma tabela já usada por StageNav.
    /// </summary>
    private nint StageObjViaTable()
    {
        long uoTi =
            sym.Get("uo_ti");

        long uoCur =
            sym.Get("uo_cur");

        long balTi =
            sym.Get("bal_ti");

        long stageOff =
            sym.Get("stage_off");

        if (uoTi == 0 ||
            uoCur == 0 ||
            balTi == 0 ||
            stageOff == 0)
            return 0;

        nint uoSf =
            resolver.StaticFields(uoTi);

        if (uoSf == 0)
            return 0;

        int? current =
            ObscuredValue.ReadInt(
                mem,
                uoSf + (nint)uoCur);

        if (current is null ||
            current <= 0)
            return 0;

        nint balSf =
            resolver.StaticFields(balTi);

        if (balSf == 0)
            return 0;

        // StaticFields retorna o bloco de static fields.
        // O singleton balanceado está no primeiro ponteiro desse bloco.
        nint bal =
            mem.ReadPtr(balSf);

        if (!MemoryAccess.IsValidPointer(bal))
            return 0;

        nint list =
            mem.ReadPtr(
                bal + (nint)stageOff);

        if (!MemoryAccess.IsValidPointer(list))
            return 0;

        nint arr =
            mem.ReadPtr(list + 0x10);

        uint count =
            mem.ReadU32(list + 0x18);

        if (!MemoryAccess.IsValidPointer(arr) ||
            count == 0 ||
            count > 500)
            return 0;

        ulong[] items =
            mem.ReadArray<ulong>(
                arr + 0x20,
                (int)count);

        foreach (ulong raw in items)
        {
            nint obj = (nint)raw;

            if (!MemoryAccess.IsValidPointer(obj))
                continue;

            int key =
                mem.ReadI32(obj + 0x30);

            if (key != current.Value)
                continue;

            return Sane(obj)
                ? obj
                : 0;
        }

        return 0;
    }

    // ============================================================
    // STAGE — FALLBACK ANTIGO
    // ============================================================

    private nint StageDataOf(nint slot)
    {
        nint p =
            (nint)mem.ReadU64(slot);

        foreach (int off in GameConstants.StageChain)
        {
            if (!MemoryAccess.IsValidPointer(p))
                return 0;

            p =
                (nint)mem.ReadU64(p + off);
        }

        return MemoryAccess.IsValidPointer(p)
            ? p
            : 0;
    }

    private bool Sane(nint sd)
    {
        uint a =
            mem.ReadU32(sd + 0x48);

        uint s =
            mem.ReadU32(sd + 0x4C);

        uint l =
            mem.ReadU32(sd + 0x50);

        uint w =
            mem.ReadU32(sd + 0x54);

        uint mo =
            mem.ReadU32(sd + 0x58);

        return
            a is >= 1 and <= 12 &&
            s is >= 1 and <= 40 &&
            l is >= 1 and <= 300 &&
            w is >= 1 and <= 80 &&
            mo is >= 1 and <= 400;
    }

    private nint StageObjOld()
    {
        if (_stageSlots is null)
        {
            _stageSlots =
                new List<nint>();

            foreach (nint m in
                     scan.FindAllAob(
                         GameConstants.AobStage))
            {
                uint rel =
                    mem.ReadU32(m + 3);

                _stageSlots.Add(
                    m + 7 + (nint)rel);
            }
        }

        if (_stageSlot != 0)
        {
            nint sd0 =
                StageDataOf(_stageSlot);

            if (sd0 != 0 &&
                Sane(sd0))
                return sd0;

            _stageSlot = 0;
        }

        foreach (nint slot in _stageSlots)
        {
            nint sd =
                StageDataOf(slot);

            if (sd != 0 &&
                Sane(sd))
            {
                _stageSlot = slot;
                return sd;
            }
        }

        return 0;
    }

    private nint StageObj()
    {
        nint modern =
            StageObjViaTable();

        if (modern != 0)
            return modern;

        return StageObjOld();
    }

    public Dictionary<string, int> ReadStage()
    {
        var result =
            new Dictionary<string, int>();

        nint sd =
            StageObj();

        if (sd == 0)
            return result;

        foreach (var (name, off) in
                 GameConstants.StageFields)
        {
            result[name] =
                mem.ReadI32(sd + off);
        }

        return result;
    }

    public bool ApplyStage(
        IReadOnlyDictionary<string, int> fields)
    {
        if (fields.Count == 0)
            return true;

        nint sd =
            StageObj();

        if (sd == 0)
        {
            _stageSlot = 0;
            _stageSlots = null;
            return false;
        }

        foreach (var (name, value) in fields)
        {
            if (!GameConstants.StageFields.TryGetValue(
                    name,
                    out int off))
                continue;

            mem.Write<int>(
                sd + off,
                value);
        }

        return true;
    }
}
