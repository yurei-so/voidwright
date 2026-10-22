using System.Text.Json;
using System.Text;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Voidwright.Core;
using Voidwright.AgentMcp;
using Voidwright.ServerPlugin;

var passed = 0;
var runtimeNow = DateTime.UtcNow;
var anonymousRuntime = new LiveVehicleRuntime(new TestVehiclePrincipalProvider(null), _ => new TestLiveVehicleAdapter(8877));
var authorizationClosed = false;
try { anonymousRuntime.Enroll(8877, runtimeNow); }
catch (UnauthorizedAccessException) { authorizationClosed = true; }
Check(authorizationClosed, "live vehicle enrollment must fail closed without a verified principal");
var testAdapter = new TestLiveVehicleAdapter(8877);
var liveRuntime = new LiveVehicleRuntime(new TestVehiclePrincipalProvider(new VerifiedVehiclePrincipal(
    "test-operator", new HashSet<string>(["enroll", "observe", "control", "stop", "release"], StringComparer.Ordinal),
    runtimeNow.AddMinutes(2))), _ => testAdapter);
var liveSessionId = liveRuntime.Enroll(8877, runtimeNow);
Check((string)liveRuntime.Observe(liveSessionId, runtimeNow) == "observed", "mock runtime did not return dynamic state");
var firstDynamicGoal = liveRuntime.Submit(liveSessionId, "dynamic-command-1",
    new VehicleGoal("position", 1, 2, 3, 0.2, 30), runtimeNow);
var duplicateDynamicGoal = liveRuntime.Submit(liveSessionId, "dynamic-command-1",
    new VehicleGoal("position", 1, 2, 3, 0.2, 30), runtimeNow);
Check(firstDynamicGoal.State == "ACTIVE" && duplicateDynamicGoal.CommandId == firstDynamicGoal.CommandId &&
      testAdapter.AppliedCommands.Count == 1, "duplicate live command was not idempotent");
var replacedDynamicGoal = liveRuntime.Submit(liveSessionId, "dynamic-command-2",
    new VehicleGoal("position", 4, 5, 6, 0.3, 30), runtimeNow.AddSeconds(1), replace: true);
Check(liveRuntime.Status(liveSessionId, "dynamic-command-1", runtimeNow)!.State == "CANCELLED" &&
      replacedDynamicGoal.State == "ACTIVE" && testAdapter.AppliedCommands.Count == 2,
    "a live replacement did not atomically cancel the prior command and apply the new goal");
var cancellation = liveRuntime.Cancel(liveSessionId, "dynamic-command-2", runtimeNow.AddSeconds(2));
Check(cancellation.State == "CANCELLED" && cancellation.StopOutcome is { ActuatorsNeutralized: true, BrakesRequested: true,
      PhysicalStopObserved: false } && testAdapter.StopCalls == 1,
    "cancellation did not distinguish stop request from unconfirmed physical stopping");
liveRuntime.Submit(liveSessionId, "dynamic-command-3", new VehicleGoal("position", 7, 8, 9, 0.1, 30), runtimeNow.AddSeconds(3));
var expiredDynamicGoal = liveRuntime.Status(liveSessionId, "dynamic-command-3", runtimeNow.AddSeconds(40));
Check(expiredDynamicGoal!.State == "EXPIRED" && expiredDynamicGoal.StopOutcome is { BrakesRequested: true,
      PhysicalStopObserved: false } && testAdapter.StopCalls == 2,
    "command expiry did not request braking and preserve unconfirmed physical-stop status");
var independentStop = liveRuntime.Stop(liveSessionId, runtimeNow.AddSeconds(41));
Check(independentStop.ActuatorsNeutralized && independentStop.BrakesRequested && testAdapter.StopCalls == 3,
    "emergency stop did not remain available independently of ordinary goal admission");
liveRuntime.Submit(liveSessionId, "dynamic-command-4", new VehicleGoal("position", 9, 8, 7, 0.1, 30), runtimeNow.AddSeconds(42));
Check(liveRuntime.Complete(liveSessionId, "dynamic-command-4", runtimeNow.AddSeconds(43), "mock_arrival").State == "COMPLETED" &&
      liveRuntime.Status(liveSessionId, "dynamic-command-4", runtimeNow.AddSeconds(43))!.Result == "mock_arrival",
    "runtime did not publish a terminal result for a dynamically completed goal");
liveRuntime.Release(liveSessionId, runtimeNow.AddSeconds(44));
Check(testAdapter.ReleaseCalls == 1, "live runtime session did not release without rebuilding the runtime");
Pass("dynamic live runtime mock: enrollment, observation, submission, idempotency, replacement, cancellation, expiry, completion, emergency stop, and release");

var actuatorAdapter = new TestLiveVehicleAdapter(8878);
var actuatorRuntime = new LiveVehicleRuntime(new TestVehiclePrincipalProvider(new VerifiedVehiclePrincipal(
    "local-test-run:fixture-a", new HashSet<string>(["enroll", "observe", "control", "stop", "release"], StringComparer.Ordinal),
    runtimeNow.AddMinutes(2))), _ => actuatorAdapter);
var actuatorSession = actuatorRuntime.Enroll(8878, runtimeNow);
var steeringCommand = actuatorRuntime.Submit(actuatorSession, "stationary-steer",
    new VehicleGoal("steering", 0.15, 0, 0, 0, 2), runtimeNow);
var propulsionCommand = actuatorRuntime.Submit(actuatorSession, "bounded-propulsion",
    new VehicleGoal("propulsion", 0.02, 0, 0, 0.15, 2), runtimeNow.AddMilliseconds(100), replace: true);
var brakeCommand = actuatorRuntime.Submit(actuatorSession, "brake-command",
    new VehicleGoal("brake", 1, 0, 0, 0, 3), runtimeNow.AddMilliseconds(200), replace: true);
var neutralCommand = actuatorRuntime.Submit(actuatorSession, "neutral-command",
    new VehicleGoal("neutral", 0, 0, 0, 0, 2), runtimeNow.AddMilliseconds(300), replace: true);
var actuatorBoundsRejected = false;
try
{
    actuatorRuntime.Submit(actuatorSession, "too-fast-propulsion",
        new VehicleGoal("propulsion", 0.04, 0, 0, 0.5, 20), runtimeNow.AddMilliseconds(400), replace: true);
}
catch (ArgumentException) { actuatorBoundsRejected = true; }
Check(steeringCommand.State == "ACTIVE" && propulsionCommand.State == "ACTIVE" &&
      brakeCommand.State == "ACTIVE" && neutralCommand.State == "ACTIVE" && actuatorBoundsRejected &&
      actuatorAdapter.AppliedCommands.Count == 4,
    "transport-independent actuator runtime must accept bounded steer/propel/brake/neutral commands and reject wider bounds");
actuatorRuntime.Stop(actuatorSession, runtimeNow.AddMilliseconds(500));
actuatorRuntime.Release(actuatorSession, runtimeNow.AddMilliseconds(600));
Check(actuatorAdapter.StopCalls == 1 && actuatorAdapter.ReleaseCalls == 1,
    "local actuator command lifecycle must retain independent stop and release operations");
Pass("transport-independent actuator command API validates steering, bounded propulsion, brake, neutral, stop, and release");

