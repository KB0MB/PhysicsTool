using Assimp;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop.Rendering;

public sealed class ModelAnimationClip
{
    internal sealed record Track(VectorKey[] Positions, QuaternionKey[] Rotations, VectorKey[] Scales);
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly double _ticksPerFrame = 1;
    internal ToolboxActorAnimation? Native { get; }
    public string Name { get; }
    public double EndFrame { get; }
    public double FramesPerSecond { get; }
    internal float UnitScale { get; } = 1;
    public IEnumerable<string> BoneNames => Native?.BoneNames ?? _tracks.Keys;
    internal ModelAnimationClip(ToolboxActorAnimation animation)
    {
        Native = animation; Name = animation.Name; EndFrame = animation.FrameCount; FramesPerSecond = ToolboxActorAnimation.BaseFramesPerSecond;
    }
    internal ModelAnimationClip(Animation animation, float units)
    {
        Name = string.IsNullOrWhiteSpace(animation.Name) ? "Animation" : animation.Name;
        // Assimp time units may be milliseconds rather than display frames.
        // Normalize transport to the same 30 Hz base used by native BFRES clips.
        _ticksPerFrame = (animation.TicksPerSecond > 0 ? animation.TicksPerSecond : 30) / 30;
        FramesPerSecond = 30;
        EndFrame = animation.DurationInTicks / _ticksPerFrame; UnitScale = units;
        foreach (var track in animation.NodeAnimationChannels)
            _tracks[Normalize(track.NodeName)] = new(track.PositionKeys.OrderBy(k => k.Time).ToArray(),
                track.RotationKeys.OrderBy(k => k.Time).ToArray(), track.ScalingKeys.OrderBy(k => k.Time).ToArray());
        if (!double.IsFinite(EndFrame) || EndFrame <= 0 || !double.IsFinite(FramesPerSecond))
            throw new InvalidDataException("Animation has an invalid duration or frame rate.");
    }
    internal static string Normalize(string name) => name[(name.LastIndexOf(':') + 1)..];
    internal void Sample(string name, double frame, float modelUnits, ref Vector3 p, ref Quaternion q, ref Vector3 s)
    {
        if (Native != null)
        {
            var rotation = new Vector4(q.X, q.Y, q.Z, q.W); var euler = false;
            p *= modelUnits;
            Native.Sample(Normalize(name), frame, ref p, ref rotation, ref s, ref euler);
            p /= modelUnits;
            q = euler ? Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationX(rotation.X) * Matrix4x4.CreateRotationY(rotation.Y) * Matrix4x4.CreateRotationZ(rotation.Z))
                : new(rotation.X, rotation.Y, rotation.Z, rotation.W);
        }
        else if (_tracks.TryGetValue(Normalize(name), out var track))
        {
            frame *= _ticksPerFrame;
            if (track.Positions.Length > 0) p = Vector(track.Positions, frame) * (UnitScale / modelUnits);
            if (track.Scales.Length > 0) s = Vector(track.Scales, frame);
            if (track.Rotations.Length > 0)
            {
                var i = Segment(track.Rotations.Length, index => track.Rotations[index].Time, frame);
                var a = track.Rotations[i]; var b = track.Rotations[Math.Min(i + 1, track.Rotations.Length - 1)];
                q = Quaternion.Slerp(a.Value, b.Value, Factor(a.Time, b.Time, frame));
            }
        }
        q = q.LengthSquared() > 1e-12f ? Quaternion.Normalize(q) : Quaternion.Identity;
    }
    private static Vector3 Vector(VectorKey[] keys, double frame)
    {
        var i = Segment(keys.Length, index => keys[index].Time, frame);
        var a = keys[i]; var b = keys[Math.Min(i + 1, keys.Length - 1)];
        return Vector3.Lerp(a.Value, b.Value, Factor(a.Time, b.Time, frame));
    }
    private static int Segment(int count, Func<int, double> time, double frame)
    {
        int low = 0, high = count - 1;
        while (low < high) { var mid = (low + high + 1) / 2; if (time(mid) <= frame) low = mid; else high = mid - 1; }
        return low;
    }
    private static float Factor(double start, double end, double frame) => end > start ? (float)Math.Clamp((frame - start) / (end - start), 0, 1) : 0;
    public static IReadOnlyList<ModelAnimationClip> Load(string path)
    {
        if (Path.GetExtension(path).Equals(".bfres", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".sbfres", StringComparison.OrdinalIgnoreCase))
        {
            var resource = new ToolboxBfresDocument(File.ReadAllBytes(path));
            var clips = resource.SkeletalAnimations.Select(a => new ModelAnimationClip(new ToolboxActorAnimation((object)a, Path.GetFileName(path)))).ToArray();
            if (clips.Length == 0) throw new InvalidDataException("The BFRES contains no skeletal animations.");
            return clips;
        }
        if (!new[] { ".fbx", ".dae", ".gltf", ".glb" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
            throw new NotSupportedException("Select an animation BFRES, FBX, DAE, glTF, or GLB. Maya .anim is not yet supported by the portable importer.");
        using var importer = new AssimpContext();
        var scene = importer.ImportFile(path, PostProcessSteps.ValidateDataStructure);
        if (!scene.HasAnimations) throw new InvalidDataException("The file contains no skeletal animations.");
        var units = 1f;
        if (Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase) && scene.Metadata.TryGetValue("UnitScaleFactor", out var value))
            units = Convert.ToSingle(value.Data) * .01f;
        if (!float.IsFinite(units) || units <= 0) throw new InvalidDataException("Invalid animation unit scale.");
        return scene.Animations.Select(a => new ModelAnimationClip(a, units)).ToArray();
    }
    public override string ToString() => Name;
}
