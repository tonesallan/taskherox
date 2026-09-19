using System.Buffers.Binary;
using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppLlxFlowExtractorTests
{
    [Fact]
    public void TryExtractBoxCounter_ReturnsLastCallBeforeTestEaxAndJle()
    {
        const long llx = 0x1000;
        const long expectedIuw = 0x1800;

        var code = new List<byte>();
        AppendCall(code, llx + code.Count, 0x1700);                    // decoy
        code.AddRange([0x84, 0xC0, 0x74, 0x00]);                       // test al,al; je +0
        AppendCall(code, llx + code.Count, expectedIuw);               // real
        code.AddRange([0x85, 0xC0, 0x7E, 0x00, 0xC3]);                 // test eax,eax; jle +0; ret

        bool ok = Il2CppLlxFlowExtractor.TryExtractBoxCounter(
            new Il2CppPeImage(BuildPe(llx, [.. code])),
            llx,
            out long iuw);

        Assert.True(ok);
        Assert.Equal(expectedIuw, iuw);
    }

    [Fact]
    public void TryExtractBoxCounter_AcceptsJgAndRejectsUnrelatedBranches()
    {
        const long llx = 0x1000;
        const long target = 0x1900;

        var code = new List<byte>();
        AppendCall(code, llx + code.Count, 0x1800);
        code.AddRange([0x85, 0xC0, 0x74, 0x00]);                       // test eax,eax; je -> não é a âncora
        AppendCall(code, llx + code.Count, target);
        code.AddRange([0x85, 0xC0, 0x7F, 0x00, 0xC3]);                 // test eax,eax; jg

        Assert.True(Il2CppLlxFlowExtractor.TryExtractBoxCounter(
            new Il2CppPeImage(BuildPe(llx, [.. code])), llx, out long iuw));
        Assert.Equal(target, iuw);
    }

    [Fact]
    public void TryExtractBoxCounter_ReturnsFalseWithoutLegacyGateShape()
    {
        const long llx = 0x1000;
        var code = new List<byte>();
        AppendCall(code, llx, 0x1800);
        code.AddRange([0x85, 0xC0, 0x74, 0x00, 0xC3]);                 // test eax,eax; je

        Assert.False(Il2CppLlxFlowExtractor.TryExtractBoxCounter(
            new Il2CppPeImage(BuildPe(llx, [.. code])), llx, out long iuw));
        Assert.Equal(0, iuw);
    }

    private static void AppendCall(List<byte> code, long ip, long target)
    {
        code.Add(0xE8);
        int relative = checked((int)(target - (ip + 5)));
        code.AddRange(BitConverter.GetBytes(relative));
    }

    private static byte[] BuildPe(long rva, byte[] code)
    {
        const int peOffset = 0x80;
        const int optionalHeaderSize = 0xF0;
        const int sectionTable = peOffset + 24 + optionalHeaderSize;
        const int rawOffset = 0x200;
        const int rawSize = 0x1000;
        const uint virtualAddress = 0x1000;

        var data = new byte[rawOffset + rawSize];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x3C, 4), peOffset);
        data[peOffset] = (byte)'P';
        data[peOffset + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 6, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 20, 2), optionalHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionTable + 8, 4), rawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionTable + 12, 4), virtualAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionTable + 16, 4), rawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionTable + 20, 4), rawOffset);

        int offset = checked(rawOffset + (int)(rva - virtualAddress));
        Buffer.BlockCopy(code, 0, data, offset, code.Length);
        return data;
    }
}