var mailboxDirectory = Path.Combine(Path.GetTempPath(), "voidwright-local-mailbox-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(mailboxDirectory);
try
{
    const string mailboxInstance = "test-suite-world-v2-r3-voidwright-unified-20261013-c";
    const string mailboxRun = "11111111111111111111111111111111";
    const string mailboxFixture = "22222222222222222222222222222222";
    const long mailboxGrid = 7767;
    const long mailboxController = 8877;
    const long mailboxCamera = 9988;
    var mailboxAuthorization = new LocalVehicleTestAuthorization(mailboxInstance, mailboxRun,
        mailboxFixture, "local-run-test", mailboxGrid, mailboxController, mailboxCamera,
        runtimeNow.AddMinutes(1));
    var mailboxAdapter = new LocalVehicleTestFileAdapter(mailboxDirectory, mailboxAuthorization);
    Check(File.Exists(Path.Combine(mailboxDirectory, LocalVehicleTestFileAdapter.AuthorizationFileName)) &&
          !File.Exists(Path.Combine(mailboxDirectory, LocalVehicleTestFileAdapter.CommandFileName)),
        "local enrollment wrote a motion command before an operator submitted one");
    var mailboxRuntime = new LiveVehicleRuntime(new LocalVehicleTestPrincipalProvider(mailboxAuthorization),
        grid => grid == mailboxGrid ? mailboxAdapter : null);
    var mailboxSession = mailboxRuntime.Enroll(mailboxGrid, runtimeNow);
    var mailboxCommand = mailboxRuntime.Submit(mailboxSession, "mailbox-steering",
        new VehicleGoal("steering", 0.1, 0, 0, 0, 2), runtimeNow);
    Check(mailboxCommand.State == "ACTIVE" &&
          LocalVehicleTestWire.TryParseAuthorization(File.ReadAllText(Path.Combine(mailboxDirectory,
              LocalVehicleTestFileAdapter.AuthorizationFileName)), out var mailboxAuthorizationRoundTrip) &&
          mailboxAuthorizationRoundTrip.GridId == mailboxGrid &&
          LocalVehicleTestWire.TryParseCommand(File.ReadAllText(Path.Combine(mailboxDirectory,
              LocalVehicleTestFileAdapter.CommandFileName)), out var mailboxCommandRoundTrip) &&
          mailboxCommandRoundTrip.CommandId == "mailbox-steering" && mailboxCommandRoundTrip.GridId == mailboxGrid &&
          mailboxCommandRoundTrip.ControllerId == mailboxController && mailboxCommandRoundTrip.Value == 0.1,
        "local runtime API did not write its exact authorization and command into the game mailbox");
    var unconfirmedStop = mailboxRuntime.Stop(mailboxSession, runtimeNow.AddMilliseconds(20));
    var stopWire = File.ReadAllText(Path.Combine(mailboxDirectory, LocalVehicleTestFileAdapter.CommandFileName));
    var parsedStopCommand = LocalVehicleTestWire.TryParseCommand(stopWire, out var stopCommand);
    Check(!unconfirmedStop.ActuatorsNeutralized && !unconfirmedStop.BrakesRequested &&
          parsedStopCommand &&
          stopCommand.Kind == LocalVehicleTestCommandKind.Brake,
        "local file adapter claimed a stop receipt before the game-side readback arrived");
    var stopReceipt = new LocalVehicleTestReceipt(stopCommand.CommandId, "stopped", "readback_verified",
        runtimeNow.AddMilliseconds(30), 0, 0, 3, true, true, true);
    File.WriteAllText(Path.Combine(mailboxDirectory, LocalVehicleTestFileAdapter.ReceiptFileName),
        LocalVehicleTestWire.FormatReceipt(stopReceipt));
    Check(LocalVehicleTestWire.TryParseReceipt((string)mailboxRuntime.Observe(mailboxSession,
              runtimeNow.AddMilliseconds(40)), out var observedStop) && observedStop.PhysicalStopObserved,
        "local operator could not observe the game-side bounded stop receipt");
    mailboxRuntime.Release(mailboxSession, runtimeNow.AddMilliseconds(50));
    Check(!File.Exists(Path.Combine(mailboxDirectory, LocalVehicleTestFileAdapter.AuthorizationFileName)),
        "local session release did not revoke the run grant");
    Pass("explicit local runtime API writes the plugin mailbox and waits for a game-side stop receipt");
}
finally
{
    Directory.Delete(mailboxDirectory, recursive: true);
}

var fixture = Path.Combine(Path.GetTempPath(), "voidwright-agent-mcp-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var snapshotPath = Path.Combine(fixture, "snapshot.json");
try
{
    File.WriteAllText(snapshotPath, TestFixtures.Snapshot);
    Environment.SetEnvironmentVariable(SnapshotFileReader.PathEnvironmentVariable, snapshotPath);

    var reader = new SnapshotFileReader();
    var snapshot = reader.Read();
    Check(snapshot.Agents.Count == 1, "reader should return the one fixture agent");
    Check(reader.GetStatus().Readable, "status should confirm a valid snapshot");
    Pass("read and validate a bounded v1 snapshot");
    VehicleObservationChecks.Run(snapshotPath, Check, Pass);

    Environment.SetEnvironmentVariable(SnapshotFileReader.PathEnvironmentVariable, "relative-snapshot.json");
    var invalidConfiguration = new SnapshotFileReader().GetStatus();
    Check(!invalidConfiguration.Configured &&
          !invalidConfiguration.Issue!.Contains("relative-snapshot", StringComparison.Ordinal),
        "relative paths should be rejected without reflecting the path value");
    Pass("require an explicit absolute snapshot file path without echoing it");

    Environment.SetEnvironmentVariable(SnapshotFileReader.PathEnvironmentVariable, snapshotPath);
    var model = AgentWireframeMapper.Create(snapshot.Agents[0]);
    var rendered = AgentWireframeRenderer.Render(model, [new AgentWireframeSelection
    {
        SubtypeId = "Camera",
        Color = "#00FF00"
    }]);
    Check(rendered.RenderedBlocks == 1 && rendered.SelectedBlocks == 1, "wireframe should contain and select the camera");
    var corners = model.Blocks[0].Corners;
    Check(Math.Abs(corners.Min(point => point.X) - 0.75) < 1e-9 &&
          Math.Abs(corners.Max(point => point.X) - 3.25) < 1e-9 &&
          Math.Abs(corners.Min(point => point.Z) + 2.5) < 1e-9 &&
          Math.Abs(corners.Max(point => point.Z) - 2.5) < 1e-9,
        "rotated physical-subgrid axes and world offset should map into the Agent Core frame");
    Check(rendered.Svg.Contains("TOP", StringComparison.Ordinal) &&
          rendered.Svg.Contains("SIDE", StringComparison.Ordinal) &&
          rendered.Svg.Contains("FRONT", StringComparison.Ordinal), "wireframe should expose all three profiles");
    Check(rendered.Svg.Contains("#00FF00", StringComparison.Ordinal), "selected wireframe should keep the requested color");
    Pass("map physical subgrid pose into colored top/side/front wireframe");

    var damageModel = new AgentWireframeModel
    {
        AgentId = "damage-fixture",
        CapturedSimulationTick = 12,
        SourceSnapshotComplete = false
    };
    damageModel.Blocks.Add(new AgentWireframeBlock
    {
        BlockEntityId = 1, HasIntegrityData = true, DamageRatio = 0, IsFunctional = true,
        Corners = { new(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0), new(0,0,1), new(1,0,1), new(1,1,1), new(0,1,1) }
    });
    damageModel.Blocks.Add(new AgentWireframeBlock
    {
        BlockEntityId = 2, HasIntegrityData = true, DamageRatio = 0.25f, IsFunctional = true,
        Corners = { new(2,0,0), new(3,0,0), new(3,1,0), new(2,1,0), new(2,0,1), new(3,0,1), new(3,1,1), new(2,1,1) }
    });
    damageModel.Blocks.Add(new AgentWireframeBlock
    {
        BlockEntityId = 3, HasIntegrityData = true, DamageRatio = 0.75f, IsFunctional = false,
        Corners = { new(4,0,0), new(5,0,0), new(5,1,0), new(4,1,0), new(4,0,1), new(5,0,1), new(5,1,1), new(4,1,1) }
    });
    damageModel.Blocks.Add(new AgentWireframeBlock
    {
        BlockEntityId = 4, HasIntegrityData = false,
        Corners = { new(6,0,0), new(7,0,0), new(7,1,0), new(6,1,0), new(6,0,1), new(7,0,1), new(7,1,1), new(6,1,1) }
    });
    var damageMap = AgentWireframeRenderer.Render(damageModel, [], AgentWireframeVisualizationMode.Damage);
    Check(damageMap.VisualizationMode == "damage" && damageMap.HealthyBlocks == 1 &&
          damageMap.DamagedBlocks == 1 && damageMap.CriticalBlocks == 1 && damageMap.UnknownIntegrityBlocks == 1 &&
          damageMap.Svg.Contains("#36C98F", StringComparison.Ordinal) &&
          damageMap.Svg.Contains("#FFB547", StringComparison.Ordinal) &&
          damageMap.Svg.Contains("#FF5D70", StringComparison.Ordinal) &&
          damageMap.Svg.Contains("#8190A5", StringComparison.Ordinal) &&
          damageMap.Svg.Contains("HEALTHY + FUNCTIONAL", StringComparison.Ordinal) &&
          damageMap.Svg.Contains("DAMAGED + FUNCTIONAL", StringComparison.Ordinal) &&
          damageMap.Svg.Contains("NONFUNCTIONAL", StringComparison.Ordinal) &&
          damageMap.Svg.Contains("INTEGRITY UNKNOWN", StringComparison.Ordinal) &&
          damageMap.Svg.Contains("omitted block state is unknown", StringComparison.Ordinal) &&
          damageMap.Svg.Contains("no historical destruction is inferred", StringComparison.Ordinal),
        $"damage map should distinguish observed integrity without historical claims (mode={damageMap.VisualizationMode}, healthy={damageMap.HealthyBlocks}, damaged={damageMap.DamagedBlocks}, critical={damageMap.CriticalBlocks}, unknown={damageMap.UnknownIntegrityBlocks}, colors={damageMap.Svg.Contains("#36C98F")}/{damageMap.Svg.Contains("#FFB547")}/{damageMap.Svg.Contains("#FF5D70")}/{damageMap.Svg.Contains("#8190A5")}, unknown-label={damageMap.Svg.Contains("omitted block state is unknown")}, historical-label={damageMap.Svg.Contains("no historical destruction is inferred")})");
    Pass("render current observed integrity as a bounded damage map with explicit unknown coverage");

    var requestedPose = new ViewPose([1, 2, 3], [0, 0, -1], [0, 1, 0]);
    var syntheticImage = new byte[] { 1, 2, 3, 4 };
    var syntheticCapture = new ViewCaptureEvidence("test-world", "test-run", 10, 75,
        "holoview-software-render-virtual", requestedPose, 1, 1, syntheticImage);
    var reconciled = ViewCaptureReconciler.Reconcile("test-world", "test-run", 10, 60,
        requestedPose, syntheticCapture);
    Check(reconciled.TickDelta == 15 && !reconciled.Simultaneous && reconciled.RgbaSha256.Length == 64,
        "capture receipt must preserve a signed render-vs-structure tick delta and image hash");
    var wrongPoseRejected = false;
    try
    {
        _ = ViewCaptureReconciler.Reconcile("test-world", "test-run", 10, 60,
            requestedPose, syntheticCapture with { ActualPose = new ViewPose([2, 2, 3], [0, 0, -1], [0, 1, 0]) });
    }
    catch (InvalidDataException) { wrongPoseRejected = true; }
    Check(wrongPoseRejected, "moving the actual render pose without moving the request must reject the frame");
    var wrongRotationRejected = false;
    try
    {
        _ = ViewCaptureReconciler.Reconcile("test-world", "test-run", 10, 60,
            requestedPose, syntheticCapture with { ActualPose = new ViewPose([1, 2, 3], [1, 0, 0], [0, 1, 0]) });
    }
    catch (InvalidDataException) { wrongRotationRejected = true; }
    Check(wrongRotationRejected, "rotating the actual render pose without rotating the request must reject the frame");
    var wrongRunRejected = false;
    try
    {
        _ = ViewCaptureReconciler.Reconcile("test-world", "test-run", 10, 60,
            requestedPose, syntheticCapture with { RunId = "other-run" });
    }
    catch (InvalidDataException) { wrongRunRejected = true; }
    Check(wrongRunRejected, "cross-run images must not join to structural observations");
    Pass("reconcile synthetic capture metadata without hiding pose or tick mismatch");

    var rgba = new byte[256 * 256 * 4];
    for (var pixel = 0; pixel < 256 * 256; pixel++)
    {
        rgba[pixel * 4] = (byte)(32 + pixel % 4 * 40);
        rgba[pixel * 4 + 1] = (byte)(48 + pixel % 3 * 30);
        rgba[pixel * 4 + 2] = 180;
        rgba[pixel * 4 + 3] = 255;
    }
    var snapshotTime = DateTimeOffset.UtcNow.AddMilliseconds(-250);
    var captureTime = DateTimeOffset.UtcNow;
    var liveFrame = new HoloViewCaptureFrame("capture-test", "perspective-test", "test-world", "test-run",
        "test-suite-world-v2-r3", "renderer-session", 10, 60, snapshotTime.ToString("O"), 76,
        captureTime.ToString("O"), "holoview-software-render-virtual", requestedPose, 55, 64,
        256, 256, [10], 12, 65536, 12, rgba);
    var liveReceipt = LiveViewCaptureReconciler.Reconcile("test-suite-world-v2-r3", "test-world", "test-run",
        10, 60, snapshotTime.ToString("O"), requestedPose, 55, "perspective-test",
        captureTime.AddMilliseconds(-100), liveFrame);
    var encodedPng = PngEncoder.Encode(rgba, 256, 256);
    Check(!liveReceipt.Simultaneous && liveReceipt.TickRelationship.Contains("incomparable-domains", StringComparison.Ordinal) &&
          liveReceipt.RgbaSha256.Length == 64 && liveReceipt.PngSha256.Length == 64 &&
          liveReceipt.CaptureToSnapshotAgeMilliseconds >= 0 &&
          encodedPng.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "live capture receipt must separate renderer counters from game ticks and produce a valid bounded PNG payload");
    var wrongWorldRejected = false;
    try
    {
        _ = LiveViewCaptureReconciler.Reconcile("other-world", "test-world", "test-run", 10, 60,
            snapshotTime.ToString("O"), requestedPose, 55, "perspective-test", captureTime.AddMilliseconds(-100), liveFrame);
    }
    catch (InvalidDataException) { wrongWorldRejected = true; }
    Check(wrongWorldRejected, "live captures must reject an observed world-name mismatch");
    Pass("validate live render/snapshot provenance and encode a bounded MCP image");

    var corruptPath = Path.Combine(fixture, "corrupt.json");
    File.WriteAllText(corruptPath, "{\"schemaVersion\":1,\"agents\":[");
    Environment.SetEnvironmentVariable(SnapshotFileReader.PathEnvironmentVariable, corruptPath);
    Check(!new SnapshotFileReader().GetStatus().Readable, "corrupt/in-progress file must not be reported readable");
    Pass("fail closed on partial snapshot JSON");

    var oversizedPath = Path.Combine(fixture, "oversized.json");
    using (var stream = new FileStream(oversizedPath, FileMode.CreateNew, FileAccess.Write))
        stream.SetLength(SnapshotFileReader.MaximumFileBytes + 1);
    Environment.SetEnvironmentVariable(SnapshotFileReader.PathEnvironmentVariable, oversizedPath);
    var rejected = false;
    try { _ = new SnapshotFileReader().Read(); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "oversized snapshot should be refused before parsing");
    Pass("reject oversized local snapshot before allocating it");

    File.WriteAllText(snapshotPath, TestFixtures.Snapshot);
    var repositoryRoot = FindRepositoryRoot();
    var serverProject = Path.Combine(repositoryRoot, "AgentMcp", "Voidwright.AgentMcp.csproj");
    var fakeCliPath = Path.Combine(fixture, "fake-vragecage");
    var commandLogPath = Path.Combine(fixture, "lab-cli-commands.log");
    File.WriteAllText(fakeCliPath, TestFixtures.FakeVrageCageCli);
    if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(fakeCliPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
    environment[SnapshotFileReader.PathEnvironmentVariable] = snapshotPath;
    environment[HoloViewCaptureClient.StoragePathVariable] = fixture;
    environment[HoloViewCaptureClient.WorldNameVariable] = "test_suite_world_v2_r3";
    environment[LabTrialRunner.CliPathVariable] = fakeCliPath;
    environment[LabTrialRunner.InstanceVariable] = "fixture-copy";
    environment[LabTrialRunner.FixtureIdVariable] = "11111111111111111111111111111111";
    environment[LabTrialRunner.GridNameVariable] = "Ordinary Test Truck";
    environment[LabTrialRunner.ControllerIdVariable] = "333";
    environment[LabTrialRunner.CameraIdVariable] = "444";
    environment[LabTrialRunner.SnapshotRelativePathVariable] = "Storage/exporter/agent-core-snapshot-v1.json";
    environment["VOIDWRIGHT_TEST_CALL_LOG"] = commandLogPath;
    var transport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "Voidwright local stdio integration test",
        Command = "dotnet",
        Arguments = ["run", "--configuration", "Release", "--no-build", "--project", serverProject],
        WorkingDirectory = repositoryRoot,
        InheritEnvironmentVariables = false,
        EnvironmentVariables = environment
    });
    await using var client = await McpClient.CreateAsync(transport);
    var tools = await client.ListToolsAsync();
    var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
    Check(toolNames.SetEquals(["voidwright_snapshot_status", "list_voidwright_agents", "get_voidwright_character_observation", "query_voidwright_body", "observe_voidwright_vehicle", "voidwright_lab_trial", "voidwright_drive_lab_target", "capture_view",
        "create_voidwright_perspective", "move_voidwright_perspective", "look_at_voidwright_perspective", "inspect_voidwright_cell",
        "raycast_voidwright_perspective", "voidwright_live_enroll_vehicle", "voidwright_live_vehicle_state",
        "voidwright_live_submit_goal", "voidwright_live_cancel_goal", "voidwright_live_goal_status",
        "voidwright_live_stop_vehicle", "voidwright_live_release_vehicle"]),
        "MCP server should expose the expected observation, virtual capture, and separately bounded lab command tools; got: " + string.Join(", ", toolNames));
    var unauthorizedLiveEnrollment = await client.CallToolAsync("voidwright_live_enroll_vehicle",
        new Dictionary<string, object?> { ["gridEntityId"] = "8877", ["principal"] = "test-operator" });
    var unauthorizedLiveEnrollmentJson = JsonDocument.Parse(
        unauthorizedLiveEnrollment.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(unauthorizedLiveEnrollmentJson.GetProperty("error").GetString() == "authorization_unavailable" &&
          unauthorizedLiveEnrollmentJson.GetProperty("detail").GetString()!.Contains(
              "No verified, unexpired principal", StringComparison.Ordinal),
        "arbitrary stdio MCP arguments must not create a live session or supply an authorized principal");
    Pass("refuse live MCP enrollment when the stdio caller has no trusted principal");
    var status = await client.CallToolAsync("voidwright_snapshot_status", new Dictionary<string, object?>());
    var statusText = status.Content.OfType<TextContentBlock>().Single().Text;
    Check(statusText.Contains("\"readable\": true", StringComparison.Ordinal) &&
          !statusText.Contains(snapshotPath, StringComparison.Ordinal),
        "MCP status should verify readability without disclosing the configured local path");
    var list = await client.CallToolAsync("list_voidwright_agents", new Dictionary<string, object?>());
    var listText = list.Content.OfType<TextContentBlock>().Single().Text;
    Check(listText.Contains("voidwright-core:42", StringComparison.Ordinal), "MCP list call should return the fixture agent");
    const string largeGridId = "106313676705820376";
    File.WriteAllText(snapshotPath, TestFixtures.Snapshot
        .Replace("\"rootGridEntityId\": 10,", $"\"rootGridEntityId\": {largeGridId},", StringComparison.Ordinal)
        .Replace("\"entityId\": 10,", $"\"entityId\": {largeGridId},", StringComparison.Ordinal));
    var exactGridPerspective = await client.CallToolAsync("create_voidwright_perspective", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42", ["gridEntityId"] = largeGridId
    });
    var exactGridText = exactGridPerspective.Content.OfType<TextContentBlock>().Single().Text;
    Check(exactGridPerspective.IsError != true &&
          JsonDocument.Parse(exactGridText).RootElement.GetProperty("gridId").GetInt64() == 106313676705820376L,
        "18-digit grid ID must survive MCP argument parsing without rounding");
    var roundedGridPerspective = await client.CallToolAsync("create_voidwright_perspective", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42", ["gridEntityId"] = "106313676705820384"
    });
    Check(roundedGridPerspective.IsError == true, "a nearby rounded grid ID must not silently select the real grid");
    var forensicVehicle = await client.CallToolAsync("observe_voidwright_vehicle", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42", ["gridEntityId"] = largeGridId, ["requireFresh"] = false
    });
    var vehicleJson = JsonDocument.Parse(forensicVehicle.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(forensicVehicle.IsError != true && vehicleJson.GetProperty("gridEntityId").GetString() == largeGridId
        && !vehicleJson.GetProperty("fresh").GetBoolean(), "MCP vehicle summary must keep exact string identity and explicit stale status");
    File.WriteAllText(snapshotPath, TestFixtures.Snapshot);
    Pass("preserve exact 64-bit grid identity across the MCP tool boundary");
    File.WriteAllText(snapshotPath, TestFixtures.CreatePlayerSnapshot());
    var characterObservation = await client.CallToolAsync("get_voidwright_character_observation", new Dictionary<string, object?>
    {
        ["expectedRunId"] = "11111111111111111111111111111111",
        ["expectedSimulationTick"] = 60L
    });
    var characterText = characterObservation.Content.OfType<TextContentBlock>().Single().Text;
    using (var characterJson = JsonDocument.Parse(characterText))
    {
        var root = characterJson.RootElement;
        Check(characterObservation.IsError != true && root.GetProperty("status").GetString() == "observed" &&
              root.GetProperty("characterEntityId").GetInt64() == 987 &&
              root.GetProperty("position")[0].GetDouble() == 12.5 &&
              root.GetProperty("snapshotSimulationTick").GetInt64() == 60 &&
              root.GetProperty("runId").GetString() == "11111111111111111111111111111111",
            "character pose should retain fresh world/run/tick provenance");
        Check(!characterText.Contains("SteamUserId", StringComparison.OrdinalIgnoreCase) &&
              !characterText.Contains("DisplayName", StringComparison.OrdinalIgnoreCase) &&
              !characterText.Contains("IdentityId", StringComparison.OrdinalIgnoreCase),
            "character observation should omit account and display identity");
    }
    Pass("return an anonymous current character pose with run and tick provenance over MCP");
    var mismatchedCharacterRun = await client.CallToolAsync("get_voidwright_character_observation", new Dictionary<string, object?>
    {
        ["expectedRunId"] = "22222222222222222222222222222222"
    });
    Check(mismatchedCharacterRun.IsError == true, "character pose must reject a different expected run ID");
    var mismatchedCharacterTick = await client.CallToolAsync("get_voidwright_character_observation", new Dictionary<string, object?>
    {
        ["expectedSimulationTick"] = 61L
    });
    Check(mismatchedCharacterTick.IsError == true, "character pose must reject a different expected simulation tick");
    File.WriteAllText(snapshotPath, TestFixtures.CreatePlayerSnapshot(status: "ambiguous_multiple_humans", playerCount: 2));
    var ambiguousCharacters = await client.CallToolAsync("get_voidwright_character_observation", new Dictionary<string, object?>());
    var ambiguousText = ambiguousCharacters.Content.OfType<TextContentBlock>().Single().Text;
    Check(ambiguousCharacters.IsError != true && ambiguousText.Contains("ambiguous_multiple_humans", StringComparison.Ordinal) &&
          ambiguousText.Contains("\"position\": null", StringComparison.Ordinal) &&
          ambiguousText.Contains("\"characterEntityId\": null", StringComparison.Ordinal),
        "multiple connected humans must withhold all player positions");
    File.WriteAllText(snapshotPath, TestFixtures.CreatePlayerSnapshot(capturedAtUtc: "2026-10-01T00:00:00.0000000Z"));
    var staleCharacter = await client.CallToolAsync("get_voidwright_character_observation", new Dictionary<string, object?>());
    Check(staleCharacter.IsError == true, "stale player snapshots must not be returned as current locations");
    File.WriteAllText(snapshotPath, TestFixtures.Snapshot);
    var query = await client.CallToolAsync("query_voidwright_body", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42",
        ["offset"] = 0,
        ["limit"] = 10,
        ["includeWireframe"] = true,
        ["selections"] = new object[]
        {
            new Dictionary<string, object?>
            {
                ["subtypeId"] = "Camera",
                ["color"] = "#00FF00"
            }
        }
    });
    var queryText = query.Content.OfType<TextContentBlock>().Single().Text;
    var svgResource = query.Content.OfType<EmbeddedResourceBlock>().Single().Resource as TextResourceContents;
    Check(queryText.Contains("worldCenter", StringComparison.Ordinal), "MCP body call should return paged block metadata");
    Check(svgResource is not null && svgResource.MimeType == "image/svg+xml" && svgResource.Text.Contains("#00FF00", StringComparison.Ordinal),
        "MCP body call should attach the selected three-profile SVG");
    Check(list.IsError != true && query.IsError != true, "valid MCP calls should succeed");
    using (var queryJson = JsonDocument.Parse(queryText))
    {
        var summary = queryJson.RootElement.GetProperty("integritySummary");
        Check(summary.GetProperty("coverageComplete").GetBoolean() &&
              summary.GetProperty("observedBlockCount").GetInt32() == 1 &&
              summary.GetProperty("knownDamagedBlockCount").GetInt32() == 0 &&
              summary.GetProperty("destroyedOrMissingBlockCount").ValueKind == JsonValueKind.Null,
            "complete block snapshots should summarize only currently observed integrity and avoid destroyed-block claims");
    }
    File.WriteAllText(snapshotPath, TestFixtures.CreateIntegritySnapshot());
    var incompleteIntegrity = await client.CallToolAsync("query_voidwright_body", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42", ["limit"] = 10
    });
    using (var integrityJson = JsonDocument.Parse(incompleteIntegrity.Content.OfType<TextContentBlock>().Single().Text))
    {
        var summary = integrityJson.RootElement.GetProperty("integritySummary");
        Check(incompleteIntegrity.IsError != true && !summary.GetProperty("coverageComplete").GetBoolean() &&
              summary.GetProperty("observedBlockCount").GetInt32() == 1 &&
              summary.GetProperty("knownDamagedBlockCount").GetInt32() == 1 &&
              summary.GetProperty("knownNonFunctionalBlockCount").GetInt32() == 1 &&
              summary.GetProperty("destroyedOrMissingBlockCount").ValueKind == JsonValueKind.Null &&
              summary.GetProperty("unknownState").GetString()!.Contains("unknown", StringComparison.OrdinalIgnoreCase),
            "incomplete coverage must expose known damage while keeping omitted and destroyed state unknown");
    }
    File.WriteAllText(snapshotPath, TestFixtures.CreateIntegritySnapshot());
    Pass("summarize observed integrity without treating omitted blocks as destroyed");
    var damageWireframe = await client.CallToolAsync("query_voidwright_body", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42", ["limit"] = 10,
        ["includeWireframe"] = true, ["visualizationMode"] = "damage"
    });
    var damageWireframeText = damageWireframe.Content.OfType<TextContentBlock>().Single().Text;
    var damageWireframeSvg = damageWireframe.Content.OfType<EmbeddedResourceBlock>().Single().Resource as TextResourceContents;
    using (var damageJson = JsonDocument.Parse(damageWireframeText))
    {
        var wireframe = damageJson.RootElement.GetProperty("wireframe");
        Check(damageWireframe.IsError != true && wireframe.GetProperty("visualizationMode").GetString() == "damage" &&
              wireframe.GetProperty("criticalBlocks").GetInt32() == 1 &&
              wireframe.GetProperty("unknownIntegrityBlocks").GetInt32() == 0 &&
              damageWireframeSvg is not null && damageWireframeSvg.Text.Contains("#FF5D70", StringComparison.Ordinal) &&
              damageWireframeSvg.Text.Contains("omitted block state is unknown", StringComparison.Ordinal),
            $"MCP damage mode should return current-integrity SVG (error={damageWireframe.IsError}, mode={wireframe.GetProperty("visualizationMode").GetString()}, critical={wireframe.GetProperty("criticalBlocks").GetInt32()}, unknown={wireframe.GetProperty("unknownIntegrityBlocks").GetInt32()}, svg={damageWireframeSvg is not null}, color={damageWireframeSvg?.Text.Contains("#FF5D70")}, coverage={damageWireframeSvg?.Text.Contains("omitted block state is unknown")})");
    }
    var invalidDamageMode = await client.CallToolAsync("query_voidwright_body", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42", ["includeWireframe"] = true, ["visualizationMode"] = "materials"
    });
    Check(invalidDamageMode.IsError == true, "unsupported visualization modes must be rejected, not silently treated as structural");
    File.WriteAllText(snapshotPath, TestFixtures.Snapshot);
    Pass("expose damage visualization as a read-only MCP option and reject unsupported modes");
    var created = await client.CallToolAsync("create_voidwright_perspective", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42", ["gridEntityId"] = "10",
        ["rightMeters"] = 0.0, ["upMeters"] = 5.0, ["forwardMeters"] = 0.0
    });
    Check(created.IsError != true, "virtual perspective should be created on enrolled grid");
    var perspectiveId = JsonDocument.Parse(created.Content.OfType<TextContentBlock>().Single().Text).RootElement.GetProperty("id").GetString()!;
    var createdPose = JsonDocument.Parse(created.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(createdPose.GetProperty("runId").GetString() == "11111111111111111111111111111111" &&
          createdPose.GetProperty("worldScopeId").GetString()!.Length == 64,
        "perspective must bind to a simulation run and exact local world scope");
    var chasePerspectiveResult = await client.CallToolAsync("create_voidwright_perspective", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42", ["gridEntityId"] = "10",
        ["rightMeters"] = 0.0, ["upMeters"] = 0.0, ["forwardMeters"] = -8.0
    });
    var chaseText = chasePerspectiveResult.Content.OfType<TextContentBlock>().Single().Text;
    Check(chasePerspectiveResult.IsError != true, "grid-relative chase viewpoint should be created");
    var chaseId = JsonDocument.Parse(chaseText).RootElement.GetProperty("id").GetString()!;
    var chaseInitial = JsonDocument.Parse(chaseText).RootElement;
    Check(chaseInitial.GetProperty("worldPosition")[0].GetDouble() == -5 &&
          chaseInitial.GetProperty("worldForward")[0].GetDouble() == 1,
        "grid-relative chase viewpoint should resolve its initial position and orientation from the grid basis");
    File.WriteAllText(snapshotPath, TestFixtures.CreateMovedGridSnapshot());
    var chaseFollowedResult = await client.CallToolAsync("move_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = chaseId, ["rightMeters"] = 0.0, ["upMeters"] = 0.0, ["forwardMeters"] = -8.0
    });
    var chaseFollowed = JsonDocument.Parse(chaseFollowedResult.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(chaseFollowedResult.IsError != true &&
          chaseFollowed.GetProperty("gridSnapshotTick").GetInt64() == 61 &&
          chaseFollowed.GetProperty("worldPosition")[0].GetDouble() == 21 &&
          chaseFollowed.GetProperty("worldPosition")[2].GetDouble() == 1 &&
          chaseFollowed.GetProperty("worldForward")[0].GetDouble() == -1 &&
          chaseFollowed.GetProperty("worldForward")[2].GetDouble() == 0,
        "existing grid-local perspective should follow a translated and rotated grid transform on refresh");
    var markerFile = Path.Combine(fixture, VirtualCameraMarkerWire.FileName);
    VirtualCameraMarkerBatch? markers = null;
    Check(File.Exists(markerFile) && VirtualCameraMarkerWire.TryParse(File.ReadAllText(markerFile), out markers) &&
          markers!.RunId == "11111111111111111111111111111111" && markers.Markers.Count == 2,
        $"camera marker packet should bind to the exact run and include each process-local camera (exists={File.Exists(markerFile)}, parsed={markers is not null}, run={markers?.RunId}, count={markers?.Markers.Count}, body={(File.Exists(markerFile) ? File.ReadAllText(markerFile) : "missing")})");
    var chasedMarker = markers!.Markers.Single(item => item.Id == chaseId);
    Check(chasedMarker.SnapshotTick == chaseFollowed.GetProperty("gridSnapshotTick").GetInt64() &&
          Math.Abs(chasedMarker.Position[0] - chaseFollowed.GetProperty("worldPosition")[0].GetDouble()) < 1e-9 &&
          Math.Abs(chasedMarker.Position[2] - chaseFollowed.GetProperty("worldPosition")[2].GetDouble()) < 1e-9 &&
          Math.Abs(chasedMarker.Forward[0] - chaseFollowed.GetProperty("worldForward")[0].GetDouble()) < 1e-9,
        "marker pose must match the camera pose resolved from the moved grid");
    Check(!VirtualCameraMarkerWire.TryParse(File.ReadAllText(markerFile).Replace("VWCM1", "VWCM0"), out _) &&
          !VirtualCameraMarkerWire.Fresh(markers, chasedMarker, markers.PublishedUtc.AddSeconds(11)),
        "malformed packets and abandoned marker updates must not be presented as active");
    Pass("publish exact camera poses for client markers and expire stale display state");
    Pass("resolve a vehicle-relative virtual viewpoint against the latest grid transform");
    File.WriteAllText(snapshotPath, TestFixtures.Snapshot);
    var moved = await client.CallToolAsync("move_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = perspectiveId, ["rightMeters"] = 1.0, ["upMeters"] = 5.0, ["forwardMeters"] = 0.0
    });
    Check(moved.IsError != true && moved.Content.OfType<TextContentBlock>().Single().Text.Contains("worldPosition", StringComparison.Ordinal),
        "virtual movement should report world-space pose");
    var movedPose = JsonDocument.Parse(moved.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(!createdPose.GetProperty("worldPosition").ToString().Equals(movedPose.GetProperty("worldPosition").ToString(), StringComparison.Ordinal),
        "moving the perspective must change its reported world-space position");
    var aimed = await client.CallToolAsync("look_at_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = perspectiveId, ["worldX"] = 10.0, ["worldY"] = 1.0, ["worldZ"] = 1.0
    });
    Check(aimed.IsError != true && aimed.Content.OfType<TextContentBlock>().Single().Text.Contains("worldForward", StringComparison.Ordinal),
        "look-at should return an explicit world-space orientation");
    var aimedPose = JsonDocument.Parse(aimed.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(!movedPose.GetProperty("worldForward").ToString().Equals(aimedPose.GetProperty("worldForward").ToString(), StringComparison.Ordinal),
        "looking at a new target must change the reported orientation");
    var cell = await client.CallToolAsync("inspect_voidwright_cell", new Dictionary<string, object?>
    {
        ["perspectiveId"] = perspectiveId, ["x"] = 0, ["y"] = 0, ["z"] = 0
    });
    Check(cell.IsError != true && cell.Content.OfType<TextContentBlock>().Single().Text.Contains("Camera", StringComparison.Ordinal),
        "exact cell inspection should return observed block identity");
    var unbounded = await client.CallToolAsync("move_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = perspectiveId, ["rightMeters"] = 300.0, ["upMeters"] = 0.0, ["forwardMeters"] = 0.0
    });
    Check(unbounded.IsError == true, "virtual movement must reject unbounded offsets");
    File.WriteAllText(snapshotPath, TestFixtures.Snapshot.Replace(
        "11111111111111111111111111111111", "22222222222222222222222222222222", StringComparison.Ordinal));
    var changedRun = await client.CallToolAsync("inspect_voidwright_cell", new Dictionary<string, object?>
    {
        ["perspectiveId"] = perspectiveId, ["x"] = 0, ["y"] = 0, ["z"] = 0
    });
    Check(changedRun.IsError == true, "a perspective must refuse an overwritten world run even at the same file path");
    File.WriteAllText(snapshotPath, TestFixtures.Snapshot);
    Pass("create, move, aim, inspect, and bind a bounded virtual perspective to one world run over MCP");

    var raySnapshot = TestFixtures.CreateRaySnapshot();
    File.WriteAllText(snapshotPath, raySnapshot);
    var rayPerspectiveResult = await client.CallToolAsync("create_voidwright_perspective", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42", ["gridEntityId"] = "10",
        ["rightMeters"] = 4.5, ["upMeters"] = 10.0, ["forwardMeters"] = 12.5
    });
    var rayPerspective = JsonDocument.Parse(rayPerspectiveResult.Content.OfType<TextContentBlock>().Single().Text).RootElement.GetProperty("id").GetString()!;
    var occludedRay = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 100.0,
        ["targetGridCell"] = new[] { 2, 0, 5 }, ["expectedSimulationTick"] = 60L
    });
    var occludedJson = JsonDocument.Parse(occludedRay.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(occludedRay.IsError != true && occludedJson.GetProperty("status").GetString() == "hit" &&
          occludedJson.GetProperty("block").GetProperty("entityId").GetInt64() == 101 &&
          occludedJson.GetProperty("block").GetProperty("cell")[1].GetInt32() == 3 &&
          occludedJson.GetProperty("block").GetProperty("localFace").GetString() == "+Y" &&
          Math.Abs(occludedJson.GetProperty("block").GetProperty("worldNormal")[1].GetDouble() - 1) < 1e-9,
        "targeting the lower stacked cell must hit the nearer block and report exact cell and transformed face normal");
    var sideRay = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 100.0,
        ["targetGridCell"] = new[] { -2, 0, 5 }, ["expectedSimulationTick"] = 60L
    });
    var sideJson = JsonDocument.Parse(sideRay.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(sideJson.GetProperty("status").GetString() == "hit" &&
          sideJson.GetProperty("block").GetProperty("entityId").GetInt64() == 303,
        "a second aim direction must select the distinct side block in the rotated grid frame");
    var boundedRay = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 1.0,
        ["targetGridCell"] = new[] { 2, 0, 5 }
    });
    Check(JsonDocument.Parse(boundedRay.Content.OfType<TextContentBlock>().Single().Text).RootElement.GetProperty("status").GetString() == "miss",
        "a hit beyond the explicit range must be reported as a miss");
    var missRay = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 100.0,
        ["directionWorld"] = new[] { 0.0, 0.0, -1.0 }
    });
    Check(JsonDocument.Parse(missRay.Content.OfType<TextContentBlock>().Single().Text).RootElement.GetProperty("status").GetString() == "miss",
        "a direction that clears all exported block envelopes must return a miss");
    var defaultAim = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 100.0
    });
    Check(JsonDocument.Parse(defaultAim.Content.OfType<TextContentBlock>().Single().Text).RootElement
              .GetProperty("aimSource").GetString() == "perspective-forward",
        "omitting an aim vector must use the current perspective forward direction");
    var tickMismatch = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 100.0, ["expectedSimulationTick"] = 61L
    });
    Check(tickMismatch.IsError == true, "raycast must reject a requested snapshot tick that does not match");
    var positionAim = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 100.0,
        ["targetWorldPosition"] = new[] { 15.5, 1.0, -4.0 }
    });
    Check(JsonDocument.Parse(positionAim.Content.OfType<TextContentBlock>().Single().Text).RootElement.GetProperty("status").GetString() == "hit",
        "a world-space target point must aim through the same oriented-grid transform");

    var movedRaySnapshot = TestFixtures.CreateRaySnapshot(position: [103, 1, 1], tick: 61);
    File.WriteAllText(snapshotPath, movedRaySnapshot);
    var movedGridRay = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 100.0,
        ["targetGridCell"] = new[] { 2, 0, 5 }, ["expectedSimulationTick"] = 61L
    });
    var movedGridJson = JsonDocument.Parse(movedGridRay.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(movedGridJson.GetProperty("status").GetString() == "hit" &&
          movedGridJson.GetProperty("block").GetProperty("entityId").GetInt64() == 101 &&
          movedGridJson.GetProperty("simulationTick").GetInt64() == 61,
        "the grid-relative perspective must follow a moved grid while preserving the current tick");
    var incompleteSnapshot = TestFixtures.CreateRaySnapshot(blockCoverageComplete: false);
    File.WriteAllText(snapshotPath, incompleteSnapshot);
    var incompleteRay = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 100.0,
        ["targetGridCell"] = new[] { 2, 0, 5 }
    });
    var incompleteJson = JsonDocument.Parse(incompleteRay.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(incompleteJson.GetProperty("status").GetString() == "unresolved" &&
          incompleteJson.GetProperty("observedGridHit").GetBoolean() &&
          incompleteJson.GetProperty("unresolvedReason").ValueKind == JsonValueKind.String,
        "incomplete structural coverage must label even an observed hit as unresolved");
    var staleSnapshot = TestFixtures.CreateRaySnapshot(capturedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O"));
    File.WriteAllText(snapshotPath, staleSnapshot);
    var staleRay = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 100.0
    });
    Check(staleRay.IsError == true, "raycast must refuse stale snapshot context");
    var changedRayRun = TestFixtures.CreateRaySnapshot(runId: "22222222222222222222222222222222");
    File.WriteAllText(snapshotPath, changedRayRun);
    var runMismatchRay = await client.CallToolAsync("raycast_voidwright_perspective", new Dictionary<string, object?>
    {
        ["perspectiveId"] = rayPerspective, ["maxDistanceMeters"] = 100.0
    });
    Check(runMismatchRay.IsError == true, "raycast must refuse a perspective reused across simulation runs");
    File.WriteAllText(snapshotPath, TestFixtures.Snapshot);
    Pass("raycast rotated grid snapshots with range, misses, occlusion, cell faces, motion, and stale-run guards");

    File.WriteAllText(snapshotPath, TestFixtures.CreateRaySnapshot());
    var capturePerspectiveResult = await client.CallToolAsync("create_voidwright_perspective", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:42", ["gridEntityId"] = "10",
        ["rightMeters"] = 0.0, ["upMeters"] = 10.0, ["forwardMeters"] = 0.0
    });
    var capturePerspectiveId = JsonDocument.Parse(capturePerspectiveResult.Content.OfType<TextContentBlock>().Single().Text)
        .RootElement.GetProperty("id").GetString()!;
    var requestFile = Path.Combine(fixture, "voidwright-live-capture-request-v1.txt");
    var fakeRenderer = FakeHoloViewRenderer.CompleteOnce(fixture);
    var liveCapture = await client.CallToolAsync("capture_view", new Dictionary<string, object?>
    {
        ["perspectiveId"] = capturePerspectiveId, ["fovDegrees"] = 55.0,
        ["rangeMeters"] = 64.0, ["timeoutSeconds"] = 10
    });
    await fakeRenderer;
    var captureText = liveCapture.Content.OfType<TextContentBlock>().Single().Text;
    var captureJson = JsonDocument.Parse(captureText).RootElement;
    var renderedImage = liveCapture.Content.OfType<ImageContentBlock>().Single();
    Check(liveCapture.IsError != true && renderedImage.MimeType == "image/png" &&
          renderedImage.DecodedData.Span[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
          captureJson.GetProperty("receipt").GetProperty("simultaneous").GetBoolean() == false &&
          captureJson.GetProperty("receipt").GetProperty("rendererUpdateCounter").GetInt64() == 75 &&
          captureJson.GetProperty("receipt").GetProperty("tickRelationship").GetString()!.Contains("incomparable-domains", StringComparison.Ordinal),
        "capture_view should return a bounded PNG with exact pose, run, structural tick, and separate renderer clock metadata");
    Check(!File.Exists(requestFile), "MCP should remove its single-use request after receipt reconciliation");
    Pass("request and reconcile a bounded virtual HoloView color frame through the MCP tool with a fake renderer");

    var missingBody = await client.CallToolAsync("query_voidwright_body", new Dictionary<string, object?>
    {
        ["agentId"] = "voidwright-core:missing"
    });
    Check(missingBody.IsError == true, "missing agent should be reported as an MCP tool error");
    var drive = await client.CallToolAsync("voidwright_drive_lab_target", new Dictionary<string, object?>
    {
        ["gpsTarget"] = "End Position"
    });
    var driveText = drive.Content.OfType<TextContentBlock>().Single().Text;
    Check(drive.IsError != true && driveText.Contains("\"status\": \"arrival-verified\"", StringComparison.Ordinal) &&
          driveText.Contains("\"arrival_verified\": true", StringComparison.Ordinal) &&
          driveText.Contains("vragecage.lab-trial-result.v1", StringComparison.Ordinal),
        "GPS-target tool should run only the pinned ordinary-grid depth trial and return its terminal receipt");
    var invalidDrive = await client.CallToolAsync("voidwright_drive_lab_target", new Dictionary<string, object?>
    {
        ["gpsTarget"] = "Obstacle Wall"
    });
    Check(invalidDrive.IsError == true, "GPS-target tool must reject non-platform targets before invoking VRageCage");
    var towerDrive = await client.CallToolAsync("voidwright_drive_lab_target", new Dictionary<string, object?>
    {
        ["gpsTarget"] = "Observation Towers"
    });
    Check(towerDrive.IsError == true, "observation towers must remain supervision-only and never be a drive target");
    Check(File.ReadAllLines(commandLogPath).Length == 2,
        "rejecting invalid and supervision-only targets must not invoke the lab CLI");
    var recordedCommands = File.ReadAllLines(commandLogPath);
    Check(recordedCommands.Length == 2 &&
          recordedCommands.All(line => line.Contains("lab-trial fixture-copy", StringComparison.Ordinal) &&
              line.Contains("--controller-id 333", StringComparison.Ordinal) && line.Contains("--depth-camera-id 444", StringComparison.Ordinal)) &&
          !recordedCommands[0].Contains("--execute", StringComparison.Ordinal) &&
          recordedCommands[1].Contains("--execute --expect-plan-sha256", StringComparison.Ordinal),
        "compatibility goal tool must use the reviewed, pinned VRageCage trial workflow");
    Pass("launch stdio MCP process, query body/wireframe, and complete a bounded normal-grid lab command");

    var trialPreview = await client.CallToolAsync("voidwright_lab_trial", new Dictionary<string, object?> { ["gpsTarget"] = "End Position" });
    var previewJson = JsonDocument.Parse(trialPreview.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(trialPreview.IsError != true && File.ReadAllLines(commandLogPath).Length == 3
        && !File.ReadAllLines(commandLogPath)[2].Contains("--execute", StringComparison.Ordinal), "Default trial request must preview only");
    var planRequestId = previewJson.GetProperty("plan").GetProperty("request").GetProperty("request_id").GetString();
    var planHash = previewJson.GetProperty("plan_sha256").GetString();
    var trialExecute = await client.CallToolAsync("voidwright_lab_trial", new Dictionary<string, object?>
    {
        ["gpsTarget"] = "End Position", ["action"] = "execute", ["requestId"] = planRequestId, ["expectedPlanSha256"] = planHash
    });
    Check(trialExecute.IsError != true && File.ReadAllLines(commandLogPath).Length == 4
        && File.ReadAllLines(commandLogPath)[3].Contains(planRequestId!, StringComparison.Ordinal), "Execute must preserve the preview's exact request identity");
    var invalidTrial = await client.CallToolAsync("voidwright_lab_trial", new Dictionary<string, object?>
    {
        ["gpsTarget"] = "End Position", ["action"] = "execute", ["requestId"] = planRequestId, ["expectedPlanSha256"] = "not-a-hash"
    });
    Check(invalidTrial.IsError == true && File.ReadAllLines(commandLogPath).Length == 4, "Invalid plan acknowledgement must not invoke the CLI");
    var journalStatus = await client.CallToolAsync("voidwright_lab_trial", new Dictionary<string, object?>
    { ["gpsTarget"] = "End Position", ["action"] = "status", ["requestId"] = planRequestId });
    Check(journalStatus.IsError != true && File.ReadAllLines(commandLogPath).Length == 5
        && File.ReadAllLines(commandLogPath)[4].Contains("lab-trial-status", StringComparison.Ordinal)
        && !File.ReadAllLines(commandLogPath)[4].Contains("--execute", StringComparison.Ordinal), "Status must read the journal without replaying execution");
    Pass("preview a lab trial without mutation, then execute with the exact request and plan hash over MCP");

    var activeLogPath = Path.Combine(fixture, "active-lab-cli-commands.log");
    var activeEnvironment = new Dictionary<string, string?>(environment)
    {
        ["VOIDWRIGHT_TEST_ACTIVE"] = "true",
        ["VOIDWRIGHT_TEST_CALL_LOG"] = activeLogPath
    };
    var activeTransport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "Voidwright active-instance safety test",
        Command = "dotnet",
        Arguments = ["run", "--configuration", "Release", "--no-build", "--project", serverProject],
        WorkingDirectory = repositoryRoot,
        InheritEnvironmentVariables = false,
        EnvironmentVariables = activeEnvironment
    });
    await using var activeClient = await McpClient.CreateAsync(activeTransport);
    var activeDrive = await activeClient.CallToolAsync("voidwright_drive_lab_target", new Dictionary<string, object?>
    {
        ["gpsTarget"] = "End Position"
    });
    Check(activeDrive.IsError == true && File.ReadAllLines(activeLogPath).Length == 1,
        "an active lab instance must be refused before command queueing or interruption");
    var deniedEnrollment = await activeClient.CallToolAsync("voidwright_live_enroll_vehicle", new Dictionary<string, object?>
    { ["gridEntityId"] = 8877 });
    var deniedEnrollmentJson = JsonDocument.Parse(deniedEnrollment.Content.OfType<TextContentBlock>().Single().Text).RootElement;
    Check(deniedEnrollmentJson.GetProperty("error").GetString() == "authorization_unavailable" &&
          File.ReadAllLines(activeLogPath).Length == 1,
        "the live MCP surface must fail closed without a verified caller principal");
    Pass("refuse control when the pinned lab instance is already active");

    var floodEnvironment = new Dictionary<string, string?>(environment)
    {
        ["VOIDWRIGHT_TEST_OUTPUT_FLOOD"] = "true",
        ["VOIDWRIGHT_TEST_CALL_LOG"] = Path.Combine(fixture, "flood-lab-cli.log")
    };
    var floodTransport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "Voidwright bounded CLI output test", Command = "dotnet",
        Arguments = ["run", "--configuration", "Release", "--no-build", "--project", serverProject],
        WorkingDirectory = repositoryRoot, InheritEnvironmentVariables = false, EnvironmentVariables = floodEnvironment
    });
    await using var floodClient = await McpClient.CreateAsync(floodTransport);
    var floodClock = System.Diagnostics.Stopwatch.StartNew();
    var floodResult = await floodClient.CallToolAsync("voidwright_lab_trial", new Dictionary<string, object?> { ["gpsTarget"] = "End Position" });
    Check(floodResult.IsError == true && floodClock.Elapsed < TimeSpan.FromSeconds(10),
        "Over-limit output must interrupt its CLI child promptly instead of waiting for the command deadline");
    Pass("bound CLI output during reads and stop a flooding child promptly");

    var readOnlyEnvironment = new Dictionary<string, string?>(environment)
    {
        ["VOIDWRIGHT_READ_ONLY_ONLY"] = "true"
    };
    var readOnlyTransport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "Voidwright read-only interactive MCP surface",
        Command = "dotnet",
        Arguments = ["run", "--configuration", "Release", "--no-build", "--project", serverProject],
        WorkingDirectory = repositoryRoot,
        InheritEnvironmentVariables = false,
        EnvironmentVariables = readOnlyEnvironment
    });
    await using var readOnlyClient = await McpClient.CreateAsync(readOnlyTransport);
    var readOnlyTools = await readOnlyClient.ListToolsAsync();
    Check(readOnlyTools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal).SetEquals([
              "voidwright_snapshot_status", "list_voidwright_agents", "get_voidwright_character_observation", "query_voidwright_body",
              "capture_view", "observe_voidwright_vehicle",
              "create_voidwright_perspective", "move_voidwright_perspective",
              "look_at_voidwright_perspective", "inspect_voidwright_cell",
              "raycast_voidwright_perspective"]),
        "read-only mode should expose snapshot, body, and perspective tools without lab actuation");
    Pass("expose an observation-only MCP tool surface for interactive gameplay");
    await VehicleProfileChecks.Run(repositoryRoot, serverProject, snapshotPath, environment, Check, Pass);

    Console.WriteLine($"Voidwright Agent MCP: {passed} checks passed.");
}
finally
{
    Environment.SetEnvironmentVariable(SnapshotFileReader.PathEnvironmentVariable, null);
    Directory.Delete(fixture, recursive: true);
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Voidwright.csproj"))) return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate the Voidwright repository root from the test output directory.");
}

