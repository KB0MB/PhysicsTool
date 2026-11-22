using System.Numerics;

namespace HKCLTool;

// The visual model never becomes part of an HKCL/BPHCL/HKRG document. Keeping
// its data model separate makes that boundary explicit and prevents a physics
// save from accidentally serializing visual-only state.
public sealed class VisualModelData
{
    public string SourcePath { get; init; } = string.Empty;
    public List<VisualModelMesh> Meshes { get; } = new();
    public List<VisualModelBone> Bones { get; } = new();
    public string AnimationSourcePath { get; set; } = string.Empty;
    public double AnimationFrame { get; set; }
    public Vector3 BoundsMinimum { get; set; }
    public Vector3 BoundsMaximum { get; set; }
    public int TriangleCount { get; set; }
    public int EmbeddedTextureCount { get; set; }
    public int ExternalTextureCount { get; set; }
    public int DefaultTextureMeshCount { get; set; }
    public List<string> Warnings { get; } = new();

    public bool HasTextures => Meshes.Any(mesh => mesh.Texture != null);
}

public sealed class VisualModelBone
{
    public int Index { get; init; }
    public int ParentIndex { get; init; } = -1;
    public string Name { get; init; } = string.Empty;
    public Vector3 Position { get; init; }
    public Quaternion Rotation { get; init; } = Quaternion.Identity;
    public Vector3 Scale { get; init; } = Vector3.One;
    public VisualBoneSource Sources { get; set; } = VisualBoneSource.Model;
    internal Matrix4x4? AffineTransform { get; init; }
    internal Matrix4x4 WorldMatrix => AffineTransform ?? VisualBoneTransform.Compose(Position, Rotation, Scale);

    public Vector3 AxisX => Vector3.Normalize(Vector3.Transform(Vector3.UnitX, Rotation));
    public Vector3 AxisY => Vector3.Normalize(Vector3.Transform(Vector3.UnitY, Rotation));
    public Vector3 AxisZ => Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, Rotation));
}

[Flags]
public enum VisualBoneSource
{
    None = 0,
    Model = 1,
    Support = 2,
    Cloth = 4
}

public sealed record VisualBoneOverride(
    string Name,
    Vector3 Position,
    Quaternion Rotation,
    Vector3 Scale)
{
    internal Matrix4x4? AffineTransform { get; init; }
    internal Matrix4x4 WorldMatrix => AffineTransform ?? VisualBoneTransform.Compose(Position, Rotation, Scale);
}

internal static class VisualBoneTransform
{
    internal static Matrix4x4 Compose(Vector3 position, Quaternion rotation, Vector3 scale) =>
        Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);

    internal static VisualBoneOverride Override(string name, Matrix4x4 world)
    {
        Decompose(world, out var scale, out var rotation, out var position);
        return new(name, position, rotation, scale) { AffineTransform = world };
    }

    internal static VisualModelBone Bone(int index, int parent, string name, Matrix4x4 world)
    {
        var value = Override(name, world);
        return new() { Index = index, ParentIndex = parent, Name = name, Position = value.Position,
            Rotation = value.Rotation, Scale = value.Scale, AffineTransform = world };
    }

    internal static void Decompose(Matrix4x4 world, out Vector3 scale, out Quaternion rotation, out Vector3 position)
    {
        var x = new Vector3(world.M11, world.M12, world.M13);
        var y = new Vector3(world.M21, world.M22, world.M23);
        var z = new Vector3(world.M31, world.M32, world.M33);
        position = world.Translation;
        if (!float.IsFinite(x.LengthSquared()) || !float.IsFinite(y.LengthSquared()) || !float.IsFinite(z.LengthSquared()) ||
            !float.IsFinite(position.LengthSquared()) || world.M14 != 0 || world.M24 != 0 || world.M34 != 0 ||
            !float.IsFinite(world.M44) || Math.Abs(world.M44 - 1) > .001f)
            throw new InvalidDataException("Invalid visual bone matrix.");
        if (Matrix4x4.Decompose(world, out scale, out rotation, out position)) return;

        // Rotated children of nonuniformly scaled parents legitimately contain shear.
        // SRT fields are only a frame approximation; skinning keeps the exact matrix.
        scale = new(x.Length(), y.Length(), z.Length());
        if (scale.X < 1e-8f) throw new InvalidDataException("Degenerate visual bone frame.");
        x /= scale.X;
        y -= x * Vector3.Dot(x, y);
        if (y.LengthSquared() < 1e-16f) throw new InvalidDataException("Degenerate visual bone frame.");
        y = Vector3.Normalize(y);
        var orthogonalZ = Vector3.Cross(x, y);
        if (Vector3.Dot(orthogonalZ, z) < 0) scale.Z = -scale.Z;
        rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new(
            x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, orthogonalZ.X, orthogonalZ.Y, orthogonalZ.Z, 0, 0, 0, 0, 1)));
    }
}
public enum VisualModelCullingMode
{
    Off = 0,
    CounterClockwise = 1,
    Clockwise = 2
}

public enum VisualTextureChannel
{
    None = -1,
    Red = 0,
    Green = 1,
    Blue = 2
}

public enum VisualModelAlphaMode
{
    Opaque,
    Cutout,
    Blend
}

public enum VisualModelShaderModel
{
    Standard,
    Cloth,
    Skin,
    Hair,
    Eye,
    Eyelash
}

public sealed class VisualModelUvSet
{
    public string Name { get; init; } = string.Empty;
    // One UV pair per expanded render vertex.
    public float[] Coordinates { get; init; } = Array.Empty<float>();
}

