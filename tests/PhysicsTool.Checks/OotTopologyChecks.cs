using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

internal static class OotTopologyChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run(string root, string output)
    {
        Directory.CreateDirectory(output);
        int added = 0, bones = 0;
        var blocked = new Dictionary<string, int>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Phive/Cloth"), "*.bphcl"))
        {
            var session = new EditorSession(); session.ImportOotCloth(File.ReadAllBytes(path));
            int count = session.Document.CaptureBphclNativeDocument()!.Cloths.Count;
            for (int c = 0; c < count; c++)
            {
                session.ClothIndex = c;
                var original = session.Document.CaptureState();
                try
                {
                    var particles = session.Document.GetParticleRows(c);
                    int source = particles.First(p => !p.Fixed).Index;
                    int index = session.AddOotParticle(source);
                    Require(index == particles.Count, "New particle index");
                    var before = session.Document.GetParticleBoneAttachments(c).Single(p => p.ParticleIndex == source);
                    var p = particles[source];
                    session.EditOotGeometry(new Dictionary<int, Vector3> { [index] = new(p.X + .01f, p.Y + .012f, p.Z + .007f) });
                    var after = session.Document.GetParticleBoneAttachments(c).Single(p => p.ParticleIndex == source);
                    Require(before.BoneWeights.Count == after.BoneWeights.Count && before.BoneWeights.Zip(after.BoneWeights).All(w =>
                        w.First.BoneIndex == w.Second.BoneIndex && Math.Abs(w.First.Weight - w.Second.Weight) < 1e-6f &&
                        Vector3.Distance(w.First.ReferencePosition!.Value, w.Second.ReferencePosition!.Value) < .0001f), "New particle changed its source skin: " + string.Join(", ", before.BoneWeights.Zip(after.BoneWeights).Select(w => Vector3.Distance(w.First.ReferencePosition!.Value, w.Second.ReferencePosition!.Value))));
                    var triangles = session.Document.CaptureBphclNativeDocument()!.Cloths[c].SimCloths[0].TriangleIndices;
                    var edge = triangles.Chunk(3).First(t => t.Contains((ushort)source));
                    int other = edge.First(i => i != source);
                    session.AddOotTriangle([source, other, index]);
                    Require(session.Document.CaptureBphclNativeDocument()!.Cloths[c].SimCloths[0].TriangleIndices.Count == triangles.Count + 3, "Triangle not added");
                    var changed = session.Document.CaptureState();
                    session.Undo(); session.Redo(); Require(session.Document.CaptureState() == changed, "Topology redo differs");
                    if (added++ == 0) { var save = Path.Combine(output, "topology.bphcl"); session.Save(save); var reload = new EditorSession(); reload.Load(save); Require(reload.Document.CaptureState() == changed, "Topology save/reopen differs"); }
                }
                catch (InvalidOperationException ex) { blocked[ex.Message] = blocked.GetValueOrDefault(ex.Message) + 1; }
                catch (Exception ex) { throw new Exception($"{Path.GetFileName(path)} cloth {c}: {ex.Message}", ex); }
                session.Document.RestoreState(original);
                try
                {
                    var row = session.Document.GetBoneRows(c).First(b => b.ParentIndex == -1);
                    var particles = session.Document.GetParticleRows(c);
                    session.EditOotBonePositions(new Dictionary<int, Vector3> { [row.Index] = new(row.X + .001f, row.Y, row.Z) }, true);
                    var changed = session.Document.GetParticleRows(c);
                    Require(changed.Zip(particles).All(p => Math.Abs(p.First.Y - p.Second.Y) < .0001f), "Root move affected Y");
                    session.Undo(); Require(session.Document.CaptureState() == original, "Bone undo differs");
                    bones++;
                }
                catch (InvalidOperationException ex) { blocked[ex.Message] = blocked.GetValueOrDefault(ex.Message) + 1; }
                catch (Exception ex) { throw new Exception($"Bone: {Path.GetFileName(path)} cloth {c}: {ex.Message}", ex); }
                session.Document.RestoreState(original);
            }
        }
        Console.WriteLine($"PASS: {added} cloth topology edits; {bones} bone position edits.");
        foreach (var pair in blocked) Console.WriteLine($"Guarded {pair.Value}: {pair.Key}");
        Require(added > 0 && bones > 0, "No successful native edits");
    }
}
