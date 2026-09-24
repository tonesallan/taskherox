using System.Globalization;
using System.Text.Json;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Índice mínimo do <c>script.json</c> do Il2CppDumper usado pela extração semântica de offsets.
/// Mantém apenas os contratos consumidos pelo extrator legado: Addresses, ScriptMetadata e
/// ScriptMetadataMethod.
/// </summary>
public sealed class Il2CppScriptIndex
{
    private readonly Dictionary<string, long> _metadataAddresses;
    private readonly Dictionary<long, long> _metadataMethodAddresses;

    private Il2CppScriptIndex(
        long[] addresses,
        Dictionary<string, long> metadataAddresses,
        Dictionary<long, long> metadataMethodAddresses)
    {
        Addresses = addresses;
        _metadataAddresses = metadataAddresses;
        _metadataMethodAddresses = metadataMethodAddresses;
    }

    /// <summary>Endereços de código únicos e ordenados, equivalente a sorted(set(script["Addresses"])).</summary>
    public IReadOnlyList<long> Addresses { get; }

    public IReadOnlyDictionary<string, long> MetadataAddresses => _metadataAddresses;
    public IReadOnlyDictionary<long, long> MetadataMethodAddresses => _metadataMethodAddresses;

    public bool TryGetMetadataAddress(string name, out long address) =>
        _metadataAddresses.TryGetValue(name, out address);

    public bool TryGetMethodAddress(long metadataAddress, out long methodAddress) =>
        _metadataMethodAddresses.TryGetValue(metadataAddress, out methodAddress);

    public static Il2CppScriptIndex ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static Il2CppScriptIndex Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return Parse(stream);
    }

    public static Il2CppScriptIndex Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        var addresses = new SortedSet<long>();
        if (root.TryGetProperty("Addresses", out JsonElement addressArray) &&
            addressArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in addressArray.EnumerateArray())
                if (TryReadInt64(element, out long value))
                    addresses.Add(value);
        }

        var metadata = new Dictionary<string, long>(StringComparer.Ordinal);
        if (root.TryGetProperty("ScriptMetadata", out JsonElement metadataArray) &&
            metadataArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in metadataArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("Name", out JsonElement nameElement) ||
                    nameElement.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("Address", out JsonElement addressElement) ||
                    !TryReadInt64(addressElement, out long address))
                    continue;

                string? name = nameElement.GetString();
                if (!string.IsNullOrEmpty(name))
                    metadata.TryAdd(name, address); // legado usa next(...): primeira ocorrência vence.
            }
        }

        var metadataMethods = new Dictionary<long, long>();
        if (root.TryGetProperty("ScriptMetadataMethod", out JsonElement methodArray) &&
            methodArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in methodArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("Address", out JsonElement addressElement) ||
                    !item.TryGetProperty("MethodAddress", out JsonElement methodAddressElement) ||
                    !TryReadInt64(addressElement, out long address) ||
                    !TryReadInt64(methodAddressElement, out long methodAddress))
                    continue;

                metadataMethods[address] = methodAddress; // dict-comprehension legado: última ocorrência vence.
            }
        }

        return new Il2CppScriptIndex([.. addresses], metadata, metadataMethods);
    }

    private static bool TryReadInt64(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt64(out value);

        if (element.ValueKind == JsonValueKind.String)
        {
            string? text = element.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                value = 0;
                return false;
            }

            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        value = 0;
        return false;
    }
}
