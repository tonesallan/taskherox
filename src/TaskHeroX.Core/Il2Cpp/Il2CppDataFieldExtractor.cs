using System.Text.RegularExpressions;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Porta os anchors de campos de dados do extrator legado. Esses campos pertencem a classes
/// serializadas por nome (InfoData/SaveData), nas quais os nomes relevantes permanecem estáveis
/// entre builds mesmo quando classes/métodos auxiliares são ofuscados.
/// </summary>
public static class Il2CppDataFieldExtractor
{
    private static readonly (string Symbol, string ClassName, string FieldName)[] NamedFields =
    [
        ("recipe_minlvl", "SynthesisRecipeInfoData", "MinResultLevel"),
        ("recipe_maxlvl", "SynthesisRecipeInfoData", "MaxResultLevel"),
        ("iteminfo_type", "ItemInfoData", "ITEMTYPE"),
        ("iteminfo_grade", "ItemInfoData", "GRADE"),
        ("iteminfo_synth", "ItemInfoData", "ItemSynthesisType"),
        ("iteminfo_level", "ItemInfoData", "Level"),
        ("psd_common_off", "PlayerSaveData", "commonSaveData"),
        ("commonsave_usestorage", "CommonSaveData", "useStorage"),
        ("commonsave_curstage", "CommonSaveData", "currentStageKey"),
        ("commonsave_maxstage", "CommonSaveData", "maxCompletedStage"),
        ("itemsave_key", "ItemSaveData", "ItemKey"),
        ("itemsave_uid", "ItemSaveData", "ItemUniqueId"),
    ];

    private static readonly HashSet<string> DataClasses = new(StringComparer.Ordinal)
    {
        "StageInfoData", "ItemInfoData", "SynthesisRecipeInfoData", "CraftingRecipeInfoData",
        "CommonSaveData", "PlayerSaveData", "ItemSaveData", "InventorySaveData", "StashSaveData",
        "HeroSaveData", "AccountSaveData", "SettingSaveData", "HeroInfoData", "GearInfoData",
        "MonsterInfoData", "SkillInfoData", "GradeInfoData", "DropInfoData",
        "SynthesisDropInfoData", "CurrencyInfoData", "PetInfoData", "AttributeInfoData",
        "GearTypeInfoData", "ItemLevelScaleInfoData", "ItemTypeScaleInfoData",
        "GearTypeScaleInfoData", "CubeRecipeInfoData", "ExtractionCostInfoData", "CubeInData",
    };

    private static readonly Regex ObfuscatedFieldName = new(
        @"^[a-z]{1,6}\d?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Extrai os aliases curtos usados diretamente pelo runtime legado e, adicionalmente, todos os
    /// campos em claro das classes de dados como "Classe.Campo". Campos ausentes são simplesmente
    /// omitidos, preservando a semântica best-effort de _data_anchors.
    /// </summary>
    public static IReadOnlyDictionary<string, long> Extract(IReadOnlyList<Il2CppDumpClass> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);

        // Equivalente a {k.name:k for k in classes}: em nomes duplicados, a última declaração vence.
        var byName = new Dictionary<string, Il2CppDumpClass>(StringComparer.Ordinal);
        foreach (Il2CppDumpClass klass in classes)
            byName[klass.Name] = klass;

        var result = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach ((string symbol, string className, string fieldName) in NamedFields)
        {
            if (!byName.TryGetValue(className, out Il2CppDumpClass? klass))
                continue;

            Il2CppDumpField? field = klass.Fields
                .FirstOrDefault(candidate => !candidate.IsStatic &&
                                             string.Equals(candidate.Name, fieldName, StringComparison.Ordinal));

            if (field is not null)
                result[symbol] = field.Offset;
        }

        foreach ((string className, Il2CppDumpClass klass) in byName)
        {
            if (!DataClasses.Contains(className))
                continue;

            foreach (Il2CppDumpField field in klass.Fields)
            {
                if (!field.IsStatic && IsClearName(field.Name))
                    result[$"{className}.{field.Name}"] = field.Offset;
            }
        }

        return result;
    }

    internal static bool IsClearName(string name) =>
        (name.Any(char.IsUpper) || name.Contains('_', StringComparison.Ordinal)) &&
        !ObfuscatedFieldName.IsMatch(name);
}
