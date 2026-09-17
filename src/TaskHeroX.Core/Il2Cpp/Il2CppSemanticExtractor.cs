using System.Text.RegularExpressions;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Símbolos estáticos de stage resolvidos apenas pela estrutura do dump/script, sem disassembly
/// e sem qualquer alteração de runtime.
/// </summary>
public sealed record StageStaticSymbols(
    long UoTypeInfo,
    long UoDictionaryOffset,
    long? UoCurrentCacheOffset);

/// <summary>
/// Offsets de slots de inventário/stash encontrados semanticamente em PlayerSaveData (ou classe
/// equivalente), sem depender de nomes de campos ofuscados.
/// </summary>
public sealed record InventorySlotOffsets(
    long InventorySlotsOffset,
    long StashSlotsOffset,
    string DeclaringClass);

/// <summary>
/// Metadados do singleton de inventário e da lista mestre de itens. O resultado preserva as duas
/// rotas do legado: TypeInfo da classe concreta e TypeInfo da base genérica, exigindo ao menos uma.
/// </summary>
public sealed record InventoryRootSymbols(
    string InventoryClass,
    long PlayerSaveDataOffset,
    long ItemSaveDataListOffset,
    long? InventoryClassTypeInfo,
    long? GenericSingletonTypeInfo);

/// <summary>
/// Grupos semânticos portados do extrator legado. Mantém comportamento fail-closed e não altera
/// qualquer rota runtime existente.
/// </summary>
public static class Il2CppSemanticExtractor
{
    private static readonly Regex StageCacheListType = new(
        @"^List<[\w\.]*\.?StageCache>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StageCacheDictionaryType = new(
        @"^Dictionary<int,\s*[\w\.]*\.?StageCache>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StageCacheType = new(
        @"^[\w\.]*\.?StageCache$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InventorySaveListType = new(
        @"^List<[\w\.]*\.?InventorySaveData>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StashSaveListType = new(
        @"^List<[\w\.]*\.?StashSaveData>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PlayerSaveDataType = new(
        @"^[\w\.]*\.?PlayerSaveData$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ItemSaveDataListType = new(
        @"^List<[\w\.]*\.?ItemSaveData>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryExtractStageStaticSymbols(
        IReadOnlyList<Il2CppDumpClass> classes,
        Il2CppScriptIndex script,
        out StageStaticSymbols? symbols,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(script);

        Il2CppDumpClass[] candidates = classes
            .Where(IsStageStaticClass)
            .ToArray();

        if (candidates.Length != 1)
        {
            symbols = null;
            error = $"stage static class ambiguous ({candidates.Length})";
            return false;
        }

        Il2CppDumpClass stageClass = candidates[0];
        if (!script.TryGetMetadataAddress(stageClass.Name + "_TypeInfo", out long typeInfo))
        {
            symbols = null;
            error = $"missing TypeInfo for {stageClass.Name}";
            return false;
        }

        Il2CppDumpField? dictionaryField = stageClass.Fields
            .FirstOrDefault(field => field.IsStatic && StageCacheDictionaryType.IsMatch(field.Type));

        if (dictionaryField is null)
        {
            symbols = null;
            error = "stage dictionary field missing";
            return false;
        }

        Il2CppDumpField? currentCacheField = stageClass.Fields
            .FirstOrDefault(field => field.IsStatic && StageCacheType.IsMatch(field.Type));

        symbols = new StageStaticSymbols(
            typeInfo,
            dictionaryField.Offset,
            currentCacheField?.Offset);
        error = null;
        return true;
    }

    public static bool TryExtractInventorySlotOffsets(
        IReadOnlyList<Il2CppDumpClass> classes,
        out InventorySlotOffsets? offsets,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);

        var perClass = classes
            .Select(klass => new
            {
                Class = klass,
                Inventory = klass.Fields
                    .Where(field => !field.IsStatic && InventorySaveListType.IsMatch(field.Type))
                    .ToArray(),
                Stash = klass.Fields
                    .Where(field => !field.IsStatic && StashSaveListType.IsMatch(field.Type))
                    .ToArray(),
            })
            .ToArray();

        var pairedOwners = perClass
            .Where(entry => entry.Inventory.Length > 0 && entry.Stash.Length > 0)
            .ToArray();

