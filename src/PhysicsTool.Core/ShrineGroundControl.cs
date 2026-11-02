using System.Numerics;
using BymlLibrary;

namespace PhysicsTool.Core;

internal sealed record GroundControlParameter(string Key, string Label, string Hint, float[] Default, bool Integer = false);
internal sealed record ShrineGroundControl(int Controller, int Body, IReadOnlyList<int> Members, string? Restriction, string? MoveRestriction);

internal sealed partial class ShrinePlacementEditor
{
    private sealed record GroundControlEdit(ushort GroupId, ulong Guid, Dictionary<string, float[]> Parameters);

    // Names and defaults come from each native actor's BlackboardParamTable. Do not
    // invent Y/Z scalar variants: these controllers expose their local X axis only.
    internal static IReadOnlyList<GroundControlParameter> GroundControlParameters(string actor)
    {
        GroundControlParameter P(string key, string label, string hint, float value = 0, bool integer = false)
            => new(key, label, hint, [value], integer);
        GroundControlParameter V(string key, string label, string hint) => new(key, label, hint, [0, 0, 0]);
        var travel = new[] {
            P("LinearVelX", "Travel speed", "LinearVelX: movement speed along the controller's local X axis."),
            P("TargetPosX", "Travel distance", "TargetPosX: target offset along local X, relative to the starting pivot."),
            P("AngularVelX", "Turn speed", "AngularVelX: rotation speed around local X. Native angular values use degrees."),
            P("TargetRotX", "Target angle", "TargetRotX: rotation about local X, in degrees.") };
        return actor switch
        {
            "GroundControlActor_SimpleMoveTo" => travel,
            "GroundControlActor_SimpleMoveAndStop" => [.. travel,
                P("InitialPosX", "Initial offset", "InitialPosX: starting offset along local X."),
                P("InitialRotX", "Initial angle", "InitialRotX: starting rotation around local X, in degrees.")],
            "GroundControlActor_Rotate" => [
                P("RotateSpeedX", "Turn speed", "RotateSpeedX: continuous rotation around local X. Negative values reverse the direction."),
                P("RotateAccelX", "Turn acceleration", "RotateAccelX: rate at which the controller reaches its turn speed.", 360)],
            "GroundControlActor_RotateRightAngleOrder" => [P("RotateSpeed", "Turn speed", "RotateSpeed: speed of the ordered quarter-turn movement. Activation is controlled by scene logic.", 30)],
            "GroundControlActor_RailLoop" => [
                P("MoveVel", "Rail speed", "MoveVel: travel speed on the existing linked rail.", 10),
                P("InitialNodeIndex", "Starting rail node", "InitialNodeIndex: zero-based index into the existing rail.", integer: true),
                P("RotationType", "Rail rotation mode", "RotationType: native enum. Keep the original value unless you have tested another mode.", integer: true)],
            "GroundControlActor_WaveLoop" => [
                V("WaveRadius_Linear", "Travel amplitude", "WaveRadius_Linear: oscillation amplitude on each local axis."),
                V("WaveCycle_Linear", "Travel cycle", "WaveCycle_Linear: native cycle values on each local axis; keep unused axes at zero."),
                V("WaveRadius_Angular", "Turn amplitude", "WaveRadius_Angular: angular oscillation amplitude on each local axis."),
                V("WaveCycle_Angular", "Turn cycle", "WaveCycle_Angular: native angular cycle values on each local axis.")],
            _ => []
        };
    }

    private IDictionary<string, Byml> OriginalRecord(ShrineActor actor)
        => Byml.FromBinary(_sources[actor.SourceScene]).GetMap()["Actors"].GetArray()[actor.SourceIndex].GetMap();

