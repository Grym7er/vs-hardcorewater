# HardcoreWater Functionality Investigation Report

## Scope

This review focused on gameplay functionality, simulation correctness, performance characteristics, edge cases, and maintainability risks in:

- `HardcoreWater/ModBlockEntity/BlockEntityAqueduct.cs`
- `HardcoreWater/ModPatches/PatchBlockBehaviorFiniteSpreadingLiquid.cs`
- `HardcoreWater/ModBlock/BlockAqueduct.cs`
- `HardcoreWater/ModBlock/BlockEnclosedAqueduct.cs`
- `HardcoreWater/HardcoreWaterModSystem.cs`
- Supporting classes and docs (`HardcoreWaterConfig`, `AqueductDebugInfo`, `README.md`)

## Executive Summary

The mod is functionally solid and already includes meaningful hardening (config clamping, patch safety checks, deserialization invariant correction). The biggest remaining opportunities are in:

1. **Gameplay consistency** (source rules can feel permissive or unintuitive in some edge scenarios).
2. **Fluid-type correctness** (fallback logic can unintentionally coerce non-water fluids to freshwater behavior).
3. **Performance under scale** (high aqueduct counts can produce avoidable tick/update overhead).
4. **Rule clarity** (some existing behavior is technically valid but not obvious to players).

## High-Impact Findings

## 1) Adjacent aqueduct source check ignores `minLevel` intent

- **Location:** `BlockEntityAqueduct.IsValidFilledAqueduct()`
- **Issue:** The method accepts `minLevel` but does not use it; it currently uses `adjacentAqueduct.HasWaterSource` only.
- **Impact:**
  - Upstream aqueducts with a "source flag" but low effective fluid state can still be treated as fully valid suppliers.
  - This can make chain behavior feel too binary (all-or-nothing) and less physically intuitive.
- **Gameplay concern:** Players may see segments staying "logically sourced" even where expected pressure/level would be insufficient.
- **Recommendation:** Incorporate a level threshold check using `WaterLevel` and/or fluid layer level in addition to `HasWaterSource`.

## 2) Unloaded source chunk is treated as valid source in many validation branches

- **Location:** `BlockEntityAqueduct.onServerTick1s()`, `unloadedWaterSource` usage
- **Issue:** Source validity becomes true whenever source chunk is unloaded.
- **Impact:**
  - Preserves continuity across chunk borders (good), but can over-preserve source status when upstream changes while unloaded.
  - Can produce delayed or surprising correction when chunks reload.
- **Gameplay concern:** "Phantom supply" perception in long-distance systems.
- **Recommendation:** Keep chunk-tolerance, but add bounded grace behavior (for example: max tolerated unloaded duration before downgrade or soft decay mode).

## 3) Fluid family detection is hardcoded and can mis-handle modded liquids

- **Location:** `BlockEntityAqueduct.onServerTick1s()`
- **Issue:** Fluid family routing relies on string prefixes for salt/boiling and defaults everything else to freshwater.
- **Impact:**
  - Non-standard waterlike liquids from other mods may be coerced to `game:water-still-*`.
  - Potential for fluid conversion exploits or immersion break.
- **Gameplay concern:** Unexpected liquid type mutation in modded environments.
- **Recommendation:** Introduce explicit liquid-family mapping/allowlist and "do not refill if unknown family" fallback option.

## 4) Aqueduct path injection may still admit unintuitive spread paths

- **Location:** `PatchBlockBehaviorFiniteSpreadingLiquid.PostfixFindDownwardPaths()`
- **Issue:** Candidate filtering checks barriers and replaceability, but does not use aqueduct orientation/enclosure constraints when adding paths.
- **Impact:**
  - Water may route through aqueduct neighbors that are technically valid blocks but semantically wrong for intended channel direction.
- **Gameplay concern:** Spreading can seem to disregard visual/structural channel logic in specific layouts.
- **Recommendation:** Add candidate orientation compatibility checks (`IAqueduct.Orientation`, `IsEnclosed`) before adding path.

## Medium-Impact Findings

## 5) Reacquire timeout is fixed in ticks, not time-normalized

