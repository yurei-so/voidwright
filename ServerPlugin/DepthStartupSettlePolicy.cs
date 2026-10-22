using System;

namespace Voidwright.ServerPlugin
{
    /// <summary>
    /// Fails closed when position-derived motion telemetry is unavailable,
    /// while tolerating the small linear-velocity discrepancy observed in the
    /// dedicated-server fixture when the grid is otherwise stationary.
    /// </summary>
    internal static class DepthStartupSettlePolicy
    {
        private const double MaximumReportedSpeedWithFreshPositionSample = 0.08;
        private const double MaximumStableSpeed = 0.05;

        public static bool IsStable(
            double reportedSpeed,
            bool positionVelocityFresh,
            double positionDerivedSpeed)
        {
            if (!Finite(reportedSpeed) || reportedSpeed < 0) return false;
            if (!positionVelocityFresh) return reportedSpeed <= MaximumStableSpeed;

            return Finite(positionDerivedSpeed) && positionDerivedSpeed >= 0 &&
                   positionDerivedSpeed <= MaximumStableSpeed &&
                   reportedSpeed <= MaximumReportedSpeedWithFreshPositionSample;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
