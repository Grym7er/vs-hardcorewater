using System;
using System.Text;
using HardcoreWater.ModBlockEntity;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

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

		public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
		{
            ICoreClientAPI capi = this.api as ICoreClientAPI;
			if (capi != null && capi.Settings.Bool["extendedDebugInfo"])
			{
				BlockEntityAqueduct blockEntityAqueduct = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityAqueduct;
				if (blockEntityAqueduct == null)
				{
					return "";
				}

				StringBuilder dsc = new StringBuilder();
				dsc.AppendLine(Lang.Get("Water Level: {0}, Source Position: {1}", new object[]
					{
						blockEntityAqueduct.WaterLevel,
						blockEntityAqueduct.WaterSourcePos != null ? blockEntityAqueduct.WaterSourcePos.ToString() : "none"
					}));
				if (world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid) is Block block)
				{
					dsc.AppendLine(Lang.Get("Liquid: {0}", new object[]
					{
					   block.Code
					}));
				}
				if (this.GetLiquidBarrierHeightOnSide(BlockFacing.NORTH, pos) > 0f)
				{
					dsc.AppendLine("Barrier to liquid on side: North");
				}
				if (this.GetLiquidBarrierHeightOnSide(BlockFacing.EAST, pos) > 0f)
				{
					dsc.AppendLine("Barrier to liquid on side: East");
				}
				if (this.GetLiquidBarrierHeightOnSide(BlockFacing.SOUTH, pos) > 0f)
				{
					dsc.AppendLine("Barrier to liquid on side: South");
				}
				if (this.GetLiquidBarrierHeightOnSide(BlockFacing.WEST, pos) > 0f)
				{
					dsc.AppendLine("Barrier to liquid on side: West");
				}
				return dsc.ToString();
			}

            return "";
        }
	}
}
