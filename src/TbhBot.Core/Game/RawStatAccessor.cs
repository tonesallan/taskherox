using TbhBot.Core.Memory;

namespace TbhBot.Core.Game;

/// <summary>
/// Acesso controlado a StatType que ainda não fazem parte dos 25 campos do editor principal.
/// Usa a mesma rota validada do StatEditor (we.uu -> wg -> bam -> Dictionary&lt;StatType,float&gt;),
/// mas sem alterar o contrato ReadStats()/ApplyStats() já coberto pelo E2E 19/19.
/// </summary>
public sealed class RawStatAccessor(MemoryAccess mem, MemoryScanner scan)
{
    private nint _bam;

    private bool TryFindEntry(nint dict, int key, out nint entry)
    {
        entry = 0;
        if (!MemoryAccess.IsValidPointer(dict)) return false;

        nint entries = mem.ReadPtr(dict + 0x18);
        if (!MemoryAccess.IsValidPointer(entries)) return false;

        uint count = mem.ReadU32(dict + 0x20);
        uint capacity = mem.ReadU32(entries + 0x18);
        if (count == 0 || count > 256 || capacity == 0 || capacity > 512) return false;

        for (int i = 0; i < (int)capacity; i++)
        {
            nint a = entries + (nint)(0x20 + i * 0x10);
            int hash = mem.ReadI32(a);
            int k = mem.ReadI32(a + 0x08);
            if (hash >= 0 && k == key)
            {
                entry = a;
                return true;
            }
        }
        return false;
    }

    private bool LooksLikeStatsDictionary(nint dict)
    {
        if (!MemoryAccess.IsValidPointer(dict)) return false;
        uint count = mem.ReadU32(dict + 0x20);
        if (count is < 50 or > 128) return false;
        return TryFindEntry(dict, 1, out _) &&
               TryFindEntry(dict, 2, out _) &&
               TryFindEntry(dict, 64, out _);
    }

    private nint ResolveBam()
    {
        if (_bam != 0)
        {
            nint effective = mem.ReadPtr(_bam + 0x20);
            if (LooksLikeStatsDictionary(effective)) return _bam;
            _bam = 0;
        }

        foreach (nint m in scan.FindAllAob(GameConstants.AobPstat))
        {
            uint rel = mem.ReadU32(m + 3);
            nint typeInfoSlot = m + 7 + (nint)rel;
            nint klass = mem.ReadPtr(typeInfoSlot);
            if (!MemoryAccess.IsValidPointer(klass)) continue;

            nint sf = mem.ReadPtr(klass + GameConstants.StaticFieldsOff);
            if (!MemoryAccess.IsValidPointer(sf)) continue;

            nint wg = mem.ReadPtr(sf + 0x38);
            if (!MemoryAccess.IsValidPointer(wg)) continue;

            nint bam = mem.ReadPtr(wg + 0x10);
            if (!MemoryAccess.IsValidPointer(bam)) continue;

            nint effective = mem.ReadPtr(bam + 0x20);
            if (!LooksLikeStatsDictionary(effective)) continue;

            _bam = bam;
            return bam;
        }

        return 0;
    }

    public double? Read(int statType)
    {
        if (statType is < 0 or > 127) return null;
        nint bam = ResolveBam();
        if (bam == 0) return null;

        nint dict = mem.ReadPtr(bam + 0x20);
        if (!TryFindEntry(dict, statType, out nint entry)) return null;

        float value = mem.Read<float>(entry + 0x0C);
        return float.IsFinite(value) ? value : null;
    }

    public bool Write(int statType, double value)
    {
        if (statType is < 0 or > 127) return false;
        float v = (float)value;
        if (!float.IsFinite(v)) return false;

        nint bam = ResolveBam();
        if (bam == 0) return false;

        bool wrote = false;
        foreach (int dictOff in new[] { 0x18, 0x20 })
        {
            nint dict = mem.ReadPtr(bam + dictOff);
            if (!TryFindEntry(dict, statType, out nint entry)) continue;
            wrote |= mem.Write<float>(entry + 0x0C, v);
        }
        return wrote;
    }
}
