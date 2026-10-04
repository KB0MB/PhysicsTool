using System.Numerics;
using HKCLTool;

namespace PhysicsTool.Core;

public enum WindAxis { Random, PositiveX, NegativeX, PositiveY, NegativeY, PositiveZ, NegativeZ }

public sealed record PreviewOptions
{
    public WindAxis Direction { get; init; } = WindAxis.Random;
    public float WindSpeed { get; init; } = HkclPreviewSimulator.DefaultWindSpeed;
    public float Gustiness { get; init; } = .35f;
    public float GravityScale { get; init; } = 1;
    public float PlaybackSpeed { get; init; } = 1;
    public int SolverPasses { get; init; } = 1;
    public void Validate()
    {
        if (!Enum.IsDefined(Direction) || !float.IsFinite(WindSpeed) || WindSpeed is < 0 or > 50 ||
            !float.IsFinite(Gustiness) || Gustiness is < 0 or > 1 || !float.IsFinite(GravityScale) || GravityScale is < 0 or > 3 ||
            !float.IsFinite(PlaybackSpeed) || PlaybackSpeed is < .1f or > 4 || SolverPasses is < 1 or > 24)
            throw new ArgumentOutOfRangeException(nameof(PreviewOptions), "Preview options are outside their supported ranges.");
    }
    public void ApplyTo(HkclPreviewSimulator solver)
    {
        Validate();
        solver.RandomWindDirections = Direction == WindAxis.Random;
        solver.WindDirection = Direction switch
        {
            WindAxis.NegativeX => -Vector3.UnitX, WindAxis.PositiveY => Vector3.UnitY, WindAxis.NegativeY => -Vector3.UnitY,
            WindAxis.PositiveZ => Vector3.UnitZ, WindAxis.NegativeZ => -Vector3.UnitZ, _ => Vector3.UnitX
        };
        solver.WindSpeed = WindSpeed; solver.WindGustiness = Gustiness;
        solver.GravityScale = GravityScale; solver.SolverIterations = SolverPasses;
    }
}

public sealed class PreviewStepClock
{
    public const double FixedStep = 1d / 60;
    private double _pending;
    public void Reset() => _pending = 0;
    public int Advance(double elapsedSeconds, float playbackSpeed)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return 0;
        if (!float.IsFinite(playbackSpeed) || playbackSpeed is < .1f or > 4) throw new ArgumentOutOfRangeException(nameof(playbackSpeed));
        // Bound catch-up after a stalled UI; subdivide high speeds so the solver's per-call cap cannot discard time.
        _pending += Math.Min(elapsedSeconds, .05) * playbackSpeed;
        var steps = (int)Math.Floor((_pending + 1e-9) / FixedStep);
        _pending = Math.Max(0, _pending - steps * FixedStep);
        return steps;
    }
}
