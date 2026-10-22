using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Voidwright.AgentMcp;

[McpServerToolType]
public sealed class LiveVehicleMcpTools(LiveVehicleRuntime runtime)
{
    [McpServerTool(Name = "voidwright_live_enroll_vehicle", ReadOnly = false), Description("Creates a live session for an exact grid. Currently fail-closed because this local stdio MCP host cannot supply a verified authorized principal.")]
    public string Enroll([Description("Exact physical grid entity ID.")] long gridEntityId)
        => Invoke(() => new { sessionId = runtime.Enroll(gridEntityId, DateTime.UtcNow), gridEntityId });

    [McpServerTool(Name = "voidwright_live_vehicle_state", ReadOnly = true), Description("Returns live state for an enrolled session; unavailable until trusted principal and game adapter are integrated.")]
    public string Observe(string sessionId) => Invoke(() => runtime.Observe(sessionId, DateTime.UtcNow));

    [McpServerTool(Name = "voidwright_live_submit_goal", ReadOnly = false), Description("Submits a dynamic live goal to an enrolled session; currently fail-closed pending trusted principal and game adapter integration.")]
    public string Submit(string sessionId, string commandId, string goalType, double x, double y, double z,
        double maximumSpeedMetersPerSecond, int timeoutSeconds, bool replace = false)
        => Invoke(() => runtime.Submit(sessionId, commandId,
            new VehicleGoal(goalType, x, y, z, maximumSpeedMetersPerSecond, timeoutSeconds), DateTime.UtcNow, replace));

    [McpServerTool(Name = "voidwright_live_cancel_goal", ReadOnly = false), Description("Cancels a live goal; cancellation acknowledgment is separate from confirmed physical stopping.")]
    public string Cancel(string sessionId, string commandId) => Invoke(() => runtime.Cancel(sessionId, commandId, DateTime.UtcNow));

    [McpServerTool(Name = "voidwright_live_goal_status", ReadOnly = true), Description("Reads a live goal receipt if present. A cancellation acknowledgment does not confirm physical stopping.")]
    public string Status(string sessionId, string commandId)
        => Invoke(() => runtime.Status(sessionId, commandId, DateTime.UtcNow));

    [McpServerTool(Name = "voidwright_live_stop_vehicle", ReadOnly = false), Description("Requests emergency stop for a verified enrolled session; unavailable until trusted principal and game adapter integration.")]
    public string Stop(string sessionId) => Invoke(() => { runtime.Stop(sessionId, DateTime.UtcNow); return new { acknowledged = true, physicalStopConfirmed = false }; });

    [McpServerTool(Name = "voidwright_live_release_vehicle", ReadOnly = false), Description("Releases an enrolled live session after requesting stop if a goal remains active.")]
    public string Release(string sessionId) => Invoke(() => { runtime.Release(sessionId, DateTime.UtcNow); return new { released = true }; });

    private static string Invoke<T>(Func<T> operation)
    {
        try { return JsonSerializer.Serialize(operation()); }
        catch (UnauthorizedAccessException exception)
        { return JsonSerializer.Serialize(new { error = "authorization_unavailable", detail = exception.Message }); }
        catch (InvalidOperationException exception)
        { return JsonSerializer.Serialize(new { error = "operation_unavailable", detail = exception.Message }); }
        catch (ArgumentException exception)
        { return JsonSerializer.Serialize(new { error = "invalid_request", detail = exception.Message }); }
    }
}
