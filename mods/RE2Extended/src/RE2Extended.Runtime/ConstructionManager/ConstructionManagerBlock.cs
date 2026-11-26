using System;
using System.Collections.Generic;
using Eleon.Modding;

namespace RE2Extended.Runtime.ConstructionManager
{
    internal sealed class ConstructionManagerBlock
    {
        internal const string BlockName = "ConstructionManager";
        internal const string InventoryGroupName = "ConstructionManagerBlocks";
        internal const string DisplayName = "Construction Manager";

        private readonly IModApi api;
        private readonly ConstructionManagerGameAccess access;

        internal ConstructionManagerBlock(IModApi api, ConstructionManagerGameAccess access)
        {
            this.api = api;
            this.access = access;
        }

        internal List<VectorInt3> GetPositions(IStructure structure)
        {
            if (structure == null) return new List<VectorInt3>();

            int blockId = access.ResolveBlockId(BlockName);
            if (blockId > 0)
            {
                List<VectorInt3> native = access.GetBucketPositions(
                    structure, DeviceBucketPatch.BucketName, blockId);
                if (native.Count > 0) return native;
            }

            return access.GetCustomDevicePositions(structure, BlockName);
        }

        internal bool ExistsOn(IStructure structure)
        {
            return GetPositions(structure).Count > 0;
        }

        internal bool IsManagerUse(object eventArgument)
        {
            string text = eventArgument as string;
            if (string.IsNullOrEmpty(text)) return false;

            return string.Equals(text, BlockName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, DisplayName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, InventoryGroupName, StringComparison.OrdinalIgnoreCase);
        }

        internal void LogDiagnostics(IStructure structure)
        {
            int blockId = access.ResolveBlockId(BlockName);
            ConstructionManagerDiagnostics.Log(api,
                "Manager block mapping: name=" + BlockName + " id=" + blockId);
            if (structure == null)
            {
                ConstructionManagerDiagnostics.Log(api, "Manager block lookup skipped: no local structure.");
                return;
            }

            List<VectorInt3> positions = GetPositions(structure);
            ConstructionManagerDiagnostics.Log(api,
                "Manager block positions on structure " + structure.Id + ": " + positions.Count);
            for (int i = 0; i < positions.Count && i < 8; i++)
                ConstructionManagerDiagnostics.Log(api,
                    "  manager[" + i + "]=" + ConstructionManagerDiagnostics.DescribePosition(positions[i]));
        }
    }
}
