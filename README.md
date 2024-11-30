Hardcore Water : Transport Edition
=================

Overview
--------

Originally, this mod prevented water from being placed by buckets; now that source bucket prevention is a vanilla world option with VS 1.20.x, this mod is more focused on transportation methods to move water around.

This mod currently adds:

 * Aqueduct sections, made with bricks and mortar. One section must be connected to a source block to propagate water along a length of sections.
   * Enclosed aqueducts which cannot feed aqueducts from the side, but can be used in room walls without affecting room integrity. Greenhouse-friendly.

Note that aqueducts can feed other aqueducts when placed orthogonal to each other, but only one-way. The source aqueduct in this arrangement will have smaller openings.


Config Settings (`VintageStoryData/ModConfig/HardcoreWater.json`)
--------

 * `AqueductUpdateFrequencySeconds`: Sets how often aqueducts are allowed to update, in seconds; defaults to `0.75`.


Future Plans
--------

 * Mechanical screw pumps, which moves any adjacent water upward when powered; helpful for when water is needed at a higher elevation than nearby source blocks.


Known Issues
--------

 * Visual glitches can sometimes occur when adjacent to filled aqueducts and the camera is turned.
 * Water will not flow out of an aqueduct section downwards unless a full block is placed below the end section.
