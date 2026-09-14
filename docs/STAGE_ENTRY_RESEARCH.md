# Stage-entry research (build 139467f3ad72)

## Status

`AutoBoss` and `Evolution` remain intentionally blocked at engine level. The old single `jgc(StageCache)` route no longer has a validated one-to-one replacement in build `139467f3ad72`.

## Recovered candidates

Reverse engineering of the current build found two genuine stage-entry validation methods called from the same stage flow:

- candidate A — RVA `0x99E5E0`: currently associated with stage-type branches 1/3 and capacity-like conditions.
- candidate B — RVA `0x99E750`: currently associated with stage-type 2 and nested resource/state conditions.

These observations are hypotheses from decompilation, not yet a safe runtime contract. Neither candidate must be assigned to the legacy `jgc` symbol until the return semantics are proven for the stage types actually used by TaskHeroX.

## Live read-only observation — 2026-09-14

Probe result on the validated game build:

- PID `20096`
- build `139467f3ad72`
- offsets loaded: `true`
- current progress: max `4310`, current `4309`, wave `0`
- live stage table: `189` entries
- type counts:
  - type `0`: `108`
  - type `1`: `12`
  - type `2`: `60`
  - type `3`: `9`
- `3310`: type `1`, next `4101`, soulstone `190003`
- `4310`: type `1`, next `0`, soulstone `190004`
- current `4309`: type `0`, next `4310`
- both candidate RVAs contain live executable code at the expected addresses.

### Scope reduction

The current TaskHeroX implementation does not need a generic replacement for every old `jgc` use before AutoBoss/Evolution can be reconsidered:

- `AutoBoss` only targets `3310` and `4310`, both confirmed type `1`.
- `Evolution` calls `CanEnter` only when the next stage is type `1`; normal type `0` movement uses `GoToStage` directly.
- Therefore candidate B / type `2` is **not a prerequisite** for reactivating only AutoBoss/Evolution.
- The immediate blocker is proving candidate A as the correct, side-effect-free type `1` validator with stable return semantics.

This does **not** justify enabling the features yet.

## Safety rule

Do not pick candidate A or B arbitrarily. Do not enable AutoBoss/Evolution merely because one candidate returns a plausible value for one stage. The current `AutomationLoop` block is the expected behavior while the type `1` validator is unresolved.

## Read-only probe

Normal run:

```powershell
dotnet run --project tools\TaskHeroX.StageEntryProbe
```

Deep code dump:

```powershell
dotnet run --project tools\TaskHeroX.StageEntryProbe -- --code-dump
```

The probe:

- attaches to the live game;
- refuses any build other than `139467f3ad72`;
- reads candidate method bytes only;
- optionally reads 768 bytes of each candidate for offline disassembly;
- reads current stage progress;
- groups the live stage table by `STAGETYPE`;
- prints representative `StageCache` pointers and metadata;
- prints soulstone and inventory/stash snapshots using existing read-only readers;
- does **not** call either candidate;
- does **not** navigate stages, write progress, consume soulstones, or patch memory.

## Unblock criteria for AutoBoss/Evolution

Before the engine-level block can be removed for these two features, all of the following must be established:

1. candidate A is confirmed as the type `1` validation route.
2. Return enum semantics for success/soulstone/capacity/failure are confirmed for type `1`.
3. Validation itself is proven not to reserve, consume, navigate, or mutate persistent stage/account state.
4. Controlled runtime observations match independently readable state.
5. Existing `jgd + jgk` boss-entry path remains unchanged unless separate evidence proves otherwise.
6. E2E remains green and AutoBoss/Evolution stay blocked on unknown builds.

Candidate B/type `2` can remain unresolved in this phase because current AutoBoss/Evolution do not use it.

Only after these conditions are met should TaskHeroX expose a typed stage-entry validator such as `CanEnterActBoss` instead of restoring the ambiguous legacy name `jgc`.
