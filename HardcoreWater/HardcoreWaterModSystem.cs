using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

using HardcoreWater.ModNetwork;
using HardcoreWater.ModBlock;
using HardcoreWater.ModBlockEntity;
using HardcoreWater.Compat;
using HarmonyLib;
using AdditionalSpawnConstraints.ModPatches;
using Vintagestory.GameContent;
using System;
using System.Reflection;
using Vintagestory.API.MathTools;

namespace HardcoreWater
{
    public class HardcoreWaterModSystem : ModSystem
    {
        private const string ArchimedesPrimaryModId = "thetruearchimedesscrew";
        private const string ArchimedesLegacyModId = "archimedes_screw";
        private const int CompatRecoveryTickMs = 30000;
        private const int CompatSummaryTickMs = 180000;
        private const int MaxCompatRecoveryAttempts = 10;

        private IServerNetworkChannel serverChannel;
        private ICoreAPI api;
        public Harmony harmonyInst;
        internal static ArchimedesCompatService ArchimedesCompat { get; private set; }
        private long compatRecoveryTickListenerId = -1;
        private int compatRecoveryAttempts = 0;
        private int compatSummaryAccumulatedMs = 0;

        public override void StartPre(ICoreAPI api)
        {
            string cfgFileName = "HardcoreWater.json";

            try 
            {
                HardcoreWaterConfig cfgFromDisk;
                if ((cfgFromDisk = api.LoadModConfig<HardcoreWaterConfig>(cfgFileName)) == null)
                {
                    HardcoreWaterConfig.Loaded.Sanitize();
                    api.StoreModConfig(HardcoreWaterConfig.Loaded, cfgFileName);
                }
                else
                {
                    HardcoreWaterConfig.Loaded = cfgFromDisk;
                    HardcoreWaterConfig.Loaded.Sanitize();
                }
            } 
            catch 
            {
                HardcoreWaterConfig.Loaded.Sanitize();
                api.StoreModConfig(HardcoreWaterConfig.Loaded, cfgFileName);
            }

            base.StartPre(api);
        }

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            base.Start(api);

            api.RegisterBlockClass("BlockAqueduct", typeof(BlockAqueduct));
            api.RegisterBlockClass("BlockEnclosedAqueduct", typeof(BlockEnclosedAqueduct));
            api.RegisterBlockEntityClass("BlockEntityAqueduct", typeof(BlockEntityAqueduct));

            api.Logger.Notification("Loaded Hardcore Water!");
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            if (this.serverChannel == null) return;

            // Send connecting players config settings
            this.serverChannel.SendPacket(
                new SyncConfigClientPacket {
                    AqueductUpdateFrequencySeconds = HardcoreWaterConfig.Loaded.AqueductUpdateFrequencySeconds
                }, player);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            // Create server channel for config data sync before subscribing to events that use it
            this.serverChannel = sapi.Network.RegisterChannel("hardcorewaterforked")
                .RegisterMessageType<SyncConfigClientPacket>()
                .SetMessageHandler<SyncConfigClientPacket>((player, packet) => {});

            if (!Harmony.HasAnyPatches(Mod.Info.ModID)) {
				harmonyInst = new Harmony(Mod.Info.ModID);

				PatchBlockBehaviorFiniteSpreadingLiquidTryLoweringLiquidLevel(sapi, harmonyInst);

                PatchBlockBehaviorFiniteSpreadingLiquidCanSpreadIntoBlock(sapi, harmonyInst);

                PatchBlockBehaviorFiniteSpreadingLiquidFindDownwardPaths(sapi, harmonyInst);

                PatchBlockBehaviorFiniteSpreadingLiquidUpdateOwnFlowDir(sapi, harmonyInst);
            }

            sapi.Event.PlayerJoin += this.OnPlayerJoin;
            sapi.Event.SaveGameLoaded += this.OnSaveGameLoaded;
            compatRecoveryTickListenerId = sapi.Event.RegisterGameTickListener(OnCompatRecoveryTick, CompatRecoveryTickMs);

