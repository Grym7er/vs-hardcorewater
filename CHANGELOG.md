### v1.20.x-1.3.4

- Fix disconnected aqueduct case staying filled

### v1.20.x-1.3.3

- Fixed aqueduct end flow issues by patching FindDownwardPaths in BlockBehaviorFiniteSpreadingLiquid
- Changed water source validity checks to make aqueduct usage a bit more intuitive
- Added spanish translations; thanks to C4BR3R4!

### v1.20.x-1.3.2

- Fixed opaque/solid side issues with enclosed aqueducts
- Change patch to `TryLoweringLiquidLevel` to also ignore water levels of 1, and use `HasWaterSource` instead of `WaterPos != null`

### v1.20.x-1.3.1

- Changed wooden aqueduct recipe to be slightly cheaper

### v1.20.x-1.3.0

- Added wooden aqueducts
- Fixed UVs of stone aqueducts
- Fixed case where aqueducts had invalid source and did not update
- Fixed aqueduct block entity getting deleted when block was changed due to adjacent aqueduct being placed 

### v1.20.x-1.2.0

- Added enclosed aqueduct sections that do not affect room integrity
- Fixed collision boxes for aqueduct sections
- Changed aqueducts to not consider enclosed sections as sources from the enclosed sides

### v1.20.x-1.1.1

- Fixed aqueducts recipe
- Allowed aqueducts to fill from above waterfall
- Fixed aqueduct constant updating
- Added feed connectors to models when aqueducts are connected to them

### v1.20.x-1.1.0

- Removed patch to bucket to prevent source block placement as it is now vanilla feature
- Added aqueducts

### v1.19.7-1.0.0

- Initial release