    internal IReadOnlyDictionary<string, float[]> GroundControlValues(int index)
    {
        var actor = Scene.Actors[index]; var original = _original.Scene.Actors[_keys[index]];
        var record = OriginalRecord(original);
        IDictionary<string, Byml> dynamic = record.TryGetValue("Dynamic", out var node) ? node.GetMap() : new Dictionary<string, Byml>();
        var edits = _state.Groups.GetValueOrDefault(_keys[index]);
        var result = new Dictionary<string, float[]>();
        foreach (var parameter in GroundControlParameters(actor.Name))
        {
            if (edits?.Parameters.TryGetValue(parameter.Key, out var value) == true) result[parameter.Key] = (float[])value.Clone();
            else if (dynamic.TryGetValue(parameter.Key, out node))
            {
                float Number(Byml v) => v.Type == BymlNodeType.Int ? v.GetInt() : v.GetFloat();
                result[parameter.Key] = parameter.Default.Length == 3 ? node.GetArray().Select(Number).ToArray() : [Number(node)];
            }
            else result[parameter.Key] = (float[])parameter.Default.Clone();
            if (result[parameter.Key].Length != parameter.Default.Length || result[parameter.Key].Any(v => !float.IsFinite(v)))
                throw new InvalidDataException("Invalid GroundControl parameter: " + parameter.Key);
        }
        return result;
    }

    internal ShrineGroundControl? GroundControl(int actorIndex)
    {
        var selected = Scene.Actors[actorIndex];
        var controller = selected.Name.StartsWith("GroundControlActor", StringComparison.Ordinal) ? selected :
            Scene.Actors.FirstOrDefault(a => selected.GroupGuid != 0 && a.GroupGuid == selected.GroupGuid && a.Name.StartsWith("GroundControlActor", StringComparison.Ordinal));
        if (controller == null) return null;
        var body = Compound?.Bodies.SingleOrDefault(b => b.PlacementGroupId != 0 && b.PlacementGroupId == controller.GroupGuid);
        var members = Scene.Actors.Where(a => a.GroupGuid != 0 && a.GroupGuid == controller.GroupGuid).Select(a => a.Index).ToArray();
        string? restriction = null, moveRestriction = null;
        if (body == null || controller.GroupId == 0 || body.GroupId != controller.GroupId)
            restriction = "The controller's GroupID/GroupGUID does not match a collision body.";
        else if (Scene.Actors.Count(a => a.GroupGuid == controller.GroupGuid && a.Name.StartsWith("GroundControlActor", StringComparison.Ordinal)) != 1)
            restriction = "Several controllers share this group. Resolve that ambiguity before editing it.";
        else if (Compound!.Water.Count != 0 || Compound.ExternalMeshCount != 0)
            restriction = "GroundControl editing for water or external collision meshes is not supported yet.";
        else
        {
            foreach (int index in members)
            {
                var actor = Scene.Actors[index];
                if (actor.GroupId != controller.GroupId || actor.ParentIndex >= 0 || actor.MergedPath.Length > 0 ||
                    _original.Scene.Actors.Count(a => a.SourceScene == actor.SourceScene && a.SourceIndex == actor.SourceIndex) != 1)
                    restriction = "This group has mixed identifiers or merged-scene placements that require additional link handling.";
                if (Scene.Actors.Count(a => CollisionId(a) == CollisionId(actor)) != 1)
                    restriction = "A group member shares its collision placement ID with another actor. Collision ownership is ambiguous.";
                var link = Compound.ActorLinks.SingleOrDefault(l => l.Hash == CollisionId(actor));
                if (link == null || link.BodyIndex != body.Index || link.GroupId != body.GroupId)
                    restriction = "A group member is missing its matching BPHSC actor link.";
            }
            var hashes = members.Select(i => CollisionId(Scene.Actors[i])).ToHashSet();
            if (Compound.ActorLinks.Any(l => l.BodyIndex == body.Index && !hashes.Contains(l.Hash)))
                restriction = "This collision group contains actors not resolved in the loaded BANC scene.";
            // Constraint poses, rails and AI positions can be world-space. Pivot-only
            // edits leave all member actors in place; moving the assembly needs more care.
            foreach (int index in members)
            {
                var record = OriginalRecord(_original.Scene.Actors[_keys[index]]);
                if (record.ContainsKey("Rails") || (record.TryGetValue("Phive", out var phive) && phive.GetMap().Any(p => p.Key != "Placement")))
                    moveRestriction = "This group has rails or physics constraints. Edit its pivot without moving members; assembly relocation is not supported for these links yet.";
            }
        }
        return new(controller.Index, body?.Index ?? -1, members, restriction, restriction ?? moveRestriction);
    }

