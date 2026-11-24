using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Eleon.Modding;
using HarmonyLib;
using UnityEngine;

namespace RE2Extended.Runtime
{
    // Naive PoC: every removed Thruster block on the local player's structure
    // plays the Critical Hit clip, provided an active VoiceInterface device is
    // present on that same structure.
    internal sealed class VoiceInterfaceFeature : IDisposable
    {
        private enum ComponentKind
        {
            None,
            Thruster,
            Turret,
            Weapon,
            Power
        }

        private const string VoiceBlockName = "VoiceInterface";
        private const float StructureAlertPollIntervalSeconds = 0.333f;
        private const string NoShieldAlarmId = "no_shield";
        private const string NoFuelAlarmId = "no_fuel";
        private const float FuelEmptyEpsilon = 0.001f;
        private const string CriticalHitAudioFile = "voice_critical_hit.wav";
        private const string JetEngineAudioFile = "voice_jet_engine_destroyed.wav";
        private const string TurretAudioFile = "voice_turret_unit_destroyed.wav";
        private const string WeaponAudioFile = "voice_weapon_destroyed.wav";
        private const string PowerAudioFile = "voice_power_unit_destroyed.wav";
        private const string AlarmCriticalDamageAudioFile = "alarm_critical_damage.wav";
        private const string AlarmNoShieldAudioFile = "alarm_no_shield.wav";
        private const string AlarmNoFuelAudioFile = "alarm_no_fuel.wav";

        private readonly IModApi api;
        private readonly Type registryTokenType;
        private readonly Type controlTableType;
        private readonly Type conditionDictionaryType;
        private readonly FieldInfo registryControlTableField;
        private readonly MethodInfo controlTableJoinDrive;
        private readonly FieldInfo actionLayoutConditionField;
        private readonly MethodInfo conditionOpenImage;
        private readonly MethodInfo conditionCopyComponent;
        private readonly MethodInfo listGetBlockPosValue;
        private readonly MethodInfo listGetAllPositionsOfType;
        private readonly FieldInfo vectorXField;
        private readonly FieldInfo vectorYField;
        private readonly FieldInfo vectorZField;
        private readonly string modRoot;
        private readonly VoicePlayer player = new VoicePlayer();
        private readonly VoiceEvent thrusterDestroyedEvent;
        private readonly VoiceEvent turretDestroyedEvent;
        private readonly VoiceEvent weaponDestroyedEvent;
        private readonly VoiceEvent powerDestroyedEvent;

        private GameObject structureAlertObject;
        private VoiceStructureAlertMonitor structureAlertMonitor;
        private int alertStructureId = int.MinValue;
        private bool noShieldAlarmArmed;
        private bool pentaxidAlarmArmed;
        private float nextStructureAlertPoll;
        private bool structureAlertFailureLogged;

        private Dictionary<string, int> blockNameToId;
        private Dictionary<int, string> blockIdToName;
        private bool failureLogged;
        private bool noMappingLogged;

        private VoiceInterfaceFeature(IModApi api, Type registryTokenType, Type controlTableType,
            Type conditionDictionaryType, FieldInfo registryControlTableField,
            MethodInfo controlTableJoinDrive, FieldInfo actionLayoutConditionField,
            MethodInfo conditionOpenImage, MethodInfo conditionCopyComponent,
            MethodInfo listGetBlockPosValue, MethodInfo listGetAllPositionsOfType,
            FieldInfo vectorXField, FieldInfo vectorYField, FieldInfo vectorZField,
            string modRoot)
        {
            this.api = api;
            this.registryTokenType = registryTokenType;
            this.controlTableType = controlTableType;
            this.conditionDictionaryType = conditionDictionaryType;
            this.registryControlTableField = registryControlTableField;
            this.controlTableJoinDrive = controlTableJoinDrive;
            this.actionLayoutConditionField = actionLayoutConditionField;
            this.conditionOpenImage = conditionOpenImage;
            this.conditionCopyComponent = conditionCopyComponent;
            this.listGetBlockPosValue = listGetBlockPosValue;
            this.listGetAllPositionsOfType = listGetAllPositionsOfType;
            this.vectorXField = vectorXField;
            this.vectorYField = vectorYField;
            this.vectorZField = vectorZField;
            this.modRoot = modRoot;
            thrusterDestroyedEvent = new VoiceEvent(
                "thruster_destroyed", CriticalHitPath, JetEnginePath);
            turretDestroyedEvent = new VoiceEvent(
                "turret_unit_destroyed", CriticalHitPath, TurretPath);
            weaponDestroyedEvent = new VoiceEvent(
                "weapon_destroyed", CriticalHitPath, WeaponPath);
            powerDestroyedEvent = new VoiceEvent(
                "power_unit_destroyed", CriticalHitPath, PowerPath);
        }

