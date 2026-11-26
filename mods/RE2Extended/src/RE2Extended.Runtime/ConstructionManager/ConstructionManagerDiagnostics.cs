using System;

namespace RE2Extended.Runtime.ConstructionManager
{
    internal static class ConstructionManagerDiagnostics
    {
        private const string Prefix = "[RE2Extended] [ConstructionManager] ";

        internal static void Log(Eleon.Modding.IModApi api, string text)
        {
            try
            {
                if (api != null) api.Log(Prefix + text);
            }
            catch
            {
                // Diagnostics must never break a game callback.
            }
        }

        internal static void Warning(Eleon.Modding.IModApi api, string text)
        {
            try
            {
                if (api != null) api.LogWarning(Prefix + text);
            }
            catch
            {
                // Diagnostics must never break a game callback.
            }
        }

        internal static string Describe(object value)
        {
            if (value == null) return "<null>";
            try
            {
                return value.GetType().FullName + " = " + value;
            }
            catch
            {
                return value.GetType().FullName;
            }
        }

        internal static string DescribePosition(Eleon.Modding.VectorInt3 position)
        {
            return position.x + "," + position.y + "," + position.z;
        }
    }
}