    internal string? GroundControlParameterRestriction(int index)
    {
        var actor = Scene.Actors[index];
        if (!actor.Name.StartsWith("GroundControlActor", StringComparison.Ordinal) || actor.GroupGuid == 0 || actor.GroupId == 0 ||
            Compound?.Bodies.Count(b => b.PlacementGroupId == actor.GroupGuid && b.GroupId == actor.GroupId) != 1)
            return "The controller needs a matching collision group before its movement can be edited.";
        if (_original.Scene.Actors.Count(a => a.SourceScene == actor.SourceScene && a.SourceIndex == actor.SourceIndex) != 1)
            return "This controller's source is reused. Its movement cannot be edited independently.";
        return null;
    }

    internal void SetGroundControl(int index, ShrineTransform pivot, bool moveMembers, ushort groupId, ulong guid,
        IReadOnlyDictionary<string, float[]> parameters)
    {
        var group = GroundControl(index) ?? throw new ArgumentException("Select a GroundControl group.");
        var controller = Scene.Actors[group.Controller];
        pivot.Validate();
        if (pivot.Scale != controller.Scale) throw new NotSupportedException("GroundControl pivots cannot be scaled.");
        bool moving = pivot != ShrineTransform.Local(controller);
        bool rebinding = groupId != controller.GroupId || guid != controller.GroupGuid;
        string? restriction = moving || rebinding ? group.Restriction : GroundControlParameterRestriction(group.Controller);
        if (restriction is { } reason) throw new NotSupportedException(reason);
        if (moving && moveMembers && group.MoveRestriction is { } moveReason) throw new NotSupportedException(moveReason);
        if (groupId == 0 || guid == 0 || (rebinding && (_original.Compound!.Bodies.Any(b => b.Index != group.Body && (b.GroupId == groupId || b.PlacementGroupId == guid)) ||
            Compound!.Bodies.Any(b => b.Index != group.Body && (b.GroupId == groupId || b.PlacementGroupId == guid)) ||
            Scene.Actors.Any(a => !group.Members.Contains(a.Index) && (a.GroupId == groupId || a.GroupGuid == guid)))))
            throw new ArgumentException("GroupID and GroupGUID must be nonzero and unused by other groups.");
        var current = GroundControlValues(group.Controller);
        var values = new Dictionary<string, float[]>(_state.Groups.GetValueOrDefault(_keys[group.Controller])?.Parameters ?? new());
        foreach (var (key, value) in parameters)
        {
            var definition = GroundControlParameters(controller.Name).SingleOrDefault(p => p.Key == key)
                ?? throw new ArgumentException("Unsupported controller parameter: " + key);
            if (value.Length != definition.Default.Length || value.Any(v => !float.IsFinite(v)) ||
                (definition.Integer && value.Any(v => v != MathF.Truncate(v) || v < int.MinValue || v >= 2147483648f)))
                throw new ArgumentException("Invalid value for " + key);
            if (!value.SequenceEqual(current[key])) values[key] = (float[])value.Clone();
        }
        if (!moving && groupId == controller.GroupId && guid == controller.GroupGuid && parameters.All(p => p.Value.SequenceEqual(current[p.Key]))) return;
        var next = _state with { Transforms = new(_state.Transforms), Groups = new(_state.Groups) };
        next.Groups[_keys[group.Controller]] = new(groupId, guid, values);
        if (moving)
        {
            if (!Matrix4x4.Invert(controller.WorldTransform, out var inverse)) throw new InvalidDataException("Controller pivot cannot be inverted.");
            var target = ShrineSceneAssets.Placement(controller with { Position = pivot.Position, Rotation = pivot.Rotation, Scale = pivot.Scale });
            var delta = inverse * target;
            if (moveMembers)
                foreach (int member in group.Members.Where(i => i != group.Controller))
                {
                    var actor = Scene.Actors[member];
                    next.Transforms[_keys[member]] = ShrineTransform.SameLinear(delta, Matrix4x4.Identity)
                        ? ShrineTransform.Local(actor) with { Position = actor.LocalPosition + delta.Translation }
                        : ShrineTransform.FromMatrix(actor.WorldTransform * delta);
                }
            next.Transforms[_keys[group.Controller]] = pivot;
        }
        Commit(next);
    }

