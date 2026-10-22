using System;

namespace Voidwright.Core
{
    public enum LocalVehicleTestObstacleStatus
    {
        Clear,
        Obstacle,
        Unknown,
        InvalidScan
    }

    public struct LocalVehicleTestObstacleDecision
    {
        public LocalVehicleTestObstacleStatus Status;
        public double NearestClearanceMeters;
        public int RayCount;
    }

    /// <summary>
    /// Fail-closed decision for the local-test forward swept-volume ray fan.
    /// This policy only authorizes continuing a short test segment; it does not
    /// classify terrain as traversable or authorize a route.
    /// </summary>
    public static class LocalVehicleTestObstacleStopPolicy
    {
        public static LocalVehicleTestObstacleDecision Evaluate(
            double[] clearancesMeters, double maximumRangeMeters,
            double stopClearanceMeters, bool scanComplete)
        {
            var invalid = new LocalVehicleTestObstacleDecision
            {
                Status = LocalVehicleTestObstacleStatus.InvalidScan,
                NearestClearanceMeters = 0,
                RayCount = clearancesMeters == null ? 0 : clearancesMeters.Length
            };
            if (!Finite(maximumRangeMeters) || maximumRangeMeters <= 0 ||
                !Finite(stopClearanceMeters) || stopClearanceMeters <= 0 ||
                stopClearanceMeters > maximumRangeMeters || clearancesMeters == null ||
                clearancesMeters.Length < 3 || clearancesMeters.Length > 27)
                return invalid;

            var nearest = maximumRangeMeters;
            foreach (var clearance in clearancesMeters)
            {
                if (!Finite(clearance) || clearance < 0 || clearance > maximumRangeMeters)
                    return invalid;
                nearest = Math.Min(nearest, clearance);
            }
            if (!scanComplete)
            {
                invalid.Status = LocalVehicleTestObstacleStatus.Unknown;
                invalid.NearestClearanceMeters = nearest;
                return invalid;
            }
            return new LocalVehicleTestObstacleDecision
            {
                Status = nearest < stopClearanceMeters
                    ? LocalVehicleTestObstacleStatus.Obstacle
                    : LocalVehicleTestObstacleStatus.Clear,
                NearestClearanceMeters = nearest,
                RayCount = clearancesMeters.Length
            };
        }

        public static bool MayContinue(LocalVehicleTestObstacleDecision decision)
            => decision.Status == LocalVehicleTestObstacleStatus.Clear;

        private static bool Finite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
