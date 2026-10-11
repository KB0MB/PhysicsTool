using System.Buffers.Binary;
using System.Numerics;
using HKCLTool;
using PhysicsTool.Desktop.Rendering;
using ZstdSharp;
using Res = Syroot.NintenTools.NSW.Bfres;

internal static class TotkMaterialChecks
{
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        foreach (var (height, expected) in new[] { (1, 1), (10, 1), (11, 2), (21, 2), (22, 4), (42, 4), (43, 8), (85, 8), (86, 16), (256, 16), (1024, 16) })
            Require(TexToGoTexture.BaseBlockHeight(height) == expected, $"TXTG inferred block height incorrect for {height}");
        TallTexture();
        var bc = Fixture(0x203, [0, 1, 2, 3]);
        var red = TexToGoTexture.Decode(bc, "fixture", 0);
        var green = TexToGoTexture.Decode(bc, "fixture", 1);
        var grayscale = TexToGoTexture.Decode(Fixture(0x602, [0, 1, 2, 3]), "grayscale");
        Require(grayscale.IsSingleChannel && grayscale.UseRedAsLuminance && grayscale.Pixels[0] == 128 && grayscale.Pixels[1] == 0 && grayscale.Pixels[2] == 0,
            "BC4 luminance metadata or raw mask data incorrect");
        var explicitSwizzle = TexToGoTexture.Decode(Fixture(0x602, [0, 0, 0, 5]), "explicit");
        Require(!explicitSwizzle.UseRedAsLuminance && explicitSwizzle.Pixels[1] == 128, "Explicit BC4 channel selectors were replaced");
        Require(red.IsSrgb && red.Pixels[0] > 240 && red.Pixels[1] < 10, "BC1 base surface or sRGB metadata incorrect");
        Require(green.Pixels[1] > 240 && green.Pixels[0] < 10, "TexToGo array selection incorrect");
        var channels = TexToGoTexture.Decode(Fixture(0x202, [2, 1, 0, 5]), "swizzle");
        Require(channels.Pixels[2] > 240 && channels.Pixels[0] < 10 && channels.Pixels[3] == 255, "TexToGo channel selectors incorrect");
        var astc = TexToGoTexture.Decode(Fixture(0x109, [0, 1, 2, 3]), "astc");
        Require(astc.IsSrgb && astc.Pixels[0] > 240 && astc.Pixels[1] < 10 && astc.Pixels[3] == 255, "Managed ASTC decode incorrect");
        try { TexToGoTexture.Decode(bc[..90], "truncated"); throw new Exception("Truncated TXTG accepted"); } catch (InvalidDataException) { }
        var invalid = (byte[])bc.Clone(); invalid[14] = 255;
        try { TexToGoTexture.Decode(invalid, "invalid"); throw new Exception("Invalid TXTG accepted"); } catch (InvalidDataException) { }

