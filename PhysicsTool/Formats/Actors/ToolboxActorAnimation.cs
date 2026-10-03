using System.Numerics;

namespace HKCLTool;

internal sealed class ToolboxActorAnimation
{
    // BotW's clock setup at 0x710183ec94 initializes 30 Hz. BAS state-rate
    // overrides are separate from this viewer's unmodified clip rate.
    public const double BaseFramesPerSecond = 30;
    internal sealed record Curve(uint Target, float[] Frames, float[,] Keys, float Scale, float Offset, string Type)
    {
        public float Sample(double frame)
        {
            if (Frames.Length == 0) return Offset;
            var index = Array.BinarySearch(Frames, (float)frame);
            if (index < 0) index = Math.Max(0, ~index - 1);
            index = Math.Min(index, Frames.Length - 1);
            var t = index + 1 < Frames.Length && Frames[index + 1] > Frames[index]
                ? (float)Math.Clamp((frame - Frames[index]) / (Frames[index + 1] - Frames[index]), 0, 1) : 0;
            // BFRES stores normalized polynomial coefficients, not Bezier handles.
            var value = Keys[index, 0];
            if (Type == "Cubic") value += t * (Keys[index, 1] + t * (Keys[index, 2] + t * Keys[index, 3]));
            else if (Type == "Linear") value += t * Keys[index, 1];
            return value * Scale + Offset;
        }
    }
    private sealed record Track(Vector3 Position, Vector4 Rotation, Vector3 Scale, string BaseFlags, Curve[] Curves);
    private readonly Dictionary<string, Track> _tracks;
    private readonly bool _euler;
    public string Name { get; }
    public string ResourceName { get; }
    public int FrameCount { get; }
    public IEnumerable<string> BoneNames => _tracks.Keys;
    public ToolboxActorAnimation(object source, string resource)
    {
        dynamic animation = source;
        Name = animation.Name; ResourceName = resource; FrameCount = Math.Max(1, (int)animation.FrameCount);
        _euler = animation.FlagsRotate.ToString().Contains("Euler");
        _tracks = new(StringComparer.OrdinalIgnoreCase);
        foreach (dynamic bone in ToolboxBfresDocument.Items((object)animation.BoneAnims))
        {
            var curves = ToolboxBfresDocument.Items((object)bone.Curves).Select(c => new Curve((uint)c.AnimDataOffset,
                (float[])c.Frames, (float[,])c.Keys, (float)c.Scale == 0 ? 1 : (float)c.Scale, (float)c.Offset, c.CurveType.ToString())).ToArray();
            _tracks[(string)bone.Name] = new(ToolboxActorModel.V3(bone.BaseData.Translate), ToolboxActorModel.V4(bone.BaseData.Rotate),
                ToolboxActorModel.V3(bone.BaseData.Scale), bone.FlagsBase.ToString(), curves);
        }
    }
    public void Sample(string name, double frame, ref Vector3 position, ref Vector4 rotation, ref Vector3 scale, ref bool euler)
    {
        if (!_tracks.TryGetValue(name, out var track)) return;
        if (track.BaseFlags.Contains("Translate")) position = track.Position;
        if (track.BaseFlags.Contains("Scale")) scale = track.Scale;
        if (track.BaseFlags.Contains("Rotate")) { rotation = track.Rotation; euler = _euler; }
        foreach (var curve in track.Curves)
        {
            var value = curve.Sample(frame);
            switch (curve.Target)
            {
                case 0x4: scale.X = value; break;
                case 0x8: scale.Y = value; break;
                case 0xc: scale.Z = value; break;
                case 0x10: position.X = value; break;
                case 0x14: position.Y = value; break;
                case 0x18: position.Z = value; break;
                case 0x20: rotation.X = value; euler = _euler; break;
                case 0x24: rotation.Y = value; euler = _euler; break;
                case 0x28: rotation.Z = value; euler = _euler; break;
                case 0x2c: rotation.W = value; euler = _euler; break;
            }
        }
    }
    public override string ToString() => Name;
}
