using System.Buffers.Binary;
using System.Numerics;
using BymlLibrary;
using HKCLTool;
using Revrs;
using ZstdSharp;

namespace PhysicsTool.Core;

// Edits are source-local. World poses, model vertices and collision are derived from
// the original scene each time, avoiding cumulative drift through undo or repeated moves.
internal sealed partial class ShrinePlacementEditor
{
    private readonly ShrineSceneAssets _original;
    private readonly string _root;
    private readonly Dictionary<string, byte[]> _sources = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Copy(int Original, ulong Hash, ulong PlacementId, uint SrtHash);
    private sealed record State(Dictionary<int, ShrineTransform> Transforms, HashSet<int> Deleted, Dictionary<int, Copy> Copies,
        Dictionary<int, GroundControlEdit> Groups);
    private State _state = new(new(), new(), new(), new());
    private readonly Stack<State> _undo = new(), _redo = new();
    private int[] _keys;
    private int _nextKey;
    internal ShrineSceneDocument Scene { get; private set; }
    internal VisualModelData Model { get; private set; }
    internal BphscDocument? Compound { get; private set; }
    internal bool Dirty { get; private set; }
    internal bool CanUndo => _undo.Count != 0;
    internal bool CanRedo => _redo.Count != 0;
    internal int SourceCount => _sources.Count;
    internal VisualModelData PreviewMove(int index, Vector3 worldDelta)
        => PreviewMove([index], worldDelta);

    internal VisualModelData PreviewMove(IEnumerable<int> indices, Vector3 worldDelta)
        => PreviewTransform(indices, Matrix4x4.CreateTranslation(worldDelta));

    internal VisualModelData PreviewTransform(IEnumerable<int> indices, Matrix4x4 worldDelta)
    {
        var owners = indices.ToHashSet();
        foreach (var actor in Scene.Actors) if (owners.Contains(actor.ParentIndex)) owners.Add(actor.Index);
        var model = new VisualModelData { SourcePath = Model.SourcePath, BoundsMinimum = Model.BoundsMinimum, BoundsMaximum = Model.BoundsMaximum };
        foreach (var mesh in Model.Meshes)
        {
            int owner = int.Parse(mesh.Name.AsSpan(0, mesh.Name.IndexOf(':')));
            if (owners.Contains(owner)) ShrineSceneAssets.AddMesh(model, mesh, worldDelta, mesh.Name);
            else { model.Meshes.Add(mesh); model.TriangleCount += mesh.Indices.Length / 3; }
        }
        return model;
    }

    internal ShrinePlacementEditor(ShrineSceneAssets assets, string root)
    {
        _original = assets; _root = Path.GetFullPath(root); Scene = assets.Scene; Model = assets.Model; Compound = assets.Compound;
        _keys = assets.Scene.Actors.Select(a => a.Index).ToArray(); _nextKey = _keys.Length;
        foreach (string path in assets.Scene.Actors.Select(a => a.SourceScene).Distinct(StringComparer.OrdinalIgnoreCase))
            _sources[path] = BphscDocument.ReadResource(path, [root]);
        // Keep empty companion files too: the scene is always exported as a pair.
        string source = Path.GetFullPath(assets.Model.SourcePath);
        if (ShrineSceneCatalog.Companion(source) is { } other && !_sources.ContainsKey(other)) _sources[other] = BphscDocument.ReadResource(other, [root]);
    }

    internal string? EditRestriction(int index)
    {
        var actor = Scene.Actors[index];
        if (actor.Name.StartsWith("GroundControlActor", StringComparison.Ordinal))
            return "Use the GroundControl inspector to move this controller and its collision group together.";
        if (_original.Scene.Actors.Count(a => a.SourceScene == actor.SourceScene && a.SourceIndex == actor.SourceIndex) != 1)
            return "This merged BANC is reused by several placements. Move its parent scene instead of changing every copy.";
        if (_original.Compound == null) return "Placement saving requires the matching BPHSC to be loaded.";
        if (_original.Compound.Water.Count != 0 || _original.Compound.ExternalMeshCount != 0)
            return "This shrine contains water or external collision meshes. Coordinated placement editing for that layout is not supported yet.";
        return null;
    }

