using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Voidwright.Core;

namespace Voidwright.AgentMcp;

internal static class LocalVehicleTestOperator
{
    private const string ExactInstance = LocalVehicleTestBindingPolicy.FreshInstance;

    public static Task RunAsync(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("Choose authorize, observe, steering, propulsion, drive, brake, neutral, stop, or release.");
        var steeringOnly = string.Equals(Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_STEERING_ONLY"), "true", StringComparison.OrdinalIgnoreCase);
        var isolatedClear = string.Equals(Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_ISOLATED_CLEAR"), "true", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST"), "1", StringComparison.Ordinal) ||
            !string.Equals(Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_INSTANCE"), ExactInstance, StringComparison.Ordinal) ||
            steeringOnly || !isolatedClear)
            throw new InvalidOperationException("The local vehicle test command is disabled or its exact clone and safety mode do not match.");
        if (steeringOnly && (args[0] == "propulsion" || args[0] == "drive"))
            throw new InvalidOperationException("Propulsion is hard-disabled in stationary-steering-only mode.");

        var directory = Environment.GetEnvironmentVariable("VOIDWRIGHT_LOCAL_TEST_MAILBOX_DIR");
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("The disposable instance WorldStorage mailbox is not configured.");
        var workerRoot = Environment.GetEnvironmentVariable("VRAGECAGE_WORKER_ROOT");
        if (string.IsNullOrWhiteSpace(workerRoot) || !Path.IsPathFullyQualified(workerRoot))
            throw new InvalidOperationException("The local test worker root is not configured.");
        var instanceRoot = Path.GetFullPath(Path.Combine(workerRoot, "instances", ExactInstance));
        var cloneRoot = instanceRoot + Path.DirectorySeparatorChar;
        var mailboxRoot = Path.GetFullPath(directory);
        if (!mailboxRoot.StartsWith(cloneRoot, StringComparison.Ordinal))
            throw new InvalidOperationException("The local test mailbox must remain inside the exact disposable clone.");
        var run = ReadActiveRun(instanceRoot);
        var gridId = ParseExactId("VRAGECAGE_LOCAL_VEHICLE_TEST_GRID_ID");
        var controllerId = ParseExactId("VRAGECAGE_LOCAL_VEHICLE_TEST_CONTROLLER_ID");
        var cameraId = ParseExactId("VRAGECAGE_LOCAL_VEHICLE_TEST_CAMERA_ID");
        if (!LocalVehicleTestBindingPolicy.IsExactRun(ExactInstance, run.RunId, run.RunId,
                gridId, controllerId, cameraId))
            throw new InvalidOperationException("The active receipt does not bind this local test to the pinned Blue Truck, controller, and camera.");
        var authorizationPath = Path.Combine(directory, LocalVehicleTestFileAdapter.AuthorizationFileName);
        if (args.Length > 0 && args[0] == "authorize")
        {
            RequireArguments(args, 1);
            var grantNow = DateTime.UtcNow;
            var newAuthorization = new LocalVehicleTestAuthorization(ExactInstance,
                run.RunId, run.FixtureId,
                "local-run-" + Guid.NewGuid().ToString("N"),
                gridId, controllerId, cameraId,
                grantNow.AddSeconds(ParseGrantLifetimeSeconds()));
            if (!LocalVehicleTestControlPolicy.IsAuthorized(newAuthorization, ExactInstance,
                    run.RunId, run.FixtureId, gridId, controllerId, cameraId, grantNow))
                throw new InvalidOperationException("The local run environment does not provide exact valid grid, controller, and camera bindings.");
            _ = new LocalVehicleTestFileAdapter(directory, newAuthorization);
            Console.WriteLine(JsonSerializer.Serialize(new { state = "local-run-authorized", newAuthorization.AuthorizationId,
                newAuthorization.Instance, newAuthorization.RunId, newAuthorization.FixtureId, newAuthorization.GridId,
                newAuthorization.ControllerId, newAuthorization.CameraId, newAuthorization.ExpiresUtc }));
            return Task.CompletedTask;
        }
        if (!LocalVehicleTestWire.TryParseAuthorization(File.ReadAllText(authorizationPath), out var authorization) ||
            authorization == null ||
            !LocalVehicleTestControlPolicy.IsAuthorized(authorization, ExactInstance,
                run.RunId, run.FixtureId, gridId, controllerId, cameraId, DateTime.UtcNow))
            throw new InvalidOperationException("No fresh exact run, fixture, grid, controller, camera, and expiry grant is present.");
        var adapter = new LocalVehicleTestFileAdapter(directory, authorization);
        var principal = new LocalVehicleTestPrincipalProvider(authorization);
        var runtime = new LiveVehicleRuntime(principal, grid => grid == authorization.GridId ? adapter : null);
        var now = DateTime.UtcNow;
        var session = runtime.Enroll(authorization.GridId, now);
        object result;
        switch (args[0])
        {
            case "observe":
                RequireArguments(args, 1);
                result = runtime.Observe(session, now);
                break;
            case "stop":
                RequireArguments(args, 1);
                result = runtime.Stop(session, now);
                break;
            case "release":
                RequireArguments(args, 1);
                runtime.Release(session, now);
                result = new { state = "release-requested" };
                break;
            case "steering":
                RequireArguments(args, 3);
                result = Submit(runtime, session, "steering", Parse(args[1]), 0, ParseSeconds(args[2]), now);
                break;
            case "propulsion":
                RequireArguments(args, 4);
                result = Submit(runtime, session, "propulsion", Parse(args[1]), Parse(args[2]), ParseSeconds(args[3]), now);
                break;
            case "drive":
                RequireArguments(args, 5);
                var driveCommandId = "local-" + Guid.NewGuid().ToString("N");
                adapter.ApplyLocalDriveCommand(driveCommandId, Parse(args[1]), Parse(args[2]),
                    Parse(args[3]), ParseSeconds(args[4]), now);
                result = new { CommandId = driveCommandId, SessionId = session,
                    GridEntityId = authorization.GridId, State = "ACTIVE",
                    AcceptedAtUtc = now, ExpiresAtUtc = now.AddSeconds(ParseSeconds(args[4])) };
                break;
            case "brake":
                RequireArguments(args, 1, 2);
                result = Submit(runtime, session, "brake", 1, 0,
                    args.Length == 2 ? ParseSeconds(args[1]) : 2, now);
                break;
            case "neutral":
                RequireArguments(args, 1, 2);
                result = Submit(runtime, session, "neutral", 0, 0,
                    args.Length == 2 ? ParseSeconds(args[1]) : 2, now);
                break;
            default:
                throw new ArgumentException("Unsupported local vehicle test command.");
        }
        Console.WriteLine(JsonSerializer.Serialize(result));
        return Task.CompletedTask;
    }

