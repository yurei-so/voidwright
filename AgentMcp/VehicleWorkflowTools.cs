using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Voidwright.AgentMcp;

// A small opt-in surface over the same owners and guards. It grants no new
// fixture, renderer, actuator or filesystem capability.
[McpServerToolType]
public sealed class VehicleWorkflowTools(SnapshotFileReader snapshots, VehicleObservationTools observations,
    AgentMcpTools bodies, PerspectiveMcpTools perspectives)
{
    [McpServerTool(Name = "voidwright_snapshot_status", ReadOnly = true), Description("Checks the configured snapshot and lists bounded enrolled-agent/grid identities for this vehicle workflow. One snapshot supplies freshness and discovery; exact IDs are strings, paths are withheld.")]
    public string Status()
    {
        object context;
        try
        {
            var snapshot = snapshots.Read();
            var captured = DateTimeOffset.Parse(snapshot.CapturedAtUtc!, CultureInfo.InvariantCulture);
            var age = DateTimeOffset.UtcNow - captured;
            context = new
            {
                schema = "voidwright.vehicle-context.v1", readable = true,
                fresh = age >= TimeSpan.Zero && age <= SnapshotFileReader.FreshnessWindow,
                ageSeconds = age.TotalSeconds, capturedAtUtc = captured.UtcDateTime.ToString("O"),
                worldScopeId = snapshots.WorldScopeId, runId = snapshot.RunId, simulationTick = snapshot.CapturedSimulationTick,
                snapshot.AgentsTruncated,
                agents = snapshot.Agents.Select(agent => new
                {
                    agent.AgentId, rootGridEntityId = agent.RootGridEntityId.ToString(CultureInfo.InvariantCulture),
                    rootGridName = Label(agent.RootGridName), agent.GridCoverageComplete, agent.BlockCoverageComplete,
                    gridsOmitted = Math.Max(0, agent.PhysicalGrids.Count - 16),
                    grids = agent.PhysicalGrids.OrderByDescending(grid => grid.EntityId == agent.RootGridEntityId).ThenBy(grid => grid.EntityId).Take(16)
                        .Select(grid => new { gridEntityId = grid.EntityId.ToString(CultureInfo.InvariantCulture), name = Label(grid.Name), grid.IsStatic })
                })
            };
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            context = new { schema = "voidwright.vehicle-context.v1", readable = false, fresh = false,
                catalogAvailable = false, snapshotStatus = snapshots.GetStatus() };
        }
        return JsonSerializer.Serialize(context, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static string? Label(string? value) => value is { Length: > 128 } ? value[..128] : value;

    [McpServerTool(Name = "observe_voidwright_vehicle", ReadOnly = true), Description("One-snapshot vehicle motion, local-gravity attitude, bounded equipment counts and integrity. Exact IDs are strings. Privileged telemetry, not camera perception, clearance or driving authority.")]
    public CallToolResult Observe(string agentId, string? gridEntityId = null, string? expectedRunId = null,
        long? expectedSimulationTick = null, bool requireFresh = true)
        => observations.Observe(agentId, gridEntityId, expectedRunId, expectedSimulationTick, requireFresh);

    [McpServerTool(Name = "view_voidwright_vehicle", ReadOnly = true), Description("Frames one enrolled vehicle with a reusable chase/front/left/right/top virtual view and optionally returns one HoloView image. Uses observed block bounds and fresh snapshot poses. Follows the grid, including tilt; no game camera entity, terrain clearance, continuous recording or shared render/simulation clock.")]
    public async Task<CallToolResult> View(string agentId, string gridEntityId, string preset = "chase", double distanceMeters = 0, bool capture = true)
        => ExactIdentifiers(await perspectives.ViewVehicle(agentId, gridEntityId, preset, distanceMeters, capture));

    [McpServerTool(Name = "query_voidwright_body", ReadOnly = true), Description("Pages up to 200 observed blocks, optionally attaching the bounded damage wireframe. Exact entity IDs are strings; incomplete coverage stays unknown.")]
    public CallToolResult Body(string agentId, int offset = 0, int limit = 100, bool includeWireframe = false)
        => ExactIdentifiers(bodies.QueryVoidwrightBody(agentId, offset, limit, includeWireframe, visualizationMode: "damage"));

    private static CallToolResult ExactIdentifiers(CallToolResult result)
    {
        if (result.Content.OfType<TextContentBlock>().Any(text => text.Text.Length > 1_000_000))
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "{\"error\":\"Vehicle response exceeded its text budget; request a smaller body page.\"}" }] };
        return new CallToolResult { IsError = result.IsError, Content = result.Content.Select(block => block is TextContentBlock text ? Rewrite(text) : block).ToList() };
    }

    private static TextContentBlock Rewrite(TextContentBlock text)
    {
        try
        {
            var node = JsonNode.Parse(text.Text);
            ConvertIds(node);
            return new TextContentBlock { Text = node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? text.Text };
        }
        catch (JsonException) { return text; }
    }

    private static void ConvertIds(JsonNode? node)
    {
        if (node is JsonObject fields)
            foreach (var (name, value) in fields.ToArray())
            {
                if (name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && value is JsonValue scalar && scalar.TryGetValue<long>(out var id))
                    fields[name] = id.ToString(CultureInfo.InvariantCulture);
                else if (name.EndsWith("Ids", StringComparison.OrdinalIgnoreCase) && value is JsonArray ids)
                    for (var index = 0; index < ids.Count; index++)
                    {
                        if (ids[index] is JsonValue item && item.TryGetValue<long>(out var number)) ids[index] = number.ToString(CultureInfo.InvariantCulture);
                        else ConvertIds(ids[index]);
                    }
                else ConvertIds(value);
            }
        else if (node is JsonArray items)
            foreach (var item in items) ConvertIds(item);
    }
}
