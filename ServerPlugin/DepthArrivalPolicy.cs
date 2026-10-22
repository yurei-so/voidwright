using System;

namespace Voidwright.ServerPlugin
{
    /// <summary>
    /// Converts a world-space arrival radius into the ground-plane radius a
    /// wheeled controller can actually close, and verifies the full 3D radius.
    /// </summary>
    internal static class DepthArrivalPolicy
    {
        public static bool IsNewHostSample(long hostUpdateTick, long lastHostUpdateTick)
            => hostUpdateTick >= 0 && hostUpdateTick > lastHostUpdateTick;

        public static bool IsStableArrival(
            double distanceMeters,
            double speedMetersPerSecond,
            double arrivalRadiusMeters,
            double maximumSpeedMetersPerSecond,
            bool actuatorsNeutral,
            bool freshBoundHeartbeat)
        {
            if (!Finite(distanceMeters) || !Finite(speedMetersPerSecond) ||
                !Finite(arrivalRadiusMeters) || !Finite(maximumSpeedMetersPerSecond) ||
                distanceMeters < 0 || speedMetersPerSecond < 0 || arrivalRadiusMeters <= 0 ||
                maximumSpeedMetersPerSecond <= 0 || !actuatorsNeutral || !freshBoundHeartbeat)
                return false;

            var stableSpeedLimit = Math.Min(0.15, maximumSpeedMetersPerSecond);
            return distanceMeters <= arrivalRadiusMeters && speedMetersPerSecond <= stableSpeedLimit;
        }

        public static bool TryGetPlanarRadius(
            double worldRadiusMeters,
            double verticalErrorMeters,
            out double planarRadiusMeters)
        {
            planarRadiusMeters = 0;
            if (!Finite(worldRadiusMeters) || !Finite(verticalErrorMeters) || worldRadiusMeters < 0)
                return false;

            var vertical = Math.Abs(verticalErrorMeters);
            if (vertical > worldRadiusMeters) return false;

            var remainingSquared = (worldRadiusMeters - vertical) * (worldRadiusMeters + vertical);
            planarRadiusMeters = Math.Sqrt(Math.Max(0, remainingSquared));
            return true;
        }

        public static bool IsWithinWorldRadius(
            double planarDistanceMeters,
            double verticalErrorMeters,
            double worldRadiusMeters)
        {
            if (!Finite(planarDistanceMeters) || !Finite(verticalErrorMeters) ||
                !Finite(worldRadiusMeters) || planarDistanceMeters < 0 || worldRadiusMeters < 0)
                return false;

            var vertical = Math.Abs(verticalErrorMeters);
            return vertical <= worldRadiusMeters &&
                   planarDistanceMeters <= worldRadiusMeters &&
                   (planarDistanceMeters * planarDistanceMeters + vertical * vertical) <=
                   worldRadiusMeters * worldRadiusMeters;
        }

        private static bool Finite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