var captureOne = new DrivingLabCaptureRecord
{
    RunId = "run-a", SessionId = "session-a", StreamId = "grid-7", Source = "offline-fixture",
    Sequence = 1, CapturedAtUtc = DateTimeOffset.UtcNow, ClockDomain = "simulation",
    GridEntityId = 7, DroppedRecordsTotal = 0,
    Actuators = CaptureSection<ActuatorObservationFrame>.Observed(new(
        [new ActuatorState(70, "wheel", true, false, 0, false)])),
    Physics = CaptureSection<PhysicsObservationFrame>.Observed(new(1, 0, 0, 1, null, null)),
    SoftwareGeometry = CaptureSection<GeometryObservationFrame>.Partial(new("partial", 1, 2, false, false,
        "terrain and unrelated entities excluded"), "coverage_incomplete"),
    VisualFrame = CaptureSection<VisualObservationFrame>.Unavailable("native_framebuffer_unavailable")
};
var captureTwo = captureOne with { Sequence = 3, CapturedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(10) };
var captureLines = new MemoryStream();
captureLines.Write(DrivingLabCaptureValidator.SerializeLine(captureOne));
captureLines.WriteByte((byte)'\n');
captureLines.Write(DrivingLabCaptureValidator.SerializeLine(captureTwo));
captureLines.WriteByte((byte)'\n');
captureLines.Position = 0;
var captureReplay = DrivingLabCaptureReplayReader.Read(captureLines);
Check(captureReplay.Count == 2 && captureReplay[1].MissingSequencesBefore == 1 &&
      captureReplay[0].Record.HumanInput.Status == "unavailable" &&
      captureReplay[0].Record.Actuators.Value!.Ownership == "observed_only_no_control" &&
      captureReplay[0].Record.VisualFrame.Status == "unavailable",
    "capture replay must keep channels distinct, preserve unavailable states, and expose sequence gaps");