        internal static VoiceInterfaceFeature TryInstall(Assembly game, IModApi api,
            Harmony harmony, ApplicationMode mode, bool knownClient)
        {
            if (!knownClient || (mode != ApplicationMode.Client && mode != ApplicationMode.SinglePlayer))
            {
                api.Log("[RE2Extended] Voice Interface audio not installed in mode " + mode +
                        " (knownClient=" + knownClient + ").");
                return null;
            }

            try
            {
                Type blockPosListType = game.GetType("BlockPosValueList", false);
                Type registryTokenType = game.GetType("Assembly-CSharp.RegistryToken", false);
                Type controlTableType = game.GetType("Assembly-CSharp.ControlTable", false);
                Type conditionDictionaryType = game.GetType("Assembly-CSharp.ConditionDictionary", false);
                Type databaseTokenType = game.GetType("Assembly-CSharp.DatabaseToken", false);
                Type actionLayoutType = game.GetType("Assembly-CSharp.ActionLayout", false);
                Type vectorType = game.GetType("Vector3i", false);
                Type structureBridgeType = game.GetType("Eleon.ModBridge.StructureBridge", false);

                if (blockPosListType == null || registryTokenType == null ||
                    controlTableType == null || conditionDictionaryType == null ||
                    databaseTokenType == null || actionLayoutType == null ||
                    vectorType == null || structureBridgeType == null)
                {
                    api.Log("[RE2Extended] Voice Interface disabled: required native types missing.");
                    return null;
                }

                MethodInfo remove = blockPosListType.GetMethod(
                    "Remove",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { vectorType },
                    null);

                MethodInfo getBlockPosValue = blockPosListType.GetMethod(
                    "GetBlockPosValue",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { vectorType },
                    null);

                MethodInfo openImage = conditionDictionaryType.GetMethod(
                    "OpenImage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);

                MethodInfo copyComponent = conditionDictionaryType.GetMethod(
                    "CopyComponent",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                MethodInfo getAllPositions = blockPosListType.GetMethod(
                    "GetAllBlockPositonsOfType",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int) },
                    null);

                FieldInfo registryControlTableField = registryTokenType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType == controlTableType);

                MethodInfo joinDrive = controlTableType.GetMethod(
                    "get_JoinDrive",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                FieldInfo actionConditionField = FindFieldInHierarchy(actionLayoutType,
                    f => f.FieldType == conditionDictionaryType);

                FieldInfo xField = vectorType.GetField("x", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo yField = vectorType.GetField("y", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo zField = vectorType.GetField("z", BindingFlags.Instance | BindingFlags.Public);

                if (remove == null || getBlockPosValue == null || openImage == null ||
                    copyComponent == null || getAllPositions == null ||
                    registryControlTableField == null || joinDrive == null ||
                    actionConditionField == null ||
                    xField == null || yField == null || zField == null)
                {
                    api.Log("[RE2Extended] Voice Interface disabled: native contract incomplete.");
                    return null;
                }

                string runtimeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string modRoot = runtimeFolder == null ? null : Path.GetDirectoryName(runtimeFolder);
                if (string.IsNullOrEmpty(modRoot))
                {
                    api.Log("[RE2Extended] Voice Interface disabled: mod folder unavailable.");
                    return null;
                }

                VoiceInterfaceFeature feature = new VoiceInterfaceFeature(api, registryTokenType,
                    controlTableType, conditionDictionaryType, registryControlTableField, joinDrive,
                    actionConditionField, openImage, copyComponent, getBlockPosValue, getAllPositions,
                    xField, yField, zField, modRoot);
                feature.StartStructureAlertMonitor();

                VoiceInterfaceRemovePatch.Current = feature;
                harmony.Patch(remove, prefix: new HarmonyMethod(
                    typeof(VoiceInterfaceRemovePatch).GetMethod(
                        nameof(VoiceInterfaceRemovePatch.Prefix),
                        BindingFlags.Static | BindingFlags.Public)));

                api.Log("[RE2Extended] Voice Interface installed: block=" + VoiceBlockName +
                        " audio=" + feature.CriticalHitPath + ", " + feature.JetEnginePath +
                        ", " + feature.AlarmCriticalDamagePath + ", " + feature.AlarmNoShieldPath +
                        ", " + feature.AlarmNoFuelPath +
                        " alerts=" + StructureAlertPollIntervalSeconds.ToString("0.###") + "s" +
                        " hook=BlockPosValueList.Remove(Vector3i).");
                return feature;
            }
            catch (Exception error)
            {
                VoiceInterfaceRemovePatch.Current = null;
                api.Log("[RE2Extended] Voice Interface disabled after install error: " +
                        error.GetType().Name + ": " + error.Message);
                return null;
            }
        }

        private string CriticalHitPath
        {
            get { return Path.Combine(modRoot, "Audio", CriticalHitAudioFile); }
        }

        private string JetEnginePath
        {
            get { return Path.Combine(modRoot, "Audio", JetEngineAudioFile); }
        }

        private string TurretPath
        {
            get { return Path.Combine(modRoot, "Audio", TurretAudioFile); }
        }

        private string WeaponPath
        {
            get { return Path.Combine(modRoot, "Audio", WeaponAudioFile); }
        }

        private string PowerPath
        {
            get { return Path.Combine(modRoot, "Audio", PowerAudioFile); }
        }

        private string AlarmCriticalDamagePath
        {
            get { return Path.Combine(modRoot, "Audio", AlarmCriticalDamageAudioFile); }
        }

        private string AlarmNoShieldPath
        {
            get { return Path.Combine(modRoot, "Audio", AlarmNoShieldAudioFile); }
        }

        private string AlarmNoFuelPath
        {
            get { return Path.Combine(modRoot, "Audio", AlarmNoFuelAudioFile); }
        }

        private void StartStructureAlertMonitor()
        {
            try
            {
                structureAlertObject = new GameObject("RE2Extended_VoiceStructureAlerts");
                UnityEngine.Object.DontDestroyOnLoad(structureAlertObject);
                structureAlertMonitor = structureAlertObject.AddComponent<VoiceStructureAlertMonitor>();
                structureAlertMonitor.Owner = this;
            }
            catch (Exception error)
            {
                api.Log("[RE2Extended] Voice structure alert monitor disabled: " +
                        error.GetType().Name + ": " + error.Message);
                structureAlertMonitor = null;
                try
                {
                    if (structureAlertObject != null)
                        UnityEngine.Object.Destroy(structureAlertObject);
                }
                catch
                {
                    // Shutdown must not throw.
                }
                structureAlertObject = null;
            }
        }

        // Generic per-structure alert tick. Future alerts such as low fuel can
        // be added here without changing the monitor or its lifecycle.
        internal void TickStructureAlerts()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now < nextStructureAlertPoll) return;
                nextStructureAlertPoll = now + StructureAlertPollIntervalSeconds;

                IStructure structure = GetLocalStructure();
                if (structure == null || !structure.IsReady)
                {
                    ResetStructureAlertState();
                    return;
                }

                if (structure.Id != alertStructureId)
                {
                    alertStructureId = structure.Id;
                    noShieldAlarmArmed = false;
                    pentaxidAlarmArmed = false;
                }

                if (!structure.IsPowered)
                {
                    noShieldAlarmArmed = false;
                    pentaxidAlarmArmed = false;
                    return;
                }

                CheckShieldAlert(structure);
                CheckPentaxidAlert(structure);
            }
            catch (Exception error)
            {
                if (!structureAlertFailureLogged)
                {
                    structureAlertFailureLogged = true;
                    LogOnce("Voice structure alert monitor skipped after error: " +
                            error.GetType().Name + ": " + error.Message);
                }
            }
        }

