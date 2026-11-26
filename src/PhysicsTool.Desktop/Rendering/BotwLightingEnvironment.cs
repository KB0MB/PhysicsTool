using System.Numerics;

namespace PhysicsTool.Desktop.Rendering;

internal sealed record BotwLightingEnvironment(string Name, Vector3 Direct, Vector3 Specular, Vector3 Sky, Vector3 Ground)
{
    internal static readonly BotwLightingEnvironment Preview = new("Preview", new(1.1f, 1.05f, 1), Vector3.One, new(.3f,.32f,.36f), new(.18f));
    // Comparison setups, not extracted game time-of-day environments.
    internal static readonly BotwLightingEnvironment Golden = new("Warm studio", new(1.12f,1.08f,.85f), new(1,.97f,.83f), new(.34f,.33f,.25f), new(.21f,.20f,.15f));
    internal static readonly BotwLightingEnvironment Sunset = new("Sunset test", new(1.25f,.88f,.59f), new(1,.8f,.56f), new(.32f,.29f,.28f), new(.24f,.17f,.13f));
    // IconCapture.baglenv colors. Keep editor exposure independent of the game's intensity-6 HDR pipeline.
    internal static readonly BotwLightingEnvironment IconCapture = new("Icon capture palette", new(1,.921569f,.74902f), Vector3.One, new(.131f,.304f,1), new(1,.96f,.88f));
    internal static IReadOnlyList<BotwLightingEnvironment> All { get; } = [Preview, Golden, Sunset, IconCapture];
    public override string ToString() => Name;
}
