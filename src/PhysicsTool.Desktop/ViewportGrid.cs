using System.Numerics;

namespace PhysicsTool.Desktop;

public readonly record struct GridLine(Vector3 Start, Vector3 End, Vector3 Color);

public sealed partial class PhysicsViewport
{
    public IReadOnlyList<GridLine> GridLines => IsLightViewport ? LightWorldGrid : WorldGrid;
    private static readonly IReadOnlyList<GridLine> WorldGrid = CreateWorldGrid(false);
    private static readonly IReadOnlyList<GridLine> LightWorldGrid = CreateWorldGrid(true);

    private static IReadOnlyList<GridLine> CreateWorldGrid(bool light)
    {
        // Neither camera navigation nor framing may change the grid's units or bounds.
        const float step = .25f;
        const int count = 40;
        const float span = count * step;
        var result = new List<GridLine>();
        var minor = light ? new Vector3(.72f, .76f, .78f) : new Vector3(.23f, .255f, .26f);
        for (var i = -count; i <= count; i++)
        {
            var offset = i * step;
            result.Add(new(new(offset, 0, -span), new(offset, 0, span),
                i == 0 ? new(.3f, .42f, .65f) : minor));
            result.Add(new(new(-span, 0, offset), new(span, 0, offset),
                i == 0 ? new(.65f, .3f, .3f) : minor));
        }
        return result.AsReadOnly();
    }
}
