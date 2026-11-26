using HKCLTool;

internal static class AutoMaterialChecks
{
    internal static void Run(string root)
    {
        if (!ToolboxAutoMaterialAnimation.IsAutomatic("Weapon_Sword_023_Auto_fts") ||
            !ToolboxAutoMaterialAnimation.IsAutomatic("Weapon_Bow_023_auto") ||
            ToolboxAutoMaterialAnimation.IsAutomatic("Weapon_Bow_023_Automatic")) throw new Exception("Auto track name matching");
        foreach (var name in new[] { "Weapon_Sword_023", "Weapon_Bow_023" })
        {
            var file = Path.Combine(root, "Model", name + ".sbfres");
            var document = new ToolboxBfresDocument(File.ReadAllBytes(file));
            var warnings = new List<string>();
            var model = new ToolboxActorModel(document, name, new(), warnings: warnings);
            if (!model.AutoMaterials.HasTracks) throw new Exception("Missing auto tracks: " + name);
            if (warnings.Any(w => w.StartsWith("Auto material"))) throw new Exception(string.Join("; ", warnings));
            var materials = model.RestPose.Meshes.Select(m => m.Material.Botw).Where(m => m != null).ToArray();
            float[] Snapshot() => materials.SelectMany(m => m!.Parameters.OrderBy(p => p.Key).SelectMany(p => p.Value)).ToArray();
            model.AutoMaterials.Sample(0); var initial = Snapshot();
            model.AutoMaterials.Sample(1); var next = Snapshot();
            if (initial.SequenceEqual(next)) throw new Exception("Static auto tracks: " + name);
            model.AutoMaterials.Sample(0);
            if (!initial.SequenceEqual(Snapshot())) throw new Exception("Non-deterministic reset");
            if (name == "Weapon_Bow_023")
            {
                var material = model.RestPose.Meshes.First(m => m.Name.EndsWith("__Mt_Weapon_Bow_023")).Material.Botw!;
                if (Math.Abs(material.Parameters["const_value0"][0] - 10) > .001f) throw new Exception("Wrong bow baseline");
                model.AutoMaterials.Sample(52.5 / 30);
                if (Math.Abs(material.Parameters["const_value0"][0] - 20) > .001f) throw new Exception("Wrong bow cubic key");
                model.AutoMaterials.Sample(10);
                if (!initial.SequenceEqual(Snapshot())) throw new Exception("Bow loop boundary");
            }
            Console.WriteLine($"PASS {name}: auto selection, shader parameter and texture SRT evaluation, deterministic sampling ({(document.IsSwitch ? "Switch" : "Wii U")}).");
        }
        var pack = Path.Combine(root, "Actor", "Pack", "Weapon_Bow_023.sbactorpack");
        if (File.Exists(pack))
        {
            using var scene = BotwActorScene.Load(pack, [root]);
            if (!scene.HasAutomaticMaterials) throw new Exception("Actor dropped auto materials");
            var mesh = scene.CurrentModel.Meshes.First(m => m.Name.EndsWith("__Mt_Weapon_Bow_023"));
            var before = mesh.Material.Botw!.Parameters["const_value0"][0];
            var vertices = mesh.Vertices;
            scene.AdvanceAutomaticMaterials(1);
            if (before == mesh.Material.Botw.Parameters["const_value0"][0] || !ReferenceEquals(vertices, mesh.Vertices)) throw new Exception("Material-only update failed");
            Console.WriteLine("PASS standalone actor materials advance without skeletal posing or vertex uploads.");
        }
    }
}
