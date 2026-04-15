using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HardcoreWater.ModBlock
{
	public class BlockEnclosedAqueduct : Block, IAqueduct
	{
		public string Orientation
		{
			get
			{
				return this.Variant["orientation"];
			}
		}

        public bool IsEnclosed => true;

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            BlockFacing[] horVer = Block.SuggestedHVOrientation(byPlayer, blockSel);
            string orientation = "ns";
            if (horVer[0].Index == 1 || horVer[0].Index == 3)
            {
                orientation = "we";
            }
            Block block = world.BlockAccessor.GetBlock(base.CodeWithVariant("orientation", orientation));
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

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
		{
			base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);

			// Ensure adjacent aqueducts get an update
			world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
		}

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            AssetLocation newBlockCode = base.CodeWithVariant("orientation", "ns");
            return new ItemStack(world.BlockAccessor.GetBlock(newBlockCode), 1);
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
		{
            return AqueductDebugInfo.Build(this.api, this, world, pos);
        }
	}
}
