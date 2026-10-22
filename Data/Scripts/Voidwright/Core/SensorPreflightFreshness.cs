using System;

namespace Voidwright.Core
{
    /// <summary>
    /// Pure movement-boundary predicate for cached obstacle scans. A fresh ray
    /// count alone is insufficient: the controller, active camera, and scanned
    /// grid/camera identities must still match the run-pinned sensor.
    /// </summary>
    public static class SensorPreflightFreshness
    {
        public static bool IsFresh(DateTime nowUtc, long currentSimulationTick, long scanSimulationTick,
            DateTime scanUtc, int validRayCount, int expectedRayCount,
            long controllerGridId, long cameraGridId, long scanGridId,
            long currentCameraId, long expectedCameraId, long scanCameraId,
            TimeSpan maximumAge, long maximumTickAge)
        {
            var now = nowUtc.ToUniversalTime();
            var scan = scanUtc.ToUniversalTime();
            return maximumAge >= TimeSpan.Zero && maximumTickAge >= 0 &&
                   expectedRayCount > 0 && validRayCount == expectedRayCount &&
                   controllerGridId > 0 && cameraGridId == controllerGridId &&
                   scanGridId == controllerGridId && currentCameraId > 0 &&
                   currentCameraId == expectedCameraId && scanCameraId == expectedCameraId &&
                   scanSimulationTick >= 0 && currentSimulationTick >= scanSimulationTick &&
                   currentSimulationTick - scanSimulationTick <= maximumTickAge &&
                   now >= scan && now - scan <= maximumAge;
        }
    }
}
