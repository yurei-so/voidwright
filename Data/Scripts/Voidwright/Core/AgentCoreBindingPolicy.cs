using System;

namespace Voidwright.Core
{
    /// <summary>Fail-closed check for a fresh Agent Core bound to one anchor grid.</summary>
    public static class AgentCoreBindingPolicy
    {
        public static bool IsValid(long expectedCoreId, long observedCoreId,
            long expectedAnchorGridId, long observedRootGridId,
            long capturedTick, long currentTick, long maximumAgeTicks,
            bool discoveryComplete)
        {
            return expectedCoreId > 0 && expectedCoreId == observedCoreId &&
                expectedAnchorGridId > 0 && expectedAnchorGridId == observedRootGridId &&
                capturedTick >= 0 && currentTick >= capturedTick && maximumAgeTicks >= 0 &&
                currentTick - capturedTick <= maximumAgeTicks && discoveryComplete;
        }
    }
}
