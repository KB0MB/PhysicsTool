namespace HKCLTool;

// Creates the small phcl AAMP archive stored beside TAG0 in a BPHCL. These
// are the fields observed in every matched retail cloth archive: each mesh
// registers its name, anchor bone, and compiler defaults; each collider gets
// its own registration record. This is deliberately native and static: no
// donor BPHCL is consulted while converting an HKCL.
internal static class NativeAampStandaloneWriter
{
    private const string DefaultAxisOrder = "xyz";
    private const string DefaultSwingAxis = "y";
    private const string DefaultPreset = "leather";

    public static byte[] Create(StandaloneBphclDocumentGraph graph)
    {
        var root = new BotwAampWriter.AampListNode("param_root");
        var clothMeshes = new BotwAampWriter.AampListNode("cloth_mesh_list");
        var collidables = new BotwAampWriter.AampListNode("collidable_list");
        root.Children.Add(clothMeshes);
        root.Children.Add(collidables);

        foreach (var cloth in graph.Cloths.OrderBy(cloth => cloth.Index))
        {
            var mesh = new BotwAampWriter.AampObjectNode($"cloth_mesh_{cloth.Index}");
            mesh.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("Name", cloth.Name));
            mesh.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("BaseBone", ResolveBaseBone(cloth)));
            mesh.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("BoneCorrection", false));
            mesh.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("BoneCorrectionAxisOrder", DefaultAxisOrder));
            mesh.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("Twist", false));
            mesh.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("TwistSwingAxis", DefaultSwingAxis));
            mesh.Parameters.Add(BotwAampWriter.AampParameterNode.Single("TwistAngleCoef", 0.0f));
            mesh.Parameters.Add(BotwAampWriter.AampParameterNode.Single("TwistMaxAngle", 180.0f));
            mesh.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("KeepBoneLength", false));
            mesh.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("Preset", ResolvePreset(cloth)));
            clothMeshes.Objects.Add(mesh);
        }

        var colliderIndex = 0;
        var registeredColliders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var collider in graph.Cloths
                     .OrderBy(cloth => cloth.Index)
                     .SelectMany(cloth => cloth.Colliders.OrderBy(collider => collider.Index)))
        {
            // Retail archives register shared colliders once globally. Repeating
            // a name for every cloth creates a larger archive and leaves the
            // engine with an ambiguous collider registry.
            if (!registeredColliders.Add(collider.Name))
                continue;

            var entry = new BotwAampWriter.AampObjectNode($"collidable_{colliderIndex++}");
            entry.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("Name", collider.Name));
            entry.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("ForReplace", false));
            collidables.Objects.Add(entry);
        }

        return BotwAampWriter.WriteArchive("phcl", root);
    }

    private static string ResolvePreset(StandaloneBphclClothGraph cloth)
    {
        // These are material-family defaults from native BPHCL AAMP records,
        // selected from the cloth's own identity. This does not read or copy
        // any donor BPHCL: unknown names retain the conservative old-leather
        // preset rather than pretending all cloth has identical runtime tuning.
        var name = cloth.Name;
        if (name.Contains("earring", StringComparison.OrdinalIgnoreCase))
            return "old_leather";
        if (name.Contains("mant", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("cape", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("skirt", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("outer", StringComparison.OrdinalIgnoreCase))
        {
            return "old_leather";
        }
        if (name.Contains("hair", StringComparison.OrdinalIgnoreCase))
            return "hair";

        return DefaultPreset;
    }
    private static string ResolveBaseBone(StandaloneBphclClothGraph cloth)
    {
        // The embedded phcl AAMP anchors each simulation independently. Hair
        // meshes must follow Hair_Root rather than Head; treating every setup
        // as a head accessory leaves valid runtime graphs inert in-game.
        var isHair = cloth.Name.Contains("hair", StringComparison.OrdinalIgnoreCase);
        var preferredNames = isHair
            ? new[] { "Hair_Root", "Link:Hair_Root", "Head", "Link:Head" }
            : new[] { "Head", "Link:Head", "Hair_Root", "Link:Hair_Root" };

        foreach (var preferredName in preferredNames)
        {
            var preferred = cloth.Bones.FirstOrDefault(bone =>
                string.Equals(bone.Name, preferredName, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return StripRuntimeNamespace(preferred.Name);
        }

        return StripRuntimeNamespace(cloth.Bones.FirstOrDefault(bone => bone.ParentIndex < 0)?.Name
            ?? cloth.Bones[0].Name);
    }

    private static string StripRuntimeNamespace(string name) =>
        name.StartsWith("Link:", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;
}
