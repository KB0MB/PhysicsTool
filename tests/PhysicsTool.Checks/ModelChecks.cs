using Assimp;
using HKCLTool;
using PhysicsTool.Desktop.Rendering;
using SkiaSharp;
using System.Numerics;

internal static class ModelChecks
{
    public static string Run(string output)
    {
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        using (var bitmap = new SKBitmap(2, 2))
        {
            bitmap.SetPixel(0, 0, SKColors.Red); bitmap.SetPixel(1, 0, SKColors.Green);
            bitmap.SetPixel(0, 1, SKColors.Blue); bitmap.SetPixel(1, 1, SKColors.White);
            using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(output, "quadrants.png"), data.ToArray());
        }
        File.WriteAllText(Path.Combine(output, "model.mtl"), "newmtl Probe\nKd 1 1 1\nmap_Kd quadrants.png\nnewmtl Missing\nKd 1 1 1\nmap_Kd missing.png\n");
        var path = Path.Combine(output, "model.obj");
        File.WriteAllText(path, """
            mtllib model.mtl
            o TexturedQuad
            v -1 -1 0
            v 1 -1 0
            v 1 1 0
            v -1 1 0
            vt 0 0
            vt 1 0
            vt 1 1
            vt 0 1
            usemtl Probe
            f 1/1 2/2 3/3
            f 1/1 3/3 4/4
            o MissingTexture
            v 1.2 -0.5 -0.5
            v 1.8 -0.5 -0.5
            v 1.2 0.5 -0.5
            usemtl Missing
            f 5/1 6/2 7/3
            """);
        var model = PortableModelImporter.Load(path);
        Require(model.Meshes.Count == 2 && model.TriangleCount == 3, "OBJ mesh topology");
        Require(model.BoundsMinimum.X == -1 && Math.Abs(model.BoundsMaximum.X - 1.8f) < .001f, "Model was mirrored or rescaled");
        Require(model.ExternalTextureCount == 1 && model.DefaultTextureMeshCount == 1 && model.Warnings.Count == 1, "Texture fallback and warnings");
        var texture = model.Meshes[0].Texture!;
        Require(texture.Pixels[2] == 255 && texture.Pixels[0] == 0, "Texture rows must start at bottom-left");
        Require(texture.Pixels[8] == 255 && texture.Pixels[10] == 0, "Texture top row orientation");
        foreach (var mesh in model.Meshes) Require(ModelVertices.Build(mesh).All(float.IsFinite), "Non-finite tangents");
        Require(ModelTextures.Default.Width > 1, "Missing packaged Toolbox texture");

        using var exporter = new AssimpContext();
        var scene = new Scene { RootNode = new Node("Root") };
        scene.Materials.Add(new Material { Name = "Plain" });
        var triangle = new Mesh("Asymmetric", PrimitiveType.Triangle);
        triangle.Vertices.AddRange([new(0, 0, 0), new(1, 0, 0), new(0, 2, 0)]);
        triangle.Faces.Add(new Face([0, 1, 2])); scene.Meshes.Add(triangle);
        var child = new Node("Translated") { Transform = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(3, 0, 0)) };
        child.MeshIndices.Add(0); scene.RootNode.Children.Add(child);
        foreach (var (extension, id) in new[] { ("dae", "collada"), ("glb", "glb2"), ("fbx", "fbxa") })
        {
            var file = Path.Combine(output, "translated." + extension);
            Require(exporter.ExportFile(scene, file, id), "Fixture export " + id);
            var imported = PortableModelImporter.Load(file);
            Require(imported.TriangleCount == 1, "Import topology " + extension);
            var size = imported.BoundsMaximum - imported.BoundsMinimum;
            Require(size.X > 0 && Math.Abs(imported.BoundsMinimum.X / size.X - 3) < .01f, "Hierarchy or handedness " + extension);
            Require(Math.Abs(size.Y / size.X - 2) < .01f, "Axis conversion " + extension);
            Require(Math.Abs(size.X - (extension == "fbx" ? .01f : 1)) < .001f, "Model units " + extension);
        }
        scene.Textures.Add(new EmbeddedTexture("png", File.ReadAllBytes(Path.Combine(output, "quadrants.png")), "quadrants.png"));
        scene.Materials[0].TextureDiffuse = new TextureSlot("*0", TextureType.Diffuse, 0, TextureMapping.FromUV, 0, 1, TextureOperation.Multiply, TextureWrapMode.Wrap, TextureWrapMode.Wrap, 0);
        triangle.TextureCoordinateChannels[0].AddRange([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)]);
        triangle.UVComponentCount[0] = 2;
        var embeddedPath = Path.Combine(output, "embedded.glb");
        Require(exporter.ExportFile(scene, embeddedPath, "glb2"), "Embedded GLB export");
        var embeddedModel = PortableModelImporter.Load(embeddedPath);
        Require(embeddedModel.EmbeddedTextureCount == 1 && embeddedModel.Meshes[0].Texture?.Width == 2, "Embedded texture decode");
        Console.WriteLine("PASS: OBJ/DAE/FBX/GLB import, hierarchy, handedness, texture origin/fallback, and tangents.");
        return path;
    }
}
