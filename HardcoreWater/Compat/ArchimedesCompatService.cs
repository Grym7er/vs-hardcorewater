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
        private const int OwnerResolutionCacheTtlMs = 30000;

        private readonly ICoreAPI api;
        private readonly object modSystem;
        private object waterManager;
        private MethodInfo tryResolveManagedWaterFamily;
        private MethodInfo tryResolveVanillaWaterFamily;
        private MethodInfo getManagedBlock;
        private MethodInfo assignOwnedSourceForController;
        private bool assignOwnedUsesSourceContext;
        private MethodInfo tryGetSourceOwner;
        private MethodInfo isArchimedesSourceBlock;
        private MethodInfo isArchimedesWaterBlock;

        private int managedRefillOverrides;
        private int ownerTraceResolved;
        private int ownerTraceFailed;
        private int outletOwnershipAssignments;
        private int unresolvedOwnerFallbacks;
        private readonly Dictionary<string, CachedOwnerResolution> ownerResolutionCache = new Dictionary<string, CachedOwnerResolution>(StringComparer.Ordinal);

        private ArchimedesCompatService(ICoreAPI api, object modSystem)
        {
            this.api = api;
            this.modSystem = modSystem;
        }

        public bool IsActive { get; private set; }

        public static ArchimedesCompatService Create(ICoreAPI api)
        {
            bool isPrimaryInstalled = api.ModLoader.IsModEnabled(ArchimedesModId);
            bool isLegacyInstalled = api.ModLoader.IsModEnabled(ArchimedesLegacyModId);
            if (!isPrimaryInstalled && !isLegacyInstalled)
            {
                api.Logger.Notification("[hardcorewaterforked] Archimedes compatibility inactive: mod not installed.");
                return null;
            }

            Type modSystemType = ResolveType(ArchimedesModSystemTypeName);
            if (modSystemType == null)
            {
                api.Logger.Warning("[hardcorewaterforked] Archimedes compatibility inactive: mod system type not found.");
                return null;
            }

            if (!TryResolveModSystem(api, modSystemType, out object modSystem, out string resolveReason))
            {
                api.Logger.Warning("[hardcorewaterforked] Archimedes compatibility inactive: {0}", resolveReason);
                return null;
            }

            var service = new ArchimedesCompatService(api, modSystem);
            if (!service.TryBindWaterManagerContract(out string bindReason))
            {
                api.Logger.Warning("[hardcorewaterforked] Archimedes compatibility inactive: {0}", bindReason);
                return null;
            }

            service.IsActive = true;
            api.Logger.Notification("[hardcorewaterforked] Archimedes compatibility active.");
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
                if (TryBindWaterManagerContract(out string reason))
                {
                    api.Logger.Notification("[hardcorewaterforked] Archimedes compatibility rebound after WaterManager refresh.");
                }
                else
                {
                    Deactivate($"failed to rebind WaterManager contract: {reason}");
                }
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
                if (HardcoreWaterConfig.Loaded.UnresolvedOwnerFallbackMode == UnresolvedOwnerFallbackMode.SkipRefill)
                {
                    return false;
                }

                // Preserve managed family even when owner tracing is transiently unavailable.
                // We intentionally skip ownership assignment in this path (ownerControllerId remains null).
                fillBlock = ResolveManagedStillBlock(sourceManagedFamily, waterLevel)
                    ?? ResolveVanillaStillBlock(sourceManagedFamily, waterLevel)
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

        public bool IsManagedSourceBlock(Block fluid)
        {
            if (!IsActive || fluid == null || fluid.BlockId == 0)
            {
                return false;
            }

            return IsArchimedesSourceBlock(fluid);
        }

        public void LogDebugSummaryIfNeeded()
        {
            if (!IsActive)
            {
                return;
            }

            // Low-noise one-line operational summary useful during soak tests.
            api.Logger.Debug(
                "[hardcorewaterforked] [compat/archimedes] counters managedRefillOverrides={0}, ownerTraceResolved={1}, ownerTraceFailed={2}, outletOwnershipAssignments={3}, unresolvedOwnerFallbacks={4}",
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
                    if (TryGetCachedOwnerResolution(sourcePos, out string cachedOwnerId, out string cachedFamilyId))
                    {
                        ownerControllerId = cachedOwnerId;
                        managedFamilyId = cachedFamilyId;
                        return true;
                    }
                    return false;
                }

                Block sourceFluid = current.Api.World.BlockAccessor.GetBlock(sourcePos, BlockLayersAccess.Fluid);
                if (TryResolveManagedFamily(sourceFluid, out string sourceFamily) &&
                    TryGetSourceOwner(sourcePos, out string ownerId))
                {
                    ownerControllerId = ownerId;
                    managedFamilyId = sourceFamily;
                    CacheOwnerResolution(sourcePos, ownerControllerId, managedFamilyId);
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
            bool result;
            try
            {
                result = (bool)tryResolveManagedWaterFamily.Invoke(waterManager, args)!;
            }
            catch (Exception ex)
            {
                Deactivate($"TryResolveManagedWaterFamily failed: {ex.GetType().Name}");
                familyId = string.Empty;
                return false;
            }
            familyId = result ? (string)args[1] : string.Empty;
            return result;
        }

        private bool TryResolveVanillaFamily(Block fluid, out string familyId)
        {
            object[] args = { fluid, string.Empty };
            bool result;
            try
            {
                result = (bool)tryResolveVanillaWaterFamily.Invoke(waterManager, args)!;
            }
            catch (Exception ex)
            {
                Deactivate($"TryResolveVanillaWaterFamily failed: {ex.GetType().Name}");
                familyId = string.Empty;
                return false;
            }
            familyId = result ? (string)args[1] : string.Empty;
            return result;
        }

        private Block ResolveManagedStillBlock(string familyId, int level)
        {
            object[] args = { familyId, "still", Math.Clamp(level, 1, 7) };
            try
            {
                return getManagedBlock.Invoke(waterManager, args) as Block;
            }
            catch (Exception ex)
            {
                Deactivate($"GetManagedBlock failed: {ex.GetType().Name}");
                return null;
            }
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
            object[] args = assignOwnedUsesSourceContext
                ? new object[] { ownerControllerId, pos, familyId, null, null }
                : new object[] { ownerControllerId, pos, familyId };
            try
            {
                return (bool)assignOwnedSourceForController.Invoke(waterManager, args)!;
            }
            catch (Exception ex)
            {
                Deactivate($"AssignOwnedSourceForController failed: {ex.GetType().Name}");
                return false;
            }
        }

        private bool TryGetSourceOwner(BlockPos pos, out string ownerId)
        {
            object[] args = { pos, string.Empty };
            bool ok;
            try
            {
                ok = (bool)tryGetSourceOwner.Invoke(waterManager, args)!;
            }
            catch (Exception ex)
            {
                Deactivate($"TryGetSourceOwner failed: {ex.GetType().Name}");
                ownerId = string.Empty;
                return false;
            }
            ownerId = ok ? (string)args[1] : string.Empty;
            return ok;
        }

        private bool IsArchimedesSourceBlock(Block fluid)
        {
            object[] args = { fluid };
            try
            {
                return (bool)isArchimedesSourceBlock.Invoke(waterManager, args)!;
            }
            catch (Exception ex)
            {
                Deactivate($"IsArchimedesSourceBlock failed: {ex.GetType().Name}");
                return false;
            }
        }

        private bool IsArchimedesWaterBlock(Block fluid)
        {
            object[] args = { fluid };
            try
            {
                return (bool)isArchimedesWaterBlock.Invoke(waterManager, args)!;
            }
            catch (Exception ex)
            {
                Deactivate($"IsArchimedesWaterBlock failed: {ex.GetType().Name}");
                return false;
            }
        }

        private static string PosKey(BlockPos pos)
        {
            return $"{pos.X},{pos.Y},{pos.Z}";
        }

        private void CacheOwnerResolution(BlockPos sourcePos, string ownerControllerId, string managedFamilyId)
        {
            if (string.IsNullOrEmpty(ownerControllerId) || string.IsNullOrEmpty(managedFamilyId))
            {
                return;
            }

            ownerResolutionCache[PosKey(sourcePos)] = new CachedOwnerResolution
            {
                OwnerControllerId = ownerControllerId,
                ManagedFamilyId = managedFamilyId,
                TimestampMs = api.World.ElapsedMilliseconds
            };
        }

        private bool TryGetCachedOwnerResolution(BlockPos sourcePos, out string ownerControllerId, out string managedFamilyId)
        {
            ownerControllerId = string.Empty;
            managedFamilyId = string.Empty;

            string key = PosKey(sourcePos);
            if (!ownerResolutionCache.TryGetValue(key, out CachedOwnerResolution cached))
            {
                return false;
            }

            if (api.World.ElapsedMilliseconds - cached.TimestampMs > OwnerResolutionCacheTtlMs)
            {
                ownerResolutionCache.Remove(key);
                return false;
            }

            ownerControllerId = cached.OwnerControllerId;
            managedFamilyId = cached.ManagedFamilyId;
            return true;
        }

        private void Deactivate(string reason)
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            api.Logger.Warning("[hardcorewaterforked] Archimedes compatibility disabled at runtime: {0}", reason);
        }

        private bool TryBindWaterManagerContract(out string reason)
        {
            reason = string.Empty;
            PropertyInfo waterManagerProp = modSystem.GetType().GetProperty("WaterManager", BindingFlags.Public | BindingFlags.Instance);
            if (waterManagerProp == null)
            {
                reason = "WaterManager property missing on Archimedes mod system";
                return false;
            }

            object manager = waterManagerProp.GetValue(modSystem);
            if (manager == null)
            {
                reason = "WaterManager unavailable";
                return false;
            }

            Type managerType = manager.GetType();
            MethodInfo tryResolveManaged = managerType.GetMethod("TryResolveManagedWaterFamily", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo tryResolveVanilla = managerType.GetMethod("TryResolveVanillaWaterFamily", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getManaged = managerType.GetMethod("GetManagedBlock", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo assignOwned = managerType.GetMethod("AssignOwnedSourceForController", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo tryGetOwner = managerType.GetMethod("TryGetSourceOwner", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo isSource = managerType.GetMethod("IsArchimedesSourceBlock", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo isManaged = managerType.GetMethod("IsArchimedesWaterBlock", BindingFlags.Public | BindingFlags.Instance);

            if (!HasExpectedSignature(tryResolveManaged, typeof(bool), typeof(Block), typeof(string).MakeByRefType()))
            {
                reason = "TryResolveManagedWaterFamily missing or incompatible";
                return false;
            }
            if (!HasExpectedSignature(tryResolveVanilla, typeof(bool), typeof(Block), typeof(string).MakeByRefType()))
            {
                reason = "TryResolveVanillaWaterFamily missing or incompatible";
                return false;
            }
            if (!HasExpectedSignature(getManaged, typeof(Block), typeof(string), typeof(string), typeof(int)))
            {
                reason = "GetManagedBlock missing or incompatible";
                return false;
            }
            bool assignOwnedLegacy = HasExpectedSignature(assignOwned, typeof(bool), typeof(string), typeof(BlockPos), typeof(string));
            bool assignOwnedWithContext = HasExpectedSignature(assignOwned, typeof(bool), typeof(string), typeof(BlockPos), typeof(string), typeof(BlockPos), typeof(BlockFacing));
            if (!assignOwnedLegacy && !assignOwnedWithContext)
            {
                reason = "AssignOwnedSourceForController missing or incompatible";
                return false;
            }
            if (!HasExpectedSignature(tryGetOwner, typeof(bool), typeof(BlockPos), typeof(string).MakeByRefType()))
            {
                reason = "TryGetSourceOwner missing or incompatible";
                return false;
            }
            if (!HasExpectedSignature(isSource, typeof(bool), typeof(Block)))
            {
                reason = "IsArchimedesSourceBlock missing or incompatible";
                return false;
            }
            if (!HasExpectedSignature(isManaged, typeof(bool), typeof(Block)))
            {
                reason = "IsArchimedesWaterBlock missing or incompatible";
                return false;
            }

            waterManager = manager;
            tryResolveManagedWaterFamily = tryResolveManaged;
            tryResolveVanillaWaterFamily = tryResolveVanilla;
            getManagedBlock = getManaged;
            assignOwnedSourceForController = assignOwned;
            assignOwnedUsesSourceContext = assignOwnedWithContext;
            tryGetSourceOwner = tryGetOwner;
            isArchimedesSourceBlock = isSource;
            isArchimedesWaterBlock = isManaged;
            return true;
        }

        private static bool HasExpectedSignature(MethodInfo method, Type returnType, params Type[] parameterTypes)
        {
            if (method == null || method.ReturnType != returnType)
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != parameterTypes.Length)
            {
                return false;
            }

            for (int i = 0; i < parameterTypes.Length; i++)
            {
                if (parameters[i].ParameterType != parameterTypes[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryResolveModSystem(ICoreAPI api, Type modSystemType, out object modSystem, out string reason)
        {
            modSystem = null;
            reason = string.Empty;

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
                    modSystem = getModSystemByString.Invoke(api.ModLoader, new object[] { ArchimedesModId })
                        ?? getModSystemByString.Invoke(api.ModLoader, new object[] { ArchimedesLegacyModId })
                        ?? getModSystemByString.Invoke(api.ModLoader, new object[] { ArchimedesModSystemTypeName });
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
                else
                {
                    reason = "no compatible ModLoader GetModSystem API found";
                    return false;
                }
            }
            catch (Exception ex)
            {
                reason = $"mod system lookup failed: {ex.GetType().Name}";
                return false;
            }

            if (modSystem == null)
            {
                reason = "mod system instance not found";
                return false;
            }

            return true;
        }

        private sealed class CachedOwnerResolution
        {
            public string OwnerControllerId { get; set; } = string.Empty;
            public string ManagedFamilyId { get; set; } = string.Empty;
            public long TimestampMs { get; set; }
        }
    }
}
