using System.Collections.Concurrent;
using System.Numerics;

namespace HKCLTool;

public sealed class BotwParticleMaterial
{
    public BotwPreviewSampler[] Samplers { get; internal set; } = [];
    public Vector4 Color0 { get; internal set; }
    public Vector4 Color1 { get; internal set; }
    public Vector4 Texture1Srt { get; internal set; }
    public float Alpha { get; internal set; }
}

internal sealed class BotwArrowEffect
{
    private sealed record Key(float Time, Vector3 Value);
    private static readonly ConcurrentDictionary<(string Path, long Stamp, string User), Lazy<BotwArrowEffect?>> Cache = new();
    internal BotwEffectBinding Binding { get; }
    internal VisualModelTexture Mask { get; }
    internal VisualModelTexture Noise { get; }
    private readonly Vector3 _translation, _rotation, _scale;
    private readonly Vector2 _pivot, _scroll, _uvScale;
    private readonly Vector4 _color0, _color1;
    private readonly Key[] _colorKeys0, _colorKeys1, _alphaKeys;
    private readonly float _colorScale;
    private readonly VisualModelMaterialSettings _material = new() { Particle = new() };
    private static readonly uint[] Indices = [0, 1, 2, 0, 2, 3];

    internal static BotwArrowEffect? Load(BotwActorResources resources)
    {
        if (resources.IsTotk || resources.EffectUser is not { } user) return null;
        foreach (string root in resources.Roots)
        {
            string path = Path.Combine(root, "Pack", "Bootup.pack");
            if (!File.Exists(path)) continue;
            var template = Cache.GetOrAdd((path, File.GetLastWriteTimeUtc(path).Ticks, user), key => new(() =>
            {
                var sarc = BotwSarcArchive.OpenIndexed(key.Path);
                if (sarc.LittleEndian != resources.IsSwitch) return null;
                byte[] Read(string name)
                {
                    var entry = sarc.Entries.FirstOrDefault(e => e.Name == name) ?? throw new FileNotFoundException("Missing native effect resource " + name);
                    return BotwYaz0.Decode(sarc.Read(entry).ToArray());
                }
                var binding = BotwEffectBinding.Read(Read("ELink2/ELink2DB.sbelnk"), !resources.IsSwitch, user);
                if (binding?.Effect != "AncientArrow_Blade01") return null;
                return ReadArchive(Read("Effect/GameResident.sesetlist"), binding);
            })).Value;
            // Each attachment owns its sampled uniforms; immutable texture/definition data are shared.
            if (template != null) return new(template);
        }
        return null;
    }

    private BotwArrowEffect(BotwArrowEffect other)
    {
        Binding = other.Binding; Mask = other.Mask; Noise = other.Noise;
        _translation = other._translation; _rotation = other._rotation; _scale = other._scale;
        _pivot = other._pivot; _scroll = other._scroll; _uvScale = other._uvScale;
        _color0 = other._color0; _color1 = other._color1; _colorScale = other._colorScale;
        _colorKeys0 = other._colorKeys0; _colorKeys1 = other._colorKeys1; _alphaKeys = other._alphaKeys;
        _material.Particle!.Samplers = other._material.Particle!.Samplers;
    }