        private void CheckShieldAlert(IStructure structure)
        {
            bool shieldActive;
            int shieldLevel;
            try
            {
                shieldActive = structure.IsShieldActive;
                shieldLevel = structure.ShieldLevel;
            }
            catch
            {
                return;
            }

            // A ship without a shield generator, or with its shield switched
            // off, is never armed. This is also the "has shields" gate: the
            // native state only leaves Off when a shield is installed and on.
            if (!shieldActive)
            {
                noShieldAlarmArmed = false;
                return;
            }

            if (shieldLevel > 0)
            {
                noShieldAlarmArmed = true;
                return;
            }

            // Edge-triggered: one alarm per depletion, no spam while the
            // ship remains at zero shields.
            if (!noShieldAlarmArmed) return;

            // Only scan the device bucket when the alarm is about to fire.
            if (!HasActiveVoiceInterface(structure)) return;

            player.PlayAlarm(NoShieldAlarmId, AlarmNoShieldPath, LogOnce);
            noShieldAlarmArmed = false;
        }

        private void CheckPentaxidAlert(IStructure structure)
        {
            IStructureTank tank;
            try
            {
                tank = structure.PentaxidTank;
            }
            catch
            {
                return;
            }

            if (tank == null)
            {
                pentaxidAlarmArmed = false;
                return;
            }

            float content;
            try
            {
                content = tank.Content;
            }
            catch
            {
                return;
            }

            // Arm only after a positive amount has been observed. This avoids
            // playing the alarm for a ship that is already empty on entry/load.
            if (content > FuelEmptyEpsilon)
            {
                pentaxidAlarmArmed = true;
                return;
            }

            // Edge-triggered: only fire on the positive-to-zero transition.
            if (!pentaxidAlarmArmed) return;

            // Only scan the device bucket when the alarm is about to fire.
            if (!HasActiveVoiceInterface(structure)) return;

            player.PlayAlarm(NoFuelAlarmId, AlarmNoFuelPath, LogOnce);
            pentaxidAlarmArmed = false;
        }

