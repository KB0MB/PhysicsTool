using ResU = Syroot.NintenTools.Bfres;
using ResS = Syroot.NintenTools.NSW.Bfres;

namespace HKCLTool;

// Actor texture variations are static FTXP selections, independent of the skeletal clock.
internal static class ToolboxActorAppearance
{
    internal static Dictionary<(string Material, string Sampler), string> Read(
        ToolboxBfresDocument document, string name, int frame, List<string> warnings)
    {
        var result = new Dictionary<(string Material, string Sampler), string>();
        if (string.IsNullOrWhiteSpace(name) || name == "Dummy") return result;
        if (document.Resource is ResS.ResFile nx)
        {
            var anim = nx.MaterialAnims.FirstOrDefault(a => a.Name == name || a.Name == name + "_ftp");
            if (anim == null) return result;
            var names = anim.TextureNames.ToArray();
            foreach (var material in anim.MaterialAnimDataList)
            foreach (var info in material.TexturePatternAnimInfos)
            {
                try
                {
                    int index;
                    if (info.CurveIndex != ushort.MaxValue)
                    {
                        var curve = material.Curves[info.CurveIndex];
                        index = Sample(curve.Frames, curve.Keys, (int)curve.Offset, Math.Clamp(frame, 0, anim.FrameCount));
                    }
                    else if (info.BeginConstant != ushort.MaxValue)
                    {
                        if (material.Constants == null) throw new InvalidDataException("Missing texture constant.");
                        index = checked((int)material.Constants[info.BeginConstant].Value);
                    }
                    else continue;
                    Add(material.Name, info.Name, index, names);
                }
                catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException or OverflowException)
                { warnings.Add($"Texture variation {name}, {material.Name}/{info.Name}: invalid texture track."); }
            }
        }
        else if (document.Resource is ResU.ResFile wii)
        {
            var anim = wii.TexPatternAnims.Values.FirstOrDefault(a => a.Name == name);
            if (anim == null) return result;
            var names = anim.TextureRefNames?.Select(t => t.Name).ToArray();
            if (names == null || names.Length == 0) names = anim.TextureRefs.Keys.ToArray();
            foreach (var material in anim.TexPatternMatAnims)
            for (var i = 0; i < material.PatternAnimInfos.Count; i++)
            {
                var info = material.PatternAnimInfos[i];
                try
                {
                    int index;
                    if (info.CurveIndex >= 0)
                    {
                        var curve = material.Curves[info.CurveIndex];
                        index = Sample(curve.Frames, curve.Keys, (int)curve.Offset, Math.Clamp(frame, 0, anim.FrameCount));
                    }
                    else index = material.BaseDataList[i];
                    Add(material.Name, info.Name, index, names);
                }
                catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException or OverflowException)
                { warnings.Add($"Texture variation {name}, {material.Name}/{info.Name}: invalid texture track."); }
            }
        }
        return result;

        void Add(string material, string sampler, int index, string[] names)
        {
            if ((uint)index < names.Length) result[(material, sampler)] = names[index];
            else warnings.Add($"Texture variation {name}, {material}/{sampler}: texture index {index} is out of range.");
        }
    }

    internal static int Sample(float[] frames, float[,] keys, int offset, double frame)
    {
        if (frames.Length == 0 || keys.GetLength(0) < frames.Length || keys.GetLength(1) == 0)
            throw new InvalidDataException("Empty or incomplete texture-pattern curve.");
        var index = Array.BinarySearch(frames, (float)frame);
        if (index < 0) index = Math.Max(0, ~index - 1);
        // StepInt offsets are integers. Scale is unused (often zero), not a multiplier.
        return checked((int)keys[index, 0] + offset);
    }
}
