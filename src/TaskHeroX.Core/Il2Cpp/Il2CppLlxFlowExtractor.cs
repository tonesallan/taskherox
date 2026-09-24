using Iced.Intel;

namespace TaskHeroX.Core.Il2Cpp;

/// <summary>
/// Resolve <c>iuw</c> a partir do código de <c>llx</c>, preservando a âncora do legado:
/// último CALL direto imediatamente anterior ao gate <c>test eax,eax</c> seguido por <c>jle</c>/<c>jg</c>.
/// </summary>
public static class Il2CppLlxFlowExtractor
{
    private const int LegacyScanBytes = 320;

    public static bool TryExtractBoxCounter(
        Il2CppPeImage image,
        long llxRva,
        out long iuwRva)
    {
        ArgumentNullException.ThrowIfNull(image);

        byte[] code = image.ReadRva(llxRva, LegacyScanBytes);
        if (code.Length == 0)
        {
            iuwRva = 0;
            return false;
        }

        var reader = new ByteArrayCodeReader(code);
        var decoder = Decoder.Create(64, reader);
        decoder.IP = checked((ulong)llxRva);
        var instructions = new List<Instruction>();

        while (reader.CanReadByte)
        {
            decoder.Decode(out Instruction instruction);
            if (instruction.IsInvalid)
                break;
            instructions.Add(instruction);
        }

        long? lastDirectCall = null;
        for (int i = 0; i < instructions.Count; i++)
        {
            Instruction instruction = instructions[i];
            if (instruction.Mnemonic == Mnemonic.Call &&
                instruction.Op0Kind is OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64)
            {
                lastDirectCall = checked((long)instruction.NearBranchTarget);
                continue;
            }

            if (lastDirectCall is null || i + 1 >= instructions.Count || !IsTestEaxEax(instruction))
                continue;

            Mnemonic next = instructions[i + 1].Mnemonic;
            if (next is Mnemonic.Jle or Mnemonic.Jg)
            {
                iuwRva = lastDirectCall.Value;
                return true;
            }
        }

        iuwRva = 0;
        return false;
    }

    private static bool IsTestEaxEax(Instruction instruction) =>
        instruction.Mnemonic == Mnemonic.Test &&
        instruction.OpCount >= 2 &&
        instruction.Op0Kind == OpKind.Register &&
        instruction.Op1Kind == OpKind.Register &&
        instruction.Op0Register == Register.EAX &&
        instruction.Op1Register == Register.EAX;
}
