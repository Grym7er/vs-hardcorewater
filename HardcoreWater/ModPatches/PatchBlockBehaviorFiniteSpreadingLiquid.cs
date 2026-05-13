using System.Collections.Generic;
using System.Linq;
using HardcoreWater.ModBlock;
using HardcoreWater.ModBlockEntity;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using System;
using AdditionalSpawnConstraints.ModPatches;

namespace AdditionalSpawnConstraints.ModPatches
{
	public class PatchBlockBehaviorFiniteSpreadingLiquid
	{


        /// <summary>
        /// Vanilla allows standard finite water to displace rapids at equal/lower level (waterwheel anti-exploit).
        /// Prevent normal water from overwriting rapids inside aqueduct channels so rapids can be transported.
        /// Water wheels and other vanilla behaviors on non-aqueduct cells are unchanged.
        /// Registered in HardcoreWaterModSystem.PatchBlockBehaviorFiniteSpreadingLiquidCanSpreadIntoBlock.
        /// </summary>
        internal static void PostfixCanSpreadIntoBlock(ref bool __result, Block ourblock, Block ourSolid, BlockPos pos, BlockPos npos, BlockFacing facing, IWorldAccessor world)
        {
            if (!HardcoreWater.HardcoreWaterConfig.Loaded.EnableAqueductRapids || !__result)
            {
                return;
            }

            Block neighborSolid = world.BlockAccessor.GetBlock(npos, BlockLayersAccess.SolidBlocks);
            Block neighborLiquid = world.BlockAccessor.GetBlock(npos, BlockLayersAccess.Fluid);
            if (neighborSolid is IAqueduct && PatchHelpers.IsRapidWaterBlockCode(neighborLiquid) && PatchHelpers.IsVanillaFiniteFreshWaterCode(ourblock))
            {
                __result = false;
            }
        }

        /// <summary>
        /// Vanilla <c>updateOwnFlowDir</c> rewrites rapid flow variants (still / n / e / w) from hydraulics, which fights our
        /// axis-aligned aqueduct fill and causes flicker. Skip that rewrite for rapid fluid in aqueduct channels that carry rapids.
        /// Does not run for <c>rapidwater-d-*</c> unless path is still rapidwater; horizontal channels are the intended case.
        /// Registered in HardcoreWaterModSystem.PatchBlockBehaviorFiniteSpreadingLiquidUpdateOwnFlowDir.
        /// </summary>
        internal static bool PrefixUpdateOwnFlowDir(BlockBehaviorFiniteSpreadingLiquid __instance, Block block, IWorldAccessor world, BlockPos pos)
        {
            Block solid = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.SolidBlocks);
            if (solid is IAqueduct)
            {
                // We don't want vanilla to handle flow dir stuff in aqueducts
                return false;
            }
            else{
                return true;
            }
        }

        [HarmonyPatch(typeof(BlockBehaviorFiniteSpreadingLiquid), "TryLoweringLiquidLevel")]
        [HarmonyPrefix]
		static bool PrefixTryLoweringLiquidLevel(BlockBehaviorFiniteSpreadingLiquid __instance, ref bool __result, Block ourBlock, IWorldAccessor world, BlockPos pos)
		{
            Block ourSolid = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);

            if (ourSolid is IAqueduct aqueduct)
            {
                if (ourBlock.GetBlockEntity<BlockEntityAqueduct>(pos) is BlockEntityAqueduct blockEntityAqueduct)
                {
                    // Add check to ignore liquid levels of 1, based on reports from Chronolegionnaire
                    if (ourBlock.LiquidLevel != 1 && ourBlock.LiquidLevel-1 <= blockEntityAqueduct.WaterLevel && blockEntityAqueduct.HasWaterSource)
                    {
                        __result = false;
                        return false; // skip original method
                    }
                }
            }

            return true; // resume original method
		}


        [HarmonyPatch(typeof(BlockBehaviorFiniteSpreadingLiquid), "FindDownwardPaths")]
        [HarmonyPostfix]
        static void PostfixFindDownwardPaths(BlockBehaviorFiniteSpreadingLiquid __instance, ref List<PosAndDist> __result, IWorldAccessor world, BlockPos pos, Block ourBlock)
        {
            // If solid block of water is aqueduct, add aqueduct directions to valid downward paths
            Block posBlock = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);
            if (__result != null && posBlock is BlockAqueduct blockAqueduct) // posBlock is either aqueduct or sluice because both implement IAqueduct
            {
                if (string.IsNullOrEmpty(blockAqueduct.Orientation))
                {
                    return;
                }

                    if (BlockFacing.FromFirstLetter(blockAqueduct.Orientation) == BlockFacing.NORTH || BlockFacing.FromFirstLetter(blockAqueduct.Orientation) == BlockFacing.SOUTH)
                    {
                        PatchHelpers.TryAddCandidatePath(__result, world, pos, pos.NorthCopy(), ourBlock);
                        PatchHelpers.TryAddCandidatePath(__result, world, pos, pos.SouthCopy(), ourBlock);
                        PatchHelpers.TryAddOpenOutletPath(__result, world, pos, pos.NorthCopy(), ourBlock);
                        PatchHelpers.TryAddOpenOutletPath(__result, world, pos, pos.SouthCopy(), ourBlock);
                    }
                    else
                    {
                        PatchHelpers.TryAddCandidatePath(__result, world, pos, pos.WestCopy(), ourBlock);
                        PatchHelpers.TryAddCandidatePath(__result, world, pos, pos.EastCopy(), ourBlock);
                        PatchHelpers.TryAddOpenOutletPath(__result, world, pos, pos.WestCopy(), ourBlock);
                        PatchHelpers.TryAddOpenOutletPath(__result, world, pos, pos.EastCopy(), ourBlock);
                    }



            }
        }
    }
}
    