        private void ResetStructureAlertState()
        {
            alertStructureId = int.MinValue;
            noShieldAlarmArmed = false;
            pentaxidAlarmArmed = false;
        }

        internal void HandleRemoval(object list, object blockPosition)
        {
            if (list == null || blockPosition == null) return;

            try
            {
                int blockType = ReadBlockType(list, blockPosition);
                if (blockType <= 0) return;

                string blockName = GetBlockName(blockType);
                if (string.IsNullOrEmpty(blockName)) return;

                ComponentKind kind;
                VoiceEvent voiceEvent;
                if (!TryGetVoiceEvent(blockName, out kind, out voiceEvent)) return;

                IStructure structure = GetLocalStructure();
                if (structure == null) return;

                object condition = GetConditionDictionary(structure);
                if (condition == null) return;

                // Only accept removals from one of this structure's own device
                // aggregate buckets. This keeps the PoC scoped to the local ship.
                if (!BelongsToStructure(condition, list)) return;

                if (!structure.IsReady || !structure.IsPowered) return;

                if (!HasActiveVoiceInterface(structure, condition)) return;

                // Verbal channel: the event contains its clips in sequence.
                // Repeats are suppressed while this event is playing or queued.
                player.EnqueueEvent(voiceEvent, LogOnce);

                // Alarm channel: no queue. Play immediately unless this alarm is
                // already playing. Any component destruction currently represents
                // the Critical Damage event.
                player.PlayAlarm("critical_damage", AlarmCriticalDamagePath, LogOnce);
            }
            catch (Exception error)
            {
                if (!failureLogged)
                {
                    failureLogged = true;
                    try
                    {
                        api.Log("[RE2Extended] Voice Interface event skipped after error: " +
                                error.GetType().Name + ": " + error.Message);
                    }
                    catch
                    {
                        // Never let diagnostics break the game callback.
                    }
                }
            }
        }

        private bool TryGetVoiceEvent(string blockName, out ComponentKind kind,
            out VoiceEvent voiceEvent)
        {
            kind = ComponentKind.None;
            voiceEvent = null;

            if (Contains(blockName, "Thruster"))
            {
                kind = ComponentKind.Thruster;
                voiceEvent = thrusterDestroyedEvent;
                return true;
            }

            if (IsTurret(blockName))
            {
                kind = ComponentKind.Turret;
                voiceEvent = turretDestroyedEvent;
                return true;
            }

            if (IsFixedWeapon(blockName))
            {
                kind = ComponentKind.Weapon;
                voiceEvent = weaponDestroyedEvent;
                return true;
            }

            if (IsPowerGenerator(blockName))
            {
                kind = ComponentKind.Power;
                voiceEvent = powerDestroyedEvent;
                return true;
            }

            return false;
        }