    internal void Move(int index, Vector3 localPosition)
        => SetTransform(index, ShrineTransform.Local(Scene.Actors[index]) with { Position = localPosition });

    internal void SetTransform(int index, ShrineTransform transform)
    {
        if (EditRestriction(index) is { } reason) throw new NotSupportedException(reason);
        transform.Validate();
        if (transform == ShrineTransform.Local(Scene.Actors[index])) return;
        Commit(_state with { Transforms = new(_state.Transforms) { [_keys[index]] = transform } });
    }

    internal void MoveTogether(IEnumerable<int> indices, Vector3 worldDelta)
        => TransformTogether(indices, Matrix4x4.CreateTranslation(worldDelta));

    internal void TransformTogether(IEnumerable<int> indices, Matrix4x4 worldDelta)
    {
        ShrineTransform.Decompose(worldDelta, out _, out _, out _);
        var selected = indices.ToHashSet();
        if (selected.Count == 0 || worldDelta == Matrix4x4.Identity) return;
        var next = new Dictionary<int, ShrineTransform>(_state.Transforms);
        foreach (int index in selected)
        {
            var actor = Scene.Actors[index];
            // A selected descendant already inherits the selected ancestor's move.
            int ancestor = actor.ParentIndex;
            while (ancestor >= 0 && !selected.Contains(ancestor)) ancestor = Scene.Actors[ancestor].ParentIndex;
            if (ancestor >= 0) continue;
            if (EditRestriction(index) is { } reason) throw new NotSupportedException(reason);
            var parent = actor.ParentIndex < 0 ? Matrix4x4.Identity : Scene.Actors[actor.ParentIndex].WorldTransform;
            if (!Matrix4x4.Invert(parent, out var inverse)) throw new InvalidOperationException("Parent placement cannot be inverted.");
            next[_keys[index]] = ShrineTransform.SameLinear(worldDelta, Matrix4x4.Identity)
                ? ShrineTransform.Local(actor) with { Position = actor.LocalPosition + Vector3.TransformNormal(worldDelta.Translation, inverse) }
                : ShrineTransform.FromMatrix(actor.WorldTransform * worldDelta * inverse);
        }
        Commit(_state with { Transforms = next });
    }

    private void Commit(State next)
    {
        Rebuild(next);
        _undo.Push(_state); _redo.Clear(); _state = next; Dirty = true;
    }
    internal void Undo() { if (_undo.TryPeek(out var next)) { Rebuild(next); _undo.Pop(); _redo.Push(_state); _state = next; Dirty = true; } }
    internal void Redo() { if (_redo.TryPeek(out var next)) { Rebuild(next); _redo.Pop(); _undo.Push(_state); _state = next; Dirty = true; } }

