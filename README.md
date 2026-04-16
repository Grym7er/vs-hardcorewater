Hardcore Water : Transport Edition
=================

Local Build Prerequisites
--------

* .NET SDK `8.0.125` (pinned via `global.json`)
* Vintage Story `1.21.6`
* A valid game install path exposed via `VINTAGE_STORY`, or available at fallback path:
  * Linux/macOS fallback: `/home/dewet/Games/vintagestory`
  * Windows fallback: `C:\Program Files\Vintage Story`

Set `VINTAGE_STORY` explicitly if your install is elsewhere:

* Linux/macOS:
  * `export VINTAGE_STORY="/path/to/Vintage Story"`
* Windows PowerShell:
  * `$env:VINTAGE_STORY="C:\\Path\\To\\Vintage Story"`

Daily Build Flow
--------

For normal local compilation (does not require Cake project):

* Debug build:
  * `./build_mod.sh`
* Release build:
  * `./build_mod.sh Release`

Output path:

* `HardcoreWater/bin/<Configuration>/Mods/mod`

Build And Deploy (Debug)
--------

To build, zip, and copy directly to your local mods folder:

* `./build_and_deploy_debug.sh`

Override mods deployment folder (optional):

* `export VINTAGE_STORY_MODS_DIR="/path/to/VintagestoryData/Mods"`

Packaging Flow (Cake)
--------

If you want packaged release output with Cake tasks:

* `./build.sh`

This uses `CakeBuild` and is optional for normal compile-test iteration.

Overview
--------

Originally, this mod prevented water from being placed by buckets; now that source bucket prevention is a vanilla world option with VS 1.20.x, this mod is more focused on transportation methods to move water around.

This mod currently adds:

* Aqueduct sections
  * Can be made with 3 bricks and 1 mortar (creates 3 sections), or a debarked log and resin (creates 1 section, requires hammer and chisel).
  * One section must be connected to a source block to propagate water along a length of sections.
  * Enclosed aqueducts which cannot feed aqueducts from the side, but can be used in room walls without affecting room integrity. 

Note that aqueducts can feed other aqueducts when placed orthogonal to each other, but only one-way. The source aqueduct in this arrangement will have smaller openings.


Config Settings (`VintageStoryData/ModConfig/HardcoreWater.json`)
--------

* `AqueductUpdateFrequencySeconds`: Sets how often aqueducts are allowed to update, in seconds; defaults to `0.75`.
* `UnresolvedOwnerFallbackMode`: Controls Archimedes compat behavior when managed-family owner tracing fails:
  * `VanillaFallback` (default): fallback to vanilla still outflow blocks.
  * `SkipRefill`: do not refill that aqueduct segment until owner resolution succeeds.

Archimedes Compatibility Contract (Runtime)
--------

When Archimedes Screw is installed, HardcoreWater compatibility expects:

* Mod system type `ArchimedesScrew.ArchimedesScrewModSystem`.
* Public `WaterManager` property on that mod system.
* `WaterManager` methods:
  * `TryResolveManagedWaterFamily`
  * `TryResolveVanillaWaterFamily`
  * `GetManagedBlock`
  * `AssignOwnedSourceForController`
  * `TryGetSourceOwner`
  * `IsArchimedesSourceBlock`
  * `IsArchimedesWaterBlock`

If this contract is unavailable or changes at runtime, compat deactivates cleanly and aqueduct refill falls back according to `UnresolvedOwnerFallbackMode`.

Runtime behavior notes:

* Compat initialization remains startup + `SaveGameLoaded` first, then uses a bounded low-frequency recovery loop only when Archimedes is installed and compat is inactive.
* String-based mod-system fallback resolves by mod ID (`thetruearchimedesscrew`, then `archimedes_screw`) to avoid type-name lookup mismatch.
* When active, compat emits rate-limited debug summaries to aid live diagnostics without log spam.
* Owner tracing includes a short-lived conservative cache for unloaded-source-chunk cases; if no safe cached mapping exists, behavior falls back to normal unresolved-owner handling.


Future Plans
--------

* Mechanical screw pumps and/or water-lifting devices; moves any adjacent water upward when powered; helpful for when water is needed at a higher elevation than nearby source blocks.


Known Issues
--------

* Visual glitches can sometimes occur when adjacent to filled aqueducts and the camera is turned.
* Water will not flow out of an aqueduct section downwards unless a full block is placed below the end section.
* No compatibility yet with Wildcraft Trees.
