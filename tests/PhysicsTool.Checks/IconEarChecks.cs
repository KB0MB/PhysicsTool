using BymlLibrary;
using HKCLTool;
using System.Numerics;
using PhysicsTool.Core;

internal static class IconEarChecks
{
    internal static void Inspect(string path, string root)
    {
        var archive = BotwSarcArchive.Parse(new TotkResourceCompression().Decode(File.ReadAllBytes(path), [root]));
        foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".bgyml")))
        {
            var yaml = Byml.FromBinary(archive.Read(entry).ToArray()).ToYaml();
            if (yaml.Contains("Ear", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(entry.Name + "\n" + yaml);
        }
        var resources = new BotwActorResources(path, [root]);
        Console.WriteLine("READ EAR " + resources.ArmorEarRotationDegrees);
        if (resources.ArmorEarRotationDegrees != new Vector3(0,50,0)) throw new Exception("Hylian Hood ear offset was not resolved.");
        var warnings = new List<string>();
        if (BotwActorResources.ReadTotkEarRotation(new Dictionary<string,Byml>(), warnings) != Vector3.Zero || warnings.Count != 0)
            throw new Exception("Absent ear offset changed the pose.");
        var invalid = new Dictionary<string,Byml> { ["HeadEarBoneOffset"] = new(new Dictionary<string,Byml> { ["Y"] = new(float.NaN) }) };
        if (BotwActorResources.ReadTotkEarRotation(invalid, warnings) != Vector3.Zero || warnings.Count != 1)
            throw new Exception("Non-finite ear offset accepted.");
        foreach (var character in new[] { IconCharacterProfile.Link, IconCharacterProfile.LoadAll().Single(c => c.Id == "Riju") })
        {
            var props = IconArmorProps.Load(resources, default, character: character)!;
            var before = new IconArmorPose([props], null, true).Apply(new Dictionary<string,Vector3>(), restPose: true);
            var after = new IconArmorPose([props], null, true, resources.ArmorEarRotationDegrees).Apply(new Dictionary<string,Vector3>(), restPose: true);
            foreach (var name in new[] { "Ear_L", "Ear_R" })
            {
                var bone = before.Bones.Single(b => b.Name == name);
                var parent = before.Bones.Single(b => b.Index == bone.ParentIndex);
                Matrix4x4.Invert(parent.WorldMatrix, out var inverse);
                var expected = Matrix4x4.CreateRotationY(50 * MathF.PI / 180) * bone.WorldMatrix * inverse * parent.WorldMatrix;
                var actual = after.Bones.Single(b => b.Name == name).WorldMatrix;
                foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                    if (Vector3.Distance(Vector3.Transform(point, actual), Vector3.Transform(point, expected)) > .00001f) throw new Exception("Wrong ear transform: " + character.Name + " " + name);
            }
            var oldFace = before.Meshes.Single(m => m.Name.EndsWith("Icon_Head_Face"));
            var newFace = after.Meshes.Single(m => m.Name == oldFace.Name);
            if (oldFace.Vertices.SequenceEqual(newFace.Vertices)) throw new Exception("Ear bones did not deform " + character.Name);
            Console.WriteLine("PASS ear bone transforms and face skinning: " + character.Name);
        }
    }
}
