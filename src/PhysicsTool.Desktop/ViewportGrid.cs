using System.Numerics;

namespace PhysicsTool.Desktop;

public readonly record struct GridLine(Vector3 Start, Vector3 End, Vector3 Color);

public sealed partial class PhysicsViewport
{
    public IReadOnlyList<GridLine> GridLines
    {
        get
        {
            var radius = Math.Max(1, Math.Max(Bounds.Width, Bounds.Height) / PixelsPerUnit * 1.5);
            var desired = 48 / PixelsPerUnit;
            var power = Math.Pow(10, Math.Floor(Math.Log10(desired)));
            var ratio = desired / power;
            var step = (float)(power * (ratio <= 1 ? 1 : ratio <= 2 ? 2 : ratio <= 5 ? 5 : 10));
            var count = (int)Math.Ceiling(radius / step);
            var x = MathF.Floor(_center.X / step) * step;
            var z = MathF.Floor(_center.Z / step) * step;
            var span = count * step;
            var result = new List<GridLine>();
            for (var i = -count; i <= count; i++)
            {
                var px = x + i * step; var pz = z + i * step;
                // Grid coordinates are world-space multiples, never camera-relative heights.
                result.Add(new(new(px, 0, z - span), new(px, 0, z + span),
                    MathF.Abs(px) < step * .01f ? new(.3f, .42f, .65f) : new(.23f, .255f, .26f)));
                result.Add(new(new(x - span, 0, pz), new(x + span, 0, pz),
                    MathF.Abs(pz) < step * .01f ? new(.65f, .3f, .3f) : new(.23f, .255f, .26f)));
            }
            return result;
        }
    }
}
