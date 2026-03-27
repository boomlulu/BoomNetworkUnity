// BoomNetwork VampireSurvivors Demo — Deterministic Fixed-Point Number
//
// 22.10 fixed-point: 22-bit integer + 10-bit fraction.
// All arithmetic is integer-only — bit-level identical across all platforms.
// Sin/Cos/Sqrt use precomputed lookup tables, no floating-point at runtime.
//
// Range: ±2,097,151.999  Precision: ~0.001 (1/1024)

using System;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public struct FInt : IEquatable<FInt>, IComparable<FInt>
    {
        public const int SHIFT = 10;
        public const int SCALE = 1 << SHIFT; // 1024

        public int Raw;

        // ==================== Construction ====================

        public FInt(int raw) { Raw = raw; }

        public static FInt FromInt(int v) => new FInt(v << SHIFT);
        public static FInt FromFloat(float v) => new FInt((int)(v * SCALE));

        // ==================== Conversion (Rendering only!) ====================

        public float ToFloat() => Raw * (1f / SCALE);
        public int ToInt() => Raw >> SHIFT;

        // ==================== Constants ====================

        public static readonly FInt Zero = new FInt(0);
        public static readonly FInt One = new FInt(SCALE);
        public static readonly FInt Half = new FInt(SCALE / 2);
        public static readonly FInt MinValue = new FInt(int.MinValue);
        public static readonly FInt MaxValue = new FInt(int.MaxValue);
        public static readonly FInt Epsilon = new FInt(1);
        public static readonly FInt Pi = new FInt(3217);       // π × 1024 ≈ 3216.99
        public static readonly FInt TwoPi = new FInt(6434);
        public static readonly FInt Deg2Rad = new FInt(18);    // (π/180) × 1024 ≈ 17.87

        // ==================== Operators ====================

        public static FInt operator +(FInt a, FInt b) => new FInt(a.Raw + b.Raw);
        public static FInt operator -(FInt a, FInt b) => new FInt(a.Raw - b.Raw);
        public static FInt operator -(FInt a) => new FInt(-a.Raw);
        public static FInt operator *(FInt a, FInt b) => new FInt((int)((long)a.Raw * b.Raw >> SHIFT));
        public static FInt operator /(FInt a, FInt b) => new FInt((int)(((long)a.Raw << SHIFT) / b.Raw));

        // FInt × int (no shift needed for the int side)
        public static FInt operator *(FInt a, int b) => new FInt(a.Raw * b);
        public static FInt operator *(int a, FInt b) => new FInt(a * b.Raw);
        public static FInt operator /(FInt a, int b) => new FInt(a.Raw / b);

        // ==================== Comparison ====================

        public static bool operator >(FInt a, FInt b) => a.Raw > b.Raw;
        public static bool operator <(FInt a, FInt b) => a.Raw < b.Raw;
        public static bool operator >=(FInt a, FInt b) => a.Raw >= b.Raw;
        public static bool operator <=(FInt a, FInt b) => a.Raw <= b.Raw;
        public static bool operator ==(FInt a, FInt b) => a.Raw == b.Raw;
        public static bool operator !=(FInt a, FInt b) => a.Raw != b.Raw;

        public bool Equals(FInt other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is FInt f && Raw == f.Raw;
        public override int GetHashCode() => Raw;
        public int CompareTo(FInt other) => Raw.CompareTo(other.Raw);

        // ==================== Math ====================

        public static FInt Abs(FInt v) => new FInt(v.Raw < 0 ? -v.Raw : v.Raw);
        public static FInt Min(FInt a, FInt b) => a.Raw < b.Raw ? a : b;
        public static FInt Max(FInt a, FInt b) => a.Raw > b.Raw ? a : b;
        public static FInt Clamp(FInt v, FInt min, FInt max)
        {
            if (v.Raw < min.Raw) return min;
            if (v.Raw > max.Raw) return max;
            return v;
        }

        /// <summary>Integer square root in fixed-point. Fully deterministic.</summary>
        public static FInt Sqrt(FInt v)
        {
            if (v.Raw <= 0) return Zero;

            // We need: result = sqrt(v) in fixed-point
            // result.Raw = sqrt(v.Raw * SCALE)
            long val = (long)v.Raw << SHIFT;
            long x = v.Raw;
            if (x <= 0) x = 1;

            // Newton-Raphson (converges in ~15 iterations for 32-bit)
            for (int i = 0; i < 16; i++)
            {
                long nx = (x + val / x) >> 1;
                if (nx >= x) break;
                x = nx;
            }
            return new FInt((int)x);
        }

        // ==================== Trigonometry (Lookup Table) ====================

        // 1024 entries covering [0°, 360°), precomputed at class init.
        // Resolution: 360/1024 ≈ 0.35° per step.
        const int TABLE_SIZE = 1024;
        const int TABLE_MASK = TABLE_SIZE - 1;
        static readonly int[] SinTable;

        static FInt()
        {
            // Computed once at startup using double-precision Math.
            // The resulting integer table is identical on all platforms.
            SinTable = new int[TABLE_SIZE];
            for (int i = 0; i < TABLE_SIZE; i++)
            {
                double angle = i * 2.0 * 3.14159265358979323846 / TABLE_SIZE;
                SinTable[i] = (int)Math.Round(Math.Sin(angle) * SCALE);
            }
        }

        /// <summary>Sin of angle in degrees. Deterministic lookup.</summary>
        public static FInt SinDeg(FInt degrees)
        {
            // Normalize to [0, 360) then map to table index
            int deg = degrees.Raw; // fixed-point degrees
            // Convert to table index: idx = deg * TABLE_SIZE / (360 * SCALE)
            // = deg * TABLE_SIZE / 368640
            // Simplify: deg * 1024 / 368640 = deg / 360
            long idx = ((long)deg * TABLE_SIZE) / (360 * SCALE);
            int i = (int)(idx % TABLE_SIZE);
            if (i < 0) i += TABLE_SIZE;
            return new FInt(SinTable[i]);
        }

        /// <summary>Cos of angle in degrees. Deterministic lookup.</summary>
        public static FInt CosDeg(FInt degrees)
        {
            // cos(x) = sin(x + 90)
            int deg = degrees.Raw + 90 * SCALE; // add 90 degrees
            long idx = ((long)deg * TABLE_SIZE) / (360 * SCALE);
            int i = (int)(idx % TABLE_SIZE);
            if (i < 0) i += TABLE_SIZE;
            return new FInt(SinTable[i]);
        }

        /// <summary>Sin of angle in radians (FInt). Deterministic lookup.</summary>
        public static FInt Sin(FInt radians)
        {
            // Convert radians to table index: idx = rad * TABLE_SIZE / (2π)
            // 2π in fixed-point = 6434
            long idx = ((long)radians.Raw * TABLE_SIZE) / 6434;
            int i = (int)(idx % TABLE_SIZE);
            if (i < 0) i += TABLE_SIZE;
            return new FInt(SinTable[i]);
        }

        /// <summary>Cos of angle in radians (FInt). Deterministic lookup.</summary>
        public static FInt Cos(FInt radians)
        {
            long idx = ((long)radians.Raw * TABLE_SIZE) / 6434 + TABLE_SIZE / 4; // +90°
            int i = (int)(idx % TABLE_SIZE);
            if (i < 0) i += TABLE_SIZE;
            return new FInt(SinTable[i]);
        }

        // ==================== Debug ====================

        public override string ToString() => ToFloat().ToString("F3");
    }
}
