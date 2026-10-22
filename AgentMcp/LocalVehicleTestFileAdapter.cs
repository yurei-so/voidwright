using Voidwright.Core;

namespace Voidwright.AgentMcp;

/// <summary>Explicit local-only adapter over the disposable instance's WorldStorage mailbox.</summary>
public sealed class LocalVehicleTestFileAdapter : ILiveVehicleAdapter
{
    public const string AuthorizationFileName = "voidwright-local-test-authorization-v1.txt";
    public const string CommandFileName = "voidwright-local-test-command-v1.txt";
    public const string ReceiptFileName = "voidwright-local-test-receipt-v1.txt";
    private readonly string mailboxDirectory;
    private readonly LocalVehicleTestAuthorization authorization;

    public long GridEntityId => authorization.GridId;

    public LocalVehicleTestFileAdapter(string mailboxDirectory, LocalVehicleTestAuthorization authorization)
    {
        if (string.IsNullOrWhiteSpace(mailboxDirectory) || !Path.IsPathFullyQualified(mailboxDirectory))
            throw new ArgumentException("The local test mailbox directory must be an absolute path.", nameof(mailboxDirectory));
        this.mailboxDirectory = Path.GetFullPath(mailboxDirectory);
        this.authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        Directory.CreateDirectory(this.mailboxDirectory);
        WriteAtomic(AuthorizationFileName, LocalVehicleTestWire.FormatAuthorization(authorization));
    }

    public object Observe()
    {
        try { return File.ReadAllText(PathFor(ReceiptFileName)); }
        catch (FileNotFoundException) { return "no-game-receipt"; }
    }

    public void ApplyGoal(string commandId, VehicleGoal goal)
    {
        var now = DateTime.UtcNow;
        LocalVehicleTestCommandKind kind;
        double value;
        switch (goal.Type)
        {
            case "steering": kind = LocalVehicleTestCommandKind.Steering; value = goal.X; break;
            case "propulsion": kind = LocalVehicleTestCommandKind.Propulsion; value = goal.X; break;
            case "brake": kind = LocalVehicleTestCommandKind.Brake; value = goal.X; break;
            case "neutral": kind = LocalVehicleTestCommandKind.Neutral; value = 0; break;
            default: throw new ArgumentException("Unsupported local vehicle test command.", nameof(goal));
        }
        var command = new LocalVehicleTestCommand(commandId, authorization.Instance, authorization.RunId,
            authorization.FixtureId, authorization.GridId, authorization.ControllerId, kind, value,
            goal.MaximumSpeedMetersPerSecond, now, now.AddSeconds(goal.TimeoutSeconds));
        WriteAtomic(CommandFileName, LocalVehicleTestWire.FormatCommand(command));
    }

    /// <summary>Direct local-only combined steering/propulsion mailbox command; not exposed through MCP.</summary>
    public void ApplyLocalDriveCommand(string commandId, double propulsion, double steering,
        double maximumSpeedMetersPerSecond, int timeoutSeconds, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 128 ||
            commandId.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':' || c == '.')) ||
            !double.IsFinite(propulsion) || Math.Abs(propulsion) > LocalVehicleTestControlPolicy.MaximumPropulsionOverride ||
            !double.IsFinite(steering) || Math.Abs(steering) > LocalVehicleTestControlPolicy.MaximumSteeringOverride ||
            !double.IsFinite(maximumSpeedMetersPerSecond) || maximumSpeedMetersPerSecond <= 0 ||
            maximumSpeedMetersPerSecond > LocalVehicleTestControlPolicy.MaximumSpeedMetersPerSecond ||
            timeoutSeconds is < 1 || timeoutSeconds > LocalVehicleTestControlPolicy.MaximumCommandLifetime.TotalSeconds)
            throw new ArgumentException("Combined local drive values exceed the local lane's short command bounds.");
        var command = new LocalVehicleTestCommand(commandId, authorization.Instance, authorization.RunId,
            authorization.FixtureId, authorization.GridId, authorization.ControllerId,
            LocalVehicleTestCommandKind.Drive, propulsion, maximumSpeedMetersPerSecond,
            nowUtc, nowUtc.AddSeconds(timeoutSeconds), steering);
        WriteAtomic(CommandFileName, LocalVehicleTestWire.FormatCommand(command));
    }

    public void CancelGoal(string commandId)
    {
        WriteSafetyCommand("cancel-" + Guid.NewGuid().ToString("N"), LocalVehicleTestCommandKind.Brake);
    }

    public VehicleStopOutcome Stop()
    {
        var commandId = "stop-" + Guid.NewGuid().ToString("N");
        WriteSafetyCommand(commandId, LocalVehicleTestCommandKind.Brake);
        try
        {
            if (LocalVehicleTestWire.TryParseReceipt(File.ReadAllText(PathFor(ReceiptFileName)), out var receipt) &&
                receipt.CommandId == commandId)
                return new VehicleStopOutcome(receipt.ActuatorsNeutralized, receipt.BrakesRequested,
                    receipt.LinearSpeedMetersPerSecond, receipt.AngularSpeedRadiansPerSecond,
                    receipt.ConsecutiveStationarySamples, receipt.PhysicalStopObserved);
        }
        catch { }
        return new VehicleStopOutcome(false, false, null, null, 0, false);
    }

    public void Release()
    {
        // The plugin treats a missing grant as revocation and continues the local stop path.
        try { File.Delete(PathFor(AuthorizationFileName)); }
        catch { }
    }

    private void WriteSafetyCommand(string commandId, LocalVehicleTestCommandKind kind)
    {
        var now = DateTime.UtcNow;
        var command = new LocalVehicleTestCommand(commandId, authorization.Instance, authorization.RunId,
            authorization.FixtureId, authorization.GridId, authorization.ControllerId, kind,
            kind == LocalVehicleTestCommandKind.Brake ? 1 : 0, 0, now, now.AddSeconds(2));
        WriteAtomic(CommandFileName, LocalVehicleTestWire.FormatCommand(command));
    }

    private string PathFor(string fileName) => Path.Combine(mailboxDirectory, fileName);

    private void WriteAtomic(string fileName, string text)
    {
        var target = PathFor(fileName);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, new System.Text.UTF8Encoding(false));
            File.Move(temporary, target, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }
}

/// <summary>Local run grant provider; its subject is a run authorization ID, never a player or MCP identity.</summary>
public sealed class LocalVehicleTestPrincipalProvider(LocalVehicleTestAuthorization authorization)
    : IVerifiedVehiclePrincipalProvider
{
    public VerifiedVehiclePrincipal? Resolve()
    {
        if (authorization == null || authorization.ExpiresUtc <= DateTime.UtcNow) return null;
        return new VerifiedVehiclePrincipal("local-test-run:" + authorization.AuthorizationId,
            new HashSet<string>(["enroll", "observe", "control", "stop", "release"], StringComparer.Ordinal),
            authorization.ExpiresUtc);
    }
}