    private static (string RunId, string FixtureId) ReadActiveRun(string instanceRoot)
    {
        var activePath = Path.Combine(instanceRoot, ".vragecage-active-run.json");
        var labPath = Path.Combine(instanceRoot, ".vragecage-lab.json");
        var importPath = Path.Combine(instanceRoot, ".vragecage-world-import.json");
        var configPath = Path.Combine(instanceRoot, ".vragecage-local-vehicle-test.json");
        var ledgerPath = Path.Combine(instanceRoot, ".vragecage-diagnostic-instrumentation.json");
        foreach (var path in new[] { activePath, labPath, importPath, configPath, ledgerPath })
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Local test receipts must not be symbolic links.");
        using var active = JsonDocument.Parse(File.ReadAllText(activePath));
        using var lab = JsonDocument.Parse(File.ReadAllText(labPath));
        using var imported = JsonDocument.Parse(File.ReadAllText(importPath));
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        using var ledger = JsonDocument.Parse(File.ReadAllText(ledgerPath));
        var activeRoot = active.RootElement;
        var labRoot = lab.RootElement;
        var importRoot = imported.RootElement;
        var configRoot = config.RootElement;
        var ledgerRoot = ledger.RootElement;
        var runId = activeRoot.GetProperty("run_id").GetString() ?? string.Empty;
        var fixtureId = activeRoot.GetProperty("fixture_id").GetString() ?? string.Empty;
        var quarantine = Path.GetFullPath(activeRoot.GetProperty("quarantine").GetString() ?? string.Empty);
        var expectedQuarantine = Path.GetFullPath(Path.Combine(instanceRoot, "Quarantine", runId));
        if (activeRoot.GetProperty("schema").GetString() != "vragecage.lab-run.v1" ||
            activeRoot.GetProperty("authority").GetString() != "destructive-lab" ||
            activeRoot.GetProperty("instance").GetString() != ExactInstance ||
            !string.Equals(quarantine, expectedQuarantine, StringComparison.Ordinal) ||
            !System.Text.RegularExpressions.Regex.IsMatch(runId, "^[0-9a-f]{32}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(fixtureId, "^[0-9a-f]{32}$"))
            throw new InvalidOperationException("The active run receipt is not exact or is not confined to this clone.");
        if (labRoot.GetProperty("schema").GetString() != "vragecage.lab-authority.v1" ||
            labRoot.GetProperty("authority").GetString() != "destructive-lab" ||
            labRoot.GetProperty("disposable").ValueKind != JsonValueKind.True ||
            labRoot.GetProperty("engine").GetString() != "magnetar" ||
            labRoot.GetProperty("instance").GetString() != ExactInstance ||
            labRoot.GetProperty("source_tree_sha256").GetString() != "b1564710681a6d0bc901ab0437d59f475f62c1fabda7fdd6bd9198306a5982ed" ||
            importRoot.GetProperty("schema").GetString() != "vragecage.world-import.v1" ||
            importRoot.GetProperty("instance").GetString() != ExactInstance ||
            importRoot.GetProperty("engine").GetString() != "magnetar" ||
            importRoot.GetProperty("tree_sha256").GetString() != "b1564710681a6d0bc901ab0437d59f475f62c1fabda7fdd6bd9198306a5982ed" ||
            configRoot.GetProperty("instance").GetString() != ExactInstance ||
            configRoot.GetProperty("grid_id").GetInt64() != LocalVehicleTestBindingPolicy.GridId ||
            configRoot.GetProperty("controller_id").GetInt64() != LocalVehicleTestBindingPolicy.ControllerId ||
            configRoot.GetProperty("source_tree_sha256").GetString() != "b1564710681a6d0bc901ab0437d59f475f62c1fabda7fdd6bd9198306a5982ed" ||
            ledgerRoot.GetProperty("instance").GetString() != ExactInstance ||
            ledgerRoot.GetProperty("authority").GetString() != "disposable-clone-only" ||
            ledgerRoot.GetProperty("source_tree_sha256").GetString() != "b1564710681a6d0bc901ab0437d59f475f62c1fabda7fdd6bd9198306a5982ed")
            throw new InvalidOperationException("Local test clone, import, and instrumentation receipts do not match the pinned fixture.");
        var cameraId = configRoot.GetProperty("camera_id").GetInt64();
        if (!long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_CAMERA_ID"),
                NumberStyles.None, CultureInfo.InvariantCulture, out var runtimeCameraId) ||
            cameraId != LocalVehicleTestBindingPolicy.CameraIdFor(ExactInstance) || runtimeCameraId != cameraId ||
            !HasExactIds(configRoot.GetProperty("wheel_ids"), LocalVehicleTestBindingPolicy.WheelIds) ||
            !HasExactIds(configRoot.GetProperty("wheel_grid_ids"), LocalVehicleTestBindingPolicy.WheelGridIds))
            throw new InvalidOperationException("Local test camera or suspension-to-subgrid config differs from the pinned fixture.");
        var addedBlocks = ledgerRoot.GetProperty("added_blocks");
        var ledgerCameraMatches = addedBlocks.ValueKind == JsonValueKind.Array && addedBlocks.EnumerateArray().Any(block =>
            block.ValueKind == JsonValueKind.Object && block.TryGetProperty("subtype", out var subtype) &&
            subtype.GetString() == "SmallCameraBlock" && block.TryGetProperty("entity_id", out var entityId) &&
            long.TryParse(entityId.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
            parsed == cameraId && block.TryGetProperty("grid_id", out var grid) &&
            grid.GetString() == LocalVehicleTestBindingPolicy.GridId.ToString(CultureInfo.InvariantCulture));
        var sectorPath = Path.Combine(instanceRoot, "World", "SANDBOX_0_0_0_.sbs");
        var expectedSectorHash = ledgerRoot.GetProperty("sector_after_sha256").GetString();
        if (!ledgerCameraMatches || string.IsNullOrEmpty(expectedSectorHash) ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sectorPath))).ToLowerInvariant(),
                expectedSectorHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Local test camera ledger or instrumented sector hash does not match the exact clone.");
        var runReceiptPath = Path.Combine(quarantine, "run.json");
        var contextPath = Path.Combine(quarantine, "preflight-context-v1.txt");
        using var runReceipt = JsonDocument.Parse(File.ReadAllText(runReceiptPath));
        if (runReceipt.RootElement.GetProperty("run_id").GetString() != runId ||
            runReceipt.RootElement.GetProperty("fixture_id").GetString() != fixtureId ||
            !File.Exists(contextPath))
            throw new InvalidOperationException("The current run receipt or preflight context is missing or mismatched.");
        RequireActiveInstance();
        var suppliedRun = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID");
        var suppliedFixture = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID");
        if ((!string.IsNullOrEmpty(suppliedRun) && suppliedRun != runId) ||
            (!string.IsNullOrEmpty(suppliedFixture) && suppliedFixture != fixtureId))
            throw new InvalidOperationException("The requested run does not match the active run receipt.");
        return (runId, fixtureId);
    }

