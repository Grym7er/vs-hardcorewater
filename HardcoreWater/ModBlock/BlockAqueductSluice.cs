using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using HardcoreWater.ModBlockEntity;
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
                    orientation = "sn";
                    break;
                case 1:
                    orientation = "we";
                    // Console.WriteLine("case: "+ horVer[0].Index + " orientation: " + orientation);
                    break;
                case 2:
                    orientation = "ns";
                    // Console.WriteLine("case: "+ horVer[0].Index + " orientation: " + orientation);
                    break;
                case 3:
                    orientation = "ew";
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
        return;
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
	}
}
