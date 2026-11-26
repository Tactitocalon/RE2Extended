using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Eleon.Modding;
using UnityEngine;

namespace RE2Extended.Runtime.ConstructionManager
{
    // Phase 0 opt-in probe. Enable by creating ConstructionManager.probe.next to
    // the mod folder. Queue mutation additionally requires
    // ConstructionManager.probe.mutate containing "itemType,count" on the first
    // line. The probe is intentionally read-only unless that second file exists.
    internal sealed class ConstructionManagerProbe
    {
        private const string ProbeMarker = "ConstructionManager.probe";
        private const string MutateMarker = "ConstructionManager.probe.mutate";
        private const string ConstructorBucket = "Constructor";

        private readonly IModApi api;
        private readonly ConstructionManagerGameAccess access;
        private readonly ConstructionManagerBlock managerBlock;
        private readonly string modRoot;

        private int lastProbedStructureId = int.MinValue;
        private int observedEvents;
        private bool failureLogged;
        private bool enabled;
        private bool enabledKnown;
        private float nextEnabledCheck;

        internal ConstructionManagerProbe(IModApi api, Assembly game,
            ConstructionManagerGameAccess access, ConstructionManagerBlock managerBlock,
            string modRoot)
        {
            this.api = api;
            this.access = access;
            this.managerBlock = managerBlock;
            this.modRoot = modRoot;
        }

        internal bool Enabled
        {
            get
            {
                RefreshEnabled();
                return enabled;
            }
        }

        private void RefreshEnabled()
        {
            try
            {
                if (enabledKnown && Time.unscaledTime < nextEnabledCheck) return;
                enabledKnown = true;
                nextEnabledCheck = Time.unscaledTime + 2f;
                enabled = !string.IsNullOrEmpty(modRoot) &&
                          File.Exists(Path.Combine(modRoot, ProbeMarker));
            }
            catch
            {
                enabled = false;
            }
        }

        internal void Tick()
        {
            if (!Enabled) return;

            try
            {
                IStructure structure = GetLocalStructure();
                if (structure == null || !structure.IsReady) return;
                if (structure.Id == lastProbedStructureId) return;
                lastProbedStructureId = structure.Id;
                Run(structure);
            }
            catch (Exception error)
            {
                if (!failureLogged)
                {
                    failureLogged = true;
                    ConstructionManagerDiagnostics.Warning(api,
                        "Probe stopped after error: " + error.GetType().Name + ": " + error.Message);
                }
            }
        }

        internal void ObserveGameEvent(GameEventType type, object arg1, object arg2,
            object arg3, object arg4, object arg5)
        {
            if (!Enabled || observedEvents >= 40) return;

            try
            {
                if (type != GameEventType.DeviceUsed &&
                    type != GameEventType.InventoryOpened &&
                    type != GameEventType.WindowOpened &&
                    type != GameEventType.BlockChanged)
                    return;

                observedEvents++;
                ConstructionManagerDiagnostics.Log(api,
                    "Probe event " + type +
                    " arg1=" + ConstructionManagerDiagnostics.Describe(arg1) +
                    " arg2=" + ConstructionManagerDiagnostics.Describe(arg2) +
                    " arg3=" + ConstructionManagerDiagnostics.Describe(arg3) +
                    " arg4=" + ConstructionManagerDiagnostics.Describe(arg4) +
                    " arg5=" + ConstructionManagerDiagnostics.Describe(arg5));
            }
            catch
            {
                // Observation must never break the game event dispatch.
            }
        }

