# HardcoreWater 1.21.6 Compatibility Test Checklist

This checklist is intended for validating the 1.21.6 migration while preserving existing HardcoreWater behavior.

## Environment Setup

- [ ] Install Vintage Story `1.21.6`.
- [ ] Install `vs-hardcorewater` build under test.
- [ ] Install baseline `vssurvivalmod` and `vsessentials` matching game version.
- [ ] Enable detailed logging for mod startup and Harmony patch application messages.
- [ ] Use a clean test world plus one upgraded world from an older HardcoreWater version.

## Startup And Patch Safety

- [ ] Server starts cleanly when all expected finite-liquid methods are present.
- [ ] Server starts cleanly when one or more target methods are unavailable (patch skip warnings only, no crash).
- [ ] Client join triggers config sync with no null-channel errors.
- [ ] Config value is clamped when receiving malformed values (below `0.1` and above `10`).

## Aqueduct Source Acquisition And Decay

- [ ] Aqueduct fed by source in the same block correctly sets source state and fills to expected level.
- [ ] Aqueduct fed by source above correctly acquires source and fills to expected level.
- [ ] Aqueduct fed by side-adjacent source correctly acquires source.
- [ ] Aqueduct fed by waterfall adjacency obeys solid-face support checks.
- [ ] Aqueduct source removal triggers reacquire timeout and smooth level decay.
- [ ] Aqueduct resumes normal behavior after source returns.

## Source Invariants And Save/Load

- [ ] Save/load preserves valid `HasWaterSource` + `WaterSourcePos` states.
- [ ] Legacy/bad persisted state (`HasWaterSource=true`, `WaterSourcePos=null`) self-heals without crash.
- [ ] Chunk unload/reload at source does not create permanent phantom water.
- [ ] Two adjacent aqueduct dependency loops are prevented by invalid-source dependency checks.

## Liquid Type Preservation

- [ ] Freshwater remains freshwater through transport/refill.
- [ ] Saltwater remains saltwater through transport/refill.
- [ ] Boiling water remains boiling water through transport/refill.
- [ ] Iced states are not incorrectly overwritten by aqueduct fill logic.

## Finite Spreading Liquid Interaction

- [ ] Aqueduct paths are considered in downward path search where valid.
- [ ] Added aqueduct paths still respect barrier checks and replaceability constraints.
- [ ] Standard finite spread behavior in non-aqueduct terrain is unchanged.
- [ ] No runaway spread loops or severe tick spikes near dense aqueduct networks.

## Block Variant And Neighbor Update Behavior

- [ ] Open aqueduct updates connection variants correctly on place/break/neighbor change.
- [ ] Enclosed aqueduct placement and pick block behavior remain unchanged.
- [ ] Neighbor updates still propagate to adjacent aqueducts after state-changing events.

## Multiplayer And Networking

- [ ] Multiple players joining simultaneously receive config without exceptions.
- [ ] Late joiners receive current server config.
- [ ] Dedicated server + client behavior matches local host behavior.

## Regression Acceptance Criteria

- [ ] No server crashes from null source states or missing patch targets.
- [ ] No regressions in existing aqueduct gameplay flow and source logic.
- [ ] No obvious incompatibility with Survival/Essentials water behavior in standard scenarios.
- [ ] Logs contain only expected compatibility warnings and no repeated error spam.