        if (pairedOwners.Length == 1)
        {
            var owner = pairedOwners[0];

            if (owner.Inventory.Length != 1)
            {
                offsets = null;
                error = $"inventory slot field ambiguous ({owner.Inventory.Length})";
                return false;
            }

            if (owner.Stash.Length != 1)
            {
                offsets = null;
                error = $"stash slot field ambiguous ({owner.Stash.Length})";
                return false;
            }

            offsets = new InventorySlotOffsets(
                owner.Inventory[0].Offset,
                owner.Stash[0].Offset,
                owner.Class.Name);
            error = null;
            return true;
        }

        if (pairedOwners.Length > 1)
        {
            offsets = null;
            error = $"inventory/stash owner ambiguous ({pairedOwners.Length})";
            return false;
        }

        int inventoryCount = perClass.Sum(entry => entry.Inventory.Length);
        int stashCount = perClass.Sum(entry => entry.Stash.Length);

        if (inventoryCount != 1)
        {
            offsets = null;
            error = $"inventory slot field ambiguous ({inventoryCount})";
            return false;
        }

        if (stashCount != 1)
        {
            offsets = null;
            error = $"stash slot field ambiguous ({stashCount})";
            return false;
        }

        offsets = null;
        error = "inventory and stash fields belong to different classes";
        return false;
    }

    public static bool TryExtractInventoryRootSymbols(
        IReadOnlyList<Il2CppDumpClass> classes,
        Il2CppScriptIndex script,
        out InventoryRootSymbols? symbols,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(script);

        var playerSaveMatches = classes
            .SelectMany(klass => klass.Fields
                .Where(field => !field.IsStatic && PlayerSaveDataType.IsMatch(field.Type))
                .Select(field => (Class: klass, Field: field)))
            .ToArray();

        if (playerSaveMatches.Length != 1)
        {
            symbols = null;
            error = $"PlayerSaveData owner ambiguous ({playerSaveMatches.Length})";
            return false;
        }

        Il2CppDumpClass inventoryClass = playerSaveMatches[0].Class;
        string escapedClass = Regex.Escape(inventoryClass.Name);
        var selfSingletonPattern = new Regex(
            @":\s*[\w\.]+<" + escapedClass + @">(?:\s|$)",
            RegexOptions.CultureInvariant);

        if (!selfSingletonPattern.IsMatch(inventoryClass.Declaration))
        {
            symbols = null;
            error = $"inventory owner {inventoryClass.Name} is not a self-generic singleton";
            return false;
        }

        var itemListMatches = classes
            .SelectMany(klass => klass.Fields
                .Where(field => !field.IsStatic && ItemSaveDataListType.IsMatch(field.Type))
                .Select(field => (Class: klass, Field: field)))
            .ToArray();

        if (itemListMatches.Length != 1)
        {
            symbols = null;
            error = $"ItemSaveData list ambiguous ({itemListMatches.Length})";
            return false;
        }

        long? concreteTypeInfo = script.TryGetMetadataAddress(
            inventoryClass.Name + "_TypeInfo",
            out long concreteAddress)
            ? concreteAddress
            : null;

        Regex genericTypeInfoPattern = new(
            @"^[\w\.]+<" + escapedClass + @">_TypeInfo$",
            RegexOptions.CultureInvariant);

        var genericMatches = script.MetadataAddresses
            .Where(entry => genericTypeInfoPattern.IsMatch(entry.Key))
            .ToArray();

        if (genericMatches.Length > 1)
        {
            symbols = null;
            error = $"generic inventory TypeInfo ambiguous ({genericMatches.Length})";
            return false;
        }

        long? genericTypeInfo = genericMatches.Length == 1
            ? genericMatches[0].Value
            : null;

        if (concreteTypeInfo is null && genericTypeInfo is null)
        {
            symbols = null;
            error = "inventory TypeInfo missing";
            return false;
        }

        symbols = new InventoryRootSymbols(
            inventoryClass.Name,
            playerSaveMatches[0].Field.Offset,
            itemListMatches[0].Field.Offset,
            concreteTypeInfo,
            genericTypeInfo);
        error = null;
        return true;
    }

    private static bool IsStageStaticClass(Il2CppDumpClass klass)
    {
        if (!klass.Declaration.Contains(" static class ", StringComparison.Ordinal))
            return false;

        bool hasList = klass.Fields.Any(field =>
            field.IsStatic && StageCacheListType.IsMatch(field.Type));

        bool hasDictionary = klass.Fields.Any(field =>
            field.IsStatic && StageCacheDictionaryType.IsMatch(field.Type));

        return hasList && hasDictionary;
    }
}