Pass("validate and replay bounded, passive Driving Lab capture records with explicit gaps");

var reversedCaptureLines = new MemoryStream();
reversedCaptureLines.Write(DrivingLabCaptureValidator.SerializeLine(captureTwo));
reversedCaptureLines.WriteByte((byte)'\n');
reversedCaptureLines.Write(DrivingLabCaptureValidator.SerializeLine(captureOne));
reversedCaptureLines.WriteByte((byte)'\n');
reversedCaptureLines.Position = 0;
var reversedCaptureRejected = false;
try { _ = DrivingLabCaptureReplayReader.Read(reversedCaptureLines); }
catch (InvalidDataException) { reversedCaptureRejected = true; }
Check(reversedCaptureRejected, "capture replay must reject duplicate or backwards sequence values");
var backwardsTickLines = new MemoryStream();
backwardsTickLines.Write(DrivingLabCaptureValidator.SerializeLine(captureOne with
{ SimulationTick = 20, SimulationTimeSeconds = 1.0 }));
backwardsTickLines.WriteByte((byte)'\n');
backwardsTickLines.Write(DrivingLabCaptureValidator.SerializeLine(captureOne with
{ Sequence = 2, SimulationTick = 19, SimulationTimeSeconds = 2.0 }));
backwardsTickLines.WriteByte((byte)'\n');
backwardsTickLines.Position = 0;
var backwardsTickRejected = false;
try { _ = DrivingLabCaptureReplayReader.Read(backwardsTickLines); }
catch (InvalidDataException) { backwardsTickRejected = true; }
Check(backwardsTickRejected, "capture replay must reject a backwards simulation tick");
var backwardsTimeLines = new MemoryStream();
backwardsTimeLines.Write(DrivingLabCaptureValidator.SerializeLine(captureOne with
{ SimulationTick = 20, SimulationTimeSeconds = 2.0 }));
backwardsTimeLines.WriteByte((byte)'\n');
backwardsTimeLines.Write(DrivingLabCaptureValidator.SerializeLine(captureOne with
{ Sequence = 2, SimulationTick = 21, SimulationTimeSeconds = 1.0 }));
backwardsTimeLines.WriteByte((byte)'\n');
backwardsTimeLines.Position = 0;
var backwardsTimeRejected = false;
try { _ = DrivingLabCaptureReplayReader.Read(backwardsTimeLines); }
catch (InvalidDataException) { backwardsTimeRejected = true; }
Check(backwardsTimeRejected, "capture replay must reject backwards simulation seconds");
var malformedCaptureRejected = false;
try { _ = DrivingLabCaptureValidator.SerializeLine(captureOne with { GridEntityId = 0 }); }
catch (InvalidDataException) { malformedCaptureRejected = true; }
Check(malformedCaptureRejected, "capture validation must reject invalid exact-grid identity");
var duplicateCaptureKeyRejected = false;
try
{
    _ = DrivingLabCaptureValidator.ParseLine(System.Text.Encoding.UTF8.GetBytes(
        "{\"wrapper\":{\"sample\":1,\"sample\":2}}"));
}
catch (InvalidDataException exception) when (exception.Message.Contains("duplicate object key", StringComparison.Ordinal))
{
    duplicateCaptureKeyRejected = true;
}
Check(duplicateCaptureKeyRejected,
    "capture replay accepted duplicate nested JSON keys that could overwrite evidence");