- **Location:** `BlockEntityAqueduct.WaterSourceReacquireTimeout` logic
- **Issue:** Timeout uses a hardcoded tick count (`4`) while tick frequency is configurable.
- **Impact:** Changing `AqueductUpdateFrequencySeconds` alters real-world timeout duration dramatically.
- **Gameplay concern:** Same config can make drain/reacquire feel either too snappy or too sluggish.
- **Recommendation:** Express timeout in seconds and convert to ticks based on configured update interval.

## 6) Discovery order is deterministic but not always intuitive

- **Location:** `BlockEntityAqueduct.onServerTick1s()` discovery branch
- **Issue:** First-match-wins search order can produce "valid but surprising" source selection in multi-source setups.
- **Impact:** Source may lock to a side source when players expect vertical source priority, or vice versa, depending on arrangement.
- **Gameplay concern:** Hard to predict behavior in complex networks.
- **Recommendation:** Document priority in handbook and optionally implement priority scoring (vertical > same-level > lateral, or shortest path).

## 7) Waterfall validation is brittle to variant assumptions

- **Location:** `IsValidWaterFall()`
- **Issue:** Uses `blockWaterfall.Variant["flow"] == "d"` assumption.
- **Impact:** If variant keying changes or modded waterfall variants differ, waterfall capture may silently stop working.
- **Gameplay concern:** Inconsistent waterfall intake behavior across environments.
- **Recommendation:** Add defensive variant-key checks and fallback behavior.

## 8) Frequent neighbor updates in decay/fill can amplify server load

- **Location:** `onServerTick1s()` (`TriggerNeighbourBlockUpdate` calls)
- **Issue:** Neighbor updates are triggered on many state transitions, including per-step decay.
- **Impact:** In large aqueduct systems, can create significant block update churn.
- **Gameplay concern:** Server TPS degradation in megabases.
- **Recommendation:** Coalesce updates (trigger only on state boundary changes or level delta thresholds).

## Low-Impact / Quality Findings

## 9) Repeated `BlockPos` allocations in hot tick path

- **Location:** `onServerTick1s()` (`NorthCopy`, `SouthCopy`, `UpCopy`, etc.)
- **Issue:** Frequent short-lived allocations every aqueduct tick.
- **Impact:** Minor per-instance overhead; meaningful at scale.
- **Recommendation:** Reuse cached positions where possible or minimize copies in repeated checks.

## 10) Commented patch block adds maintenance noise

- **Location:** `PatchBlockBehaviorFiniteSpreadingLiquid.cs`
- **Issue:** Large commented-out patch code can obscure active logic.
- **Impact:** Higher cognitive load during troubleshooting.
- **Recommendation:** Remove or move to design notes/docs.

## 11) Some known gameplay limitations remain undocumented in in-game-facing mechanics detail

- **Location:** `README.md` known issues + gameplay behavior
- **Issue:** Known constraints exist (for example downflow support requirement) but logic rationale is not deeply explained for players.
- **Impact:** Player confusion interpreted as bug.
- **Recommendation:** Expand handbook/README with "why this rule exists" and construction examples.

## Edge Cases Worth Explicitly Testing

1. **Chunk-edge network:** source unload/reload while downstream remains loaded.
2. **Multi-source junction:** two or more valid source candidates available simultaneously.
3. **Orthogonal mixed enclosure chains:** open + enclosed combinations around corners.
4. **Modded fluid presence:** non-vanilla fluid code in aqueduct fluid layer.
5. **High frequency config:** `AqueductUpdateFrequencySeconds=0.1` with large networks.
6. **Low frequency config:** `AqueductUpdateFrequencySeconds=10` and timeout behavior sanity.
7. **Waterfall variant mismatch:** waterfall with altered flow variant schema.
8. **Loop topology:** circular aqueduct systems with split/merge branches.

## Efficiency Improvement Opportunities

## Quick wins

- Time-normalize reacquire timeout (seconds -> ticks conversion). **Implemented**
- Reduce redundant neighbor update calls. **Implemented**
- Add orientation compatibility in path candidate checks. **Implemented**
- Avoid unnecessary MarkDirty/neighbor updates when no effective state change occurred. **Implemented**

