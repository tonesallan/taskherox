using System.Buffers.Binary;
using Iced.Intel;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Leitor PE mínimo para GameAssembly.dll. Porta somente o contrato usado pelo resolver legado:
/// mapear RVA para bytes da seção correspondente e decodificar fluxo x64 direto.
/// </summary>
public sealed class Il2CppPeImage
{
    private readonly byte[] _data;
    private readonly Section[] _sections;

    private readonly record struct Section(uint VirtualAddress, uint VirtualSize, uint RawOffset, uint RawSize);

    public Il2CppPeImage(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;

        if (data.Length < 0x40 || data[0] != (byte)'M' || data[1] != (byte)'Z')
            throw new InvalidDataException("invalid DOS header");

        int peOffset = checked((int)ReadUInt32(data, 0x3C));
        if (peOffset < 0 || peOffset > data.Length - 24)
            throw new InvalidDataException("invalid PE header offset");

        if (data[peOffset] != (byte)'P' || data[peOffset + 1] != (byte)'E' ||
            data[peOffset + 2] != 0 || data[peOffset + 3] != 0)
            throw new InvalidDataException("invalid PE signature");

        ushort sectionCount = ReadUInt16(data, peOffset + 6);
        ushort optionalHeaderSize = ReadUInt16(data, peOffset + 20);
        int sectionTable = checked(peOffset + 24 + optionalHeaderSize);
        int tableEnd = checked(sectionTable + sectionCount * 40);
        if (sectionTable < 0 || tableEnd > data.Length)
            throw new InvalidDataException("invalid PE section table");

        _sections = new Section[sectionCount];
        for (int i = 0; i < sectionCount; i++)
        {
            int entry = sectionTable + i * 40;
            _sections[i] = new Section(
                VirtualAddress: ReadUInt32(data, entry + 12),
                VirtualSize: ReadUInt32(data, entry + 8),
                RawOffset: ReadUInt32(data, entry + 20),
                RawSize: ReadUInt32(data, entry + 16));
        }
    }

    public static Il2CppPeImage FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new Il2CppPeImage(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Lê bytes pelo RVA. Se o RVA cair apenas na cauda virtual sem backing raw, retorna vazio.
    /// </summary>
    public byte[] ReadRva(long rva, int count)
    {
        if (rva < 0 || count <= 0)
            return [];

        ulong wanted = (ulong)rva;
        foreach (Section section in _sections)
        {
            ulong start = section.VirtualAddress;
            ulong span = Math.Max(section.VirtualSize, section.RawSize);
            if (wanted < start || wanted >= start + span)
                continue;

            ulong delta = wanted - start;
            if (delta >= section.RawSize)
                return [];

            ulong raw = section.RawOffset + delta;
            if (raw >= (ulong)_data.Length)
                return [];

            int availableBySection = checked((int)Math.Min((ulong)section.RawSize - delta, int.MaxValue));
            int availableByFile = _data.Length - checked((int)raw);
            int length = Math.Min(count, Math.Min(availableBySection, availableByFile));
            if (length <= 0)
                return [];

            var result = new byte[length];
            Buffer.BlockCopy(_data, checked((int)raw), result, 0, length);
            return result;
        }

        return [];
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        if ((uint)offset > (uint)(data.Length - sizeof(ushort)))
            throw new InvalidDataException("truncated PE header");
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        if ((uint)offset > (uint)(data.Length - sizeof(uint)))
            throw new InvalidDataException("truncated PE header");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
    }
}

public sealed record Il2CppDirectFlow(Mnemonic Kind, long TargetRva);

/// <summary>
/// Disassembly x64 somente-leitura sobre o PE do disco. Não lê memória do processo.
/// </summary>
public static class Il2CppDisassembly
{
    public static int ComputeExtent(IReadOnlyList<long> sortedAddresses, long rva)
    {
        ArgumentNullException.ThrowIfNull(sortedAddresses);

        long end = rva + 0x400;
        for (int i = 0; i < sortedAddresses.Count; i++)
        {
            if (sortedAddresses[i] > rva)
            {
                end = sortedAddresses[i];
                break;
            }
        }

        long extent = end - rva;
        extent = Math.Max(0x10, Math.Min(extent, 0x4000));
        return checked((int)extent);
    }

    public static IReadOnlyList<Instruction> Decode(
        Il2CppPeImage image,
        IReadOnlyList<long> sortedAddresses,
        long rva)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(sortedAddresses);

        byte[] code = image.ReadRva(rva, ComputeExtent(sortedAddresses, rva));
        if (code.Length == 0)
            return [];

        var reader = new ByteArrayCodeReader(code);
        var decoder = Decoder.Create(64, reader);
        decoder.IP = checked((ulong)rva);

        var instructions = new List<Instruction>();
        while (reader.CanReadByte)
        {
            decoder.Decode(out Instruction instruction);
            if (instruction.Code == Code.INVALID)
                break;
            instructions.Add(instruction);
        }
        return instructions;
    }

    public static IReadOnlyList<Il2CppDirectFlow> DirectFlows(
        Il2CppPeImage image,
        IReadOnlyList<long> sortedAddresses,
        long rva)
    {
        var result = new List<Il2CppDirectFlow>();
        foreach (Instruction instruction in Decode(image, sortedAddresses, rva))
        {
            if (instruction.Mnemonic is not (Mnemonic.Call or Mnemonic.Jmp))
                continue;

            if (instruction.Op0Kind is not (OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64))
                continue;

            result.Add(new Il2CppDirectFlow(
                instruction.Mnemonic,
                checked((long)instruction.NearBranchTarget)));
        }
        return result;
    }

    public static IReadOnlyList<Mnemonic> MnemonicShape(
        Il2CppPeImage image,
        IReadOnlyList<long> sortedAddresses,
        long rva) =>
        Decode(image, sortedAddresses, rva)
            .Select(instruction => instruction.Mnemonic)
            .ToArray();
}
