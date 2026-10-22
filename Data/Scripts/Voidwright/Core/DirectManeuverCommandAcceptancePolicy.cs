using System;

namespace Voidwright.Core
{
    /// <summary>Host-side acceptance rules for the bounded, actuator-neutral hold command.</summary>
    public static class DirectManeuverCommandAcceptancePolicy
    {
        public static bool IsZeroOnlyStationaryHold(DirectManeuverPlan plan,
            string runId, string fixtureId, string operationId, long gridId,
            long controllerId, long agentCoreId, string compactSegments,
            int commandTimeoutTicks)
        {
            string validationReason;
            if (plan == null || !plan.IsValid(out validationReason) || !plan.IsZeroActuationOnly ||
                plan.Segments.Count != 1) return false;

            var segment = plan.Segments[0];
            return segment.Kind == DirectManeuverKind.StationaryHold &&
                segment.Value == 0 && segment.MaximumSpeedMetersPerSecond == 0 &&
                segment.TimeoutTicks > 0 &&
                commandTimeoutTicks == segment.TimeoutTicks + DirectManeuverPlan.MaximumStopConfirmationTicks &&
                plan.RunId == runId && plan.FixtureId == fixtureId && plan.OperationId == operationId &&
                plan.GridId == gridId && plan.ControllerId == controllerId && plan.AgentCoreId == agentCoreId &&
                string.Equals(plan.ToCompactSegments(), compactSegments, StringComparison.Ordinal);
        }

        public static bool IsDiscoveryFresh(DateTime capturedUtc, DateTime nowUtc, TimeSpan maximumAge)
        {
            var captured = capturedUtc.ToUniversalTime();
            var now = nowUtc.ToUniversalTime();
            return maximumAge > TimeSpan.Zero && captured <= now && now - captured <= maximumAge;
        }

        public static bool MatchesDiscoveryIdentity(string observedRunId, long observedTick,
            long observedGridId, long observedControllerId, long observedAgentCoreId,
            string expectedRunId, long expectedTick, long expectedGridId,
            long expectedControllerId, long expectedAgentCoreId)
        {
            return !string.IsNullOrWhiteSpace(observedRunId) &&
                string.Equals(observedRunId, expectedRunId, StringComparison.Ordinal) &&
                observedTick == expectedTick && observedGridId == expectedGridId &&
                observedControllerId == expectedControllerId && observedAgentCoreId == expectedAgentCoreId;
        }

        public static bool MatchesFreshDiscoveryAdvance(string observedRunId, long observedTick,
            string observedFingerprint, long observedGridId, long observedControllerId,
            long observedAgentCoreId, string expectedRunId, long expectedTick,
            string expectedFingerprint, long expectedGridId, long expectedControllerId,
            long expectedAgentCoreId)
        {
            return !string.IsNullOrWhiteSpace(observedRunId) &&
                string.Equals(observedRunId, expectedRunId, StringComparison.Ordinal) &&
                observedTick >= expectedTick &&
                (observedTick != expectedTick || string.Equals(observedFingerprint,
                    expectedFingerprint, StringComparison.Ordinal)) &&
                observedGridId == expectedGridId && observedControllerId == expectedControllerId &&
                observedAgentCoreId == expectedAgentCoreId;
        }
    }
}