    private BotwArrowEffect(BotwEffectBinary r, int section, BotwEffectBinding binding, VisualModelTexture mask, VisualModelTexture noise)
    {
        Binding = binding; Mask = mask; Noise = noise;
        int p = r.Relative(section, 20);
        r.Span(p, 0xa88);
        if (r.B(p + 0x8a8) != 1 || r.B(p + 0x8aa) != 3 || r.U64(p + 0x8c8) != ulong.MaxValue ||
            r.B(p + 0x8f8) != 2 || r.B(p + 0x8f9) != 3 || r.B(p + 0x89e) != 0)
            throw new NotSupportedException("Unsupported native arrow blade particle/combiner configuration.");
        _translation = r.V3(p + 0x770) + binding.Position;
        _rotation = r.V3(p + 0x700) + r.V3(p + 0x788);
        _scale = r.V3(p + 0x9c8) * r.V3(p + 0x7a0);
        _pivot = new(r.F(p + 0xd0), r.F(p + 0xd4));
        _scroll = new(r.F(p + 0x310), r.F(p + 0x314));
        _uvScale = new(r.F(p + 0x330), r.F(p + 0x334));
        _color0 = r.V4(p + 0x9a8) * r.V4(p + 0x7ac);
        _color1 = r.V4(p + 0x9b8) * r.V4(p + 0x7bc);
        _color0.W = r.F(p + 0x440) * r.F(p + 0x7b8);
        _color1.W = r.F(p + 0x540) * r.F(p + 0x7c8);
        _colorScale = r.F(p + 0x3b0);
        BotwPreviewSampler Sampler(int s)
        {
            int Wrap(int mode) => mode switch { 0 => 2, 1 => 0, 2 => 1, _ => throw new NotSupportedException("Unsupported particle texture wrap.") };
            return new(Wrap(r.B(s + 8)), Wrap(r.B(s + 9)), r.B(s + 10) == 0, r.B(s + 10) == 0,
                MaxLod: r.F(s + 12));
        }
        _material.Particle!.Samplers = [Sampler(p + 0x9f8), Sampler(p + 0xa18)];
        Key[] Curve(string name)
        {
            int s = r.Sections(r.Relative(section, 16)).FirstOrDefault(s => r.Magic(s) == name, -1);
            if (s < 0) return [];
            int d = r.Relative(s, 20), count = r.I32(d + 4);
            if (count is < 0 or > 4096) throw new InvalidDataException("Invalid particle animation key count.");
            var keys = Enumerable.Range(0, count).Select(i => new Key(r.F(d + 24 + i * 16), r.V3(d + 12 + i * 16))).ToArray();
            if (keys.Zip(keys.Skip(1)).Any(pair => pair.First.Time > pair.Second.Time)) throw new InvalidDataException("Unsorted particle animation keys.");
            return keys;
        }
        _colorKeys0 = Curve("EAC0"); _colorKeys1 = Curve("EAC1"); _alphaKeys = Curve("EAA0");
    }

    internal static BotwArrowEffect ReadArchive(byte[] data, BotwEffectBinding binding)
    {
        bool wii = data.AsSpan(0, 4).SequenceEqual("EFTB"u8);
        var r = new BotwEffectBinary(data, wii);
        if ((!wii && r.Magic(0) != "VFXB") || (wii ? r.U32(4) : r.U16(10)) != 20)
            throw new NotSupportedException("Unsupported BotW particle archive version.");
        var sections = new List<int>();
        void Visit(int start, int depth)
        {
            if (depth > 16) throw new InvalidDataException("Invalid particle section nesting.");
            foreach (int s in r.Sections(start)) { sections.Add(s); Visit(r.Relative(s, 8), depth + 1); }
        }
        Visit(wii ? 0x30 : 0x40, 0);
        int set = sections.FirstOrDefault(s => r.Magic(s) == "ESET" && r.Text(r.Relative(s, 20) + 16, 64) == binding.Effect, -1);
        if (set < 0) throw new InvalidDataException("Missing native Ancient Arrow blade emitter set.");
        int emitter = r.Sections(r.Relative(set, 8)).First(s => r.Magic(s) == "EMTR" && r.Text(r.Relative(s, 20) + 16, 64) == "blade_left_a");
        int p = r.Relative(emitter, 20);
        var ids = new[] { r.U64(p + 0x9f8), r.U64(p + 0xa18) };
        var textures = new Dictionary<ulong, VisualModelTexture>();
        if (!wii)
        {
            int textureSection = sections.First(s => r.Magic(s) == "GRTF");
            int names = sections.First(s => r.Magic(s) == "GTNT");
            var map = new Dictionary<ulong, string>();
            int t = r.Relative(names, 20);
            for (int i = 0; i < 100000; i++)
            {
                ulong id = r.U64(t); int next = r.I32(t + 8);
                if (ids.Contains(id)) map[id] = r.Text(t + 16);
                if (next == 0) break;
                if (next < 16) throw new InvalidDataException("Invalid effect texture table.");
                t = checked(t + next);
            }
            int start = r.Relative(textureSection, 20);
            using var stream = new MemoryStream(data, start, data.Length - start);
            var bntx = new Syroot.NintenTools.NSW.Bntx.BntxFile(stream);
            foreach (var (id, name) in map)
                textures[id] = ToolboxActorTexture.Decode(bntx.Textures.First(t => t.Name == name));
        }
        else
        {
            foreach (int s in sections.Where(s => r.Magic(s) == "TEXR"))
            {
                int t = r.Relative(s, 20); ulong id = r.U32(t + 36);
                if (!ids.Contains(id)) continue;
                int image = r.Sections(r.Relative(s, 8)).First(s => r.Magic(s) == "GX2B");
                textures[id] = ToolboxActorTexture.DecodeEffectSurface(id.ToString(), r.U16(t), r.U16(t + 2), r.U32(t + 16), r.U32(t + 20),
                    r.Span(r.Relative(image, 20), r.I32(t + 28)).ToArray());
            }
        }
        if (ids.Any(id => !textures.ContainsKey(id))) throw new InvalidDataException("Missing native Ancient Arrow effect textures.");
        return new(r, emitter, binding, textures[ids[0]], textures[ids[1]]);
    }