var actuatorCommandRejected = false;
try
{
    _ = DrivingLabCaptureValidator.SerializeLine(captureOne with
    {
        Actuators = CaptureSection<ActuatorObservationFrame>.Observed(new([], "control_owner"))
    });
}
catch (InvalidDataException) { actuatorCommandRejected = true; }
Check(actuatorCommandRejected, "capture validation must reject actuator ownership claims beyond observation");
Pass("fail closed on invalid capture identities and out-of-order replay records");

var selectionNow = DateTimeOffset.UtcNow;
var selectionRequest = new DrivingLabPassiveSelectionRequest(
    true, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
    202, selectionNow, selectionNow.AddMinutes(5), 10, 100_000, 64 * 1024 * 1024);
var passiveInventory = new DrivingLabPassiveGridInventory(
    selectionRequest.SessionId, selectionRequest.RunId, selectionNow, 12_000,
    true, [101, 202, 303]);
Check(DrivingLabPassiveSelectionContract.TryBind(selectionRequest, passiveInventory,
        selectionNow, out var boundSelection) == DrivingLabSelectionRejection.None &&
      boundSelection is { GridEntityId: 202, PermissionScope: "read_only_observation", MaximumSampleRateHz: 10 },
    "explicit passive selection should bind only the exact session/run/grid and bounded read-only limits");
Check(DrivingLabPassiveSelectionContract.TryBind(selectionRequest with { ExplicitOptIn = false },
        passiveInventory, selectionNow, out _) == DrivingLabSelectionRejection.MissingOptIn,
    "selection without explicit user opt-in was accepted");
Check(DrivingLabPassiveSelectionContract.TryBind(selectionRequest with { SessionId = "cccccccccccccccccccccccccccccccc" },
        passiveInventory, selectionNow, out _) == DrivingLabSelectionRejection.InventoryIdentityMismatch &&
      DrivingLabPassiveSelectionContract.TryBind(selectionRequest with { RunId = "cccccccccccccccccccccccccccccccc" },
        passiveInventory, selectionNow, out _) == DrivingLabSelectionRejection.InventoryIdentityMismatch,
    "selection from another session or run was accepted");
Check(DrivingLabPassiveSelectionContract.TryBind(selectionRequest with { GridEntityId = 999 },
        passiveInventory, selectionNow, out _) == DrivingLabSelectionRejection.MissingGrid &&
      DrivingLabPassiveSelectionContract.TryBind(selectionRequest,
        passiveInventory with { GridEntityIds = [101, 202, 202] }, selectionNow, out _) ==
            DrivingLabSelectionRejection.AmbiguousGrid,
    "missing or ambiguous exact grid identity was accepted");
