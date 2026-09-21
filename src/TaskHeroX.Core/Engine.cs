using TaskHeroX.Core.Game;
using TaskHeroX.Core.Il2Cpp;
using TaskHeroX.Core.Memory;

namespace TaskHeroX.Core;

/// <summary>
/// Fachada de alto nÃ­vel: attach ao jogo, resoluÃ§Ã£o de offsets por build, cheats, leitura/escrita de save e
/// o dispatcher main-thread (stub por ora). Os loops (automaÃ§Ã£o/watchdog) leem as flags <c>Want*</c> daqui.
/// </summary>
public sealed class Engine : IDisposable
{
    public const string AutoExtractOffsetsSource = "auto-extract-csharp";
    public ProcessTarget   Target   { get; } = new();
    public MemoryAccess    Memory   { get; private set; } = null!;
    public SymbolTable     Symbols  { get; private set; } = null!;
    public MemoryScanner   Scanner  { get; private set; } = null!;
    public Il2CppResolver  Resolver { get; private set; } = null!;
    public Cheats          Cheats   { get; private set; } = null!;
    public StatEditor      Stats    { get; private set; } = null!;
    public SaveData        Save     { get; private set; } = null!;
    public Il2CppApi       Il2Cpp   { get; private set; } = null!;
    public AutoBox         AutoBox  { get; private set; } = null!;
    public AutoStash       AutoStash { get; private set; } = null!;
    public AutoFuse        AutoFuse { get; private set; } = null!;
    public RuneDefs        RuneDefs { get; private set; } = null!;
    public RuneLevels      RuneLevels { get; private set; } = null!;
    public Inventory       Inventory { get; private set; } = null!;
    public StageNav        StageNav { get; private set; } = null!;
    public StageAutomation StageAutomation { get; private set; } = null!;
    public IMainThreadDispatcher Dispatcher { get; private set; } = new StubDispatcher();

    public bool IsAttached => Target.IsAttached && Memory is not null;

    /// <summary>Hash do build (md5 dos 2MB do GameAssembly.dll) e se os offsets resolveram â€” pro status da UI.</summary>
    public string? BuildHash { get; private set; }
    public bool OffsetsLoaded { get; private set; }
    public string? OffsetsSource { get; private set; }

    // Flags de intenÃ§Ã£o â€” o AutomationLoop lÃª a cada tick.
    public bool WantActk, WantGodmode, WantAutobox, WantAutostash, WantAutofuse, WantAutoboss, WantEvolve;
    public bool WantWatchdog;   // Auto-restart: jogo fechou? reabre via Steam + reaplica tudo

    /// <summary>Durante o restart: o loop re-attacha mas NÃƒO aplica nada (start limpo). Igual ao _wd_hold do Python.</summary>
    public volatile bool WdHold;

    /// <summary>Steam appid do TaskBarHero â€” usado pelo Auto-restart pra reabrir (steam://run/&lt;appid&gt;).</summary>
    public const int SteamAppId = 3678970;

    // Stats/stage FORÃ‡ADOS (re-aplicados a cada tick â€” o jogo recalcula e sobrescreve uma escrita Ãºnica).
    // O painel troca a referÃªncia inteira (assign atÃ´mico) p/ nÃ£o correr com o loop.
    public Dictionary<string, double> WantStats = new();
    public Dictionary<string, int> WantStage = new();

    public event Action<string>? Log;
    internal void Emit(string msg) => Log?.Invoke(msg);