    private static Vector3 Sample(Key[] keys, float frame, Vector3 fallback)
    {
        if (keys.Length == 0) return fallback;
        if (frame <= keys[0].Time) return keys[0].Value;
        for (int i = 1; i < keys.Length; i++)
            if (frame < keys[i].Time) return Vector3.Lerp(keys[i - 1].Value, keys[i].Value, (frame - keys[i - 1].Time) / (keys[i].Time - keys[i - 1].Time));
        return keys[^1].Value;
    }

    internal VisualModelMesh Pose(Matrix4x4 bone, float? effectFrame)
    {
        float frame = effectFrame ?? 0;
        var material = _material.Particle!;
        material.Color0 = new Vector4(new Vector3(_color0.X, _color0.Y, _color0.Z) * Sample(_colorKeys0, frame, Vector3.One) * _colorScale, _color0.W * Sample(_alphaKeys, frame, Vector3.One).X);
        material.Color1 = new Vector4(new Vector3(_color1.X, _color1.Y, _color1.Z) * Sample(_colorKeys1, frame, Vector3.One) * _colorScale, _color1.W);
        material.Alpha = effectFrame == null ? 0 : 1;
        material.Texture1Srt = new(_uvScale.X, _uvScale.Y, (1 - _uvScale.X) * .5f - _scroll.X * frame, (1 - _uvScale.Y) * .5f - _scroll.Y * frame);
        var rotation = Matrix4x4.CreateRotationY(_rotation.Y) * Matrix4x4.CreateRotationZ(_rotation.Z) * Matrix4x4.CreateRotationX(_rotation.X);
        var transform = rotation * Matrix4x4.CreateTranslation(_translation) * bone;
        float[] vertices = new float[32];
        Vector2[] uv = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        for (int i = 0; i < 4; i++)
        {
            var local = new Vector3((uv[i].X - .5f + _pivot.X * .5f) * _scale.X, (uv[i].Y - .5f + _pivot.Y * .5f) * _scale.Y, 0);
            var v = Vector3.Transform(local, transform);
            vertices[i * 8] = v.X; vertices[i * 8 + 1] = v.Y; vertices[i * 8 + 2] = v.Z;
            vertices[i * 8 + 5] = 1; vertices[i * 8 + 6] = uv[i].X; vertices[i * 8 + 7] = 1 - uv[i].Y;
        }
        return new() { Name = "AncientArrow_Blade01/blade_left_a", Vertices = vertices, Indices = Indices, Texture = Mask, SpecularTexture = Noise, Material = _material };
    }
}

internal sealed partial class BotwActorResources
{
    internal string? EffectUser => IsTotk ? null : Objects(Parameters("ActorLink", ActorName, "bxml"))
        .Select(o => o.Find("ElinkUser")?.AsString()).FirstOrDefault(v => v != null && v != "Dummy");
}
