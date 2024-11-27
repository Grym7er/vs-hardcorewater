using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

using HardcoreWater.ModNetwork;
using HardcoreWater.ModBlock;
using HardcoreWater.ModBlockEntity;
using HarmonyLib;
using AdditionalSpawnConstraints.ModPatches;
using Vintagestory.GameContent;

namespace HardcoreWater
{
    public class HardcoreWaterModSystem : ModSystem
    {
        private IServerNetworkChannel serverChannel;
        private ICoreAPI api;
        public Harmony harmonyInst;

        public override void StartPre(ICoreAPI api)
        {
            string cfgFileName = "HardcoreWater.json";

            try 
            {
                HardcoreWaterConfig cfgFromDisk;
                if ((cfgFromDisk = api.LoadModConfig<HardcoreWaterConfig>(cfgFileName)) == null)
                {
                    api.StoreModConfig(HardcoreWaterConfig.Loaded, cfgFileName);
                }
                else
                {
                    HardcoreWaterConfig.Loaded = cfgFromDisk;
                }
            } 
            catch 
            {
                api.StoreModConfig(HardcoreWaterConfig.Loaded, cfgFileName);
            }

            base.StartPre(api);
        }

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            base.Start(api);

            api.RegisterBlockClass("BlockAqueduct", typeof(BlockAqueduct));
			api.RegisterBlockEntityClass("BlockEntityAqueduct", typeof(BlockEntityAqueduct));

            api.Logger.Notification("Loaded Hardcore Water!");
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            // Send connecting players config settings
            this.serverChannel.SendPacket(
                new SyncConfigClientPacket {
                    AqueductUpdateFrequencySeconds = HardcoreWaterConfig.Loaded.AqueductUpdateFrequencySeconds
                }, player);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            sapi.Event.PlayerJoin += this.OnPlayerJoin; 
            
            if (!Harmony.HasAnyPatches(Mod.Info.ModID)) {
				harmonyInst = new Harmony(Mod.Info.ModID);

				PatchBlockBehaviorFiniteSpreadingLiquidTryLoweringLiquidLevel(sapi, harmonyInst);
			}

            // Create server channel for config data sync
            this.serverChannel = sapi.Network.RegisterChannel("hardcorewater")
                .RegisterMessageType<SyncConfigClientPacket>()
                .SetMessageHandler<SyncConfigClientPacket>((player, packet) => {});

            base.StartServerSide(sapi);
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            // Sync config settings with clients
            capi.Network.RegisterChannel("hardcorewater")
                .RegisterMessageType<SyncConfigClientPacket>()
                .SetMessageHandler<SyncConfigClientPacket>(p => {
                    this.Mod.Logger.Event("Received config settings from server");
                    HardcoreWaterConfig.Loaded.AqueductUpdateFrequencySeconds = p.AqueductUpdateFrequencySeconds;
                });
        }
        
        public override void Dispose()
        {
            if (this.api is ICoreServerAPI sapi)
            {
                sapi.Event.PlayerJoin -= this.OnPlayerJoin;
            }
        }

        internal void PatchBlockBehaviorFiniteSpreadingLiquidTryLoweringLiquidLevel(ICoreServerAPI sapi, Harmony harmony)
		{
			var original = typeof(BlockBehaviorFiniteSpreadingLiquid).GetMethod("TryLoweringLiquidLevel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			var prefix = typeof(PatchBlockBehaviorFiniteSpreadingLiquid).GetMethod("PrefixTryLoweringLiquidLevel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
				
			harmony.Patch(original, new HarmonyMethod(prefix), null);			

			sapi.Logger.Notification("Applied patch to VintageStory's BlockBehaviorFiniteSpreadingLiquid.TryLoweringLiquidLevel from Hardcore Water!");		
		}
    }
}
