# HardcoreWater Feature Description

## Overview

HardcoreWater extends Vintage Story finite-liquid behavior by introducing aqueduct-based water transport mechanics.  
The mod allows players to build directional water channels that can acquire and maintain fluid from nearby valid sources while still interacting with the base finite-spreading liquid system.

At runtime, the mod is composed of:

- A mod system (`HardcoreWaterModSystem`) that loads/synchronizes config and installs Harmony patches.
- Two aqueduct block classes:
  - Open aqueduct (`BlockAqueduct`)
  - Enclosed aqueduct (`BlockEnclosedAqueduct`)
- A shared block entity (`BlockEntityAqueduct`) that drives source detection, fluid level state, and refill/decay behavior.
- A network packet (`SyncConfigClientPacket`) used for server-authoritative config sync to clients.
- Harmony patches for finite-liquid behavior (`PatchBlockBehaviorFiniteSpreadingLiquid`).

## Gameplay Features

## 1) Aqueduct Blocks

### Open Aqueduct

The open aqueduct block supports dynamic connection variants and directional flow orientation.

- Orientation is selected at placement time (`ns` or `we`) based on player placement orientation.
- Connections are recalculated against neighboring aqueduct blocks.
- The block uses variant switching via `ExchangeBlock` to keep visual connections in sync while preserving block entity data.
- When broken or neighboring blocks change, neighbor updates are triggered to keep adjacent aqueducts coherent.

### Enclosed Aqueduct

The enclosed aqueduct is a directional variant that does not expose open side connection variants in the same way.

- Placement still resolves orientation from player direction.
- It participates in aqueduct source logic through the same block entity.
- It triggers neighbor updates on break for chain consistency.

## 2) Source Acquisition And Water Transport

Each aqueduct block entity runs a periodic server tick and manages these state variables:

- `WaterLevel`: internal desired liquid level.
- `HasWaterSource`: whether the aqueduct currently has a valid source.
- `WaterSourcePos`: source position reference if a source is active.
- `WaterSourceReacquireTimeout`: cooldown/decay timer after source loss.

### Source discovery

Source discovery runs only when `HasWaterSource` is false and the reacquire timeout has expired.  
At this point the aqueduct performs an ordered search and takes the first valid candidate.  
This priority order matters because it determines which source is remembered in `WaterSourcePos`, which in turn drives later validation and dependency checks.

The current discovery order is:

1. **Source in same block (`this.Pos`)**  
   - If the fluid layer in the aqueduct block itself is a strong source (level threshold check), the aqueduct immediately self-sources.  
   - Water level is set to full (`7`) and `WaterSourcePos` becomes `this.Pos`.  
   - This is the most stable case and is evaluated first.

2. **Source directly above (`this.Pos.UpCopy()`)**  
   - If a source exists above, the aqueduct adopts it and sets internal level to `6` (downstream-from-source behavior).  
   - This models gravity-fed intake from vertical stacks or overhead channels.

3. **Waterfall / flowing-above combinations**  
   - The aqueduct accepts specific above-block dynamic-flow patterns, including waterfall-compatible states.  
   - This allows capture from moving water features rather than requiring only still source blocks.

4. **Filled aqueduct directly above**  
   - If the block above is another aqueduct with a valid source state, the lower aqueduct can chain from it.  
   - This is the primary vertical network propagation path.

5. **Front/back endpoints (orientation-aware side scan)**  
   - If no above candidate is valid, the aqueduct scans its two axial end positions (north/south for `ns`, west/east for `we`).  
   - At each end, it attempts in order:
     - direct source
     - supported waterfall
     - supported waterfall + flowing-above pattern
     - adjacent filled aqueduct
   - First valid endpoint wins and is recorded as `WaterSourcePos`.

### Discovery examples

- **Example A: Intake basin feeds line**  
  A source block is placed inside the first aqueduct segment. That segment self-sources at level `7`; downstream segments then discover via adjacent aqueduct chaining and settle at transport levels.

- **Example B: Elevated head tank**  
  A reservoir sits one block above an aqueduct trunk. Each aqueduct segment under a valid above source acquires from `upwardPos`, enabling gravity-fed transfer without placing source in each segment.

- **Example C: Waterfall capture point**  
  An aqueduct terminus is placed at waterfall edge with proper support beneath the capture block. The endpoint can acquire from waterfall/flow criteria and propagate laterally into the line.

- **Example D: Orthogonal junction behavior**  
  Two aqueduct lines cross orthogonally. Discovery can choose one side as source, but later validation/dependency checks prevent unstable mutual-source loops from persisting.

### Source validation

Once `HasWaterSource` is true, each server tick re-validates that source before refilling fluid.  
Validation is intentionally strict: discovery can be permissive to establish flow quickly, but ongoing validity must prevent phantom water, invalid chains, and circular dependencies.

Validation evaluates multiple criteria against the remembered `WaterSourcePos`:

1. **Direct local continuity**  
   - If the current aqueduct position still contains a valid source-level fluid, it remains valid even if upstream topology changed.

2. **Referenced source integrity**  
   - If `WaterSourcePos` points to a source block that still satisfies level/type requirements (plus structural support checks where required), source remains valid.

3. **Waterfall/flow pattern continuity**  
   - If source was effectively dynamic flow (waterfall or flowing-above combination), those conditions must still match accepted rules.

4. **Aqueduct-chain integrity**  
   - If source is another aqueduct, that upstream aqueduct must still represent a valid sourced state and acceptable orientation relationship.

