using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Eleon.Modding;
using HarmonyLib;

namespace RE2Extended.Runtime
{
    public sealed class RE2ExtendedRuntime : IRE2ExtendedRuntime
    {
        private const string ExpectedClientMvid = "462483cd-2f1f-4a24-9c83-46d24b1cb3f4";
        private const string ExpectedDediMvid = "fae3f936-680a-459b-99d0-6fe468a1f17f";

        private IModApi api;
        private Harmony harmony;
        private MethodInfo speedTarget;
        private CruiseSpeedContext speedContext;
        private VoiceInterfaceFeature voiceInterface;
        private bool disposed;
        private bool localizationCsvLogged;
        private bool localizationCsvErrorLogged;
        private bool localizationWaitLogged;

        public void Start(IModApi api)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            this.api = api;

            Assembly game = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal));
            if (game == null)
                throw new InvalidOperationException("Assembly-CSharp is not loaded");

            Module module = game.ManifestModule;
            string mvid = module.ModuleVersionId.ToString("D");
            bool knownClient = string.Equals(mvid, ExpectedClientMvid, StringComparison.OrdinalIgnoreCase);
            bool knownDedi = string.Equals(mvid, ExpectedDediMvid, StringComparison.OrdinalIgnoreCase);
            if (!knownClient && !knownDedi)
                api.Log("[RE2Extended] Warning: unknown Assembly-CSharp MVID " + mvid);

            ApplicationMode mode = api.Application == null ? ApplicationMode.Client : api.Application.Mode;
            api.Log("[RE2Extended] Runtime mode=" + mode + " assemblyMvid=" + mvid);

            harmony = new Harmony("RE2Extended.runtime");
            DeviceBucketPatch.Configure(game, api, harmony);
            voiceInterface = VoiceInterfaceFeature.TryInstall(game, api, harmony, mode, knownClient);
            InstallIconAliasPatch(api);

            InstallLocalization(api);
            if (api.Application != null)
                api.Application.OnPlayfieldLoaded += OnPlayfieldLoadedLocalization;
            InstallConfigMerge(game, api);

            // Player ship speed is client-authoritative. Do not install this
            // patch in the dedicated master or playfield-server process.
            if ((mode == ApplicationMode.Client || mode == ApplicationMode.SinglePlayer) && knownClient)
                TryInstallSpeedPatch(game, module, api);
            else
                api.Log("[RE2Extended] Cruise Amplifier speed patch not installed in mode " + mode +
                        " (knownClient=" + knownClient + ").");
        }

        private void TryInstallSpeedPatch(Assembly game, Module module, IModApi api)
        {
            try
            {
                Type diskType = game.GetType("Assembly-CSharp.Disk", false);
                if (diskType == null)
                {
                    api.Log("[RE2Extended] Disk type not found; Cruise Amplifier disabled.");
                    return;
                }

                MethodInfo target = diskType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "SaveView" &&
                                !m.IsStatic &&
                                m.ReturnType == typeof(float) &&
                                m.GetParameters().Length == 0)
                    .SingleOrDefault();

                if (target == null)
                {
                    api.Log("[RE2Extended] Disk.SaveView() float target not found; Cruise Amplifier disabled.");
                    return;
                }

                speedContext = CruiseSpeedContext.Create(game, diskType, api);

                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(typeof(CruiseSpeedPatch).GetMethod(
                        nameof(CruiseSpeedPatch.Postfix), BindingFlags.Static | BindingFlags.Public)));

                CruiseSpeedPatch.Context = speedContext;
                speedTarget = target;

                api.Log("[RE2Extended] Harmony postfix installed: " +
                        target.DeclaringType.FullName + "." + target.Name +
                        "() MVID=" + module.ModuleVersionId +
                        " harmony=" + typeof(Harmony).Assembly.GetName().Version);
            }
            catch (Exception error)
            {
                CruiseSpeedPatch.Context = null;
                speedContext = null;
                speedTarget = null;
                api.Log("[RE2Extended] Cruise Amplifier disabled after patch error: " +
                        error.GetType().Name + ": " + error.Message);
            }
        }

        private void InstallIconAliasPatch(IModApi api)
        {
            try
            {
                Type uiSpriteType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("UISprite", false))
                    .FirstOrDefault(t => t != null);
                if (uiSpriteType == null)
                {
                    api.Log("[RE2Extended] UISprite not found; icon aliases disabled.");
                    return;
                }

                PropertyInfo spriteName = uiSpriteType.GetProperty(
                    "spriteName",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo setter = spriteName == null ? null : spriteName.GetSetMethod();
                if (setter == null)
                {
                    api.Log("[RE2Extended] UISprite.spriteName setter not found; icon aliases disabled.");
                    return;
                }

                harmony.Patch(
                    setter,
                    prefix: new HarmonyMethod(typeof(IconAliasPatch).GetMethod(
                        nameof(IconAliasPatch.Prefix), BindingFlags.Static | BindingFlags.Public)));

                api.Log("[RE2Extended] Icon aliases installed: Cruise Amplifier -> CapacitorMS.");
            }
            catch (Exception error)
            {
                api.Log("[RE2Extended] Icon alias install failed: " +
                        error.GetType().Name + ": " + error.Message);
            }
        }

        private void InstallLocalization(IModApi api)
        {
            try
            {
                Type localizationType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Localization", false))
                    .FirstOrDefault(t => t != null &&
                        (t.GetMethod("Set", BindingFlags.Static | BindingFlags.Public, null,
                            new[] { typeof(string), typeof(string) }, null) != null ||
                         t.GetMethod("LoadCSV", BindingFlags.Static | BindingFlags.Public, null,
                            new[] { typeof(byte[]), typeof(bool) }, null) != null));

                if (localizationType == null)
                {
                    api.Log("[RE2Extended] Localization type not found; device names will use internal keys.");
                    return;
                }

                MethodInfo set = localizationType.GetMethod(
                    "Set",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(string), typeof(string) },
                    null);
                MethodInfo loadCsv = localizationType.GetMethod(
                    "LoadCSV",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(byte[]), typeof(bool) },
                    null);
                PropertyInfo dictionaryProperty = localizationType.GetProperty(
                    "dictionary",
                    BindingFlags.Static | BindingFlags.Public);
                FieldInfo localizationSetField = localizationType.GetField(
                    "localizationHasBeenSet",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                bool merged = false;
                if (loadCsv != null)
                {
                    string configuration = GetConfigurationFolder();
                    string csvPath = configuration == null
                        ? null
                        : Path.Combine(configuration, "Localization.csv");

                    if (csvPath != null && File.Exists(csvPath))
                    {
                        if (IsLocalizationTableReady(dictionaryProperty, localizationSetField))
                        {
                            try
                            {
                                merged = (bool)loadCsv.Invoke(null,
                                    new object[] { File.ReadAllBytes(csvPath), true });
                                if (merged)
                                {
                                    if (!localizationCsvLogged)
                                    {
                                        localizationCsvLogged = true;
                                        api.Log("[RE2Extended] Localization CSV merged: " + csvPath + ".");
                                    }
                                }
                                else
                                {
                                    LogLocalizationCsvFailure(api,
                                        "Localization.LoadCSV returned false");
                                }
                            }
                            catch (TargetInvocationException error)
                            {
                                LogLocalizationCsvFailure(api, error.InnerException ?? error);
                            }
                            catch (Exception error)
                            {
                                LogLocalizationCsvFailure(api, error);
                            }
                        }
                        else if (!localizationWaitLogged)
                        {
                            localizationWaitLogged = true;
                            api.Log("[RE2Extended] Localization table not ready; using fallback entries until the next playfield load.");
                        }
                    }
                    else
                    {
                        LogLocalizationCsvFailure(api, "Localization.csv not found at " + csvPath);
                    }
                }

                if (!merged && set != null)
                    InstallLocalizationFallback(set);
            }
            catch (Exception error)
            {
                api.Log("[RE2Extended] Localization install failed; device names will use internal keys: " +
                        error.GetType().Name + ": " + error.Message);
            }
        }

        private static bool IsLocalizationTableReady(PropertyInfo dictionaryProperty, FieldInfo localizationSetField)
        {
            try
            {
                // Do not touch Localization.dictionary before the game has loaded
                // the base table: the getter can mark localizationHasBeenSet early
                // and prevent the normal base CSV load.
                if (localizationSetField != null &&
                    !(bool)localizationSetField.GetValue(null))
                    return false;

                if (dictionaryProperty == null) return true;

                object dictionary = dictionaryProperty.GetValue(null, null);
                ICollection collection = dictionary as ICollection;
                return dictionary != null && (collection == null || collection.Count > 0);
            }
            catch
            {
                return false;
            }
        }

        private void LogLocalizationCsvFailure(IModApi api, object error)
        {
            if (localizationCsvErrorLogged) return;
            localizationCsvErrorLogged = true;

            try
            {
                api.Log("[RE2Extended] Localization CSV merge failed; using fallback entries: " + error);
            }
            catch
            {
                // Never let diagnostics break the localization callback.
            }
        }

        private static void InstallLocalizationFallback(MethodInfo set)
        {
            SetLocalization(set, "CruiseAmplifier", "Cruise Amplifier");
            SetLocalization(set, "CruiseAmplifierCV", "Cruise Amplifier");
            SetLocalization(set, "CruiseAmplifierSV", "Cruise Amplifier");

            const string common =
                "Cruise Amplifiers are standard aftermarket upgrades for commercial vessels for improving sublight travel times. " +
                "They increase the maximum velocity of the ship, but require shields to be down to function. " +
                "Additionally, they automatically shut off when under fire. " +
                "Removing a cruise amplifier will activate the safety lockout for 20 seconds.";

            SetLocalization(set, "bkiCruiseAmplifierCV", common +
                "\n\nIn space:\n" +
                "1 amplifier: 115 m/s\n" +
                "2 amplifiers: 130 m/s\n" +
                "3 amplifiers: 145 m/s\n" +
                "4 amplifiers: 160 m/s\n\n" +
                "In atmosphere:\n" +
                "1 amplifier: 65 m/s\n" +
                "2 amplifiers: 70 m/s\n" +
                "3 amplifiers: 75 m/s\n" +
                "4 amplifiers: 80 m/s");

            SetLocalization(set, "bkiCruiseAmplifierSV", common +
                "\n\nIn space:\n" +
                "1 amplifier: 160 m/s\n" +
                "2 amplifiers: 200 m/s\n\n" +
                "In atmosphere:\n" +
                "1 amplifier: 80 m/s\n" +
                "2 amplifiers: 100 m/s");
        }

        private void OnPlayfieldLoadedLocalization(IPlayfield playfield)
        {
            // Localization may finish loading after mod Init. Retry the native
            // CSV merge once the playfield is live; the merge is idempotent.
            InstallLocalization(api);
        }

        private static void SetLocalization(MethodInfo set, string key, string value)
        {
            try
            {
                set.Invoke(null, new object[] { key, value });
            }
            catch
            {
                // A missing localization entry must not break the mod.
            }
        }

        private void InstallConfigMerge(Assembly game, IModApi api)
        {
            try
            {
                string configuration = GetConfigurationFolder();
                string blocksFolder = configuration == null ? null : Path.Combine(configuration, "Blocks");
                string templatesFolder = configuration == null ? null : Path.Combine(configuration, "Templates");

                if (blocksFolder == null || !Directory.Exists(blocksFolder))
                {
                    api.Log("[RE2Extended] Block fragments folder missing: " + blocksFolder);
                    return;
                }

                Type collectionType = game.GetType("DynamicPropertiesCollection", false);
                if (collectionType == null)
                {
                    api.Log("[RE2Extended] DynamicPropertiesCollection not found; config merge disabled.");
                    return;
                }

                Type configFileType = game.GetType("ConfigFile", false);
                PropertyInfo configFileProperties = configFileType == null
                    ? null
                    : configFileType.GetProperty("PropertiesCollection",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (!ConfigMerger.Configure(collectionType, configFileType, configFileProperties,
                        api, blocksFolder, templatesFolder))
                {
                    api.Log("[RE2Extended] Config merger could not resolve the ECF collection shape.");
                    return;
                }

                bool installed = false;
                if (configFileType != null && configFileProperties != null)
                    installed = TryInstallConfigFileHook(api, configFileType);

                if (installed)
                {
                    api.Log("[RE2Extended] Config merge installed on ConfigFile.Load; " +
                            "fragments are merged once per named config load.");
                    return;
                }

                MethodInfo postfix = typeof(ConfigMergePatch)
                    .GetMethod(nameof(ConfigMergePatch.Postfix), BindingFlags.Static | BindingFlags.Public);

                int patched = 0;
                foreach (MethodInfo method in collectionType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name != "ReadEcf" && method.Name != "LoadBinary" && method.Name != "Read")
                        continue;

                    try
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                        patched++;
                        api.Log("[RE2Extended] Config merge fallback hook: " + method.Name +
                                "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")");
                    }
                    catch (Exception error)
                    {
                        api.Log("[RE2Extended] Config merge hook failed for " + method.Name + ": " +
                                error.GetType().Name + ": " + error.Message);
                    }
                }

                api.Log("[RE2Extended] Config merge installed on " + patched + " fallback loader(s).");
            }
            catch (Exception error)
            {
                api.Log("[RE2Extended] Config merge disabled after error: " +
                        error.GetType().Name + ": " + error.Message);
            }
        }

        private bool TryInstallConfigFileHook(IModApi api, Type configFileType)
        {
            try
            {
                MethodInfo load = configFileType.GetMethod(
                    "Load",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool) },
                    null);

                if (load == null)
                {
                    api.Log("[RE2Extended] ConfigFile.Load signature not found; using collection fallback.");
                    return false;
                }

                MethodInfo postfix = typeof(ConfigFileLoadPatch)
                    .GetMethod(nameof(ConfigFileLoadPatch.Postfix), BindingFlags.Static | BindingFlags.Public);
                harmony.Patch(load, postfix: new HarmonyMethod(postfix));
                api.Log("[RE2Extended] Config merge hook: ConfigFile.Load(string,string,...).");
                return true;
            }
            catch (Exception error)
            {
                api.Log("[RE2Extended] ConfigFile.Load hook failed; using collection fallback: " +
                        error.GetType().Name + ": " + error.Message);
                return false;
            }
        }

        private static string GetConfigurationFolder()
        {
            string runtimeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string modRoot = runtimeFolder == null ? null : Path.GetDirectoryName(runtimeFolder);
            return modRoot == null ? null : Path.Combine(modRoot, "Configuration");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            CruiseSpeedPatch.Context = null;

            try
            {
                if (voiceInterface != null)
                {
                    voiceInterface.Dispose();
                    voiceInterface = null;
                }
            }
            catch
            {
                // Shutdown must not throw into the game.
            }

            try
            {
                if (api != null && api.Application != null)
                    api.Application.OnPlayfieldLoaded -= OnPlayfieldLoadedLocalization;
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
                speedContext = null;
                speedTarget = null;
                harmony = null;
            }
        }
    }

    public static class CruiseSpeedPatch
    {
        internal static CruiseSpeedContext Context;

        public static void Postfix(object __instance, ref float __result)
        {
            CruiseSpeedContext context = Context;
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

    public static class DeviceBucketPatch
    {
        internal const string BucketName = "RE2Extended";

        private static Type blockPosListType;
        private static ConstructorInfo blockPosListConstructor;
        private static FieldInfo dictionaryField;
        private static bool configured;

        internal static void Configure(Assembly game, IModApi api, Harmony harmony)
        {
            try
            {
                Type conditionType = game.GetType("Assembly-CSharp.ConditionDictionary", false);
                blockPosListType = game.GetType("BlockPosValueList", false);
                if (conditionType == null || blockPosListType == null)
                {
                    api.Log("[RE2Extended] Device bucket disabled: ConditionDictionary/BlockPosValueList missing.");
                    return;
                }

                dictionaryField = conditionType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType.IsGenericType &&
                        f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                        f.FieldType.GetGenericArguments()[0] == typeof(string) &&
                        f.FieldType.GetGenericArguments()[1] == blockPosListType);

                blockPosListConstructor = blockPosListType
                    .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(c => c.GetParameters().Length == 1 &&
                                         c.GetParameters()[0].ParameterType == typeof(int));

                ConstructorInfo conditionCtor = conditionType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);

                if (dictionaryField == null || blockPosListConstructor == null || conditionCtor == null)
                {
                    api.Log("[RE2Extended] Device bucket disabled: ConditionDictionary constructor/device map missing.");
                    return;
                }

                harmony.Patch(conditionCtor, postfix: new HarmonyMethod(
                    typeof(DeviceBucketPatch).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.Public)));

                configured = true;
                api.Log("[RE2Extended] Device bucket installed: " + BucketName + ".");
            }
            catch (Exception error)
            {
                api.Log("[RE2Extended] Device bucket install failed: " +
                        error.GetType().Name + ": " + error.Message);
            }
        }

        public static void Postfix(object __instance)
        {
            Ensure(__instance);
        }

        internal static void Ensure(object conditionDictionary)
        {
            if (!configured || conditionDictionary == null || dictionaryField == null) return;

            try
            {
                IDictionary dictionary = dictionaryField.GetValue(conditionDictionary) as IDictionary;
                if (dictionary == null || dictionary.Contains(BucketName)) return;

                object list = blockPosListConstructor.Invoke(new object[] { 16 });
                dictionary.Add(BucketName, list);
            }
            catch
            {
                // Detection fails closed if the bucket could not be created.
            }
        }
    }

    public static class ConfigMergePatch
    {
        public static void Postfix(object __instance)
        {
            try
            {
                ConfigMerger.TryMerge(__instance);
            }
            catch
            {
                // Never let an optional config merge break a game config load.
            }
        }
    }

    public static class IconAliasPatch
    {
        private static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "CruiseAmplifierCV", "CapacitorMS" },
                { "CruiseAmplifierSV", "CapacitorMS" },
                { "CruiseAmplifierCVBlocks", "CapacitorMS" },
                { "CruiseAmplifierSVBlocks", "CapacitorMS" },
                { "VoiceInterface", "LCDNoFrame1x1" },
                { "VoiceInterfaceBlocks", "LCDNoFrame1x1" }
            };

        public static void Prefix(ref string value)
        {
            string alias;
            if (!string.IsNullOrEmpty(value) && Aliases.TryGetValue(value, out alias))
                value = alias;
        }
    }

    public static class ConfigFileLoadPatch
    {
        public static void Postfix(object __instance, object[] __args, bool __result)
        {
            try
            {
                string name = __args != null && __args.Length > 1 ? __args[1] as string : null;
                ConfigMerger.TryMergeConfigFile(__instance, __result, name);
            }
            catch
            {
                // Never let an optional config merge break a game config load.
            }
        }
    }

    internal sealed class CruiseSpeedContext
    {
        private const int EntityTypeCv = 3;
        private const int EntityTypeSv = 4;
        private const string CvBlockName = "CruiseAmplifierCV";
        private const string SvBlockName = "CruiseAmplifierSV";
        private const int DetectionCacheMs = 500;
        private const int AttackTimeoutSeconds = 20;

        private static readonly float[] CvSpace = { 115f, 130f, 145f, 160f };
        private static readonly float[] CvAtmosphere = { 65f, 70f, 75f, 80f };
        private static readonly float[] SvSpace = { 160f, 200f };
        private static readonly float[] SvAtmosphere = { 80f, 100f };

        private readonly IModApi api;
        private readonly Type diskType;
        private readonly FieldInfo diskEmulatorField;
        private readonly FieldInfo entityTypeField;
        private readonly FieldInfo worldField;
        private readonly MethodInfo closeConfigMethod;
        private readonly FieldInfo structureEccField;
        private readonly FieldInfo dbTokenActionLayoutField;
        private readonly FieldInfo actionLayoutDbTokenField;
        private readonly FieldInfo dbTokenLastAttackField;
        private readonly FieldInfo dbTokenRelatedField;
        private readonly FieldInfo currentTicksStaticField;
        private readonly FieldInfo currentTicksValueField;
        private readonly bool combatContractAvailable;
        private readonly FieldInfo actionLayoutConditionField;
        private readonly MethodInfo conditionOpenImageMethod;
        private readonly MethodInfo positionsOfTypeMethod;
        private readonly FieldInfo vectorXField;
        private readonly FieldInfo vectorYField;
        private readonly FieldInfo vectorZField;
        private readonly bool conditionContractAvailable;

        private int cachedStructureId = int.MinValue;
        private string cachedBlockName;
        private int cachedAtTicks;
        private int cachedCount;
        private string lastDetection;
        private string lastApplied;
        private bool failureLogged;
        private bool detectionFailureLogged;
        private bool combatFailureLogged;
        private bool firstApplyLogged;
        private bool firstStructureLogged;
        private readonly HashSet<string> loggedZeroDetection = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> mappedBlockIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> mappedBlockIdTicks = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> blockIdLookupLogged = new HashSet<string>(StringComparer.Ordinal);

        private CruiseSpeedContext(IModApi api, Type diskType, FieldInfo diskEmulatorField,
            FieldInfo entityTypeField, FieldInfo worldField, MethodInfo closeConfigMethod,
            FieldInfo structureEccField, FieldInfo dbTokenActionLayoutField,
            FieldInfo actionLayoutDbTokenField, FieldInfo dbTokenLastAttackField,
            FieldInfo dbTokenRelatedField, FieldInfo currentTicksStaticField,
            FieldInfo currentTicksValueField, bool combatContractAvailable,
            FieldInfo actionLayoutConditionField, MethodInfo conditionOpenImageMethod,
            MethodInfo positionsOfTypeMethod, FieldInfo vectorXField,
            FieldInfo vectorYField, FieldInfo vectorZField,
            bool conditionContractAvailable)
        {
            this.api = api;
            this.diskType = diskType;
            this.diskEmulatorField = diskEmulatorField;
            this.entityTypeField = entityTypeField;
            this.worldField = worldField;
            this.closeConfigMethod = closeConfigMethod;
            this.structureEccField = structureEccField;
            this.dbTokenActionLayoutField = dbTokenActionLayoutField;
            this.actionLayoutDbTokenField = actionLayoutDbTokenField;
            this.dbTokenLastAttackField = dbTokenLastAttackField;
            this.dbTokenRelatedField = dbTokenRelatedField;
            this.currentTicksStaticField = currentTicksStaticField;
            this.currentTicksValueField = currentTicksValueField;
            this.combatContractAvailable = combatContractAvailable;
            this.actionLayoutConditionField = actionLayoutConditionField;
            this.conditionOpenImageMethod = conditionOpenImageMethod;
            this.positionsOfTypeMethod = positionsOfTypeMethod;
            this.vectorXField = vectorXField;
            this.vectorYField = vectorYField;
            this.vectorZField = vectorZField;
            this.conditionContractAvailable = conditionContractAvailable;
        }

        internal static CruiseSpeedContext Create(Assembly game, Type diskType, IModApi api)
        {
            Type emulatorType = game.GetType("Assembly-CSharp.EmulatorService", false);
            if (emulatorType == null)
                throw new InvalidOperationException("EmulatorService missing");

            FieldInfo diskEmulatorField = diskType
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(f => f.FieldType == emulatorType);
            if (diskEmulatorField == null) throw new InvalidOperationException("Disk emulator field missing");

            FieldInfo entityTypeField = FindFieldInHierarchy(emulatorType,
                f => f.FieldType != null && string.Equals(f.FieldType.Name, "EntityType", StringComparison.Ordinal));
            if (entityTypeField == null) throw new InvalidOperationException("EntityType field missing");

            FieldInfo worldField = FindFieldInHierarchy(emulatorType,
                f => f.FieldType != null && string.Equals(f.FieldType.Name, "World", StringComparison.Ordinal));
            if (worldField == null) throw new InvalidOperationException("World field missing");

            MethodInfo closeConfigMethod = worldField.FieldType.GetMethod(
                "get_CloseConfig",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (closeConfigMethod == null || closeConfigMethod.ReturnType != typeof(bool))
                throw new InvalidOperationException("World.get_CloseConfig() missing");

            // Combat/shield native contract. If this cannot be resolved, the
            // feature still installs but never boosts (fail closed).
            FieldInfo structureEccField = null;
            FieldInfo dbTokenActionLayoutField = null;
            FieldInfo actionLayoutDbTokenField = null;
            FieldInfo dbTokenLastAttackField = null;
            FieldInfo dbTokenRelatedField = null;
            FieldInfo currentTicksStaticField = null;
            FieldInfo currentTicksValueField = null;
            FieldInfo actionLayoutConditionField = null;
            MethodInfo conditionOpenImageMethod = null;
            MethodInfo positionsOfTypeMethod = null;
            FieldInfo vectorXField = null;
            FieldInfo vectorYField = null;
            FieldInfo vectorZField = null;
            bool combatAvailable = false;
            bool conditionAvailable = false;

            try
            {
                Type structureBridgeType = game.GetType("Eleon.ModBridge.StructureBridge", false);
                Type databaseTokenType = game.GetType("Assembly-CSharp.DatabaseToken", false);
                Type actionLayoutType = game.GetType("Assembly-CSharp.ActionLayout", false);
                Type registryTokenType = game.GetType("Assembly-CSharp.RegistryTokenConnectionToken", false);
                if (structureBridgeType == null || databaseTokenType == null ||
                    actionLayoutType == null || registryTokenType == null)
                    throw new InvalidOperationException("combat contract types missing");

                structureEccField = structureBridgeType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType == databaseTokenType);
                dbTokenActionLayoutField = databaseTokenType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType == actionLayoutType);
                actionLayoutDbTokenField = actionLayoutType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType == databaseTokenType);
                dbTokenLastAttackField = databaseTokenType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.Name == "keywordsLength" && f.FieldType == typeof(ulong));
                dbTokenRelatedField = FindFieldInHierarchy(databaseTokenType,
                    f => f.Name == "messageCollection" && f.FieldType == databaseTokenType);
                currentTicksStaticField = registryTokenType
                    .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType == registryTokenType);
                currentTicksValueField = registryTokenType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.Name == "messageCollection" && f.FieldType == typeof(ulong));

                Type conditionType = game.GetType("Assembly-CSharp.ConditionDictionary", false);
                Type positionListType = game.GetType("BlockPosValueList", false);
                Type vector3iType = game.GetType("Vector3i", false);
                if (conditionType != null && positionListType != null && vector3iType != null)
                {
                    actionLayoutConditionField = actionLayoutType
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType == conditionType);
                    conditionOpenImageMethod = conditionType.GetMethod(
                        "OpenImage",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(string) }, null);
                    positionsOfTypeMethod = positionListType
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == "GetAllBlockPositonsOfType" &&
                                             m.GetParameters().Length == 1 &&
                                             m.GetParameters()[0].ParameterType == typeof(int));
                    vectorXField = vector3iType.GetField("x", BindingFlags.Instance | BindingFlags.Public);
                    vectorYField = vector3iType.GetField("y", BindingFlags.Instance | BindingFlags.Public);
                    vectorZField = vector3iType.GetField("z", BindingFlags.Instance | BindingFlags.Public);
                    conditionAvailable = actionLayoutConditionField != null &&
                                         conditionOpenImageMethod != null &&
                                         positionsOfTypeMethod != null &&
                                         vectorXField != null &&
                                         vectorYField != null &&
                                         vectorZField != null;
                }

                combatAvailable = structureEccField != null &&
                                  dbTokenActionLayoutField != null &&
                                  actionLayoutDbTokenField != null &&
                                  dbTokenLastAttackField != null &&
                                  dbTokenRelatedField != null &&
                                  currentTicksStaticField != null &&
                                  currentTicksValueField != null;

                if (!combatAvailable)
                    api.Log("[RE2Extended] Combat contract incomplete; Cruise Amplifier will never boost.");
            }
            catch (Exception error)
            {
                api.Log("[RE2Extended] Combat contract resolution failed; Cruise Amplifier will never boost: " +
                        error.GetType().Name + ": " + error.Message);
            }

            api.Log("[RE2Extended] Speed contract: SaveView + " +
                    (combatAvailable ? "combat gate available" : "combat gate unavailable") +
                    "; device aggregate=" +
                    (conditionAvailable ? "ConditionDictionary available" : "unavailable"));

            return new CruiseSpeedContext(api, diskType, diskEmulatorField, entityTypeField,
                worldField, closeConfigMethod, structureEccField, dbTokenActionLayoutField,
                actionLayoutDbTokenField, dbTokenLastAttackField, dbTokenRelatedField,
                currentTicksStaticField, currentTicksValueField, combatAvailable,
                actionLayoutConditionField, conditionOpenImageMethod, positionsOfTypeMethod,
                vectorXField, vectorYField, vectorZField, conditionAvailable);
        }

        internal void Apply(object disk, ref float result)
        {
            if (disk == null || disk.GetType() != diskType) return;

            object emulator = diskEmulatorField.GetValue(disk);
            if (emulator == null) return;

            int entityType;
            try { entityType = Convert.ToInt32(entityTypeField.GetValue(emulator)); }
            catch { return; }

            if (entityType != EntityTypeCv && entityType != EntityTypeSv) return;

            if (!firstApplyLogged)
            {
                firstApplyLogged = true;
                api.Log("[RE2Extended] SaveView postfix active; entityType=" + entityType + ".");
            }

            IApplication application = api.Application;
            if (application == null) return;
            IPlayer player = application.LocalPlayer;
            if (player == null) return;
            IStructure structure = player.CurrentStructure;
            if (structure == null || !structure.IsReady || !structure.IsPowered) return;

            if (!firstStructureLogged)
            {
                firstStructureLogged = true;
                string driving = "unavailable";
                try
                {
                    IEntity drivingEntity = player.DrivingEntity;
                    if (drivingEntity != null)
                        driving = DescribeEntity(drivingEntity);
                }
                catch (Exception error)
                {
                    driving = "error:" + error.GetType().Name;
                }

                api.Log("[RE2Extended] Structure context: current=" + DescribeStructure(structure) +
                        " driving=" + driving + ".");
            }

            if (structure.IsShieldActive) return;
            if (IsUnderAttack(structure)) return;

            string blockName = entityType == EntityTypeCv ? CvBlockName : SvBlockName;
            int count = GetActiveAmplifierCount(structure, blockName);
            if (count <= 0) return;

            object world = worldField.GetValue(emulator);
            if (world == null) return;
            bool isSpace = (bool)closeConfigMethod.Invoke(world, null);

            int max = entityType == EntityTypeCv ? 4 : 2;
            int tier = Math.Min(count, max);
            float target = entityType == EntityTypeCv
                ? (isSpace ? CvSpace[tier - 1] : CvAtmosphere[tier - 1])
                : (isSpace ? SvSpace[tier - 1] : SvAtmosphere[tier - 1]);

            string detection = "type=" + (entityType == EntityTypeCv ? "CV" : "SV") +
                               " space=" + isSpace +
                               " activeAmps=" + count +
                               " tier=" + tier +
                               " current=" + result.ToString("0.##") +
                               " target=" + target.ToString("0.##");
            if (!string.Equals(detection, lastDetection, StringComparison.Ordinal))
            {
                lastDetection = detection;
                api.Log("[RE2Extended] Cruise Amplifier: " + detection);
            }

            if (target > result + 0.01f)
            {
                result = target;
                string applied = detection + "|applied";
                if (!string.Equals(applied, lastApplied, StringComparison.Ordinal))
                {
                    lastApplied = applied;
                    api.Log("[RE2Extended] Applied Cruise Amplifier limit " + target.ToString("0.##") +
                            " m/s (" + (isSpace ? "space" : "atmosphere") +
                            ", active amplifiers=" + count + ").");
                }
            }
        }

        private int GetActiveAmplifierCount(IStructure structure, string blockName)
        {
            int now = Environment.TickCount;
            if (structure.Id == cachedStructureId &&
                string.Equals(blockName, cachedBlockName, StringComparison.Ordinal) &&
                unchecked(now - cachedAtTicks) >= 0 &&
                unchecked(now - cachedAtTicks) < DetectionCacheMs)
                return cachedCount;

            int count = 0;
            int positions = 0;
            string source = "none";
            List<VectorInt3> list = new List<VectorInt3>();
            int mappedBlockId = ResolveBlockId(blockName);

            if (conditionContractAvailable && mappedBlockId >= 0)
            {
                try
                {
                    object ecc = structureEccField.GetValue(structure);
                    object actionLayout = ecc == null ? null : dbTokenActionLayoutField.GetValue(ecc);
                    object condition = actionLayout == null ? null : actionLayoutConditionField.GetValue(actionLayout);
                    if (condition != null)
                    {
                        // ConditionDictionary keeps device positions incrementally as
                        // blocks are placed, removed, replaced or reloaded. Query our
                        // dedicated mod bucket and let the game filter it to the mapped
                        // block type. No structure/block scan is performed.
                        DeviceBucketPatch.Ensure(condition);
                        object deviceList = conditionOpenImageMethod.Invoke(
                            condition, new object[] { DeviceBucketPatch.BucketName });
                        if (deviceList != null)
                        {
                            object result = positionsOfTypeMethod.Invoke(deviceList, new object[] { mappedBlockId });
                            list = ConvertPositions(result);
                            source = "ConditionDictionary." + DeviceBucketPatch.BucketName;
                        }
                    }
                }
                catch (Exception error)
                {
                    if (!detectionFailureLogged)
                    {
                        detectionFailureLogged = true;
                        api.Log("[RE2Extended] ConditionDictionary lookup failed: " +
                                error.GetType().Name + ": " + error.Message);
                    }
                }
            }

            if (list.Count == 0)
            {
                try
                {
                    List<VectorInt3> registered = structure.GetDevicePositions(blockName);
                    if (registered != null && registered.Count > 0)
                    {
                        list = registered;
                        source = "GetDevicePositions";
                    }
                }
                catch (Exception error)
                {
                    if (!detectionFailureLogged)
                    {
                        detectionFailureLogged = true;
                        api.Log("[RE2Extended] Device-position fallback failed: " +
                                error.GetType().Name + ": " + error.Message);
                    }
                }
            }

            if (list.Count == 0)
            {
                try
                {
                    List<SenderSignal> signals = structure.GetBlockSignals(blockName);
                    if (signals != null)
                    {
                        foreach (SenderSignal signal in signals)
                        {
                            if (!signal.BlockPos.HasValue) continue;
                            VectorInt3 pos = signal.BlockPos.Value;
                            if (!list.Contains(pos)) list.Add(pos);
                        }
                        if (list.Count > 0) source = "GetBlockSignals";
                    }
                }
                catch (Exception error)
                {
                    if (!detectionFailureLogged)
                    {
                        detectionFailureLogged = true;
                        api.Log("[RE2Extended] Block-signal fallback failed: " +
                                error.GetType().Name + ": " + error.Message);
                    }
                }
            }

            positions = list.Count;
            try
            {
                foreach (VectorInt3 pos in list)
                {
                    IBlock block = structure.GetBlock(pos);
                    if (block == null) continue;

                    int type;
                    int shape;
                    int rotation;
                    bool active;
                    block.Get(out type, out shape, out rotation, out active);

                    // A block can be present but switched off manually or by a signal.
                    // The host structure power gate is checked separately in Apply().
                    if (active) count++;
                }
            }
            catch (Exception error)
            {
                if (!detectionFailureLogged)
                {
                    detectionFailureLogged = true;
                    api.Log("[RE2Extended] Amplifier active-state check failed; no boost: " +
                            error.GetType().Name + ": " + error.Message);
                }
                count = 0;
            }

            cachedStructureId = structure.Id;
            cachedBlockName = blockName;
            cachedAtTicks = now;
            cachedCount = count;

            if (positions != 0 || count != 0)
            {
                api.Log("[RE2Extended] Amplifier count: structure=" + structure.Id +
                        " block=" + blockName +
                        " blockId=" + mappedBlockId +
                        " source=" + source +
                        " positions=" + positions +
                        " active=" + count + ".");
            }
            else
            {
                string key = structure.Id + "|" + blockName;
                if (loggedZeroDetection.Add(key))
                {
                    api.Log("[RE2Extended] No amplifier positions from device aggregate: structure=" +
                            DescribeStructure(structure) + " block=" + blockName +
                            " blockId=" + mappedBlockId +
                            " aggregateContract=" + conditionContractAvailable);
                }
            }
            return count;
        }

        private List<VectorInt3> ConvertPositions(object result)
        {
            List<VectorInt3> list = new List<VectorInt3>();
            IEnumerable items = result as IEnumerable;
            if (items == null) return list;

            foreach (object item in items)
            {
                if (item == null) continue;
                try
                {
                    int x = (int)vectorXField.GetValue(item);
                    int y = (int)vectorYField.GetValue(item);
                    int z = (int)vectorZField.GetValue(item);
                    list.Add(new VectorInt3(x, y, z));
                }
                catch
                {
                    // Ignore unexpected vector shapes; the contract remains fail-closed.
                }
            }
            return list;
        }

        private int ResolveBlockId(string blockName)
        {
            int now = Environment.TickCount;
            int cached;
            if (mappedBlockIds.TryGetValue(blockName, out cached))
            {
                if (cached >= 0) return cached;
                int cachedAt;
                if (mappedBlockIdTicks.TryGetValue(blockName, out cachedAt) &&
                    unchecked(now - cachedAt) >= 0 &&
                    unchecked(now - cachedAt) < 5000)
                    return -1;
            }

            mappedBlockIdTicks[blockName] = now;
            try
            {
                Dictionary<string, int> mapping = api.Application == null
                    ? null
                    : api.Application.GetBlockAndItemMapping();
                int id;
                if (mapping != null && mapping.TryGetValue(blockName, out id))
                {
                    mappedBlockIds[blockName] = id;
                    return id;
                }
            }
            catch
            {
                // Fall through to the fail-closed path.
            }

            mappedBlockIds[blockName] = -1;
            if (blockIdLookupLogged.Add(blockName))
                api.Log("[RE2Extended] No runtime block ID mapping for " + blockName + ".");
            return -1;
        }

        private static string DescribeStructure(IStructure structure)
        {
            if (structure == null) return "null";

            string id = "id=" + structure.Id;
            try
            {
                IEntity entity = structure.Entity;
                if (entity == null) return id + " entity=null";
                return id + " entityId=" + entity.Id + " name='" + entity.Name + "' type=" + entity.Type;
            }
            catch (Exception error)
            {
                return id + " entity=error:" + error.GetType().Name;
            }
        }

        private static string DescribeEntity(IEntity entity)
        {
            if (entity == null) return "null";
            try
            {
                return "entityId=" + entity.Id + " name='" + entity.Name + "' type=" + entity.Type;
            }
            catch (Exception error)
            {
                return "entity=error:" + error.GetType().Name;
            }
        }

        private bool IsUnderAttack(IStructure structure)
        {
            if (!combatContractAvailable)
                return true;

            try
            {
                object ecc = structureEccField.GetValue(structure);
                if (ecc == null) return true;

                object actionLayout = dbTokenActionLayoutField.GetValue(ecc);
                if (actionLayout == null) return true;

                object token = actionLayoutDbTokenField.GetValue(actionLayout);
                if (token == null) return true;

                ulong current = GetCurrentTicks();
                ulong ownLast = (ulong)dbTokenLastAttackField.GetValue(token);
                ulong elapsed = unchecked(current - ownLast);

                object related = dbTokenRelatedField.GetValue(token);
                if (related != null)
                {
                    ulong relatedLast = (ulong)dbTokenLastAttackField.GetValue(related);
                    ulong relatedElapsed = unchecked(current - relatedLast);
                    if (relatedElapsed < elapsed) elapsed = relatedElapsed;
                }

                return elapsed < (ulong)(20 * AttackTimeoutSeconds);
            }
            catch (Exception error)
            {
                if (!combatFailureLogged)
                {
                    combatFailureLogged = true;
                    api.Log("[RE2Extended] Combat gate failed; no boost: " +
                            error.GetType().Name + ": " + error.Message);
                }
                return true;
            }
        }

        private ulong GetCurrentTicks()
        {
            try
            {
                object registry = currentTicksStaticField.GetValue(null);
                if (registry != null)
                    return (ulong)currentTicksValueField.GetValue(registry);
            }
            catch
            {
                // Fall through to the public tick counter.
            }

            IApplication application = api.Application;
            return application == null ? 0UL : application.GameTicks;
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

        internal void FailOnce(Exception error)
        {
            if (failureLogged) return;
            failureLogged = true;
            try
            {
                api.Log("[RE2Extended] Speed postfix skipped after error: " +
                        error.GetType().Name + ": " + error.Message);
            }
            catch
            {
                // Never let diagnostics break the game callback.
            }
        }
    }

    internal static class ConfigMerger
    {
        private static Type collectionType;
        private static Type configFileType;
        private static PropertyInfo configFileProperties;
        private static Type elementType;
        private static FieldInfo elementsField;
        private static FieldInfo nameField;
        private static MethodInfo readEcfMethod;
        private static MethodInfo applyReferencesMethod;
        private static IModApi api;
        private static string[] blockFragments = new string[0];
        private static string[] templateFragments = new string[0];
        private static bool failureLogged;
        private static readonly HashSet<string> injectedNames = new HashSet<string>(StringComparer.Ordinal);

        internal static bool Configure(Type type, Type fileType, PropertyInfo fileProperties,
            IModApi modApi, string blocksFolder, string templatesFolder)
        {
            collectionType = type;
            configFileType = fileType;
            configFileProperties = fileProperties;
            api = modApi;
            blockFragments = Directory.Exists(blocksFolder) ? Directory.GetFiles(blocksFolder, "*.ecf") : new string[0];
            templateFragments = templatesFolder != null && Directory.Exists(templatesFolder)
                ? Directory.GetFiles(templatesFolder, "*.ecf")
                : new string[0];

            elementType = collectionType.GetNestedType("Element", BindingFlags.Public | BindingFlags.NonPublic);
            elementsField = collectionType.GetField("Elements", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            nameField = elementType == null
                ? null
                : elementType.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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

            return elementType != null && elementsField != null && nameField != null && readEcfMethod != null;
        }

        internal static void TryMergeConfigFile(object configFile, bool loaded, string configName)
        {
            if (!loaded || configFile == null || configFileType == null || configFileProperties == null)
                return;
            if (!configFileType.IsInstanceOfType(configFile))
                return;
            if (!string.Equals(configName, "BlocksConfig", StringComparison.Ordinal) &&
                !string.Equals(configName, "Templates", StringComparison.Ordinal))
                return;

            try
            {
                object collection = configFileProperties.GetValue(configFile, null);
                if (collection != null)
                    TryMerge(collection);
            }
            catch (Exception error)
            {
                LogOnce("ConfigFile.Load merge failed: " + error.GetType().Name + ": " + error.Message);
            }
        }

        internal static void TryMerge(object collection)
        {
            if (collection == null || collectionType == null || !collectionType.IsInstanceOfType(collection))
                return;

            try
            {
                IList elements = elementsField.GetValue(collection) as IList;
                if (elements == null) return;

                bool isBlocks = ContainsName(elements, "CapacitorMS") && ContainsName(elements, "Air");
                bool isTemplates = ContainsName(elements, "WarpDriveSV") &&
                                   ContainsName(elements, "ThrusterSVSmallT2");

                if (isBlocks)
                    Merge(collection, elements, blockFragments, "Block");
                else if (isTemplates)
                    Merge(collection, elements, templateFragments, "Template");
            }
            catch (Exception error)
            {
                LogOnce("merge failed: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private static void Merge(object collection, IList elements, string[] fragments, string elementTag)
        {
            if (fragments == null || fragments.Length == 0) return;

            int added = 0;
            foreach (string fragmentPath in fragments)
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
                    read = (bool)readEcfMethod.Invoke(fragment, new object[] { fragmentPath, elementTag });
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

                    elements.Add(element);
                    added++;

                    if (injectedNames.Add(elementTag + ":" + name))
                    {
                        api.Log("[RE2Extended] Injected " + elementTag + " '" + name +
                                "' from " + Path.GetFileName(fragmentPath) + ".");
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
                api.Log("[RE2Extended] Config merge: " + text);
            }
            catch
            {
                // Never let diagnostics break a config load.
            }
        }
    }
}
