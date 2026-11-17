using System;
using System.Globalization;

namespace UltraPuck.Core
{
    /// <summary>
    /// A fixed-point number: 48 bits whole, 16 bits fraction, stored in a long. Every PC gets exactly the same result
    /// from the same maths (floats can differ between CPUs and compilers), which online rollback depends on.
    /// Precision is 1/65536; values up to about +-8 million are safe to multiply.
    /// </summary>
    [Serializable]
    public struct Fix : IEquatable<Fix>, IComparable<Fix>
    {
        public const int Shift = 16;
        public const long OneRaw = 1L << Shift;

        public long Raw;

        private Fix(long raw) { Raw = raw; }

        public static Fix FromRaw(long raw) { return new Fix(raw); }
        public static Fix FromInt(int v) { return new Fix((long)v << Shift); }
        /// <summary>a / b as an exact fraction (for constants: Ratio(3, 2) = 1.5).</summary>
        public static Fix Ratio(long a, long b) { return new Fix((a << Shift) / b); }
        public static Fix FromDecimal(decimal d) { return new Fix((long)Math.Round(d * OneRaw, MidpointRounding.AwayFromZero)); }

        /// <summary>Parses "12", "-3.75", "1e2" style numbers without going through floats.</summary>
        public static Fix Parse(string s)
        {
            return FromDecimal(decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        public static readonly Fix Zero = new Fix(0);
        public static readonly Fix One = new Fix(OneRaw);
        public static readonly Fix Half = new Fix(OneRaw / 2);
        public static readonly Fix Two = new Fix(OneRaw * 2);
        public static readonly Fix Epsilon = new Fix(1);

        /// <summary>For drawing only, never for game rules.</summary>
        public float ToFloat() { return Raw / (float)OneRaw; }
        public double ToDouble() { return Raw / (double)OneRaw; }
        public int FloorToInt() { return (int)(Raw >> Shift); }

        public static Fix operator +(Fix a, Fix b) { return new Fix(a.Raw + b.Raw); }
        public static Fix operator -(Fix a, Fix b) { return new Fix(a.Raw - b.Raw); }
        public static Fix operator -(Fix a) { return new Fix(-a.Raw); }
        /// <summary>
        /// Rounds to the nearest, halves away from zero, so -a * b is exactly -(a * b). (A plain shift rounds towards
        /// minus infinity, which made everything moving left lose a hair more speed to drag than things moving right:
        /// enough to tilt a mirrored rink towards one side.) Division already truncates towards zero, which is mirrored too.
        /// </summary>
        public static Fix operator *(Fix a, Fix b)
        {
            var p = a.Raw * b.Raw;
            const long half = OneRaw / 2;
            return new Fix(p >= 0 ? (p + half) >> Shift : -((-p + half) >> Shift));
        }
        public static Fix operator *(Fix a, int b) { return new Fix(a.Raw * b); }
        public static Fix operator /(Fix a, Fix b) { return new Fix((a.Raw << Shift) / b.Raw); }
        public static Fix operator /(Fix a, int b) { return new Fix(a.Raw / b); }

        public static bool operator <(Fix a, Fix b) { return a.Raw < b.Raw; }
        public static bool operator >(Fix a, Fix b) { return a.Raw > b.Raw; }
        public static bool operator <=(Fix a, Fix b) { return a.Raw <= b.Raw; }
        public static bool operator >=(Fix a, Fix b) { return a.Raw >= b.Raw; }
        public static bool operator ==(Fix a, Fix b) { return a.Raw == b.Raw; }
        public static bool operator !=(Fix a, Fix b) { return a.Raw != b.Raw; }

        public static Fix Abs(Fix a) { return a.Raw < 0 ? new Fix(-a.Raw) : a; }
        public static Fix Min(Fix a, Fix b) { return a.Raw < b.Raw ? a : b; }
        public static Fix Max(Fix a, Fix b) { return a.Raw > b.Raw ? a : b; }
        public static Fix Clamp(Fix v, Fix lo, Fix hi) { return v.Raw < lo.Raw ? lo : v.Raw > hi.Raw ? hi : v; }
        public static Fix Clamp01(Fix v) { return Clamp(v, Zero, One); }

        /// <summary>Square root (0 for negative numbers), exact to the last bit, integer-only.</summary>
        public static Fix Sqrt(Fix a)
        {
            if (a.Raw <= 0) return Zero;
            // sqrt(raw / 2^16) * 2^16 = sqrt(raw * 2^16)
            return new Fix((long)ISqrt((ulong)a.Raw << Shift));
        }

        private static ulong ISqrt(ulong n)
        {
            ulong res = 0, bit = 1UL << 62;
            while (bit > n) bit >>= 2;
            while (bit != 0)
            {
                if (n >= res + bit) { n -= res + bit; res = (res >> 1) + bit; }
                else res >>= 1;
                bit >>= 2;
            }
            return res;
        }

        public bool Equals(Fix other) { return Raw == other.Raw; }
        public override bool Equals(object obj) { return obj is Fix && ((Fix)obj).Raw == Raw; }
        public override int GetHashCode() { return Raw.GetHashCode(); }
        public int CompareTo(Fix other) { return Raw.CompareTo(other.Raw); }
        public override string ToString() { return ToDouble().ToString("0.####", CultureInfo.InvariantCulture); }
    }

    /// <summary>A 2D vector of fixed-point numbers.</summary>
    [Serializable]
    public struct FixVec2 : IEquatable<FixVec2>
    {
        public Fix X, Y;

        public FixVec2(Fix x, Fix y) { X = x; Y = y; }

        public static readonly FixVec2 Zero = new FixVec2(Fix.Zero, Fix.Zero);
        public static readonly FixVec2 Right = new FixVec2(Fix.One, Fix.Zero);

        public static FixVec2 operator +(FixVec2 a, FixVec2 b) { return new FixVec2(a.X + b.X, a.Y + b.Y); }
        public static FixVec2 operator -(FixVec2 a, FixVec2 b) { return new FixVec2(a.X - b.X, a.Y - b.Y); }
        public static FixVec2 operator -(FixVec2 a) { return new FixVec2(-a.X, -a.Y); }
        public static FixVec2 operator *(FixVec2 a, Fix s) { return new FixVec2(a.X * s, a.Y * s); }
        public static FixVec2 operator *(Fix s, FixVec2 a) { return new FixVec2(a.X * s, a.Y * s); }
        public static FixVec2 operator /(FixVec2 a, Fix s) { return new FixVec2(a.X / s, a.Y / s); }
        public static bool operator ==(FixVec2 a, FixVec2 b) { return a.X == b.X && a.Y == b.Y; }
        public static bool operator !=(FixVec2 a, FixVec2 b) { return !(a == b); }

        public static Fix Dot(FixVec2 a, FixVec2 b) { return a.X * b.X + a.Y * b.Y; }
        public static Fix Cross(FixVec2 a, FixVec2 b) { return a.X * b.Y - a.Y * b.X; }

        public Fix SqrLength { get { return X * X + Y * Y; } }
        public Fix Length { get { return Fix.Sqrt(SqrLength); } }

        /// <summary>Unit vector in the same direction (zero stays zero).</summary>
        public FixVec2 Normalized
        {
            get
            {
                var len = Length;
                return len.Raw <= 0 ? Zero : new FixVec2(X / len, Y / len);
            }
        }

        /// <summary>This vector shortened to at most <paramref name="max"/> long.</summary>
        public FixVec2 ClampLength(Fix max)
        {
            var len = Length;
            return len > max && len.Raw > 0 ? this * (max / len) : this;
        }

        public static Fix Distance(FixVec2 a, FixVec2 b) { return (a - b).Length; }

        public bool Equals(FixVec2 other) { return this == other; }
        public override bool Equals(object obj) { return obj is FixVec2 && (FixVec2)obj == this; }
        public override int GetHashCode() { return X.GetHashCode() * 31 + Y.GetHashCode(); }
        public override string ToString() { return "(" + X + ", " + Y + ")"; }
    }
}
