using System.Numerics;
using PhysicsTool.Desktop.Rendering;
using Silk.NET.OpenGL;

internal static class CaptureEdgeChecks
{
    internal static unsafe void Run(GL gl, bool es)
    {
        using var source = new SceneRenderTarget(gl);
        using var effect = new CaptureEdgeRenderer(gl, es);
        byte[] Read()
        {
            var pixels = new byte[256 * 256 * 4];
            fixed (byte* p = pixels) gl.ReadPixels(0, 0, 256, 256, PixelFormat.Rgba, PixelType.UnsignedByte, p);
            return pixels;
        }
        void Clear(float depth, float r, float g, float b, float a)
        {
            if (es) gl.ClearDepth(depth); else gl.ClearDepth((double)depth);
            gl.ClearColor(r, g, b, a); gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }
        byte[] Scene(int scale, bool overlap = false)
        {
            source.Bind(256, 256, true); gl.DepthMask(true); gl.ColorMask(true, true, true, true);
            gl.Disable(EnableCap.ScissorTest); Clear(1, 0, 0, 0, 0);
            gl.Enable(EnableCap.ScissorTest);
            int half = 48 * scale;
            gl.Scissor(128 - half, 128 - half, (uint)half, (uint)(half * 2)); Clear(.5f, .2f, .3f, .4f, 1);
            gl.Scissor(128, 128 - half, (uint)half, (uint)(half * 2)); Clear(overlap ? .7f : .5f, .5f, .2f, .1f, 1);
            gl.Disable(EnableCap.ScissorTest);
            return Read();
        }
        byte[] Highlight(int scale, Vector3 light, float strength = 1)
        {
            effect.Apply(source, 256, 256, Matrix4x4.CreateScale(scale, scale, 1), 8, light, strength);
            return Read();
        }
        static int At(int x, int y) => (y * 256 + x) * 4;
        var basis = Scene(1); var right = Highlight(1, Vector3.UnitX);
        var left = Highlight(1, -Vector3.UnitX);
        for (int i = 3; i < basis.Length; i += 4)
            if (basis[i] != right[i] || basis[i] != left[i]) throw new Exception("Highlight changed pixel coverage");
        if (!basis.AsSpan(At(124, 128), 8 * 4).SequenceEqual(right.AsSpan(At(124, 128), 8 * 4)))
            throw new Exception("Highlight crossed a coplanar color/material join");
        if (right[At(175, 128)] <= basis[At(175, 128)] || right[At(80, 128)] != basis[At(80, 128)] ||
            left[At(80, 128)] <= basis[At(80, 128)] || left[At(175, 128)] != basis[At(175, 128)])
            throw new Exception("Highlight did not follow light direction");
        var fade = Enumerable.Range(169, 7).Select(x => right[At(x, 128)] - basis[At(x, 128)]).ToArray();
        if (fade.Distinct().Count() < 3 || fade.Zip(fade.Skip(1)).Any(p => p.First > p.Second))
            throw new Exception("Highlight band has no inward fade");
        if (!Highlight(1, Vector3.UnitX, 0).SequenceEqual(basis)) throw new Exception("Unlit highlight changed image");
        double width1 = Enumerable.Range(128, 48).Sum(x => (double)right[At(x, 128)] - basis[At(x, 128)]);
        var bigger = Scene(2); var zoomed = Highlight(2, Vector3.UnitX);
        double width2 = Enumerable.Range(128, 96).Sum(x => (double)zoomed[At(x, 128)] - bigger[At(x, 128)]);
        if (width2 / width1 < 1.6 || width2 / width1 > 2.4) throw new Exception("Highlight width did not scale with zoom: " + width2 / width1);
        var overlap = Scene(1, true); var front = Highlight(1, Vector3.UnitX);
        if (!front.AsSpan(At(124, 128), 8 * 4).SequenceEqual(overlap.AsSpan(At(124, 128), 8 * 4)))
            throw new Exception("Highlight traced an internal depth boundary");
        Console.WriteLine($"PASS edge pixels: material/depth joins untouched, silhouette only, light direction, soft fade, alpha, zero light, zoom width ratio {width2 / width1:F2}.");
    }
}