    private static bool HasExactIds(JsonElement value, long[] expected)
    {
        if (value.ValueKind != JsonValueKind.Array) return false;
        var observed = value.EnumerateArray().ToArray();
        if (observed.Length != expected.Length) return false;
        for (var index = 0; index < expected.Length; index++)
            if (observed[index].ValueKind != JsonValueKind.Number ||
                !observed[index].TryGetInt64(out var id) || id != expected[index]) return false;
        return true;
    }

    private static void RequireActiveInstance()
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "systemctl",
            Arguments = "--user is-active --quiet vragecage-" + ExactInstance + ".service",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (process == null) throw new InvalidOperationException("Could not verify the local test instance state.");
        process.WaitForExit(5000);
        if (!process.HasExited || process.ExitCode != 0)
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
            throw new InvalidOperationException("The pinned local test instance is not actively running.");
        }
    }

    private static object Submit(LiveVehicleRuntime runtime, string session, string kind,
        double value, double speed, int timeout, DateTime now)
    {
        var commandId = "local-" + Guid.NewGuid().ToString("N");
        return runtime.Submit(session, commandId, new VehicleGoal(kind, value, 0, 0, speed, timeout), now);
    }

    private static void RequireArguments(string[] args, params int[] allowedCounts)
    {
        if (!allowedCounts.Contains(args.Length))
            throw new ArgumentException("The local command has an invalid number of values.");
    }

    private static double Parse(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
            double.IsNaN(result) || double.IsInfinity(result))
            throw new ArgumentException("Command values must be finite invariant-culture numbers.");
        return result;
    }

    private static int ParseSeconds(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException("Timeout must be an integer number of seconds.");
        return result;
    }

    private static long ParseExactId(string name)
        => long.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None,
            CultureInfo.InvariantCulture, out var result) && result > 0 ? result : 0;

    private static int ParseGrantLifetimeSeconds()
        => int.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_AUTH_TIMEOUT_SECONDS"),
            NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 5 and <= 900
            ? seconds : 60;
}
