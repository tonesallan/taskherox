# Stage-entry research — legacy 139467f3ad72 and current c265dc8bc7aa

## Status

The old single `jgc(StageCache)` route split in build `139467f3ad72`.

For the legacy build `139467f3ad72`, candidate A (RVA `0x99E5E0`) was validated live for `STAGETYPE=1`:

- `Success(0)` confirmed on stage `3310` with available Hell soulstones and free capacity;
- `NeedSoulStone(2)` confirmed twice on stage `4310` with zero Torment soulstones;
- tested calls did not consume soulstones;
- tested calls did not change `maxCompletedStage` or `currentStageKey`;
- the game process remained alive;
- the dispatcher hook was removed/restored after every controlled call.

TaskHeroX therefore exposes this route only as a typed type-1 validator. It does **not** restore candidate A under the ambiguous legacy `jgc` name.

Candidate B (RVA `0x99E750`) / type `2` remains unresolved and is not exposed by this work. Type `3` is also not enabled by inference.

## Recovered candidates

Reverse engineering of the current build found two genuine stage-entry validation methods called from the same stage flow:

- candidate A — RVA `0x99E5E0`: associated with stage-type branches 1/3 and resource/capacity conditions;
- candidate B — RVA `0x99E750`: associated with stage type 2 and nested resource/state conditions.

Only candidate A / `STAGETYPE=1` has the runtime contract required by current AutoBoss/Evolution.

## Live observations — 2026-09-14

Validated game build:

- PID `20096` during the test session;
- build `139467f3ad72`;
- offsets loaded: `true`;
- live stage table: `189` entries;
- type `0`: `108`;
- type `1`: `12`;
- type `2`: `60`;
- type `3`: `9`;
- `3310`: type `1`, next `4101`, soulstone `190003`;
- `4310`: type `1`, next `0`, soulstone `190004`;
- current `4309`: type `0`, next `4310`.

### Controlled semantic tests

Missing-resource branch:

```text
4310 / type=1 / Torment stone 190004 = 0
candidate-A(4310) => 2
stone: 0 -> 0
max/cur: 4310/4309 -> 4310/4309
```

This result was reproduced twice.

Success branch:

```text
3310 / type=1 / Hell stone 190003 = 16
inventory free = 76
stash free = 244
candidate-A(3310) => 0
stone: 16 -> 16
max/cur: 4310/4309 -> 4310/4309
```

`currentStageWave` changed during one success run. Wave is live combat state and advances asynchronously while the game continues running, so it is recorded but is not used as the invariant for the validator. No persistent stage progression field used by the gate changed.

## Why type 1 is enough for this phase

The current TaskHeroX implementation does not need a generic replacement for every old `jgc` use:

- `AutoBoss` targets `3310` and `4310`, both type `1`;
- `Evolution` calls `CanEnter` only when the next stage is type `1`;
- normal type `0` movement uses `GoToStage` directly;
- candidate B / type `2` is therefore not required to recover current AutoBoss/Evolution.

## Production route

`StageNav.CanEnter(key)` now follows these rules:

1. If a legacy build has a validated `jgc`, keep using it.
2. If the exact structural-symbol tuple for build `139467f3ad72` is present, the split route is available.
3. The split route calls RVA `0x99E5E0` only when the target stage is `STAGETYPE=1`.
4. Type `2` and type `3` return no split validation route.

`AutomationLoop` now gates AutoBoss/Evolution on the typed type-1 capability rather than on the presence of legacy `jgc`.

The Compatibility Center reports this explicitly as a validated split type-1 route.

## Safety rules kept after unblocking

- Do not assign candidate A or B back to `jgc`.
- Do not expose candidate B/type `2` until independently validated.
- Do not expose type `3` merely because candidate A's decompilation contains a type-3 branch.
- Unknown builds remain blocked unless they have a separately validated route.
- Existing `jgd + jgk` boss-entry behavior is unchanged.
- Game Speed is outside this work.

## Diagnostic probes

Read-only probe:

```powershell
dotnet run --project tools\TaskHeroX.StageEntryProbe
```

Deep code dump:

```powershell
dotnet run --project tools\TaskHeroX.StageEntryProbe -- --code-dump
```

Direct semantic probe:

```powershell
dotnet run --project tools\TaskHeroX.StageEntrySemanticProbe

dotnet run --project tools\TaskHeroX.StageEntrySemanticProbe -- --success
```

Production-route semantic probe:

```powershell
dotnet run --project tools\TaskHeroX.StageEntrySemanticProbe -- --production-route

dotnet run --project tools\TaskHeroX.StageEntrySemanticProbe -- --success --production-route
```

The semantic probe still refuses the wrong build, checks type/resource/capacity preconditions, calls only the validator once, removes the dispatcher hook in `finally`, and compares resource plus max/cur state before/after.

## Current build validation (`c265dc8bc7aa`)

The current build was re-extracted and validated independently.

Resolved runtime stage fields:

- `uo_max = 0x50`
- `uo_cur = 0x80`
- `uo_wave = 0x90`
- `uo_cur_cache = 0xA8`

Resolved stage-entry validators:

- type 1/3 validator = `0x9A9F20`
- type 2 validator = `0x9AA0C0`
- boss entry callback path `jgd = 0x9AA370`

The extractor was upgraded to V8 so these fields are resolved semantically instead of by static-field position. The generated cache for `c265dc8bc7aa` reproduces the validated runtime offsets and does not emit a generic `jgc`.

## Final validation status

Completed successfully:

- direct `NeedSoulStone(2)` semantic probe;
- direct `Success(0)` semantic probe;
- production-route stage-entry probe;
- AutoBoss production smoke;
- Evolution production smoke;
- local E2E: `19 PASS / 0 FAIL`;
- unit tests: `21 / 21`;
- solution build;
- GitHub Actions `TaskHeroX CI #65`.

Candidate B/type `2` remains outside the production route because current AutoBoss/Evolution do not require it.
