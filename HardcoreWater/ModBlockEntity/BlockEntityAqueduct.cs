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
            Block block = this.Api.World.BlockAccessor.GetBlock(blockPos, BlockLayersAccess.Fluid);
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
                return (blockWaterfall.LiquidLevel >= 6 && blockWaterfall.Variant["flow"] == "d");
            }

            return false;
        }

        private bool IsValidFilledAqueduct(BlockPos blockPos, int minLevel = 7)
        {
            
            if (this.Api.World.BlockAccessor.GetBlock(blockPos) is IAqueduct aqueduct)
            {
                if (this.Api.World.BlockAccessor.GetBlockEntity<BlockEntityAqueduct>(blockPos) is BlockEntityAqueduct adjacentAqueduct)
                {
                    // To be a valid source aqueduct for this one, the adjacent aqueduct must be oriented in the same direction OR not enclosed
                    bool correctOrientation = aqueduct.Orientation == this.blockAqueduct.Orientation || !aqueduct.IsEnclosed;
                    bool notSourcingThis = adjacentAqueduct.WaterSourcePos != this.WaterSourcePos;
                    //bool hasMinWater = adjacentAqueduct.WaterLevel >= minLevel || adjacentAqueduct.Block.LiquidLevel >= minLevel;
                    bool hasMinWater = adjacentAqueduct.HasWaterSource;
                    bool isValid = correctOrientation && notSourcingThis && hasMinWater;
                    return isValid;
                }
                else
                {
                    // Sometimes block entity will return null while block is aqueduct; assume valid source
                    return true;
                }
            }
            
            return false;
		}

        private bool HasInvalidSourceDependency(BlockPos posA, BlockPos posB)
        {
            // Check for case that aqueduct is valid source for two adjacent aqueducts, invalidate if so
            if (this.Api.World.BlockAccessor.GetBlockEntity(posA) is BlockEntityAqueduct entityAqueductA && this.Api.World.BlockAccessor.GetBlockEntity(posB) is BlockEntityAqueduct entityAqueductB)
            {
                bool sourcesBothAdjacent = (entityAqueductA.WaterSourcePos == this.Pos && entityAqueductB.WaterSourcePos == this.Pos);
                bool sourcedFromEitherAdjacent = (this.WaterSourcePos == entityAqueductA.Pos || this.WaterSourcePos == entityAqueductB.Pos);
                if (sourcesBothAdjacent && sourcedFromEitherAdjacent)
                {
                    return true;
                }
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
			//if (this.WaterSourcePos != null)
            if (this.HasWaterSource)
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

				if (!hasSource || HasInvalidSourceDependency(blockPosFB[0], blockPosFB[1]))
				{
                    this.WaterSourceReacquireTimeout = 4;
                    this.HasWaterSource = false;
                    this.WaterSourcePos = null;
                    this.MarkDirty(true);
				}
			}
			else
			{
                if (WaterSourceReacquireTimeout > 0)
                {
                    --WaterSourceReacquireTimeout;
                    this.WaterLevel = Math.Max(0, this.WaterLevel - 1);
                    this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos);
                    this.MarkDirty(true);
                    return;
                }

                bool hasSource = false;
                BlockPos upwardPos = this.Pos.UpCopy();
                if (IsValidWaterSource(upwardPos))
                {
                    this.WaterSourcePos = upwardPos;
                    this.WaterLevel = 6;
                    hasSource = true;
                    this.HasWaterSource = true;
                    // Connected to source block above
                }
                else if (IsValidWaterFall(upwardPos))
                {
                    this.WaterSourcePos = upwardPos;
                    this.WaterLevel = 6;
                    hasSource = true;
                    this.HasWaterSource = true;
                    // Connected to waterfall above
                }
                else if (IsValidFilledAqueduct(upwardPos, 6))
                {
                    this.WaterSourcePos = upwardPos;
                    this.WaterLevel = 6;
                    hasSource = true;
                    this.HasWaterSource = true;
                    // Connected to aqueduct above
                }

                if (!hasSource) // Check ends if no source above
                {
                    foreach (BlockPos endPos in blockPosFB)
                    {
                        if (IsValidWaterSource(endPos))
                        {
                            this.WaterSourcePos = endPos;
                            this.WaterLevel = 6;
                            hasSource = true;
                            this.HasWaterSource = true;
                            break; // Connected to source block
                        }
                        else if (IsValidFilledAqueduct(endPos, 6))
                        {
                            this.WaterSourcePos = endPos;
                            this.WaterLevel = 6;
                            hasSource = true;
                            this.HasWaterSource = true;
                            break; // Connected to aqueduct that isn't using this one as a source and has valid water source
                        }
                    }
                }
                

				if (hasSource)
				{
                    // Handle fresh, salt, and boiling water separately, lest we desalinate or something else weird
                    Block ourBlockFluid = this.Api.World.BlockAccessor.GetBlock(this.Pos, BlockLayersAccess.Fluid);
                    Block liquidSourceBlock;
                    Block liquidBlockToSet;
                    if (ourBlockFluid != null && ourBlockFluid.Code.BeginsWith("game", "salt"))
                    {
                        liquidSourceBlock = this.Api.World.GetBlock(new AssetLocation("game:saltwater-still-7"));
                        liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:saltwater-still-" + Math.Min(7, this.WaterLevel)));
                    }
                    else if (ourBlockFluid != null && ourBlockFluid.Code.BeginsWith("game", "boiling"))
                    {
                        liquidSourceBlock = this.Api.World.GetBlock(new AssetLocation("game:boilingwater-still-7"));
                        liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:boilingwater-still-" + Math.Min(7, this.WaterLevel)));
                    }
                    else
                    {
                        liquidSourceBlock = this.Api.World.GetBlock(new AssetLocation("game:water-still-7"));
                        liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:water-still-" + Math.Min(7, this.WaterLevel)));
                    }

                    bool notIced = !ourBlockFluid.Code.Path.Contains("ice");
                    if (notIced && ourBlockFluid.LiquidLevel < this.WaterLevel && !HasInvalidSourceDependency(blockPosFB[0], blockPosFB[1]))
                    {
                        this.Api.World.BlockAccessor.SetBlock(liquidBlockToSet.BlockId, this.Pos, BlockLayersAccess.Fluid);
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
            tree.SetBool("HasWaterSource", this.HasWaterSource);
			if (this.HasWaterSource)
				tree.SetBlockPos("WaterSourcePos", this.WaterSourcePos);
		}

		public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
		{
			base.FromTreeAttributes(tree, worldAccessForResolve);
			this.WaterLevel = tree.GetInt("WaterLevel");
            this.WaterSourceReacquireTimeout = tree.GetInt("WaterSourceReacquireTimeout", 0);
            this.HasWaterSource = tree.GetBool("HasWaterSource", true);
            this.WaterSourcePos = tree.GetBlockPos("WaterSourcePos", null);
        }

		private IAqueduct blockAqueduct;

		public int WaterLevel { get; set; } = 0;

		public BlockPos WaterSourcePos { get; set; } = null;

        public bool HasWaterSource { get; set; } = false;

        private int WaterSourceReacquireTimeout = 0;
	}
}
