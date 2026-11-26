namespace HKCLTool;

internal sealed record PlayerStateTrack(int Type, string Clip, float? Start, float? End)
{
    internal double Frame => End is >= 0 ? End.Value : Start is >= 0 ? Start.Value : 0;
}

internal sealed class PlayerEquipmentState
{
    internal ToolboxActorAnimation? Animation { get; private set; }
    internal double Frame { get; private set; }
    internal HashSet<string> HiddenMaterials { get; } = new(StringComparer.Ordinal);
    private readonly List<(ToolboxBfresDocument Source, string Clip, double Frame)> _materials = new();
    private readonly ToolboxActorAnimation? _draw, _shoot, _arrowOpen;
    private readonly bool _equipped;
    internal BotwArrowEffect? ArrowEffect { get; }

    internal PlayerEquipmentState(BotwActorResources resources, IReadOnlyDictionary<string, ToolboxBfresDocument> documents, bool equipped)
    {
        _equipped = equipped;
        ToolboxActorAnimation? Find(string state, string clip)
        {
            if (!resources.Animations.Any(a => a.State == state && a.Clip == clip)) return null;
            foreach (var (resource, document) in documents)
                if (document.SkeletalAnimations.FirstOrDefault(a => (string)a.Name == clip) is { } source)
                    return new(source, resource);
            return null;
        }
        _draw = Find("Draw", "Weapon_Bow_Attack_Draw");
        _shoot = Find("Shoot", "Weapon_Bow_Attack_Shoot");
        _arrowOpen = Find("BladeOpen", "Blade_Open");
        if (_arrowOpen != null)
        {
            try { ArrowEffect = BotwArrowEffect.Load(resources); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { resources.Warnings.Add("Ancient Arrow effect: " + UserFacingExceptionFormatter.Format(ex)); }
        }
        var tracks = resources.ReadWeaponState(equipped);
        foreach (var track in tracks)
        foreach (var (resource, document) in documents)
        {
            if (track.Type == 67 && Animation == null)
            {
                var source = document.SkeletalAnimations.FirstOrDefault(a => (string)a.Name == track.Clip);
                if (source != null) { Animation = new(source, resource); Frame = Math.Clamp(track.Frame, 0, Animation.FrameCount); }
            }
            if (track.Type == 62 && !ToolboxAutoMaterialAnimation.IsAutomatic(track.Clip)) _materials.Add((document, track.Clip, track.Frame));
            if (track.Type == 39) ReadVisibility(document, track);
        }
    }

    internal (ToolboxActorAnimation? Animation, double Frame) PoseFor(ToolboxActorAnimation? actor, double frame)
    {
        if (!_equipped || actor == null) return (Animation, Frame);
        bool charge = actor.Name == "Bow_Attack_Charge";
        bool release = actor.Name == "Bow_Attack_Shoot";
        bool heldDraw = actor.Name.StartsWith("Bow_Attack_", StringComparison.Ordinal) && actor.Name.Contains("_Draw", StringComparison.Ordinal);
        var clip = _arrowOpen != null && (charge || release || heldDraw) ? _arrowOpen
            : release ? _shoot : charge || heldDraw ? _draw : null;
        if (clip == null) return (Animation, Frame);
        var sample = heldDraw || release && ReferenceEquals(clip, _arrowOpen) ? clip.FrameCount
            : frame / actor.FramesPerSecond * clip.FramesPerSecond;
        return (clip, Math.Clamp(sample, 0, clip.FrameCount));
    }

    internal void ApplyMaterials(ToolboxActorModel model, List<string> warnings)
    {
        foreach (var state in _materials) model.AutoMaterials.ApplyState(state.Source, state.Clip, state.Frame, warnings);
    }

    internal float? EffectFrameFor(ToolboxActorAnimation? actor, double frame)
    {
        if (!_equipped || ArrowEffect == null || actor == null || _arrowOpen == null) return null;
        bool charge = actor.Name == "Bow_Attack_Charge";
        bool release = actor.Name == "Bow_Attack_Shoot";
        bool held = actor.Name.StartsWith("Bow_Attack_", StringComparison.Ordinal) && actor.Name.Contains("_Draw", StringComparison.Ordinal);
        if (!charge && !release && !held) return null;
        double animationFrame = frame / actor.FramesPerSecond * _arrowOpen.FramesPerSecond;
        double stateFrame = charge ? animationFrame : _arrowOpen.FrameCount + animationFrame;
        if (stateFrame < ArrowEffect.Binding.StartFrame || stateFrame > ArrowEffect.Binding.EndFrame) return null;
        return (float)((stateFrame - ArrowEffect.Binding.StartFrame) * 60 / _arrowOpen.FramesPerSecond);
    }

    private void ReadVisibility(ToolboxBfresDocument document, PlayerStateTrack state)
    {
        if (document.Resource is Syroot.NintenTools.NSW.Bfres.ResFile nx)
        {
            var anim = nx.MaterialAnims.FirstOrDefault(a => a.Name == state.Clip + "_fvm" || a.Name == state.Clip);
            if (anim == null) return;
            foreach (var track in anim.MaterialAnimDataList)
            {
                bool? visible = null;
                if (track.VisalCurveIndex != ushort.MaxValue)
                {
                    var curve = track.Curves[track.VisalCurveIndex];
                    visible = BooleanCurve(curve, state.Frame);
                }
                else if (track.VisualConstantIndex != ushort.MaxValue)
                    visible = (uint)track.Constants[track.VisualConstantIndex].Value != 0;
                if (visible == false) HiddenMaterials.Add(track.Name);
            }
        }
        else if (document.Resource is Syroot.NintenTools.Bfres.ResFile wii)
        {
            var anim = wii.MatVisibilityAnims.Values.FirstOrDefault(a => a.Name == state.Clip);
            if (anim == null) return;
            for (int i = 0; i < anim.Names.Count; i++)
            {
                var curve = anim.Curves.FirstOrDefault(c => c.AnimDataOffset == i);
                bool visible = curve == null ? anim.BaseDataList[i] : BooleanCurve(curve, state.Frame);
                if (!visible) HiddenMaterials.Add(anim.Names[i]);
            }
        }
    }

    private static bool BooleanCurve(dynamic curve, double frame)
    {
        float[] frames = curve.Frames;
        if (frames.Length == 0) throw new InvalidDataException("Equipment visibility curve has no keys.");
        int index = Array.BinarySearch(frames, (float)frame);
        if (index < 0) index = Math.Max(0, ~index - 1);
        if (((object)curve).GetType().GetProperty("KeyStepBoolData")?.GetValue(curve) is bool[] values)
            return values[index];
        float[,] keys = curve.Keys;
        return (((uint)keys[index / 32, 0] >> (index % 32)) & 1) != 0;
    }
}

internal sealed partial class BotwActorResources
{
    internal IReadOnlyList<PlayerStateTrack> ReadWeaponState(bool equipped)
    {
        if (IsTotk) return [];
        var link = Parameters("ActorLink", ActorName, "bxml");
        var user = Objects(link).Select(o => o.Find("ASUser")?.AsString()).FirstOrDefault(v => v != null);
        if (user == null || user == "Dummy") return [];
        var definitions = Parameters("ASList", user, "baslist")?.FindChild("ASDefines");
        var candidates = equipped ? new[] { "On", "Equiped", "Equipped" } : new[] { "Off", "Hold", "Null" };
        foreach (var state in candidates)
        {
            var file = Objects(definitions).FirstOrDefault(o => o.Find("Name")?.AsString() == state)?.Find("Filename")?.AsString();
            if (file == null || Parameters("AS", file, "bas") is not { } root) continue;
            var tracks = new List<PlayerStateTrack>();
            void Visit(AampReadList list)
            {
                foreach (var obj in list.Objects)
                {
                    if (obj.Find("FileName")?.AsString() is not { } clip || obj.Find("TypeIndex") is not { } type) continue;
                    var controls = list.Children.SelectMany(Objects).ToArray();
                    float? Read(string key) => controls.Select(o => o.Find(key)).FirstOrDefault(p => p?.Type == 1)?.AsSingle();
                    tracks.Add(new(type.AsInt32(), clip, Read("StartFrame"), Read("EndFrame")));
                }
                foreach (var child in list.Children) Visit(child);
            }
            Visit(root);
            // Stable equipment states may include an initial transition branch.
            return tracks.Where(t => t.Type is 67 or 62 or 39).GroupBy(t => t.Type).Select(g => g.Last()).ToArray();
        }
        return [];
    }
}
