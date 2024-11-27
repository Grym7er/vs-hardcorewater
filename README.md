Hardcore Water : Transport Edition
=================

Overview
--------

Originally, this mode prevented water from being placed by buckets; now that source bucket prevention is a vanilla world option with VS 1.20.x, this mod is more focused on transportation methods to move water around.

This mod currently adds:

 * Aqueduct sections, made with bricks and mortar. One section must be connected to a source block to propagate water along a lenght of sections.


Config Settings (`VintageStoryData/ModConfig/HardcoreWater.json`)
--------

 * `AqueductUpdateFrequencySeconds`: Sets how often aqueducts are allowed to update; defaults to `0.5`.


Future Plans
--------

 * Mechanical screw pumps, which moves any adjacent water upward when powered; helpful for when water is needed at a higher elevation than nearby source blocks.


Known Issues
--------

 * Visual glitches can sometimes occur when adjacent to filled aqueducts and the camera is turned.
 * Water will not flow out of an aqueduct section downwards unless blocks are placed at least one side of the section.
