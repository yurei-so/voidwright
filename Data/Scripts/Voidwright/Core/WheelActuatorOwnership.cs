using System;

namespace Voidwright.Core
{
    /// <summary>
    /// Describes which wheel properties still equal the ordinary adapter's last
    /// attempted values. A stop may change only those properties; changed or
    /// non-finite values are treated as externally owned and left untouched.
    /// Brake configuration is intentionally excluded: the mod-facing Brake
    /// member is not a live brake input in the installed game implementation.
    /// </summary>
    public sealed class WheelActuatorStopPlan
    {
        public const float MatchTolerance = 0.0001f;

        public readonly bool ClearSteering;
        public readonly bool ClearPropulsion;
        public readonly bool OwnsSteering;
        public readonly bool OwnsPropulsion;

        private WheelActuatorStopPlan(bool ownsSteering, bool ownsPropulsion,
            bool clearSteering, bool clearPropulsion)
        {
            OwnsSteering = ownsSteering;
            OwnsPropulsion = ownsPropulsion;
            ClearSteering = clearSteering;
            ClearPropulsion = clearPropulsion;
        }

        public bool HasExternalChanges
        {
            get
            {
                return (OwnsSteering && !ClearSteering) || (OwnsPropulsion && !ClearPropulsion);
            }
        }

        public static WheelActuatorStopPlan Create(float currentSteering, float expectedSteering,
            float currentPropulsion, float expectedPropulsion,
            bool ownsSteering = true, bool ownsPropulsion = true)
        {
            return new WheelActuatorStopPlan(ownsSteering, ownsPropulsion,
                ownsSteering && Matches(currentSteering, expectedSteering),
                ownsPropulsion && Matches(currentPropulsion, expectedPropulsion));
        }

        public static bool IsOwnedValue(float current, float expected) => Matches(current, expected);

        public static bool IsNeutral(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= MatchTolerance;
        }

        public static bool CanAdopt(float steering, float propulsion)
        {
            return IsNeutral(steering) && IsNeutral(propulsion);
        }

        private static bool Matches(float current, float expected)
        {
            return !float.IsNaN(current) && !float.IsInfinity(current) &&
                   !float.IsNaN(expected) && !float.IsInfinity(expected) &&
                   Math.Abs(current - expected) <= MatchTolerance;
        }
    }
}