        private void Run(IStructure structure)
        {
            ConstructionManagerDiagnostics.Log(api, "=== Phase 0 probe start ===");
            ConstructionManagerDiagnostics.Log(api,
                "Structure: id=" + structure.Id +
                " ready=" + structure.IsReady +
                " powered=" + structure.IsPowered +
                " steamOwner=" + (structure.PlayerCreatedSteamId ?? "<null>") +
                " lastVisitedTicks=" + structure.LastVisitedTicks +
                " min=" + ConstructionManagerDiagnostics.DescribePosition(structure.MinPos) +
                " max=" + ConstructionManagerDiagnostics.DescribePosition(structure.MaxPos));

            ProbeSavePath();
            ProbeBlockMapping();
            ProbeManagerBlock(structure);
            ProbeConstructors(structure);
            ProbeMutation(structure);
            ConstructionManagerDiagnostics.Log(api, "=== Phase 0 probe end ===");
        }

        private void ProbeSavePath()
        {
            try
            {
                IApplication application = api.Application;
                if (application == null)
                {
                    ConstructionManagerDiagnostics.Log(api, "Save path probe: IApplication is null.");
                    return;
                }

                foreach (AppFolder folder in new[]
                {
                    AppFolder.Root, AppFolder.Content, AppFolder.Mod,
                    AppFolder.SaveGame, AppFolder.Cache, AppFolder.ActiveScenario
                })
                {
                    string path;
                    try { path = application.GetPathFor(folder); }
                    catch (Exception error)
                    {
                        ConstructionManagerDiagnostics.Log(api,
                            "Save path " + folder + ": GetPathFor failed: " + error.GetType().Name);
                        continue;
                    }

                    bool exists = !string.IsNullOrEmpty(path) && Directory.Exists(path);
                    ConstructionManagerDiagnostics.Log(api,
                        "Path " + folder + ": " + (path ?? "<null>") + " exists=" + exists);
                }

                string save = application.GetPathFor(AppFolder.SaveGame);
                if (!string.IsNullOrEmpty(save))
                {
                    string mods = Path.Combine(save, "Mods");
                    ConstructionManagerDiagnostics.Log(api,
                        "Co-save candidate: " + Path.Combine(mods, "RE2Extended", "ConstructionManager.json") +
                        " modsExists=" + Directory.Exists(mods));
                    if (Directory.Exists(mods))
                    {
                        string[] children = Directory.GetDirectories(mods);
                        ConstructionManagerDiagnostics.Log(api,
                            "Existing save Mods folders: " + children.Length +
                            (children.Length == 0 ? "" : " [" + string.Join(", ", children.Select(Path.GetFileName).ToArray()) + "]"));
                    }
                }
            }
            catch (Exception error)
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "Save path probe failed: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private void ProbeBlockMapping()
        {
            Dictionary<string, int> mapping = access.GetBlockMapping();
            if (mapping == null)
            {
                ConstructionManagerDiagnostics.Log(api, "Block mapping probe: mapping not ready.");
                return;
            }

            ConstructionManagerDiagnostics.Log(api,
                "Block mapping entries: " + mapping.Count +
                " manager=" + access.ResolveBlockId(ConstructionManagerBlock.BlockName));

            var constructorIds = new StringBuilder();
            for (int i = 0; i < ConstructorRegistry.All.Length; i++)
            {
                ConstructorDefinition definition = ConstructorRegistry.All[i];
                if (i > 0) constructorIds.Append(", ");
                constructorIds.Append(definition.BlockName)
                    .Append('=').Append(access.ResolveBlockId(definition.BlockName));
            }
            ConstructionManagerDiagnostics.Log(api, "Constructor block IDs: " + constructorIds);
        }

        private void ProbeManagerBlock(IStructure structure)
        {
            try
            {
                string[] customNames;
                try { customNames = structure.GetAllCustomDeviceNames(); }
                catch (Exception error)
                {
                    ConstructionManagerDiagnostics.Warning(api,
                        "GetAllCustomDeviceNames failed: " + error.GetType().Name);
                    customNames = null;
                }

                if (customNames != null)
                {
                    string[] relevant = customNames
                        .Where(n => !string.IsNullOrEmpty(n) &&
                                    (n.IndexOf("Constructor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     n.IndexOf("ConstructionManager", StringComparison.OrdinalIgnoreCase) >= 0))
                        .Take(64).ToArray();
                    ConstructionManagerDiagnostics.Log(api,
                        "Custom device names: total=" + customNames.Length +
                        " relevant=" + relevant.Length +
                        (relevant.Length == 0 ? "" : " [" + string.Join(", ", relevant) + "]"));
                }

                int managerId = access.ResolveBlockId(ConstructionManagerBlock.BlockName);
                List<VectorInt3> bucketPositions = access.GetBucketPositions(
                    structure, DeviceBucketPatch.BucketName, managerId);
                List<VectorInt3> customPositions = access.GetCustomDevicePositions(
                    structure, ConstructionManagerBlock.BlockName);
                ConstructionManagerDiagnostics.Log(api,
                    "Manager positions: bucket=" + bucketPositions.Count +
                    " customDevice=" + customPositions.Count);
                managerBlock.LogDiagnostics(structure);
            }
            catch (Exception error)
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "Manager block probe failed: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private void ProbeConstructors(IStructure structure)
        {
            for (int i = 0; i < ConstructorRegistry.All.Length; i++)
            {
                ConstructorDefinition definition = ConstructorRegistry.All[i];
                try
                {
                    int blockId = access.ResolveBlockId(definition.BlockName);
                    List<VectorInt3> bucketPositions = access.GetBucketPositions(
                        structure, ConstructorBucket, blockId);
                    List<VectorInt3> customPositions = access.GetCustomDevicePositions(
                        structure, definition.BlockName);

                    IContainer named = null;
                    try { named = structure.GetDevice<IContainer>(definition.BlockName); }
                    catch { }

                    IContainer first = null;
                    VectorInt3 firstPosition = default(VectorInt3);
                    bool hasFirst = false;
                    if (bucketPositions.Count > 0)
                    {
                        firstPosition = bucketPositions[0];
                        hasFirst = true;
                    }
                    else if (customPositions.Count > 0)
                    {
                        firstPosition = customPositions[0];
                        hasFirst = true;
                    }

                    if (hasFirst)
                    {
                        try { first = structure.GetDevice<IContainer>(firstPosition); }
                        catch { }
                    }

                    ConstructionManagerDiagnostics.Log(api,
                        "Constructor " + definition.BlockName + " class=" + definition.EcfClass +
                        " id=" + blockId +
                        " bucketPos=" + bucketPositions.Count +
                        " customPos=" + customPositions.Count +
                        " namedContainer=" + (named == null ? "null" : named.GetType().FullName) +
                        " firstPos=" + (hasFirst ? ConstructionManagerDiagnostics.DescribePosition(firstPosition) : "<none>") +
                        " firstContainer=" + (first == null ? "null" : first.GetType().FullName));

                    if (named != null)
                        LogQueue(definition.BlockName + " named", named);
                    if (first != null)
                        LogQueue(definition.BlockName + " first", first);
                }
                catch (Exception error)
                {
                    ConstructionManagerDiagnostics.Warning(api,
                        "Constructor probe failed for " + definition.BlockName + ": " +
                        error.GetType().Name + ": " + error.Message);
                }
            }
        }

        private void LogQueue(string label, IContainer container)
        {
            try
            {
                List<ItemStack> content = container.GetContent() ?? new List<ItemStack>();
                var text = new StringBuilder();
                for (int i = 0; i < content.Count && i < 12; i++)
                {
                    if (i > 0) text.Append(", ");
                    text.Append(content[i].id).Append('x').Append(content[i].count);
                }
                ConstructionManagerDiagnostics.Log(api,
                    "Queue " + label + ": count=" + content.Count +
                    " volumeCapacity=" + container.VolumeCapacity +
                    (content.Count == 0 ? "" : " [" + text + "]"));
            }
            catch (Exception error)
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "Queue read failed for " + label + ": " + error.GetType().Name + ": " + error.Message);
            }
        }

        private void ProbeMutation(IStructure structure)
        {
            string path = Path.Combine(modRoot, MutateMarker);
            if (!File.Exists(path))
            {
                ConstructionManagerDiagnostics.Log(api,
                    "Queue mutation probe skipped (no " + MutateMarker + ").");
                return;
            }

            int itemType;
            int count;
            if (!TryReadMutation(path, out itemType, out count))
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "Queue mutation probe skipped: expected first line 'itemType,count'.");
                return;
            }

            bool mutatedAny = false;
            for (int i = 0; i < ConstructorRegistry.All.Length; i++)
            {
                ConstructorDefinition definition = ConstructorRegistry.All[i];
                try
                {
                    int blockId = access.ResolveBlockId(definition.BlockName);
                    List<VectorInt3> positions = access.GetBucketPositions(structure, ConstructorBucket, blockId);
                    if (positions.Count == 0)
                        positions = access.GetCustomDevicePositions(structure, definition.BlockName);
                    if (positions.Count == 0) continue;

                    IContainer container = structure.GetDevice<IContainer>(positions[0]);
                    if (container == null) continue;

                    List<ItemStack> original = container.GetContent() ?? new List<ItemStack>();
                    ConstructionManagerDiagnostics.Log(api,
                        "Mutation probe " + definition.BlockName + " at " +
                        ConstructionManagerDiagnostics.DescribePosition(positions[0]) +
                        " item=" + itemType + " count=" + count +
                        " before=" + original.Count);

                    try
                    {
                        int added = container.AddItems(itemType, count);
                        List<ItemStack> afterAdd = container.GetContent() ?? new List<ItemStack>();
                        ConstructionManagerDiagnostics.Log(api,
                            "  AddItems result=" + added + " contentAfter=" + afterAdd.Count);

                        int removed = container.RemoveItems(itemType, count);
                        List<ItemStack> afterRemove = container.GetContent() ?? new List<ItemStack>();
                        ConstructionManagerDiagnostics.Log(api,
                            "  RemoveItems result=" + removed + " contentAfter=" + afterRemove.Count);
                    }
                    finally
                    {
                        try
                        {
                            container.SetContent(original.Select(item => item).ToList());
                            List<ItemStack> restored = container.GetContent() ?? new List<ItemStack>();
                            ConstructionManagerDiagnostics.Log(api,
                                "  Restore SetContent contentAfter=" + restored.Count);
                        }
                        catch (Exception restoreError)
                        {
                            ConstructionManagerDiagnostics.Warning(api,
                                "  Queue restore failed for " + definition.BlockName + ": " +
                                restoreError.GetType().Name + ": " + restoreError.Message);
                        }
                    }

                    mutatedAny = true;
                    break;
                }
                catch (Exception error)
                {
                    ConstructionManagerDiagnostics.Warning(api,
                        "Mutation probe failed for " + definition.BlockName + ": " +
                        error.GetType().Name + ": " + error.Message);
                }
            }

            if (!mutatedAny)
                ConstructionManagerDiagnostics.Warning(api,
                    "Mutation probe found no constructor queue to test.");
        }

        private static bool TryReadMutation(string path, out int itemType, out int count)
        {
            itemType = 0;
            count = 0;
            try
            {
                string first = File.ReadAllLines(path).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
                if (first == null) return false;
                string[] parts = first.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return false;
                return int.TryParse(parts[0].Trim(), out itemType) &&
                       int.TryParse(parts[1].Trim(), out count) &&
                       itemType > 0 && count > 0;
            }
            catch
            {
                return false;
            }
        }

        private IStructure GetLocalStructure()
        {
            try
            {
                IApplication application = api.Application;
                if (application == null) return null;
                IPlayer player = application.LocalPlayer;
                return player == null ? null : player.CurrentStructure;
            }
            catch
            {
                return null;
            }
        }
    }
}
