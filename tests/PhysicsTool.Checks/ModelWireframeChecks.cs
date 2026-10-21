using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;

internal static class ModelWireframeChecks
{
    internal static void Run()
    {
        var edges = ModelWireframe.Edges([0, 1, 2, 2, 1, 3, 3, 3, 3]);
        var pairs = edges.Chunk(2).Select(pair => (Math.Min(pair[0], pair[1]), Math.Max(pair[0], pair[1]))).ToHashSet();
        if (edges.Length != 10 || !pairs.SetEquals(new[] { (0u, 1u), (1u, 2u), (0u, 2u), (1u, 3u), (2u, 3u) }))
            throw new Exception("Wireframe lost triangle edges or duplicated shared/degenerate edges.");
        if (ModelWireframe.Edges([]).Length != 0)
            throw new Exception("Empty mesh generated wireframe edges.");
        var viewport = new PhysicsViewport();
        if (viewport.DisplayMode != ModelDisplayMode.Textured)
            throw new Exception("Model display should default to Textured.");
        var notifications = 0;
        viewport.CameraChanged += () => notifications++;
        var camera = viewport.CameraMatrix;
        viewport.Wireframe = true;
        viewport.Wireframe = true;
        viewport.Wireframe = false;
        viewport.DisplayMode = ModelDisplayMode.EdgedFaces;
        viewport.DisplayMode = ModelDisplayMode.EdgedFaces;
        viewport.DisplayMode = ModelDisplayMode.Textured;
        if (notifications != 4 || viewport.CameraMatrix != camera || !viewport.ShowPhysics)
            throw new Exception("Wireframe toggle changed camera/physics state or failed to request rendering.");
        Console.WriteLine("PASS: complete wireframe edges, shared-edge deduplication, empty meshes and isolated viewport toggle.");
    }
}
