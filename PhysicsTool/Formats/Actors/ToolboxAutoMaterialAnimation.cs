using System.Buffers.Binary;
using ResS = Syroot.NintenTools.NSW.Bfres;
using ResU = Syroot.NintenTools.Bfres;

namespace HKCLTool;

// Material clocks are independent of skeletal animation and each auto clip loops at its own length.
internal sealed class ToolboxAutoMaterialAnimation
{
    private sealed record Channel(int Component, float Constant, ToolboxActorAnimation.Curve? Curve);
    private sealed record Track(double Length, bool Loop, Channel[] Channels, double? FixedFrame);
    private sealed record Parameter(string Material, string Name, bool TexSrt, float[] Base, List<Track> Tracks);
    private sealed record Binding(Parameter Parameter, Dictionary<string, float[]> Parameters, float[] Values);
    private readonly List<Parameter> _parameters = new();
    private readonly List<Binding> _bindings = new();
    private readonly List<(string Name, VisualModelMesh Mesh)> _meshes = new();
    private readonly ToolboxBfresDocument _document;
    private readonly string _modelName;
    internal bool HasTracks => _bindings.Any(b => b.Parameter.Tracks.Any(t => t.FixedFrame == null));
    internal static bool IsAutomatic(string name)
    {
        int index = name.IndexOf("_auto", StringComparison.OrdinalIgnoreCase);
        return index >= 0 && (index + 5 == name.Length || name[index + 5] == '_');
    }

    internal ToolboxAutoMaterialAnimation(ToolboxBfresDocument document, string modelName, List<string>? warnings)
    {
        _document = document; _modelName = modelName;
        Read(document, modelName, warnings, document, null, null);
    }

    internal void ApplyState(ToolboxBfresDocument source, string clip, double frame, List<string> warnings)
    {
        Read(_document, _modelName, warnings, source, clip, frame);
        _bindings.Clear();
        foreach (var (name, mesh) in _meshes) BindParameters(name, mesh);
        Sample(0);
    }
    internal void AddAutomatic(ToolboxBfresDocument source, List<string> warnings)
    {
        Read(_document, _modelName, warnings, source, null, null);
        _bindings.Clear();
        foreach (var (name, mesh) in _meshes) BindParameters(name, mesh);
        Sample(0);
    }

