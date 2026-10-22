using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Voidwright.AgentMcp;

[McpServerToolType]
public sealed class LabMcpTools(LabTrialRunner trials)
{
    [McpServerTool(Name = "voidwright_lab_trial", ReadOnly = false),
     Description("Preview, execute, or inspect one pinned low-speed GPS trial in an explicitly disposable destructive-lab copy. A connected operator may observe the run. Arrival is reported separately from cleanup; the server is stopped only with fresh zero-human evidence. Never operates on ordinary saves.")]
    public string Trial(string gpsTarget, string action = "preview", string? requestId = null, string? expectedPlanSha256 = null)
        => action switch
        {
            "preview" when requestId is null && expectedPlanSha256 is null => trials.PlanGpsGoal(gpsTarget),
            "execute" when requestId is not null && expectedPlanSha256 is not null => trials.ExecuteGpsPlan(gpsTarget, requestId, expectedPlanSha256),
            "status" when requestId is not null && expectedPlanSha256 is null => trials.InspectGpsPlan(gpsTarget, requestId),
            _ => throw new ArgumentException("Use preview without execution fields, execute with requestId and expectedPlanSha256, or status with requestId only.")
        };

}

[McpServerToolType]
public sealed class LegacyLabMcpTools(LabTrialRunner trials)
{
    [McpServerTool(Name = "voidwright_drive_lab_target", ReadOnly = false),
     Description("Compatibility one-call GPS trial using the same pinned VRageCage plan/journal workflow. Prefer voidwright_lab_trial for explicit preview and retry identity. Bounded depth guidance only, no Agent Core required. A connected operator may observe the disposable run; cleanup requires owned-run and fresh zero-human evidence. Never operates on ordinary saves.")]
    public string DriveLabTarget(
        [Description("Exact allowed GPS label: Red Zone, Yellow Zone, Green Zone, Blue Zone, Safe Spawn Zone, Starting Area, or End Position.")]
        string gpsTarget)
        => trials.RunGpsGoal(gpsTarget);
}
