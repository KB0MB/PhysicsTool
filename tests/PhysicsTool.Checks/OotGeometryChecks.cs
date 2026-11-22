using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

internal static class OotGeometryChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Run(string root, string output)
    {
        Directory.CreateDirectory(output);
        int edited = 0;
        var blocked = new Dictionary<string, int>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Phive/Cloth"), "*.bphcl"))
        {
            var bytes = File.ReadAllBytes(path);
            var session = new EditorSession(); session.ImportOotCloth(bytes);
            var count = session.Document.CaptureBphclNativeDocument()!.Cloths.Count;
            for (int cloth = 0; cloth < count; cloth++)
            {
                session.ClothIndex = cloth;
                try { session.Document.ValidateOotGeometry(cloth); }
                catch (InvalidOperationException ex) { blocked[ex.Message] = blocked.GetValueOrDefault(ex.Message) + 1; continue; }
                var p = session.Document.GetParticleRows(cloth).First(p => !p.Fixed);
                var position = new Vector3(p.X, p.Y, p.Z);
                var before = session.Document.CaptureState();
                session.EditOotGeometry(new Dictionary<int, Vector3> { [p.Index] = position });
                Require(session.Document.CaptureState() == before, "Geometry no-op changed native data");
                try { session.EditOotGeometry(new Dictionary<int, Vector3> { [p.Index] = position + new Vector3(.0001f, 0, 0) }); }
                catch (InvalidOperationException ex) { Require(session.Document.CaptureState() == before, "Failed geometry edit changed document"); blocked[ex.Message] = blocked.GetValueOrDefault(ex.Message) + 1; continue; }
                var after = session.Document.GetParticleRows(cloth).Single(r => r.Index == p.Index);
                Require(Math.Abs(after.X - position.X - .0001f) < .00001f, "Particle position not updated");
                Require(after.Mass == p.Mass && after.InverseMass == p.InverseMass && float.IsFinite(after.Radius) && after.Radius >= 0, "Geometry changed mass or produced an invalid radius");
                var changed = session.Document.CaptureState();
                session.Undo(); Require(session.Document.CaptureState() == before, "Geometry undo not exact");
                session.Redo(); Require(session.Document.CaptureState() == changed, "Geometry redo not exact");
                if (edited++ == 0)
                {
                    var save = Path.Combine(output, "geometry.bphcl"); session.Save(save);
                    var reopened = new EditorSession(); reopened.Load(save);
                    Require(reopened.Document.CaptureState() == changed, "Geometry save/reopen changed data");
                }
                session.Undo();
            }
            Require(File.ReadAllBytes(path).SequenceEqual(bytes), "Dump modified");
        }
        Console.WriteLine($"Edited {edited} native cloths; guarded {blocked.Values.Sum()}.");
        foreach (var (reason, count) in blocked) Console.WriteLine($"  {count}: {reason}");
        Require(edited > 0, "No native cloth geometry editable");
        var entry = new BotwActorCatalogEntry("Player_Adult", Path.Combine(root, "Pack/Actor/Player_Adult.pack.zs"), null, root, true, true, true) { IsOot = true };
        var asset = OotActorResources.Load(entry).Assets.First(a => !a.IsCloth);
        var helper = new OotHelperDocument(asset.Bytes);
        var template = helper.Connections().First();
        var group = helper.Groups.First(g => g.Kind == "DrivenParams" && !helper.Connections(g.Index).Any(c => c.Channel == "TranslateY"));
        helper.AddConnection(group.Index, "TranslateY", template.Driver, template);
        var added = helper.Connections(group.Index).Single(c => c.Channel == "TranslateY");
        Require(added.Attribute == template.Attribute && added.Driver == template.Driver, "New connection lost input binding");
        var connected = helper.Bytes;
        helper.SetCurveKeys(added, [new(-1, -2, 2, 2), new(1, 2, 2, 2)]);
        helper.InsertCurveKey(added, 0);
        var keys = helper.CurveKeys(added);
        Require(keys.Count == 3 && keys[1] == new OotCurveKey(0, 0, 2, 2), "Inserted curve key changed linear shape");
        var valid = helper.Bytes;
        try { helper.InsertCurveKey(added, 0); throw new Exception("Duplicate key accepted"); } catch (ArgumentException) { }
        try { helper.SetCurveKeys(added, [new(1, 0, 0, 0), new(-1, 0, 0, 0)]); throw new Exception("Unsorted keys accepted"); } catch (ArgumentException) { }
        Require(helper.Bytes.SequenceEqual(valid), "Invalid key edit changed helper");
        helper.SetCurveKeys(added, keys.Where(k => k.X != 0).ToArray());
        helper.RemoveConnection(added); Require(!helper.Connections(group.Index).Any(c => c.Channel == "TranslateY"), "Connection not removed");
        helper.Undo(); Require(helper.Connections(group.Index).Any(c => c.Channel == "TranslateY"), "Connection undo failed");
        var helperSave = Path.Combine(output, "connections.bgyml"); helper.Save(helperSave);
        Require(new OotHelperDocument(File.ReadAllBytes(helperSave)).CurveKeys(added).Count == 2, "Keys not saved");
        var host = new OotHelperDocument(asset.Bytes); host.ApplyConnections(connected, group.Index); host.Undo();
        Require(host.Bytes.SequenceEqual(asset.Bytes), "Dialog changes not one undo step");
        Console.WriteLine("PASS geometry, native saves, undo/redo, helper connections, curve insertion/removal and atomic validation.");
    }
}
