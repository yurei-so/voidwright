using System.Security.Cryptography;

namespace Voidwright.AgentMcp;

public sealed record ViewPose(double[] Position, double[] Forward, double[] Up);

// A pure consistency contract for a future trusted virtual HoloView producer.
// It cannot prove that pixels came from the pose; producer provenance must be verified separately.
public sealed record ViewCaptureEvidence(
    string WorldScopeId, string RunId, long TargetGridId, long RenderTick,
    string SourceKind, ViewPose ActualPose, int Width, int Height, byte[] RgbaPixels);

public sealed record ViewCaptureReceipt(
    string WorldScopeId, string RunId, long TargetGridId, long RenderTick,
    long StructuralSnapshotTick, long TickDelta, bool Simultaneous,
    string SourceKind, ViewPose ActualPose, int Width, int Height, string RgbaSha256, int RgbaBytes);

public static class ViewCaptureReconciler
{
    private const double MaximumPositionErrorMeters = 0.001;
    private const double MinimumDirectionDot = 0.999999;
    private const int MaximumImageBytes = 8 * 1024 * 1024;

    public static ViewCaptureReceipt Reconcile(
        string worldScopeId, string runId, long targetGridId, long structuralSnapshotTick,
        ViewPose requestedPose, ViewCaptureEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(requestedPose);
        ArgumentNullException.ThrowIfNull(evidence);
        if (string.IsNullOrWhiteSpace(worldScopeId) || string.IsNullOrWhiteSpace(runId) ||
            evidence.WorldScopeId != worldScopeId || evidence.RunId != runId ||
            targetGridId <= 0 || evidence.TargetGridId != targetGridId)
            throw new InvalidDataException("Capture world, run, or target grid does not match the perspective.");
        if (evidence.RenderTick < 0 || structuralSnapshotTick < 0)
            throw new InvalidDataException("Capture and structural ticks must be nonnegative.");
        if (evidence.SourceKind != "holoview-software-render-virtual")
            throw new InvalidDataException("Capture source is not an attested virtual HoloView render.");
        ValidatePose(requestedPose);
        ValidatePose(evidence.ActualPose);
        if (Distance(requestedPose.Position, evidence.ActualPose.Position) > MaximumPositionErrorMeters ||
            Dot(requestedPose.Forward, evidence.ActualPose.Forward) < MinimumDirectionDot ||
            Dot(requestedPose.Up, evidence.ActualPose.Up) < MinimumDirectionDot)
            throw new InvalidDataException("Rendered camera pose differs from the requested perspective.");
        if (evidence.Width < 1 || evidence.Height < 1 || evidence.Width > 1024 || evidence.Height > 1024 ||
            evidence.RgbaPixels is null || evidence.RgbaPixels.Length != checked(evidence.Width * evidence.Height * 4) ||
            evidence.RgbaPixels.Length > MaximumImageBytes)
            throw new InvalidDataException("Capture RGBA dimensions or byte length are invalid.");

        var delta = checked(evidence.RenderTick - structuralSnapshotTick);
        return new ViewCaptureReceipt(worldScopeId, runId, targetGridId, evidence.RenderTick,
            structuralSnapshotTick, delta, delta == 0, evidence.SourceKind, evidence.ActualPose,
            evidence.Width, evidence.Height,
            Convert.ToHexString(SHA256.HashData(evidence.RgbaPixels)).ToLowerInvariant(), evidence.RgbaPixels.Length);
    }

    private static void ValidatePose(ViewPose pose)
    {
        if (!ValidVector(pose.Position) || !ValidVector(pose.Forward) || !ValidVector(pose.Up) ||
            Math.Abs(Length(pose.Forward) - 1) > 0.001 || Math.Abs(Length(pose.Up) - 1) > 0.001 ||
            Math.Abs(Dot(pose.Forward, pose.Up)) > 0.001)
            throw new InvalidDataException("Capture pose is not finite and orthonormal.");
    }

    private static bool ValidVector(double[]? value) => value is { Length: 3 } && value.All(double.IsFinite);
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double Length(double[] value) => Math.Sqrt(Dot(value, value));
    private static double Distance(double[] a, double[] b) =>
        Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2) + Math.Pow(a[2] - b[2], 2));
}