            base.StartServerSide(sapi);
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            // Sync config settings with clients
            capi.Network.RegisterChannel("hardcorewaterforked")
                .RegisterMessageType<SyncConfigClientPacket>()
                .SetMessageHandler<SyncConfigClientPacket>(p => {
                    this.Mod.Logger.Event("Received config settings from server");
                    HardcoreWaterConfig.Loaded.AqueductUpdateFrequencySeconds = p.AqueductUpdateFrequencySeconds;
                    HardcoreWaterConfig.Loaded.Sanitize();
                });
        }
        
        public override void Dispose()
        {
            if (this.api is ICoreServerAPI sapi)
            {
                sapi.Event.PlayerJoin -= this.OnPlayerJoin;
                sapi.Event.SaveGameLoaded -= this.OnSaveGameLoaded;
                if (compatRecoveryTickListenerId >= 0)
                {
                    sapi.Event.UnregisterGameTickListener(compatRecoveryTickListenerId);
                    compatRecoveryTickListenerId = -1;
                }
            }

            harmonyInst?.UnpatchAll(Mod.Info.ModID);
            harmonyInst = null;
            ArchimedesCompat = null;
        }

        private void TryInitializeArchimedesCompat(ICoreServerAPI sapi)
        {
            bool installed = sapi.ModLoader.IsModEnabled(ArchimedesPrimaryModId) || sapi.ModLoader.IsModEnabled(ArchimedesLegacyModId);
            if (!installed)
            {
                ArchimedesCompat = null;
                return;
            }

            ArchimedesCompatService created = ArchimedesCompatService.Create(sapi);
            if (created != null)
            {
                ArchimedesCompat = created;
                compatRecoveryAttempts = 0;
                return;
            }

            if (ArchimedesCompat?.IsActive == true)
            {
                return;
            }
        }

        private void OnSaveGameLoaded()
        {
            if (this.api is not ICoreServerAPI sapi)
            {
                return;
            }

            // Align with waterfall-compat style: re-resolve once when game state is fully loaded.
            if (ArchimedesCompat == null)
            {
                TryInitializeArchimedesCompat(sapi);
            }

            ArchimedesCompat?.Refresh();
            ArchimedesCompat?.LogDebugSummaryIfNeeded();
        }

        private void OnCompatRecoveryTick(float dt)
        {
            if (this.api is not ICoreServerAPI sapi)
            {
                return;
            }

            bool installed = sapi.ModLoader.IsModEnabled(ArchimedesPrimaryModId) || sapi.ModLoader.IsModEnabled(ArchimedesLegacyModId);
            if (!installed)
            {
                return;
            }

            if (ArchimedesCompat == null)
            {
                if (compatRecoveryAttempts >= MaxCompatRecoveryAttempts)
                {
                    return;
                }

                compatRecoveryAttempts++;
                TryInitializeArchimedesCompat(sapi);
                return;
            }

            ArchimedesCompat.Refresh();
            compatSummaryAccumulatedMs += CompatRecoveryTickMs;
            if (compatSummaryAccumulatedMs >= CompatSummaryTickMs)
            {
                ArchimedesCompat.LogDebugSummaryIfNeeded();
                compatSummaryAccumulatedMs = 0;
            }
        }

        internal void PatchBlockBehaviorFiniteSpreadingLiquidTryLoweringLiquidLevel(ICoreServerAPI sapi, Harmony harmony)
		{
			MethodInfo original = typeof(BlockBehaviorFiniteSpreadingLiquid).GetMethod("TryLoweringLiquidLevel", BindingFlags.NonPublic | BindingFlags.Instance);
			MethodInfo prefix = typeof(PatchBlockBehaviorFiniteSpreadingLiquid).GetMethod("PrefixTryLoweringLiquidLevel", BindingFlags.NonPublic | BindingFlags.Static);

            if (original == null || prefix == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked] Skipped patch for BlockBehaviorFiniteSpreadingLiquid.TryLoweringLiquidLevel. Method lookup failed for current game version.");
                return;
            }

