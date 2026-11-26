using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eleon.Modding;

namespace RE2Extended.Runtime.ConstructionManager
{
    // Shared reflection access for the Construction Manager. The public mod API
    // exposes IStructure.GetDevicePositions/GetDevice, but constructor/multi-type
    // enumeration still needs the native ConditionDictionary buckets.
    internal sealed class ConstructionManagerGameAccess
    {
        private readonly IModApi api;
        private readonly Type conditionDictionaryType;
        private readonly Type blockPosValueListType;
        private readonly Type vector3iType;
        private readonly Type databaseTokenType;
        private readonly Type actionLayoutType;
        private readonly FieldInfo structureEccField;
        private readonly FieldInfo databaseTokenActionLayoutField;
        private readonly FieldInfo actionLayoutConditionField;
        private readonly MethodInfo conditionOpenImageMethod;
        private readonly MethodInfo positionsOfTypeMethod;
        private readonly FieldInfo vectorXField;
        private readonly FieldInfo vectorYField;
        private readonly FieldInfo vectorZField;
        private readonly bool conditionContractAvailable;

        private Dictionary<string, int> blockNameToId;
        private Dictionary<int, string> blockIdToName;
        private bool mappingFailureLogged;

        private ConstructionManagerGameAccess(
            IModApi api,
            Type conditionDictionaryType,
            Type blockPosValueListType,
            Type vector3iType,
            Type databaseTokenType,
            Type actionLayoutType,
            FieldInfo structureEccField,
            FieldInfo databaseTokenActionLayoutField,
            FieldInfo actionLayoutConditionField,
            MethodInfo conditionOpenImageMethod,
            MethodInfo positionsOfTypeMethod,
            FieldInfo vectorXField,
            FieldInfo vectorYField,
            FieldInfo vectorZField,
            bool conditionContractAvailable)
        {
            this.api = api;
            this.conditionDictionaryType = conditionDictionaryType;
            this.blockPosValueListType = blockPosValueListType;
            this.vector3iType = vector3iType;
            this.databaseTokenType = databaseTokenType;
            this.actionLayoutType = actionLayoutType;
            this.structureEccField = structureEccField;
            this.databaseTokenActionLayoutField = databaseTokenActionLayoutField;
            this.actionLayoutConditionField = actionLayoutConditionField;
            this.conditionOpenImageMethod = conditionOpenImageMethod;
            this.positionsOfTypeMethod = positionsOfTypeMethod;
            this.vectorXField = vectorXField;
            this.vectorYField = vectorYField;
            this.vectorZField = vectorZField;
            this.conditionContractAvailable = conditionContractAvailable;
        }

