using System.Collections.Generic;
using System.Linq;
using HardcoreWater.ModBlock;
using HardcoreWater.ModBlockEntity;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using System;

namespace AdditionalSpawnConstraints.ModPatches
{
	public class PatchBlockBehaviorFiniteSpreadingLiquid
	{
        private static bool IsRapidWaterBlockCode(Block block)
        {
            return block?.Code?.Path != null && block.Code.Path.StartsWith("rapidwater");
        }

        /// <summary>Finite fresh water blocks using the vanilla water-* code path (excludes rapidwater-*, saltwater, etc.).</summary>
        private static bool IsVanillaFiniteFreshWaterCode(Block block)
        {
            return block?.Code?.Path != null && block.Code.Path.StartsWith("water-");
        }

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
            if (neighborSolid is IAqueduct && IsRapidWaterBlockCode(neighborLiquid) && IsVanillaFiniteFreshWaterCode(ourblock))
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
            if (!HardcoreWater.HardcoreWaterConfig.Loaded.EnableAqueductRapids)
            {
                return true;
            }

            if (!IsRapidWaterBlockCode(block))
            {
                return true;
            }

            Block solid = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.SolidBlocks);
            if (solid is not IAqueduct)
            {
                return true;
            }

            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityAqueduct be || !be.CarriesRapids)
            {
                return true;
            }

            return false;
        }

        private static bool AcceptsFlowFromSideAqueduct(IAqueduct aqueduct, BlockFacing incomingSide)
        {
            if (string.IsNullOrEmpty(aqueduct.Orientation) || aqueduct.Orientation.Length < 2)
            {
                return false;
            }
            
            BlockFacing orientationA = BlockFacing.FromFirstLetter(aqueduct.Orientation[0]);
            BlockFacing orientationB = BlockFacing.FromFirstLetter(aqueduct.Orientation[1]);
            return orientationA == incomingSide || orientationB == incomingSide;
        }

        private static bool IsSluiceOpen(BlockPos candidatePos, IWorldAccessor world)
        {
            BlockEntityAqueductSluice be = world.BlockAccessor.GetBlockEntity(candidatePos) as BlockEntityAqueductSluice;
            return be.IsOpen;
        }

        private static bool AcceptsFlowFromSideSluice(IAqueduct aqueduct, BlockFacing incomingSide, bool IsSluiceOpen)
        {
            if (string.IsNullOrEmpty(aqueduct.Orientation) || aqueduct.Orientation.Length < 2)
            {
                return false;
            }

            BlockFacing openEnd = BlockFacing.FromFirstLetter(aqueduct.Orientation[1]); // idx 1 is the open end
            if (!IsSluiceOpen)
            {
                return openEnd == incomingSide;
            }else{
                BlockFacing closedEnd = BlockFacing.FromFirstLetter(aqueduct.Orientation[0]);
                return closedEnd == incomingSide || openEnd == incomingSide;
            }

        }

        private static bool IsValidAqueductPathCandidate(IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
        {
            BlockFacing facing = BlockFacing.HORIZONTALS.FirstOrDefault(f => sourcePos.AddCopy(f).Equals(candidatePos));
            if (facing == null) return false;

            Block sourceSolid = world.BlockAccessor.GetBlock(sourcePos, BlockLayersAccess.Solid);
            Block candidateSolid = world.BlockAccessor.GetBlock(candidatePos, BlockLayersAccess.Solid);
            Block candidateFluid = world.BlockAccessor.GetBlock(candidatePos, BlockLayersAccess.Fluid);

            if (sourceSolid == null || candidateSolid == null || candidateFluid == null) return false;
            if (!(candidateSolid is IAqueduct candidateAqueduct)) return false;

            BlockEntityAqueduct sourceAqueductBE = world.BlockAccessor.GetBlockEntity(sourcePos) as BlockEntityAqueduct;
            
            if (sourceAqueductBE != null && candidatePos == sourceAqueductBE.WaterSourcePos)
            {
                Console.WriteLine("Return false because I don't want to spread from " + sourceSolid.Code.ToShortString() + " to " + candidateSolid.Code.ToShortString());
                return false; // the logic is that I don't want to spread backwards
            }



            if ( candidateSolid is BlockAqueductSluice) 
            {
                if (!AcceptsFlowFromSideSluice(candidateAqueduct, facing.Opposite, IsSluiceOpen(candidatePos, world))) return false;
            }else{
                if (!AcceptsFlowFromSideAqueduct(candidateAqueduct, facing.Opposite)) return false;
            }
            

             
            // Flow enters candidate from the opposite side of source->candidate facing.


            // Respect standard spread constraints so appended paths don't bypass survival invariants.
            float sourceBarrier = sourceSolid.GetLiquidBarrierHeightOnSide(facing, sourcePos);
            float candidateBarrier = candidateSolid.GetLiquidBarrierHeightOnSide(facing.Opposite, candidatePos);
            
            bool barrierCheck = sourceBarrier >= ((float)ourBlock.LiquidLevel / 7f) || candidateBarrier >= ((float)ourBlock.LiquidLevel / 7f);
            Console.WriteLine("========================");
            Console.WriteLine("sourceBarrier: " + sourceBarrier);
            Console.WriteLine("candidateBarrier: " + candidateBarrier);
            Console.WriteLine("barrierCheck result: " + barrierCheck);
            Console.WriteLine("========================");
            
            if (barrierCheck) return true;

            if (candidateFluid.BlockId != 0 && candidateFluid.Replaceable < ourBlock.Replaceable) return false;

            return true;
        }

        private static void TryAddCandidatePath(List<PosAndDist> paths, IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
        {
            if (!IsValidAqueductPathCandidate(world, sourcePos, candidatePos, ourBlock)) return;
            if (paths.Exists(pad => pad.pos.Equals(candidatePos))) return;

            Block candidateSolid = world.BlockAccessor.GetBlock(candidatePos, BlockLayersAccess.Solid);
            Block sourceSolid = world.BlockAccessor.GetBlock(sourcePos, BlockLayersAccess.Solid);
            Console.WriteLine("========================");
            Console.WriteLine("Accepted candidate to spread into is: " + candidateSolid.Code.ToShortString() + " at position: " + candidatePos);
            Console.WriteLine("The source solid is: " + sourceSolid.Code.ToShortString() + " at position: " + sourcePos);
            Console.WriteLine("========================");

        

            
            paths.Add(new PosAndDist()
            {
                pos = candidatePos,
                dist = 1
            });
        }

        private static bool IsValidOpenOutletCandidate(IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
        {
            BlockFacing facing = BlockFacing.HORIZONTALS.FirstOrDefault(f => sourcePos.AddCopy(f).Equals(candidatePos));
            if (facing == null) return false;

            Block sourceSolid = world.BlockAccessor.GetBlock(sourcePos, BlockLayersAccess.Solid);
            Block candidateSolid = world.BlockAccessor.GetBlock(candidatePos, BlockLayersAccess.Solid);
            Block candidateFluid = world.BlockAccessor.GetBlock(candidatePos, BlockLayersAccess.Fluid);



            if (sourceSolid == null || candidateSolid == null || candidateFluid == null) return false;

            if (candidateSolid is IAqueduct) return false;

            // We don't want to spread from aqueduct to open air if the open air is also the aqueduct source
            // so basically check if candidatePos is also the sourceAqueudct's waterSourcePos

            BlockEntityAqueduct sourceAqueduct = world.BlockAccessor.GetBlockEntity(sourcePos) as BlockEntityAqueduct;
            if (sourceAqueduct != null && sourceAqueduct.WaterSourcePos == candidatePos )
            {
                Console.WriteLine("Return false because I don't want to spread from " + sourceSolid.Code.ToShortString() + " to " + candidateSolid.Code.ToShortString());
                return false;
            }


            // Keep same barrier invariants as normal spread.
            float sourceBarrier = sourceSolid.GetLiquidBarrierHeightOnSide(facing, sourcePos);
            float candidateBarrier = candidateSolid.GetLiquidBarrierHeightOnSide(facing.Opposite, candidatePos);
            if (sourceBarrier >= (float)ourBlock.LiquidLevel / 7f || candidateBarrier >= (float)ourBlock.LiquidLevel / 7f) return false;

            // Prefer empty cells; otherwise respect replaceable rules.
            bool emptyFluidCell = candidateFluid.BlockId == 0;
            bool fluidReplaceable = candidateFluid.Replaceable >= ourBlock.Replaceable;
            Console.WriteLine("-------------");
            Console.WriteLine("sourceSolid: " + sourceSolid.Code.ToShortString());
            Console.WriteLine("candidateSolid: " + candidateSolid.Code.ToShortString());
            Console.WriteLine("emptyFluidCell: " + emptyFluidCell);
            Console.WriteLine("fluidReplaceable: " + fluidReplaceable);
            Console.WriteLine("-------------");
            if (!emptyFluidCell && !fluidReplaceable) return false;

            return true;
        }

        private static void TryAddOpenOutletPath(List<PosAndDist> paths, IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
        {
            if (!IsValidOpenOutletCandidate(world, sourcePos, candidatePos, ourBlock)) return;
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
            Block posBlock = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);
            if (__result != null && posBlock is BlockAqueduct blockAqueduct) // posBlock is either aqueduct or sluice because both implement IAqueduct
            {
                if (string.IsNullOrEmpty(blockAqueduct.Orientation))
                {
                    return;
                }

                    if (BlockFacing.FromFirstLetter(blockAqueduct.Orientation) == BlockFacing.NORTH || BlockFacing.FromFirstLetter(blockAqueduct.Orientation) == BlockFacing.SOUTH)
                    {
                        TryAddCandidatePath(__result, world, pos, pos.NorthCopy(), ourBlock);
                        TryAddCandidatePath(__result, world, pos, pos.SouthCopy(), ourBlock);
                        TryAddOpenOutletPath(__result, world, pos, pos.NorthCopy(), ourBlock);
                        TryAddOpenOutletPath(__result, world, pos, pos.SouthCopy(), ourBlock);
                    }
                    else
                    {
                        TryAddCandidatePath(__result, world, pos, pos.WestCopy(), ourBlock);
                        TryAddCandidatePath(__result, world, pos, pos.EastCopy(), ourBlock);
                        TryAddOpenOutletPath(__result, world, pos, pos.WestCopy(), ourBlock);
                        TryAddOpenOutletPath(__result, world, pos, pos.EastCopy(), ourBlock);
                    }



            }
        }
    }
}
    