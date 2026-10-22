namespace Voidwright.Core
{
    /// <summary>Determines when bounded simulation evidence should be persisted.</summary>
    public static class SimulationEvidenceCadence
    {
        public static bool IsPeriodicTick(long simulationTick, long intervalTicks)
        {
            return simulationTick >= 0 && intervalTicks > 0 && simulationTick % intervalTicks == 0;
        }

        public static bool IsDue(long simulationTick, long lastEvidenceTick, long intervalTicks)
        {
            if (intervalTicks <= 0) return false;
            if (lastEvidenceTick == long.MinValue) return true;
            return simulationTick >= lastEvidenceTick && simulationTick - lastEvidenceTick >= intervalTicks;
        }
    }
}
