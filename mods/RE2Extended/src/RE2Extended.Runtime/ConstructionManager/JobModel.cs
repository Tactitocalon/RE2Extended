using System;
using System.Collections.Generic;

namespace RE2Extended.Runtime.ConstructionManager
{
    internal enum ConstructionJobStatus
    {
        Pending,
        Started
    }

    internal sealed class ConstructionJobItem
    {
        internal int ItemType;
        internal string InternalName;
        internal string ItemName;
        internal string IconName;
        internal int Count;

        internal ConstructionJobItem(int itemType, string itemName, int count)
        {
            ItemType = itemType;
            InternalName = string.IsNullOrEmpty(itemName) ? "item-" + itemType : itemName;
            ItemName = InternalName;
            IconName = InternalName;
            Count = count < 1 ? 1 : count;
        }

        internal ConstructionJobItem(int itemType, string internalName, string displayName,
            string iconName, int count)
        {
            ItemType = itemType;
            InternalName = string.IsNullOrEmpty(internalName) ? "item-" + itemType : internalName;
            ItemName = string.IsNullOrEmpty(displayName) ? InternalName : displayName;
            IconName = string.IsNullOrEmpty(iconName) ? InternalName : iconName;
            Count = count < 1 ? 1 : count;
        }

        internal ConstructionJobItem Clone()
        {
            return new ConstructionJobItem(ItemType, InternalName, ItemName, IconName, Count);
        }
    }

    internal sealed class ConstructionJob
    {
        internal string JobId;
        internal string Name;
        internal string Type;
        internal ConstructionJobStatus Status;
        internal readonly List<ConstructionJobItem> Items = new List<ConstructionJobItem>();

        internal ConstructionJob(string jobId, string name)
        {
            JobId = jobId;
            Name = string.IsNullOrEmpty(name) ? "Unnamed job" : name;
            Type = "Once";
            Status = ConstructionJobStatus.Pending;
        }

        internal string StatusText
        {
            get { return Status == ConstructionJobStatus.Started ? "Started" : "Pending"; }
        }

        internal int TotalItemCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Items.Count; i++) total += Items[i].Count;
                return total;
            }
        }
    }

    internal sealed class ConstructionManagerSession
    {
        internal readonly List<ConstructionJob> Jobs = new List<ConstructionJob>();
        private int nextJobNumber = 1;

        internal ConstructionJob CreateJob()
        {
            var job = new ConstructionJob(Guid.NewGuid().ToString("N"), "Job " + nextJobNumber);
            nextJobNumber++;
            Jobs.Add(job);
            return job;
        }

        internal void RemoveJob(ConstructionJob job)
        {
            if (job != null) Jobs.Remove(job);
        }
    }
}
