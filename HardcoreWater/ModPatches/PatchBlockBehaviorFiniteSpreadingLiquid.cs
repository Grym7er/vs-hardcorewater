using System.Collections.Generic;
using System.Text.Json.Nodes;
using HardcoreWater.ModBlock;
using HardcoreWater.ModBlockEntity;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace AdditionalSpawnConstraints.ModPatches
{
	public class PatchBlockBehaviorFiniteSpreadingLiquid
	{
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
    }
}
    