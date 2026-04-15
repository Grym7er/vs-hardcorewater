using System.Collections.Generic;
using System.Linq;
using HardcoreWater.ModBlock;
using HardcoreWater.ModBlockEntity;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AdditionalSpawnConstraints.ModPatches
{
	public class PatchBlockBehaviorFiniteSpreadingLiquid
	{
        private static bool IsValidAqueductPathCandidate(IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
        {
            BlockFacing facing = BlockFacing.HORIZONTALS.FirstOrDefault(f => sourcePos.AddCopy(f).Equals(candidatePos));
            if (facing == null) return false;

            Block sourceSolid = world.BlockAccessor.GetBlock(sourcePos, BlockLayersAccess.Solid);
            Block candidateSolid = world.BlockAccessor.GetBlock(candidatePos, BlockLayersAccess.Solid);
            Block candidateFluid = world.BlockAccessor.GetBlock(candidatePos, BlockLayersAccess.Fluid);

            if (sourceSolid == null || candidateSolid == null || candidateFluid == null) return false;
            if (!(candidateSolid is IAqueduct)) return false;

            // Respect standard spread constraints so appended paths don't bypass survival invariants.
            float sourceBarrier = sourceSolid.GetLiquidBarrierHeightOnSide(facing, sourcePos);
            float candidateBarrier = candidateSolid.GetLiquidBarrierHeightOnSide(facing.Opposite, candidatePos);
            if (sourceBarrier >= (float)ourBlock.LiquidLevel / 7f || candidateBarrier >= (float)ourBlock.LiquidLevel / 7f) return false;

            if (candidateFluid.BlockId != 0 && candidateFluid.Replaceable < ourBlock.Replaceable) return false;

            return true;
        }

        private static void TryAddCandidatePath(List<PosAndDist> paths, IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
        {
            if (!IsValidAqueductPathCandidate(world, sourcePos, candidatePos, ourBlock)) return;
            if (paths.Exists(pad => pad.pos.Equals(candidatePos))) return;

            paths.Add(new PosAndDist()
            {
                pos = candidatePos,
                dist = 1
            });
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

        /*
        [HarmonyPatch(typeof(BlockBehaviorFiniteSpreadingLiquid), "CanSpreadIntoBlock")]
        [HarmonyPrefix]
        static bool PrefixCanSpreadIntoBlock(BlockBehaviorFiniteSpreadingLiquid __instance, ref bool __result, string ___collidesWith, Block ourblock, Block ourSolid, BlockPos pos, BlockPos npos, BlockFacing facing, IWorldAccessor world)
        {
            if (ourSolid is IAqueduct aqueduct)
            {
                if (ourSolid.GetBlockEntity<BlockEntityAqueduct>(pos) is BlockEntityAqueduct blockEntityAqueduct)
                {
                    float liquidbarrier = ourSolid.GetLiquidBarrierHeightOnSide(facing, pos);
                    if (liquidbarrier >= (float)ourblock.LiquidLevel / 7f)
                    {
                        __result = false;
                        return false; // skip original method
                    }
                    if (world.BlockAccessor.GetBlock(npos, 1).GetLiquidBarrierHeightOnSide(facing.Opposite, npos) >= (float)ourblock.LiquidLevel / 7f)
                    {
                        __result = false;
                        return false; // skip original method
                    }
                    Block neighborLiquid = world.BlockAccessor.GetBlock(npos, 2);
                    if ((ourblock.LiquidCode == neighborLiquid.LiquidCode))
                    {
                        __result = neighborLiquid.LiquidLevel < ourblock.LiquidLevel;
                        return false; // skip original method
                    }
                    if (neighborLiquid.LiquidLevel == 7 && !(neighborLiquid.IsLiquid() && ourblock.IsLiquid() && neighborLiquid.LiquidCode == ___collidesWith))
                    {
                        __result = false;
                        return false; // skip original method
                    }
                    if (neighborLiquid.BlockId != 0)
                    {
                        __result = neighborLiquid.Replaceable >= ourblock.Replaceable;
                        return false; // skip original method
                    }

                    __result = ourblock.LiquidLevel > 1 || facing == BlockFacing.DOWN;
                    return false; // skip original method
                }
            }

            return true; // resume original method
        }
        */

        [HarmonyPatch(typeof(BlockBehaviorFiniteSpreadingLiquid), "FindDownwardPaths")]
        [HarmonyPostfix]
        static void PostfixFindDownwardPaths(BlockBehaviorFiniteSpreadingLiquid __instance, ref List<PosAndDist> __result, IWorldAccessor world, BlockPos pos, Block ourBlock)
        {
            // If solid block of water is aqueduct, add aqueduct directions to valid downward paths
            if (__result != null && world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid) is BlockAqueduct blockAqueduct)
            {
                if (string.IsNullOrEmpty(blockAqueduct.Orientation))
                {
                    return;
                }

                // Scan blocks front and back of the aqueduct
                if (BlockFacing.FromFirstLetter(blockAqueduct.Orientation) == BlockFacing.NORTH)
                {
                    TryAddCandidatePath(__result, world, pos, pos.NorthCopy(), ourBlock);
                    TryAddCandidatePath(__result, world, pos, pos.SouthCopy(), ourBlock);
                }
                else
                {
                    TryAddCandidatePath(__result, world, pos, pos.WestCopy(), ourBlock);
                    TryAddCandidatePath(__result, world, pos, pos.EastCopy(), ourBlock);
                }
            }
        }
    }
}
    