Check(DrivingLabPassiveSelectionContract.TryBind(selectionRequest,
        passiveInventory with { Complete = false }, selectionNow, out _) == DrivingLabSelectionRejection.IncompleteInventory &&
      DrivingLabPassiveSelectionContract.TryBind(selectionRequest,
        passiveInventory with { CapturedAtUtc = selectionNow.AddSeconds(-6) }, selectionNow, out _) ==
            DrivingLabSelectionRejection.StaleInventory,
    "incomplete or stale passive inventory was accepted");
Check(DrivingLabPassiveSelectionContract.TryBind(selectionRequest with { ExpiresAtUtc = selectionNow },
        passiveInventory, selectionNow, out _) == DrivingLabSelectionRejection.ExpiredOptIn &&
      DrivingLabPassiveSelectionContract.TryBind(selectionRequest with { MaximumSampleRateHz = 11 },
        passiveInventory, selectionNow, out _) == DrivingLabSelectionRejection.InvalidLimits &&
      DrivingLabPassiveSelectionContract.TryBind(selectionRequest with { MaximumBufferedRecords = 100_001 },
        passiveInventory, selectionNow, out _) == DrivingLabSelectionRejection.InvalidLimits &&
      DrivingLabPassiveSelectionContract.TryBind(selectionRequest with { MaximumBufferedBytes = 64 * 1024 * 1024 + 1 },
        passiveInventory, selectionNow, out _) == DrivingLabSelectionRejection.InvalidLimits,
    "expired opt-in or unbounded sample/buffer limits were accepted");
Pass("bind explicit passive Driving Lab selection to one fresh exact session/run/grid with bounded rates and buffers");

var playerConsentRequest = new DrivingLabPlayerConsentRequest(
    true, 12345, selectionRequest.SessionId, selectionRequest.RunId,
    selectionRequest.GridEntityId, selectionNow, selectionNow.AddMinutes(5),
    10, 100_000, 64 * 1024 * 1024);
var playerInventory = new DrivingLabPlayerEnrollmentInventory(
    passiveInventory, [playerConsentRequest.AuthenticatedPlayerIdentityId, 987654321],
    playerConsentRequest.GridEntityId, [playerConsentRequest.AuthenticatedPlayerIdentityId], true, true,
    [playerConsentRequest.AuthenticatedPlayerIdentityId], true);
Check(DrivingLabPlayerConsentContract.TryBind(playerConsentRequest, playerInventory,
        selectionNow, out var boundPlayerConsent) == DrivingLabPlayerConsentRejection.None &&
      boundPlayerConsent is { PlayerIdentityId: 12345, PermissionScope: "read_only_observation" } &&
      boundPlayerConsent.Selection.GridEntityId == selectionRequest.GridEntityId,
    "player consent did not bind authenticated player to one exact grid with read-only scope");
Check(DrivingLabPlayerConsentContract.TryBind(playerConsentRequest with { ExplicitOptIn = false },
        playerInventory, selectionNow, out _) == DrivingLabPlayerConsentRejection.MissingOptIn &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest with { AuthenticatedPlayerIdentityId = 0 },
        playerInventory, selectionNow, out _) == DrivingLabPlayerConsentRejection.InvalidPlayerIdentity,
    "player enrollment accepted missing opt-in or an invalid authenticated identity");
Check(DrivingLabPlayerConsentContract.TryBind(playerConsentRequest,
        playerInventory with { ConnectedHumanPlayerIdentityIds = [42] },
        selectionNow, out _) == DrivingLabPlayerConsentRejection.PlayerNotConnected &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest,
        playerInventory with { AuthenticatedIdentityVerified = false }, selectionNow, out _) ==
            DrivingLabPlayerConsentRejection.IncompleteOccupantInventory &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest,
        playerInventory with { OccupantInventoryComplete = false }, selectionNow, out _) ==
            DrivingLabPlayerConsentRejection.IncompleteOccupantInventory &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest,
        playerInventory with { GridOccupantPlayerIdentityIds = [playerConsentRequest.AuthenticatedPlayerIdentityId, 42] },
        selectionNow, out _) == DrivingLabPlayerConsentRejection.MultipleGridOccupants,
    "player enrollment did not permit bystanders or fail closed on ambiguous occupants");
Check(DrivingLabPlayerConsentContract.TryBind(playerConsentRequest,
        playerInventory with { GridControllerHumanPlayerIdentityIds = [12345, 42] }, selectionNow, out _) ==
            DrivingLabPlayerConsentRejection.OtherHumanControlsGrid &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest,
        playerInventory with { ControllerInventoryComplete = false }, selectionNow, out _) ==
            DrivingLabPlayerConsentRejection.IncompleteOccupantInventory,
    "player enrollment accepted another human controller or incomplete controller inventory");
Check(DrivingLabPlayerConsentContract.TryBind(playerConsentRequest with { AuthenticatedPlayerIdentityId = 42 },
        playerInventory, selectionNow, out _) == DrivingLabPlayerConsentRejection.PlayerNotConnected &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest,
        playerInventory with { GridOccupantPlayerIdentityIds = [42] }, selectionNow, out _) ==
            DrivingLabPlayerConsentRejection.PlayerNotOccupyingGrid &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest,
        playerInventory with { OccupantGridEntityId = 303 }, selectionNow, out _) ==
            DrivingLabPlayerConsentRejection.PlayerNotOccupyingGrid &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest with { GridEntityId = 999 },
        playerInventory with { OccupantGridEntityId = 999 }, selectionNow, out _) == DrivingLabPlayerConsentRejection.GridSelectionRejected,
    "player enrollment accepted a wrong player or grid identity");
Check(DrivingLabPlayerConsentContract.TryBind(playerConsentRequest with { ExpiresAtUtc = selectionNow },
        playerInventory, selectionNow, out _) == DrivingLabPlayerConsentRejection.GridSelectionRejected &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest with { MaximumSampleRateHz = 11 },
        playerInventory, selectionNow, out _) == DrivingLabPlayerConsentRejection.GridSelectionRejected &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest with { MaximumBufferedRecords = 100_001 },
        playerInventory, selectionNow, out _) == DrivingLabPlayerConsentRejection.GridSelectionRejected &&
      DrivingLabPlayerConsentContract.TryBind(playerConsentRequest with { MaximumBufferedBytes = 64 * 1024 * 1024 + 1 },
        playerInventory, selectionNow, out _) == DrivingLabPlayerConsentRejection.GridSelectionRejected,
    "player consent accepted expired consent or exceeded sample/record/byte bounds");
Pass("bind revocable player consent to authenticated identity, exact occupied grid, and bounded read-only scope");
Check(DrivingLabConsentWire.TryReadRequest(DrivingLabConsentWire.CreateStartRequest(), out var startOperation) &&
      startOperation == DrivingLabConsentWire.StartRequest &&
      DrivingLabConsentWire.TryReadRequest(DrivingLabConsentWire.CreateStopRequest(), out var stopOperation) &&
      stopOperation == DrivingLabConsentWire.StopRequest &&
      !DrivingLabConsentWire.TryReadRequest([DrivingLabConsentWire.StartRequest, 1], out _) &&
      DrivingLabConsentWire.TryReadResult(DrivingLabConsentWire.CreateResult(true, "started"),
          out var acceptedResult, out var resultMessage) && acceptedResult && resultMessage == "started" &&
      DrivingLabConsentWire.TryReadResult(DrivingLabConsentWire.CreateResult(false, "rejected"),
          out var rejectedResult, out _) && !rejectedResult,
    "player consent UI wire accepted an unbounded/malformed identity payload or lost authenticated responses");
Pass("bound explicit client opt-in/revocation messages without accepting player identity from the client");

var serverNow = selectionNow.UtcDateTime;
DrivingLabPassiveConsentRequest CreateServerConsentRequest(
    long playerId = 12345, long gridId = 202, DateTime? expiresAt = null,
    int sampleRate = 10, int records = 100_000, int bytes = 64 * 1024 * 1024) => new()
{
    ExplicitOptIn = true,
    AuthenticatedPlayerIdentityId = playerId,
    SessionId = selectionRequest.SessionId,
    RunId = selectionRequest.RunId,
    GridEntityId = gridId,
    ConfirmedAtUtc = serverNow,
    ExpiresAtUtc = expiresAt ?? serverNow.AddMinutes(5),
    MaximumSampleRateHz = sampleRate,
    MaximumBufferedRecords = records,
    MaximumBufferedBytes = bytes
};
DrivingLabPassiveIdentityInventory CreateServerInventory(
    IReadOnlyList<long>? connectedIds = null, IReadOnlyList<long>? occupantIds = null,
    IReadOnlyList<long>? controllerIds = null, bool controllerInventoryComplete = true) => new()
{
    SessionId = selectionRequest.SessionId,
    RunId = selectionRequest.RunId,
    CapturedAtUtc = serverNow,
    SimulationTick = 12_000,
    Complete = true,
    IdentityVerified = true,
    GridEntityIds = [selectionRequest.GridEntityId],
    ConnectedHumanIdentityIds = connectedIds ?? [12345],
    OccupantGridEntityId = selectionRequest.GridEntityId,
    GridOccupantIdentityIds = occupantIds ?? [12345],
    GridControllerHumanIdentityIds = controllerIds ?? [12345],
    ControllerInventoryComplete = controllerInventoryComplete
};
var serverConsentRequest = CreateServerConsentRequest();
var serverInventory = CreateServerInventory();
Check(DrivingLabPassiveTelemetryPolicy.TryBind(serverConsentRequest, serverInventory,
        serverNow, out var serverSelection) == DrivingLabPassiveTelemetryRejection.None &&
      serverSelection is { PlayerIdentityId: 12345, GridEntityId: 202, PermissionScope: "read_only_observation" },
    "server passive policy did not bind authenticated identity to exact occupied grid");
Check(DrivingLabPassiveTelemetryPolicy.TryBind(CreateServerConsentRequest(playerId: 42),
        serverInventory, serverNow, out _) == DrivingLabPassiveTelemetryRejection.PlayerMismatch &&
      DrivingLabPassiveTelemetryPolicy.TryBind(CreateServerConsentRequest(gridId: 999),
        serverInventory, serverNow, out _) == DrivingLabPassiveTelemetryRejection.GridMismatch &&
      DrivingLabPassiveTelemetryPolicy.TryBind(serverConsentRequest,
        CreateServerInventory(connectedIds: [12345, 42]), serverNow, out var bystanderSelection) ==
            DrivingLabPassiveTelemetryRejection.None && bystanderSelection?.PlayerIdentityId == 12345 &&
      DrivingLabPassiveTelemetryPolicy.TryBind(serverConsentRequest,
        CreateServerInventory(occupantIds: [12345, 42]), serverNow, out _) ==
            DrivingLabPassiveTelemetryRejection.MultipleOccupants &&
      DrivingLabPassiveTelemetryPolicy.TryBind(serverConsentRequest,
        CreateServerInventory(controllerIds: [12345, 42]), serverNow, out _) ==
            DrivingLabPassiveTelemetryRejection.OtherHumanControlsGrid,
    "server passive policy accepted player/grid mismatch or ambiguous occupants");
Check(DrivingLabPassiveTelemetryPolicy.TryBind(CreateServerConsentRequest(expiresAt: serverNow),
        serverInventory, serverNow, out _) == DrivingLabPassiveTelemetryRejection.ExpiredConsent &&
      DrivingLabPassiveTelemetryPolicy.TryBind(CreateServerConsentRequest(sampleRate: 11),
        serverInventory, serverNow, out _) == DrivingLabPassiveTelemetryRejection.InvalidLimits &&
      DrivingLabPassiveTelemetryPolicy.TryBind(CreateServerConsentRequest(records: 100_001),
        serverInventory, serverNow, out _) == DrivingLabPassiveTelemetryRejection.InvalidLimits &&
      DrivingLabPassiveTelemetryPolicy.TryBind(CreateServerConsentRequest(bytes: 64 * 1024 * 1024 + 1),
        serverInventory, serverNow, out _) == DrivingLabPassiveTelemetryRejection.InvalidLimits,
    "server passive policy accepted expired consent or exceeded rate/storage caps");
Check(DrivingLabPassiveTelemetryPolicy.TryBind(CreateServerConsentRequest(records: 2, bytes: 1024),
        serverInventory, serverNow, out var tinyServerSelection) == DrivingLabPassiveTelemetryRejection.None &&
      DrivingLabPassiveTelemetryPolicy.CanBuffer(tinyServerSelection!, 0, 0, 800) &&
      DrivingLabPassiveTelemetryPolicy.CanBuffer(tinyServerSelection!, 1, 800, 224) &&
      !DrivingLabPassiveTelemetryPolicy.CanBuffer(tinyServerSelection!, 1, 800, 225) &&
      !DrivingLabPassiveTelemetryPolicy.CanBuffer(tinyServerSelection!, 2, 0, 1),
    "server passive adapter exceeded record or byte capacity");
Check(DrivingLabPassiveTelemetryPolicy.ValidateCurrentBinding(serverSelection!, serverInventory,
        serverNow.AddSeconds(1)) == DrivingLabPassiveTelemetryRejection.None &&
      DrivingLabPassiveTelemetryPolicy.ValidateCurrentBinding(serverSelection!,
        CreateServerInventory(connectedIds: [12345, 42]), serverNow.AddSeconds(1)) ==
            DrivingLabPassiveTelemetryRejection.None &&
      DrivingLabPassiveTelemetryPolicy.ValidateCurrentBinding(serverSelection!,
        CreateServerInventory(controllerIds: [12345, 42]), serverNow.AddSeconds(1)) ==
            DrivingLabPassiveTelemetryRejection.OtherHumanControlsGrid &&
      DrivingLabPassiveTelemetryPolicy.ValidateCurrentBinding(serverSelection!,
        CreateServerInventory(controllerInventoryComplete: false), serverNow.AddSeconds(1)) ==
            DrivingLabPassiveTelemetryRejection.IncompleteInventory,
    "server passive policy did not revalidate bystanders and exact vehicle control");
