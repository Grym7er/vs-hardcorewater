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
    public class PatchHelpers
    {

        internal static bool IsRapidWaterBlockCode(Block block)
        {
            return block?.Code?.Path != null && block.Code.Path.StartsWith("rapidwater");
        }

        /// <summary>Finite fresh water blocks using the vanilla water-* code path (excludes rapidwater-*, saltwater, etc.).</summary>
        internal static bool IsVanillaFiniteFreshWaterCode(Block block)
        {
            return block?.Code?.Path != null && block.Code.Path.StartsWith("water-");
        }

        internal static bool AcceptsFlowFromSideAqueduct(IAqueduct aqueduct, BlockFacing incomingSide)
        {
            if (string.IsNullOrEmpty(aqueduct.Orientation) || aqueduct.Orientation.Length < 2)
            {
                return false;
            }

            BlockFacing orientationA = BlockFacing.FromFirstLetter(aqueduct.Orientation[0]);
            BlockFacing orientationB = BlockFacing.FromFirstLetter(aqueduct.Orientation[1]);
            return orientationA == incomingSide || orientationB == incomingSide;
        }

        internal static bool IsSluiceOpen(BlockPos candidatePos, IWorldAccessor world)
        {
            BlockEntityAqueductSluice be = world.BlockAccessor.GetBlockEntity(candidatePos) as BlockEntityAqueductSluice;
            return be.IsOpen;
        }

        internal static bool AcceptsFlowFromSideSluice(IAqueduct aqueduct, BlockFacing incomingSide, bool IsSluiceOpen)
        {
            if (string.IsNullOrEmpty(aqueduct.Orientation) || aqueduct.Orientation.Length < 2)
            {
                return false;
            }

            BlockFacing openEnd = BlockFacing.FromFirstLetter(aqueduct.Orientation[1]); // idx 1 is the open end
            if (!IsSluiceOpen)
            {
                return openEnd == incomingSide;
            }
            else
            {
                BlockFacing closedEnd = BlockFacing.FromFirstLetter(aqueduct.Orientation[0]);
                return closedEnd == incomingSide || openEnd == incomingSide;
            }

        }

        internal static bool IsValidAqueductPathCandidate(IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
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
                return false; // the logic is that I don't want to spread backwards
            }



            if (candidateSolid is BlockAqueductSluice)
            {
                if (!AcceptsFlowFromSideSluice(candidateAqueduct, facing.Opposite, IsSluiceOpen(candidatePos, world))) return false;
            }
            else
            {
                if (!AcceptsFlowFromSideAqueduct(candidateAqueduct, facing.Opposite)) return false;
            }



            // Flow enters candidate from the opposite side of source->candidate facing.


            // Respect standard spread constraints so appended paths don't bypass survival invariants.
            float sourceBarrier = sourceSolid.GetLiquidBarrierHeightOnSide(facing, sourcePos);
            float candidateBarrier = candidateSolid.GetLiquidBarrierHeightOnSide(facing.Opposite, candidatePos);

            bool barrierCheck = sourceBarrier >= ((float)ourBlock.LiquidLevel / 7f) || candidateBarrier >= ((float)ourBlock.LiquidLevel / 7f);


            if (barrierCheck) return false;

            if (candidateFluid.BlockId != 0 && candidateFluid.Replaceable < ourBlock.Replaceable) return false;

            return true;
        }

        internal static void TryAddCandidatePath(List<PosAndDist> paths, IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
        {
            if (!IsValidAqueductPathCandidate(world, sourcePos, candidatePos, ourBlock)) return;
            if (paths.Exists(pad => pad.pos.Equals(candidatePos))) return;
            

            paths.Add(new PosAndDist()
            {
                pos = candidatePos,
                dist = 1
            });
        }

        internal static bool IsValidOpenOutletCandidate(IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
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
            if (sourceAqueduct != null && sourceAqueduct.WaterSourcePos == candidatePos)
            {
                // Console.WriteLine("Return false because I don't want to spread from " + sourceSolid.Code.ToShortString() + " to " + candidateSolid.Code.ToShortString());
                return false;
            }


            // Keep same barrier invariants as normal spread.
            float sourceBarrier = sourceSolid.GetLiquidBarrierHeightOnSide(facing, sourcePos);
            float candidateBarrier = candidateSolid.GetLiquidBarrierHeightOnSide(facing.Opposite, candidatePos);
            if (sourceBarrier >= (float)ourBlock.LiquidLevel / 7f || candidateBarrier >= (float)ourBlock.LiquidLevel / 7f) return false;

            // Prefer empty cells; otherwise respect replaceable rules.
            bool emptyFluidCell = candidateFluid.BlockId == 0;
            bool fluidReplaceable = candidateFluid.Replaceable >= ourBlock.Replaceable;
            // Console.WriteLine("-------------");
            // Console.WriteLine("sourceSolid: " + sourceSolid.Code.ToShortString());
            // Console.WriteLine("candidateSolid: " + candidateSolid.Code.ToShortString());
            // Console.WriteLine("emptyFluidCell: " + emptyFluidCell);
            // Console.WriteLine("fluidReplaceable: " + fluidReplaceable);
            // Console.WriteLine("-------------");
            if (!emptyFluidCell && !fluidReplaceable) return false;

            return true;
        }

        internal static void TryAddOpenOutletPath(List<PosAndDist> paths, IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
        {
            if (!IsValidOpenOutletCandidate(world, sourcePos, candidatePos, ourBlock)) return;
            if (paths.Exists(pad => pad.pos.Equals(candidatePos))) return;

            paths.Add(new PosAndDist()
            {
                pos = candidatePos,
                dist = 1
            });
        }

    }
}