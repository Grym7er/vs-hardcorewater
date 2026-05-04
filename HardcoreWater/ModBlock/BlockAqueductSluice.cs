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

        // public void Activate(BlockEntityAqueductSluice entity)
        // {
        //     if (!HasFuel || Api == null) return;

        //     On = true;
        //     lastUpdateTotalDays = Api.World.Calendar.TotalDays;

        //     animUtil?.StartAnimation(new AnimationMetaData() { Animation = "on-spin", Code = "on-spin", EaseInSpeed = 1, EaseOutSpeed = 2, AnimationSpeed = 1f });
        //     this.entity.MarkDirty(true);
        //     ToggleAmbientSound(true);
        // }

        // public void Deactivate()
        // {
        //     animUtil?.StopAnimation("on-spin");
        //     On = false;
        //     ToggleAmbientSound(false);
        //     MarkDirty(true);
        // }

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
