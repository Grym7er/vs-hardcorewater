using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HardcoreWater.ModBlockEntity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HardcoreWater.Compat
{
    internal sealed class ArchimedesCompatService
    {
        private const string ArchimedesModId = "thetruearchimedesscrew";
        private const string ArchimedesLegacyModId = "archimedes_screw";
        private const string ArchimedesModSystemTypeName = "ArchimedesScrew.ArchimedesScrewModSystem";
        private const int MaxOwnerTraceDepth = 64;

        private readonly ICoreAPI api;
        private readonly object modSystem;
        private readonly object waterManager;
        private readonly MethodInfo tryResolveManagedWaterFamily;
        private readonly MethodInfo tryResolveVanillaWaterFamily;
        private readonly MethodInfo getManagedBlock;
        private readonly MethodInfo assignOwnedSourceForController;
        private readonly MethodInfo tryGetSourceOwner;
        private readonly MethodInfo isArchimedesSourceBlock;
        private readonly MethodInfo isArchimedesWaterBlock;

        private int managedRefillOverrides;
        private int ownerTraceResolved;
        private int ownerTraceFailed;
        private int outletOwnershipAssignments;
        private int unresolvedOwnerFallbacks;

        private ArchimedesCompatService(
            ICoreAPI api,
            object modSystem,
            object waterManager,
            MethodInfo tryResolveManagedWaterFamily,
            MethodInfo tryResolveVanillaWaterFamily,
            MethodInfo getManagedBlock,
            MethodInfo assignOwnedSourceForController,
            MethodInfo tryGetSourceOwner,
            MethodInfo isArchimedesSourceBlock,
            MethodInfo isArchimedesWaterBlock)
        {
            this.api = api;
            this.modSystem = modSystem;
            this.waterManager = waterManager;
            this.tryResolveManagedWaterFamily = tryResolveManagedWaterFamily;
            this.tryResolveVanillaWaterFamily = tryResolveVanillaWaterFamily;
            this.getManagedBlock = getManagedBlock;
            this.assignOwnedSourceForController = assignOwnedSourceForController;
            this.tryGetSourceOwner = tryGetSourceOwner;
            this.isArchimedesSourceBlock = isArchimedesSourceBlock;
            this.isArchimedesWaterBlock = isArchimedesWaterBlock;
        }

        public bool IsActive { get; private set; }

        public static ArchimedesCompatService Create(ICoreAPI api)
        {
            bool isPrimaryInstalled = api.ModLoader.IsModEnabled(ArchimedesModId);
            bool isLegacyInstalled = api.ModLoader.IsModEnabled(ArchimedesLegacyModId);
            if (!isPrimaryInstalled && !isLegacyInstalled)
            {
                api.Logger.Notification("[hardcorewater] Archimedes compatibility inactive: mod not installed.");
                return null;
            }

            Type modSystemType = ResolveType(ArchimedesModSystemTypeName);
            if (modSystemType == null)
            {
                api.Logger.Warning("[hardcorewater] Archimedes compatibility inactive: mod system type not found.");
                return null;
            }

            MethodInfo getModSystemGeneric = api.ModLoader.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetModSystem" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            MethodInfo getModSystemGenericInterface = api.ModLoader.GetType()
                .GetInterfaces()
                .SelectMany(i => i.GetMethods())
                .FirstOrDefault(m => m.Name == "GetModSystem" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            MethodInfo getModSystemsEnumerable = api.ModLoader.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetModSystems" && m.GetParameters().Length == 0);
            MethodInfo getModSystemByType = api.ModLoader.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                    m.Name == "GetModSystem" &&
                    !m.IsGenericMethod &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(Type));
            MethodInfo getModSystemByString = api.ModLoader.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                    m.Name == "GetModSystem" &&
                    !m.IsGenericMethod &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(string));
            object modSystem = null;
            try
            {
                if (getModSystemGeneric != null)
                {
                    modSystem = getModSystemGeneric.MakeGenericMethod(modSystemType).Invoke(api.ModLoader, Array.Empty<object>());
                }
                else if (getModSystemGenericInterface != null)
                {
                    modSystem = getModSystemGenericInterface.MakeGenericMethod(modSystemType).Invoke(api.ModLoader, Array.Empty<object>());
                }
                else if (getModSystemByType != null)
                {
                    modSystem = getModSystemByType.Invoke(api.ModLoader, new object[] { modSystemType });
                }
                else if (getModSystemByString != null)
                {
                    modSystem = getModSystemByString.Invoke(api.ModLoader, new object[] { ArchimedesModSystemTypeName });
                }
                else if (getModSystemsEnumerable != null)
                {
                    object allSystemsObj = getModSystemsEnumerable.Invoke(api.ModLoader, Array.Empty<object>());
                    if (allSystemsObj is System.Collections.IEnumerable allSystems)
                    {
                        foreach (object candidate in allSystems)
                        {
                            if (candidate != null && modSystemType.IsInstanceOfType(candidate))
                            {
                                modSystem = candidate;
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // fallback handled by null modSystem check below
            }
            if (modSystem == null)
            {
                api.Logger.Warning("[hardcorewater] Archimedes compatibility inactive: mod system instance not found.");
                return null;
            }

            PropertyInfo waterManagerProp = modSystemType.GetProperty("WaterManager", BindingFlags.Public | BindingFlags.Instance);
            object waterManager = waterManagerProp != null ? waterManagerProp.GetValue(modSystem) : null;
            if (waterManager == null)
            {
                api.Logger.Warning("[hardcorewater] Archimedes compatibility inactive: WaterManager unavailable.");
                return null;
            }

            Type managerType = waterManager.GetType();
            MethodInfo tryResolveManaged = managerType.GetMethod("TryResolveManagedWaterFamily", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo tryResolveVanilla = managerType.GetMethod("TryResolveVanillaWaterFamily", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getManaged = managerType.GetMethod("GetManagedBlock", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo assignOwned = managerType.GetMethod("AssignOwnedSourceForController", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo tryGetOwner = managerType.GetMethod("TryGetSourceOwner", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo isSource = managerType.GetMethod("IsArchimedesSourceBlock", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo isManaged = managerType.GetMethod("IsArchimedesWaterBlock", BindingFlags.Public | BindingFlags.Instance);

            if (tryResolveManaged == null || tryResolveVanilla == null || getManaged == null || assignOwned == null ||
                tryGetOwner == null || isSource == null || isManaged == null)
            {
                api.Logger.Warning("[hardcorewater] Archimedes compatibility inactive: required WaterManager methods missing.");
                return null;
            }

            var service = new ArchimedesCompatService(
                api,
                modSystem,
                waterManager,
                tryResolveManaged,
                tryResolveVanilla,
                getManaged,
                assignOwned,
                tryGetOwner,
                isSource,
                isManaged
            );
            service.IsActive = true;
            api.Logger.Notification("[hardcorewater] Archimedes compatibility active.");
            return service;
        }

        public void Refresh()
        {
            if (!IsActive)
            {
                return;
            }

            PropertyInfo waterManagerProp = modSystem.GetType().GetProperty("WaterManager", BindingFlags.Public | BindingFlags.Instance);
            object refreshed = waterManagerProp != null ? waterManagerProp.GetValue(modSystem) : null;
            if (refreshed != null && !ReferenceEquals(refreshed, waterManager))
            {
                api.Logger.Warning("[hardcorewater] Archimedes WaterManager instance changed after startup; restarting server recommended.");
            }
        }

        public bool TryResolveAqueductFill(
            BlockEntityAqueduct aqueduct,
            Block currentFluid,
            int waterLevel,
            out Block fillBlock,
            out string ownerControllerId,
            out string managedFamilyId)
        {
            fillBlock = null;
            ownerControllerId = null;
            managedFamilyId = null;
            if (!IsActive || aqueduct.Api == null)
            {
                return false;
            }

            Block sourceFluid = ResolveSourceFluid(aqueduct);
            if (sourceFluid == null || sourceFluid.BlockId == 0)
            {
                return false;
            }

            if (TryResolveManagedFamily(sourceFluid, out string sourceManagedFamily))
            {
                bool ownerResolved = TryTraceOwnerControllerId(aqueduct, out string tracedOwnerId, out string tracedFamilyId);
                if (ownerResolved)
                {
                    fillBlock = ResolveManagedStillBlock(tracedFamilyId, waterLevel);
                    ownerControllerId = tracedOwnerId;
                    managedFamilyId = tracedFamilyId;
                    managedRefillOverrides++;
                    ownerTraceResolved++;
                    return fillBlock != null;
                }

                ownerTraceFailed++;
                unresolvedOwnerFallbacks++;

                // Strict policy fallback: unresolved owner means vanilla outflow behavior.
                fillBlock = ResolveVanillaStillBlock(sourceManagedFamily, waterLevel)
                           ?? ResolveVanillaStillBlockFromFluid(currentFluid, waterLevel)
                           ?? ResolveVanillaStillBlockFromFluid(sourceFluid, waterLevel);
                return fillBlock != null;
            }

            if (TryResolveVanillaFamily(sourceFluid, out string vanillaFamily))
            {
                fillBlock = ResolveVanillaStillBlock(vanillaFamily, waterLevel);
                return fillBlock != null;
            }

            return false;
        }

        public int TryAssignOutletOwnership(BlockEntityAqueduct aqueduct, BlockPos[] outletPositions, string ownerControllerId, string managedFamilyId)
        {
            if (!IsActive || aqueduct.Api == null || outletPositions == null || outletPositions.Length == 0)
            {
                return 0;
            }

            IBlockAccessor ba = aqueduct.Api.World.BlockAccessor;
            int assigned = 0;
            foreach (BlockPos outletPos in outletPositions)
            {
                Block outletFluid = ba.GetBlock(outletPos, BlockLayersAccess.Fluid);
                if (!IsArchimedesWaterBlock(outletFluid))
                {
                    continue;
                }

                if (!IsArchimedesSourceBlock(outletFluid))
                {
                    continue;
                }

                if (!TryResolveManagedFamily(outletFluid, out string outletFamilyId) ||
                    !string.Equals(outletFamilyId, managedFamilyId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryGetSourceOwner(outletPos, out _))
                {
                    continue;
                }

                if (AssignOwnedSourceForController(ownerControllerId, outletPos, managedFamilyId))
                {
                    assigned++;
                }
            }

            if (assigned > 0)
            {
                outletOwnershipAssignments += assigned;
            }

            return assigned;
        }

        public void LogDebugSummaryIfNeeded()
        {
            if (!IsActive)
            {
                return;
            }

            // Low-noise one-line operational summary useful during soak tests.
            api.Logger.Debug(
                "[hardcorewater] [compat/archimedes] counters managedRefillOverrides={0}, ownerTraceResolved={1}, ownerTraceFailed={2}, outletOwnershipAssignments={3}, unresolvedOwnerFallbacks={4}",
                managedRefillOverrides,
                ownerTraceResolved,
                ownerTraceFailed,
                outletOwnershipAssignments,
                unresolvedOwnerFallbacks
            );
        }

        private static Type ResolveType(string fullName)
        {
            Type direct = Type.GetType(fullName, throwOnError: false);
            if (direct != null)
            {
                return direct;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private Block ResolveSourceFluid(BlockEntityAqueduct aqueduct)
        {
            if (aqueduct.WaterSourcePos == null || aqueduct.Api == null)
            {
                return aqueduct.Api != null ? aqueduct.Api.World.BlockAccessor.GetBlock(aqueduct.Pos, BlockLayersAccess.Fluid) : null;
            }

            return aqueduct.Api.World.BlockAccessor.GetBlock(aqueduct.WaterSourcePos, BlockLayersAccess.Fluid);
        }

        private bool TryTraceOwnerControllerId(BlockEntityAqueduct startAqueduct, out string ownerControllerId, out string managedFamilyId)
        {
            ownerControllerId = string.Empty;
            managedFamilyId = string.Empty;
            if (startAqueduct.Api == null)
            {
                return false;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            BlockEntityAqueduct current = startAqueduct;
            for (int depth = 0; depth < MaxOwnerTraceDepth && current != null; depth++)
            {
                string currentKey = PosKey(current.Pos);
                if (!visited.Add(currentKey))
                {
                    return false;
                }

                BlockPos sourcePos = current.WaterSourcePos;
                if (sourcePos == null)
                {
                    return false;
                }

                if (current.Api.World.BlockAccessor.GetChunkAtBlockPos(sourcePos) == null)
                {
                    return false;
                }

                Block sourceFluid = current.Api.World.BlockAccessor.GetBlock(sourcePos, BlockLayersAccess.Fluid);
                if (TryResolveManagedFamily(sourceFluid, out string sourceFamily) &&
                    TryGetSourceOwner(sourcePos, out string ownerId))
                {
                    ownerControllerId = ownerId;
                    managedFamilyId = sourceFamily;
                    return true;
                }

                BlockEntityAqueduct sourceAqueduct = current.Api.World.BlockAccessor.GetBlockEntity(sourcePos) as BlockEntityAqueduct;
                if (sourceAqueduct == null || !sourceAqueduct.HasWaterSource)
                {
                    return false;
                }

                current = sourceAqueduct;
            }

            return false;
        }

        private bool TryResolveManagedFamily(Block fluid, out string familyId)
        {
            object[] args = { fluid, string.Empty };
            bool result = (bool)tryResolveManagedWaterFamily.Invoke(waterManager, args)!;
            familyId = result ? (string)args[1] : string.Empty;
            return result;
        }

        private bool TryResolveVanillaFamily(Block fluid, out string familyId)
        {
            object[] args = { fluid, string.Empty };
            bool result = (bool)tryResolveVanillaWaterFamily.Invoke(waterManager, args)!;
            familyId = result ? (string)args[1] : string.Empty;
            return result;
        }

        private Block ResolveManagedStillBlock(string familyId, int level)
        {
            object[] args = { familyId, "still", Math.Clamp(level, 1, 7) };
            return getManagedBlock.Invoke(waterManager, args) as Block;
        }

        private Block ResolveVanillaStillBlockFromFluid(Block fluid, int level)
        {
            if (TryResolveVanillaFamily(fluid, out string family))
            {
                return ResolveVanillaStillBlock(family, level);
            }

            return null;
        }

        private Block ResolveVanillaStillBlock(string familyId, int level)
        {
            AssetLocation code = new AssetLocation("game", $"{familyId}-still-{Math.Clamp(level, 1, 7)}");
            return api.World.GetBlock(code);
        }

        private bool AssignOwnedSourceForController(string ownerControllerId, BlockPos pos, string familyId)
        {
            object[] args = { ownerControllerId, pos, familyId };
            return (bool)assignOwnedSourceForController.Invoke(waterManager, args)!;
        }

        private bool TryGetSourceOwner(BlockPos pos, out string ownerId)
        {
            object[] args = { pos, string.Empty };
            bool ok = (bool)tryGetSourceOwner.Invoke(waterManager, args)!;
            ownerId = ok ? (string)args[1] : string.Empty;
            return ok;
        }

        private bool IsArchimedesSourceBlock(Block fluid)
        {
            object[] args = { fluid };
            return (bool)isArchimedesSourceBlock.Invoke(waterManager, args)!;
        }

        private bool IsArchimedesWaterBlock(Block fluid)
        {
            object[] args = { fluid };
            return (bool)isArchimedesWaterBlock.Invoke(waterManager, args)!;
        }

        private static string PosKey(BlockPos pos)
        {
            return $"{pos.X},{pos.Y},{pos.Z}";
        }
    }
}