var exportLine = DrivingLabPassiveTelemetryRecordFormatter.Format(serverSelection!,
    new DrivingLabPassiveTelemetryFrame
    {
        CapturedAtUtc = serverNow,
        SimulationTick = 12000,
        SimulationTimeSeconds = 600,
        CockpitEntityId = 303,
        CockpitDefinitionType = "MyObjectBuilder_Cockpit",
        CockpitSubtypeId = "SmallBlockCapCockpit",
        PositionX = 1, PositionY = 2, PositionZ = 3,
        RightX = 1, UpY = 1, ForwardZ = 1,
        VelocityX = 1, VelocityY = -2, VelocityZ = 0,
        SpeedMetersPerSecond = Math.Sqrt(5),
        WheelStates = [new DrivingLabPassiveWheelTelemetry
        {
            EntityId = 220, BrakingEnabled = true, ActiveBrake = false, Handbrake = false,
            SteeringOverride = 0.25, PropulsionOverride = 0.5
        }]
    }, 1, 3);
var exportedRecord = DrivingLabCaptureValidator.ParseLine(Encoding.UTF8.GetBytes(exportLine));
Check(exportedRecord.Source == "server-passive-grid" && exportedRecord.GridEntityId == 202 &&
      exportedRecord.CapturedAtUtc == new DateTimeOffset(serverNow) && exportedRecord.SimulationTick == 12000 &&
      exportedRecord.DroppedRecordsTotal == 3 && exportedRecord.Physics.Value?.SpeedMetersPerSecond == Math.Sqrt(5) &&
      exportedRecord.Pose.Value?.PositionX == 1 && exportedRecord.HumanInput.Status == "unavailable" &&
      exportedRecord.Actuators.Value?.Ownership == "observed_only_no_control" &&
      exportedRecord.Actuators.Value.Items[0].ActiveBrake == false &&
      exportedRecord.Actuators.Value.Items[0].SteeringOverride == 0.25 &&
      exportedRecord.Actuators.Value.Items[0].PropulsionOverride == 0.5,
    "server passive export did not preserve source, freshness, drop count, motion, pose, or read-only wheel state");
Check(!exportLine.Contains("987654321", StringComparison.Ordinal) &&
      !exportLine.Contains("playerIdentityId", StringComparison.Ordinal),
    "passive telemetry export included a connected participant identity");
Check(exportLine.Contains("\"driverSeat\":{\"status\":\"observed\",\"reason\":null,\"value\":{\"blockEntityId\":303,\"definitionType\":\"MyObjectBuilder_Cockpit\",\"subtypeId\":\"SmallBlockCapCockpit\"}}", StringComparison.Ordinal),
    "passive telemetry export omitted the occupied SmallBlockCapCockpit identity");
serverSelection!.Revoke();
Check(DrivingLabPassiveTelemetryPolicy.ValidateCurrentBinding(serverSelection, serverInventory,
        serverNow.AddSeconds(1)) == DrivingLabPassiveTelemetryRejection.Revoked,
    "server passive policy continued after explicit revocation");
Pass("validate server-side authenticated player binding, ambiguity, revocation, freshness, and caps offline");

var selectedCapture = captureOne with
{
    RunId = selectionRequest.RunId,
    SessionId = selectionRequest.SessionId,
    StreamId = "grid-202",
    GridEntityId = selectionRequest.GridEntityId,
    CapturedAtUtc = selectionNow,
    Sequence = 1
};
var boundedCapture = new DrivingLabCaptureBuffer(boundSelection!);
var invalidBoundSelectionRejected = false;
try
{
    _ = new DrivingLabCaptureBuffer(boundSelection! with { MaximumSampleRateHz = 0 });
}
catch (ArgumentException)
{
    invalidBoundSelectionRejected = true;
}
Check(invalidBoundSelectionRejected,
    "capture buffer accepted a forged bound selection with an invalid sample rate");
Check(boundedCapture.TryAppend(selectedCapture, selectionNow) == DrivingLabCaptureAppendRejection.None &&
      boundedCapture.TryAppend(selectedCapture with
      {
          Sequence = 2, CapturedAtUtc = selectionNow.AddMilliseconds(50)
      }, selectionNow.AddMilliseconds(50)) == DrivingLabCaptureAppendRejection.RateLimit,
    "capture buffer failed exact-grid append or failed to enforce its sample-rate ceiling");
var simulationOrderedBuffer = new DrivingLabCaptureBuffer(boundSelection!);
Check(simulationOrderedBuffer.TryAppend(selectedCapture with
      { SimulationTick = 100, SimulationTimeSeconds = 5.0 }, selectionNow) == DrivingLabCaptureAppendRejection.None &&
      simulationOrderedBuffer.TryAppend(selectedCapture with
      { Sequence = 2, CapturedAtUtc = selectionNow.AddMilliseconds(100), SimulationTick = 99, SimulationTimeSeconds = 6.0 },
          selectionNow.AddMilliseconds(100)) == DrivingLabCaptureAppendRejection.SimulationOrder &&
      simulationOrderedBuffer.TryAppend(selectedCapture with
      { Sequence = 2, CapturedAtUtc = selectionNow.AddMilliseconds(100), SimulationTick = 101, SimulationTimeSeconds = 4.0 },
          selectionNow.AddMilliseconds(100)) == DrivingLabCaptureAppendRejection.SimulationOrder,
    "capture buffer accepted a backwards simulation tick or simulation time");
Check(boundedCapture.TryAppend(selectedCapture with
      {
          Sequence = 2, GridEntityId = 203, CapturedAtUtc = selectionNow.AddMilliseconds(100)
      }, selectionNow.AddMilliseconds(100)) == DrivingLabCaptureAppendRejection.IdentityMismatch &&
      boundedCapture.TryAppend(selectedCapture with
      {
          Sequence = 2, CapturedAtUtc = selectionNow.AddMilliseconds(100)
      }, selectionNow.AddMilliseconds(100)) == DrivingLabCaptureAppendRejection.None,
    "capture buffer accepted another grid or rejected the next properly spaced exact-grid sample");
using (var boundedReplayStream = new MemoryStream(boundedCapture.ToArray()))
    Check(DrivingLabCaptureReplayReader.Read(boundedReplayStream).Count == 2,
        "bounded capture buffer did not produce replayable JSONL");

var oneRecordRequest = selectionRequest with { MaximumBufferedRecords = 1 };
DrivingLabPassiveSelectionContract.TryBind(oneRecordRequest, passiveInventory,
    selectionNow, out var oneRecordSelection);
var oneRecordBuffer = new DrivingLabCaptureBuffer(oneRecordSelection!);
Check(oneRecordBuffer.TryAppend(selectedCapture, selectionNow) == DrivingLabCaptureAppendRejection.None &&
      oneRecordBuffer.TryAppend(selectedCapture with
      {
          Sequence = 2, CapturedAtUtc = selectionNow.AddMilliseconds(100)
      }, selectionNow.AddMilliseconds(100)) == DrivingLabCaptureAppendRejection.RecordLimit,
    "capture buffer exceeded its selected record limit");

var oneKilobyteRequest = selectionRequest with { MaximumBufferedBytes = 1024 };
DrivingLabPassiveSelectionContract.TryBind(oneKilobyteRequest, passiveInventory,
    selectionNow, out var oneKilobyteSelection);
var oneKilobyteBuffer = new DrivingLabCaptureBuffer(oneKilobyteSelection!);
var oversizedBoundRecord = selectedCapture with
{
    SoftwareGeometry = CaptureSection<GeometryObservationFrame>.Observed(
        new("partial", 1, 2, false, false, new string('x', 1200)))
};
Check(oneKilobyteBuffer.TryAppend(oversizedBoundRecord, selectionNow) == DrivingLabCaptureAppendRejection.ByteLimit,
    "capture buffer exceeded its selected byte limit");
var oversizedLineBuffer = new DrivingLabCaptureBuffer(boundSelection!);
var oversizedLineRecord = selectedCapture with
{
    SoftwareGeometry = CaptureSection<GeometryObservationFrame>.Observed(
        new("partial", 1, 2, false, false, new string('x', DrivingLabCaptureValidator.MaximumLineBytes)))
};
Check(oversizedLineBuffer.TryAppend(oversizedLineRecord, selectionNow) ==
      DrivingLabCaptureAppendRejection.InvalidRecord,
    "capture producer serialized a record beyond the one MiB line limit");
Pass("enforce exact-grid identity, sample rate, record count, and byte cap in passive capture buffer");

var passiveSource = new FakePassiveGridObservationSource();
var noOptInResult = DrivingLabPassiveCaptureProducer.TryStart(
    selectionRequest with { ExplicitOptIn = false }, passiveInventory, passiveSource,
    selectionNow, out _);
Check(noOptInResult == DrivingLabSelectionRejection.MissingOptIn,
    "passive producer started without explicit opt-in");
var producerRequest = selectionRequest with { MaximumBufferedRecords = 4 };
Check(DrivingLabPassiveCaptureProducer.TryStart(producerRequest, passiveInventory,
        passiveSource, selectionNow, out var passiveProducer) == DrivingLabSelectionRejection.None &&
      passiveProducer is not null,
    "passive producer did not bind the trusted exact-grid selection");
passiveSource.Sample = new DrivingLabPassiveGridSample(
    producerRequest.SessionId, producerRequest.RunId, producerRequest.GridEntityId,
    selectionNow, 12_000, 600.0,
    new PhysicsObservationFrame(1, -2, 0, Math.Sqrt(5), 1.0, 12_000),
    new PoseObservationFrame(10, 20, 30, 1, 0, 0, 0, 1, 0, 0, 0, 1),
    new GridStateObservationFrame(false, "Large", 24, true),
    [new ActuatorState(220, "wheel", true, false, 0, false)]);
Check(passiveProducer!.TryCapture(selectionNow, out var capturedPassive) ==
      DrivingLabPassiveCaptureRejection.None && capturedPassive is not null &&
      passiveSource.RequestedGridIds.SequenceEqual([producerRequest.GridEntityId]) &&
      capturedPassive.GridEntityId == producerRequest.GridEntityId &&
      capturedPassive.Physics.Status == "observed" && capturedPassive.Pose.Status == "observed" &&
      capturedPassive.GridState.Value?.BlockCount == 24,
    "passive producer did not sample the exact selected grid's physics, pose, and grid state");
Check(capturedPassive!.HumanInput.Status == "unavailable" &&
      capturedPassive.HumanInput.Reason == "human_intent_unavailable_server_indicators_mix_autopilot" &&
      capturedPassive.Actuators.Value?.Ownership == "observed_only_no_control",
    "passive producer inferred human intent or actuator ownership");
Check(DrivingLabPlayerPassiveCapture.TryStart(playerConsentRequest, playerInventory,
        passiveSource, selectionNow, out var playerCapture) == DrivingLabPlayerConsentRejection.None &&
      playerCapture is { PermissionScope: "read_only_observation", PlayerIdentityId: 12345 } &&
      playerCapture.TryCapture(selectionNow, out var playerRecord) == DrivingLabPlayerCaptureRejection.None &&
      playerRecord is { Source: "server-passive-grid", GridEntityId: 202 } &&
      playerRecord.HumanInput.Status == "unavailable" &&
      playerRecord.HumanInput.Reason == "human_intent_unavailable_server_indicators_mix_autopilot" &&
      playerRecord.Actuators.Value?.Ownership == "observed_only_no_control",
    "player capture lost server source attribution or implied human input/control authority");
var sourceCallsBeforeRevocation = passiveSource.RequestedGridIds.Count;
playerCapture!.Revoke();
Check(playerCapture.TryCapture(selectionNow.AddMilliseconds(100), out _) ==
          DrivingLabPlayerCaptureRejection.ConsentRevoked &&
      passiveSource.RequestedGridIds.Count == sourceCallsBeforeRevocation,
    "revoked player consent continued reading telemetry");
var callsBeforeRateLimit = passiveSource.RequestedGridIds.Count;
Check(passiveProducer.TryCapture(selectionNow.AddMilliseconds(50), out _) ==
      DrivingLabPassiveCaptureRejection.RateLimited &&
      passiveSource.RequestedGridIds.Count == callsBeforeRateLimit,
    "passive producer sampled faster than the selected 10 Hz ceiling");
passiveSource.Sample = passiveSource.Sample with
{
    SessionId = "cccccccccccccccccccccccccccccccc",
    CapturedAtUtc = selectionNow.AddMilliseconds(100),
    SimulationTick = 12_001
};
Check(passiveProducer.TryCapture(selectionNow.AddMilliseconds(100), out _) ==
      DrivingLabPassiveCaptureRejection.IdentityMismatch,
    "passive producer accepted a stale session identity");
var callsAfterIdentityMismatch = passiveSource.RequestedGridIds.Count;
Check(passiveProducer.TryCapture(selectionNow.AddMilliseconds(150), out _) ==
      DrivingLabPassiveCaptureRejection.RateLimited &&
      passiveSource.RequestedGridIds.Count == callsAfterIdentityMismatch,
    "passive producer polled the world observer faster than the rate limit after a rejected sample");
