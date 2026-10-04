using System;

namespace Voidwright.Core
{
    public struct GridPoint : IEquatable<GridPoint>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public GridPoint(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(GridPoint other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is GridPoint && Equals((GridPoint)obj);
        public override int GetHashCode()
        {
            unchecked { return ((X * 397) ^ Y) * 397 ^ Z; }
        }
        public static bool operator ==(GridPoint left, GridPoint right) => left.Equals(right);
        public static bool operator !=(GridPoint left, GridPoint right) => !left.Equals(right);
        public override string ToString() => X + "," + Y + "," + Z;
    }
}
