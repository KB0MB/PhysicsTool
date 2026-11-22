using System.Numerics;

namespace HKCLTool;

internal static class OotClothWind
{
    // hclSimpleWindAction::apply, 0x7101dbbda0. Lift is not read by this action.
    internal static Vector3 Acceleration(OotAerodynamicsData data, int particle, Vector3 airVelocity,
        Vector3 direction, Vector3 velocity, Vector3? normal, float mass, float inverseMass,
        float totalMass, float actionDrag = 1, float actionScale = 1)
    {
        if (totalMass <= 0 || mass <= 0 || inverseMass <= 0) return Vector3.Zero;
        float denominator = data.UseSurfaceDensity ? data.SurfaceDensity : totalMass;
        float exposure = normal is { } n ? MathF.Abs(Vector3.Dot(n, direction)) : .7f;
        float rate = data.DragCoefficient / denominator * actionDrag * actionScale * mass * data.DragFactor(particle) * exposure * inverseMass;
        return (airVelocity - velocity) * rate;
    }
}
