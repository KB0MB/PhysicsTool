using BymlLibrary;

namespace PhysicsTool.Core;

internal sealed record BphscGroundControl(string Actor, ulong Hash, ulong PlacementId, ulong GroupGuid, uint GroupId);

internal static class BphscBancCheck
{
    internal static IReadOnlyList<BphscGroundControl> Read(string path, IEnumerable<string>? roots = null)
    {
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("BANC exceeds the 64 MiB input limit.");
        var bytes = BphscDocument.ReadResource(path, roots);
        var root = Byml.FromBinary(bytes).GetMap();
        var controls = new List<BphscGroundControl>();
        if (!root.TryGetValue("Actors", out var actors)) return controls;
        foreach (var node in actors.GetArray())
        {
            var actor = node.GetMap();
            if (!actor.TryGetValue("Gyaml", out var name) || !name.GetString().StartsWith("GroundControlActor", StringComparison.Ordinal)) continue;
            var placement = actor.TryGetValue("Phive", out var phive) && phive.GetMap().TryGetValue("Placement", out var p) ? p.GetMap() : [];
            controls.Add(new(name.GetString(), Unsigned(actor, "Hash"), Unsigned(placement, "ID"), Unsigned(placement, "GroupGUID"), checked((uint)Unsigned(placement, "GroupID"))));
        }
        return controls;
    }

    internal static string Report(BphscDocument document, IReadOnlyList<BphscGroundControl> controls)
    {
        if (controls.Count == 0) return "No GroundControl actors found in this BANC. Check the shrine's other BANC files. Referenced merged BANC files are not scanned automatically.";
        var lines = new List<string>();
        foreach (var control in controls)
        {
            lines.Add($"{control.Actor}\nActor 0x{control.Hash:x16} / Placement ID 0x{control.PlacementId:x16}\nGroupID {control.GroupId} / GroupGUID 0x{control.GroupGuid:x16}");
            var problems = new List<string>();
            var bodies = document.Bodies.Where(b => b.PlacementGroupId == control.GroupGuid && control.GroupGuid != 0).ToArray();
            var links = document.ActorLinks.Where(l => l.Hash == control.PlacementId).ToArray();
            if (control.Hash != control.PlacementId) problems.Add("Actor hash and placement ID differ; check scene references.");
            if (control.GroupId == 0 || control.GroupGuid == 0) problems.Add("Missing group binding in BANC.");
            if (bodies.Length != 1) problems.Add($"Expected one BPHSC body with this GroupGUID; found {bodies.Length}.");
            if (links.Length != 1) problems.Add($"Expected one BPHSC actor link for the placement ID; found {links.Length}.");
            if (bodies.Length == 1 && bodies[0].GroupId != control.GroupId) problems.Add($"Body {bodies[0].Index} uses GroupID {bodies[0].GroupId} instead.");
            if (links.Length == 1 && (links[0].GroupId != control.GroupId || bodies.Length == 1 && links[0].BodyIndex != bodies[0].Index))
                problems.Add($"Actor link targets body {links[0].BodyIndex}, GroupID {links[0].GroupId}.");
            lines.Add(problems.Count == 0 ? $"OK: linked to body {bodies[0].Index}." : string.Join("\n", problems));
            lines.Add("");
        }
        lines.Add("This checks group references only. It does not validate AI logic, movement parameters, merged BANC files, or shape membership. BANC files are never modified here.");
        return string.Join("\n", lines);
    }

    private static ulong Unsigned(IDictionary<string, Byml> map, string key)
    {
        if (!map.TryGetValue(key, out var node)) return 0;
        return node.Type switch
        {
            BymlNodeType.UInt64 => node.GetUInt64(),
            BymlNodeType.UInt32 => node.GetUInt32(),
            BymlNodeType.Int => checked((ulong)node.GetInt()),
            BymlNodeType.Int64 => checked((ulong)node.GetInt64()),
            _ => throw new InvalidDataException($"{key} must be an unsigned integer.")
        };
    }
}
