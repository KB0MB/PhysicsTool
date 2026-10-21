namespace PhysicsTool.Desktop.Rendering;

internal static class ModelWireframe
{
    internal static uint[] Edges(uint[] triangles)
    {
        var edges = new List<uint>();
        var seen = new HashSet<ulong>();
        for (var index = 0; index + 2 < triangles.Length; index += 3)
        {
            AddEdge(triangles[index], triangles[index + 1]);
            AddEdge(triangles[index + 1], triangles[index + 2]);
            AddEdge(triangles[index + 2], triangles[index]);
        }
        return edges.ToArray();

        void AddEdge(uint a, uint b)
        {
            if (a == b) return;
            var key = ((ulong)Math.Min(a, b) << 32) | Math.Max(a, b);
            if (!seen.Add(key)) return;
            edges.Add(a);
            edges.Add(b);
        }
    }
}
