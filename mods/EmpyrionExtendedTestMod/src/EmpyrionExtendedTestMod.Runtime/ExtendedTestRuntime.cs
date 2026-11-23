using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Eleon.Modding;
using HarmonyLib;

namespace EmpyrionExtendedTestMod.Runtime
{
    /// <summary>
    /// Test runtime: client speed patch on Disk.SaveView(), a runtime ECF merge
    /// for custom blocks, and a server-authoritative InfiniteFuelGenerator loop.
    /// </summary>
    public sealed class ExtendedTestRuntime : IExtendedTestRuntime
    {
        private const string ExpectedClientMvid = "462483cd-2f1f-4a24-9c83-46d24b1cb3f4";
        private const string ExpectedDediMvid = "fae3f936-680a-459b-99d0-6fe468a1f17f";

        private Harmony harmony;
        private MethodInfo target;
        private SpeedPatchContext context;
        private InfiniteFuelGeneratorService generatorService;
        private bool disposed;

        public void Start(IModApi api)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));

            Assembly game = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal));
            if (game == null)
                throw new InvalidOperationException("Assembly-CSharp is not loaded");

            Module module = game.ManifestModule;
            string mvid = module.ModuleVersionId.ToString("D");
            bool knownClient = string.Equals(mvid, ExpectedClientMvid, StringComparison.OrdinalIgnoreCase);
            bool knownDedi = string.Equals(mvid, ExpectedDediMvid, StringComparison.OrdinalIgnoreCase);
            if (!knownClient && !knownDedi)
                api.Log("[ExtendedTestMod] Warning: Assembly-CSharp MVID differs from the known client/dedi baselines: " + mvid);

            ApplicationMode mode = api.Application == null ? ApplicationMode.Client : api.Application.Mode;
            api.Log("[ExtendedTestMod] Runtime mode=" + mode + " assemblyMvid=" + mvid);

            harmony = new Harmony("EmpyrionExtendedTestMod.runtime");

            // The speed feature is only meaningful in the client process. A
            // playfield server must not install client movement/UI patches.
            if (mode == ApplicationMode.Client || mode == ApplicationMode.SinglePlayer)
                TryInstallSpeedPatch(game, module, knownClient, api);
            else
                api.Log("[ExtendedTestMod] Client speed patch skipped in mode " + mode + ".");

            // Add the custom devices by merging ECF fragments into the game's
            // in-memory block collection. Content/Mods DLL mods cannot provide
            // a BlocksConfig.ecf through the normal mod loader.
            InstallBlockConfigMerge(game, api);

            // Container/inventory changes are server-authoritative. Run this only
            // in the playfield-owning process, never on a remote multiplayer client.
            generatorService = new InfiniteFuelGeneratorService(api);
            generatorService.Start();
        }

        private void TryInstallSpeedPatch(Assembly game, Module module, bool knownImage, IModApi api)
        {
            try
            {
                Type diskType = game.GetType("Assembly-CSharp.Disk", false);
                if (diskType == null)
                {
                    api.Log("[ExtendedTestMod] Disk not found; client speed patch disabled.");
                    return;
                }

                if (knownImage)
                {
                    try { target = module.ResolveMethod(0x06004882) as MethodInfo; }
                    catch { target = null; }
                }

                if (!IsTarget(target, diskType))
                {
                    target = diskType
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(m => m.Name == "SaveView" && m.ReturnType == typeof(float) && m.GetParameters().Length == 0)
                        .SingleOrDefault();
                }

                if (!IsTarget(target, diskType))
                {
                    api.Log("[ExtendedTestMod] Disk.SaveView() float target not found; client speed patch disabled.");
                    return;
                }

                context = SpeedPatchContext.Create(game, diskType, api);

                // Install the speed postfix first. If this fails the speed feature fails closed.
                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(typeof(SpeedPatch).GetMethod(nameof(SpeedPatch.Postfix), BindingFlags.Static | BindingFlags.Public)));

                SpeedPatch.Context = context;
                api.Log("[ExtendedTestMod] Harmony postfix installed: " + target.DeclaringType.FullName + "." + target.Name +
                        "() MVID=" + module.ModuleVersionId + " harmony=" + typeof(Harmony).Assembly.GetName().Version);
            }
            catch (Exception error)
            {
                // Do not disable the server-side devices just because the client speed target moved.
                SpeedPatch.Context = null;
                context = null;
                target = null;
                api.Log("[ExtendedTestMod] Client speed patch disabled after error: " +
                        error.GetType().Name + ": " + error.Message);
            }
        }

        private void InstallBlockConfigMerge(Assembly game, IModApi api)
        {
            try
            {
                string runtimeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string modRoot = runtimeFolder == null ? null : Path.GetDirectoryName(runtimeFolder);
                string configurationFolder = modRoot == null
                    ? null
                    : Path.Combine(modRoot, "Configuration");

                if (configurationFolder == null || !Directory.Exists(configurationFolder))
                {
                    api.Log("[ExtendedTestMod] Configuration folder missing; custom devices will not be added: " + configurationFolder);
                    return;
                }

                string[] fragments = Directory.GetFiles(configurationFolder, "*.ecf");
                if (fragments.Length == 0)
                {
                    api.Log("[ExtendedTestMod] No ECF fragments found; custom devices will not be added.");
                    return;
                }

                Type collectionType = game.GetType("DynamicPropertiesCollection", false);
                if (collectionType == null)
                {
                    api.Log("[ExtendedTestMod] DynamicPropertiesCollection not found; device config merge disabled.");
                    return;
                }

                if (!BlockConfigMerger.Configure(collectionType, api, fragments))
                {
                    api.Log("[ExtendedTestMod] Device config merger could not resolve the ECF collection shape; custom devices disabled.");
                    return;
                }

                MethodInfo postfix = typeof(BlockConfigMergePatch)
                    .GetMethod(nameof(BlockConfigMergePatch.Postfix), BindingFlags.Static | BindingFlags.Public);

                int patched = 0;
                foreach (MethodInfo method in collectionType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name != "ReadEcf" && method.Name != "LoadBinary" && method.Name != "Read")
                        continue;

                    try
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                        patched++;
                        api.Log("[ExtendedTestMod] Config merge hook: " + method.Name +
                                "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")");
                    }
                    catch (Exception error)
                    {
                        api.Log("[ExtendedTestMod] Config merge hook failed for " + method.Name + ": " +
                                error.GetType().Name + ": " + error.Message);
                    }
                }

                api.Log("[ExtendedTestMod] Device config merge installed on " + patched + " collection loader(s) for " +
                        fragments.Length + " fragment(s).");
            }
            catch (Exception error)
            {
                // The speed experiment must continue even if the optional devices cannot be added.
                api.Log("[ExtendedTestMod] Device config merge disabled after error: " +
                        error.GetType().Name + ": " + error.Message);
            }
        }

        private static bool IsTarget(MethodInfo method, Type diskType)
        {
            return method != null &&
                   method.DeclaringType == diskType &&
                   method.Name == "SaveView" &&
                   !method.IsStatic &&
                   method.ReturnType == typeof(float) &&
                   method.GetParameters().Length == 0;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SpeedPatch.Context = null;

            try
            {
                if (generatorService != null)
                    generatorService.Dispose();
            }
            catch
            {
                // Shutdown must not throw into the game.
            }

            try
            {
                if (harmony != null)
                    harmony.UnpatchAll(harmony.Id);
            }
            catch
            {
                // Shutdown must not throw into the game.
            }
            finally
            {
                context = null;
                target = null;
                generatorService = null;
                harmony = null;
            }
        }
    }

    public static class SpeedPatch
    {
        internal static SpeedPatchContext Context;

        public static void Postfix(object __instance, ref float __result)
        {
            SpeedPatchContext context = Context;
            if (context == null) return;

            try
            {
                context.Apply(__instance, ref __result);
            }
            catch (Exception error)
            {
                context.FailOnce(error);
            }
        }
    }

    public static class BlockConfigMergePatch
    {
        public static void Postfix(object __instance)
        {
            try
            {
                BlockConfigMerger.TryMerge(__instance);
            }
            catch
            {
                // Never let an optional config merge break a game config load.
            }
        }
    }

    internal sealed class SpeedPatchContext
    {
        private const int EntityTypeCv = 3; // EntityType.CV
        private const string BoosterBlockName = "GigaSpeedBooster";
        private const int BoosterBlockId = 1565;
        private const float BaseCvSpeed = 300f;
        private const float BoosterCvSpeed = 600f;
        private const int DetectionCacheMs = 1000;

        private readonly IModApi api;
        private readonly Type diskType;
        private readonly Type actionListType;
        private readonly Type deviceCollectionType;

        private readonly FieldInfo diskEmulatorField;
        private readonly FieldInfo entityTypeField;
        private readonly FieldInfo worldField;
        private readonly MethodInfo closeConfigMethod;

        private string lastDetection;
        private string lastApplied;
        private bool failureLogged;
        private bool boosterFailureLogged;
        private int lastProbeStructureId = int.MinValue;

        private int cachedStructureId = int.MinValue;
        private int cachedAtTicks;
        private bool cachedHasBooster;
        private string cachedBoosterDetail;
        private int boosterBlockId = int.MinValue;
        private int boosterMappingTicks;
        private bool boosterMappingMismatchLogged;

        private SpeedPatchContext(IModApi api, Type diskType, Type actionListType, Type deviceCollectionType,
            FieldInfo diskEmulatorField, FieldInfo entityTypeField, FieldInfo worldField, MethodInfo closeConfigMethod)
        {
            this.api = api;
            this.diskType = diskType;
            this.actionListType = actionListType;
            this.deviceCollectionType = deviceCollectionType;
            this.diskEmulatorField = diskEmulatorField;
            this.entityTypeField = entityTypeField;
            this.worldField = worldField;
            this.closeConfigMethod = closeConfigMethod;
        }

        internal static SpeedPatchContext Create(Assembly game, Type diskType, IModApi api)
        {
            Type emulatorType = game.GetType("Assembly-CSharp.EmulatorService", false);
            Type actionListType = game.GetType("Assembly-CSharp.ActionList", false);
            Type deviceCollectionType = game.GetType("Assembly-CSharp.DeviceCollection", false);
            if (emulatorType == null || actionListType == null || deviceCollectionType == null)
                throw new InvalidOperationException("vessel types missing");

            FieldInfo diskEmulatorField = diskType
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(f => f.FieldType == emulatorType);
            if (diskEmulatorField == null) throw new InvalidOperationException("Disk emulator field missing");

            FieldInfo entityTypeField = FindFieldInHierarchy(emulatorType, f => TypeNameIs(f.FieldType, "EntityType"));
            if (entityTypeField == null) throw new InvalidOperationException("EntityType field missing");

            FieldInfo worldField = FindFieldInHierarchy(emulatorType, f => TypeNameIs(f.FieldType, "World"));
            if (worldField == null) throw new InvalidOperationException("World field missing");

            MethodInfo closeConfigMethod = worldField.FieldType.GetMethod(
                "get_CloseConfig",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (closeConfigMethod == null || closeConfigMethod.ReturnType != typeof(bool))
                throw new InvalidOperationException("World.get_CloseConfig() missing");

            return new SpeedPatchContext(api, diskType, actionListType, deviceCollectionType,
                diskEmulatorField, entityTypeField, worldField, closeConfigMethod);
        }

        private static bool TypeNameIs(Type type, string name)
        {
            return type != null && string.Equals(type.Name, name, StringComparison.Ordinal);
        }

        private static FieldInfo FindFieldInHierarchy(Type type, Func<FieldInfo, bool> predicate)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(predicate);
                if (field != null) return field;
            }
            return null;
        }

        internal void Apply(object disk, ref float result)
        {
            if (disk == null || disk.GetType() != diskType) return;

            object emulator = diskEmulatorField.GetValue(disk);
            if (emulator == null) return;

            object typeValue = entityTypeField.GetValue(emulator);
            if (typeValue == null || Convert.ToInt32(typeValue) != EntityTypeCv) return;

            if (deviceCollectionType.IsInstanceOfType(emulator)) return;

            object world = worldField.GetValue(emulator);
            if (world == null) return;

            bool isSpace = (bool)closeConfigMethod.Invoke(world, null);

            string boosterDetail;
            bool hasBooster = TryGetBoosterState(out boosterDetail);
            float target = hasBooster ? BoosterCvSpeed : BaseCvSpeed;

            string detection = emulator.GetType().Name + "|space=" + isSpace +
                               "|booster=" + hasBooster + "|why=" + boosterDetail +
                               "|current=" + result.ToString("0.##") + "|target=" + target.ToString("0.##");
            if (!string.Equals(detection, lastDetection, StringComparison.Ordinal))
            {
                lastDetection = detection;
                api.Log("[ExtendedTestMod] CV SaveView: class=" + emulator.GetType().Name +
                        " space=" + isSpace + " booster=" + hasBooster + " (" + boosterDetail + ")" +
                        " current=" + result.ToString("0.##") + " target=" + target.ToString("0.##"));
            }

            if (result > 0f && target > result + 0.01f)
            {
                result = target;
                string applied = detection + "|applied=" + target.ToString("0.##");
                if (!string.Equals(applied, lastApplied, StringComparison.Ordinal))
                {
                    lastApplied = applied;
                    api.Log("[ExtendedTestMod] Applied CV SaveView target " + target.ToString("0.##") +
                            " in " + (isSpace ? "space" : "atmosphere") +
                            " (GigaSpeedBooster=" + hasBooster + ").");
                }
            }
        }

        private bool TryGetBoosterState(out string detail)
        {
            detail = "unavailable";
            try
            {
                IApplication application = api.Application;
                if (application == null)
                {
                    detail = "no-application";
                    return false;
                }

                IPlayer player = application.LocalPlayer;
                if (player == null)
                {
                    detail = "no-local-player";
                    return false;
                }

                IStructure structure = player.CurrentStructure;
                if (structure == null)
                {
                    detail = "no-current-structure";
                    return false;
                }

                int now = Environment.TickCount;
                if (structure.Id == cachedStructureId &&
                    unchecked(now - cachedAtTicks) >= 0 &&
                    unchecked(now - cachedAtTicks) < DetectionCacheMs)
                {
                    detail = cachedBoosterDetail;
                    return cachedHasBooster;
                }

                bool has = false;
                string evidence = "not-found";

                try
                {
                    IDevice device = structure.GetDevice<IDevice>(BoosterBlockName);
                    if (device != null)
                    {
                        has = true;
                        evidence = "GetDevice(name)";
                    }
                }
                catch (Exception error)
                {
                    evidence = "GetDevice-error:" + error.GetType().Name;
                }

                if (!has)
                {
                    try
                    {
                        IDevicePosList lights = structure.GetDevices(DeviceTypeName.Light);
                        int id = GetBoosterBlockId();
                        if (lights != null && id >= 0)
                        {
                            for (int i = 0; i < lights.Count && !has; i++)
                            {
                                IBlock block = structure.GetBlock(lights.GetAt(i));
                                if (block == null) continue;

                                int type;
                                int shape;
                                int rotation;
                                bool active;
                                block.Get(out type, out shape, out rotation, out active);
                                if (type == id)
                                {
                                    has = true;
                                    evidence = "Light-device-block-id=" + id;
                                }
                            }

                            if (!has && lights.Count == 0)
                                evidence = "no-light-devices";
                        }
                        else if (id < 0)
                        {
                            evidence = "block-id-unmapped";
                        }
                    }
                    catch (Exception error)
                    {
                        evidence += "|GetDevices-error:" + error.GetType().Name;
                    }
                }

                if (!has)
                {
                    try
                    {
                        List<VectorInt3> positions = structure.GetDevicePositions(BoosterBlockName);
                        if (positions != null && positions.Count > 0)
                        {
                            has = true;
                            evidence = "GetDevicePositions";
                        }
                    }
                    catch (Exception error)
                    {
                        evidence += "|GetDevicePositions-error:" + error.GetType().Name;
                    }
                }

                detail = "structure=" + structure.Id + ",blocks=" + structure.BlockCount +
                         ",devices=" + structure.DeviceCount + "," + evidence;

                if (structure.Id != lastProbeStructureId)
                {
                    lastProbeStructureId = structure.Id;
                    api.Log("[ExtendedTestMod] Booster probe: " + detail +
                            ", blockMapping=" + GetBoosterBlockId());
                }

                cachedStructureId = structure.Id;
                cachedAtTicks = now;
                cachedHasBooster = has;
                cachedBoosterDetail = detail;
                return has;
            }
            catch (Exception error)
            {
                if (!boosterFailureLogged)
                {
                    boosterFailureLogged = true;
                    try
                    {
                        api.Log("[ExtendedTestMod] Booster detection failed, using base CV limit: " +
                                error.GetType().Name + ": " + error.Message);
                    }
                    catch
                    {
                        // Never let diagnostics break the game callback.
                    }
                }
                return false;
            }
        }

        private int GetBoosterBlockId()
        {
            if (boosterBlockId >= 0) return boosterBlockId;

            int now = Environment.TickCount;
            if (boosterBlockId == -1 && unchecked(now - boosterMappingTicks) >= 0 &&
                unchecked(now - boosterMappingTicks) < 5000)
                return -1;

            boosterMappingTicks = now;
            try
            {
                Dictionary<string, int> mapping = api.Application.GetBlockAndItemMapping();
                int id;
                if (mapping != null && mapping.TryGetValue(BoosterBlockName, out id))
                {
                    boosterBlockId = id;
                    if (id != BoosterBlockId && !boosterMappingMismatchLogged)
                    {
                        boosterMappingMismatchLogged = true;
                        api.Log("[ExtendedTestMod] Warning: GigaSpeedBooster mapping is " + id +
                                ", expected fixed ID " + BoosterBlockId + ".");
                    }
                }
                else
                {
                    // The ECF entry reserves this fixed ID. If the public mapping is
                    // not ready yet, the constant is still the correct fallback.
                    boosterBlockId = BoosterBlockId;
                }
            }
            catch
            {
                boosterBlockId = BoosterBlockId;
            }

            return boosterBlockId;
        }

        internal void FailOnce(Exception error)
        {
            if (failureLogged) return;
            failureLogged = true;
            try
            {
                api.Log("[ExtendedTestMod] Postfix skipped after error: " + error.GetType().Name + ": " + error.Message);
            }
            catch
            {
                // Never let diagnostics break the game callback.
            }
        }
    }

    /// <summary>
    /// Server-authoritative prototype: every 10 seconds, add one FusionCell
    /// ("Promethium Hydrocell" in Reforged Eden 2) to each InfiniteFuelGenerator
    /// container on a powered CV/SV/HV in the current playfield.
    /// </summary>
    internal sealed class InfiniteFuelGeneratorService : IDisposable
    {
        // ItemsConfig.ecf says FusionCell has Item Id 325, but
        // IContainer.AddItems indexes the AddinEditor registry directly.
        // Blocks occupy the low registry range; items are registered at
        // 8192 + itemId by ImageManager.JoinDrive. 325 therefore selected
        // block 325 (TurretMSFlakRetract) in RE2. Use the item registry index.
        private const int FusionCellRegistryId = 8192 + 325;
        private const string FusionCellName = "FusionCell";
        private const string BlockName = "InfiniteFuelGenerator";
        private const int BlockId = 1564;
        private const double IntervalSeconds = 10.0;

        private readonly IModApi api;
        private readonly Stopwatch timer = new Stopwatch();
        private readonly HashSet<int> loggedStructureIds = new HashSet<int>();
        private readonly HashSet<int> loggedFullStructureIds = new HashSet<int>();
        private IPlayfield playfield;
        private bool subscribed;
        private bool disposed;
        private bool active;
        private bool inactiveLogged;
        private int noPlayfieldLogs;
        private int fusionCellItemId = FusionCellRegistryId;
        private bool fusionCellIdResolved;

        internal InfiniteFuelGeneratorService(IModApi api)
        {
            this.api = api;
        }

        internal void Start()
        {
            IApplication application = api.Application;
            if (application == null)
            {
                api.Log("[ExtendedTestMod] InfiniteFuelGenerator disabled: no application.");
                return;
            }

            ApplicationMode initialMode = application.Mode;
            if (initialMode == ApplicationMode.DedicatedServer)
            {
                api.Log("[ExtendedTestMod] InfiniteFuelGenerator inactive in DedicatedServer master mode: " +
                        "this assembly has no IPlayfield/IStructure implementation. Install/run the PfServer target.");
                return;
            }

            // Do not decide here. During mod Init the game can still report Client
            // even when a single-player save is being loaded. Re-check the mode on
            // Update and activate once it becomes server-authoritative.
            playfield = api.ClientPlayfield;
            application.OnPlayfieldLoaded += OnPlayfieldLoaded;
            application.OnPlayfieldUnloading += OnPlayfieldUnloading;
            application.Update += OnUpdate;
            application.FixedUpdate += OnUpdate;
            subscribed = true;
            timer.Start();

            api.Log("[ExtendedTestMod] InfiniteFuelGenerator armed; initial mode=" + initialMode +
                    ". It will activate when mode becomes SinglePlayer or PlayfieldServer" +
                    " (item=FusionCell registryId=" + FusionCellRegistryId +
                    ", interval=" + IntervalSeconds + "s, blockId=" + BlockId + ").");
        }

        private void OnPlayfieldLoaded(IPlayfield loaded)
        {
            playfield = loaded;
        }

        private void OnPlayfieldUnloading(IPlayfield unloading)
        {
            if (ReferenceEquals(playfield, unloading))
                playfield = null;
        }

        private void OnUpdate()
        {
            if (disposed) return;

            IApplication application = api.Application;
            ApplicationMode mode = application == null ? ApplicationMode.Client : application.Mode;

            if (mode == ApplicationMode.SinglePlayer || mode == ApplicationMode.PlayfieldServer)
            {
                if (!active)
                {
                    active = true;
                    inactiveLogged = false;
                    timer.Restart();
                    api.Log("[ExtendedTestMod] InfiniteFuelGenerator active in mode " + mode + ".");
                }
            }
            else
            {
                if (active)
                {
                    active = false;
                    api.Log("[ExtendedTestMod] InfiniteFuelGenerator paused because mode is " + mode + ".");
                }
                else if (!inactiveLogged)
                {
                    inactiveLogged = true;
                    api.Log("[ExtendedTestMod] InfiniteFuelGenerator waiting for server-authoritative mode; current mode=" + mode + ".");
                }
                return;
            }

            if (timer.Elapsed.TotalSeconds < IntervalSeconds) return;
            timer.Restart();

            try
            {
                GenerateOnce();
            }
            catch (Exception error)
            {
                api.Log("[ExtendedTestMod] InfiniteFuelGenerator cycle skipped after error: " +
                        error.GetType().Name + ": " + error.Message);
            }
        }

        private void ResolveFusionCellItemId()
        {
            if (fusionCellIdResolved) return;
            fusionCellIdResolved = true;

            try
            {
                Dictionary<string, int> mapping = api.Application == null
                    ? null
                    : api.Application.GetBlockAndItemMapping();
                int mapped;
                if (mapping != null && mapping.TryGetValue(FusionCellName, out mapped))
                {
                    // The item half of the registry is 8192+. If the mapping
                    // ever returns a low block-range value, do not repeat the
                    // turret mistake; keep the verified item-registry fallback.
                    if (mapped >= 8192)
                    {
                        fusionCellItemId = mapped;
                        api.Log("[ExtendedTestMod] FusionCell registry id resolved from mapping: " + mapped + ".");
                        return;
                    }

                    api.Log("[ExtendedTestMod] Warning: FusionCell mapping returned low registry id " + mapped +
                            "; using item-registry fallback " + FusionCellRegistryId + ".");
                    return;
                }

                api.Log("[ExtendedTestMod] FusionCell not in block/item mapping; using item-registry fallback " +
                        FusionCellRegistryId + ".");
            }
            catch (Exception error)
            {
                api.Log("[ExtendedTestMod] FusionCell mapping lookup failed; using fallback " +
                        FusionCellRegistryId + ": " + error.GetType().Name + ": " + error.Message);
            }
        }

        private void GenerateOnce()
        {
            ResolveFusionCellItemId();

            IPlayfield current = playfield ?? api.ClientPlayfield;
            if (current == null)
            {
                if (noPlayfieldLogs++ % 6 == 0)
                    api.Log("[ExtendedTestMod] InfiniteFuelGenerator: no playfield available yet.");
                return;
            }

            Dictionary<int, IEntity> entities = current.Entities;
            if (entities == null || entities.Count == 0) return;

            foreach (IEntity entity in entities.Values)
            {
                if (entity == null) continue;
                if (entity.Type != EntityType.CV && entity.Type != EntityType.SV && entity.Type != EntityType.HV)
                    continue;

                IStructure structure = entity.Structure;
                if (structure == null || !structure.IsReady || !structure.IsPowered) continue;

                List<IContainer> generators = FindGenerators(structure);
                if (generators.Count == 0) continue;

                foreach (IContainer container in generators)
                {
                    int remaining = container.AddItems(fusionCellItemId, 1);
                    if (remaining == 0)
                    {
                        if (loggedStructureIds.Add(structure.Id))
                        {
                            api.Log("[ExtendedTestMod] InfiniteFuelGenerator produced 1 FusionCell on structure " +
                                    structure.Id + " (" + entity.Type + ").");
                        }
                    }
                    else if (loggedFullStructureIds.Add(structure.Id))
                    {
                        api.Log("[ExtendedTestMod] InfiniteFuelGenerator container is full or rejected the item on structure " +
                                structure.Id + " (remaining=" + remaining + ").");
                    }
                }
            }
        }

        private static List<IContainer> FindGenerators(IStructure structure)
        {
            List<IContainer> result = new List<IContainer>();

            try
            {
                IDevicePosList containers = structure.GetDevices(DeviceTypeName.Container);
                if (containers != null)
                {
                    for (int i = 0; i < containers.Count; i++)
                    {
                        VectorInt3 pos = containers.GetAt(i);
                        IBlock block = structure.GetBlock(pos);
                        if (block == null) continue;

                        int type;
                        int shape;
                        int rotation;
                        bool active;
                        block.Get(out type, out shape, out rotation, out active);
                        if (type != BlockId) continue;

                        IContainer container = structure.GetDevice<IContainer>(pos);
                        if (container != null) result.Add(container);
                    }
                }
            }
            catch
            {
                // Fall back to the name lookup below.
            }

            if (result.Count == 0)
            {
                try
                {
                    IContainer container = structure.GetDevice<IContainer>(BlockName);
                    if (container != null) result.Add(container);
                }
                catch
                {
                    // No public container lookup available; fail closed for this structure.
                }
            }

            return result;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer.Stop();

            if (!subscribed) return;
            subscribed = false;

            try
            {
                IApplication application = api.Application;
                if (application != null)
                {
                    application.OnPlayfieldLoaded -= OnPlayfieldLoaded;
                    application.OnPlayfieldUnloading -= OnPlayfieldUnloading;
                    application.Update -= OnUpdate;
                    application.FixedUpdate -= OnUpdate;
                }
            }
            catch
            {
                // Shutdown must not throw into the game.
            }
        }
    }

    internal static class BlockConfigMerger
    {
        private const string RefName = "LightMS01";
        private const string AirName = "Air";

        // Explicit base-block mapping for our fragments. The ECF parser does not
        // populate Element.Reference for these parsed fragments on this build, so
        // do not depend on Ref being available as a runtime field.
        private static readonly Dictionary<string, string> BaseBlocks =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "GigaSpeedBooster", "LightMS01" },
                { "InfiniteFuelGenerator", "ContainerMS01" }
            };

        private static Type collectionType;
        private static Type elementType;
        private static FieldInfo elementsField;
        private static FieldInfo nameField;
        private static FieldInfo propertiesField;
        private static FieldInfo referenceField;
        private static MethodInfo readEcfMethod;
        private static MethodInfo applyReferencesMethod;
        private static MethodInfo copyFromMethod;
        private static IModApi api;
        private static string[] fragmentPaths = new string[0];
        private static bool failureLogged;
        private static readonly HashSet<string> injectedNames = new HashSet<string>(StringComparer.Ordinal);

        internal static bool Configure(Type type, IModApi modApi, string[] paths)
        {
            collectionType = type;
            api = modApi;
            fragmentPaths = paths ?? new string[0];

            elementType = collectionType.GetNestedType("Element", BindingFlags.Public | BindingFlags.NonPublic);
            elementsField = collectionType.GetField("Elements", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            nameField = elementType == null
                ? null
                : elementType.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            propertiesField = elementType == null
                ? null
                : elementType.GetField("properties", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            referenceField = elementType == null
                ? null
                : elementType.GetField("Reference", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            readEcfMethod = collectionType.GetMethod(
                "ReadEcf",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string) },
                null);

            applyReferencesMethod = collectionType.GetMethod(
                "ApplyAllReferences",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            if (propertiesField != null)
            {
                copyFromMethod = propertiesField.FieldType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "CopyFrom" && m.GetParameters().Length == 3);
            }

            return elementType != null && elementsField != null && nameField != null &&
                   propertiesField != null && referenceField != null && readEcfMethod != null;
        }

        internal static void TryMerge(object collection)
        {
            if (collection == null || collectionType == null || !collectionType.IsInstanceOfType(collection))
                return;

            try
            {
                IList elements = elementsField.GetValue(collection) as IList;
                if (elements == null) return;

                // Only merge into a block collection. This avoids injecting the
                // blocks into unrelated ECF collections such as items or dialogues.
                if (!ContainsName(elements, RefName) || !ContainsName(elements, AirName)) return;

                // Make sure existing reference-based blocks have their inherited
                // properties before we clone a base element below.
                if (applyReferencesMethod != null)
                {
                    try { applyReferencesMethod.Invoke(collection, null); }
                    catch (TargetInvocationException error)
                    {
                        LogOnce("pre-merge ApplyAllReferences failed: " + error.InnerException);
                    }
                }

                int added = 0;
                foreach (string fragmentPath in fragmentPaths)
                {
                    object fragment;
                    try { fragment = Activator.CreateInstance(collectionType); }
                    catch (Exception error)
                    {
                        LogOnce("fragment allocation failed: " + error.GetType().Name + ": " + error.Message);
                        continue;
                    }

                    bool read;
                    try
                    {
                        read = (bool)readEcfMethod.Invoke(fragment, new object[] { fragmentPath, "Block" });
                    }
                    catch (TargetInvocationException error)
                    {
                        LogOnce("fragment parse failed for " + Path.GetFileName(fragmentPath) + ": " + error.InnerException);
                        continue;
                    }

                    if (!read)
                    {
                        LogOnce("fragment parse returned false: " + fragmentPath);
                        continue;
                    }

                    IList fragmentElements = elementsField.GetValue(fragment) as IList;
                    if (fragmentElements == null) continue;

                    foreach (object element in fragmentElements)
                    {
                        string name = nameField.GetValue(element) as string;
                        if (string.IsNullOrEmpty(name) || ContainsName(elements, name)) continue;

                        // Do not rely on the game applying the fragment's Ref later.
                        // Block validation happens after this merge, so copy the base
                        // block's properties now. Otherwise the game reports e.g.
                        // "Block 1565 has missing/empty material".
                        string baseName = ResolveBaseName(element);
                        if (!CopyBaseProperties(element, elements, baseName)) continue;

                        elements.Add(element);
                        added++;

                        if (injectedNames.Add(name))
                        {
                            api.Log("[ExtendedTestMod] Injected block '" + name +
                                    "' into BlocksConfig from " + Path.GetFileName(fragmentPath) +
                                    " (base=" + baseName + ").");
                        }
                    }
                }

                if (added == 0) return;

                if (applyReferencesMethod != null)
                {
                    try { applyReferencesMethod.Invoke(collection, null); }
                    catch (TargetInvocationException error)
                    {
                        LogOnce("ApplyAllReferences failed: " + error.InnerException);
                    }
                }
            }
            catch (Exception error)
            {
                LogOnce("merge failed: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private static string ResolveBaseName(object element)
        {
            string name = nameField.GetValue(element) as string;
            if (!string.IsNullOrEmpty(name))
            {
                string mapped;
                if (BaseBlocks.TryGetValue(name, out mapped))
                    return mapped;
            }

            return referenceField.GetValue(element) as string;
        }

        private static bool CopyBaseProperties(object element, IList elements, string reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                LogOnce("an injected block has no known base; skipped it to avoid an invalid block definition.");
                return false;
            }

            object baseElement = null;
            foreach (object candidate in elements)
            {
                if (string.Equals(nameField.GetValue(candidate) as string, reference, StringComparison.Ordinal))
                {
                    baseElement = candidate;
                    break;
                }
            }

            if (baseElement == null)
            {
                LogOnce("base block '" + reference + "' not found; skipped an injected block.");
                return false;
            }

            if (copyFromMethod == null)
            {
                LogOnce("DynamicProperties.CopyFrom not found; skipped an injected block.");
                return false;
            }

            object targetProperties = propertiesField.GetValue(element);
            object baseProperties = propertiesField.GetValue(baseElement);
            if (targetProperties == null || baseProperties == null)
            {
                LogOnce("block properties missing while copying base '" + reference + "'.");
                return false;
            }

            try
            {
                copyFromMethod.Invoke(targetProperties, new object[] { baseProperties, null, false });
                return true;
            }
            catch (TargetInvocationException error)
            {
                LogOnce("CopyFrom failed for base '" + reference + "': " + error.InnerException);
                return false;
            }
        }

        private static bool ContainsName(IList elements, string name)
        {
            foreach (object element in elements)
            {
                if (string.Equals(nameField.GetValue(element) as string, name, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void LogOnce(string text)
        {
            if (failureLogged) return;
            failureLogged = true;
            try
            {
                api.Log("[ExtendedTestMod] Device config merge: " + text);
            }
            catch
            {
                // Never let diagnostics break a game config load.
            }
        }
    }
}
