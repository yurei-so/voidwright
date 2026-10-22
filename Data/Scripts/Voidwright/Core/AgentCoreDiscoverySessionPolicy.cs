using System;

namespace Voidwright.Core
{
    /// <summary>Ensures a discovery export belongs to this live mod session.</summary>
    public static class AgentCoreDiscoverySessionPolicy
    {
        public static bool IsCurrentSession(string discoveryRunId, string exporterRunId)
        {
            return IsIdentity(discoveryRunId) && IsIdentity(exporterRunId) &&
                string.Equals(discoveryRunId, exporterRunId, StringComparison.Ordinal);
        }

        private static bool IsIdentity(string value)
        {
            if (value == null || value.Length != 32) return false;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }
    }
}
