# Stage-entry research (build 139467f3ad72)

## Status

`AutoBoss` and `Evolution` remain intentionally blocked at engine level. The old single `jgc(StageCache)` route no longer has a validated one-to-one replacement in build `139467f3ad72`.

## Recovered candidates

Reverse engineering of the current build found two genuine stage-entry validation methods called from the same stage flow:

- candidate A — RVA `0x99E5E0`: currently associated with stage-type branches 1/3 and capacity-like conditions.
- candidate B — RVA `0x99E750`: currently associated with stage-type 2 and nested resource/state conditions.

These observations are hypotheses from decompilation, not yet a safe runtime contract. Neither candidate must be assigned to the legacy `jgc` symbol until the return semantics are proven for every stage type used by TaskHeroX.

## Safety rule

Do not pick candidate A or B arbitrarily. Do not enable AutoBoss/Evolution merely because one candidate returns a plausible value for one stage. The current `AutomationLoop` block is the expected behavior while `jgc` is unresolved.

## Read-only probe

Run:

```powershell
dotnet run --project tools\TaskHeroX.StageEntryProbe
```

The probe:

- attaches to the live game;
- refuses any build other than `139467f3ad72`;
- reads the first bytes of both candidate methods;
- reads current stage progress;
- groups the live stage table by `STAGETYPE`;
- prints representative `StageCache` pointers and metadata;
- does **not** call either candidate;
- does **not** navigate stages, write progress, consume soulstones, or patch memory.

## Unblock criteria

Before `jgc` can be restored, all of the following must be established:

1. Exact mapping between stage type and candidate method.
2. Return enum semantics for success/end-stage/soulstone/chest-space/failure on each applicable type.
3. Confirmation that validation calls themselves are side-effect free.
4. At least one controlled runtime observation for each relevant stage type.
5. Existing `jgd + jgk` boss-entry path remains unchanged unless separate evidence proves otherwise.
6. E2E remains green and AutoBoss/Evolution stay blocked on unknown builds.

Only after these conditions are met should TaskHeroX expose a typed stage-entry validator instead of reusing the ambiguous legacy name `jgc`.
