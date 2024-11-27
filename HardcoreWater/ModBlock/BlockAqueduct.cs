using System;
using System.Text;
using HardcoreWater.ModBlockEntity;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace HardcoreWater.ModBlock
{
	public class BlockAqueduct : Block
	{
		public string Orientation
		{
			get
			{
				return this.Variant["orientation"];
			}
		}

		public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
		{
			base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);

			if (world.BlockAccessor.GetBlockEntity(pos) != null)
            {
				world.BlockAccessor.RemoveBlockEntity(pos);
			}

			// Ensure adjacent aqueducts get an update
			world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
		}

		//public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
		//{
		//	BlockEntityAqueduct blockEntityAqueduct = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityAqueduct;
		//	if (blockEntityAqueduct == null)
		//	{
		//		return "";
		//	}
		//
		//	StringBuilder dsc = new StringBuilder();
		//	dsc.AppendLine(Lang.Get("Water Level: {0}, Source Position: {1}", new object[]
		//		{
		//			blockEntityAqueduct.WaterLevel,
		//			blockEntityAqueduct.WaterSourcePos != null ? blockEntityAqueduct.WaterSourcePos.ToString() : "none"
		//		}));
		//	return dsc.ToString();
		//}
	}
}