        var m = new Res.Material { ShaderAssign = new(), TextureRefs = ["color", "normal"], SamplerDict = new(), ShaderParams = [], ShaderParamData = new byte[28] };
        m.SamplerDict.Add("_ga"); m.SamplerDict.Add("_gn");
        var s = m.ShaderAssign; s.SamplerAssignDict = new(); s.SamplerAssigns = ["_gn", "_ga"];
        s.SamplerAssignDict.Add("_n0"); s.SamplerAssignDict.Add("_a0");
        s.ShaderOptionDict = new(); s.ShaderOptions = [];
        void Option(string name, string value) { s.ShaderOptionDict.Add(name); s.ShaderOptions.Add(value); }
        Option("o_albedo_color", "(1)"); Option("o_normal_color", "0"); Option("o_enable_normalmap", "true");
        Option("o_texture0_texcoord", "3"); Option("o_texcoord3_mapping", "2"); Option("o_texcoord3_srt", "0");
        Option("o_material_behave", "100"); Option("o_specular_hair", "500");
        s.AttribAssignDict = new(); s.AttribAssignDict.Add("_u2"); s.AttribAssigns = ["_u3"];
        m.ShaderParams.Add(new() { Name = "p_texture_array_index0", Type = Res.ShaderParamType.Float, DataOffset = 0 });
        m.ShaderParams.Add(new() { Name = "p_tex_srt0", Type = Res.ShaderParamType.TexSrt, DataOffset = 4 });
        BinaryPrimitives.WriteSingleLittleEndian(m.ShaderParamData, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(m.ShaderParamData.AsSpan(4), (uint)Res.TexSrtMode.Mode3dsMax);
        float[] values = [2, 3, 0, .25f, .75f];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(m.ShaderParamData.AsSpan(8 + i * 4), values[i]);
        var textures = new Dictionary<string, VisualModelTexture> { [TexToGoTexture.Key("color", 1)] = red, [TexToGoTexture.Key("normal", 1)] = green };
        var mesh = new VisualModelMesh { Vertices = [0, 0, 0, 0, 0, 1, 0, 0] };
        for (int i = 0; i < 4; i++) mesh.UvSets.Add(new() { Name = $"_u{i}", Coordinates = [(float)i, i + .5f] });
        ToolboxTotkMaterial.Apply(m, mesh, textures);
        var bindings = mesh.Material.Totk!;
        Require(bindings != null && ReferenceEquals(bindings.Textures[1], red) && ReferenceEquals(bindings.Textures[0], green), "Shader sampler order confused with physical sampler order");
        Require(mesh.Material.UseNormal && mesh.Material.NormalUvSet == "_u3", "Normal texture or attribute remapping missing");
        Require(bindings!.Integers["totkTextureMapping0"] == 2 && bindings.Integers["totkTextureMapping1"] == 0, "Generated texcoord routing/default incorrect");
        Require(bindings.Integers["totkTextureSrgb0"] == 1 && bindings.Integers["totkTextureSrgb1"] == 1, "Toolbox RGB preview decode was not preserved");
        Require(Vector4.Distance(bindings.Vectors["totkTextureSrt0"], new(2, 3, -.25f, -.25f)) < 1e-6f, "3dsMax SRT center correction incorrect");
        bindings.VertexColors = [new(.1f, .2f, .3f, .4f)];
        var vertices = ModelVertices.Build(mesh);
        Require(vertices.Length == 24 && vertices[10] == 3 && vertices[20] == .1f && vertices[23] == .4f, "Four-UV/color GPU layout incorrect");
        Require(ReferenceEquals(mesh.Material.Clone().Totk, bindings), "Material cloning lost TotK bindings");
        textures[TexToGoTexture.Key("color", 1)] = channels;
        textures[TexToGoTexture.Key("normal", 1)] = grayscale;
        ToolboxTotkMaterial.Apply(m, mesh, textures);
        Require(!channels.IsSrgb && mesh.Material.Totk!.Integers["totkTextureSrgb1"] == 1 && mesh.Material.Totk.Integers["totkTextureSrgb0"] == 0,
            "RGB UNORM preview decode or linear single-channel masks regressed");
        Require(mesh.Material.Totk!.Integers["totkTextureLuminance0"] == 1 && mesh.Material.Totk.Integers["totkTextureLuminance1"] == 0,
            "Per-slot luminance routing incorrect");
        mesh.Tangents = [new Vector4(0, 1, 0, -1)]; mesh.TangentUvSet = mesh.Material.NormalUvSet;
        mesh.Material.Totk!.Vectors["totkTextureSrt0"] = new Vector4(1, 1, 0, 0);
        mesh.Material.Totk.Scalars["totkTextureRotation0"] = 0;
        var authored = ModelVertices.Build(mesh);
        Require(authored[16] == 0 && authored[17] == 1 && authored[19] == -1, "Authored tangent or handedness lost");
        mesh.TangentUvSet = "changed";
        var regenerated = ModelVertices.Build(mesh);
        Require(regenerated[16] == 1 && regenerated[19] == 1, "Changed UV mapping reused incompatible tangents");
        Console.WriteLine("PASS: TexToGo BC1/ASTC, surface ordering, channel selectors, sRGB, truncation guards; TotK sampler order, UV mapping/SRT, colors and clone preservation.");
    }

