using System.Globalization;
using System.Security.Cryptography;
using BymlLibrary;

namespace PhysicsTool.Core;

internal sealed partial class ShrinePlacementEditor
{
    private readonly Dictionary<(int Original, bool Delete), string?> _populationRestrictions = new();

    internal string? PopulationRestriction(int index, bool delete)
    {
        if (EditRestriction(index) is { } restriction) return restriction;
        int key = _keys[index];
        // Copies were admitted without external links and are safe to remove again.
        if (_state.Copies.ContainsKey(key)) return null;
        if (_populationRestrictions.TryGetValue((key, delete), out var cached)) return cached;
        string? Check()
        {
            var actor = _original.Scene.Actors[key];
            if (actor.MergedPath.Length > 0 || _original.Scene.Actors.Any(a => a.ParentIndex == key))
                return "Duplicate/delete currently supports individual actors, not merged-scene parents.";
            if (actor.GroupId != 0 || actor.GroupGuid != 0)
                return "This actor belongs to a GroundControl group. Group duplication/deletion is not supported yet.";
            ulong collisionId = actor.PlacementId != 0 ? actor.PlacementId : actor.Hash;
            var link = _original.Compound?.ActorLinks.SingleOrDefault(l => l.Hash == collisionId);
            if (link != null && (link.BodyIndex != 0 || link.GroupId != 0))
                return "This actor belongs to a moving collision group. Group duplication/deletion is not supported yet.";
            var own = Byml.FromBinary(_sources[actor.SourceScene]).GetMap()["Actors"].GetArray()[actor.SourceIndex].GetMap();
            if (own.ContainsKey("Links") || own.ContainsKey("Rails") || own.ContainsKey("BancPath"))
                return "This actor has scene links or rails. Disconnect them before duplicating or deleting it.";
            if (own.TryGetValue("Phive", out var phive) && phive.GetMap().Any(p => p.Key != "Placement"))
                return "This actor has physics links or constraints that need coordinated editing.";
            var ids = new HashSet<ulong> { actor.Hash, collisionId };
            foreach (var (path, bytes) in _sources)
            {
                var root = Byml.FromBinary(bytes).GetMap();
                foreach (var (field, value) in root)
                {
                    if (field == "Actors")
                    {
                        var actors = value.GetArray();
                        for (int i = 0; i < actors.Count; i++)
                        {
                            var map = actors[i].GetMap();
                            foreach (var (name, node) in map)
                            {
                                if (path == actor.SourceScene && i == actor.SourceIndex && name == "Hash") continue;
                                if (path == actor.SourceScene && i == actor.SourceIndex && name == "Phive")
                                {
                                    if (node.GetMap().TryGetValue("Placement", out var placement))
                                        foreach (var property in placement.GetMap())
                                            if (property.Key != "ID" && References(property.Value, ids)) return "This actor has an additional physics reference.";
                                    continue;
                                }
                                if (References(node, ids)) return "This actor is referenced by scene links, AI or another actor. Linked deletion/duplication is not supported yet.";
                            }
                        }
                    }
                    else if (References(value, ids)) return "This actor is referenced by scene AI, rails or other scene data.";
                }
            }
            return null;
        }
        var result = Check(); _populationRestrictions[(key, delete)] = result; return result;
    }

    internal IReadOnlyList<int> Duplicate(IEnumerable<int> indices)
    {
        var selected = indices.Distinct().ToArray(); if (selected.Length == 0) return [];
        foreach (int index in selected) if (PopulationRestriction(index, false) is { } reason) throw new NotSupportedException(reason);
        var next = _state with { Copies = new(_state.Copies), Transforms = new(_state.Transforms) };
        var used = Scene.Actors.SelectMany(a => new[] { a.Hash, a.PlacementId }).Concat(_original.Compound!.ActorLinks.Select(l => l.Hash)).ToHashSet();
        // Avoid every ID present in loaded scene metadata, not just visible actors.
        foreach (var bytes in _sources.Values) CollectIds(Byml.FromBinary(bytes), used);
        ulong NewId()
        {
            ulong id;
            do { id = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)); } while (id == 0 || !used.Add(id));
            return id;
        }
        var added = new List<int>();
        foreach (int index in selected)
        {
            var actor = Scene.Actors[index]; int original = _state.Copies.GetValueOrDefault(_keys[index])?.Original ?? _keys[index];
            ulong hash = NewId(), placement = actor.PlacementId == 0 ? 0 : actor.PlacementId == actor.Hash ? hash : NewId();
            uint srt;
            do { srt = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)); } while (srt == 0 || !used.Add(srt));
            int key = _nextKey++;
            next.Copies.Add(key, new(original, hash, placement, srt)); next.Transforms.Add(key, ShrineTransform.Local(actor)); added.Add(key);
        }
        Commit(next);
        return added.Select(key => Array.IndexOf(_keys, key)).ToArray();
    }

    internal void Delete(IEnumerable<int> indices)
    {
        var selected = indices.Distinct().ToArray(); if (selected.Length == 0) return;
        foreach (int index in selected) if (PopulationRestriction(index, true) is { } reason) throw new NotSupportedException(reason);
        var next = _state with { Copies = new(_state.Copies), Transforms = new(_state.Transforms), Deleted = new(_state.Deleted) };
        foreach (int index in selected)
        {
            int key = _keys[index];
            if (!next.Copies.Remove(key)) next.Deleted.Add(key);
            next.Transforms.Remove(key);
        }
        Commit(next);
    }

    private static bool References(Byml node, IReadOnlySet<ulong> ids)
    {
        if (node.Type == BymlNodeType.Map) return node.GetMap().Values.Any(v => References(v, ids));
        if (node.Type == BymlNodeType.Array) return node.GetArray().Any(v => References(v, ids));
        if (TryId(node, out var id)) return ids.Contains(id);
        if (node.Type == BymlNodeType.String)
        {
            string value = node.GetString();
            return ulong.TryParse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
                value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? NumberStyles.HexNumber : NumberStyles.Integer,
                CultureInfo.InvariantCulture, out id) && ids.Contains(id);
        }
        return false;
    }
    private static void CollectIds(Byml node, HashSet<ulong> ids)
    {
        if (node.Type == BymlNodeType.Map) { foreach (var value in node.GetMap().Values) CollectIds(value, ids); }
        else if (node.Type == BymlNodeType.Array) { foreach (var value in node.GetArray()) CollectIds(value, ids); }
        else if (TryId(node, out var id)) ids.Add(id);
    }
    private static bool TryId(Byml node, out ulong id)
    {
        id = node.Type switch { BymlNodeType.UInt64 => node.GetUInt64(), BymlNodeType.UInt32 => node.GetUInt32(),
            BymlNodeType.Int64 => unchecked((ulong)node.GetInt64()), BymlNodeType.Int => unchecked((ulong)node.GetInt()), _ => 0 };
        return id != 0;
    }
}
