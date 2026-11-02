using System.Numerics;

namespace HKCLTool;

// BotW main: 7101ca6440 updates air velocity/drag; 7101ca6760 applies forces.
// Weather, actor reaction multipliers and the game's shared RNG remain preview inputs.
public sealed record BotwClothWindProfile(
    BphysicsClothProfile Cloth, Vector3 SubWindDirection, float SubWindFrequency, float SubWindSpeed)
{
    public static BotwClothWindProfile? Find(BphysicsDocument? document, string clothName)
    {
        if (document == null) return null;
        static string Name(string value) => value.StartsWith("Link:", StringComparison.OrdinalIgnoreCase) ? value[5..] : value;
        var matches = document.Cloths.Where(c => Name(c.Name).Equals(Name(clothName), StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? new(matches[0], document.SubWindDirection, document.SubWindFrequency, document.SubWindSpeed) : null;
    }

    public void Validate()
    {
        var values = new[] { Cloth.WindFrequency, Cloth.WindDrag, Cloth.WindMinSpeed, Cloth.WindMaxSpeed,
            Cloth.SubWindFactorMain, Cloth.SubWindFactorAdd, SubWindFrequency, SubWindSpeed,
            SubWindDirection.X, SubWindDirection.Y, SubWindDirection.Z };
        if (values.Any(v => !float.IsFinite(v)) || Cloth.WindFrequency < 0 || Cloth.WindDrag < 0 || SubWindFrequency < 0 ||
            SubWindSpeed < 0 || Cloth.WindMinSpeed > Cloth.WindMaxSpeed)
            throw new InvalidDataException("The BPHYSICS wind profile contains invalid values.");
    }
}

public readonly record struct BotwClothWindSample(Vector3 AirVelocity, float Drag)
{
    public Vector3 Acceleration(Vector3 velocity, float mass, float inverseMass, float totalMass, float dt)
    {
        if (!float.IsFinite(totalMass) || totalMass <= 0 || mass <= 0 || inverseMass <= 0 || dt <= 0) return Vector3.Zero;
        var rate = Drag * mass * inverseMass / totalMass;
        // Keep a malformed/lightweight preview from reversing velocity in a single step.
        // The force equation below the cap is the native action, not a mass multiplier heuristic.
        rate = Math.Clamp(rate, 0, 1 / dt);
        return (AirVelocity - velocity) * rate;
    }
}

public sealed class BotwClothWind
{
    private uint _mainPhase, _subPhase;
    public void Reset() => _mainPhase = _subPhase = 0;

    public BotwClothWindSample Step(BotwClothWindProfile profile, Vector3 mainVelocity, float dt, float randomUnit)
    {
        var cloth = profile.Cloth;
        var speed = mainVelocity.Length();
        // The cloth-set caller (7101ca3f60) caps incoming world wind before evaluating each action.
        if (speed > 20) { mainVelocity *= 20 / speed; speed = 20; }
        if (!cloth.WindEnabled || (speed == 0 && cloth.SubWindFactorAdd == 0) || dt <= 0) return default;
        var amplitude = Math.Clamp(2 * speed / 30 + .1f, 0, 1);
        var jitter = Math.Clamp(randomUnit, 0, 1) * 2;
        _mainPhase = Advance(_mainPhase, cloth.WindFrequency * dt * amplitude * jitter);
        _subPhase = Advance(_subPhase, profile.SubWindFrequency * dt * jitter);
        var airSpeed = cloth.WindMinSpeed + (cloth.WindMaxSpeed - cloth.WindMinSpeed) * (1 + amplitude * Sine(_mainPhase)) * .5f;
        var air = speed > 0 ? mainVelocity / speed * airSpeed : Vector3.Zero;
        var subSpeed = profile.SubWindSpeed * (cloth.SubWindFactorAdd + amplitude * cloth.SubWindFactorMain);
        if (subSpeed != 0)
        {
            subSpeed *= MathF.Pow(Math.Max(0, Sine(_subPhase)), 8);
            var direction = profile.SubWindDirection;
            if (direction.LengthSquared() > 0) direction = Vector3.Normalize(direction);
            air += direction * subSpeed;
        }
        var drag = cloth.WindDrag * (speed / 30 + cloth.SubWindFactorAdd * subSpeed / 30);
        return new(air, Math.Max(0, drag));
    }

    private static uint Advance(uint phase, float turns)
    {
        // ARM64 fcvtzu saturates its per-step increment; the subsequent phase addition wraps.
        var increment = (uint)Math.Clamp((double)turns * 4294967296d, 0, uint.MaxValue);
        return unchecked(phase + increment);
    }

    private static float Sine(uint phase)
    {
        // Same 256-segment interpolation as the executable's sead sin table.
        var index = phase >> 24;
        var fraction = (phase & 0xffffff) / 16777216f;
        var first = MathF.Sin(index * (MathF.Tau / 256));
        return first + (MathF.Sin((index + 1) * (MathF.Tau / 256)) - first) * fraction;
    }
}