        private static bool IsTurret(string blockName)
        {
            if (StartsWith(blockName, "Broken")) return false;
            if (Contains(blockName, "TurretRadar")) return false;
            if (Contains(blockName, "TurretAlarm")) return false;

            return Contains(blockName, "Turret") || Contains(blockName, "SentryGun");
        }

        private static bool IsFixedWeapon(string blockName)
        {
            if (Contains(blockName, "WeaponLocker")) return false;
            if (Contains(blockName, "WeaponRack")) return false;
            return Contains(blockName, "Weapon");
        }

        private static bool IsPowerGenerator(string blockName)
        {
            if (StartsWith(blockName, "Broken")) return false;
            if (!Contains(blockName, "Generator")) return false;

            // Exclude generators that are not the ship's power unit.
            if (Contains(blockName, "ShieldGenerator")) return false;
            if (Contains(blockName, "GravityGenerator")) return false;
            if (Contains(blockName, "OxygenGenerator")) return false;
            if (Contains(blockName, "WaterGenerator")) return false;
            if (Contains(blockName, "ControlFieldGenerator")) return false;

            return true;
        }

        private static bool Contains(string text, string value)
        {
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool StartsWith(string text, string value)
        {
            return text.StartsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        private IStructure GetLocalStructure()
        {
            IApplication application = api.Application;
            if (application == null) return null;
            IPlayer localPlayer = application.LocalPlayer;
            if (localPlayer == null) return null;
            return localPlayer.CurrentStructure;
        }

        private object GetConditionDictionary(IStructure structure)
        {
            Type structureType = structure.GetType();
            FieldInfo eccField = FindFieldInHierarchy(structureType,
                f => f.FieldType != null && f.FieldType.Name == "DatabaseToken");
            if (eccField == null) return null;

            object ecc = eccField.GetValue(structure);
            if (ecc == null) return null;

            FieldInfo actionLayoutField = FindFieldInHierarchy(ecc.GetType(),
                f => f.FieldType != null && f.FieldType.Name == "ActionLayout");
            if (actionLayoutField == null) return null;

            object actionLayout = actionLayoutField.GetValue(ecc);
            if (actionLayout == null) return null;

            object condition = actionLayoutConditionField.GetValue(actionLayout);
            if (condition == null) return null;

            DeviceBucketPatch.Ensure(condition);
            return condition;
        }

        private bool BelongsToStructure(object condition, object list)
        {
            try
            {
                IEnumerable bucketNames = conditionCopyComponent.Invoke(condition, null) as IEnumerable;
                if (bucketNames == null) return false;

                foreach (object nameObject in bucketNames)
                {
                    string name = nameObject as string;
                    if (string.IsNullOrEmpty(name)) continue;
                    object bucket = conditionOpenImage.Invoke(condition, new object[] { name });
                    if (ReferenceEquals(bucket, list)) return true;
                }
            }
            catch
            {
                // Fail closed.
            }
            return false;
        }

        private int ReadBlockType(object list, object blockPosition)
        {
            try
            {
                object registryToken = listGetBlockPosValue.Invoke(list, new[] { blockPosition });
                if (registryToken == null) return 0;

                object controlTable = registryControlTableField.GetValue(registryToken);
                if (controlTable == null) return 0;

                object value = controlTableJoinDrive.Invoke(controlTable, null);
                return value == null ? 0 : Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private bool HasActiveVoiceInterface(IStructure structure)
        {
            if (structure == null) return false;
            return HasActiveVoiceInterface(structure, GetConditionDictionary(structure));
        }

        private bool HasActiveVoiceInterface(IStructure structure, object condition)
        {
            if (structure == null || condition == null) return false;

            int voiceBlockId = ResolveBlockId(VoiceBlockName);
            if (voiceBlockId <= 0) return false;

            return CountActiveDevices(structure, condition, voiceBlockId) > 0;
        }

        private int CountActiveDevices(IStructure structure, object condition, int blockId)
        {
            try
            {
                object bucket = conditionOpenImage.Invoke(condition,
                    new object[] { DeviceBucketPatch.BucketName });
                if (bucket == null) return 0;

                object positions = listGetAllPositionsOfType.Invoke(bucket, new object[] { blockId });
                IEnumerable items = positions as IEnumerable;
                if (items == null) return 0;

                int active = 0;
                foreach (object item in items)
                {
                    int x, y, z;
                    if (!TryReadPosition(item, out x, out y, out z)) continue;

                    IBlock block = structure.GetBlock(x, y, z);
                    if (block == null) continue;

                    int type;
                    int shape;
                    int rotation;
                    bool isActive;
                    block.Get(out type, out shape, out rotation, out isActive);
                    if (type == blockId && isActive) active++;
                }
                return active;
            }
            catch
            {
                return 0;
            }
        }

        private bool TryReadPosition(object position, out int x, out int y, out int z)
        {
            x = y = z = 0;
            if (position == null || vectorXField == null || vectorYField == null || vectorZField == null)
                return false;

            try
            {
                x = Convert.ToInt32(vectorXField.GetValue(position));
                y = Convert.ToInt32(vectorYField.GetValue(position));
                z = Convert.ToInt32(vectorZField.GetValue(position));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private int ResolveBlockId(string blockName)
        {
            EnsureBlockMapping();
            int id;
            return blockNameToId != null && blockNameToId.TryGetValue(blockName, out id) ? id : -1;
        }

        private string GetBlockName(int blockType)
        {
            EnsureBlockMapping();
            string name;
            return blockIdToName != null && blockIdToName.TryGetValue(blockType, out name) ? name : null;
        }

        private void EnsureBlockMapping()
        {
            if (blockNameToId != null) return;

            blockNameToId = new Dictionary<string, int>(StringComparer.Ordinal);
            blockIdToName = new Dictionary<int, string>();

            try
            {
                Dictionary<string, int> mapping = api.Application == null
                    ? null
                    : api.Application.GetBlockAndItemMapping();
                if (mapping == null)
                {
                    if (!noMappingLogged)
                    {
                        noMappingLogged = true;
                        api.Log("[RE2Extended] Voice Interface: block mapping not ready.");
                    }
                    blockNameToId = null;
                    blockIdToName = null;
                    return;
                }

                foreach (KeyValuePair<string, int> entry in mapping)
                {
                    if (string.IsNullOrEmpty(entry.Key)) continue;
                    blockNameToId[entry.Key] = entry.Value;
                    if (!blockIdToName.ContainsKey(entry.Value))
                        blockIdToName.Add(entry.Value, entry.Key);
                }
            }
            catch
            {
                blockNameToId = null;
                blockIdToName = null;
            }
        }

        private void LogOnce(string text)
        {
            try
            {
                api.Log("[RE2Extended] " + text);
            }
            catch
            {
                // Diagnostics must not break playback.
            }
        }

        private static FieldInfo FindFieldInHierarchy(Type type, Func<FieldInfo, bool> predicate)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current
                    .GetFields(BindingFlags.Instance | BindingFlags.Public |
                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(predicate);
                if (field != null) return field;
            }
            return null;
        }

        public void Dispose()
        {
            VoiceInterfaceRemovePatch.Current = null;

            try
            {
                structureAlertMonitor = null;
                if (structureAlertObject != null)
                    UnityEngine.Object.Destroy(structureAlertObject);
            }
            catch
            {
                // Shutdown must not throw into the game.
            }
            finally
            {
                structureAlertObject = null;
                ResetStructureAlertState();
            }

            player.Dispose();
        }
    }

    public sealed class VoiceStructureAlertMonitor : MonoBehaviour
    {
        internal VoiceInterfaceFeature Owner;

        private void Update()
        {
            VoiceInterfaceFeature owner = Owner;
            if (owner == null) return;

            try
            {
                owner.TickStructureAlerts();
            }
            catch
            {
                // The feature contains its own diagnostics; never let a Unity
                // callback throw.
            }
        }
    }

    internal static class VoiceInterfaceRemovePatch
    {
        internal static VoiceInterfaceFeature Current;

        public static void Prefix(object __instance, object[] __args)
        {
            VoiceInterfaceFeature feature = Current;
            if (feature == null || __args == null || __args.Length == 0) return;

            try
            {
                feature.HandleRemoval(__instance, __args[0]);
            }
            catch
            {
                // The feature is already exception-contained; never let a patch
                // callback throw into the game's block-removal path.
            }
        }
    }
}
