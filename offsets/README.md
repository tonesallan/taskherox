# Feed de offsets

Um `offsets_<hash>.json` por build do jogo. **É daqui que o painel se cura sozinho quando o jogo atualiza.**

`<hash>` = MD5 dos primeiros **2.000.000 bytes** do `GameAssembly.dll`, 12 primeiros hex.

## Por que existe

Quando o jogo atualiza, todos os RVAs mudam e as features que dependem deles param (inventário, runas,
stages, nível do cubo, ACTk). Reextrair exige o Il2CppDumper, que precisa de .NET 6 — quase ninguém tem.

Com o feed, o painel instalado faz um GET do JSON do build novo e volta a funcionar **na mesma sessão**. Se o build ainda não estiver publicado aqui (404), o TaskHeroX agora tenta o **auto-offset C# local** como último fallback: usa o Il2CppDumper embutido, extrai/valida os símbolos, grava um cache v9 e o carrega sem reiniciar o painel. O contrato v9 exige também o layout write-sensitive do Cube (`cube_grade`, `cube_bers`, `cube_inlist`, `cube_active`, `cube_busy`, `cube_type`, `cube_lvrecipe`, `cube_level_off`), evitando herdar offsets de outra build. Se qualquer etapa falhar ou ficar ambígua, o fluxo é fail-closed e o painel permanece no modo degradado/AOB.

Consumido por `src/TaskHeroX.Core/Update/OffsetsFeed.cs`. A ordem de resolução é: **KnownBuilds -> cache local versionado -> cache embutido -> feed -> auto-extração C#**. Tanto o feed quanto o fallback C# gravam em `<pasta do exe>/cache/offsets_<hash>.json`; no próximo start esse cache passa a ser encontrado antes das fontes remotas.

## Como publicar um build novo

Com o jogo atualizado instalado, ainda é possível publicar manualmente um cache pelo fluxo legado Python:

```bash
cd python_old_project
py -3 -c "import tbh_core as C; print(C._offsets_ok(C.resolve_symbols(print)))"
# re-dumpa (~40s), extrai e grava cache/offsets_<hash>.json — imprime True se veio completo
```

Depois: copie o JSON pra cá **e** pra `src/TaskHeroX.Core/Offsets/` (assim o próximo release já sai com ele
embutido, e quem instalar do zero não depende de rede), commit e push. Pronto — os painéis já instalados
se atualizam sozinhos.

As âncoras da extração não dependem de nome ofuscado (só de nomes que o ofuscador preserva: `CubeInData`,
`ERecipeType`, `button_Cube`, `UI_Main`, `Guiding*`…), então um update que só renomeia classes é absorvido
sem tocar em código.

## Validação

```bash
dotnet run --project tools/TaskHeroX.Cli -- --feed <hash>      # baixa e valida
dotnet run --project tools/TaskHeroX.Cli -- --e2e             # 19 checagens ao vivo
```

O feed só persiste o JSON se ele carregar com versão aceita e trouxer símbolos-chave. Já o fallback C# passa por uma validação mais rígida: contrato crítico completo, hash do `GameAssembly.dll` conferido antes/depois da extração, ausência de `jgc` genérico e recarga final pelo `SymbolTable(requireVersion: true)` antes de o cache ser usado.

## Builds publicados

| hash | quando | nota |
|---|---|---|
| `c824ed7a2bb1` | 2026-07-18 | build da v3.5/v4.0 |
| `535f977c07ca` | 2026-07-22 | update que renomeou a base dos singletons (`nq`→`nr`) e a classe do cubo (`uu.Cube`→`ux.Cube`) |
| `f2be0e20ad2b` | 2026-07-23 | o jogo anunciou este update com o popup `An update is required / Verify integrity of game files`. Extração limpa (17s, nada faltando); as âncoras todas seguraram. A assinatura do godmode continua casando em 14 lugares, então a busca pela cauda segue necessária |
| `a8b994ee3986` | 2026-07-30 | o `PlayerSaveData` deslocou muito (`RuneSaveData` 0x80→0x90, `itemSaveDatas` 0xb0→0xc0, …). Expôs **dois offsets ainda hardcoded**: o da lista de runas (escrevia em `attributeSaveDatas`, de layout idêntico — corrompia os atributos do herói) e o do nível do cubo (0x1CC→0x1D8, lia lixo). Os dois viraram auto-extraídos |
| `9655ccb67d45` | 2026-07-31 | `cube_level_off` mudou **de novo** (0x1D8→0x1D0) e veio certo sozinho — a prova de que auto-extrair foi a decisão certa |
| `d2651aeb57f0` | 2026-08-11 | extração limpa, todas as âncoras seguraram. `cube_level_off` seguiu 0x1D0 e o offset de runa 0x90. Validado ao vivo: dispatcher instala, estágio/cubo/runas leem certo, godmode resolve |


## Auto-offset C# validado

No build `7fc7437300cf`, o fluxo foi validado ao vivo com o feed sem arquivo correspondente: o `Engine` iniciou com `OffsetsLoaded=False`, tentou o feed, executou o fallback C#, persistiu cache `_ver=9`, carregou a `SymbolTable` e terminou com `OffsetsLoaded=True` / `OffsetsSource=auto-extract-csharp`. O processo do jogo permaneceu vivo e o working tree não foi alterado.
