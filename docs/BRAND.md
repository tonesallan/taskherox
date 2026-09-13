# TaskHeroX Visual System

## Identity

**Name:** TaskHeroX  
**Short mark:** `X`  
**Descriptor:** `CONTROL PLATFORM`  
**Context line:** `TASKBAR HERO // EXTERNAL TOOLKIT`

TaskHeroX should look like a technical desktop control platform rather than a generic cheat menu. The interface must communicate status, compatibility and recoverability before raw controls.

## Color system

The current WPF theme is intentionally retained as the base and reinterpreted as the TaskHeroX palette:

- **Void** `#07090F` — application background.
- **Surface** `#0B0E17` — navigation and structural panels.
- **Signal Orange** `#FF7A18` — primary action / connected state.
- **Amber** `#FBBF24` — warnings, forced values and compatibility attention.
- **Pulse Cyan** `#22D3EE` — diagnostics, secondary technical accent and the final stop in the X mark gradient.
- **Critical Red** `#FF2A55` — offline / failure / destructive warning.
- **Primary text** `#E2E8F0`.
- **Muted text** `#94A3B8`.

The `X` mark uses a three-stop gradient: Orange → Amber → Cyan.

## Typography

- **Outfit** — UI headings, controls and navigation.
- **JetBrains Mono** — hashes, build identifiers, status text, technical labels and diagnostics.

## Information architecture

### Dashboard

The primary screen should evolve into four vertical zones:

1. **Live Status** — game process, build hash, offset compatibility and engine state.
2. **Quick Controls** — protection, multipliers and restore action.
3. **Automation** — box, stash, fuse, boss/evolution and watchdog.
4. **Character / Stage Overrides** — advanced controls isolated from everyday actions.

### Inventory

Keep item listing, filtering and Steam Market information together. Market-dependent actions must be visually distinct from local-only memory tools.

### Market Intel

Price lookup and overlay functions live here. This area should never be visually mixed with progression editing.

### Runes

Interactive rune tree and rune-specific operations.

### Stage Navigator

Stage map, current/max/wave status and progression tools. Build-sensitive stage-entry automation must expose compatibility status before enabling controls.

## Safety UX

Every feature belongs to one of three states:

- `READY` — resolver/offset validated.
- `LIMITED` — only a safe subset is available.
- `UNAVAILABLE` — feature remains disabled instead of guessing addresses.

Future versions should surface these states in a dedicated Compatibility Center.

## Naming rules

Visible product strings use **TaskHeroX**. Internal legacy project names (`TbhBot.*`) may remain temporarily while project and namespace migration is performed in buildable stages.

New public-facing files, releases, executables and updater identifiers should use `TaskHeroX` immediately.