        internal static ConstructionManagerGameAccess Create(Assembly game, IModApi api, out string failure)
        {
            failure = null;
            try
            {
                Type conditionType = game.GetType("Assembly-CSharp.ConditionDictionary", false);
                Type positionListType = game.GetType("BlockPosValueList", false);
                Type vectorType = game.GetType("Vector3i", false);
                Type structureBridgeType = game.GetType("Eleon.ModBridge.StructureBridge", false);
                Type dbTokenType = game.GetType("Assembly-CSharp.DatabaseToken", false);
                Type actionLayoutType = game.GetType("Assembly-CSharp.ActionLayout", false);

                if (conditionType == null || positionListType == null || vectorType == null ||
                    structureBridgeType == null || dbTokenType == null || actionLayoutType == null)
                {
                    failure = "ConditionDictionary/BlockPosValueList/Vector3i/StructureBridge/DatabaseToken/ActionLayout missing";
                    return null;
                }

                FieldInfo structureEccField = FindFieldInHierarchy(structureBridgeType,
                    f => f.FieldType == dbTokenType);
                FieldInfo dbTokenActionLayoutField = FindFieldInHierarchy(dbTokenType,
                    f => f.FieldType == actionLayoutType);
                FieldInfo actionConditionField = FindFieldInHierarchy(actionLayoutType,
                    f => f.FieldType == conditionType);

                MethodInfo openImage = conditionType.GetMethod(
                    "OpenImage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string) }, null);

                MethodInfo positionsOfType = positionListType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "GetAllBlockPositonsOfType" &&
                                         m.GetParameters().Length == 1 &&
                                         m.GetParameters()[0].ParameterType == typeof(int));

                FieldInfo x = vectorType.GetField("x", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo y = vectorType.GetField("y", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo z = vectorType.GetField("z", BindingFlags.Instance | BindingFlags.Public);

                bool available = structureEccField != null &&
                                 dbTokenActionLayoutField != null &&
                                 actionConditionField != null &&
                                 openImage != null &&
                                 positionsOfType != null &&
                                 x != null && y != null && z != null;

                if (!available)
                    failure = "native condition contract incomplete";

                return new ConstructionManagerGameAccess(api, conditionType, positionListType,
                    vectorType, dbTokenType, actionLayoutType, structureEccField,
                    dbTokenActionLayoutField, actionConditionField, openImage,
                    positionsOfType, x, y, z, available);
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
                return null;
            }
        }

        internal bool ConditionContractAvailable
        {
            get { return conditionContractAvailable; }
        }

        internal int ResolveBlockId(string blockName)
        {
            EnsureBlockMapping();
            int id;
            return blockNameToId != null && blockNameToId.TryGetValue(blockName, out id) ? id : -1;
        }

        internal string ResolveBlockName(int blockId)
        {
            EnsureBlockMapping();
            string name;
            return blockIdToName != null && blockIdToName.TryGetValue(blockId, out name) ? name : null;
        }

        internal Dictionary<string, int> GetBlockMapping()
        {
            EnsureBlockMapping();
            return blockNameToId;
        }

        internal void EnsureBlockMapping()
        {
            if (blockNameToId != null) return;

            try
            {
                Dictionary<string, int> mapping = api.Application == null
                    ? null
                    : api.Application.GetBlockAndItemMapping();
                if (mapping == null)
                {
                    if (!mappingFailureLogged)
                    {
                        mappingFailureLogged = true;
                        ConstructionManagerDiagnostics.Log(api,
                            "Block/item mapping not ready; name-based detection will be unavailable until the next retry.");
                    }
                    return;
                }

                var byName = new Dictionary<string, int>(StringComparer.Ordinal);
                var byId = new Dictionary<int, string>();
                foreach (KeyValuePair<string, int> entry in mapping)
                {
                    if (string.IsNullOrEmpty(entry.Key)) continue;
                    byName[entry.Key] = entry.Value;
                    if (!byId.ContainsKey(entry.Value))
                        byId.Add(entry.Value, entry.Key);
                }

                blockIdToName = byId;
                blockNameToId = byName;
            }
            catch (Exception error)
            {
                if (!mappingFailureLogged)
                {
                    mappingFailureLogged = true;
                    ConstructionManagerDiagnostics.Log(api,
                        "Block/item mapping unavailable: " + error.GetType().Name + ": " + error.Message);
                }
            }
        }

        internal object GetConditionDictionary(IStructure structure)
        {
            if (!conditionContractAvailable || structure == null) return null;

            try
            {
                object ecc = structureEccField.GetValue(structure);
                if (ecc == null) return null;

                object actionLayout = databaseTokenActionLayoutField.GetValue(ecc);
                if (actionLayout == null) return null;

                object condition = actionLayoutConditionField.GetValue(actionLayout);
                if (condition != null)
                    DeviceBucketPatch.Ensure(condition);
                return condition;
            }
            catch
            {
                return null;
            }
        }

        internal List<VectorInt3> GetBucketPositions(IStructure structure, string bucketName, int blockId)
        {
            var result = new List<VectorInt3>();
            if (structure == null || blockId <= 0 || !conditionContractAvailable)
                return result;

            try
            {
                object condition = GetConditionDictionary(structure);
                if (condition == null) return result;

                object bucket = conditionOpenImageMethod.Invoke(condition, new object[] { bucketName });
                if (bucket == null) return result;

                object positions = positionsOfTypeMethod.Invoke(bucket, new object[] { blockId });
                IEnumerable enumerable = positions as IEnumerable;
                if (enumerable == null) return result;

                foreach (object item in enumerable)
                {
                    int x, y, z;
                    if (TryReadNativePosition(item, out x, out y, out z))
                        result.Add(new VectorInt3(x, y, z));
                }
            }
            catch
            {
                // Callers fall back to the public custom-device position list.
            }

            return result;
        }

        internal List<VectorInt3> GetCustomDevicePositions(IStructure structure, string blockName)
        {
            try
            {
                return structure == null
                    ? new List<VectorInt3>()
                    : structure.GetDevicePositions(blockName) ?? new List<VectorInt3>();
            }
            catch
            {
                return new List<VectorInt3>();
            }
        }

        private bool TryReadNativePosition(object position, out int x, out int y, out int z)
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

        internal static FieldInfo FindFieldInHierarchy(Type type, Func<FieldInfo, bool> predicate)
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
    }
}