## Implemented Quick Wins (Current Status)

The following quick wins have been implemented in code:

1. **Time-normalized reacquire timeout**
   - **File:** `HardcoreWater/ModBlockEntity/BlockEntityAqueduct.cs`
   - Replaced hardcoded timeout tick usage with a duration-based model (`ReacquireTimeoutSeconds`) converted into ticks via current configured update interval.
   - Behavior is now stable across different `AqueductUpdateFrequencySeconds` settings.

2. **Reduced redundant neighbor update emissions**
   - **File:** `HardcoreWater/ModBlockEntity/BlockEntityAqueduct.cs`
   - Neighbor updates are now emitted through coalesced flags rather than repeated unconditional calls in multiple branches.
   - Update emission is tied to effective state transitions (primarily level/fluid changes), reducing per-tick churn.

3. **State-delta guards for dirty/update calls**
   - **File:** `HardcoreWater/ModBlockEntity/BlockEntityAqueduct.cs`
   - Added guards to avoid unnecessary `MarkDirty(true)` and neighbor updates when no meaningful block-entity state changed.
   - Includes state tracking for `HasWaterSource`, `WaterSourcePos`, and `WaterLevel`.

4. **Orientation-compatible path candidate filtering**
   - **File:** `HardcoreWater/ModPatches/PatchBlockBehaviorFiniteSpreadingLiquid.cs`
   - Added candidate-side acceptance checks (`AcceptsFlowFromSide`) before appending aqueduct path candidates.
   - Prevents path injection into aqueduct neighbors that do not accept flow from the incoming side implied by source-to-candidate direction.

## Observed / Expected Impact After Quick Wins

- **Simulation consistency:** Reacquire/decay timing remains consistent when server owners change aqueduct update frequency.
- **Performance:** Fewer redundant neighbor updates and dirty marks in steady-state or no-op ticks.
- **Flow semantics:** Appended liquid paths better respect aqueduct orientation expectations.
- **Maintainability:** Tick-side effects are more explicit and easier to reason about through consolidated update flags.

## Validation Snapshot (Post-Implementation)

- `dotnet build HardcoreWater/HardcoreWater.csproj -c Debug` succeeded.
- `dotnet build HardcoreWater/HardcoreWater.csproj -c Release` succeeded.
- Quick-win code checks:
  - No remaining hardcoded `WaterSourceReacquireTimeout = 4` assignments.
  - Orientation-side compatibility helper present in path candidate filter.
  - Neighbor update call count in aqueduct tick path reduced and consolidated.

## Structural improvements

- Refactor discovery and validation checks into a rule table or strategy methods for easier maintenance.
- Add optional debug counters for server profiling (`ticks processed`, `source transitions`, `neighbor updates triggered`).
- Introduce a small deterministic source-selection scoring model to reduce player confusion.

## Gameplay Logic Improvements (Make Behavior More Intuitive)

1. **Pressure-like propagation option**
   - Require minimum upstream `WaterLevel` for downstream sourcing (instead of only `HasWaterSource`).
2. **Unknown-fluid safety mode**
   - "Do not refill unknown liquid families" to prevent accidental conversion.
3. **Configurable source persistence**
   - Let server owners tune chunk-unloaded grace behavior.
4. **Priority transparency**
   - Publish explicit source-selection order in handbook and possibly debug info.

## Recommended Implementation Priority

1. **High priority**
   - Fix `IsValidFilledAqueduct()` level semantics.
   - Harden fluid-family handling for unknown liquids.
2. **Medium priority**
   - Add waterfall variant-key defensiveness.
3. **Lower priority**
   - Allocation micro-optimizations.
   - Comment cleanup and additional player documentation polish.

## Final Assessment

Current functionality is generally robust and playable, especially after recent stability hardening. The next wave of improvement should focus on **predictable gameplay semantics under complex builds** and **scalability under large aqueduct networks**. Addressing the highlighted high-priority items will likely deliver the largest player-facing quality gains with relatively low implementation risk.
