namespace TaskHeroX.Core.Update;

/// <summary>
/// Busca os offsets de um build de jogo DESCONHECIDO direto do repo (pasta <c>offsets/</c> no main).
///
/// É o que faz o painel se curar sozinho quando o jogo atualiza: sem isso, um build novo exigiria
/// (a) o usuário rodar o Il2CppDumper, que precisa de .NET 6 e quase ninguém tem, ou (b) esperar um
/// release novo do .exe. Com o feed, basta eu dar push num JSON de ~13 KB e TODO painel instalado
/// se conserta no próximo start — sem baixar exe, sem reinstalar, sem ação do usuário.
///
/// Falha em silêncio (sem rede / build ainda não publicado): o painel só segue no modo degradado
/// (cheats por AOB) e o banner explica o porquê.
/// </summary>
public static class OffsetsFeed
{
    /// <summary>Raiz crua do repo — a pasta <c>offsets/</c> guarda um <c>offsets_&lt;hash&gt;.json</c> por build.</summary>
    public const string BaseUrl = "https://raw.githubusercontent.com/matheusbranhann/taskbarhero-bot/main/offsets";

    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.Add("User-Agent", "tbh_bot-offsets");
        return c;
    }

    /// <summary>Onde o JSON baixado fica: <c>&lt;pasta do exe&gt;/cache/offsets_&lt;hash&gt;.json</c> — é o
    /// primeiro lugar que o <see cref="Engine.Attach"/> procura, então o próximo start já usa o do disco.</summary>
    public static string CachePath(string hash) =>
        Path.Combine(AppContext.BaseDirectory, "cache", $"offsets_{hash}.json");

    /// <summary>
    /// Baixa <c>offsets_&lt;hash&gt;.json</c> e grava no cache. Retorna o caminho salvo, ou null se não
    /// existir pro build / não houver rede. Valida que o JSON tem os símbolos-chave antes de gravar —
    /// meio-download ou página de erro salva viraria um cache tóxico que nunca mais seria refeito.
    /// </summary>
    public static async Task<string?> TryFetchAsync(string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;
        try
        {
            var url = $"{BaseUrl}/offsets_{hash}.json";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;          // 404 = build ainda não publicado
            var body = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (body.Length is < 512 or > 4_000_000) return null; // tamanho fora do plausível -> não é o arquivo

            // Só persiste um cache que satisfaz o MESMO contrato READY do auto-extrator.
            // Versão atual, anchors críticos, Cube v9, ynj, singleton de inventário e ausência de
            // generic jgc são validados juntos; feed parcial/tampered permanece em modo degradado.
            if (!TaskHeroX.Core.Il2Cpp.Il2CppOffsetCache.TryValidateSerialized(body, out _))
                return null;

            // Preserva a guarda historica especifica do feed para navegacao/stage runtime.
            // Ela e adicional ao contrato READY canonico; nao o substitui nem o enfraquece.
            var probe = new TaskHeroX.Core.Il2Cpp.SymbolTable();
            using (var ms = new MemoryStream(body, writable: false))
                if (!probe.LoadOffsetsJson(ms, requireVersion: true) || !probe.Has("uo_ti"))
                    return null;

            var path = CachePath(hash);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Publicacao atomica: nunca grava diretamente no arquivo final. Se houver cancelamento,
            // crash ou erro de disco durante a escrita, o cache anterior permanece intacto e o
            // temporario incompleto e descartado.
            string tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(tempPath, body, ct).ConfigureAwait(false);

                byte[] persisted = await File.ReadAllBytesAsync(tempPath, ct).ConfigureAwait(false);
                if (!TaskHeroX.Core.Il2Cpp.Il2CppOffsetCache.TryValidateSerialized(persisted, out _))
                    return null;

                File.Move(tempPath, path, overwrite: true);
                tempPath = string.Empty;
                return path;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempPath))
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch
                    {
                        // best-effort cleanup; nunca promove arquivo parcial ao cache final
                    }
                }
            }
        }
        catch
        {
            return null;   // sem rede / disco read-only: segue degradado, o banner explica
        }
    }
}
