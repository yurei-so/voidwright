using System;

namespace Voidwright.Core
{
    public enum DepthAvoidanceStatus
    {
        Clear,
        Avoid,
        TargetOutOfView,
        NoClearCorridor,
        InvalidScan
    }

    public struct DepthAvoidanceDecision
    {
        public DepthAvoidanceStatus Status;
        public double HeadingRadians;
        public double MinimumClearanceMeters;
        public double DirectClearanceMeters;
        public int ConsecutiveClearFrames;
    }

    /// <summary>
    /// Small deterministic policy that turns a horizontal front-camera ray fan
    /// into a safe steering heading. It does not own actuation or simulator state.
    /// </summary>
    public static class DepthAvoidancePolicy
    {
        private const float MaximumWheelPower = 0.15f;
        private const double MaximumDriveDuty = 0.4;
        private const double DetourReleaseClearanceMarginMeters = 1.2;
        private const int DetourReleaseClearFramesRequired = 3;

        public static bool IsNavigationPermitted(DepthAvoidanceStatus status)
            => status == DepthAvoidanceStatus.Clear || status == DepthAvoidanceStatus.Avoid;

        public static float LimitWheelPower(float savedPower)
        {
            if (float.IsNaN(savedPower) || float.IsInfinity(savedPower) || savedPower <= 0)
                return 0;
            return Math.Min(savedPower, MaximumWheelPower);
        }

        public static double LimitDriveDuty(double requestedDuty)
        {
            if (!Finite(requestedDuty) || requestedDuty <= 0) return 0;
            return Math.Min(requestedDuty, MaximumDriveDuty);
        }

        public static double SignedDriveDuty(double requestedDuty, bool reversing)
        {
            var duty = LimitDriveDuty(requestedDuty);
            return reversing ? -duty : duty;
        }

        public static double SteeringCommand(double headingRadians, bool reversing)
        {
            if (!Finite(headingRadians)) return 0;
            var command = headingRadians / (Math.PI / 6.0);
            command = Math.Max(-1.0, Math.Min(1.0, command));
            // In the V4 Red Truck's measured forward response, the signed
            // heading command reduces heading error; reversing swaps the map.
            return reversing ? -command : command;
        }