			harmony.Patch(original, new HarmonyMethod(prefix), null);			

			sapi.Logger.Notification("Applied patch to VintageStory's BlockBehaviorFiniteSpreadingLiquid.TryLoweringLiquidLevel from Hardcore Water!");		
		}

        internal void PatchBlockBehaviorFiniteSpreadingLiquidCanSpreadIntoBlock(ICoreServerAPI sapi, Harmony harmony)
        {
            MethodInfo original = typeof(BlockBehaviorFiniteSpreadingLiquid).GetMethod(
                nameof(BlockBehaviorFiniteSpreadingLiquid.CanSpreadIntoBlock),
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Block), typeof(Block), typeof(BlockPos), typeof(BlockPos), typeof(BlockFacing), typeof(IWorldAccessor) },
                null);
            MethodInfo postfix = typeof(PatchBlockBehaviorFiniteSpreadingLiquid).GetMethod(
                nameof(PatchBlockBehaviorFiniteSpreadingLiquid.PostfixCanSpreadIntoBlock),
                BindingFlags.NonPublic | BindingFlags.Static);

            if (original == null || postfix == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked] Skipped patch for BlockBehaviorFiniteSpreadingLiquid.CanSpreadIntoBlock. Method lookup failed for current game version.");
                return;
            }

            harmony.Patch(original, null, new HarmonyMethod(postfix));

            sapi.Logger.Notification("Applied postfix to Vintage Story's BlockBehaviorFiniteSpreadingLiquid.CanSpreadIntoBlock from Hardcore Water!");
        }

        internal void PatchBlockBehaviorFiniteSpreadingLiquidFindDownwardPaths(ICoreServerAPI sapi, Harmony harmony)
        {
            MethodInfo original = typeof(BlockBehaviorFiniteSpreadingLiquid).GetMethod("FindDownwardPaths", BindingFlags.Public | BindingFlags.Instance, null, new Type[] {
                    typeof(IWorldAccessor),  typeof(BlockPos), typeof(Block)
                }, null);
            MethodInfo postfix = typeof(PatchBlockBehaviorFiniteSpreadingLiquid).GetMethod("PostfixFindDownwardPaths", BindingFlags.NonPublic | BindingFlags.Static);

            if (original == null || postfix == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked] Skipped patch for BlockBehaviorFiniteSpreadingLiquid.FindDownwardPaths. Method lookup failed for current game version.");
                return;
            }

            harmony.Patch(original, null, new HarmonyMethod(postfix));

            sapi.Logger.Notification("Applied patch to VintageStory's BlockBehaviorFiniteSpreadingLiquid.FindDownwardPaths from Hardcore Water!");
        }

        internal void PatchBlockBehaviorFiniteSpreadingLiquidUpdateOwnFlowDir(ICoreServerAPI sapi, Harmony harmony)
        {
            MethodInfo original = typeof(BlockBehaviorFiniteSpreadingLiquid).GetMethod(
                "updateOwnFlowDir",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(Block), typeof(IWorldAccessor), typeof(BlockPos) },
                null);
            MethodInfo prefix = typeof(PatchBlockBehaviorFiniteSpreadingLiquid).GetMethod(
                nameof(PatchBlockBehaviorFiniteSpreadingLiquid.PrefixUpdateOwnFlowDir),
                BindingFlags.NonPublic | BindingFlags.Static);

            if (original == null || prefix == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked] Skipped patch for BlockBehaviorFiniteSpreadingLiquid.updateOwnFlowDir. Method lookup failed for current game version.");
                return;
            }

            harmony.Patch(original, new HarmonyMethod(prefix), null);

            sapi.Logger.Notification("Applied prefix to Vintage Story's BlockBehaviorFiniteSpreadingLiquid.updateOwnFlowDir from Hardcore Water!");
        }
    }
}
