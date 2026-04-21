# Aqueduct + Archimedes Validation Tests

This checklist verifies the aqueduct oscillation fix and protects expected behavior for vanilla and Archimedes water handling.

## Preconditions

- Install and enable `hardcorewater`.
- Install and enable `archimedes_screw`.
- Use a fresh test area with chunk boundaries visible (or known coordinates).
- Keep server logs visible while running tests.

## Test 1: Repro no longer oscillates (open aqueduct)

1. Build a working Archimedes screw assembly with outlet facing an open aqueduct line.
2. Place at least 2 connected open aqueduct segments at outlet height.
3. Ensure there is air under the first aqueduct segment (no support block).
4. Activate the screw.

Expected:

- First and second aqueduct fill and remain stable.
- No repeated `WaterLevel` countdown cycle (`6 -> 1 -> 6`).
- No visible rapid ebb/flow pulsing.

## Test 2: Repro no longer oscillates (enclosed aqueduct)

1. Repeat Test 1 using enclosed aqueduct segments.
2. Run once with air below first segment and once with enclosed aqueduct below source cell.

Expected:

- Stable fill in both setups.
- No oscillation loop.
- Enclosed aqueduct beneath source is treated as valid support where support-based branches apply.

## Test 3: Direct source continuity without support-below

1. Create a setup where `WaterSourcePos` resolves to a direct source fluid cell (not waterfall-derived).
2. Remove support beneath that source cell.
3. Keep screw active.

Expected:

- Direct source continuity remains valid without requiring support-below.
- Aqueduct does not immediately drop source state and enter decay/reacquire loop.

## Test 4: Waterfall-derived intake still enforces support

1. Build a waterfall-fed aqueduct capture setup.
2. Confirm it works with valid support below the intake/source location.
3. Remove support below the relevant waterfall/source location.

Expected:

- With support: flow is valid.
- Without support: waterfall-derived source validation fails as intended.
- No unintended bypass of support rules for waterfall branches.

## Test 5: Vanilla-only behavior regression check

1. Disable `archimedes_screw` and restart.
2. Repeat a basic vanilla aqueduct source and waterfall test.

Expected:

- Vanilla aqueduct source behavior remains unchanged.
- No new errors or degraded refill behavior.

## Test 6: Archimedes managed source recognition

1. Enable both mods.
2. Build screw + aqueduct line where managed Archimedes water reaches aqueduct cells.
3. Observe aqueduct source continuity over 60+ seconds.

Expected:

- Managed Archimedes source fluid is recognized as valid source while compat is active.
- Source state remains stable; no periodic source loss/reacquire churn.

## Test 7: Chunk-edge stability

1. Place screw/aqueduct setup crossing a chunk boundary.
2. Move away to unload upstream chunk; return to reload.

Expected:

- No permanent oscillation introduced after chunk reload.
- Source continuity recovers consistently with existing unload tolerance behavior.

## Test 8: Outlet ownership handoff sanity

1. Run Archimedes-powered aqueduct output for at least 1 minute.
2. Inspect logs/debug output for ownership assignment anomalies.

Expected:

- Ownership handoff behavior remains normal.
- No repeated ownership churn caused by aqueduct source instability.

## Pass Criteria

- Tests 1 and 2 pass with no visible oscillation.
- Test 4 confirms waterfall support semantics still hold.
- Tests 5 and 6 show no regressions in vanilla or Archimedes source handling.
- No build errors, no linter errors, and no recurring runtime warning spam related to source validation loops.