    private static ulong CollisionId(ShrineActor actor) => actor.PlacementId != 0 ? actor.PlacementId : actor.Hash;

    private ShrineActor ApplyGroupIdentity(ShrineActor actor, ShrineActor original, State state)
    {
        if (original.GroupGuid == 0) return actor;
        foreach (var (key, edit) in state.Groups)
            if (_original.Scene.Actors[key].GroupGuid == original.GroupGuid)
                return actor with { GroupId = edit.GroupId, GroupGuid = edit.Guid };
        return actor;
    }

    private BphscDocument? RebuildGroundControls(BphscDocument? compound, List<ShrineActor> actors, Dictionary<int, int> indices, State state)
    {
        if (compound == null) return null;
        foreach (var (key, edit) in state.Groups)
        {
            var original = _original.Scene.Actors[key]; var actor = actors[indices[key]];
            var body = _original.Compound!.Bodies.Single(b => b.PlacementGroupId == original.GroupGuid);
            if (actor.WorldTransform != original.WorldTransform)
            {
                if (!Matrix4x4.Invert(original.WorldTransform, out var inverse)) throw new InvalidDataException("Controller pivot cannot be inverted.");
                var oldBody = BodyPose(body);
                var pose = ShrineTransform.FromMatrix(oldBody * inverse * actor.WorldTransform);
                var newBody = Matrix4x4.CreateFromQuaternion(HKCLTool.BoneRotationAngles.FromRadians(pose.Rotation)) * Matrix4x4.CreateTranslation(pose.Position);
                if (!Matrix4x4.Invert(newBody, out var bodyInverse)) throw new InvalidDataException("Collision pivot cannot be inverted.");
                // Rebase the existing shapes into the new body frame. Their world
                // positions have already followed the member edits above.
                var rebase = compound.ActorLinks.Where(l => l.BodyIndex == body.Index).ToDictionary(l => l.Hash, _ => bodyInverse * oldBody);
                compound = compound.TransformActors(rebase);
                compound.SetBody(body.Index, pose.Position, pose.Rotation);
            }
            if (body.GroupId != edit.GroupId || body.PlacementGroupId != edit.Guid) compound.SetGroup(body.Index, edit.GroupId, edit.Guid);
        }
        return compound;
    }

    internal static Matrix4x4 BodyPose(BphscBody body) => Matrix4x4.CreateRotationX(body.Rotation.X) *
        Matrix4x4.CreateRotationY(body.Rotation.Y) * Matrix4x4.CreateRotationZ(body.Rotation.Z) * Matrix4x4.CreateTranslation(body.Position);

    private void WriteGroundControl(Byml record, ShrineActor actor, ShrineActor original)
    {
        var map = record.GetMap();
        if (actor.GroupId != original.GroupId || actor.GroupGuid != original.GroupGuid)
        {
            var placement = map["Phive"].GetMap()["Placement"].GetMap();
            if (actor.GroupId != original.GroupId) placement["GroupID"] = placement["GroupID"].Type switch
            {
                BymlNodeType.Int => new Byml(checked((int)actor.GroupId)),
                BymlNodeType.UInt64 => new Byml((ulong)actor.GroupId),
                BymlNodeType.Int64 => new Byml((long)actor.GroupId),
                _ => new Byml(actor.GroupId)
            };
            if (actor.GroupGuid != original.GroupGuid) placement["GroupGUID"] = new Byml(actor.GroupGuid);
        }
        if (!_state.Groups.TryGetValue(_keys[actor.Index], out var edit) || edit.Parameters.Count == 0) return;
        if (!map.TryGetValue("Dynamic", out var dynamic)) map["Dynamic"] = dynamic = new Byml(new Dictionary<string, Byml>());
        foreach (var (key, value) in edit.Parameters)
        {
            var parameter = GroundControlParameters(actor.Name).Single(p => p.Key == key);
            dynamic.GetMap()[key] = value.Length == 3 ? new Byml(value.Select(v => new Byml(v)).ToList()) :
                parameter.Integer ? new Byml(checked((int)value[0])) : new Byml(value[0]);
        }
    }
}
