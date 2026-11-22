using System.Numerics;

namespace HKCLTool;

// OoT 0x7101c6b2b0: regularized polar frame, with a warm-started symmetric
// eigensystem. Matrix rows here correspond to native column vectors.
internal sealed class OotVolumeFrame
{
    internal const float Epsilon = 1.1920928955078125e-7f;
    private Matrix4x4 _basis = Matrix4x4.Identity;
    private Matrix4x4 _rotation = Matrix4x4.Identity;

    internal void Reset() { _basis = _rotation = Matrix4x4.Identity; }

    internal Matrix4x4 Fit(Vector3 x, Vector3 y, Vector3 z, Vector3 center)
    {
        var covariance = Matrix(x, y, z);
        var gram = covariance * Matrix4x4.Transpose(covariance);
        var diagonal = _basis * gram * Matrix4x4.Transpose(_basis);
        float threshold = Epsilon * Epsilon * (gram.M11 * gram.M11 + gram.M22 * gram.M22 + gram.M33 * gram.M33 +
            2 * (gram.M12 * gram.M12 + gram.M13 * gram.M13 + gram.M23 * gram.M23));
        Span<float> a = stackalloc float[9] { diagonal.M11, diagonal.M12, diagonal.M13,
            diagonal.M21, diagonal.M22, diagonal.M23, diagonal.M31, diagonal.M32, diagonal.M33 };
        Span<Vector3> basis = stackalloc Vector3[3] { Row(_basis, 0), Row(_basis, 1), Row(_basis, 2) };
        for (int iteration = 0; iteration < 20; iteration++)
        {
            if (2 * (a[1] * a[1] + a[2] * a[2] + a[5] * a[5]) < threshold ||
                a[1] == 0 && a[2] == 0 && a[5] == 0) break;
            int p = 0, q = MathF.Abs(a[2]) > MathF.Abs(a[1]) ? 2 : 1;
            if (MathF.Abs(a[5]) > MathF.Abs(a[q])) { p = 1; q = 2; }
            float tau = (a[q * 3 + q] - a[p * 3 + p]) / (2 * a[p * 3 + q]);
            float t = 1 / (tau + MathF.CopySign(MathF.Sqrt(tau * tau + 1), tau));
            float c = 1 / MathF.Sqrt(t * t + 1), s = t * c;
            float pp = a[p * 3 + p], qq = a[q * 3 + q], pq = a[p * 3 + q];
            a[p * 3 + p] = c * c * pp - 2 * s * c * pq + s * s * qq;
            a[q * 3 + q] = s * s * pp + 2 * s * c * pq + c * c * qq;
            a[p * 3 + q] = a[q * 3 + p] = 0;
            int k = 3 - p - q;
            float kp = a[k * 3 + p], kq = a[k * 3 + q];
            a[k * 3 + p] = a[p * 3 + k] = c * kp - s * kq;
            a[k * 3 + q] = a[q * 3 + k] = s * kp + c * kq;
            var bp = basis[p]; var bq = basis[q];
            basis[p] = c * bp - s * bq;
            basis[q] = s * bp + c * bq;
        }

        if (float.IsFinite(a[0]) && float.IsFinite(a[4]) && float.IsFinite(a[8]))
        {
            _basis = Matrix(Vector3.Normalize(basis[0]), Vector3.Normalize(basis[1]), Vector3.Normalize(basis[2]));
            var roots = Matrix4x4.CreateScale(MathF.Sqrt(MathF.Abs(a[0])) + Epsilon,
                MathF.Sqrt(MathF.Abs(a[4])) + Epsilon, MathF.Sqrt(MathF.Abs(a[8])) + Epsilon);
            var stretch = Matrix4x4.Transpose(_basis) * roots * _basis;
            float bound = Epsilon * L1(Row(stretch, 0)) * L1(Row(stretch, 1)) * L1(Row(stretch, 2));
            _rotation = MathF.Abs(stretch.GetDeterminant()) > bound && Matrix4x4.Invert(stretch, out var inverse)
                ? inverse * covariance : Matrix(Vector3.Zero, Vector3.Zero, Vector3.Zero);
        }
        else _basis = Matrix4x4.Identity;
        var result = _rotation;
        result.Translation = center;
        return result;
    }

    private static float L1(Vector3 v) => MathF.Abs(v.X) + MathF.Abs(v.Y) + MathF.Abs(v.Z);
    private static Vector3 Row(Matrix4x4 m, int index) => index switch
    { 0 => new(m.M11, m.M12, m.M13), 1 => new(m.M21, m.M22, m.M23), _ => new(m.M31, m.M32, m.M33) };
    private static Matrix4x4 Matrix(Vector3 x, Vector3 y, Vector3 z) => new(
        x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
}
