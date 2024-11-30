using HardcoreWater.ModBlock;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace HardcoreWater.ModBlockEntity
{
    public class BlockEntityAqueduct : BlockEntity
	{
		private bool IsValidWaterSource(BlockPos blockPos, int minLevel = 7)
        {
            if (this.Api.World.BlockAccessor.GetBlock(blockPos, BlockLayersAccess.Fluid) is BlockWaterflowing blockWaterflowing)
            {
                return (blockWaterflowing.LiquidLevel >= minLevel);
            }
            if (this.Api.World.BlockAccessor.GetBlock(blockPos, BlockLayersAccess.Fluid) is BlockWater blockWater)
			{
                return blockWater.LiquidLevel >= minLevel;
			} 

            return false;
		}

        private bool IsValidWaterFall(BlockPos blockPos)
        {
            if (this.Api.World.BlockAccessor.GetBlock(blockPos, BlockLayersAccess.Fluid) is BlockWaterfall blockWaterfall)
            {
                return (blockWaterfall.LiquidLevel == 6 && blockWaterfall.Variant["flow"] == "d");
            }

            return false;
        }

        private bool IsValidFilledAqueduct(BlockPos blockPos, int minLevel = 7)
        {
            if (this.Api.World.BlockAccessor.GetBlockEntity<BlockEntityAqueduct>(blockPos) is BlockEntityAqueduct adjacentAqueduct && this.Api.World.BlockAccessor.GetBlock(blockPos) is IAqueduct aqueduct)
            {
                // To be a valid source aqueduct for this one, the adjacent aqueduct must be oriented in the same direction OR not enclosed
                bool validSource = aqueduct.Orientation == this.blockAqueduct.Orientation || !aqueduct.IsEnclosed;
                return validSource && adjacentAqueduct.WaterSourcePos != null && adjacentAqueduct.WaterSourcePos != this.WaterSourcePos && adjacentAqueduct.WaterLevel >= minLevel;
            }

            return false;
		}

		private void onServerTick1s(float dt)
		{
			BlockPos[] blockPosFB = new BlockPos[2];

            if (this.blockAqueduct == null)
                return;

			// Scan blocks front and back of the aqueduct
			if (BlockFacing.FromFirstLetter(this.blockAqueduct.Orientation) == BlockFacing.NORTH)
            {
				blockPosFB[0] = this.Pos.NorthCopy();
				blockPosFB[1] = this.Pos.SouthCopy();
			}
            else
            {
				blockPosFB[0] = this.Pos.WestCopy();
				blockPosFB[1] = this.Pos.EastCopy();
			}

			// Check validity of previous source location, if present
			if (this.WaterSourcePos != null)
			{
				bool hasSource = false;
                if (IsValidWaterSource(this.WaterSourcePos) || this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.WaterSourcePos) == null)
                {
                    hasSource = true; // Connected to source block or source block is in unloaded chunk
                }
                else if (IsValidWaterFall(this.WaterSourcePos) || this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.WaterSourcePos) == null)
				{
                    hasSource = true; // Connected to waterfall or source block is in unloaded chunk
                } 
                else if (IsValidFilledAqueduct(this.WaterSourcePos, 6) || this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.WaterSourcePos) == null)
				{
                    hasSource = true; // Connected to aqueduct that isn't using this one as a source and has valid water source or source block is in unloaded chunk
                }

				if (!hasSource)
				{
                    this.WaterSourceReacquireTimeout = 4;
                    this.WaterSourcePos = null;
					this.MarkDirty(true);
				}
                else
                {
                    // Check that water block id didnt change; this workaround prevents waterfalls from making forever filled aqueduct channels
                    //int currentWaterSourceId = this.Api.World.BlockAccessor.GetBlock(this.WaterSourcePos, BlockLayersAccess.Fluid).Id;
                    //if (this.lastWaterSourceId != 0 && lastWaterSourceId != currentWaterSourceId)
                    //{
                    //    this.lastWaterSourceId = 0;
                    //    this.WaterSourcePos = null;
                    //    this.MarkDirty(true);
                    //}
                    //this.lastWaterSourceId = currentWaterSourceId;
                }
			}
			else
			{
                if (WaterSourceReacquireTimeout > 1)
                {
                    --WaterSourceReacquireTimeout;
                    this.WaterLevel = Math.Max(0, this.WaterLevel - 1);
                    this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos);
                    this.MarkDirty(true);
                    return;
                }

                bool hasSource = false;
                foreach (BlockPos endPos in blockPosFB)
                {
                    if (IsValidWaterSource(endPos))
                    {
                        this.WaterSourcePos = endPos;
                        this.WaterLevel = 6;
                        hasSource = true;
                        break; // Connected to source block
                    }
                    else if (IsValidWaterSource(this.Pos.UpCopy()))
                    {
                        this.WaterSourcePos = this.Pos.UpCopy();
                        this.WaterLevel = 6;
                        hasSource = true;
                        break; // Connected to source block above
                    }
                    else if (IsValidWaterFall(this.Pos.UpCopy()))
                    {
                        this.WaterSourcePos = this.Pos.UpCopy();
                        this.WaterLevel = 6;
                        hasSource = true;
                        break; // Connected to waterfall above
                    }
                    else if (IsValidFilledAqueduct(this.Pos.UpCopy(), 6))
                    {
                        this.WaterSourcePos = this.Pos.UpCopy();
                        this.WaterLevel = 6;
                        hasSource = true;
                        break; // Connected to aqueduct above
                    }
                    else if (IsValidFilledAqueduct(endPos, 6))
                    {
                        this.WaterSourcePos = endPos;
                        this.WaterLevel = 6;
                        hasSource = true;
                        break; // Connected to aqueduct that isn't using this one as a source and has valid water source
                    }
                }

				if (hasSource)
				{
                    Block ourBlockFluid = this.Api.World.BlockAccessor.GetBlock(this.Pos, BlockLayersAccess.Fluid);
                    Block waterSourceBlock = this.Api.World.GetBlock(new AssetLocation("game:water-still-7"));
                    Block waterBlock = this.Api.World.GetBlock(new AssetLocation("game:water-still-" + Math.Min(7, this.WaterLevel)));
                    bool notIced = !ourBlockFluid.Code.Path.Contains("ice");
                    if (notIced && ourBlockFluid != waterSourceBlock)
                    {
                        this.Api.World.BlockAccessor.SetBlock(waterBlock.BlockId, this.Pos, BlockLayersAccess.Fluid);
                        this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos);
                        this.MarkDirty(true);
                    }
                }
				else
				{
                    this.WaterLevel = Math.Max(0, this.WaterLevel - 1);
                    this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos);
                    this.MarkDirty(true);
                }
            }
		}

		public override void Initialize(ICoreAPI api)
		{
			base.Initialize(api);
			this.blockAqueduct = (base.Block as IAqueduct);
			this.RegisterGameTickListener(new Action<float>(this.onServerTick1s), (int) Math.Round(HardcoreWaterConfig.Loaded.AqueductUpdateFrequencySeconds * 1000), 0);
		}

		public override void ToTreeAttributes(ITreeAttribute tree)
		{
			base.ToTreeAttributes(tree);
			tree.SetInt("WaterLevel", this.WaterLevel);
            tree.SetInt("WaterSourceReacquireTimeout", this.WaterSourceReacquireTimeout);
			if (this.WaterSourcePos != null)
				tree.SetBlockPos("WaterSourcePos", this.WaterSourcePos);
		}

		public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
		{
			base.FromTreeAttributes(tree, worldAccessForResolve);
			this.WaterLevel = tree.GetInt("WaterLevel");
            this.WaterSourceReacquireTimeout = tree.GetInt("WaterSourceReacquireTimeout", 0);
            this.WaterSourcePos = tree.GetBlockPos("WaterSourcePos", null);
        }

		private IAqueduct blockAqueduct;

		public int WaterLevel { get; set; } = 0;

		public BlockPos WaterSourcePos { get; set; } = null;

        private int WaterSourceReacquireTimeout = 0;
	}
}