    private void Read(ToolboxBfresDocument document, string modelName, List<string>? warnings,
        ToolboxBfresDocument animationSource, string? clip, double? fixedFrame)
    {
        dynamic? model = document.Models.FirstOrDefault(m => (string)m.Name == modelName);
        if (model == null) return;
        var materials = ToolboxBfresDocument.Items((object)model.Materials).ToDictionary(m => (string)m.Name);
        if (!materials.Values.Any(m => (string?)m.ShaderAssign?.ShaderArchiveName == "uking_mat" ||
            ((string?)m.ShaderAssign?.ShaderArchiveName)?.StartsWith("material", StringComparison.OrdinalIgnoreCase) == true)) return;
        IEnumerable<object> animations = animationSource.Resource is ResS.ResFile nx ? nx.MaterialAnims.Cast<object>()
            : animationSource.Resource is ResU.ResFile wii ? wii.ShaderParamAnims.Values.Cast<object>().Concat(wii.TexSrtAnims.Values).Concat(wii.ColorAnims.Values) : [];
        foreach (dynamic animation in animations)
        {
            string animationName = animation.Name;
            if (clip == null ? !IsAutomatic(animationName) : animationName != clip && animationName != clip + "_fsp" && animationName != clip + "_fts" && animationName != clip + "_fcl") continue;
            var tracks = ToolboxBfresDocument.Items(animationSource.IsSwitch ? (object)animation.MaterialAnimDataList : (object)animation.ShaderParamMatAnims);
            foreach (dynamic materialTrack in tracks)
            {
                if (!materials.TryGetValue((string)materialTrack.Name, out var material)) continue;
                foreach (dynamic info in ToolboxBfresDocument.Items((object)materialTrack.ParamAnimInfos))
                {
                    try
                    {
                        string name = info.Name;
                        dynamic? definition = ToolboxBfresDocument.Items((object)material.ShaderParams).FirstOrDefault(p => (string)p.Name == name);
                        if (definition == null) continue;
                        string type = definition.Type.ToString();
                        bool srt = type.Contains("TexSrt", StringComparison.Ordinal);
                        if (!srt && !type.StartsWith("Float", StringComparison.Ordinal)) continue;
                        int offset = (int)definition.DataOffset, size = (int)definition.DataSize;
                        byte[] bytes = material.ShaderParamData;
                        if (size < (srt ? 24 : 4) || offset < 0 || offset > bytes.Length - size) throw new InvalidDataException("Invalid parameter data");
                        var parameter = _parameters.FirstOrDefault(p => p.Material == (string)materialTrack.Name && p.Name == name);
                        if (parameter == null)
                        {
                            var values = new float[size / 4];
                            for (int i = 0; i < values.Length; i++)
                            {
                                var word = bytes.AsSpan(offset + i * 4, 4);
                                values[i] = srt && i == 0 ? document.IsSwitch ? BinaryPrimitives.ReadUInt32LittleEndian(word) : BinaryPrimitives.ReadUInt32BigEndian(word)
                                    : document.IsSwitch ? BinaryPrimitives.ReadSingleLittleEndian(word) : BinaryPrimitives.ReadSingleBigEndian(word);
                            }
                            parameter = new((string)materialTrack.Name, name, srt, values, []); _parameters.Add(parameter);
                        }
                        var channels = new List<Channel>();
                        for (int i = 0; i < (int)info.ConstantCount; i++)
                        {
                            dynamic constant = materialTrack.Constants[(int)info.BeginConstant + i];
                            int component = checked((int)constant.AnimDataOffset) / 4;
                            if ((uint)component >= parameter.Base.Length) throw new InvalidDataException("Constant outside parameter");
                            channels.Add(new(component, srt && component == 0 ? (uint)constant.Value : (float)constant.Value, null));
                        }
                        for (int i = 0; i < (int)info.FloatCurveCount; i++)
                        {
                            dynamic curve = materialTrack.Curves[(int)info.BeginCurve + i];
                            uint target = curve.AnimDataOffset;
                            int component = checked((int)target) / 4;
                            var frames = (float[])curve.Frames; var keys = (float[,])curve.Keys;
                            string curveType = curve.CurveType.ToString();
                            int coefficients = curveType == "Cubic" ? 4 : curveType == "Linear" ? 2 : 1;
                            if (target % 4 != 0 || (uint)component >= parameter.Base.Length || frames.Length == 0 || keys.GetLength(0) < frames.Length || keys.GetLength(1) < coefficients)
                                throw new InvalidDataException("Invalid material curve");
                            if (curveType is not ("Cubic" or "Linear" or "StepFloat")) throw new InvalidDataException("Unsupported material curve " + curveType);
                            channels.Add(new(component, 0, new(target, frames, keys, (float)curve.Scale, (float)curve.Offset, curveType)));
                        }
                        if (channels.Count > 0) parameter.Tracks.Add(new(Math.Max(1, (int)animation.FrameCount), animation.Flags.ToString().Contains("Loop"), channels.ToArray(), fixedFrame));
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IndexOutOfRangeException or OverflowException)
                    { warnings?.Add($"Auto material animation {animation.Name}/{info.Name}: {ex.Message}"); }
                }
            }
        }
    }

    internal void Bind(string name, VisualModelMesh mesh)
    {
        _meshes.Add((name, mesh)); BindParameters(name, mesh);
    }

    private void BindParameters(string name, VisualModelMesh mesh)
    {
        var parameters = mesh.Material.Totk is { Native: true } totk ? totk.Parameters : mesh.Material.Botw?.Parameters;
        if (parameters == null) return;
        foreach (var parameter in _parameters.Where(p => p.Material == name && p.Tracks.Count > 0 && parameters.ContainsKey(p.Name)))
            _bindings.Add(new(parameter, parameters, (float[])parameter.Base.Clone()));
    }

    internal void Sample(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var binding in _bindings)
        {
            var parameter = binding.Parameter; var values = binding.Values;
            parameter.Base.CopyTo(values, 0);
            foreach (var track in parameter.Tracks)
            {
                double frame = track.FixedFrame ?? seconds * ToolboxActorAnimation.BaseFramesPerSecond;
                frame = track.FixedFrame == null && track.Loop ? frame % track.Length : Math.Clamp(frame, 0, track.Length);
                foreach (var channel in track.Channels) values[channel.Component] = channel.Curve?.Sample(frame) ?? channel.Constant;
            }
            if (!values.All(float.IsFinite)) continue;
            var destination = binding.Parameters[parameter.Name];
            if (parameter.TexSrt)
                BotwPreviewMaterial.TextureMatrix((uint)values[0], values[1], values[2], values[3], values[4], values[5]).CopyTo(destination, 0);
            else values.CopyTo(destination, 0);
        }
    }
}