    private void Rebuild(State state)
    {
        var actors = new List<ShrineActor>(); var moves = new Dictionary<ulong, Matrix4x4>();
        var deltas = new Dictionary<int, Matrix4x4>();
        var keys = _original.Scene.Actors.Select(a => a.Index).Where(i => !state.Deleted.Contains(i)).Concat(state.Copies.Keys.Order()).ToArray();
        var indices = keys.Select((key, index) => (key, index)).ToDictionary(p => p.key, p => p.index);
        var duplicateIds = _original.Scene.Actors.GroupBy(a => a.PlacementId != 0 ? a.PlacementId : a.Hash).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (int key in keys)
        {
            var copy = state.Copies.GetValueOrDefault(key);
            var original = _original.Scene.Actors[copy?.Original ?? key];
            var local = state.Transforms.GetValueOrDefault(key, ShrineTransform.Local(original));
            int parentIndex = original.ParentIndex < 0 ? -1 : indices[original.ParentIndex];
            var parent = parentIndex < 0 ? Matrix4x4.Identity : actors[parentIndex].WorldTransform;
            var transform = ShrineSceneAssets.Placement(original with { Position = local.Position, Rotation = local.Rotation, Scale = local.Scale }) * parent;
            var actor = original with { Index = actors.Count, ParentIndex = parentIndex, Hash = copy?.Hash ?? original.Hash,
                PlacementId = copy?.PlacementId ?? original.PlacementId, LocalPosition = local.Position, Rotation = local.Rotation,
                Scale = local.Scale, Position = transform.Translation, WorldTransform = transform };
            actor = ApplyGroupIdentity(actor, original, state);
            actors.Add(actor);
            if (transform == original.WorldTransform) { if (copy != null) deltas.Add(actor.Index, Matrix4x4.Identity); continue; }
            if (!Matrix4x4.Invert(original.WorldTransform, out var inverse)) throw new NotSupportedException("The original actor transform cannot be inverted.");
            var delta = ShrineTransform.SameLinear(transform, original.WorldTransform)
                ? Matrix4x4.CreateTranslation(transform.Translation - original.Position) : inverse * transform;
            if (original.Name.StartsWith("GroundControlActor", StringComparison.Ordinal) && !state.Groups.ContainsKey(key))
                throw new NotSupportedException("Moving a parent containing a GroundControl requires coordinated group edits.");
            deltas.Add(actor.Index, delta);
            ulong id = actor.PlacementId != 0 ? actor.PlacementId : actor.Hash;
            if (duplicateIds.Contains(id)) throw new NotSupportedException("A moved actor shares its placement ID with another scene instance. Collision ownership is ambiguous.");
            if (!moves.TryAdd(id, delta)) throw new InvalidDataException("Moved actors share a collision placement identifier.");
        }
        ulong CollisionId(ShrineActor a) => a.PlacementId != 0 ? a.PlacementId : a.Hash;
        var compound = _original.Compound?.EditActors(state.Deleted.Select(i => CollisionId(_original.Scene.Actors[i])).ToHashSet(),
            state.Copies.Values.Select(c => new BphscActorCopy(CollisionId(_original.Scene.Actors[c.Original]), c.PlacementId != 0 ? c.PlacementId : c.Hash, c.SrtHash)).ToArray()).TransformActors(moves);
        compound = RebuildGroundControls(compound, actors, indices, state);
        var model = new VisualModelData { SourcePath = _original.Model.SourcePath, BoundsMinimum = _original.Model.BoundsMinimum,
            BoundsMaximum = _original.Model.BoundsMaximum, TriangleCount = 0 };
        var meshes = _original.Model.Meshes.ToLookup(m => int.Parse(m.Name.AsSpan(0, m.Name.IndexOf(':'))));
        foreach (var actor in actors)
        {
            int key = keys[actor.Index], originalIndex = state.Copies.GetValueOrDefault(key)?.Original ?? key;
            foreach (var mesh in meshes[originalIndex])
            {
                string name = actor.Index + mesh.Name[mesh.Name.IndexOf(':')..];
                if (deltas.TryGetValue(actor.Index, out var delta) || actor.Index != originalIndex)
                    ShrineSceneAssets.AddMesh(model, mesh, deltas.GetValueOrDefault(actor.Index, Matrix4x4.Identity), name);
                else { model.Meshes.Add(mesh); model.TriangleCount += mesh.Indices.Length / 3; }
            }
        }
        Scene = new(actors); Model = model; Compound = compound; _keys = keys;
    }

