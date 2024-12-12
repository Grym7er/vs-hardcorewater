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

        private string GetAqueductCodes(IWorldAccessor world, BlockPos pos, BlockFacing facing)
        {
            BlockPos blockPosOffset = pos.Copy().Offset(facing);
            if (world.BlockAccessor.GetBlock(blockPosOffset) is IAqueduct blockAqueduct)
            {
                if (BlockFacing.FromFirstLetter(blockAqueduct.Orientation[0]) == facing || BlockFacing.FromFirstLetter(blockAqueduct.Orientation[1]) == facing)
                return facing.Code[0].ToString() ?? "";
            }
            return "";
        }

        public string GetConnections(IWorldAccessor world, BlockPos pos, BlockFacing originalFacing)
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
            ICoreClientAPI capi = this.api as ICoreClientAPI;
			if (capi != null && capi.Settings.Bool["extendedDebugInfo"])
			{
				BlockEntityAqueduct blockEntityAqueduct = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityAqueduct;
				if (blockEntityAqueduct == null)
				{
					return "";
				}

				StringBuilder dsc = new StringBuilder();
				dsc.AppendLine(Lang.Get("Water Level: {0}, Source Position: {1}, Current Position: {2}", new object[]
					{
						blockEntityAqueduct.WaterLevel,
						blockEntityAqueduct.WaterSourcePos != null ? blockEntityAqueduct.WaterSourcePos.ToString() : "none",
                        pos.ToString()
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
