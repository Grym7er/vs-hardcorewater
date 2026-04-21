using HardcoreWater.ModBlock;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace HardcoreWater.ModBlockEntity
{
    public class BlockEntityAqueduct : BlockEntity
	{
        private const float ReacquireTimeoutSeconds = 3f;

        private int GetReacquireTimeoutTicks()
        {
            return Math.Max(1, (int)Math.Ceiling(ReacquireTimeoutSeconds / this.tickIntervalSeconds));
        }

        private bool IsValidWaterSourceOrWaterFall(BlockPos blockPos, int minLevel = 7)
        {
            bool isValid = IsValidWaterSource(blockPos, minLevel) || IsValidWaterFall(blockPos);
            return isValid;
        }

		private bool IsValidWaterSource(BlockPos blockPos, int minLevel = 7)
        {
            Block block = this.Api.World.BlockAccessor.GetBlock(blockPos, BlockLayersAccess.Fluid);
            if (block is BlockWaterflowing blockWaterflowing)
            {
                return (blockWaterflowing.LiquidLevel >= minLevel);
            }
            if (block is BlockWater blockWater)
			{
                return blockWater.LiquidLevel >= minLevel;
			}

            if (HardcoreWaterModSystem.ArchimedesCompat != null &&
                HardcoreWaterModSystem.ArchimedesCompat.IsManagedSourceBlock(block))
            {
                return block.LiquidLevel >= minLevel;
            }

            return false;
		}

        private bool IsValidWaterFall(BlockPos blockPos)
        {
            Block block = this.Api.World.BlockAccessor.GetBlock(blockPos, BlockLayersAccess.Fluid);
            if (block is BlockWaterfall blockWaterfall)
            {
                return (blockWaterfall.LiquidLevel >= 6 && blockWaterfall.Variant["flow"] == "d");
            }

            return false;
        }

        private bool IsValidFilledAqueduct(BlockPos blockPos)
        {
            
            if (this.Api.World.BlockAccessor.GetBlock(blockPos) is IAqueduct aqueduct)
            {
                if (this.Api.World.BlockAccessor.GetBlockEntity<BlockEntityAqueduct>(blockPos) is BlockEntityAqueduct adjacentAqueduct)
                {
                    // To be a valid source aqueduct for this one, the adjacent aqueduct must be oriented in the same direction OR not enclosed
                    bool correctOrientation = aqueduct.Orientation == this.blockAqueduct.Orientation || !aqueduct.IsEnclosed;
                    bool notSourcingThis = adjacentAqueduct.WaterSourcePos != this.WaterSourcePos;
                    bool notSourcingEachOther = (this.WaterSourcePos == null || adjacentAqueduct.WaterSourcePos == null) || !(adjacentAqueduct.WaterSourcePos == this.Pos && this.WaterSourcePos == adjacentAqueduct.Pos);
                    //bool hasMinWater = adjacentAqueduct.WaterLevel >= minLevel || adjacentAqueduct.Block.LiquidLevel >= minLevel;
                    bool hasMinWater = adjacentAqueduct.HasWaterSource;
                    bool isValid = correctOrientation && notSourcingThis && notSourcingEachOther && hasMinWater;
                    return isValid;
                }
                else
                {
                    // A missing BE is usually a chunk/load boundary race. Treat as invalid for this tick.
                    return false;
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

        private bool DoesBlockBelowPosHaveUpSolidFaceOrAqueduct(BlockPos blockPos)
        {
            Block mostSolidBlock = this.Api.World.BlockAccessor.GetMostSolidBlock(blockPos.DownCopy());

            if (mostSolidBlock is IAqueduct) return true;

            bool isSolidTop = (double)mostSolidBlock.GetLiquidBarrierHeightOnSide(BlockFacing.UP, blockPos.DownCopy()) >= 1.0;

            return isSolidTop;
        }

        private bool HasOpenOutletToAir(BlockPos[] blockPosFB)
        {
            foreach (BlockPos endPos in blockPosFB)
            {
                Block endSolid = this.Api.World.BlockAccessor.GetBlock(endPos, BlockLayersAccess.Solid);
                if (endSolid is IAqueduct)
                {
                    continue;
                }

                Block endFluid = this.Api.World.BlockAccessor.GetBlock(endPos, BlockLayersAccess.Fluid);
                bool emptyFluidCell = endFluid == null || endFluid.BlockId == 0;
                bool notBlockedBySolid = endSolid == null || endSolid.BlockId == 0 || endSolid.Replaceable >= 6000;
                if (emptyFluidCell && notBlockedBySolid)
                {
                    return true;
                }
            }

            return false;
        }

		private void onServerTick1s(float dt)
		{
			BlockPos[] blockPosFB = new BlockPos[2];
            bool shouldMarkDirty = false;
            bool shouldTriggerNeighborUpdate = false;
            bool oldHasWaterSource = this.HasWaterSource;
            BlockPos oldWaterSourcePos = this.WaterSourcePos;
            int oldWaterLevelState = this.WaterLevel;

            if (this.blockAqueduct == null)
                return;
            if (string.IsNullOrEmpty(this.blockAqueduct.Orientation))
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
                if (this.WaterSourcePos == null)
                {
                    // Persisted state invariant guard: a source flag without source position is invalid.
                    bool hadSource = this.HasWaterSource;
                    this.HasWaterSource = false;
                    this.WaterSourceReacquireTimeout = GetReacquireTimeoutTicks();
                    shouldMarkDirty = hadSource;
                    if (shouldMarkDirty)
                    {
                        this.MarkDirty(true);
                    }
                    return;
                }

				bool hasSource = false;
                bool unloadedWaterSource = this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.WaterSourcePos) == null;
                if (IsValidWaterSource(this.Pos, 7) || unloadedWaterSource)
                {
                    hasSource = true; // Contains source block or source block is in unloaded chunk
                }
                else if (IsValidWaterSource(this.WaterSourcePos) || unloadedWaterSource)
                {
                    hasSource = true; // Connected to source block or source block is in unloaded chunk
                }
                else if ((IsValidWaterFall(this.WaterSourcePos) && DoesBlockBelowPosHaveUpSolidFaceOrAqueduct(this.WaterSourcePos)) || unloadedWaterSource)
				{
                    hasSource = true; // Connected to waterfall or source block is in unloaded chunk
                }
                else if ((IsValidWaterSource(this.WaterSourcePos, 5) && IsValidWaterSourceOrWaterFall(this.WaterSourcePos.UpCopy(), 5) && DoesBlockBelowPosHaveUpSolidFaceOrAqueduct(this.WaterSourcePos)) || unloadedWaterSource)
                {
                    hasSource = true; // Connected to waterfall adjacent with above flowing water or source block is in unloaded chunk
                }
                else if ((IsValidWaterSource(this.Pos, 6) && IsValidWaterSourceOrWaterFall(this.WaterSourcePos, 6) && (this.Pos.Y == this.WaterSourcePos.Y-1)) || unloadedWaterSource)
                {
                    hasSource = true; // Connected to waterfall above or source block is in unloaded chunk
                }
                else if (IsValidFilledAqueduct(this.WaterSourcePos) || unloadedWaterSource)
				{
                    hasSource = true; // Connected to aqueduct that isn't using this one as a source and has valid water source or source block is in unloaded chunk
                }

                if (!hasSource || HasInvalidSourceDependency(blockPosFB[0], blockPosFB[1]))
				{
                    bool stateChanged = this.HasWaterSource || this.WaterSourcePos != null || this.WaterSourceReacquireTimeout != GetReacquireTimeoutTicks();
                    this.WaterSourceReacquireTimeout = GetReacquireTimeoutTicks();
                    this.HasWaterSource = false;
                    this.WaterSourcePos = null;
                    shouldMarkDirty = shouldMarkDirty || stateChanged;
				}
			}
			else
			{
                if (WaterSourceReacquireTimeout > 0)
                {
                    int oldTimeout = this.WaterSourceReacquireTimeout;
                    int oldLevel = this.WaterLevel;
                    --WaterSourceReacquireTimeout;
                    this.WaterLevel = Math.Max(0, this.WaterLevel - 1);
                    bool stateChanged = oldLevel != this.WaterLevel || oldTimeout != this.WaterSourceReacquireTimeout;
                    shouldMarkDirty = shouldMarkDirty || stateChanged;
                    shouldTriggerNeighborUpdate = shouldTriggerNeighborUpdate || oldLevel != this.WaterLevel;
                    if (shouldTriggerNeighborUpdate)
                    {
                        this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos);
                    }
                    if (shouldMarkDirty)
                    {
                        this.MarkDirty(true);
                    }
                    return;
                }

                bool hasSource = false;
                BlockPos upwardPos = this.Pos.UpCopy();
                if (IsValidWaterSource(this.Pos, 7))
                {
                    this.WaterSourcePos = this.Pos;
                    this.WaterLevel = 7;
                    hasSource = true;
                    this.HasWaterSource = true;
                    // Connected to source block in aqueduct
                }
                else if (IsValidWaterSource(upwardPos))
                {
                    this.WaterSourcePos = upwardPos;
                    this.WaterLevel = 6;
                    hasSource = true;
                    this.HasWaterSource = true;
                    // Connected to source block above
                }
                else if (IsValidWaterSourceOrWaterFall(upwardPos, 6))
                {
                    this.WaterSourcePos = upwardPos;
                    this.WaterLevel = 6;
                    hasSource = true;
                    this.HasWaterSource = true;
                    // Connected to waterfall or water above
                }
                else if (IsValidFilledAqueduct(upwardPos))
                {
                    this.WaterSourcePos = upwardPos;
                    this.WaterLevel = 6;
                    hasSource = true;
                    this.HasWaterSource = true;
                    // Connected to aqueduct above
                }

                if (!hasSource) // Check ends if source above
                {
                    foreach (BlockPos endPos in blockPosFB)
                    {
                        if (IsValidWaterSource(endPos))
                        {
                            this.WaterSourcePos = endPos;
                            this.WaterLevel = 6;
                            hasSource = true;
                            this.HasWaterSource = true;
                            break; // Connected to source block adjacent
                        }
                        else if (IsValidWaterFall(endPos) && DoesBlockBelowPosHaveUpSolidFaceOrAqueduct(endPos))
                        {
                            this.WaterSourcePos = endPos;
                            this.WaterLevel = 6;
                            hasSource = true;
                            this.HasWaterSource = true;
                            break; // Connected to waterfall adjacent
                        }
                        else if (IsValidWaterSource(endPos, 5) && IsValidWaterSourceOrWaterFall(endPos.UpCopy(), 5) && DoesBlockBelowPosHaveUpSolidFaceOrAqueduct(endPos))
                        {
                            this.WaterSourcePos = endPos;
                            this.WaterLevel = 6;
                            hasSource = true;
                            this.HasWaterSource = true;
                            break; // Connected to waterfall adjacent with above flowing water
                        }
                        else if (IsValidFilledAqueduct(endPos))
                        {
                            this.WaterSourcePos = endPos;
                            this.WaterLevel = 6;
                            hasSource = true;
                            this.HasWaterSource = true;
                            break; // Connected to aqueduct that isn't using this one as a source and has valid water source adjacent
                        }
                    }
                }
                

				if (hasSource)
				{
                    // Handle fresh, salt, and boiling water separately, lest we desalinate or something else weird
                    Block ourBlockFluid = this.Api.World.BlockAccessor.GetBlock(this.Pos, BlockLayersAccess.Fluid);
                    Block liquidBlockToSet;
                    string ownerControllerId = null;
                    string managedFamilyId = null;
                    Block compatBlock = null;
                    bool compatResolved = false;
                    if (HardcoreWaterModSystem.ArchimedesCompat != null)
                    {
                        compatResolved = HardcoreWaterModSystem.ArchimedesCompat.TryResolveAqueductFill(
                            this,
                            ourBlockFluid,
                            Math.Min(7, this.WaterLevel),
                            out compatBlock,
                            out ownerControllerId,
                            out managedFamilyId);
                    }
                    if (compatResolved && compatBlock != null)
                    {
                        liquidBlockToSet = compatBlock;
                    }
                    else if (ourBlockFluid != null && ourBlockFluid.Code != null && ourBlockFluid.Code.BeginsWith("game", "salt"))
                    {
                        liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:saltwater-still-" + Math.Min(7, this.WaterLevel)));
                    }
                    else if (ourBlockFluid != null && ourBlockFluid.Code != null && ourBlockFluid.Code.BeginsWith("game", "boiling"))
                    {
                        liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:boilingwater-still-" + Math.Min(7, this.WaterLevel)));
                    }
                    else
                    {
                        liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:water-still-" + Math.Min(7, this.WaterLevel)));
                    }

                    bool hasFluidCodePath = ourBlockFluid != null && ourBlockFluid.Code != null && ourBlockFluid.Code.Path != null;
                    bool notIced = !hasFluidCodePath || !ourBlockFluid.Code.Path.Contains("ice");
                    if (ourBlockFluid != null && liquidBlockToSet != null && notIced && ourBlockFluid.LiquidLevel < this.WaterLevel && !HasInvalidSourceDependency(blockPosFB[0], blockPosFB[1]))
                    {
                        this.Api.World.BlockAccessor.SetBlock(liquidBlockToSet.BlockId, this.Pos, BlockLayersAccess.Fluid);
                        if (!string.IsNullOrEmpty(ownerControllerId) &&
                            !string.IsNullOrEmpty(managedFamilyId) &&
                            HardcoreWaterModSystem.ArchimedesCompat != null)
                        {
                            // Strict-ownership path: this segment and source-height outflow cells retain controller ownership.
                            HardcoreWaterModSystem.ArchimedesCompat.TryAssignOutletOwnership(
                                this,
                                new[] { this.Pos, blockPosFB[0], blockPosFB[1] },
                                ownerControllerId,
                                managedFamilyId
                            );
                        }

                        shouldTriggerNeighborUpdate = true;
                        shouldMarkDirty = true;
                    }
                }
				else
				{
                    int oldLevel = this.WaterLevel;
                    this.WaterLevel = Math.Max(0, this.WaterLevel - 1);
                    if (oldLevel != this.WaterLevel)
                    {
                        shouldTriggerNeighborUpdate = true;
                        shouldMarkDirty = true;
                    }
                }
            }

            if (shouldTriggerNeighborUpdate)
            {
                this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos);
            }
            bool sourcePosChanged = (oldWaterSourcePos == null) != (this.WaterSourcePos == null)
                || (oldWaterSourcePos != null && !oldWaterSourcePos.Equals(this.WaterSourcePos));
            if (oldHasWaterSource != this.HasWaterSource || oldWaterLevelState != this.WaterLevel || sourcePosChanged)
            {
                shouldMarkDirty = true;
            }
            else if (this.HasWaterSource && this.WaterLevel > 0 && !shouldTriggerNeighborUpdate)
            {
                if (HasOpenOutletToAir(blockPosFB))
                {
                    this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos);
                }
            }
            if (shouldMarkDirty)
            {
                this.MarkDirty(true);
            }
		}

		public override void Initialize(ICoreAPI api)
		{
			base.Initialize(api);
			this.blockAqueduct = (base.Block as IAqueduct);
            this.tickIntervalSeconds = GameMath.Clamp(HardcoreWaterConfig.Loaded.AqueductUpdateFrequencySeconds, 0.1f, 10f);
			this.RegisterGameTickListener(new Action<float>(this.onServerTick1s), (int)Math.Round(this.tickIntervalSeconds * 1000), 0);
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
            this.HasWaterSource = tree.GetBool("HasWaterSource", false);
            this.WaterSourcePos = tree.GetBlockPos("WaterSourcePos", null);

            // Normalize deserialized invariants for older saves.
            if (this.HasWaterSource && this.WaterSourcePos == null)
            {
                this.HasWaterSource = false;
                this.WaterSourceReacquireTimeout = Math.Max(this.WaterSourceReacquireTimeout, 1);
            }
        }

		private IAqueduct blockAqueduct;
        private float tickIntervalSeconds = 1f;

		public int WaterLevel { get; set; } = 0;

		public BlockPos WaterSourcePos { get; set; } = null;

        public bool HasWaterSource { get; set; } = false;

        private int WaterSourceReacquireTimeout = 0;
	}
}
