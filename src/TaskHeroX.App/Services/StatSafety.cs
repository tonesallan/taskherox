using System.Globalization;

namespace TaskHeroX.App.Services;

/// <summary>
/// Guardrails operacionais do editor de stats do TaskHeroX.
///
/// IMPORTANTE: estes valores NÃO representam o limite numérico do tipo float do jogo.
/// O jogo aceita números muito maiores em memória, mas valores extremos podem quebrar cálculos,
/// física, animação, UI ou deixar o estado da sessão instável. Estes são limites conservadores
/// para o trainer e podem ser ajustados por build quando tivermos validação ao vivo.
/// </summary>
public static class StatSafety
{
    public readonly record struct Limit(double Min, double Max, string DisplayMax);

    /// <summary>
    /// Quando true, os limites conservadores por stat ficam desativados.
    /// O engine continua sujeito aos limites naturais do float e rejeita NaN/Infinity.
    /// Não é persistido: cada nova execução do TaskHeroX volta ao modo seguro.
    /// </summary>
    public static bool UnsafeMode { get; set; }

    private static readonly IReadOnlyDictionary<string, Limit> Limits =
        new Dictionary<string, Limit>(StringComparer.Ordinal)
        {
            ["Attack Damage"] = new(0, 10_000_000, "10,000,000"),
            ["Attack Speed"] = new(0, 25, "25"),
            ["Critical Chance"] = new(0, 1, "1.00 (100%)"),
            ["Critical Damage"] = new(0, 100, "100x"),
            ["Cooldown Reduction"] = new(0, 0.95, "0.95 (95%)"),
            ["Cast Speed"] = new(0, 25, "25"),
            ["Physical Damage"] = new(0, 100, "100x"),
            ["Fire Damage"] = new(0, 100, "100x"),
            ["Cold Damage"] = new(0, 100, "100x"),
            ["Lightning Damage"] = new(0, 100, "100x"),
            ["Chaos Damage"] = new(0, 100, "100x"),
            ["Max Hp"] = new(0, 10_000_000, "10,000,000"),
            ["Armor"] = new(0, 10_000_000, "10,000,000"),
            ["Dodge Chance"] = new(0, 0.95, "0.95 (95%)"),
            ["Block Chance"] = new(0, 0.95, "0.95 (95%)"),
            ["All Element Resistance"] = new(0, 0.95, "0.95 (95%)"),
            ["Hp Regen /Sec"] = new(0, 1_000_000, "1,000,000"),
            ["Dmg Absorption"] = new(0, 1_000_000, "1,000,000"),
            ["Dmg Reduction"] = new(0, 0.95, "0.95 (95%)"),
            ["Movement Speed"] = new(0, 100, "100"),
            ["Area of Effect %"] = new(0, 10, "10x"),
            ["Area of Effect Damage"] = new(0, 100, "100x"),
            ["Add HP/Kill"] = new(0, 1_000_000, "1,000,000"),
            ["Life Leech"] = new(0, 1, "1.00 (100%)"),
            ["Skill Heal"] = new(0, 100, "100x"),
        };

    public static bool TryGet(string stat, out Limit limit)
    {
        if (UnsafeMode)
        {
            limit = default;
            return false;
        }
        return Limits.TryGetValue(stat, out limit);
    }

    public static bool TryValidate(string stat, string raw, out double value, out string error)
    {
        value = 0;
        error = string.Empty;

        if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            !double.IsFinite(value))
        {
            error = "valor inválido";
            return false;
        }

        if (UnsafeMode)
            return true;

        if (!Limits.TryGetValue(stat, out var limit))
            return true;

        if (value < limit.Min || value > limit.Max)
        {
            error = $"permitido pelo modo seguro: {limit.Min.ToString(CultureInfo.InvariantCulture)} até {limit.DisplayMax}";
            return false;
        }

        return true;
    }

    public static string Tooltip(string stat)
    {
        if (UnsafeMode)
            return "UNSAFE MODE ativo: limites seguros desativados. O engine ainda rejeita NaN/Infinity e valores que não cabem em float finito.";

        return Limits.TryGetValue(stat, out var l)
            ? $"Limite seguro do TaskHeroX: {l.Min.ToString(CultureInfo.InvariantCulture)} até {l.DisplayMax}."
            : "Sem limite específico configurado.";
    }
}
