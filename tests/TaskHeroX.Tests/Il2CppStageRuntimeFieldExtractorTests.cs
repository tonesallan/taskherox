using System.Buffers.Binary;
using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class Il2CppStageRuntimeFieldExtractorTests
{
    [Fact]
    public void TryExtract_ResolvesFiveFieldLayoutSemantically()
    {
        const string dump = """
public static class Uo
{
    public static List<StageCache> list; // 0x10
    public static Dictionary<int, StageCache> dict; // 0x18
    public static StageCache current; // 0x20
    public static ObscuredInt a; // 0x50
    public static ObscuredInt b; // 0x60
    public static ObscuredInt c; // 0x70
    public static ObscuredInt d; // 0x80
    public static ObscuredInt e; // 0x90

    // RVA: 0x1100
    public static void enter(int key) { }
    // RVA: 0x1200
    public static bool unlocked(int key) { }
    // RVA: 0x1300
    public static void boss(StageCache cache, Action<bool> callback) { }
    // RVA: 0x1400
    public static EStageEnterResultType validate(StageCache cache) { }
    // RVA: 0x1600
    public static void install(StageCache cache) { }
}
public class StageNode
{
    // RVA: 0x1000
    public void Click() { }
}
""";

        Il2CppScriptIndex script = Il2CppScriptIndex.Parse("""
{ "Addresses": [4096,4352,4608,4864,5120,5632] }
""");

        var functions = new Dictionary<long, byte[]>
        {
            [0x1000] = BuildCalls(0x1000, 0x1100, 0x1200, 0x1300, 0x1400),
            [0x1100] = BuildCalls(0x1100, 0x1600),
            [0x1200] = [0x8B,0x40,0x50, 0x3D,0x4D,0x04,0x00,0x00, 0xC3],
            [0x1300] = [0xC3],
            [0x1400] = [0x83,0xF8,0x01, 0x83,0xF8,0x03, 0xC3],
            [0x1600] =
            [
                0x89,0x48,0x60,
                0x48,0x89,0x48,0x20,
                0x89,0x48,0x70,
                0xC3,
            ],
        };

        IReadOnlyList<Il2CppDumpClass> classes = Il2CppDumpParser.Parse(dump);
        Il2CppPeImage image = new(BuildPe(functions));

        Assert.True(Il2CppSemanticExtractor.TryExtractStageStaticSymbols(
            classes, script, out StageStaticSymbols? stageStatic, out string? staticError), staticError);
        Assert.True(Il2CppStageHubExtractor.TryExtract(
            classes, script, image, out StageHubSymbols? hub, out string? hubError), hubError);

        bool ok = Il2CppStageRuntimeFieldExtractor.TryExtract(
            classes, script, image, stageStatic!, hub!, out StageRuntimeFieldSymbols? fields, out string? error);

        Assert.True(ok, error);
        Assert.NotNull(fields);
        Assert.Equal(0x50L, fields.UoMaxOffset);
        Assert.Equal(0x60L, fields.UoCurrentOffset);
        Assert.Equal(0x70L, fields.UoWaveOffset);
    }

    [Fact]
    public void TryExtract_RejectsAmbiguousFiveFieldLayout()
    {
        const string dump = """
public static class Uo
{
    public static List<StageCache> list; // 0x10
    public static Dictionary<int, StageCache> dict; // 0x18
    public static StageCache current; // 0x20
    public static ObscuredInt a; // 0x50
    public static ObscuredInt b; // 0x60
    public static ObscuredInt c; // 0x70
    public static ObscuredInt d; // 0x80
    public static ObscuredInt e; // 0x90

    // RVA: 0x1100
    public static void enter(int key) { }
    // RVA: 0x1200
    public static bool unlocked(int key) { }
    // RVA: 0x1300
    public static void boss(StageCache cache, Action<bool> callback) { }
    // RVA: 0x1400
    public static EStageEnterResultType validate(StageCache cache) { }
}
public class StageNode
{
    // RVA: 0x1000
    public void Click() { }
}
""";

        Il2CppScriptIndex script = Il2CppScriptIndex.Parse("""
{ "Addresses": [4096,4352,4608,4864,5120] }
""");

        var functions = new Dictionary<long, byte[]>
        {
            [0x1000] = BuildCalls(0x1000, 0x1100, 0x1200, 0x1300, 0x1400),
            [0x1100] = [0xC3],
            [0x1200] = [0x8B,0x40,0x50, 0x8B,0x48,0x80, 0x3D,0x4D,0x04,0x00,0x00, 0xC3],
            [0x1300] = [0xC3],
            [0x1400] = [0x83,0xF8,0x01, 0x83,0xF8,0x03, 0xC3],
        };

        IReadOnlyList<Il2CppDumpClass> classes = Il2CppDumpParser.Parse(dump);
        Il2CppPeImage image = new(BuildPe(functions));

        Assert.True(Il2CppSemanticExtractor.TryExtractStageStaticSymbols(
            classes, script, out StageStaticSymbols? stageStatic, out string? staticError), staticError);
        Assert.True(Il2CppStageHubExtractor.TryExtract(
            classes, script, image, out StageHubSymbols? hub, out string? hubError), hubError);

        bool ok = Il2CppStageRuntimeFieldExtractor.TryExtract(
            classes, script, image, stageStatic!, hub!, out StageRuntimeFieldSymbols? fields, out string? error);

        Assert.False(ok);
        Assert.Null(fields);
        Assert.Equal("uo_max ambiguous: obscured=5 hits=2", error);
    }

    [Fact]
    public void TryExtract_AcceptsHistoricalThreeFieldFallback()
    {
        const string dump = """
public static class Uo
{
    public static List<StageCache> list; // 0x10
    public static Dictionary<int, StageCache> dict; // 0x18
    public static ObscuredInt max; // 0x50
    public static ObscuredInt current; // 0x60
    public static ObscuredInt wave; // 0x70

    // RVA: 0x1100
    public static void enter(int key) { }
    // RVA: 0x1200
    public static bool unlocked(int key) { }
    // RVA: 0x1300
    public static void boss(StageCache cache, Action<bool> callback) { }
    // RVA: 0x1400
    public static EStageEnterResultType validate(StageCache cache) { }
}
public class StageNode
{
    // RVA: 0x1000
    public void Click() { }
}
""";

        Il2CppScriptIndex script = Il2CppScriptIndex.Parse("""
{ "Addresses": [4096,4352,4608,4864,5120] }
""");

        var functions = new Dictionary<long, byte[]>
        {
            [0x1000] = BuildCalls(0x1000, 0x1100, 0x1200, 0x1300, 0x1400),
            [0x1100] = [0xC3],
            [0x1200] = [0x3D,0x4D,0x04,0x00,0x00, 0xC3],
            [0x1300] = [0xC3],
            [0x1400] = [0x83,0xF8,0x01, 0x83,0xF8,0x03, 0xC3],
        };

        IReadOnlyList<Il2CppDumpClass> classes = Il2CppDumpParser.Parse(dump);
        Il2CppPeImage image = new(BuildPe(functions));

        Assert.True(Il2CppSemanticExtractor.TryExtractStageStaticSymbols(
            classes, script, out StageStaticSymbols? stageStatic, out string? staticError), staticError);
        Assert.True(Il2CppStageHubExtractor.TryExtract(
            classes, script, image, out StageHubSymbols? hub, out string? hubError), hubError);

        bool ok = Il2CppStageRuntimeFieldExtractor.TryExtract(
            classes, script, image, stageStatic!, hub!, out StageRuntimeFieldSymbols? fields, out string? error);

        Assert.True(ok, error);
        Assert.NotNull(fields);
        Assert.Equal(0x50L, fields.UoMaxOffset);
        Assert.Equal(0x60L, fields.UoCurrentOffset);
        Assert.Equal(0x70L, fields.UoWaveOffset);
    }

    private static byte[] BuildCalls(long startRva, params long[] targets)
    {
        var bytes = new List<byte>();
        long ip = startRva;
        foreach (long target in targets)
        {
            bytes.Add(0xE8);
            int relative = checked((int)(target - (ip + 5)));
            bytes.AddRange(BitConverter.GetBytes(relative));
            ip += 5;
        }
        bytes.Add(0xC3);
        return [.. bytes];
    }

    private static byte[] BuildPe(IReadOnlyDictionary<long, byte[]> functions)
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

        foreach ((long rva, byte[] code) in functions)
        {
            int offset = checked(rawOffset + (int)(rva - virtualAddress));
            Buffer.BlockCopy(code, 0, data, offset, code.Length);
        }

        return data;
    }
}