public sealed class VisualModelMaterialSettings
{
    public TotkPreviewMaterial? Totk { get; set; }
    public BotwPreviewMaterial? Botw { get; set; }
    public bool UseBotwPreviewShader { get; set; }
    public VisualModelShaderModel ShaderModel { get; set; }
    public bool UseAlbedo { get; set; } = true;
    public bool UseSpm { get; set; }
    public bool UseNormal { get; set; }
    public bool UseAmbientOcclusion { get; set; }
    public bool UseAuxiliary { get; set; }
    public bool UseEyeGlint { get; set; } = true;
    public string AlbedoUvSet { get; set; } = string.Empty;
    public string SpmUvSet { get; set; } = string.Empty;
    public string NormalUvSet { get; set; } = string.Empty;
    public string AmbientOcclusionUvSet { get; set; } = string.Empty;
    public string AuxiliaryUvSet { get; set; } = string.Empty;
    // The reference Blender cloth materials wire SPM X/R to smoothness and
    // SPM Y/G to metal.
    public VisualTextureChannel MetalChannel { get; set; } = VisualTextureChannel.Green;
    public VisualTextureChannel SmoothnessChannel { get; set; } = VisualTextureChannel.Red;
    public float MetalScale { get; set; } = 1.0f;
    public float SmoothnessScale { get; set; } = 1.0f;
    public float NormalStrength { get; set; } = 1.0f;
    public float AmbientOcclusionStrength { get; set; } = 1.0f;
    public float AuxiliaryStrength { get; set; } = 1.0f;
    public float EyeGlintStrength { get; set; } = 0.65f;
    public Vector4 Tint { get; set; } = Vector4.One;

    public VisualModelMaterialSettings Clone() => new()
    {
        Totk = Totk,
        Botw = Botw,
        UseBotwPreviewShader = UseBotwPreviewShader,
        ShaderModel = ShaderModel,
        UseAlbedo = UseAlbedo,
        UseSpm = UseSpm,
        UseNormal = UseNormal,
        UseAmbientOcclusion = UseAmbientOcclusion,
        UseAuxiliary = UseAuxiliary,
        UseEyeGlint = UseEyeGlint,
        AlbedoUvSet = AlbedoUvSet,
        SpmUvSet = SpmUvSet,
        NormalUvSet = NormalUvSet,
        AmbientOcclusionUvSet = AmbientOcclusionUvSet,
        AuxiliaryUvSet = AuxiliaryUvSet,
        MetalChannel = MetalChannel,
        SmoothnessChannel = SmoothnessChannel,
        MetalScale = MetalScale,
        SmoothnessScale = SmoothnessScale,
        NormalStrength = NormalStrength,
        AmbientOcclusionStrength = AmbientOcclusionStrength,
        AuxiliaryStrength = AuxiliaryStrength,
        EyeGlintStrength = EyeGlintStrength,
        Tint = Tint
    };
}

public sealed class VisualModelMesh
{
    // Render-only fallback; keep missing material slots empty for presets/imports.
    public bool UseMissingTextureFallback { get; init; }
    public string Name { get; init; } = string.Empty;
    // Interleaved position, normal, and the FBX albedo UV: 3 + 3 + 2 floats.
    public float[] Vertices { get; init; } = Array.Empty<float>();
    public Vector4[] Tangents { get; set; } = [];
    public string TangentUvSet { get; set; } = "";
    public uint[] Indices { get; init; } = Array.Empty<uint>();
    public Vector4 BaseColor { get; init; } = Vector4.One;
    public VisualModelCullingMode CullingMode { get; init; }
    public VisualModelTexture? Texture { get; set; }
    public VisualModelTexture? SpecularTexture { get; set; }
    public VisualModelTexture? NormalTexture { get; set; }
    public VisualModelTexture? AmbientOcclusionTexture { get; set; }
    public VisualModelTexture? AuxiliaryTexture { get; set; }
    public VisualModelTexture? EyeGlintTexture { get; set; }
    public List<VisualModelUvSet> UvSets { get; init; } = new();
    public VisualModelMaterialSettings Material { get; set; } = new();

    public string MaterialIdentity =>
        string.Join("|", Name, Texture?.Name, SpecularTexture?.Name, NormalTexture?.Name, AmbientOcclusionTexture?.Name, AuxiliaryTexture?.Name, EyeGlintTexture?.Name);
}

public sealed class VisualModelTexture
{
    public bool IsSrgb { get; init; }
    public bool IsSingleChannel { get; init; }
    public bool UseRedAsLuminance { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    // RGBA8 pixels ordered from the OpenGL texture origin.
    public byte[] Pixels { get; init; } = Array.Empty<byte>();
    public VisualModelAlphaMode AlphaMode { get; init; }
}

public sealed class VisualModelDisplaySettings
{
    public bool Visible { get; set; } = true;
    public bool ShowTextures { get; set; } = true;
    public bool Wireframe { get; set; }
    public bool MirrorX { get; set; }
    public float Opacity { get; set; } = 1.0f;
    public float UniformScale { get; set; } = 1.0f;
    public Vector3 Offset { get; set; }
    public Vector3 RotationDegrees { get; set; }

    public VisualModelDisplaySettings Clone() => new()
    {
        Visible = Visible,
        ShowTextures = ShowTextures,
        Wireframe = Wireframe,
        MirrorX = MirrorX,
        Opacity = Opacity,
        UniformScale = UniformScale,
        Offset = Offset,
        RotationDegrees = RotationDegrees
    };
}