    internal string Save(string destination)
    {
        destination = Path.GetFullPath(destination);
        var relative = Path.GetRelativePath(_root, destination);
        if (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            throw new InvalidOperationException("Choose an output folder outside the game dump.");
        var output = new Dictionary<string, byte[]>();
        foreach (var (path, bytes) in _sources)
        {
            var changed = Scene.Actors.Where(a => a.SourceScene == path && (_state.Copies.ContainsKey(_keys[a.Index]) ||
                ShrineTransform.Local(a) != ShrineTransform.Local(_original.Scene.Actors[_keys[a.Index]]) ||
                a.GroupId != _original.Scene.Actors[_keys[a.Index]].GroupId || a.GroupGuid != _original.Scene.Actors[_keys[a.Index]].GroupGuid ||
                _state.Groups.ContainsKey(_keys[a.Index]))).ToArray();
            var deleted = _state.Deleted.Select(i => _original.Scene.Actors[i]).Where(a => a.SourceScene == path).Select(a => a.SourceIndex).ToHashSet();
            byte[] saved = bytes;
            if (changed.Length > 0 || deleted.Count > 0)
            {
                var byml = Byml.FromBinary(bytes); var array = byml.GetMap()["Actors"].GetArray();
                foreach (var actor in changed)
                {
                    var copy = _state.Copies.GetValueOrDefault(_keys[actor.Index]);
                    var original = _original.Scene.Actors[copy?.Original ?? _keys[actor.Index]];
                    var record = copy == null ? array[actor.SourceIndex] : Byml.FromBinary(array[actor.SourceIndex].ToBinary(Endianness.Little, 7));
                    if (copy != null)
                    {
                        record.GetMap()["Hash"] = new Byml(copy.Hash); record.GetMap()["SRTHash"] = new Byml(copy.SrtHash);
                        if (copy.PlacementId != 0) record.GetMap()["Phive"].GetMap()["Placement"].GetMap()["ID"] = new Byml(copy.PlacementId);
                        array.Add(record);
                    }
                    void Write(string name, Vector3 value, Vector3 previous)
                    {
                        if (copy != null || value != previous) record.GetMap()[name] = new Byml(new List<Byml> { new(value.X), new(value.Y), new(value.Z) });
                    }
                    Write("Translate", actor.LocalPosition, original.LocalPosition);
                    Write("Rotate", actor.Rotation, original.Rotation);
                    Write("Scale", actor.Scale, original.Scale);
                    WriteGroundControl(record, actor, original);
                }
                foreach (int index in deleted.OrderDescending()) array.RemoveAt(index);
                bool little = bytes[0] == (byte)'Y';
                ushort version = little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)) : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2));
                saved = byml.ToBinary(little ? Endianness.Little : Endianness.Big, version);
                var check = ShrineSceneDocument.Parse(saved);
                foreach (var actor in changed)
                {
                    var savedActor = check.Actors.Single(a => a.Hash == actor.Hash);
                    if (savedActor.Position != actor.LocalPosition || savedActor.Rotation != actor.Rotation || savedActor.Scale != actor.Scale)
                        throw new InvalidDataException("BANC transform verification failed.");
                    if (savedActor.GroupId != actor.GroupId || savedActor.GroupGuid != actor.GroupGuid)
                        throw new InvalidDataException("BANC GroundControl verification failed.");
                }
            }
            output.Add(Relative(path), Encode(path, saved));
        }
        if (Compound != null && _original.CompoundPath is { } compoundPath)
        {
            var bytes = Compound.Snapshot(); _ = BphscDocument.Parse(bytes);
            output.Add(Relative(compoundPath), Encode(compoundPath, bytes));
        }
        string name = Path.GetFileName(_original.Model.SourcePath).Split('_')[0].Split('.')[0];
        string folder = Path.Combine(destination, name + "-Edited-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        string staging = folder + ".partial";
        Directory.CreateDirectory(staging);
        foreach (var (path, bytes) in output)
        {
            string target = Path.Combine(staging, "romfs", path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            AtomicFile.WriteAllBytes(target, bytes);
            if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) throw new IOException("Shrine output verification failed.");
        }
        Directory.Move(staging, folder); Dirty = false; return folder;
    }

    private string Relative(string path)
    {
        string relative = Path.GetRelativePath(_root, path);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative)) throw new InvalidOperationException("Scene resource is outside the selected romfs.");
        return relative;
    }
    private static byte[] Encode(string path, byte[] bytes)
    {
        if (!path.EndsWith(".zs", StringComparison.OrdinalIgnoreCase)) return bytes;
        using var compressor = new Compressor(9); return compressor.Wrap(bytes).ToArray();
    }
}
