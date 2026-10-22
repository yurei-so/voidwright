namespace Voidwright.Core
{
    /// <summary>Requires the discovered controller, grid, and camera identities to remain exact.</summary>
    public static class VehicleControllerBindingPolicy
    {
        public static bool IsExact(bool shipControllerResolved,
            long expectedControllerId, long controllerId,
            long expectedGridId, long controllerGridId,
            bool cameraResolved, long expectedCameraId, long cameraId, long cameraGridId)
        {
            return shipControllerResolved && cameraResolved &&
                expectedControllerId > 0 && controllerId == expectedControllerId &&
                expectedGridId > 0 && controllerGridId == expectedGridId &&
                expectedCameraId > 0 && cameraId == expectedCameraId &&
                cameraGridId == expectedGridId;
        }
    }
}