passiveSource.Sample = passiveSource.Sample with
{
    SessionId = producerRequest.SessionId,
    RunId = "dddddddddddddddddddddddddddddddd"
};
Check(passiveProducer.TryCapture(selectionNow.AddMilliseconds(200), out _) ==
      DrivingLabPassiveCaptureRejection.IdentityMismatch,
    "passive producer accepted a stale run identity");
passiveSource.Sample = passiveSource.Sample with
{
    RunId = producerRequest.RunId,
    GridEntityId = producerRequest.GridEntityId + 1
};
Check(passiveProducer.TryCapture(selectionNow.AddMilliseconds(300), out _) ==
      DrivingLabPassiveCaptureRejection.IdentityMismatch,
    "passive producer accepted a stale grid identity");
passiveSource.Sample = passiveSource.Sample with
{
    GridEntityId = producerRequest.GridEntityId,
    CapturedAtUtc = selectionNow.AddMilliseconds(400),
    SimulationTick = 12_004,
    Physics = new PhysicsObservationFrame(0, null, 0, null, null, 12_004)
};
Check(passiveProducer.TryCapture(selectionNow.AddMilliseconds(400), out var recoveredPassive) ==
      DrivingLabPassiveCaptureRejection.None && recoveredPassive?.DroppedRecordsTotal == 3 &&
      recoveredPassive.Physics.Status == "partial" &&
      recoveredPassive.Physics.Reason == "physics_fields_unavailable",
    "passive producer did not preserve drop accounting after stale identity samples");
var staleProducerSample = passiveSource.Sample with
{
    CapturedAtUtc = selectionNow.AddSeconds(-6),
    SimulationTick = 12_005
};
passiveSource.Sample = staleProducerSample;
Check(passiveProducer.TryCapture(selectionNow.AddMilliseconds(500), out _) ==
      DrivingLabPassiveCaptureRejection.StaleSample,
    "passive producer accepted stale observation data");
using (var passiveReplayStream = new MemoryStream(passiveProducer.ToArray()))
{
    var passiveReplay = DrivingLabCaptureReplayReader.Read(passiveReplayStream);
    Check(passiveProducer.RecordCount == 2 && passiveReplay.Count == 2 &&
          passiveProducer.BufferedBytes <= producerRequest.MaximumBufferedBytes,
        "passive producer exceeded its bounded buffer or emitted invalid JSONL");
}
var callsBeforeStop = passiveSource.RequestedGridIds.Count;
passiveProducer.Stop();
Check(passiveProducer.TryCapture(selectionNow.AddMilliseconds(600), out _) ==
      DrivingLabPassiveCaptureRejection.Stopped &&
      passiveSource.RequestedGridIds.Count == callsBeforeStop,
    "passive producer continued polling after explicit stop");
Pass("produce opt-in exact-grid passive observations with stale-binding rejection and no-control semantics");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

void Pass(string name)
{
    passed++;
    Console.WriteLine("PASS " + name);
}

static class TestFixtures
{
public static string CreatePlayerSnapshot(string status = "observed", int playerCount = 1, string? capturedAtUtc = null)
{
    var envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(Snapshot,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    envelope.CapturedAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow.ToString("O");
    envelope.PlayerObservation = new PlayerCharacterObservationSnapshot
    {
        SchemaVersion = 1,
        CapturedSimulationTick = envelope.CapturedSimulationTick,
        ConnectedHumanPlayerCount = playerCount,
        Status = status,
        CharacterObserved = status == "observed",
        CharacterEntityId = status == "observed" ? 987 : 0,
        Position = status == "observed" ? [12.5, 2.0, -8.25] : [],
        Right = status == "observed" ? [1.0, 0.0, 0.0] : [],
        Up = status == "observed" ? [0.0, 1.0, 0.0] : [],
        Forward = status == "observed" ? [0.0, 0.0, -1.0] : []
    };
    return JsonSerializer.Serialize(envelope);
}

public static string CreateRaySnapshot(string? capturedAtUtc = null, long tick = 60,
    string runId = "11111111111111111111111111111111", double[]? position = null,
    bool blockCoverageComplete = true)
{
    var envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(Snapshot,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    envelope.CapturedAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow.ToString("O");
    envelope.CapturedSimulationTick = tick;
    envelope.RunId = runId;
    var agent = envelope.Agents.Single();
    agent.CapturedSimulationTick = tick;
    agent.BlockCoverageComplete = blockCoverageComplete;
    var grid = agent.PhysicalGrids.Single();
    grid.Position = position ?? [3, 1, 1];
    grid.BlockCount = blockCoverageComplete ? 3 : 4;
    grid.Blocks =
    [
        MakeBlock(101, [2, 3, 5]),
        MakeBlock(202, [2, 0, 5]),
        MakeBlock(303, [-2, 0, 5])
    ];
    agent.BlocksObserved = 3;
    return JsonSerializer.Serialize(envelope);
}

public static string CreateMovedGridSnapshot()
{
    var envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(Snapshot,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    envelope.CapturedAtUtc = DateTimeOffset.UtcNow.ToString("O");
    envelope.CapturedSimulationTick = 61;
    var agent = envelope.Agents.Single();
    agent.CapturedSimulationTick = 61;
    var grid = agent.PhysicalGrids.Single();
    grid.Position = [13, 1, 1];
    grid.Right = [0, 0, 1];
    grid.Up = [0, 1, 0];
    grid.Forward = [-1, 0, 0];
    grid.LinearVelocity = [2, 0, 0];
    return JsonSerializer.Serialize(envelope);
}

public static string CreateIntegritySnapshot()
{
    var envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(Snapshot,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    envelope.CapturedAtUtc = DateTimeOffset.UtcNow.ToString("O");
    var agent = envelope.Agents.Single();
    agent.BlockCoverageComplete = false;
    agent.BlocksObserved = 1;
    var grid = agent.PhysicalGrids.Single();
    grid.BlockCount = 2;
    grid.Blocks[0].DamageRatio = 0.4f;
    grid.Blocks[0].BuildLevelRatio = 0.6f;
    grid.Blocks[0].IsFunctional = false;
    grid.Blocks[0].IsWorking = false;
    return JsonSerializer.Serialize(envelope);
}

private static BlockSnapshot MakeBlock(long id, int[] cell) => new()
{
    EntityId = id, DefinitionType = "MyObjectBuilder_CubeBlock", SubtypeId = "TestArmor",
    Name = "Known block " + id, Position = cell, Min = cell, Max = cell,
    Forward = "Forward", Up = "Up", BuildLevelRatio = 1, IsFunctional = true,
    IsWorking = true, WorldCenter = [0, 0, 0]
};

public const string FakeVrageCageCli = """
#!/bin/sh
set -eu
printf '%s\n' "$*" >> "$VOIDWRIGHT_TEST_CALL_LOG"
case "${2:-}" in
  lab-trial|lab-trial-status)
    python3 - "$@" <<'PY'
import json,os,sys
if os.environ.get('VOIDWRIGHT_TEST_OUTPUT_FLOOD')=='true':
    import time
    print('x'*1100000,flush=True);time.sleep(30)
args=sys.argv[1:]
def value(name): return args[args.index(name)+1]
if os.environ.get('VOIDWRIGHT_TEST_ACTIVE')=='true':
    print(json.dumps({'schema':'vragecage.lab-trial-error.v1','error':'Lab instance is active'}));sys.exit(1)
if args[1]=='lab-trial-status':
    request={'fixture_id':value('--fixture-id'),'request_id':value('--request-id'),'controller_id':'333','depth_camera_id':'444',
        'grid_name':'Ordinary Test Truck','target':'End Position','snapshot_relative_path':'Storage/exporter/agent-core-snapshot-v1.json'}
else:
    request={name.replace('-','_'):value('--'+name) for name in ('fixture-id','request-id','controller-id','depth-camera-id','grid-name','target','snapshot-relative-path')}
request['instance']='fixture-copy'
if args[1]=='lab-trial-status':
    result={'schema':'vragecage.lab-trial-status.v1','request_id':request['request_id'],'fixture_id':request['fixture_id'],'request':request,'phase':'terminal','status':'result-recorded'}
elif '--execute' in args:
    result={'schema':'vragecage.lab-trial-result.v1','request_id':request['request_id'],'fixture_id':request['fixture_id'],
        'plan_sha256':value('--expect-plan-sha256'),'status':'arrival-verified','success':True,'arrival_verified':True,'clean_shutdown':True}
else:
    result={'schema':'vragecage.lab-trial-preview.v1','plan_sha256':'a'*64,'plan':{'request':request,'grid':{'name':'Ordinary Test Truck'}}}
print(json.dumps(result, separators=(', ', ': ')))
PY
    ;;
  fixture-inspect)
    printf '%s\n' '{"schema":"vragecage.fixture-inspection.v1","instance":"fixture-copy","fixture_id":"11111111111111111111111111111111","grids":[{"entity_id":222,"name":"Ordinary Test Truck","static":false,"remote_controls":[{"entity_id":333}],"cameras":[{"entity_id":444}]}],"gps_targets":[{"name":"Yellow Zone"},{"name":"End Position"}]}'
    ;;
  status)
    if [ "${VOIDWRIGHT_TEST_ACTIVE:-false}" = "true" ]; then
      printf '%s\n' '{"schema":"vragecage.status.v1","service":{"ActiveState":"active"}}'
    else
      printf '%s\n' '{"schema":"vragecage.status.v1","service":{"ActiveState":"inactive"}}'
    fi
    ;;
  lab-drive-zone)
    printf '%s\n' '{"schema":"vragecage.lab-command.v1","action":"drive-to-coordinate-depth","fixture_id":"11111111111111111111111111111111","operation_id":"operation-test-001","controller_id":333,"depth_camera_id":444,"zone":"End Position"}'
    ;;
  run)
    printf '%s\n' 'Running as unit: test.service' '{"schema":"vragecage.operation.v1","status":"starting"}'
    ;;
  wait)
    printf '%s\n' '{"schema":"vragecage.magnetar-receipt.v1","passed":true,"lab_operation_id":"operation-test-001","lab_operation_status":"complete","lab_run_id":"run-test-001","clean_shutdown":false}'
    ;;
  stop)
    printf '%s\n' 'server stopped cleanly' '{"schema":"vragecage.operation.v1","status":"stopped","receipt":{"lab_operation_id":"operation-test-001","lab_operation_status":"complete","lab_run_id":"run-test-001","clean_shutdown":true}}'
    ;;
  *) exit 2 ;;
esac
""";

public const string Snapshot = """
{
  "schemaVersion": 1,
  "capturedAtUtc": "2026-10-09T12:00:00.0000000Z",
  "capturedSimulationTick": 60,
  "runId": "11111111111111111111111111111111",
  "agentsTruncated": false,
  "agents": [
    {
      "schemaVersion": 1,
      "identityScope": "world-local",
      "agentId": "voidwright-core:42",
      "capturedSimulationTick": 60,
      "coreEntityId": 42,
      "coreName": "Test Core",
      "rootGridEntityId": 10,
      "rootGridName": "Root",
      "corePosition": [1, 1, 1],
      "coreRight": [1, 0, 0],
      "coreUp": [0, 1, 0],
      "coreForward": [0, 0, 1],
      "physicalGridCount": 1,
      "blocksObserved": 1,
      "gridCoverageComplete": true,
      "blockCoverageComplete": true,
      "structureFingerprint": "000000000000002A",
      "physicalGrids": [
        {
          "entityId": 10,
          "name": "Root",
          "gridSizeClass": "Large",
          "gridSizeMeters": 2.5,
          "isStatic": false,
          "position": [3, 1, 1],
          "right": [0, 0, -1],
          "up": [0, 1, 0],
          "forward": [1, 0, 0],
          "linearVelocity": [0, 0, 0],
          "naturalGravity": [0, -9.81, 0],
          "blockCount": 1,
          "blocks": [
            {
              "entityId": 100,
              "definitionType": "MyObjectBuilder_CameraBlock",
              "subtypeId": "Camera",
              "name": "Front Camera",
              "position": [0, 0, 0],
              "min": [0, 0, 0],
              "max": [1, 0, 0],
              "forward": "Forward",
              "up": "Up",
              "buildLevelRatio": 1,
              "damageRatio": 0,
              "hasEnabledState": true,
              "enabled": true,
              "isFunctional": true,
              "isWorking": true,
              "worldCenter": [3, 1, 1]
            }
          ]
        }
      ]
    }
  ]
}
""";
}

sealed class FakePassiveGridObservationSource : IDrivingLabPassiveGridObservationSource
{
    public DrivingLabPassiveGridSample? Sample { get; set; }
    public List<long> RequestedGridIds { get; } = [];

    public bool TryObserveExactGrid(long gridEntityId, out DrivingLabPassiveGridSample? sample)
    {
        RequestedGridIds.Add(gridEntityId);
        sample = Sample;
        return sample is not null;
    }
}

sealed class TestVehiclePrincipalProvider(VerifiedVehiclePrincipal? principal) : IVerifiedVehiclePrincipalProvider
{
    public VerifiedVehiclePrincipal? Resolve() => principal;
}

sealed class TestLiveVehicleAdapter(long gridEntityId) : ILiveVehicleAdapter
{
    public long GridEntityId { get; } = gridEntityId;
    public List<string> AppliedCommands { get; } = [];
    public int StopCalls { get; private set; }
    public int ReleaseCalls { get; private set; }
    public object Observe() => "observed";
    public void ApplyGoal(string commandId, VehicleGoal goal) => AppliedCommands.Add(commandId);
    public void CancelGoal(string commandId) { }
    public VehicleStopOutcome Stop()
    {
        StopCalls++;
        return new VehicleStopOutcome(true, true, 0, 0, 0, false);
    }
    public void Release() => ReleaseCalls++;
}
