using TaskHeroX.Core.Game;
using TaskHeroX.Core.Il2Cpp;

namespace TaskHeroX.Tests;

public sealed class RealDispatcherTests
{
    [Fact]
    public void Command11_RejectsMissingLlmBeforeTouchingMemory()
    {
        var symbols = new SymbolTable();
        var dispatcher = new RealDispatcher(null!, symbols);

        Assert.False(dispatcher.Command(11));
    }
}
