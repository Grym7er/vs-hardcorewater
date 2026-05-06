using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using HardcoreWater.ModBlockEntity;
using System.Linq;
namespace HardcoreWater.ModBlock
{
	public class BlockAqueductSluice : BlockAqueduct
    {
        protected override string GetConnections(IWorldAccessor world, BlockPos pos, BlockFacing originalFacing)
        {
            return "none";
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            BlockFacing[] horVer = Block.SuggestedHVOrientation(byPlayer, blockSel);
            string orientation = "";
            switch (horVer[0].Index)
            {
                case 0:
                    orientation = "ns";
                    break;
                case 1:
                    orientation = "ew";
                    // Console.WriteLine("case: "+ horVer[0].Index + " orientation: " + orientation);
                    break;
                case 2:
                    orientation = "sn";
                    // Console.WriteLine("case: "+ horVer[0].Index + " orientation: " + orientation);
                    break;
                case 3:
                    orientation = "we";
                    // Console.WriteLine("case: "+ horVer[0].Index + " orientation: " + orientation);
                    break;
            }

            string connections = this.GetConnections(world, blockSel.Position, BlockFacing.FromFirstLetter(orientation));
            Block block = world.BlockAccessor.GetBlock(base.CodeWithVariants(new string[]
            {
                "connections",
                "orientation"
            }, new string[]
            {
                connections,
                orientation
            }));
            if (block == null)
            {
                block = this;
            }
            if (block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            {
                world.BlockAccessor.SetBlock(block.BlockId, blockSel.Position);
                return true;
            }
            return false;
        }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        var be = GetBlockEntity<BlockEntityAqueductSluice>(blockSel);
        if (be != null && be.OnInteract(blockSel, byPlayer))
        {
            return true;
        }
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    private BlockFacing[] GetSolidFacesDefault(string orientation)
    {
        if (orientation == "ns" || orientation == "sn")
        {
            return [BlockFacing.WEST, BlockFacing.EAST, BlockFacing.DOWN];
        }
        else if (orientation == "we" || orientation == "ew")
        {
            return [BlockFacing.NORTH, BlockFacing.SOUTH, BlockFacing.DOWN];
        }
        return BlockFacing.ALLFACES;
    }

    private BlockFacing[] GetSluiceFace(string orientation)
    {
        if (orientation == "ns") return [BlockFacing.NORTH];
        else if (orientation == "sn") return [BlockFacing.SOUTH];
        else if (orientation == "we") return [BlockFacing.WEST];
        else if (orientation == "ew") return [BlockFacing.EAST];
        return BlockFacing.ALLFACES;
    }

    public override float GetLiquidBarrierHeightOnSide(BlockFacing face, BlockPos pos)
    {


        string myOrientation = this.Orientation;
        BlockEntityAqueductSluice be = GetBlockEntity<BlockEntityAqueductSluice>(pos);
        if (be != null)
        {
            if (be.IsOpen)
            {
                // IsOpen, so only side faces are solid
                BlockFacing[] solidFaces = GetSolidFacesDefault(myOrientation);
                foreach (BlockFacing solidFace in solidFaces)
                {
                    if (solidFace == face)
                    {
                        return 1f;
                    }
                }
            }
            else
            {
                BlockFacing[] solidFaces = GetSolidFacesDefault(myOrientation);
                BlockFacing[] blockedFaces = GetSluiceFace(myOrientation);
                // Console.WriteLine("===========");
                // Console.WriteLine("For queried face: " + face +" at pos: " + pos);
                // Console.WriteLine("solidFaces: " + string.Join(", ", solidFaces.Select(f => f.ToString())));
                // Console.WriteLine("blockedFaces: " + string.Join(", ", blockedFaces.Select(f => f.ToString())));
                // Console.WriteLine("===========");
                foreach (BlockFacing solidFace in solidFaces)
                {
                    if (solidFace == face)
                    {
                        return 1f;
                    }
                }
                foreach (BlockFacing blockedFace in blockedFaces)
                {
                    if (blockedFace == face)
                    {
                        return 1f;
                    }
                }
            }
            return 0f;
        }
        else{
            return 1f;
        }
         
    }
	}
}
