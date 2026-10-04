namespace Voidwright.Core
{
    public enum AvoidanceState { Clear, Caution, Emergency }

    public sealed class NavigationIntent
    {
        public Route PlannedRoute { get; private set; }
        public AvoidanceState Avoidance { get; private set; }
        public GridPoint? EmergencyDirection { get; private set; }

        public NavigationIntent(Route plannedRoute, AvoidanceState avoidance, GridPoint? emergencyDirection)
        {
            PlannedRoute = plannedRoute;
            Avoidance = avoidance;
            EmergencyDirection = emergencyDirection;
        }
    }
}
