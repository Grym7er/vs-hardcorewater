using System.Text;
using HardcoreWater.ModBlockEntity;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace HardcoreWater.ModBlock
{
    internal static class AqueductDebugInfo
    {
        internal static string Build(ICoreAPI api, Block block, IWorldAccessor world, BlockPos pos)
        {
            ICoreClientAPI capi = api as ICoreClientAPI;
            if (capi == null || !capi.Settings.Bool["extendedDebugInfo"])
            {
                return "";
            }

            BlockEntityAqueduct blockEntityAqueduct = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityAqueduct;
            if (blockEntityAqueduct == null)
            {
                return "";
            }

            StringBuilder dsc = new StringBuilder();
            dsc.AppendLine(Lang.Get("Water Level: {0}, Source Position: {1}, Current Position: {2}, Carries rapids: {3}", new object[]
                {
                    blockEntityAqueduct.WaterLevel,
                    blockEntityAqueduct.WaterSourcePos != null ? blockEntityAqueduct.WaterSourcePos.ToLocalPosition(api).ToString() : "null",
                    pos.ToLocalPosition(api).ToString(),
                    blockEntityAqueduct.CarriesRapids
                }));

            if (world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid) is Block fluidBlock)
            {
                dsc.AppendLine(Lang.Get("Liquid: {0}", new object[]
                {
                    fluidBlock.Code
                }));
            }

            if (block.GetLiquidBarrierHeightOnSide(BlockFacing.NORTH, pos) > 0f) dsc.AppendLine("Barrier to liquid on side: North");
            if (block.GetLiquidBarrierHeightOnSide(BlockFacing.EAST, pos) > 0f) dsc.AppendLine("Barrier to liquid on side: East");
            if (block.GetLiquidBarrierHeightOnSide(BlockFacing.SOUTH, pos) > 0f) dsc.AppendLine("Barrier to liquid on side: South");
            if (block.GetLiquidBarrierHeightOnSide(BlockFacing.WEST, pos) > 0f) dsc.AppendLine("Barrier to liquid on side: West");
            BlockEntityAqueductSluice blockEntityAqueductSluice = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityAqueductSluice;
            if (blockEntityAqueductSluice == null)
            {
                return dsc.ToString();
            }
            dsc.AppendLine(Lang.Get("Sluice Open: {0}", new object[]
            {
                blockEntityAqueductSluice.IsOpen
            }));
            
            return dsc.ToString();
        }
    }
}
