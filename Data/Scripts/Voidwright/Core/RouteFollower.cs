using System;

namespace Voidwright.Core
{
    public enum GuidanceStatus { Tracking, Arrived, Invalid }

    public struct GuidanceCommand
    {
        public readonly GuidanceStatus Status;
        public readonly double Forward;
        public readonly double Right;
        public readonly double Up;
        public readonly double Yaw;

        public GuidanceCommand(GuidanceStatus status, double forward, double right, double up, double yaw)
        {
            Status = status;
            Forward = Clamp(forward);
            Right = Clamp(right);
            Up = Clamp(up);
            Yaw = Clamp(yaw);
        }

        private static double Clamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (value < -1) return -1;
            if (value > 1) return 1;
            return value;
        }
    }

    /// <summary>
    /// Converts a target expressed in vehicle-local coordinates into a bounded,
    /// actuator-neutral guidance command. Game-facing adapters remain responsible
    /// for applying this intent to wheels or thrusters.
    /// </summary>
    public sealed class RouteFollower
    {
        public GuidanceCommand FollowLocalTarget(
            MobilityClass mobility,
            double rightMeters,
            double upMeters,
            double forwardMeters,
            double forwardSpeed,
            double maximumSpeed,
            double arrivalRadius)
        {
            if (!Finite(rightMeters) || !Finite(upMeters) || !Finite(forwardMeters) ||
                !Finite(forwardSpeed) || !Finite(maximumSpeed) || !Finite(arrivalRadius) ||
                maximumSpeed <= 0 || arrivalRadius < 0)
                return new GuidanceCommand(GuidanceStatus.Invalid, 0, 0, 0, 0);

            var distance = Math.Sqrt((rightMeters * rightMeters) +
                                     (upMeters * upMeters) +
                                     (forwardMeters * forwardMeters));
            if (distance <= arrivalRadius)
                return new GuidanceCommand(GuidanceStatus.Arrived, 0, 0, 0, 0);

            if (mobility == MobilityClass.Air)
            {
                var speedScale = SpeedScale(forwardSpeed, maximumSpeed);
                return new GuidanceCommand(
                    GuidanceStatus.Tracking,
                    (forwardMeters / distance) * speedScale,
                    (rightMeters / distance) * speedScale,
                    (upMeters / distance) * speedScale,
                    Math.Atan2(rightMeters, forwardMeters) / Math.PI);
            }

            var heading = Math.Atan2(rightMeters, forwardMeters);
            var yaw = Clamp(heading / (Math.PI / 2));
            var alignment = Math.Max(0, 1 - Math.Abs(yaw));
            var approach = Math.Min(1, distance / Math.Max(arrivalRadius * 4, 1));
            var throttle = alignment * approach * SpeedScale(forwardSpeed, maximumSpeed);
            return new GuidanceCommand(GuidanceStatus.Tracking, throttle, 0, 0, yaw);
        }

        private static double SpeedScale(double speed, double maximumSpeed)
        {
            if (speed <= 0) return 1;
            return Clamp((maximumSpeed - speed) / maximumSpeed);
        }

        private static bool Finite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        private static double Clamp(double value)
        {
            if (value < -1) return -1;
            if (value > 1) return 1;
            return value;
        }
    }
}
