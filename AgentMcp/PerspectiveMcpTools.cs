using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Voidwright.Core;

namespace Voidwright.AgentMcp;

// A virtual observation point, not a game entity or a client spectator camera.
public sealed class PerspectiveStore
{
    internal readonly Dictionary<string, PerspectiveState> Items = new(StringComparer.Ordinal);
    internal readonly object Gate = new();
}

internal sealed record PerspectiveState(string Id, string AgentId, long GridId, string Fingerprint,
    string WorldScopeId, string RunId, double[] LocalPosition, double[] LocalForward,
    double[] LocalUp, long CreatedTick, string? NamedPreset = null);

[McpServerToolType]
public sealed class PerspectiveMcpTools(SnapshotFileReader snapshots, PerspectiveStore store,
    HoloViewCaptureClient captureClient)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const double MaximumRadiusMeters = 250;
    private const int MaximumPerspectives = 16;

    // Used by the compact vehicle profile. Legacy perspective methods stay
    // available in the default profile without adding another default tool.
    public async Task<CallToolResult> ViewVehicle(string agentId, string gridEntityId, string preset = "chase",
        double distanceMeters = 0, bool capture = true)
    {
        try
        {
            double[] offsetDirection = preset switch
            {
                "chase" => [0, 0.5, -1], "front" => [0, 0.35, 1],
                "left" => [-1, 0.25, 0], "right" => [1, 0.25, 0], "top" => [0, 1, 0],
                _ => throw new ArgumentException("preset must be chase, front, left, right, or top.")
            };
            if (!double.IsFinite(distanceMeters) || distanceMeters != 0 && distanceMeters is < 4 or > 150)
                throw new ArgumentException("distanceMeters must be zero for automatic framing, or 4..150 m.");
            string id;
            double range;
            object configuration;
            lock (store.Gate)
            {
                var gridId = ParseGridId(gridEntityId);
                var (agent, grid, envelope) = Resolve(agentId, gridId);
                if (IsStale(envelope) || agent.CapturedSimulationTick != envelope.CapturedSimulationTick)
                    throw new InvalidOperationException("Named views require a fresh, same-tick body snapshot.");
                if (agent.StructureFingerprint is not { Length: > 0 and <= 128 })
                    throw new InvalidDataException("Named views require a bounded structure fingerprint.");
                if (grid.Blocks.Count == 0) throw new InvalidOperationException("No observed block envelope is available for framing.");
                if (grid.Blocks.Any(block => Enumerable.Range(0, 3).Any(axis => block.Min[axis] > block.Max[axis])))
                    throw new InvalidDataException("Observed block envelope bounds are invalid.");
                var minimum = Enumerable.Range(0, 3).Select(axis => grid.Blocks.Min(block => (block.Min[axis] - 0.5) * grid.GridSizeMeters)).ToArray();
                var maximum = Enumerable.Range(0, 3).Select(axis => grid.Blocks.Max(block => (block.Max[axis] + 0.5) * grid.GridSizeMeters)).ToArray();
                var center = Enumerable.Range(0, 3).Select(axis => (minimum[axis] + maximum[axis]) / 2).ToArray();
                var radius = Length(Enumerable.Range(0, 3).Select(axis => (maximum[axis] - minimum[axis]) / 2).ToArray());
                var distance = distanceMeters == 0 ? Math.Max(4, radius / Math.Sin(55 * Math.PI / 360) + 2) : distanceMeters;
                if (distance > 150 || distance <= radius + 0.5)
                    throw new ArgumentException("Observed body cannot be framed at that bounded distance; use a farther manual perspective.");
                var direction = Normalize(offsetDirection);
                var local = Enumerable.Range(0, 3).Select(axis => center[axis] + direction[axis] * distance).ToArray();
                _ = CheckedLocal(local[0], local[1], local[2]);
                var forward = Normalize(direction.Select(value => -value).ToArray());
                var up = OrthonormalUp(forward, preset == "top" ? [0, 0, 1] : [0, 1, 0]);
                // Retire only our named rigs after a run/structure change. Raw
                // perspectives retain their original fail-closed binding.
                foreach (var old in store.Items.Values.Where(value => value.NamedPreset is not null &&
                    (value.WorldScopeId != snapshots.WorldScopeId || value.RunId != envelope.RunId ||
                     value.AgentId == agentId && value.Fingerprint != agent.StructureFingerprint)).ToArray())
                    store.Items.Remove(old.Id);
                var prior = store.Items.Values.SingleOrDefault(value => value.NamedPreset is not null
                    && value.AgentId == agentId && value.GridId == gridId);
                if (prior is null && store.Items.Count >= MaximumPerspectives) throw new InvalidOperationException("Perspective limit reached.");
                id = prior?.Id ?? Guid.NewGuid().ToString("N");
                var state = new PerspectiveState(id, agentId, gridId, agent.StructureFingerprint,
                    snapshots.WorldScopeId!, envelope.RunId!, local, forward, up, envelope.CapturedSimulationTick, preset);
                store.Items[id] = state;
                PublishMarkers(envelope);
                range = Math.Clamp(distance + radius * 2 + 4, 10, 512);
                configuration = new
                {
                    schema = "voidwright.named-vehicle-view.v1", perspectiveId = id, agentId, gridEntityId,
                    preset, frame = "grid-relative; follows the refreshed grid transform, including vehicle tilt",
                    worldScopeId = state.WorldScopeId, runId = state.RunId,
                    configurationSnapshotTick = envelope.CapturedSimulationTick,
                    configurationCapturedAtUtc = DateTimeOffset.Parse(envelope.CapturedAtUtc!, CultureInfo.InvariantCulture).UtcDateTime.ToString("O"),
                    observedBounds = new { minimum, maximum, center, radiusMeters = radius,
                        complete = agent.GridCoverageComplete && agent.BlockCoverageComplete && grid.BlockCount == grid.Blocks.Count },
                    distanceMeters = distance, fovDegrees = 55, rangeMeters = range, perspectiveReused = prior is not null,
                    localPosition = local, worldPosition = World(grid, local), worldForward = WorldDirection(grid, forward), worldUp = WorldDirection(grid, up),
                    physicalEntity = false, renderedCamera = false,
                    scope = "Observed grid envelopes only; configuration and capture may use different snapshots. No terrain-clearance inference."
                };
            }
            var metadata = new TextContentBlock { Text = JsonSerializer.Serialize(configuration, Json) };
            if (!capture) return new CallToolResult { Content = [metadata] };
            var rendered = await CaptureView(id, 55, range);
            return new CallToolResult { IsError = rendered.IsError, Content = [metadata, .. rendered.Content] };
        }
        catch (Exception error) when (Expected(error)) { return Error(error.Message); }
    }

    [McpServerTool(Name = "create_voidwright_perspective", ReadOnly = true), Description("Creates a process-local virtual observation point relative to an enrolled physical grid. It does not create a game entity or render a camera.")]
    public CallToolResult Create(string agentId, string gridEntityId, double rightMeters = 0, double upMeters = 10, double forwardMeters = 0)
    {
        try
        {
            var gridId = ParseGridId(gridEntityId);
            var (agent, grid, envelope) = Resolve(agentId, gridId);
            var local = CheckedLocal(rightMeters, upMeters, forwardMeters);
            lock (store.Gate)
            {
                if (store.Items.Count >= MaximumPerspectives) return Error("Perspective limit reached.");
                var id = Guid.NewGuid().ToString("N");
                var forward = Length(local) > 1e-6 ? Normalize([-local[0], -local[1], -local[2]]) : new double[] { 0, 0, 1 };
                var up = OrthonormalUp(forward, [0, 1, 0]);
                var state = new PerspectiveState(id, agentId, gridId, agent.StructureFingerprint ?? "",
                    snapshots.WorldScopeId!, envelope.RunId ?? "", local, forward, up, agent.CapturedSimulationTick);
                store.Items.Add(id, state);
                PublishMarkers(envelope);
                return Result(Describe(state, grid, envelope));
            }
        }
        catch (Exception e) when (Expected(e)) { return Error(e.Message); }
    }

    [McpServerTool(Name = "look_at_voidwright_perspective", ReadOnly = true), Description("Aims a process-local virtual perspective at a world-space point. Coordinates are meters in the same world frame as the enrolled grid snapshot; no game camera moves.")]
    public CallToolResult LookAt(string perspectiveId, double worldX, double worldY, double worldZ)
    {
        try
        {
            if (!double.IsFinite(worldX) || !double.IsFinite(worldY) || !double.IsFinite(worldZ))
                return Error("Look-at coordinates must be finite.");
            lock (store.Gate)
            {
                var (state, grid, envelope) = ResolveState(perspectiveId);
                var position = World(grid, state.LocalPosition);
                var delta = new[] { worldX - position[0], worldY - position[1], worldZ - position[2] };
                if (Length(delta) < 0.001 || Length(delta) > MaximumRadiusMeters)
                    return Error("Look-at point must be 0.001..250 m from the perspective.");
                var localForward = Normalize([Dot(delta, grid.Right), Dot(delta, grid.Up), Dot(delta, grid.Forward)]);
                state = state with { LocalForward = localForward,
                    LocalUp = OrthonormalUp(localForward, [0, 1, 0]) };
                store.Items[perspectiveId] = state;
                PublishMarkers(envelope);
                return Result(Describe(state, grid, envelope));
            }
        }
        catch (Exception e) when (Expected(e)) { return Error(e.Message); }
    }

    [McpServerTool(Name = "move_voidwright_perspective", ReadOnly = true), Description("Moves a virtual perspective to an absolute grid-relative offset within 250 m. This changes only process-local observation state.")]
    public CallToolResult Move(string perspectiveId, double rightMeters, double upMeters, double forwardMeters)
    {
        try
        {
            var local = CheckedLocal(rightMeters, upMeters, forwardMeters);
            lock (store.Gate)
            {
                var (state, grid, envelope) = ResolveState(perspectiveId);
                state = state with { LocalPosition = local };
                store.Items[perspectiveId] = state;
                PublishMarkers(envelope);
                return Result(Describe(state, grid, envelope));
            }
        }
        catch (Exception e) when (Expected(e)) { return Error(e.Message); }
    }

    [McpServerTool(Name = "capture_view", ReadOnly = true), Description("Requests one bounded 256x256 virtual HoloView software-rendered color frame from an existing Voidwright perspective. Returns the image plus separate snapshot/render-clock provenance. Grid-only, read-only, no physical camera or LCD required; no continuous recording.")]
    public async Task<CallToolResult> CaptureView(string perspectiveId, double fovDegrees = 55,
        double rangeMeters = 64, int timeoutSeconds = 15)
    {
        try
        {
            if (!double.IsFinite(fovDegrees) || fovDegrees is < 25 or > 100)
                return Error("fovDegrees must be finite and within 25..100 degrees.");
            if (!double.IsFinite(rangeMeters) || rangeMeters is < 10 or > 512)
                return Error("rangeMeters must be finite and within 10..512 meters.");
            if (timeoutSeconds is < 1 or > 20)
                return Error("timeoutSeconds must be within 1..20 seconds.");

            PerspectiveState state;
            GridSnapshot grid;
            SnapshotEnvelope envelope;
            lock (store.Gate)
            {
                (state, grid, envelope) = ResolveState(perspectiveId);
                if (IsStale(envelope)) return Error("Current structural snapshot is stale; refusing to capture from an outdated perspective.");
                PublishMarkers(envelope);
            }
            var expectedWorldName = HoloViewCaptureClient.ExpectedWorldName;
            if (string.IsNullOrWhiteSpace(expectedWorldName))
                return Error("The exact isolated HoloView world name is not configured.");
            if (string.IsNullOrWhiteSpace(envelope.CapturedAtUtc) || string.IsNullOrWhiteSpace(envelope.RunId))
                return Error("Snapshot lacks capture time or run identity.");

            var requestStartedAt = DateTimeOffset.UtcNow;
            var pose = new ViewPose(
                World(grid, state.LocalPosition),
                Normalize(WorldDirection(grid, state.LocalForward)),
                Normalize(WorldDirection(grid, state.LocalUp)));
            var request = new HoloViewCaptureRequest(state.Id, state.WorldScopeId, state.RunId,
                expectedWorldName, state.GridId, envelope.CapturedSimulationTick,
                envelope.CapturedAtUtc, pose, fovDegrees, rangeMeters);
            var frame = await captureClient.CaptureAsync(request, TimeSpan.FromSeconds(timeoutSeconds));

            PerspectiveState afterState;
            GridSnapshot afterGrid;
            SnapshotEnvelope afterEnvelope;
            lock (store.Gate)
                (afterState, afterGrid, afterEnvelope) = ResolveState(perspectiveId);
            if (afterState != state ||
                afterEnvelope.RunId != envelope.RunId || snapshots.WorldScopeId != state.WorldScopeId ||
                afterGrid.EntityId != grid.EntityId)
                return Error("Perspective, world, grid, or server run changed during capture; the frame was discarded.");

            var receipt = LiveViewCaptureReconciler.Reconcile(expectedWorldName, state.WorldScopeId,
                state.RunId, state.GridId, envelope.CapturedSimulationTick, envelope.CapturedAtUtc,
                pose, fovDegrees, state.Id, requestStartedAt, frame);
            var png = PngEncoder.Encode(frame.Rgba, frame.Width, frame.Height);
            var output = new
            {
                schema = "voidwright.capture-view.v1",
                receipt,
                snapshot = new
                {
                    capturedAtUtc = envelope.CapturedAtUtc,
                    simulationTick = envelope.CapturedSimulationTick,
                    capturedAtCaptureRequest = true
                },
                rendererClock = new
                {
                    source = "HoloView PortalLcdSession update counter; not verified as the game simulation tick",
                    updateCounter = frame.RendererUpdateCounter,
                    rendererSessionId = frame.RendererSessionId
                },
                runBinding = "world scope and run ID read from the configured Voidwright snapshot before the request and revalidated after the response; HoloView independently reports the observed world name",
                sceneCoverage = "saved-grid software raster only; terrain, voxel surfaces, characters, and general dynamic entities are not verified",
                structuralComparison = "timestamps and tick domains remain separate; simultaneous is false"
            };
            return new CallToolResult
            {
                IsError = false,
                Content =
                [
                    new TextContentBlock { Text = JsonSerializer.Serialize(output, Json) },
                    ImageContentBlock.FromBytes(png, "image/png")
                ]
            };
        }
        catch (Exception e) when (Expected(e) || e is TimeoutException or OperationCanceledException)
        {
            return Error(e.Message);
        }
    }

    [McpServerTool(Name = "inspect_voidwright_cell", ReadOnly = true), Description("Inspects an exact grid cell from a virtual perspective, returning block identity and snapshot provenance. A missing block is only conclusive when block coverage is complete.")]
    public CallToolResult Inspect(string perspectiveId, int x, int y, int z)
    {
        try
        {
            lock (store.Gate)
            {
                var (state, grid, envelope) = ResolveState(perspectiveId);
                var agent = envelope.Agents.Single(a => a.AgentId == state.AgentId);
                var matches = grid.Blocks.Where(b => x >= b.Min[0] && x <= b.Max[0] &&
                    y >= b.Min[1] && y <= b.Max[1] && z >= b.Min[2] && z <= b.Max[2]).ToArray();
                var world = World(grid, [(double)x * grid.GridSizeMeters, (double)y * grid.GridSizeMeters, (double)z * grid.GridSizeMeters]);
                return Result(new { state.Id, state.GridId, cell = new[] { x, y, z }, gridBasisWorldPosition = world,
                    capturedAtUtc = envelope.CapturedAtUtc, agent.CapturedSimulationTick,
                    stale = IsStale(envelope),
                    agent.StructureFingerprint, agent.BlockCoverageComplete,
                    occupied = matches.Length > 0, emptyConclusive = matches.Length == 0 && agent.BlockCoverageComplete,
                    blocks = matches.Select(b => new { b.EntityId, b.DefinitionType, b.SubtypeId, b.Name, b.Position,
                        b.Min, b.Max, b.Forward, b.Up, b.WorldCenter, b.BuildLevelRatio, b.DamageRatio,
                        b.HasEnabledState, b.Enabled, b.IsFunctional, b.IsWorking }) });
            }
        }
        catch (Exception e) when (Expected(e)) { return Error(e.Message); }
    }

    [McpServerTool(Name = "raycast_voidwright_perspective", ReadOnly = true), Description("Casts a bounded read-only ray through the enrolled agent's exported grid-cell envelopes. This is snapshot geometry, not an engine physics raycast; terrain and non-enrolled entities are unsupported.")]
    public CallToolResult Raycast(string perspectiveId, double maxDistanceMeters,
        double[]? directionWorld = null, double[]? targetWorldPosition = null,
        int[]? targetGridCell = null, string? targetGridId = null,
        long? expectedSimulationTick = null)
    {
        try
        {
            if (!double.IsFinite(maxDistanceMeters) || maxDistanceMeters <= 0 || maxDistanceMeters > 250)
                return Error("Maximum ray distance must be finite and within (0, 250] meters.");
            if (targetWorldPosition is not null && targetGridCell is not null)
                return Error("Choose either a world target position or a target grid cell, not both.");
            if ((targetWorldPosition is not null || targetGridCell is not null) && directionWorld is not null)
                return Error("Choose a direction, world target position, or grid cell as the aim source.");
            if (directionWorld is not null) ValidateVector(directionWorld, "directionWorld");
            if (targetWorldPosition is not null) ValidateVector(targetWorldPosition, "targetWorldPosition");
            if (targetGridCell is not null && targetGridCell.Length != 3)
                return Error("targetGridCell must contain exactly three grid coordinates.");
            var parsedTargetGridId = targetGridId is null ? (long?)null : ParseGridId(targetGridId);

            lock (store.Gate)
            {
                var (state, perspectiveGrid, envelope) = ResolveState(perspectiveId);
                if (IsStale(envelope))
                    return Error("Current grid snapshot is stale; raycast requires a fresh snapshot.");
                var agent = envelope.Agents.Single(a => a.AgentId == state.AgentId);
                if (agent.CapturedSimulationTick != envelope.CapturedSimulationTick)
                    return Error("Agent and envelope simulation ticks differ; refusing a mixed snapshot.");
                if (expectedSimulationTick.HasValue && expectedSimulationTick.Value != envelope.CapturedSimulationTick)
                    return Error("Snapshot tick does not match expectedSimulationTick; refresh the observation and retry.");

                var origin = World(perspectiveGrid, state.LocalPosition);
                var worldForward = Normalize(WorldDirection(perspectiveGrid, state.LocalForward));
                double[] aim;
                var aimSource = "perspective-forward";
                if (directionWorld is not null)
                {
                    aim = Normalize(directionWorld);
                    aimSource = "explicit-world-direction";
                }
                else if (targetWorldPosition is not null)
                {
                    var delta = new[] { targetWorldPosition[0] - origin[0], targetWorldPosition[1] - origin[1], targetWorldPosition[2] - origin[2] };
                    if (Length(delta) < 0.001 || Length(delta) > 250)
                        return Error("World target must be 0.001..250 m from the perspective.");
                    aim = Normalize(delta);
                    aimSource = "world-position";
                }
                else if (targetGridCell is not null)
                {
                    var cellGridId = parsedTargetGridId ?? state.GridId;
                    var cellGrid = agent.PhysicalGrids.SingleOrDefault(g => g.EntityId == cellGridId)
                        ?? throw new InvalidOperationException("Target grid is absent from the enrolled body snapshot.");
                    ValidateBasis(cellGrid);
                    var target = World(cellGrid, targetGridCell.Select(value => (double)value * cellGrid.GridSizeMeters).ToArray());
                    var delta = new[] { target[0] - origin[0], target[1] - origin[1], target[2] - origin[2] };
                    if (Length(delta) < 0.001 || Length(delta) > 250)
                        return Error("Target grid cell must be 0.001..250 m from the perspective.");
                    aim = Normalize(delta);
                    aimSource = "grid-cell-center";
                    parsedTargetGridId = cellGridId;
                }
                else aim = worldForward;

                var selectedGrids = parsedTargetGridId.HasValue
                    ? agent.PhysicalGrids.Where(g => g.EntityId == parsedTargetGridId.Value).ToArray()
                    : agent.PhysicalGrids.ToArray();
                if (selectedGrids.Length == 0)
                    throw new InvalidOperationException("Target grid is absent from the enrolled body snapshot.");
                foreach (var grid in selectedGrids) ValidateBasis(grid);
                var selectedBlocksComplete = selectedGrids.All(grid => grid.BlockCount == grid.Blocks.Count);
                var coverageComplete = selectedBlocksComplete && (parsedTargetGridId.HasValue ||
                    (agent.GridCoverageComplete && agent.BlockCoverageComplete));
                var hit = GridRaycaster.Cast(selectedGrids, origin, aim, maxDistanceMeters);
                var status = hit is null ? (coverageComplete ? "miss" : "unresolved") : coverageComplete ? "hit" : "unresolved";
                return Result(new
                {
                    schema = "voidwright.grid-raycast.v1", status, hit = hit is not null,
                    observedGridHit = hit is not null,
                    unresolvedReason = coverageComplete ? null : "Block or grid snapshot coverage is incomplete; nearest hit and misses are not authoritative.",
                    aimSource, worldScopeId = state.WorldScopeId, runId = state.RunId,
                    simulationTick = envelope.CapturedSimulationTick, capturedAtUtc = envelope.CapturedAtUtc,
                    perspectiveId = state.Id, perspectiveGridId = state.GridId,
                    perspectivePositionWorld = origin, perspectiveForwardWorld = worldForward,
                    rayDirectionWorld = aim, maxDistanceMeters,
                    targetGridId = hit?.GridId ?? parsedTargetGridId,
                    requestedTargetGridId = parsedTargetGridId,
                    testedGridIds = selectedGrids.Select(grid => grid.EntityId),
                    targetGridCell = targetGridCell,
                    targetWorldPosition,
                    sceneScope = parsedTargetGridId.HasValue ? "selected-enrolled-grid-block-envelopes" : "enrolled-agent-grid-block-envelopes",
                    coverageComplete,
                    unsupportedTargets = new[] { "terrain and voxel geometry", "non-enrolled grids", "non-grid entities", "block mesh details" },
                    geometry = "oriented grid-cell envelopes derived from the exported structural snapshot; not a live physics raycast",
                    faceSemantics = "axis-aligned grid-cell envelope face; normal is transformed into world space",
                    block = hit is null ? null : new
                    {
                        gridEntityId = hit.GridId, gridName = hit.GridName,
                        entityId = hit.Block.EntityId, identityResolved = hit.Block.EntityId > 0,
                        definitionType = hit.Block.DefinitionType, subtypeId = hit.Block.SubtypeId, name = hit.Block.Name,
                        cell = hit.Cell, localFace = hit.LocalFace, worldPosition = hit.WorldPosition,
                        worldNormal = hit.WorldNormal, distanceMeters = hit.DistanceMeters,
                        metadata = hit.Block
                    }
                });
            }
        }
        catch (Exception e) when (Expected(e)) { return Error(e.Message); }
    }

    private (PerspectiveState, GridSnapshot, SnapshotEnvelope) ResolveState(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !store.Items.TryGetValue(id, out var state))
            throw new InvalidOperationException("Unknown process-local perspective ID.");
        var (agent, grid, envelope) = Resolve(state.AgentId, state.GridId);
        if (state.Fingerprint != (agent.StructureFingerprint ?? ""))
            throw new InvalidOperationException("Grid structure changed; recreate the perspective against the current snapshot.");
        if (state.WorldScopeId != snapshots.WorldScopeId || state.RunId != (envelope.RunId ?? ""))
            throw new InvalidOperationException("World scope or simulation run changed; recreate the perspective.");
        return (state, grid, envelope);
    }

    private static long ParseGridId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 19 || value[0] == '0' ||
            !value.All(char.IsAsciiDigit) ||
            !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ArgumentException("Grid ID must be a canonical positive decimal string within Int64 range.");
        return id;
    }

    private (AgentSnapshot, GridSnapshot, SnapshotEnvelope) Resolve(string agentId, long gridId)
    {
        if (string.IsNullOrWhiteSpace(agentId) || agentId.Length > 128 || gridId <= 0)
            throw new ArgumentException("An exact agent ID and positive physical grid ID are required.");
        var envelope = snapshots.Read();
        if (string.IsNullOrEmpty(envelope.RunId))
            throw new InvalidOperationException("Snapshot lacks a simulation run ID; rebuild the exporter before creating a perspective.");
        var agent = envelope.Agents.SingleOrDefault(a => a.AgentId == agentId)
            ?? throw new InvalidOperationException("Agent is absent from the current snapshot.");
        var grid = agent.PhysicalGrids.SingleOrDefault(g => g.EntityId == gridId)
            ?? throw new InvalidOperationException("Grid is absent from the current enrolled body snapshot.");
        ValidateBasis(grid);
        return (agent, grid, envelope);
    }

    private static void ValidateBasis(GridSnapshot grid)
    {
        var axes = new[] { grid.Right, grid.Up, grid.Forward };
        if (axes.Any(axis => Math.Abs(Length(axis) - 1) > 0.01) ||
            Math.Abs(Dot(axes[0], axes[1])) > 0.01 ||
            Math.Abs(Dot(axes[0], axes[2])) > 0.01 ||
            Math.Abs(Dot(axes[1], axes[2])) > 0.01)
            throw new InvalidDataException("Grid basis is not orthonormal; perspective pose cannot be trusted.");
    }

    private static double[] CheckedLocal(double right, double up, double forward)
    {
        if (!double.IsFinite(right) || !double.IsFinite(up) || !double.IsFinite(forward) ||
            Math.Sqrt(right * right + up * up + forward * forward) > MaximumRadiusMeters)
            throw new ArgumentException("Perspective offset must be finite and within 250 m of its grid.");
        return [right, up, forward];
    }

    private static double[] World(GridSnapshot grid, double[] local) =>
    [grid.Position[0] + local[0] * grid.Right[0] + local[1] * grid.Up[0] + local[2] * grid.Forward[0],
     grid.Position[1] + local[0] * grid.Right[1] + local[1] * grid.Up[1] + local[2] * grid.Forward[1],
     grid.Position[2] + local[0] * grid.Right[2] + local[1] * grid.Up[2] + local[2] * grid.Forward[2]];

    private static double[] WorldDirection(GridSnapshot grid, double[] local) =>
    [local[0] * grid.Right[0] + local[1] * grid.Up[0] + local[2] * grid.Forward[0],
     local[0] * grid.Right[1] + local[1] * grid.Up[1] + local[2] * grid.Forward[1],
     local[0] * grid.Right[2] + local[1] * grid.Up[2] + local[2] * grid.Forward[2]];

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double Length(double[] value) => Math.Sqrt(Dot(value, value));
    private static double[] Normalize(double[] value)
    {
        var length = Length(value);
        if (!double.IsFinite(length) || length < 1e-9) throw new ArgumentException("Degenerate perspective direction.");
        return [value[0] / length, value[1] / length, value[2] / length];
    }
    private static double[] OrthonormalUp(double[] forward, double[] preferred)
    {
        if (Math.Abs(Dot(forward, preferred)) > 0.98) preferred = [0, 0, 1];
        var projection = Dot(forward, preferred);
        return Normalize([preferred[0] - projection * forward[0],
            preferred[1] - projection * forward[1], preferred[2] - projection * forward[2]]);
    }

    private static object Describe(PerspectiveState state, GridSnapshot grid, SnapshotEnvelope envelope) => new
    {
        state.Id, state.AgentId, state.GridId, state.WorldScopeId, state.RunId,
        coordinateFrame = "grid-relative offsets in meters (right, up, forward); look-at input and reported pose in world meters",
        state.LocalPosition, state.LocalForward, state.LocalUp,
        worldPosition = World(grid, state.LocalPosition),
        worldForward = WorldDirection(grid, state.LocalForward),
        worldUp = WorldDirection(grid, state.LocalUp),
        grid.Name, grid.Right, grid.Up, grid.Forward,
        capturedAtUtc = envelope.CapturedAtUtc, gridSnapshotTick = envelope.CapturedSimulationTick,
        perspectiveCreatedTick = state.CreatedTick,
        stale = IsStale(envelope),
        observationOnly = true, renderedCamera = false, physicalEntity = false
    };

    private static bool IsStale(SnapshotEnvelope envelope) =>
        !DateTimeOffset.TryParse(envelope.CapturedAtUtc, out var captured) ||
        DateTimeOffset.UtcNow - captured > SnapshotFileReader.FreshnessWindow || captured > DateTimeOffset.UtcNow;

    private void PublishMarkers(SnapshotEnvelope envelope)
    {
        var snapshotPath = snapshots.ConfiguredPath ??
            throw new InvalidOperationException("The exact Voidwright snapshot path is not configured.");
        var runId = envelope.RunId ?? throw new InvalidOperationException("Snapshot run identity is unavailable.");
        var captured = DateTime.Parse(envelope.CapturedAtUtc, CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
        var batch = new VirtualCameraMarkerBatch { RunId = runId, PublishedUtc = DateTime.UtcNow };
        foreach (var item in store.Items.Values)
        {
            if (item.RunId != runId || item.WorldScopeId != snapshots.WorldScopeId) continue;
            var agent = envelope.Agents.SingleOrDefault(a => a.AgentId == item.AgentId);
            if (agent is null || item.Fingerprint != (agent.StructureFingerprint ?? "")) continue;
            var grid = agent.PhysicalGrids.SingleOrDefault(g => g.EntityId == item.GridId);
            if (grid is null) continue;
            ValidateBasis(grid);
            batch.Markers.Add(new VirtualCameraMarker
            {
                Id = item.Id, SnapshotTick = envelope.CapturedSimulationTick, SnapshotUtc = captured,
                Position = World(grid, item.LocalPosition),
                Forward = Normalize(WorldDirection(grid, item.LocalForward)),
                Up = Normalize(WorldDirection(grid, item.LocalUp))
            });
        }
        var path = Path.Combine(Path.GetDirectoryName(snapshotPath)!, VirtualCameraMarkerWire.FileName);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, VirtualCameraMarkerWire.Format(batch));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void ValidateVector(double[] vector, string field)
    {
        if (vector.Length != 3 || vector.Any(value => !double.IsFinite(value)))
            throw new ArgumentException($"{field} must contain three finite values.");
    }

    private static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or InvalidDataException
        or InvalidOperationException or ArgumentException or JsonException;
    private static CallToolResult Result(object value) => new() { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(value, Json) }] };
    private static CallToolResult Error(string message) => new() { IsError = true,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { error = message }, Json) }] };
}
