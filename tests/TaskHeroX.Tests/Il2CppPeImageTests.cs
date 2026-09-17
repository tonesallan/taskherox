using System.Buffers.Binary;
using Iced.Intel;
using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppPeImageTests
{
    [Fact]
    public void ReadRva_MapsSectionVirtualAddressToRawBytes()
    {
        byte[] peBytes = BuildPe([0x90, 0xC3]);
        var image = new Il2CppPeImage(peBytes);

        Assert.Equal([0x90, 0xC3], image.ReadRva(0x1000, 2));
        Assert.Empty(image.ReadRva(0x5000, 4));
    }

    [Fact]
    public void DirectFlows_DecodesRelativeCallAndJumpTargets()
    {
        // 1000: call 1010   => rel32 = 0x0B
        // 1005: jmp  1020   => rel32 = 0x16
        // 100A: ret
        byte[] code =
        [
            0xE8, 0x0B, 0x00, 0x00, 0x00,
            0xE9, 0x16, 0x00, 0x00, 0x00,
            0xC3,
        ];

        var image = new Il2CppPeImage(BuildPe(code));
        long[] addresses = [0x1000, 0x1030];

        IReadOnlyList<Il2CppDirectFlow> flows = Il2CppDisassembly.DirectFlows(image, addresses, 0x1000);

        Assert.Collection(
            flows,
            flow =>
            {
                Assert.Equal(Mnemonic.Call, flow.Kind);
                Assert.Equal(0x1010L, flow.TargetRva);
            },
            flow =>
            {
                Assert.Equal(Mnemonic.Jmp, flow.Kind);
                Assert.Equal(0x1020L, flow.TargetRva);
            });
    }

    [Fact]
    public void ComputeExtent_UsesNextKnownAddressAndLegacyBounds()
    {
        Assert.Equal(0x20, Il2CppDisassembly.ComputeExtent([0x1000, 0x1020], 0x1000));
        Assert.Equal(0x10, Il2CppDisassembly.ComputeExtent([0x1000, 0x1005], 0x1000));
        Assert.Equal(0x4000, Il2CppDisassembly.ComputeExtent([0x1000, 0x9000], 0x1000));
        Assert.Equal(0x400, Il2CppDisassembly.ComputeExtent([0x1000], 0x1000));
    }

    [Fact]
    public void Constructor_RejectsInvalidPe()
    {
        Assert.Throws<InvalidDataException>(() => new Il2CppPeImage(new byte[64]));
    }

    private static byte[] BuildPe(byte[] code)
    {
        const int peOffset = 0x80;
        const int optionalHeaderSize = 0xF0;
        const int sectionTable = peOffset + 24 + optionalHeaderSize;
        const int rawOffset = 0x200;
        const int rawSize = 0x200;
        const uint virtualAddress = 0x1000;

        var data = new byte[rawOffset + rawSize];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x3C, 4), peOffset);

        data[peOffset] = (byte)'P';
        data[peOffset + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 6, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 20, 2), optionalHeaderSize);

        int section = sectionTable;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 8, 4), rawSize); // VirtualSize
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 12, 4), virtualAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 16, 4), rawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 20, 4), rawOffset);

        Buffer.BlockCopy(code, 0, data, rawOffset, code.Length);
        return data;
    }
}