        public static DepthAvoidanceDecision SelectHeading(
            double targetHeadingRadians,
            double[] beamAnglesRadians,
            double[] beamClearancesMeters,
            double maximumRangeMeters,
            double requiredClearanceMeters,
            double previousHeadingRadians,
            int previousDirectClearFrames = 0)
        {
            var invalid = new DepthAvoidanceDecision
            {
                Status = DepthAvoidanceStatus.InvalidScan,
                HeadingRadians = 0,
                MinimumClearanceMeters = 0,
                DirectClearanceMeters = 0,
                ConsecutiveClearFrames = 0
            };
            if (!Finite(targetHeadingRadians) || !Finite(previousHeadingRadians) ||
                !Finite(maximumRangeMeters) || maximumRangeMeters <= 0 ||
                !Finite(requiredClearanceMeters) || requiredClearanceMeters <= 0 ||
                beamAnglesRadians == null || beamClearancesMeters == null ||
                beamAnglesRadians.Length != beamClearancesMeters.Length ||
                beamAnglesRadians.Length < 3 || beamAnglesRadians.Length > 41)
                return invalid;

            for (var index = 0; index < beamAnglesRadians.Length; index++)
            {
                if (!Finite(beamAnglesRadians[index]) || !Finite(beamClearancesMeters[index]) ||
                    beamClearancesMeters[index] < 0 || beamClearancesMeters[index] > maximumRangeMeters ||
                    (index > 0 && beamAnglesRadians[index] <= beamAnglesRadians[index - 1]))
                    return invalid;
            }

            var firstAngle = beamAnglesRadians[0];
            var lastAngle = beamAnglesRadians[beamAnglesRadians.Length - 1];
            if (targetHeadingRadians < firstAngle || targetHeadingRadians > lastAngle)
            {
                return new DepthAvoidanceDecision
                {
                    Status = DepthAvoidanceStatus.TargetOutOfView,
                    HeadingRadians = 0,
                    MinimumClearanceMeters = 0,
                    DirectClearanceMeters = 0,
                    ConsecutiveClearFrames = 0
                };
            }

            var nearest = 0;
            var nearestError = double.MaxValue;
            for (var index = 1; index < beamAnglesRadians.Length - 1; index++)
            {
                var error = Math.Abs(beamAnglesRadians[index] - targetHeadingRadians);
                if (error < nearestError)
                {
                    nearest = index;
                    nearestError = error;
                }
            }

            var directClearance = Math.Min(beamClearancesMeters[nearest - 1],
                Math.Min(beamClearancesMeters[nearest], beamClearancesMeters[nearest + 1]));
            if (directClearance >= requiredClearanceMeters)
            {
                // A one-frame opening must not release an active detour. On the
                // V4 course, the center fan briefly cleared at 4.765 m, then
                // closed again on the next scan; dropping the detour there
                // caused left/right steering chatter and a stall. Require a
                // wider margin for three consecutive scans, while verifying
                // that the committed detour itself remains safe.
                var clearFrames = Math.Max(0, previousDirectClearFrames) + 1;
                var previousDetourClearance = 0.0;
                var canKeepDetour = Math.Abs(previousHeadingRadians) > 0.08 &&
                    TryGetCorridorClearance(previousHeadingRadians, beamAnglesRadians,
                        beamClearancesMeters, out previousDetourClearance) &&
                    previousDetourClearance >= requiredClearanceMeters;
                var releaseThreshold = requiredClearanceMeters + DetourReleaseClearanceMarginMeters;
                if (canKeepDetour &&
                    (directClearance < releaseThreshold || clearFrames < DetourReleaseClearFramesRequired))
                {
                    return new DepthAvoidanceDecision
                    {
                        Status = DepthAvoidanceStatus.Avoid,
                        HeadingRadians = previousHeadingRadians,
                        MinimumClearanceMeters = previousDetourClearance,
                        DirectClearanceMeters = directClearance,
                        ConsecutiveClearFrames = clearFrames
                    };
                }

                return new DepthAvoidanceDecision
                {
                    Status = DepthAvoidanceStatus.Clear,
                    HeadingRadians = targetHeadingRadians,
                    MinimumClearanceMeters = directClearance,
                    DirectClearanceMeters = directClearance,
                    ConsecutiveClearFrames = 0
                };
            }

            var bestIndex = -1;
            var bestScore = double.MaxValue;
            var bestClearance = 0.0;
            var preferredTurnSign = Math.Abs(previousHeadingRadians) > 0.08
                ? (previousHeadingRadians > 0 ? 1 : -1)
                : Math.Abs(targetHeadingRadians) > 0.08
                    ? (targetHeadingRadians > 0 ? -1 : 1) : 0;
            for (var index = 1; index < beamAnglesRadians.Length - 1; index++)
            {
                var clearance = Math.Min(beamClearancesMeters[index - 1],
                    Math.Min(beamClearancesMeters[index], beamClearancesMeters[index + 1]));
                if (clearance < requiredClearanceMeters) continue;

                var angle = beamAnglesRadians[index];
                if ((preferredTurnSign > 0 && angle <= 0) ||
                    (preferredTurnSign < 0 && angle >= 0)) continue;
                var score = Math.Abs(angle - targetHeadingRadians) +
                            0.15 * Math.Abs(angle) +
                            0.20 * Math.Abs(angle - previousHeadingRadians) -
                            0.50 * (clearance - requiredClearanceMeters);
                if (score < bestScore)
                {
                    bestIndex = index;
                    bestScore = score;
                    bestClearance = clearance;
                }
            }

            if (bestIndex < 0)
            {
                return new DepthAvoidanceDecision
                {
                    Status = DepthAvoidanceStatus.NoClearCorridor,
                    HeadingRadians = 0,
                    MinimumClearanceMeters = directClearance,
                    DirectClearanceMeters = directClearance,
                    ConsecutiveClearFrames = 0
                };
            }

            return new DepthAvoidanceDecision
            {
                Status = DepthAvoidanceStatus.Avoid,
                HeadingRadians = beamAnglesRadians[bestIndex],
                MinimumClearanceMeters = bestClearance,
                DirectClearanceMeters = directClearance,
                ConsecutiveClearFrames = 0
            };
        }

        private static bool TryGetCorridorClearance(
            double headingRadians,
            double[] beamAnglesRadians,
            double[] beamClearancesMeters,
            out double clearanceMeters)
        {
            clearanceMeters = 0;
            if (!Finite(headingRadians) || beamAnglesRadians == null || beamClearancesMeters == null ||
                beamAnglesRadians.Length != beamClearancesMeters.Length ||
                beamAnglesRadians.Length < 3 || headingRadians < beamAnglesRadians[0] ||
                headingRadians > beamAnglesRadians[beamAnglesRadians.Length - 1])
                return false;

            var nearest = -1;
            var nearestError = double.MaxValue;
            for (var index = 1; index < beamAnglesRadians.Length - 1; index++)
            {
                var error = Math.Abs(beamAnglesRadians[index] - headingRadians);
                if (error < nearestError)
                {
                    nearest = index;
                    nearestError = error;
                }
            }
            if (nearest <= 0 || nearest >= beamAnglesRadians.Length - 1) return false;
            clearanceMeters = Math.Min(beamClearancesMeters[nearest - 1],
                Math.Min(beamClearancesMeters[nearest], beamClearancesMeters[nearest + 1]));
            return Finite(clearanceMeters);
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
