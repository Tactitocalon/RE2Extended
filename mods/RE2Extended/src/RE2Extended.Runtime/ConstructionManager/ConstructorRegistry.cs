using System;
using System.Text;

namespace RE2Extended.Runtime.ConstructionManager
{
    internal sealed class ConstructorDefinition
    {
        internal readonly string BlockName;
        internal readonly string EcfClass;

        internal ConstructorDefinition(string blockName, string ecfClass)
        {
            BlockName = blockName;
            EcfClass = ecfClass;
        }
    }

    internal static class ConstructorRegistry
    {
        // First-release scope: a fixed whitelist, logged once at startup.
        internal static readonly ConstructorDefinition[] All =
        {
            new ConstructorDefinition("ConstructorAlienT0", "ConstructorT0"),
            new ConstructorDefinition("ConstructorAlienT1", "ConstructorBigV2"),
            new ConstructorDefinition("ConstructorAlienT2", "ConstructorRecursive"),
            new ConstructorDefinition("ConstructorT0", "ConstructorT0"),
            new ConstructorDefinition("ConstructorT1V2", "ConstructorBigV2"),
            new ConstructorDefinition("ConstructorT2", "ConstructorRecursive"),
            new ConstructorDefinition("ConstructorT3", "ConstructorSmallV2")
        };

        internal static void LogWhitelist(Eleon.Modding.IModApi api)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < All.Length; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(All[i].BlockName).Append(" (").Append(All[i].EcfClass).Append(')');
            }
            ConstructionManagerDiagnostics.Log(api,
                "Constructor whitelist (" + All.Length + "): " + builder);
        }
    }
}