    private static byte[] Fixture(ushort format, byte[] channels)
    {
        // Intentionally shuffled surfaces and mips: the reader must use the table.
        (ushort Array, byte Mip)[] entries = [(1, 0), (0, 1), (0, 0), (1, 1)];
        var payloads = new List<byte[]>(); using var compressor = new Compressor();
        foreach (var e in entries)
        {
            var raw = new byte[512];
            if (format == 0x602)
            {
                raw[0] = 128; raw[1] = 128;
            }
            else if (format == 0x109)
            {
                raw[0] = 0xfc; raw[1] = 0xfd; Array.Fill(raw, (byte)255, 2, 6);
                BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(8), ushort.MaxValue);
                BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(14), ushort.MaxValue);
            }
            else
            {
                ushort rgb565 = e.Array == 0 ? (ushort)0xf800 : (ushort)0x07e0;
                BinaryPrimitives.WriteUInt16LittleEndian(raw, rgb565); BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(2), rgb565);
            }
            payloads.Add(compressor.Wrap(raw).ToArray());
        }
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(new byte[80]); stream.Position = 0; writer.Write((ushort)80); writer.Write((ushort)0x11); writer.Write("6PK0"u8);
        writer.Write((ushort)4); writer.Write((ushort)4); writer.Write((ushort)2); writer.Write((byte)2);
        stream.Position = 24; writer.Write(channels); stream.Position = 60; writer.Write(format); stream.Position = 80;
        foreach (var e in entries) { writer.Write(e.Array); writer.Write(e.Mip); writer.Write((byte)1); }
        foreach (var payload in payloads) { writer.Write(payload.Length); writer.Write(6); }
        foreach (var payload in payloads) writer.Write(payload);
        return stream.ToArray();
    }

    private static void TallTexture()
    {
        // Nonuniform BC1 blocks expose rearranged tiles; 4x4 solid fixtures cannot.
        const int width = 64, height = 1024, blockHeight = 16;
        var linear = new byte[width / 4 * height / 4 * 8];
        var tiled = new byte[linear.Length];
        for (int y = 0; y < height / 4; y++)
        for (int x = 0; x < width / 4; x++)
        {
            ushort color = (ushort)((x << 11) | ((y % 64) << 5) | (y / 64));
            int i = (y * (width / 4) + x) * 8;
            BinaryPrimitives.WriteUInt16LittleEndian(linear.AsSpan(i), color);
            BinaryPrimitives.WriteUInt16LittleEndian(linear.AsSpan(i + 2), color);
            int xb = x * 8;
            int gob = (y / 128 * 2 + xb / 64) * 512 * blockHeight + y % 128 / 8 * 512;
            int within = (xb & 32) * 8 + (y & 6) * 32 + (xb & 16) * 2 + (y & 1) * 16 + (xb & 15);
            linear.AsSpan(i, 8).CopyTo(tiled.AsSpan(gob + within));
        }
        using var compressor = new Compressor();
        var compressed = compressor.Wrap(tiled).ToArray();
        var file = new byte[92 + compressed.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(file, 80); BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 0x11);
        "6PK0"u8.CopyTo(file.AsSpan(4));
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(8), width); BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(10), height);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(12), 1); file[14] = 1;
        new byte[] { 0, 1, 2, 3 }.CopyTo(file, 24); BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(60), 0x302);
        file[83] = 1; BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(84), compressed.Length); file[88] = 6;
        compressed.CopyTo(file, 92);
        var expected = new BCnEncoder.Decoder.BcDecoder().DecodeRaw(linear, width, height, BCnEncoder.Shared.CompressionFormat.Bc1WithAlpha);
        var actual = TexToGoTexture.Decode(file, "tall-pattern");
        Require(actual.Pixels.AsSpan().SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(expected.AsSpan())), "Tall TXTG texture tiles were rearranged");
    }
}
