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
using System.Collections.Generic;

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
        public static bool IsRealisticWaterActive { get; private set; } = false;
        public static bool IsCollapseStoryActive { get; private set; } = false;
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

            api.RegisterBlockClass("BlockAqueductSluice", typeof(BlockAqueductSluice));
            api.RegisterBlockEntityClass("BlockEntityAqueductSluice", typeof(BlockEntityAqueductSluice));

            api.Logger.Notification("Loaded Hardcore Water!");

            IsRealisticWaterActive = api.ModLoader.IsModEnabled("realisticwater");
            IsCollapseStoryActive = api.ModLoader.IsModEnabled("collapsestory");
            if (IsCollapseStoryActive)
            {
                api.Logger.Notification("Collapse Story detected, enabling collapse story compat");
            }else{
                api.Logger.Notification("Collapse Story not detected, disabling collapse story compat");
            }
            if (IsRealisticWaterActive)
            {
                api.Logger.Notification("Realistic Water detected, enabling realistic water compat");
            }else{
                api.Logger.Notification("Realistic Water not detected, disabling realistic water compat");
            }
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

                // patch realisticwater
                if (IsRealisticWaterActive)
                {
                    PatchRealisticWater(sapi, harmonyInst);

                }

                // patch collapse story
                if (IsCollapseStoryActive)
                {
                    PatchCollapseStory(sapi, harmonyInst);
                }
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

        internal void PatchRealisticWater(ICoreServerAPI sapi, Harmony harmony)
        {
            // api.Logger.Notification("Patching Realistic Water: patching updateOwnFlowDir");

            PatchBlockBehaviorRealisticSpreadingLiquidTryLoweringLiquidLevel(sapi, harmony);
            PatchBlockBehaviorRealisticSpreadingLiquidCanSpreadIntoBlock(sapi, harmony);
            PatchBlockBehaviorRealisticSpreadingLiquidFindDownwardPaths(sapi, harmony);
            PatchBlockBehaviorRealisticSpreadingLiquidupdateOwnFlowDir(sapi, harmony);
            
            // PatchBlockBehaviorRealisticSpreadingLiquidSpreadAndUpdateLiquidLevels(sapi, harmony);
            // PatchBlockBehaviorRealisticSpreadingCanSpreadIntoBlock(sapi, harmony);

        }

        internal void PatchCollapseStory(ICoreServerAPI sapi, Harmony harmony)
        {
            PatchBlockBehaviorCollapseStoryCollapseLayer(sapi, harmony);
        }
        internal void PatchBlockBehaviorRealisticSpreadingLiquidupdateOwnFlowDir(ICoreServerAPI sapi, Harmony harmony)
        {
            Type targetType = AccessTools.TypeByName("RealisticWater.BlockBehaviorRealisticSpreadingLiquid");
            MethodInfo original = targetType.GetMethod(
                "updateOwnFlowDir",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(Block), typeof(IWorldAccessor), typeof(BlockPos) },
                null);

            MethodInfo prefix = typeof(PatchBlockBehaviorRealisticSpreadingLiquid).GetMethod(
                nameof(PatchBlockBehaviorRealisticSpreadingLiquid.PrefixupdateOwnFlowDir),
                BindingFlags.NonPublic | BindingFlags.Static);

            if (original == null || prefix == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked][realisticwater compat] Skipped patch for BlockRealistcSpreadingLiquid.updateOwnFlowDir. Method lookup failed for current game version.");
                return;
            }

            harmony.Patch(original, new HarmonyMethod(prefix), null);

            sapi.Logger.Notification("Applied prefix to Realistic Water's BlockBehaviorRealisticSpreadingLiquid.updateOwnFlowDir from Hardcore Water!");
        }
        internal void PatchBlockBehaviorRealisticSpreadingLiquidCanSpreadIntoBlock(ICoreServerAPI sapi, Harmony harmony)
        {
            Type targetType = AccessTools.TypeByName("RealisticWater.BlockBehaviorRealisticSpreadingLiquid");
            MethodInfo original = targetType.GetMethod(
                "CanSpreadIntoBlock",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public, 
                null,
                new[] {typeof(Block), typeof(Block), typeof(BlockPos), typeof(BlockPos), typeof(BlockFacing), typeof(IWorldAccessor)},
                null);

            MethodInfo prefix = typeof(PatchBlockBehaviorRealisticSpreadingLiquid).GetMethod(
                nameof(PatchBlockBehaviorRealisticSpreadingLiquid.PrefixCanSpreadIntoBlock),
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);

            if (original == null || prefix == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked][realisticwater compat] Skipped patch for BlockRealisticSpreadingLiquid.CanSpreadIntoBlock. Method lookup failed for current game version.");
                return;
            }

            harmony.Patch(original, new HarmonyMethod(prefix), null);

            sapi.Logger.Notification("Applied prefix to Realistic Water's BlockBehaviorRealisticSpreadingLiquid.CanSpreadIntoBlock from Hardcore Water!");
        }

        internal void PatchBlockBehaviorCollapseStoryCollapseLayer(ICoreServerAPI sapi, Harmony harmony)
        {
            if (!SetupCollapseStoryReflection(sapi))
            {
                sapi.Logger.Warning("[hardcorewaterforked][collapse story compat] Skipped patch for BlockCollapseStory.CollapseLayer. Setup of CollapseStory reflection failed.");
                return;
            }

            Type targetType = AccessTools.TypeByName("CollapseStory.ModSystemStructuralLoad");
            Type BlockBehaviorStructuralLoadType = AccessTools.TypeByName("CollapseStory.BlockBehaviorStructuralLoad");

            if (targetType == null || BlockBehaviorStructuralLoadType == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked][collapse story compat] Field lookup failed: {0}, {1}", targetType == null ? "targetType is null" : "ok", BlockBehaviorStructuralLoadType == null ? "BlockBehaviorStructuralLoadType is null" : "ok");
                return;
            }

            MethodInfo original = targetType.GetMethod(
                "CollapseLayer",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] {typeof(IWorldAccessor), typeof(List<BlockPos>), typeof(HashSet<BlockPos>), typeof(int), BlockBehaviorStructuralLoadType},
                null);

            MethodInfo prefix = typeof(PatchBlockBehaviorCollapseStory).GetMethod(
                nameof(PatchBlockBehaviorCollapseStory.PrefixCollapseLayer),
                BindingFlags.NonPublic | BindingFlags.Static);

            if (original == null || prefix == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked][collapse story compat] Skipped patch for BlockCollapseStory.CollapseLayer: {0}, {1}", original == null ? "original is null" : "ok", prefix == null ? "prefix is null" : "ok");
                return;
            }

            harmony.Patch(original, new HarmonyMethod(prefix), null);

            sapi.Logger.Notification("Applied prefix to Realistic Water's BlockBehaviorRealisticSpreadingLiquid.CanSpreadIntoBlock from Hardcore Water!");
        }

        internal bool SetupCollapseStoryReflection(ICoreServerAPI sapi)
        {
            Type ModSystemStructuralLoadType = AccessTools.TypeByName("CollapseStory.ModSystemStructuralLoad");
            Type CollapseStorySystemType = AccessTools.TypeByName("CollapseStory.CollapseStorySystem");

            if (CollapseStorySystemType == null || ModSystemStructuralLoadType == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked][collapse story compat] Field lookup failed: {0}, {1}", CollapseStorySystemType == null ? "CollapseStorySystemType is null" : "ok", ModSystemStructuralLoadType == null ? "ModSystemStructuralLoadType is null" : "ok");
                return false;
            }

            FieldInfo CollapseInProgress = CollapseStorySystemType.GetField("CollapseInProgress");
            FieldInfo ChiselAggregateCache = CollapseStorySystemType.GetField("ChiselAggregateCache");
            FieldInfo StressCache = CollapseStorySystemType.GetField("StressCache");

            if (CollapseInProgress == null || ChiselAggregateCache == null || StressCache == null)
            {
                sapi.Logger.Warning(
                    "[hardcorewaterforked][collapse story compat] Field lookup failed. CollapseInProgress={0}, ChiselAggregateCache={1}, StressCache={2}",
                    CollapseInProgress == null ? "null" : "ok",
                    ChiselAggregateCache == null ? "null" : "ok",
                    StressCache == null ? "null" : "ok"
                );
                return false;
            }

            object chiselCacheobj = ChiselAggregateCache.GetValue(null);
            object stressCacheobj = StressCache.GetValue(null);

            MethodInfo ChiselAggregateCache_Remove = AccessTools.Method(chiselCacheobj.GetType(), "Remove", new[] {typeof(BlockPos)});
            MethodInfo StressCache_Remove = AccessTools.Method(stressCacheobj.GetType(), "Remove", new[] {typeof(BlockPos)});

            if (CollapseInProgress == null || ChiselAggregateCache == null || StressCache == null || ChiselAggregateCache_Remove == null || StressCache_Remove == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked][collapse story compat] Skipped setup of CollapseStory reflection. Field or method lookup failed for current game version.");
                return false;
            }

            PatchBlockBehaviorCollapseStory.SetupReflection(ModSystemStructuralLoadType, CollapseStorySystemType, CollapseInProgress, ChiselAggregateCache, StressCache, ChiselAggregateCache_Remove, StressCache_Remove);

            return true;
        }

        internal bool SetupRWPosAndDist(ICoreServerAPI sapi)
        {
            Type PosAndDistType = AccessTools.TypeByName("RealisticWater.PosAndDist");

            if (PosAndDistType == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked] Skipped setup of RW PosAndDist. Type lookup failed for current game version.");
                return false;
            }

            FieldInfo PatchPos = PosAndDistType.GetField("pos");
            FieldInfo PatchDist = PosAndDistType.GetField("dist");

            if (PatchPos == null || PatchDist == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked] Skipped setup of RW PosAndDist. Field lookup failed for current game version.");
                return false;
            }
            PatchBlockBehaviorRealisticSpreadingLiquid.SetupPosAndDistReflection(
                                                                            PosAndDistType,
                                                                            PatchPos,
                                                                            PatchDist
                                                                            );

            return true;





        }

        internal void PatchBlockBehaviorRealisticSpreadingLiquidFindDownwardPaths(ICoreServerAPI sapi, Harmony harmony)
        {
            if (!SetupRWPosAndDist(sapi))
            {
                sapi.Logger.Warning("[hardcorewaterforked][realisticwater compat] Skipped patch for BlockBehaviorRealisticSpreadingLiquid.FindDownwardPaths. Setup of RW PosAndDist failed.");
                return;
            }

            Type targetType = AccessTools.TypeByName("RealisticWater.BlockBehaviorRealisticSpreadingLiquid");
            
            MethodInfo original = targetType.GetMethod("FindDownwardPaths", BindingFlags.Public | BindingFlags.Instance, null, new Type[] {
                    typeof(IWorldAccessor),  typeof(BlockPos), typeof(Block)
                }, null);
            MethodInfo postfix = typeof(PatchBlockBehaviorRealisticSpreadingLiquid).GetMethod("PostfixFindDownwardPaths", BindingFlags.NonPublic | BindingFlags.Static);

            if (original == null || postfix == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked] Skipped patch for BlockBehaviorRealisticSpreadingLiquid.FindDownwardPaths. Method lookup failed for current game version.");
                return;
            }

            harmony.Patch(original, null, new HarmonyMethod(postfix));

            sapi.Logger.Notification("Applied patch to VintageStory's BlockBehaviorRealisticSpreadingLiquid.FindDownwardPaths from Hardcore Water!");
        }
        internal void PatchBlockBehaviorRealisticSpreadingLiquidTryLoweringLiquidLevel(ICoreServerAPI sapi, Harmony harmony)
        {
            Type targetType = AccessTools.TypeByName("RealisticWater.BlockBehaviorRealisticSpreadingLiquid");
            
            MethodInfo original = targetType.GetMethod("TryLoweringLiquidLevel", BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] {
                    typeof(Block), typeof(IWorldAccessor),  typeof(BlockPos)
                }, null);
            MethodInfo prefix = typeof(PatchBlockBehaviorRealisticSpreadingLiquid).GetMethod("PrefixTryLoweringLiquidLevel", BindingFlags.NonPublic | BindingFlags.Static);

            if (original == null || prefix == null)
            {
                sapi.Logger.Warning("[hardcorewaterforked] Skipped patch for BlockBehaviorRealisticSpreadingLiquid.TryLoweringLiquidLevel. Method lookup failed for current game version.");
                return;
            }

            harmony.Patch(original, new HarmonyMethod(prefix), null);

            sapi.Logger.Notification("Applied patch to VintageStory's BlockBehaviorRealisticSpreadingLiquid.TryLoweringLiquidLevel from Hardcore Water!");
        }
    }
}
