using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HardcoreWater.ModBlock
{
	public class BlockAqueduct : Block, IAqueduct
    {
        public string Orientation
        {
            get
            {
                return this.Variant["orientation"];
            }
        }

        public bool IsEnclosed => false;

        // public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
		// {
		// 	base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);

        //     // Let's set the liquid at the position of the block to air:
        //     // This helps with CollapseStory compat (no floating water in air)
        //     // Also helps with general cleanup, I think?

        //     world.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Fluid);

		// 	// Ensure adjacent aqueducts get an update
		// 	world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
		// }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            // Doing the SetBlock and TriggerNeighbourBlockUpdate here instead of in OnBlockBroken
            // because OnBlockRemoved should be called as aprt of OnBlockBroken, and the way that
            // CollapseStory handles the collapse is by setting the block to air, so this
            // OnBlockRemoved will trigger as a part of that, and thus prevent floating liquids.

            base.OnBlockRemoved(world, pos);

            world.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Fluid);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        }


        private string GetAqueductCodes(IWorldAccessor world, BlockPos pos, BlockFacing facing)
        {
            BlockPos blockPosOffset = pos.Copy().Offset(facing);
            if (world.BlockAccessor.GetBlock(blockPosOffset) is IAqueduct blockAqueduct)
            {
                if (!string.IsNullOrEmpty(blockAqueduct.Orientation) && blockAqueduct.Orientation.Length >= 2 &&
                    (BlockFacing.FromFirstLetter(blockAqueduct.Orientation[0]) == facing || BlockFacing.FromFirstLetter(blockAqueduct.Orientation[1]) == facing))
                return facing.Code[0].ToString() ?? "";
            }
            return "";
        }

        protected virtual string GetConnections(IWorldAccessor world, BlockPos pos, BlockFacing originalFacing)
        {
			string connections = "";
			if (originalFacing == BlockFacing.NORTH || originalFacing == BlockFacing.SOUTH)
			{
                connections = this.GetAqueductCodes(world, pos, BlockFacing.WEST) + this.GetAqueductCodes(world, pos, BlockFacing.EAST);
            } 
			else
			{
                connections = this.GetAqueductCodes(world, pos, BlockFacing.NORTH) + this.GetAqueductCodes(world, pos, BlockFacing.SOUTH);
            }
            
            if (connections.Length == 0)
            {
                connections = "none";
            }
            return connections;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            BlockFacing[] horVer = Block.SuggestedHVOrientation(byPlayer, blockSel);
            string orientation = "ns";
            if (horVer[0].Index == 1 || horVer[0].Index == 3)
            {
                orientation = "we";
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
            string orientation = this.Orientation;
            string connections = this.GetConnections(world, pos, BlockFacing.FromFirstLetter(orientation));
            AssetLocation newBlockCode = base.CodeWithVariants(new string[]
            {
                "connections",
                "orientation"
            }, new string[]
            {
                connections,
                orientation
            });
            if (this.Code.Equals(newBlockCode))
            {
                base.OnNeighbourBlockChange(world, pos, neibpos);
                return;
            }
            Block block = world.BlockAccessor.GetBlock(newBlockCode);
            if (block == null)
            {
                return;
            }

            world.BlockAccessor.ExchangeBlock(block.BlockId, pos); // ExchangeBlock so that block entity is preserved
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            AssetLocation newBlockCode = base.CodeWithVariants(new string[]
            {
                "connections",
                "orientation"
            }, new string[]
            {
                "none",
                "ns"
            });
            return new ItemStack(world.BlockAccessor.GetBlock(newBlockCode), 1);
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
		{
            return base.GetPlacedBlockInfo(world, pos, forPlayer) + AqueductDebugInfo.Build(this.api, this, world, pos);
        }
	}
}