    public bool Attach()
    {
        if (!Target.Attach()) return false;
        Memory  = new MemoryAccess(Target);
        Symbols = new SymbolTable();

        var hash = BuildInfo.DllHash(Target.ModulePath);
        BuildHash = hash;
        OffsetsSource = null;
        bool loaded = false;
        if (hash is null)
            Emit("build-hash indisponÃ­vel (nÃ£o consegui ler o GameAssembly.dll do disco)");
        else
        {
            loaded = Symbols.LoadKnownBuild(hash);
            if (loaded) OffsetsSource = "known-build";
            if (!loaded)
            {
                // Fallback: cache de offsets no formato do Python (offsets_<hash>.json), ao lado do exe.
                foreach (var cand in new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "cache", $"offsets_{hash}.json"),
                    Path.Combine(AppContext.BaseDirectory, $"offsets_{hash}.json"),
                })
                {
                    // requireVersion: cache em disco de extrator antigo Ã© DESCARTADO (senÃ£o um offset
                    // errado gravado uma vez sobrevive a todas as correÃ§Ãµes â€” o cache tem prioridade
                    // sobre os embutidos). Ver SymbolTable.MinExtractVer.
                    if (TryLoadReadyOffsets(cand))
                    {
                        loaded = true;
                        OffsetsSource = "cache local";
                        Emit($"offsets carregados do cache {Path.GetFileName(cand)}");
                        break;
                    }
                }
            }
            if (!loaded)
            {
                // Fallback final: offsets EMBUTIDOS no assembly (Offsets/offsets_<hash>.json) -> 1 exe.
                var asm = typeof(Engine).Assembly;
                var res = Array.Find(asm.GetManifestResourceNames(),
                    n => n.EndsWith($"offsets_{hash}.json", StringComparison.OrdinalIgnoreCase));
                if (res is not null)
                {
                    using var s = asm.GetManifestResourceStream(res);
                    if (s is not null && Symbols.LoadOffsetsJson(s))
                    {
                        loaded = true;
                        OffsetsSource = "cache embutido";
                        Emit($"offsets embutidos ({hash})");
                    }
                }
            }
            // A BASE vai no log de propÃ³sito: quando o attach pegava a base da sessÃ£o anterior, nada
            // no log denunciava (os offsets carregavam certo, sÃ³ a base era podre). Com ela impressa,
            // comparar duas sessÃµes mostra o problema na hora.
            Emit(loaded
                ? $"build {hash} â€” offsets prontos (GameAssembly @ 0x{Target.ModuleBase:X})"
                : $"build {hash} desconhecido e sem cache - modo AOB temporario; tentando feed/auto-offset C# em background");
        }

        OffsetsLoaded = loaded;
        Scanner    = new MemoryScanner(Memory);
        Resolver   = new Il2CppResolver(Memory, Symbols, Scanner);
        Cheats     = new Cheats(Memory, Symbols, Scanner);
        Stats      = new StatEditor(Memory, Scanner, Symbols, Resolver);
        Save       = new SaveData(Memory, Symbols, Resolver);
        var disp   = new RealDispatcher(Memory, Symbols) { Log = Emit };
        Dispatcher = disp;
        Il2Cpp     = new Il2CppApi(Memory);
        AutoBox    = new AutoBox(Memory, Symbols, Scanner, Il2Cpp, disp) { Log = Emit };
        AutoStash  = new AutoStash(Memory, Symbols, Scanner, Il2Cpp, Resolver, disp) { Log = Emit };
        AutoFuse   = new AutoFuse(Memory, Symbols, Resolver, disp) { Log = Emit };
        RuneDefs   = new RuneDefs(Memory, Il2Cpp, Scanner);
        RuneLevels = new RuneLevels(Memory, Il2Cpp, Scanner);
        Inventory  = new Inventory(Memory, Symbols, Resolver);
        StageNav   = new StageNav(Memory, Symbols, Resolver, disp) { Log = Emit };
        StageAutomation = new StageAutomation(StageNav, Save, AutoBox, Inventory, Symbols)
        {
            Log = Emit,
            DisableEvolve = () => WantEvolve = false,   // evoluÃ§Ã£o chegou no topo -> desliga o modo sozinha
        };
        return true;
    }

    /// <summary>
    /// Carrega um cache de offsets DEPOIS do attach â€” usado pelo <see cref="Update.OffsetsFeed"/> quando o
    /// jogo atualizou e o build ainda nÃ£o Ã© conhecido por este exe. Cura a sessÃ£o em andamento: as features
    /// que dependem de RVA voltam sem precisar reiniciar o painel.
    /// </summary>
    private bool TryLoadReadyOffsets(string path)
    {
        if (Symbols is null) return false;

        byte[] body;
        try { body = File.ReadAllBytes(path); }
        catch { return false; }

        // Valida os MESMOS bytes que serao carregados. Um cache v9 parcial/tampered nunca pode
        // transformar AOB ONLY em READY nem deixar simbolos parciais no SymbolTable da sessao.
        if (!Il2CppOffsetCache.TryValidateSerialized(body, out _)) return false;
        using var stream = new MemoryStream(body, writable: false);
        return Symbols.LoadOffsetsJson(stream, requireVersion: true);
    }

    internal bool LoadOffsetsFrom(string path, string source = "feed")
    {
        if (!TryLoadReadyOffsets(path)) return false;

        OffsetsLoaded = true;
        OffsetsSource = source;
        Emit($"offsets do build {BuildHash} carregados de {source} - features completas de volta");
        return true;
    }

    /// <summary>
    /// Recupera um build desconhecido sem alterar a ordem das fontes: o Attach ja tentou known-build,
    /// cache local e cache embutido; aqui tentamos feed publicado e, somente se ele nao resolver,
    /// o auto-offset C# local. O cache gerado so e carregado se a mesma sessao/build continuar viva.
    /// </summary>
    public async Task<bool> RecoverUnknownBuildOffsetsAsync(
        string? cachePathOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAttached || !Target.IsAlive() || OffsetsLoaded)
            return OffsetsLoaded;

        string? hash = BuildHash;
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(Target.ModulePath))
            return false;

        string modulePath = Target.ModulePath;

        string? feedPath = await Update.OffsetsFeed.TryFetchAsync(hash, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (!SameAttachedBuild(hash, modulePath))
            return false;

        if (feedPath is not null && LoadOffsetsFrom(feedPath, source: "feed"))
            return true;

        Emit($"feed sem offsets para {hash[..Math.Min(7, hash.Length)]} - iniciando auto-offset C# fail-closed");

        string cachePath = cachePathOverride ?? Update.OffsetsFeed.CachePath(hash);
        Il2CppAutoOffsetFallbackResult generated =
            await Il2CppAutoOffsetFallback.TryGenerateAsync(
                hash,
                modulePath,
                cachePath,
                cancellationToken: cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (!generated.Success)
        {
            if (SameAttachedBuild(hash, modulePath))
                Emit($"auto-offset C# nao aceitou o build {hash[..Math.Min(7, hash.Length)]}: {generated.Error ?? "falha desconhecida"}");
            return false;
        }

        if (!SameAttachedBuild(hash, modulePath))
            return false;

        if (!LoadOffsetsFrom(generated.CachePath!, source: AutoExtractOffsetsSource))
        {
            Emit("cache gerado pelo auto-offset C# foi rejeitado na carga final");
            return false;
        }

        Emit($"auto-offset C# validado para {hash[..Math.Min(7, hash.Length)]}");
        return true;
    }

    private bool SameAttachedBuild(string hash, string modulePath) =>
        IsAttached &&
        Target.IsAlive() &&
        string.Equals(BuildHash, hash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Target.ModulePath, modulePath, StringComparison.OrdinalIgnoreCase);

    public void Dispose() => Target.Dispose();
}

