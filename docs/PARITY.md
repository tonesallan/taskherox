# Paridade Python → C# (Fase 8)

Checklist do que o painel Python (`../tbh_bot`) faz vs. o estado do port C#. O Python só é aposentado quando
tudo aqui estiver ✅ (ou conscientemente descartado).

Legenda: ✅ feito · 🟡 parcial · ⏸ adiado (documentado) · ⚪ pendente

> **Estado validado**: além da paridade histórica já registrada no build `c824ed7a2bb1`, o pipeline de **auto-offset C#** foi validado no build `7fc7437300cf`: extração real C# = Python legado nos símbolos críticos; fallback embutido gera cache v8; e o caminho `feed sem offsets -> auto-offset C# -> LoadOffsetsFrom -> OffsetsLoaded=True` foi exercitado ao vivo com o jogo permanecendo aberto.

## Engine (núcleo)
| Recurso | Python | C# | Nota |
|---|---|---|---|
| Attach ao processo | ✅ | ✅ | `ProcessTarget` |
| Leitura de memória | 1 syscall/leitura | ✅ **batch** | `MemoryAccess.ReadArray<T>` — **~200x** ao vivo |
| AOB scan | ✅ | ✅ | `MemoryScanner` |
| Offsets por build (conhecido) | ✅ | ✅ | `KnownBuilds` + `LoadOffsetsJson` (cache do Python) |
| Auto-offset por **dump** (build novo) | ✅ | ✅ | C# integrado: known/cache/embedded -> feed -> fallback local fail-closed; validado ao vivo no build `7fc7437300cf` |
| ObscuredInt (ACTk) | ✅ | ✅ | `ObscuredValue` |
| Cheats: ACTk/God/Hitkill/Speed | ✅ | ✅ | `Cheats` (portado fiel; validar ao vivo) |
| Stats (25) ler/aplicar | ✅ | ✅ | `StatEditor` |
| Stage fields ler/aplicar | ✅ | ✅ | `StatEditor` |
| Runas ler/setar | ✅ | 🟡 | lê+seta por key; **defs/árvore/ícones/max** não portados |
| Cubo nível | ✅ | ✅ | `SaveData.SetCubeLevel` |
| Stage progress / unlock | ✅ | ✅ | `SaveData.StageProgress/SetMaxStage` |
| Inventário (contagem) | ✅ | 🟡 | conta; **nomes/grade/preço** dependem de izb+Market |
| **Dispatcher main-thread** | ✅ | ⏸ | code-cave; `Game/DISPATCHER_PORT_NOTES.md`. Bloqueia auto-box/stash/fuse |
| Auto-box / stash / fuse | ✅ | ⏸ | dependem do dispatcher |

## UI (painel)
| Aba | Python | C# | Nota |
|---|---|---|---|
| Trainer (proteção+stats+stage+unlocks) | ✅ | ✅ | code-behind WPF, tema dark |
| Stages (mapa 120) | ✅ | ✅ | grid pela fórmula do StageKey |
| Runes (árvore) | ✅ | 🟡 | lista + unlock nível 1 (árvore/ícones dependem das defs) |
| Inventory (valor) | ✅ | 🟡 | contagem (valor por item = futuro) |
| Market (preços Steam) | ✅ | ✅ | `MarketDb` (HTTP + cache) |
| Concorrência (loop) | threads+GIL | ✅ | `AutomationLoop`/`Watchdog` (Task, sem GIL) |

## Periféricos
| Recurso | Python | C# | Nota |
|---|---|---|---|
| Auto-update (troca o exe) | ✅ | ✅ | `Update/AutoUpdate` (trilho de release C#) |
| Publish single-file | ✅ | ✅ | `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` |
| **Price overlay (OCR)** | ✅ | ⏸ | opcional; ver abaixo |
| Testes | — | ✅ | `tests/TaskHeroX.Tests` (ObscuredValue/GameConstants/SymbolTable) |

## Adiados — por quê e o que falta
1. **Dispatcher main-thread** (code-cave em `InputManager.Update`): precisa disassembler (Iced) + shellcode +
   suspend/resume + iteração AO VIVO. Desbloqueia auto-box/stash/fuse. Ver `Game/DISPATCHER_PORT_NOTES.md`.
3. **Rune defs (RuneInfoData)**: nomes/ícones/conexões/nível-máximo da árvore de runas. Sem isso a aba Runes é
   lista + unlock nível-1 (seguro). Portar = ler `RuneInfoData` (klass scan) como no Python.
4. **Price overlay OCR** (`tbh_overlay.py`): janela layered topmost + `Windows.Media.Ocr` + detecção de hover.
   Peça grande e opcional; o `MarketDb` (o dado de preço) já está portado.
5. **Item info (izb) + valor de inventário**: resolver nome/grade/nível/preço por item para a aba Inventory.
