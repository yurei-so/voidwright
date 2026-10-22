using System.Text.Json;
using ModelContextProtocol.Protocol;
using Voidwright.AgentMcp;

internal static class VehicleObservationChecks
{
    public static void Run(string snapshotPath, Action<bool, string> check, Action<string> pass)
    {
        var original = File.ReadAllText(snapshotPath);
        var snapshot = JsonSerializer.Deserialize<SnapshotEnvelope>(original, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        snapshot.CapturedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        var agent = snapshot.Agents[0];
        const long exactId = 106313676705820376;
        agent.RootGridEntityId = exactId;
        var grid = agent.PhysicalGrids[0];
        grid.EntityId = exactId;
        grid.LinearVelocity = [3, 4, 0];
        var tools = new VehicleObservationTools(new SnapshotFileReader());
        void Write() => File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));
        JsonElement Read(CallToolResult result)
        {
            check(result.IsError != true, "Vehicle summary unexpectedly failed: " + result.Content.OfType<TextContentBlock>().Single().Text);
            return JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement.Clone();
        }
        try
        {
            Write();
            var observed = Read(tools.Observe(agent.AgentId!, expectedRunId: snapshot.RunId, expectedSimulationTick: 60));
            check(observed.GetProperty("gridEntityId").GetString() == exactId.ToString() &&
                  observed.GetProperty("motion").GetProperty("speedMetersPerSecond").GetDouble() == 5 &&
                  observed.GetProperty("motion").GetProperty("forwardSpeedMetersPerSecond").GetDouble() == 3 &&
                  observed.GetProperty("motion").GetProperty("verticalSpeedMetersPerSecond").GetDouble() == 4 &&
                  observed.GetProperty("gravity").GetProperty("upAxisTiltDegrees").GetDouble() == 0,
                "Vehicle summary must preserve the exact ID and use the grid/local-gravity frames for motion");
            check(observed.GetProperty("source").GetString()!.Contains("not camera") &&
                  !observed.GetProperty("limits").GetProperty("drivingAuthorized").GetBoolean(),
                "Telemetry must remain distinct from perception and authority");
            pass("summarize exact-ID vehicle motion and gravity-relative attitude from one fresh snapshot");

            check(tools.Observe(agent.AgentId!, "106313676705820380").IsError == true &&
                  tools.Observe(agent.AgentId!, "0106313676705820376").IsError == true &&
                  tools.Observe(agent.AgentId!, expectedRunId: new string('2', 32)).IsError == true &&
                  tools.Observe(agent.AgentId!, expectedSimulationTick: 61).IsError == true,
                "Vehicle summaries must refuse nearby rounded/noncanonical IDs and mismatched run/tick");
            pass("reject rounded vehicle IDs and wrong run/tick expectations");

            agent.BlockCoverageComplete = false;
            grid.BlockCount = 2;
            grid.NaturalGravity = [0, 0, 0];
            Write();
            observed = Read(tools.Observe(agent.AgentId!));
            check(!observed.GetProperty("structure").GetProperty("complete").GetBoolean() &&
                  observed.GetProperty("gravity").GetProperty("localUp").ValueKind == JsonValueKind.Null &&
                  observed.GetProperty("gravity").GetProperty("upAxisTiltDegrees").ValueKind == JsonValueKind.Null,
                "Missing body coverage and zero gravity must remain unknown");
            pass("preserve incomplete structure and zero-gravity attitude as unknown");

            grid.Name = new string('n', 10_000);
            agent.StructureFingerprint = new string('f', 10_000);
            grid.Blocks[0].DefinitionType = new string('t', 10_000);
            Write();
            observed = Read(tools.Observe(agent.AgentId!));
            check(observed.GetProperty("name").GetString()!.Length == 128 && observed.GetProperty("nameTruncated").GetBoolean()
                && observed.GetProperty("structure").GetProperty("structureFingerprint").ValueKind == JsonValueKind.Null
                && observed.GetProperty("structure").GetProperty("equipment")[0].GetProperty("definitionType").GetString() == "unknown"
                && observed.GetRawText().Length < 10_000, "Large snapshot metadata must not expand the summary response");
            grid.Right = grid.Forward;
            Write();
            check(tools.Observe(agent.AgentId!).IsError == true, "Nonorthogonal pose axes must not yield trusted motion components");
            grid.Right = [0, 0, -1];
            pass("bound vehicle-summary metadata and refuse invalid pose frames");

            snapshot.CapturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
            Write();
            check(tools.Observe(agent.AgentId!).IsError == true, "Stale telemetry must not be presented as current");
            check(!Read(tools.Observe(agent.AgentId!, requireFresh: false)).GetProperty("fresh").GetBoolean(),
                "Explicit forensic reads must retain stale status");
            snapshot.CapturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1).ToString("O");
            Write();
            check(tools.Observe(agent.AgentId!).IsError == true, "Future-clock observations must not be current");
            snapshot.CapturedAtUtc = DateTimeOffset.UtcNow.ToString("O");
            agent.CapturedSimulationTick = 59;
            Write();
            check(tools.Observe(agent.AgentId!).IsError == true, "Mixed-time envelope/body telemetry must fail");
            pass("refuse stale, future-clock and mixed-time current vehicle summaries");
        }
        finally { File.WriteAllText(snapshotPath, original); }
    }
}
