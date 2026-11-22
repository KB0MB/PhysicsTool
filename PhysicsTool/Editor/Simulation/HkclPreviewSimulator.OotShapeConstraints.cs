using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclPreviewSimulator
{
    private readonly Dictionary<int, (VolumeConstraintSetData Data, OotVolumeFrame Frame)> _ootVolumes = new();
    private readonly Dictionary<int, OotBonePlane[]> _ootPlanes = new();
    private readonly Dictionary<OotBonePlane, (Vector3 Normal, Vector3 Origin)> _ootPlaneInputs = new();
    private OotConstraintVelocityMode _ootPlaneVelocity;

    private void InitializeOotShapeConstraints(ParticlePreviewData source)
    {
        if (_profile != ClothSolverProfile.OotExperimental) return;
        OotShapeConstraintValidation.Validate(source);
        _ootPlaneVelocity = OotConstraintVelocity.ForStep(2, _authoredSubSteps, 1, 1, 1, source.OotDirectConstraintVelocity);
        foreach (var set in source.OotVolumeConstraints)
            _ootVolumes.Add(set.ConstraintSetIndex, (set with { Frames = set.Frames.ToArray(), Applies = set.Applies.ToArray() }, new()));
        foreach (var group in source.OotBonePlanes.GroupBy(p => p.ConstraintSetIndex))
            _ootPlanes.Add(group.Key, group.ToArray());
    }

    private void SampleOotBonePlanes()
    {
        foreach (var plane in _ootPlanes.Values.SelectMany(p => p))
        {
            var delta = _inputBoneDeltas.GetValueOrDefault(plane.BoneIndex, Matrix4x4.Identity);
            _ootPlaneInputs[plane] = (Vector3.TransformNormal(plane.RestNormal, delta), Vector3.Transform(plane.RestOrigin, delta));
        }
    }

    private bool SolveOotShapeConstraint(int index)
    {
        if (_ootVolumes.TryGetValue(index, out var volume))
        {
            Vector3 center = Vector3.Zero, x = Vector3.Zero, y = Vector3.Zero, z = Vector3.Zero;
            foreach (var frame in volume.Data.Frames)
                center += _particles[frame.ParticleIndex].Position * frame.Weight;
            foreach (var frame in volume.Data.Frames)
            {
                var relative = (_particles[frame.ParticleIndex].Position - center) * frame.Weight;
                x += relative * frame.FrameVector.X;
                y += relative * frame.FrameVector.Y;
                z += relative * frame.FrameVector.Z;
            }
            var transform = volume.Frame.Fit(x, y, z, center);
            foreach (var apply in volume.Data.Applies)
            {
                var particle = _particles[apply.ParticleIndex];
                var local = new Vector3(apply.FrameVector.X, apply.FrameVector.Y, apply.FrameVector.Z);
                // Native records supply the correction coefficient, not inverse mass.
                ApplyConstraintCorrection(particle, (Vector3.Transform(local, transform) - particle.Position) * apply.Stiffness);
            }
            return true;
        }
        if (_ootPlanes.TryGetValue(index, out var planes))
        {
            foreach (var plane in planes)
            {
                var input = _ootPlaneInputs[plane];
                var particle = _particles[plane.ParticleIndex];
                float distance = Vector3.Dot(input.Normal, particle.Position - input.Origin) + plane.Offset;
                var before = particle.Position;
                if (distance < 0) ApplyConstraintCorrection(particle, input.Normal * (-distance * plane.Stiffness));
                particle.PreviousPosition = OotConstraintVelocity.PreviousAfterCorrection(
                    before, particle.PreviousPosition, particle.Position, _ootPlaneVelocity);
            }
            return true;
        }
        return false;
    }
}
