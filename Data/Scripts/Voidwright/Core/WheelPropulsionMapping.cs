using System;

namespace Voidwright.Core
{
    /// <summary>Resolves wheel propulsion sign from the controller/grid frame and wheel inversion.</summary>
    public static class WheelPropulsionMapping
    {
        /// <summary>
        /// Resolves the direct override sign that reproduces native wheel
        /// propulsion for a controller-frame input. Native input first applies
        /// InvertPropulsion, then RevolveInvert in MyMotorSuspension.Accelerate;
        /// a nonzero direct override bypasses RevolveInvert, so both operations
        /// must be folded into the per-wheel override sign here.
        /// </summary>
        public static double ResolveDirectOverride(double controllerInput,
            bool revolveInvert, bool invertPropulsion)
        {
            if (double.IsNaN(controllerInput) || double.IsInfinity(controllerInput)) return 0;
            if (Math.Abs(controllerInput) < 0.000001) return 0;

            var nativeInputForward = controllerInput > 0;
            var nativeAccelerateForward = nativeInputForward != invertPropulsion;
            var nativeWheelUp = revolveInvert == nativeAccelerateForward;
            var overrideAccelerateForward = nativeWheelUp;
            var overridePositive = overrideAccelerateForward != invertPropulsion;
            return Math.Abs(controllerInput) * (overridePositive ? 1.0 : -1.0);
        }

        public static double ResolvePolarity(double controllerToGridForwardAlignment,
            bool invertPropulsion)
        {
            if (double.IsNaN(controllerToGridForwardAlignment) ||
                double.IsInfinity(controllerToGridForwardAlignment) ||
                Math.Abs(controllerToGridForwardAlignment) < 0.5)
                return 0;

            var frameSign = controllerToGridForwardAlignment < 0 ? -1.0 : 1.0;
            return invertPropulsion ? -frameSign : frameSign;
        }
    }

    /// <summary>Maps a requested visual steering direction through the game suspension's direct-override inversion.</summary>
    public static class WheelSteeringMapping
    {
        /// <summary>
        /// MyMotorSuspension.Steer flips nonzero SteeringOverride when InvertSteer is false.
        /// Fold that installed-engine behavior into the direct override so positive always
        /// means the requested positive wheel angle.
        /// </summary>
        public static double ResolveDirectOverride(double requestedSteering, bool invertSteer)
        {
            if (double.IsNaN(requestedSteering) || double.IsInfinity(requestedSteering)) return 0;
            return invertSteer ? requestedSteering : -requestedSteering;
        }
    }
}
