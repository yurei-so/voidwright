namespace Voidwright.Core
{
    /// <summary>Pins a depth-navigation operation to the grid carrying its exact controller and camera.</summary>
    public static class DepthGridBindingPolicy
    {
        public static long PinExpectedGrid(long expectedGridId, bool depthMode,
            bool controllerResolved, long controllerGridId, bool cameraOnSameGrid)
        {
            if (!depthMode || expectedGridId > 0 || !controllerResolved ||
                controllerGridId <= 0 || !cameraOnSameGrid)
                return expectedGridId;
            return controllerGridId;
        }
    }
}
