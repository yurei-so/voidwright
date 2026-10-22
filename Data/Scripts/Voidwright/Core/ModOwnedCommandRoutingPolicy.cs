namespace Voidwright.Core
{
    /// <summary>Routes non-dispatching preflight by action while retaining the explicit owner gate for movement.</summary>
    public static class ModOwnedCommandRoutingPolicy
    {
        public static bool ShouldHostReview(string commandKind, string motionOwner)
        {
            if (commandKind == "preflight-only") return true;
            return (commandKind == "drive-to-coordinate-depth" || commandKind == "direct-maneuver") &&
                motionOwner == "voidwright-mod";
        }
    }
}
