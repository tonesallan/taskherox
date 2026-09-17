namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Compõe os grupos já portados em um único resultado intermediário. A aceitação final continua sob
/// responsabilidade de <see cref="Il2CppOffsetCache.TryValidate"/>; neste estágio falta o anchor
/// disassembly-backed de <c>iuw</c> e outros símbolos opcionais/runtime.
/// </summary>
public static class Il2CppFoundationExtractor
{
    public static bool TryExtract(
        IReadOnlyList<Il2CppDumpClass> classes,
        Il2CppScriptIndex script,
        Il2CppPeImage image,
        out Il2CppExtractedOffsets? offsets,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(image);

        var result = new Il2CppExtractedOffsets();

        if (!Il2CppSemanticExtractor.TryExtractStageStaticSymbols(
                classes, script, out StageStaticSymbols? stageStatic, out error) || stageStatic is null)
        {
            offsets = null;
            return false;
        }
        result.Symbols["uo_ti"] = stageStatic.UoTypeInfo;
        result.Symbols["uo_dict"] = stageStatic.UoDictionaryOffset;
        if (stageStatic.UoCurrentCacheOffset is long currentCache)
            result.Symbols["uo_cur_cache"] = currentCache;

        if (!Il2CppStageTableExtractor.TryExtract(
                classes, script, out StageTableSymbols? stageTable, out error) || stageTable is null)
        {
            offsets = null;
            return false;
        }
        result.Symbols["bal_ti"] = stageTable.BalanceTypeInfo;
        result.Symbols["stage_off"] = stageTable.StageListOffset;

        if (!Il2CppSemanticExtractor.TryExtractInventorySlotOffsets(
                classes, out InventorySlotOffsets? inventorySlots, out error) || inventorySlots is null)
        {
            offsets = null;
            return false;
        }
        result.Symbols["inv_slots_off"] = inventorySlots.InventorySlotsOffset;
        result.Symbols["stash_off"] = inventorySlots.StashSlotsOffset;

        if (!Il2CppSemanticExtractor.TryExtractInventoryRootSymbols(
                classes, script, out InventoryRootSymbols? inventoryRoot, out error) || inventoryRoot is null)
        {
            offsets = null;
            return false;
        }
        result.InvClass = inventoryRoot.InventoryClass;
        result.Symbols["inv_psd_off"] = inventoryRoot.PlayerSaveDataOffset;
        result.Symbols["inv_list_off"] = inventoryRoot.ItemSaveDataListOffset;
        if (inventoryRoot.InventoryClassTypeInfo is long concreteTypeInfo)
            result.Symbols["inv_klass_ti"] = concreteTypeInfo;
        if (inventoryRoot.GenericSingletonTypeInfo is long genericTypeInfo)
            result.Symbols["bau_ti"] = genericTypeInfo;

        foreach ((string key, long value) in Il2CppDataFieldExtractor.Extract(classes))
            result.Symbols[key] = value;

        if (!Il2CppCriticalTextAnchorExtractor.TryExtract(
                classes, out Il2CppCriticalTextAnchors? critical, out error) || critical is null)
        {
            offsets = null;
            return false;
        }
        foreach ((string key, long value) in critical.Symbols)
            result.Symbols[key] = value;
        result.RaClass = critical.MoveManagerClass;

        if (!Il2CppMethodAnchorExtractor.TryExtractItemInfoGetter(classes, out long izb))
        {
            offsets = null;
            error = "izb anchor missing";
            return false;
        }
        result.Symbols["izb"] = izb;

        if (!Il2CppMethodAnchorExtractor.TryExtractMonsterDamage(classes, out long gra, out error))
        {
            offsets = null;
            return false;
        }
        result.Symbols["gra"] = gra;

        if (!Il2CppStageHubExtractor.TryExtract(
                classes, script, image, out StageHubSymbols? stageHub, out error) || stageHub is null)
        {
            offsets = null;
            return false;
        }
        result.Symbols["jgk"] = stageHub.Jgk;
        result.Symbols["jgq"] = stageHub.Jgq;
        result.Symbols["jgd"] = stageHub.Jgd;
        result.Symbols["jgc_type13"] = stageHub.JgcType13;
        if (stageHub.JgcType2 is long type2)
            result.Symbols["jgc_type2"] = type2;

        if (result.Symbols.ContainsKey("jgc"))
        {
            offsets = null;
            error = "generic jgc is forbidden";
            return false;
        }

        offsets = result;
        error = null;
        return true;
    }
}
