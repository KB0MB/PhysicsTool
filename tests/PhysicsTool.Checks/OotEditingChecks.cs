using HKCLTool;
using PhysicsTool.Core;
using BymlLibrary;

internal static class OotEditingChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run(string root, string output)
    {
        Directory.CreateDirectory(output);
        int particles = 0, radii = 0, constraints = 0;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Phive/Cloth"), "*.bphcl"))
        {
            var doc = NativeBphclDocument.Open(path);
            var source = doc.Bytes.ToArray();
            foreach (var cloth in doc.Cloths)
            {
                var sim = cloth.SimCloths.FirstOrDefault();
                var particle = sim?.Particles.FirstOrDefault(p => !p.Fixed);
                if (particle == null) continue;
                var fields = OotClothProperties.Get(doc, cloth.Index, particle.Index, false);
                Require(OotClothProperties.Apply(doc, cloth.Index, particle.Index, false, fields.ToDictionary(f => f.Offset, f => f.Value)).Bytes.SequenceEqual(source), "No-op changed native bytes");
                var mass = fields.Single(f => f.Label == "Mass");
                var radius = fields.Single(f => f.Group == "Particle" && f.Label == "Radius");
                var updated = OotClothProperties.Apply(doc, cloth.Index, particle.Index, false, new Dictionary<uint, float> { [mass.Offset] = mass.Value * 1.1f, [radius.Offset] = radius.Value + .001f });
                var after = updated.Cloths[cloth.Index].SimCloths[0].Particles[particle.Index];
                Require(after.Mass == mass.Value * 1.1f && after.InverseMass == 1 / after.Mass && after.Radius == radius.Value + .001f && after.Position == particle.Position, "Particle edit failed");
                Require(updated.Bytes.Length == source.Length, "Particle edit reallocated native data");
                var constraint = fields.FirstOrDefault(f => f.Group != "Particle" && f.Value >= 0);
                if (constraint != null)
                {
                    var changed = OotClothProperties.Apply(doc, cloth.Index, particle.Index, false, new Dictionary<uint, float> { [constraint.Offset] = constraint.Value + .01f });
                    Require(OotClothProperties.Get(changed, cloth.Index, particle.Index, false).Single(f => f.Offset == constraint.Offset).Value == constraint.Value + .01f, "Constraint edit failed");
                    constraints++;
                }
                particles++;
            }
            foreach (var collider in doc.Colliders)
            {
                var fields = OotClothProperties.Get(doc, 0, collider.Index, true);
                if (fields.Count == 0) continue;
                var field = fields[0];
                var updated = OotClothProperties.Apply(doc, 0, collider.Index, true, new Dictionary<uint, float> { [field.Offset] = field.Value + .001f });
                Require(updated.Colliders[collider.Index].Shape.Radius == field.Value + .001f, "Collider radius failed");
                Require(updated.Colliders[collider.Index].Translation == collider.Translation, "Precise collider translation changed");
                radii++;
            }
            Require(source.SequenceEqual(doc.Bytes) && source.SequenceEqual(File.ReadAllBytes(path)), "Source modified");
        }
        var session = new EditorSession();
        var original = File.ReadAllBytes(Path.Combine(root, "Phive/Cloth/Player_Adult.Nin_Ounce_NVN2.bphcl"));
        session.ImportOotCloth(original); Require(session.Path == null, "Actor import is not detached from dump");
        var p = session.Document.GetParticleRows(0).First(p => !p.Fixed);
        var massField = session.Document.GetOotProperties(0, p.Index, false).Single(f => f.Label == "Mass");
        var snapshot = session.Document.CaptureState();
        session.EditOotProperties(p.Index, false, new Dictionary<uint, float> { [massField.Offset] = massField.Value * 1.2f });
        var edited = session.Document.CaptureState(); Require(edited != snapshot && session.CanUndo, "Undo snapshot absent");
        session.Undo(); Require(session.Document.CaptureState() == snapshot, "OoT undo failed");
        session.Redo(); Require(session.Document.CaptureState() == edited, "OoT redo failed");
        try { session.EditOotProperties(p.Index, false, new Dictionary<uint, float> { [massField.Offset] = -1 }); throw new Exception("Invalid mass accepted"); } catch (ArgumentOutOfRangeException) { }
        Require(session.Document.CaptureState() == edited, "Failed edit changed state");
        try { session.EditOotProperties(p.Index, false, new Dictionary<uint, float> { [0] = 1 }); throw new Exception("Unknown field accepted"); } catch (InvalidOperationException) { }
        var save = Path.Combine(output, "oot-edited.bphcl"); session.Save(save);
        Require(File.ReadAllBytes(save).SequenceEqual(session.Document.CaptureBphclNativeDocument()!.Bytes), "Save changed native bytes");
        var reopened = new EditorSession(); reopened.Load(save); Require(reopened.Document.CaptureState() == edited, "Reopen changed edited data");
        var entry = new BotwActorCatalogEntry("Player_Adult", Path.Combine(root, "Pack/Actor/Player_Adult.pack.zs"), null, root, true, true, true) { IsOot = true };
        var helpers = OotActorResources.Load(entry).Assets.Where(a => !a.IsCloth);
        foreach (var asset in helpers)
        {
            var helper = new OotHelperDocument(asset.Bytes);
            var group = helper.Groups.First(g => g.Kind == "DrivenParams");
            var presented = helper.Fields(group).ToDictionary(f => f.Path, f => f.Type == BymlNodeType.Float
                ? decimal.Parse(f.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture) : f.Value);
            helper.Apply(group, presented);
            Require(helper.Bytes.SequenceEqual(asset.Bytes), "UI formatting changed unchanged helper values or quaternion");
            var field = helper.Fields(group).First(f => f.Type == BymlNodeType.Float && f.Path.StartsWith("BaseTranslate/"));
            helper.Apply(group, new Dictionary<string, string> { [field.Path] = "0.123" });
            Require(helper.Fields(group).Single(f => f.Path == field.Path).Value == "0.123", "Helper edit failed");
            helper.Undo(); Require(helper.Bytes.SequenceEqual(asset.Bytes), "Helper undo did not restore exact source"); helper.Redo();
            helper.Save(Path.Combine(output, "helper.bgyml"));
            Require(new OotHelperDocument(File.ReadAllBytes(Path.Combine(output, "helper.bgyml"))).Fields(group).Single(f => f.Path == field.Path).Value == "0.123", "Helper save/reopen failed");
            helper.Apply(group, new Dictionary<string, string> { ["$RotationX"] = "0.25" });
            Require(Math.Abs(float.Parse(helper.Fields(group).Single(f => f.Path == "$RotationX").Value, System.Globalization.CultureInfo.InvariantCulture) - .25f) < .00001, "Helper Euler/quaternion edit failed");
        }
        Console.WriteLine($"PASS OoT edits: {particles} cloth particle sets, {constraints} constraint records, {radii} collider radii; undo/redo, invalid input, exact-byte saves, helper parameters and source preservation.");
    }
}
