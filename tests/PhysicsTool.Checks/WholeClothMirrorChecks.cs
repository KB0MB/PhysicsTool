using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

internal static class WholeClothMirrorChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output)
    {
        Require(WholeClothMirror.SwapSide("Cloth_L:Upper_R.ROOT") == "Cloth_R:Upper_L.ROOT", "Side-name token matching failed");
        foreach (var totk in new[] { false, true })
        {
            var doc = HkclService.CreateBlankCloth("Mirror_L", totk);
            var p = doc.AddParticle(0); var rows = doc.GetParticleRows(0); var row = rows.Single(r => r.Index == p);
            row.X = .3f; row.Y = .7f; row.Z = -.2f; doc.UpdateParticleRows(0, [row]);
            Check(doc, 0, Path.Combine(output, "whole-mirror-blank." + (totk ? "bphcl" : "hkcl")));
        }
        var accepted = 0; var rejected = 0;
        foreach (var path in Directory.EnumerateFiles(output, "fixture-*.hkcl").Concat(Directory.EnumerateFiles(output, "fixture-*.bphcl")))
        {
            var doc = new HkclService(); doc.Load(path);
            for (var i = 0; i < doc.GetClothSummaries().Count; i++)
            {
                var state = doc.CaptureState();
                try { doc.ValidateWholeClothMirror(i); }
                catch (InvalidOperationException ex)
                {
                    rejected++; Require(state == doc.CaptureState(), "Mirror preflight changed a rejected file");
                    Console.WriteLine($"MIRROR guarded {Path.GetFileName(path)} [{i}]: {ex.Message}"); continue;
                }
                Check(doc, i, Path.Combine(output, $"whole-mirror-native-{accepted++}" + Path.GetExtension(path)));
                doc.RestoreState(state);
            }
        }
        Console.WriteLine($"PASS: whole-cloth reflection, double-mirror geometry, native save/reload and rollback; {accepted} native cloths accepted, {rejected} safely guarded.");
        var nativePath = Path.Combine(output, "fixture-Armor_1106_RaulSkin_Upper.bphcl");
        if (File.Exists(nativePath))
        {
            var native = new HkclService(); native.Load(nativePath);
            var copy = native.DuplicateCloth(0);
            native.ValidateWholeClothMirror(copy);
            Check(native, copy, Path.Combine(output, "whole-mirror-isolated.bphcl"));
            Console.WriteLine("PASS: isolated native BPHCL deformer/skin mirror and double-reflection restoration.");
        }
    }
    private static void Check(HkclService doc, int cloth, string output)
    {
        var state = doc.CaptureState(); var particles = doc.GetParticleRows(cloth); var triangles = doc.GetParticlePreview(cloth).Triangles;
        var skeleton = doc.GetBoneRows(cloth); var skins = doc.GetSkinOperatorData(cloth);
        var bends = doc.GetBendStiffnessConstraintSets(cloth).SelectMany(s => s.Links).ToArray();
        var deformers = doc.GetSimpleMeshBoneDeformData(cloth);
        WholeClothMirror.Apply(doc, cloth);
        var after = doc.GetParticleRows(cloth);
        foreach (var p in particles)
        {
            var q = after.Single(r => r.Index == p.Index);
            Require(Math.Abs(q.X + p.X) < 1e-5 && q.Y == p.Y && q.Z == p.Z && q.Mass == p.Mass && q.InverseMass == p.InverseMass && q.Fixed == p.Fixed, "Whole mirror changed particle properties or wrong axis");
        }
        var newTriangles = doc.GetParticlePreview(cloth).Triangles;
        Require(doc.GetBendStiffnessConstraintSets(cloth).SelectMany(s => s.Links).Zip(bends).All(p => p.First.RestCurvature == -p.Second.RestCurvature), "Signed bend curvature was not reflected");
        if (!doc.IsBphcl)
        {
            var mirroredSkins = doc.GetSkinOperatorData(cloth);
            for (var s = 0; s < skins.Count; s++)
            {
                var a = skins[s].LocalPositionBlocks.Concat(skins[s].LocalUnpackedPositionBlocks).SelectMany(b => b.Positions).ToArray();
                var b = mirroredSkins[s].LocalPositionBlocks.Concat(mirroredSkins[s].LocalUnpackedPositionBlocks).SelectMany(b => b.Positions).ToArray();
                Require(a.Length == b.Length && a.Zip(b).All(p => Vector4.Distance(new(-p.First.X, p.First.Y, p.First.Z, p.First.W), p.Second) < .002f), "Skin reference geometry was not mirrored");
            }
        }
        for (var i = 0; i < triangles.Count; i++) Require(newTriangles[i].ParticleA == triangles[i].ParticleA && newTriangles[i].ParticleB == triangles[i].ParticleC && newTriangles[i].ParticleC == triangles[i].ParticleB, "Whole mirror did not reverse winding");
        doc.SaveHkcl(output, HkclPlatform.WiiU); var reload = new HkclService(); reload.Load(output);
        Require(reload.GetParticleRows(cloth).Count == particles.Count, "Mirrored file did not reload");
        WholeClothMirror.Apply(doc, cloth);
        var twice = doc.GetParticleRows(cloth);
        Require(twice.Zip(particles).All(p => Math.Abs(p.First.X - p.Second.X) < 1e-5 && p.First.Y == p.Second.Y && p.First.Z == p.Second.Z), "Double mirror did not restore particles");
        var bonesAfter = doc.GetBoneRows(cloth);
        var twiceDeformers = doc.GetSimpleMeshBoneDeformData(cloth);
        for (var i = 0; i < deformers.Count; i++)
            foreach (var pair in deformers[i].LocalBoneTransforms.Zip(twiceDeformers[i].LocalBoneTransforms))
                foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                    Require(Vector3.Distance(Vector3.Transform(point, pair.First), Vector3.Transform(point, pair.Second)) < .01f, "Double mirror lost authored deformer offsets");
        foreach (var b in skeleton)
        {
            var a = bonesAfter.Single(r => r.Index == b.Index);
            Require(a.Name == b.Name && a.ParentIndex == b.ParentIndex && Vector3.Distance(new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z)) < 1e-4f &&
                Math.Abs(Quaternion.Dot(new(a.RotationX, a.RotationY, a.RotationZ, a.RotationW), new(b.RotationX, b.RotationY, b.RotationZ, b.RotationW))) > .9999f, "Double mirror did not restore bone poses");
        }
        doc.RestoreState(state); Require(doc.CaptureState() == state, "Whole mirror snapshot restore failed");
    }
}
