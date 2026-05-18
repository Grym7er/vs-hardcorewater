using System.Collections.Generic;
using System.Linq;
using HardcoreWater.ModBlock;
using HardcoreWater.ModBlockEntity;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using System;
using System.Reflection;
using System.Collections;
namespace AdditionalSpawnConstraints.ModPatches
{


    public class PatchBlockBehaviorCollapseStory
    {

        static FieldInfo CollapseInProgress;
        static FieldInfo ChiselAggregateCache;
        static FieldInfo StressCache;
        static MethodInfo ChiselAggregateCache_Remove;
        static MethodInfo StressCache_Remove;




        internal static bool PrefixCollapseLayer(IWorldAccessor world, List<BlockPos> positions, HashSet<BlockPos> positionSet, int index, object bh)
        {
            if (index >= positions.Count)
            {
                return true; // execute the original method
            }

            BlockPos pos = positions[index];
            Block blockSafe4 = GetBlockSafe(world.BlockAccessor, pos);
            if (blockSafe4 is IAqueduct)
            {
                object stressCache = StressCache?.GetValue(null);
                object chiselAggregateCache = ChiselAggregateCache?.GetValue(null);

                if (stressCache == null || chiselAggregateCache == null)
                {
                    #if DEBUG
                    Console.WriteLine("CollapseStorySystem.StressCache or CollapseStorySystem.ChiselAggregateCache is null in PrefixCollapseLayer");
                    #endif
                    return true; // execute the original method
                }

                ItemStack val4 = blockSafe4.OnPickBlock(world, pos);
                CollapseInProgress.SetValue(null, true);
                world.BlockAccessor.SetBlock(0, pos);
                CollapseInProgress.SetValue(null, false);
                world.BlockAccessor.MarkBlockDirty(pos, (IPlayer)null);
                ChiselAggregateCache_Remove.Invoke(chiselAggregateCache, new object[] { pos });
                StressCache_Remove.Invoke(stressCache, new object[] { pos });
                if (val4 != null)
                {
                    world.SpawnItemEntity(val4, pos.ToVec3d().Add(0.5, 0.5, 0.5), (Vec3d)null);
                }
                world.Api.Event.RegisterCallback((Action<float>)delegate
                {
                    PrefixCollapseLayer(world, positions, positionSet, index + 1, bh);
                }, 150);
                return false; // skip the original method - callback will handle the next index
            }
            else
            {
                return true; // execute the original method
            }

        }

        internal static void SetupReflection(FieldInfo collapseInProgressField, FieldInfo chiselAggregateCacheField, FieldInfo stressCacheField, MethodInfo chiselAggregateCache_RemoveMethod, MethodInfo stressCache_RemoveMethod)
        {

            CollapseInProgress = collapseInProgressField;
            ChiselAggregateCache = chiselAggregateCacheField;
            StressCache = stressCacheField;
            ChiselAggregateCache_Remove = chiselAggregateCache_RemoveMethod;
            StressCache_Remove = stressCache_RemoveMethod;
        }

        private static Block GetBlockSafe(IBlockAccessor ba, BlockPos pos)
        {
            if (!ba.IsValidPos(pos))
            {
                return null;
            }
            return ba.GetBlock(pos, 1);
        }


    }
}
