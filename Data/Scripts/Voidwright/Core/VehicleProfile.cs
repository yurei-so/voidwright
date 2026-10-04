using System;

namespace Voidwright.Core
{
    public enum MobilityClass { Land, Air }

    public sealed class VehicleProfile
    {
        public MobilityClass Mobility { get; private set; }
        public int HalfWidthCells { get; private set; }
        public int HalfHeightCells { get; private set; }
        public int HalfLengthCells { get; private set; }
        public double MaximumSlope { get; private set; }
        public int MaximumStepCells { get; private set; }
        public double TurnPenalty { get; private set; }

        public VehicleProfile(
            MobilityClass mobility,
            int halfWidthCells,
            int halfHeightCells,
            int halfLengthCells,
            double maximumSlope,
            int maximumStepCells,
            double turnPenalty)
        {
            Mobility = mobility;
            HalfWidthCells = Math.Max(0, halfWidthCells);
            HalfHeightCells = Math.Max(0, halfHeightCells);
            HalfLengthCells = Math.Max(0, halfLengthCells);
            MaximumSlope = Math.Max(0, Math.Min(1, maximumSlope));
            MaximumStepCells = Math.Max(0, maximumStepCells);
            TurnPenalty = Math.Max(0, turnPenalty);
        }
    }
}
