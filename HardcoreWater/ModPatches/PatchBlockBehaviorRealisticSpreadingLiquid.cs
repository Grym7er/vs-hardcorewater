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
    public class PatchBlockBehaviorRealisticSpreadingLiquid
    {
        // We patch realisticwater so that our aqueducts keep handling their own flow directions and updates.

        static Type PatchPosAndDist;
        static FieldInfo PatchPos;
        static FieldInfo PatchDist;

        internal static bool PrefixupdateOwnFlowDir(Block block, IWorldAccessor world, BlockPos pos)
        {
            Block solid = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.SolidBlocks);

            if (solid is IAqueduct)
            {
                // We don't want vanilla to handle flow dir stuff in aqueducts
                return false;
            }
            else{
                // execute the original method
                return true;
            }
        }

        // internal static bool PrefixSpreadAndUpdateLiquidLevels(IWorldAccessor world, BlockPos pos)
        // {
        //     Block solid = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.SolidBlocks);
        //     // Console.WriteLine("Triggered PrefixSpreadAndUpdateLiquidLevels for realistic water in aqueduct of type: " + solid.Code.ToShortString());

        //     // Console.WriteLine("Triggered updateOwnFlowDir for realistic water in aqueduct of type: " + solid.Code.ToShortString());
            
        //     if (solid is IAqueduct)
        //     {
        //         // We don't want vanilla to handle flow dir stuff in aqueducts
        //         return false;
        //     }
        //     else{
        //         // execute the original method
        //         return true;
        //     }
        // }

        internal static bool PrefixCanSpreadIntoBlock(Block ourblock, Block ourSolid, BlockPos pos, BlockPos npos, BlockFacing facing, IWorldAccessor world)
        {
            Block nposSolid = world.BlockAccessor.GetBlock(npos, BlockLayersAccess.Solid);
            if (nposSolid is IAqueduct)
            {
                // We don't want vanilla to handle flow dir stuff in aqueducts
                return false;
            }
            else{
                // execute the original method
                return true;
            }
        }

        static bool PrefixTryLoweringLiquidLevel(object __instance, ref bool __result, Block ourBlock, IWorldAccessor world, BlockPos pos)
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

        static void PostfixFindDownwardPaths(object __instance, object __result, IWorldAccessor world, BlockPos pos, Block ourBlock)
        {
            // If solid block of water is aqueduct, add aqueduct directions to valid downward paths
            Block posBlock = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);


            if (__result != null && posBlock is BlockAqueduct blockAqueduct) // posBlock is either aqueduct or sluice because both implement IAqueduct
            {
                if (string.IsNullOrEmpty(blockAqueduct.Orientation))
                {
                    return;
                }

                if (__result is not IList paths) return;


                if (BlockFacing.FromFirstLetter(blockAqueduct.Orientation) == BlockFacing.NORTH || BlockFacing.FromFirstLetter(blockAqueduct.Orientation) == BlockFacing.SOUTH)
                {
                    TryAddCandidatePath(paths, world, pos, pos.NorthCopy(), ourBlock);
                    TryAddCandidatePath(paths, world, pos, pos.SouthCopy(), ourBlock);
                    TryAddOpenOutletPath(paths, world, pos, pos.NorthCopy(), ourBlock);
                    TryAddOpenOutletPath(paths, world, pos, pos.SouthCopy(), ourBlock);
                }
                else
                {
                    TryAddCandidatePath(paths, world, pos, pos.WestCopy(), ourBlock);
                    TryAddCandidatePath(paths, world, pos, pos.EastCopy(), ourBlock);
                    TryAddOpenOutletPath(paths, world, pos, pos.WestCopy(), ourBlock);
                    TryAddOpenOutletPath(paths, world, pos, pos.EastCopy(), ourBlock);
                }

            }
        }

  

        internal static void TryAddCandidatePath(IList paths, IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
        {
        
            if (paths == null || PatchPos == null || PatchDist == null || PatchPosAndDist == null) return; // only do if patching was succesfull
            if (!PatchHelpers.IsValidAqueductPathCandidate(world, sourcePos, candidatePos, ourBlock)) return;

            #if DEBUG
            Block candidateSolid = world.BlockAccessor.GetBlock(candidatePos, BlockLayersAccess.Solid);
            Block sourceSolid = world.BlockAccessor.GetBlock(sourcePos, BlockLayersAccess.Solid);
            Console.WriteLine("Trying to add : " + candidateSolid.Code.ToShortString() +" as a candidate from source block: " + sourceSolid.Code.ToShortString());
            #endif

            if (ContainsCandidatePos(paths, candidatePos)) return;

            object pad = Activator.CreateInstance(PatchPosAndDist);

            PatchPos.SetValue(pad, candidatePos.Copy());
            PatchDist.SetValue(pad, 1);

            paths.Add(pad);
            #if DEBUG
            Console.WriteLine("Added successfully: " + candidateSolid.Code.ToShortString() +" as a candidate from source block: " + sourceSolid.Code.ToShortString());
            Console.WriteLine("");
            #endif
        }

        internal static void TryAddOpenOutletPath(IList paths, IWorldAccessor world, BlockPos sourcePos, BlockPos candidatePos, Block ourBlock)
        {
            
            if (paths == null || PatchPos == null || PatchDist == null || PatchPosAndDist == null) return; // only do if patching was succesfull
            if (!PatchHelpers.IsValidOpenOutletCandidate(world, sourcePos, candidatePos, ourBlock)) return;

            #if DEBUG
            Block candidateSolid = world.BlockAccessor.GetBlock(candidatePos, BlockLayersAccess.Solid);
            Block sourceSolid = world.BlockAccessor.GetBlock(sourcePos, BlockLayersAccess.Solid);
            Console.WriteLine("Trying to add : " + candidateSolid.Code.ToShortString() +" as an open outlet from source block: " + sourceSolid.Code.ToShortString());
            #endif

            bool containsCandidate = ContainsCandidatePos(paths, candidatePos);

            if (containsCandidate)
            {
                #if DEBUG
                Console.WriteLine("Candidate position: " + candidateSolid.Code.ToShortString() + " at pos: " + candidatePos.ToString() + " already exists in paths");
                #endif
                return;
            }

            object pad = Activator.CreateInstance(PatchPosAndDist);

            PatchPos.SetValue(pad, candidatePos.Copy());
            PatchDist.SetValue(pad, 1);

            paths.Add(pad);

            #if DEBUG
            Console.WriteLine("Added successfully: " + candidateSolid.Code.ToShortString() +" as an open outlet from source block: " + sourceSolid.Code.ToShortString());
            Console.WriteLine("");
            #endif
        }

        private static bool ContainsCandidatePos(IList paths, BlockPos candidatePos)
        {
            foreach (object item in paths)
            {
                if (item==null) continue;

                BlockPos existingPos = PatchPos.GetValue(item) as BlockPos;
                

                if (existingPos!=null && existingPos.Equals(candidatePos)) return true;
            }

            return false;
        }

        internal static void SetupPosAndDistReflection(Type posAndDistType, FieldInfo posField, FieldInfo distField)
        {
            PatchPosAndDist = posAndDistType;
            PatchPos = posField;
            PatchDist = distField;
        }

        
    }
}
