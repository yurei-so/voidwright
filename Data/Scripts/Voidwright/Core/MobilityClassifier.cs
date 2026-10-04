namespace Voidwright.Core
{
    public enum MobilityClassification { Unknown, Land, Air, Hybrid }

    public static class MobilityClassifier
    {
        public static MobilityClassification Classify(int wheelSuspensions, int thrusters)
        {
            if (wheelSuspensions < 0) wheelSuspensions = 0;
            if (thrusters < 0) thrusters = 0;
            if (wheelSuspensions >= 2 && thrusters > 0) return MobilityClassification.Hybrid;
            if (wheelSuspensions >= 2) return MobilityClassification.Land;
            if (thrusters > 0) return MobilityClassification.Air;
            return MobilityClassification.Unknown;
        }
    }
}
