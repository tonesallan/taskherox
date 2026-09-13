# TaskHeroX

**TaskHeroX** is a community-maintained external control center, trainer, automation toolkit and diagnostics platform for **Taskbar Hero** (Unity IL2CPP, Steam appid `3678970`).

This repository continues development from the last validated state of the original `taskbarhero-bot` project while establishing a new product identity, safer compatibility workflow and independent release cycle.

> **Current baseline:** game build `139467f3ad72` validated locally with **19/19 E2E checks passing** before the TaskHeroX migration.

## Project goals

TaskHeroX is being reorganized around four principles:

- **Compatibility first** — game updates should disable only the unsupported feature instead of risking invalid memory writes.
- **Recoverability** — stable snapshots, compatibility diagnostics and restore actions are treated as first-class features.
- **Modular development** — memory access, IL2CPP resolution, game features, automation and UI remain isolated so one fix does not break unrelated systems.
- **Clear UI** — a new TaskHeroX dashboard will separate live status, trainer controls, automation, progression, inventory and diagnostics.

## Current capabilities

- ACTk bypass and God Mode.
- Live character stats with persistent overrides.
- Stage data reading and controlled overrides.
- Auto-box, auto-stash and auto-fuse.
- Inventory inspection and Steam Community Market price lookup.
- Runes tree and stage map tools.
- Cube, runes and progression utilities.
- Watchdog / auto-restart workflow.
- Build-specific offset loading and runtime diagnostics.

### Compatibility note

AutoBoss and Evolution rely on stage-entry validation symbols that can change between game builds. TaskHeroX will prefer disabling these features when validation is ambiguous rather than selecting an unsafe address.

## Repository workflow

The migration is intentionally recoverable:

- `main` — stable TaskHeroX baseline.
- `develop` — integration branch.
- `feature/taskherox-rebrand` — current UI/identity restructuring work.
- `backup/legacy-19pass-20260913` — frozen validated legacy snapshot.
- tag `legacy-migration-19pass` — permanent pointer to the 19/19 migration baseline.

## Structure

```text
TbhBot.slnx                  current solution name; scheduled for staged rename
Directory.Build.props        shared build metadata
src/
  TbhBot.Core/               process, memory, IL2CPP, game features and automation
  TbhBot.App/                WPF desktop application
tools/TbhBot.Cli/            diagnostics, E2E harness and benchmarks
tests/TbhBot.Tests/          automated tests
offsets/                     public build-offset feed
layout/                      UI specifications and prototypes
docs/                        architecture, compatibility and TaskHeroX design notes
python_old_project/          historical Python implementation/reference
```

Project/namespace renaming is being done in stages so the validated engine remains buildable throughout the rebrand.

## Build from source

Requires the **.NET 10 SDK** on Windows x64.

```powershell
dotnet build
dotnet test
dotnet run --project src\TbhBot.App
dotnet run --project tools\TbhBot.Cli -- --e2e --offsets "src\TbhBot.Core\Offsets"
```

For a self-contained executable:

```powershell
.\publish.bat
```

The application executable is being rebranded to **TaskHeroX.exe** while internal project names are migrated incrementally.

## License and attribution

TaskHeroX is distributed under the **MIT License**. Portions of the codebase originate from the MIT-licensed `taskbarhero-bot` project and retain the required attribution. Bundled or referenced third-party components keep their own licenses.

See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for details.

## Disclaimer

TaskHeroX is an independent community project and is not affiliated with the developers or publishers of Taskbar Hero, Steam, or Valve. Memory modification and automation can have unintended effects; use at your own risk.
