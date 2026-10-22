using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Voidwright.AgentMcp;

// One snapshot read supplies all values. This is privileged simulation telemetry,
// separate from HoloView pixels and from a permission to actuate the vehicle.
[McpServerToolType]
public sealed class VehicleObservationTools(SnapshotFileReader snapshots)
{
    [McpServerTool(Name = "observe_voidwright_vehicle", ReadOnly = true),
     Description("Summarizes one enrolled grid's motion, gravity-relative attitude, observed equipment and current integrity from one snapshot. Exact IDs are strings. Defaults to fresh, same-tick telemetry; explicitly separate from camera perception, terrain clearance or driving authorization.")]
    public CallToolResult Observe(string agentId, string? gridEntityId = null, string? expectedRunId = null,
        long? expectedSimulationTick = null, bool requireFresh = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agentId) || agentId.Length > 128) throw new ArgumentException("agentId must name one enrolled agent.");
            var snapshot = snapshots.Read();
            var captured = DateTimeOffset.Parse(snapshot.CapturedAtUtc!, CultureInfo.InvariantCulture);
            var age = DateTimeOffset.UtcNow - captured;
            var fresh = age >= TimeSpan.Zero && age <= SnapshotFileReader.FreshnessWindow;
            if (requireFresh && !fresh) throw new InvalidDataException("Snapshot is stale or ahead of the local clock; current vehicle telemetry is unavailable.");
            if (string.IsNullOrWhiteSpace(snapshot.RunId)) throw new InvalidDataException("Snapshot lacks an exporter run identity.");
            if (expectedRunId is not null && expectedRunId != snapshot.RunId) throw new InvalidDataException("Snapshot run differs from expectedRunId.");
            if (expectedSimulationTick is { } tick && tick != snapshot.CapturedSimulationTick) throw new InvalidDataException("Snapshot tick differs from expectedSimulationTick.");
            var agent = snapshot.Agents.SingleOrDefault(value => value.AgentId == agentId)
                ?? throw new InvalidDataException("Agent is absent from the enrolled snapshot.");
            var id = gridEntityId is null ? agent.RootGridEntityId : ParseId(gridEntityId);
            if (id <= 0) throw new InvalidDataException("Grid identity must be positive.");
            var grid = agent.PhysicalGrids.SingleOrDefault(value => value.EntityId == id)
                ?? throw new InvalidDataException("Grid is absent from this agent's enrolled body.");
            if (agent.CapturedSimulationTick != snapshot.CapturedSimulationTick)
                throw new InvalidDataException("Agent and envelope ticks differ; refusing a mixed-time vehicle observation.");
            var forward = Unit(grid.Forward);
            var right = Unit(grid.Right);
            var up = Unit(grid.Up);
            if (Math.Abs(Dot(forward, right)) > 0.001 || Math.Abs(Dot(forward, up)) > 0.001 || Math.Abs(Dot(up, right)) > 0.001)
                throw new InvalidDataException("Grid pose basis is not orthogonal.");
            var speed = Norm(grid.LinearVelocity);
            var gravity = Norm(grid.NaturalGravity);
            var localUp = gravity > 1e-6 ? grid.NaturalGravity.Select(value => -value / gravity).ToArray() : null;
            double? attitude = localUp is null ? null : Math.Acos(Math.Clamp(Dot(up, localUp), -1, 1)) * 180 / Math.PI;
            double? verticalSpeed = localUp is null ? null : Dot(grid.LinearVelocity, localUp);
            var equipmentGroups = grid.Blocks.GroupBy(block => block.DefinitionType is { Length: > 0 and <= 256 } name ? name : "unknown", StringComparer.Ordinal)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).ToArray();
            var equipment = equipmentGroups.Take(32).Select(group => new { definitionType = group.Key, observedCount = group.Count() }).ToArray();
            var knownIntegrity = grid.Blocks.Where(block => float.IsFinite(block.BuildLevelRatio) && block.BuildLevelRatio is > 0 and <= 1
                && float.IsFinite(block.DamageRatio) && block.DamageRatio is >= 0 and <= 1).ToArray();
            var complete = agent.GridCoverageComplete && agent.BlockCoverageComplete && grid.Blocks.Count == grid.BlockCount;
            var result = new
            {
                schema = "voidwright.vehicle-observation.v1", source = "exported server simulation telemetry, not camera perception",
                worldScopeId = snapshots.WorldScopeId, runId = snapshot.RunId, capturedAtUtc = captured.UtcDateTime.ToString("O"),
                simulationTick = snapshot.CapturedSimulationTick, ageSeconds = age.TotalSeconds, fresh,
                agent.AgentId, gridEntityId = id.ToString(CultureInfo.InvariantCulture), name = grid.Name is { Length: > 128 } label ? label[..128] : grid.Name,
                nameTruncated = grid.Name?.Length > 128, grid.IsStatic,
                positionMeters = grid.Position,
                motion = new { velocityMetersPerSecond = grid.LinearVelocity, speedMetersPerSecond = speed,
                    forwardSpeedMetersPerSecond = Dot(grid.LinearVelocity, forward), rightSpeedMetersPerSecond = Dot(grid.LinearVelocity, right), verticalSpeedMetersPerSecond = verticalSpeed },
                gravity = new { vectorMetersPerSecondSquared = grid.NaturalGravity, magnitudeMetersPerSecondSquared = gravity,
                    localUp, upAxisTiltDegrees = attitude, reference = localUp is null ? "gravity unavailable; attitude unknown" : "observed local natural gravity" },
                structure = new { structureFingerprint = agent.StructureFingerprint is { Length: <= 128 } fingerprint ? fingerprint : null,
                    observedBlocks = grid.Blocks.Count, reportedBlocks = grid.BlockCount,
                    complete, agent.GridCoverageComplete, agent.BlockCoverageComplete, equipment, equipmentCategoriesOmitted = Math.Max(0, equipmentGroups.Length - equipment.Length),
                    integrity = new { known = knownIntegrity.Length, unknown = grid.Blocks.Count - knownIntegrity.Length,
                        damaged = knownIntegrity.Count(block => block.DamageRatio > 0),
                        nonfunctional = knownIntegrity.Count(block => !block.IsFunctional) } },
                limits = new { terrainClearanceKnown = false, historicalDamageKnown = false, drivingAuthorized = false,
                    omittedBodyState = complete ? "none reported" : "unknown; incomplete exported coverage" }
            };
            var text = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            if (text.Length > 64_000) throw new InvalidDataException("Vehicle summary exceeded its output bound.");
            return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { error = error.Message }) }] };
        }
    }

    private static long ParseId(string text)
    {
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 || id.ToString(CultureInfo.InvariantCulture) != text)
            throw new ArgumentException("gridEntityId must be a canonical positive decimal string.");
        return id;
    }
    private static double Dot(double[] left, double[] right) => left.Zip(right, (a, b) => a * b).Sum();
    private static double Norm(double[] vector) => Math.Sqrt(Dot(vector, vector));
    private static double[] Unit(double[] vector)
    {
        var norm = Norm(vector);
        if (norm < 1e-6 || !double.IsFinite(norm)) throw new InvalidDataException("Grid pose has an invalid basis.");
        return vector.Select(value => value / norm).ToArray();
    }
}
