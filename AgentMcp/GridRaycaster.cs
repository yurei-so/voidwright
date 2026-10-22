namespace Voidwright.AgentMcp;

internal sealed record GridRayHit(
    long GridId, string? GridName, BlockSnapshot Block, int[] Cell, string LocalFace,
    double[] WorldPosition, double[] WorldNormal, double DistanceMeters);

/// <summary>Ray tests against the oriented cell envelopes in one exported agent snapshot.</summary>
internal static class GridRaycaster
{
    private const double Epsilon = 1e-8;

    public static GridRayHit? Cast(IReadOnlyList<GridSnapshot> grids, double[] origin,
        double[] direction, double maxDistanceMeters)
    {
        GridRayHit? nearest = null;
        foreach (var grid in grids)
        {
            var relative = Subtract(origin, grid.Position);
            double[] localOrigin = [Dot(relative, grid.Right), Dot(relative, grid.Up), Dot(relative, grid.Forward)];
            double[] localDirection = [Dot(direction, grid.Right), Dot(direction, grid.Up), Dot(direction, grid.Forward)];
            foreach (var block in grid.Blocks)
            {
                if (block.Min.Length != 3 || block.Max.Length != 3) continue;
                var minimum = new double[3];
                var maximum = new double[3];
                for (var axis = 0; axis < 3; axis++)
                {
                    if (block.Min[axis] > block.Max[axis])
                        throw new InvalidDataException("Snapshot block cell bounds are reversed; refusing an ambiguous ray query.");
                    minimum[axis] = (block.Min[axis] - 0.5) * grid.GridSizeMeters;
                    maximum[axis] = (block.Max[axis] + 0.5) * grid.GridSizeMeters;
                }
                if (!Intersect(localOrigin, localDirection, minimum, maximum,
                        out var distance, out var faceAxis, out var faceSign) ||
                    distance < -Epsilon || distance > maxDistanceMeters + Epsilon)
                    continue;

                var localHit = Add(localOrigin, Scale(localDirection, distance));
                var cell = new int[3];
                for (var axis = 0; axis < 3; axis++)
                {
                    var coordinate = (int)Math.Floor(localHit[axis] / grid.GridSizeMeters + 0.5 + Epsilon);
                    cell[axis] = Math.Clamp(coordinate, block.Min[axis], block.Max[axis]);
                }
                var localNormal = new double[3];
                localNormal[faceAxis] = faceSign;
                var worldNormal = Normalize(Add(Add(Scale(grid.Right, localNormal[0]),
                    Scale(grid.Up, localNormal[1])), Scale(grid.Forward, localNormal[2])));
                var worldPosition = Add(origin, Scale(direction, distance));
                var candidate = new GridRayHit(grid.EntityId, grid.Name, block, cell,
                    FaceName(faceAxis, faceSign), worldPosition, worldNormal, distance);
                if (nearest is null || candidate.DistanceMeters < nearest.DistanceMeters - Epsilon ||
                    (Math.Abs(candidate.DistanceMeters - nearest.DistanceMeters) <= Epsilon &&
                     (candidate.GridId < nearest.GridId ||
                      (candidate.GridId == nearest.GridId && candidate.Block.EntityId < nearest.Block.EntityId))))
                    nearest = candidate;
            }
        }
        return nearest;
    }

    private static bool Intersect(double[] origin, double[] direction, double[] minimum, double[] maximum,
        out double distance, out int faceAxis, out int faceSign)
    {
        var entry = double.NegativeInfinity;
        var exit = double.PositiveInfinity;
        var entryAxis = -1;
        var entrySign = 0;
        var exitAxis = -1;
        var exitSign = 0;
        for (var axis = 0; axis < 3; axis++)
        {
            if (Math.Abs(direction[axis]) < Epsilon)
            {
                if (origin[axis] < minimum[axis] - Epsilon || origin[axis] > maximum[axis] + Epsilon)
                { distance = 0; faceAxis = -1; faceSign = 0; return false; }
                continue;
            }
            var near = (minimum[axis] - origin[axis]) / direction[axis];
            var far = (maximum[axis] - origin[axis]) / direction[axis];
            var nearSign = -1;
            var farSign = 1;
            if (near > far) { (near, far) = (far, near); (nearSign, farSign) = (farSign, nearSign); }
            if (near > entry) { entry = near; entryAxis = axis; entrySign = nearSign; }
            if (far < exit) { exit = far; exitAxis = axis; exitSign = farSign; }
            if (entry > exit + Epsilon) { distance = 0; faceAxis = -1; faceSign = 0; return false; }
        }
        if (exit < -Epsilon) { distance = 0; faceAxis = -1; faceSign = 0; return false; }
        if (entry >= -Epsilon)
        { distance = Math.Max(0, entry); faceAxis = entryAxis; faceSign = entrySign; }
        else
        { distance = Math.Max(0, exit); faceAxis = exitAxis; faceSign = exitSign; }
        return faceAxis >= 0 && double.IsFinite(distance);
    }

    private static string FaceName(int axis, int sign) => (axis, sign) switch
    {
        (0, 1) => "+X", (0, -1) => "-X", (1, 1) => "+Y",
        (1, -1) => "-Y", (2, 1) => "+Z", _ => "-Z"
    };
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Subtract(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    private static double[] Add(double[] a, double[] b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
    private static double[] Scale(double[] value, double scale) => [value[0] * scale, value[1] * scale, value[2] * scale];
    private static double[] Normalize(double[] value)
    {
        var length = Math.Sqrt(Dot(value, value));
        if (!double.IsFinite(length) || length < Epsilon) throw new InvalidDataException("Grid face normal is invalid.");
        return Scale(value, 1 / length);
    }
}