5. **Chunk-load tolerance**  
   - If the source chunk is temporarily unloaded, validation can treat the source as temporarily unresolved rather than immediately tearing down the network.  
   - This reduces thrash at chunk borders and during travel/load transitions.

6. **Dependency loop invalidation**  
   - Additional checks invalidate patterns where aqueducts end up sourcing each other in an unstable loop (for example A<->B mutual dependence with side-fed neighbors).  
   - This is critical for preventing self-sustaining artificial water states.

If validation fails, source state is dropped and decay/reacquire timeout begins.

### Validation failure outcomes

When a source becomes invalid:

- `HasWaterSource` is set to false.
- `WaterSourcePos` is cleared.
- `WaterSourceReacquireTimeout` is initialized.
- Aqueduct begins controlled level decay and notifies neighbors.

This creates a smooth transition from powered flow to dry state instead of abrupt fluid disappearance.

### Validation examples

- **Example E: Source removed by player**  
  Player removes upstream source block. On subsequent ticks, referenced-source validation fails, aqueduct enters timeout/decay, and downstream line drains gradually.

- **Example F: Temporary chunk unload**  
  Source is beyond loaded chunk boundary. Validation treats source as temporarily unresolved; when chunk reloads, source can continue without full network reset if conditions are still valid.

- **Example G: Broken structural support under waterfall intake**  
  Support block beneath a waterfall-fed intake is removed. Support-sensitive validation path fails; aqueduct drops source and decays.

- **Example H: Circular feed construction**  
  Player builds a loop where two aqueducts can each appear to feed the other. Dependency-loop checks invalidate the cycle, preventing perpetual source state without a real upstream input.

### Reacquire timeout and decay

When source is lost:

- `WaterSourceReacquireTimeout` is set and ticks down.
- `WaterLevel` decays over time.
- Neighbor block updates are triggered so fluid simulations and connected blocks react promptly.

## 3) Liquid Family Preservation

The refill logic preserves liquid family to avoid fluid-type corruption during aqueduct transport:

- Freshwater path (`game:water-still-*`)
- Saltwater path (`game:saltwater-still-*`)
- Boiling water path (`game:boilingwater-still-*`)

The current local fluid code is inspected and the corresponding still-fluid block is selected for refill.  
Ice variants are guarded so frozen water is not blindly overwritten by aqueduct refill behavior.

## 4) Finite-Liquid Engine Integration (Harmony)

The mod integrates with finite-spreading liquid behavior via Harmony:

- Prefix on `TryLoweringLiquidLevel`:
  - Prevents normal decay under aqueduct-controlled conditions where source-backed level should persist.
- Postfix on `FindDownwardPaths`:
  - Adds aqueduct-oriented side path candidates so liquids can continue through aqueduct routes.
  - Added paths are filtered by barrier and replaceability checks to avoid bypassing core spread constraints.

The patching layer now includes method-lookup guards to avoid startup crashes when target signatures differ between game versions.

## 5) Network And Config Behavior

The mod exposes one runtime config field:

- `AqueductUpdateFrequencySeconds`

### Config lifecycle

- Config is loaded from `HardcoreWater.json` during `StartPre`.
- Missing or invalid config falls back to defaults.
- Values are sanitized and clamped (`0.1` to `10` seconds) to prevent pathological tick rates.

### Server-client sync

- Server registers a network channel and sends current config to players on join.
- Client receives packet and updates local loaded config.
- Received values are sanitized again on client side.

This keeps client-side calculations and display behavior aligned with server runtime configuration.

## 6) Persistence And Save Compatibility

Aqueduct runtime state is serialized into tree attributes:

- `WaterLevel`
- `WaterSourceReacquireTimeout`
- `HasWaterSource`
- `WaterSourcePos` (when source is active)

On load, state is normalized to protect against broken or legacy values.  
Specifically, impossible combinations (source flag true but missing source position) are corrected to a safe non-source state and moved into reacquire handling.

## 7) Variant And Neighbor Consistency Model

Open aqueduct blocks continuously maintain visual/functional correctness through:

- Placement-time connection determination.
- Neighbor-change recalculation.
- Block exchange to switch variants while preserving BE state.
- Explicit neighbor notifications after relevant state changes.

This ensures long aqueduct runs remain visually and mechanically coherent without manual re-placement.

## 8) Debug Information

Both aqueduct block types expose extended debug details when enabled in client settings:

- Internal water level
- Current source position
- Current block position
- Current fluid block code
- Liquid barrier presence per cardinal side

Debug info generation is centralized through a shared helper to keep behavior identical across open and enclosed aqueduct classes.

## 9) Safety And Compatibility Hardening

The current implementation includes specific hardening for cross-version robustness:

- Defensive Harmony target resolution with warning-only fallback.
- Startup ordering fix to avoid join-time null channel access.
- Config clamping at load and network receive points.
- Runtime invariant checks to avoid null source dereference in tick loop.
- Orientation/variant guards to reduce malformed-state crashes.

These changes are designed to maintain existing gameplay intent while reducing failure modes in 1.21.6 environments.

## Functional Boundaries And Non-Goals

HardcoreWater intentionally does not replace the full finite-liquid system.  
Instead, it layers aqueduct transport behavior on top of base liquid logic, with selective hook points for decay/path adjustments around aqueduct blocks only.

The mod is focused on:

- Physical infrastructure transport gameplay
- Source continuity simulation
- Integration with finite liquid rules

It is not intended as a full custom fluid engine.
