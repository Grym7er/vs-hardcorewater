using HardcoreWater;
using HardcoreWater.ModBlock;
using System;
using System.Linq;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Common.Entities;

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
                if (this.Api.World.BlockAccessor.GetBlockEntity<BlockEntityAqueductSluice>(blockPos) is BlockEntityAqueductSluice sluice)
                {
                    if (!sluice.IsOpen) return false;
                    
                    string sluice_orientation = sluice.blockAqueduct.Orientation;
                    string my_orientation = this.blockAqueduct.Orientation;

                    bool myOrientationIsCorrect = false;

                    if (((sluice_orientation == "ns" || sluice_orientation == "sn") && (my_orientation == "ns" || my_orientation == "sn")) || ((sluice_orientation == "we" || sluice_orientation == "ew") && (my_orientation == "we" || my_orientation == "ew")))
                    {
                        myOrientationIsCorrect = true;
                    }

                    bool correctOrientation = myOrientationIsCorrect;
                    bool notSourcingThis = sluice.WaterSourcePos != this.WaterSourcePos;
                    bool notSourcingEachOther = (this.WaterSourcePos == null || sluice.WaterSourcePos == null) || !(sluice.WaterSourcePos == this.Pos && this.WaterSourcePos == sluice.Pos);
                    bool hasMinWater = sluice.HasWaterSource;
                    bool isValid = correctOrientation && notSourcingThis && notSourcingEachOther && hasMinWater;
                    return isValid;
                }

                else if (this.Api.World.BlockAccessor.GetBlockEntity<BlockEntityAqueduct>(blockPos) is BlockEntityAqueduct adjacentAqueduct )
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

        private static bool IsRapidWaterCodePath(Block block)
        {
            return block?.Code?.Path != null && block.Code.Path.StartsWith("rapidwater");
        }

        

        /// <summary>
        /// Horizontal flow letter for game:rapidwater-{letter}-{level}. Vertical/same-cell sources use stable downstream toward blockPosFB[1] (south for NS, east for WE).
        /// </summary>
        public char ResolveFlowLetter(BlockPos waterSourcePos)
        {
            bool trenchNs = BlockFacing.FromFirstLetter(this.blockAqueduct.Orientation) == BlockFacing.NORTH;
            int ddx = this.Pos.X - waterSourcePos.X;
            int ddz = this.Pos.Z - waterSourcePos.Z;
            bool sameColumn = ddx == 0 && ddz == 0;

            if (sameColumn || waterSourcePos.Equals(this.Pos))
            {
                return trenchNs ? 's' : 'e';
            }

            if (trenchNs)
            {
                if (ddz != 0)
                {
                    return ddz > 0 ? 's' : 'n';
                }

                return 's';
            }

            if (ddx != 0)
            {
                return ddx > 0 ? 'e' : 'w';
            }

            return 'e';
        }

        private bool WaterSourcePosCarriesRapids(BlockPos waterSourcePos)
        {
            // If chunk is not loaded, keep existing value
            if (this.Api.World.BlockAccessor.GetChunkAtBlockPos(waterSourcePos) == null)
            {
                return this.CarriesRapids;
            }
            BlockEntityAqueduct watersourceposblock = this.Api.World.BlockAccessor.GetBlockEntity<BlockEntityAqueduct>(waterSourcePos);
            return watersourceposblock?.CarriesRapids ?? false;
        }

        private bool WaterSourcePosIsRapidsBlock(BlockPos waterSourcePos)
        {
            // If chunk is not loaded, keep existing value
            if (this.Api.World.BlockAccessor.GetChunkAtBlockPos(waterSourcePos) == null)
            {
                return this.CarriesRapids;
            }
            Block watersourceposblock = this.Api.World.BlockAccessor.GetBlock(waterSourcePos, BlockLayersAccess.Fluid);
            return IsRapidWaterCodePath(watersourceposblock);
        }

        private void TryApplyAqueductFluidFill(BlockPos[] blockPosFB, ref bool shouldMarkDirty, ref bool shouldTriggerNeighborUpdate)
        {
            if (!this.HasWaterSource || this.WaterSourcePos == null || HasInvalidSourceDependency(blockPosFB[0], blockPosFB[1]))
            {
                return;
            }

            // Handle fresh, salt, and boiling water separately, lest we desalinate or something else weird
            Block ourBlockFluid = this.Api.World.BlockAccessor.GetBlock(this.Pos, BlockLayersAccess.Fluid);
            Block liquidBlockToSet;
            string ownerControllerId = null;
            string managedFamilyId = null;
            Block compatBlock = null;
            bool compatResolved = false;

            if (compatResolved && compatBlock != null)
            {
                liquidBlockToSet = compatBlock;
                this.CarriesRapids = false;
            }
            else if (ourBlockFluid != null && ourBlockFluid.Code != null && ourBlockFluid.Code.BeginsWith("game", "salt"))
            {
                char flowdir = ResolveFlowLetter(this.WaterSourcePos);
                liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:saltwater-" + flowdir + "-" + Math.Min(7, this.WaterLevel)));
                this.CarriesRapids = false;
            }
            else if (ourBlockFluid != null && ourBlockFluid.Code != null && ourBlockFluid.Code.BeginsWith("game", "boiling"))
            {
                char flowdir = ResolveFlowLetter(this.WaterSourcePos);
                liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:boilingwater-" + flowdir + "-" + Math.Min(7, this.WaterLevel)));
                this.CarriesRapids = false;
            }
            else
            {
                bool wantsRapid;
                if (compatResolved)
                {
                    wantsRapid = false;
                }
                else if (!HardcoreWaterConfig.Loaded.EnableAqueductRapids)
                {
                    wantsRapid = false;
                }
                // Check here if the watersourcepos block is a rapids block
                else if (WaterSourcePosCarriesRapids(this.WaterSourcePos) || WaterSourcePosIsRapidsBlock(this.WaterSourcePos))
                {
                    // if the upstream aqueduct or block is rapids, then we want rapids
                    wantsRapid = true;
                }
                else
                {
                    wantsRapid = false;
                }

                this.CarriesRapids = wantsRapid;
                int lvl = Math.Min(7, this.WaterLevel);
                string lvlStr = lvl.ToString();
                if (wantsRapid)
                {
                    char flow = this.ResolveFlowLetter(this.WaterSourcePos);
                    liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:rapidwater-" + flow + "-" + lvlStr));
                    if (liquidBlockToSet == null)
                    {
                        char flowdir = ResolveFlowLetter(this.WaterSourcePos);
                        if (HardcoreWaterModSystem.IsRealisticWaterActive)
                        {
                            liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:realisticwater-" + flowdir + "-" + lvlStr + "-" + 19));
                        }
                        else
                        {
                            liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:water-" + flowdir + "-" + lvlStr));
                        }
                        // liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:water-" + flowdir + "-" + lvlStr));
                        this.CarriesRapids = false;
                    }
                }
                else
                {
                    char flowdir = ResolveFlowLetter(this.WaterSourcePos);
                    if (HardcoreWaterModSystem.IsRealisticWaterActive)
                    {
                        liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:realisticwater-" + flowdir + "-" + lvlStr + "-" + 19));
                    }
                    else
                    {
                        liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:water-" + flowdir + "-" + lvlStr));
                    }
                    // liquidBlockToSet = this.Api.World.GetBlock(new AssetLocation("game:water-" + flowdir + "-" + lvlStr));
                }
            }

            bool hasFluidCodePath = ourBlockFluid != null && ourBlockFluid.Code != null && ourBlockFluid.Code.Path != null;
            bool notIced = !hasFluidCodePath || !ourBlockFluid.Code.Path.Contains("ice");
            

            // Vanilla updateOwnFlowDir is skipped for these cells (Harmony). Skip redundant SetBlock only when fluid already
            // matches our resolved target id (still/wrong-letter vs w-6 then get one corrective SetBlock).
            int targetHeight = Math.Min(7, this.WaterLevel);
            bool skipRapidVariantReplace = this.CarriesRapids
                && ourBlockFluid != null
                && IsRapidWaterCodePath(ourBlockFluid)
                && ourBlockFluid.LiquidLevel == targetHeight
                && liquidBlockToSet != null
                && ourBlockFluid.BlockId == liquidBlockToSet.BlockId;
            bool shouldReplaceFluid = ourBlockFluid != null && liquidBlockToSet != null && notIced
                && (ourBlockFluid.LiquidLevel < this.WaterLevel
                    || (!skipRapidVariantReplace && ourBlockFluid.BlockId != liquidBlockToSet.BlockId));

            // Add another part to shouldReplaceFluid:
            // If I am a sluice, and the sluice is open, then go ahead and replace the fluid (keep normal logic)
            // If I am a sluice, and I am closed:
            // 1. Check the direction that the water is coming from.
            // 2. If the water is trying to flow from the closed end, don't replace the fluid.
            // 3. If the water is trying to flow from the open end, then replace the fluid.

            if (this is BlockEntityAqueductSluice thisBlockEntityAqueductSluice)
            // if (thisBlockEntityAqueductSluice!=null)
            {
                if (!thisBlockEntityAqueductSluice.IsOpen)
                {
                    BlockFacing closedFace = BlockFacing.FromFirstLetter(this.blockAqueduct.Orientation[0]);
                    // Get the direction that the water is coming from
                    BlockFacing watercomefromfacing = BlockFacing.HORIZONTALS.FirstOrDefault(f => this.Pos.AddCopy(f).Equals(this.WaterSourcePos));
                    shouldReplaceFluid = shouldReplaceFluid && (watercomefromfacing != closedFace);
                    // Console.WriteLine("shouldReplaceFluid: " + shouldReplaceFluid + " called from block: " + this.Block.Code.ToShortString());
                }
            }



            if (shouldReplaceFluid)
            {

                // Console.WriteLine("Setting block: " + liquidBlockToSet.Code.ToShortString() + " at pos: " + this.Pos + " in block type: " + this.Block.Code.ToShortString());
                // Console.WriteLine("Ourblock code: " + ourBlockFluid.Code.ToShortString());
                // Console.WriteLine("\n");
                this.Api.World.BlockAccessor.SetBlock(liquidBlockToSet.BlockId, this.Pos, BlockLayersAccess.Fluid);

                shouldTriggerNeighborUpdate = true;
                shouldMarkDirty = true;
            }
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
            bool oldCarriesRapids = this.CarriesRapids;

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
            else if (BlockFacing.FromFirstLetter(this.blockAqueduct.Orientation) == BlockFacing.SOUTH)
            {
                blockPosFB[0] = this.Pos.SouthCopy();
				blockPosFB[1] = this.Pos.NorthCopy();
            }
            else if (BlockFacing.FromFirstLetter(this.blockAqueduct.Orientation) == BlockFacing.WEST)
            {
				blockPosFB[0] = this.Pos.WestCopy();
				blockPosFB[1] = this.Pos.EastCopy();
			}
            else
            {
                blockPosFB[0] = this.Pos.EastCopy();
				blockPosFB[1] = this.Pos.WestCopy();
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
                    this.CarriesRapids = false;
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
                    bool stateChanged = this.HasWaterSource || this.WaterSourcePos != null || this.WaterSourceReacquireTimeout != GetReacquireTimeoutTicks() || this.CarriesRapids;
                    this.WaterSourceReacquireTimeout = GetReacquireTimeoutTicks();
                    this.HasWaterSource = false;
                    this.WaterSourcePos = null;
                    this.CarriesRapids = false;
                    shouldMarkDirty = shouldMarkDirty || stateChanged;
				}
                else
                {
                    this.TryApplyAqueductFluidFill(blockPosFB, ref shouldMarkDirty, ref shouldTriggerNeighborUpdate);

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

                    this.TryApplyAqueductFluidFill(blockPosFB, ref shouldMarkDirty, ref shouldTriggerNeighborUpdate);

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

                    this.CarriesRapids = false;
                }
            }

            if (shouldTriggerNeighborUpdate)
            {
                this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos);
            }
            bool sourcePosChanged = (oldWaterSourcePos == null) != (this.WaterSourcePos == null)
                || (oldWaterSourcePos != null && !oldWaterSourcePos.Equals(this.WaterSourcePos));
            if (oldHasWaterSource != this.HasWaterSource || oldWaterLevelState != this.WaterLevel || sourcePosChanged || oldCarriesRapids != this.CarriesRapids)
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
            if (!Api.Side.IsServer())
            {
                return;
            }
            this.RegisterGameTickListener(new Action<float>(this.onServerTick1s), (int)Math.Round(this.tickIntervalSeconds * 1000), 0);
		}

		public override void ToTreeAttributes(ITreeAttribute tree)
		{
			base.ToTreeAttributes(tree);
			tree.SetInt("WaterLevel", this.WaterLevel);
            tree.SetInt("WaterSourceReacquireTimeout", this.WaterSourceReacquireTimeout);
			tree.SetBool("HasWaterSource", this.HasWaterSource);
            tree.SetBool("CarriesRapids", this.CarriesRapids);
			if (this.HasWaterSource)
				tree.SetBlockPos("WaterSourcePos", this.WaterSourcePos);
		}

		public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
		{
			base.FromTreeAttributes(tree, worldAccessForResolve);
			this.WaterLevel = tree.GetInt("WaterLevel");
            this.WaterSourceReacquireTimeout = tree.GetInt("WaterSourceReacquireTimeout", 0);
            this.HasWaterSource = tree.GetBool("HasWaterSource", false);
            this.CarriesRapids = tree.GetBool("CarriesRapids", false);
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

        /// <summary>Whether this segment last resolved to carrying vanilla rapids (persisted; refreshed while supplied).</summary>
        public bool CarriesRapids { get; private set; } = false;

        private int WaterSourceReacquireTimeout = 0;
	}
}
