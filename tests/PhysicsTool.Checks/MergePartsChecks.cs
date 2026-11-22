using System.Numerics;
using System.Security.Cryptography;
using PhysicsTool.Core;

internal static class MergePartsChecks
{
    internal static void Run(string root)
    {
        string path = Path.Combine(root, "Banc/MainField/I-4_Static.bcett.byml.zs");
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var assets = ShrineSceneAssets.Load(path, root);
        var parts = assets.Scene.Actors.Where(a => a.IsMergeParts).ToArray();
        Require(parts.Length > 0, "Native I-4 fixture has no MergeParts");
        var owners = parts.Select(a => a.Index).ToHashSet();
        Require(assets.Model.Meshes.Count > 100 && assets.Model.Meshes.All(m => ShrineSceneAssets.MeshActorIndex(m) is not { } id || !owners.Contains(id)), "MergeParts geometry remains or normal scenery is missing");
        Require(!assets.Model.Meshes.Any(m => m.Name.Contains("FldObj_Tar_MergeParts_")), "MergeParts mesh survived filtering");
        var editor = new ShrinePlacementEditor(assets, root);
        var part = parts[0];
        void Blocked(Action action)
        {
            try { action(); } catch (NotSupportedException ex) when (ex.Message.Contains("MergeParts")) { return; }
            throw new Exception("Protected MergeParts edit was accepted");
        }
        Blocked(() => editor.Move(part.Index, part.LocalPosition + Vector3.UnitX));
        Require(editor.PopulationRestriction(part.Index, false)?.Contains("MergeParts") == true &&
            editor.PopulationRestriction(part.Index, true)?.Contains("MergeParts") == true, "MergeParts can be duplicated or deleted");
        if (part.ParentIndex >= 0) Blocked(() => editor.Move(part.ParentIndex, editor.Scene.Actors[part.ParentIndex].LocalPosition + Vector3.UnitX));
        Require(!editor.Dirty && editor.Scene.Actors.Count(a => a.IsMergeParts) == parts.Length && !editor.CanUndo, "Hidden placements were removed or changed");
        Require(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Source BANC was modified");
        Console.WriteLine($"PASS I-4: {parts.Length} MergeParts records preserved, no preview meshes; direct/parent edits and population changes blocked. {assets.RenderedActors} other actors rendered.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
