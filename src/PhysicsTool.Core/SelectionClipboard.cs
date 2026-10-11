using HKCLTool;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhysicsTool.Core;

public sealed class SelectionClipboard
{
    private HkclService? _source;
    private int _cloth;
    private readonly HashSet<string> _states = new();
    private ParticleEditRow[] _particles = [];
    private BoneEditRow[] _bones = [];
    private ColliderEditRow[] _colliders = [];
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    internal static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    private static string State(HkclService doc) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(doc.CaptureState())));
    public PhysicsSelectionKind Kind { get; private set; }
    public int Count => _particles.Length + _bones.Length + _colliders.Length;
    public static bool Supports(HkclService doc, PhysicsSelectionKind kind) => doc.HasDocument && !doc.IsHelperBoneSidecar && !doc.IsHkrg && (!doc.IsBphcl || kind == PhysicsSelectionKind.Particles);
    public bool CanPaste(HkclService doc, int cloth, PhysicsSelectionKind kind) => ReferenceEquals(doc, _source) && cloth == _cloth && Kind == kind && Count > 0 && Supports(doc, kind);

    public void Copy(HkclService doc, int cloth, PhysicsSelectionKind kind, IReadOnlySet<int> selection)
    {
        if (!Supports(doc, kind) || selection.Count == 0) throw new InvalidOperationException("Select supported items to copy first.");
        var particles = kind == PhysicsSelectionKind.Particles ? doc.GetParticleRows(cloth).Where(p => selection.Contains(p.Index)).ToArray() : [];
        var bones = kind == PhysicsSelectionKind.Bones ? doc.GetBoneRows(cloth).Where(b => selection.Contains(b.Index)).ToArray() : [];
        var colliders = kind == PhysicsSelectionKind.Colliders ? doc.GetColliderRows(cloth).Where(c => selection.Contains(c.Index)).ToArray() : [];
        if (particles.Length + bones.Length + colliders.Length != selection.Count) throw new ArgumentException("The selection contains missing items.");
        _source = doc; _cloth = cloth; Kind = kind; _particles = particles; _bones = bones; _colliders = colliders;
        _states.Clear(); _states.Add(State(doc));
    }

    public int[] Paste(EditorSession session, bool mirrorX = false)
    {
        var doc = session.Document;
        if (!CanPaste(doc, session.ClothIndex, Kind)) throw new InvalidOperationException("Paste into the same document, cloth and item tab that was copied.");
        // Native append operations clone metadata by source index. Refuse a
        // changed graph rather than silently cloning an unrelated shifted row.
        if (!_states.Contains(State(doc))) throw new InvalidOperationException("The document changed since Copy. Copy the selection again before pasting.");
        var result = new List<int>();
        session.Edit((d, cloth) => {
            foreach (var source in _particles)
            {
                var row = Clone(source); if (mirrorX) row.X = -row.X;
                result.Add(d.AddParticle(cloth, row));
            }
            if (_bones.Length > 0)
            {
                var mapping = new Dictionary<int, int>();
                foreach (var source in _bones) { var index = d.AddBone(cloth, source); mapping.Add(source.Index, index); result.Add(index); }
                var names = d.GetBoneRows(cloth).Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var edits = _bones.Select(source => {
                    var row = Clone(source); row.Index = mapping[source.Index]; row.ParentIndex = mapping.GetValueOrDefault(source.ParentIndex, source.ParentIndex);
                    var stem = source.Name + "_Copy"; row.Name = stem; for (var n = 2; !names.Add(row.Name); n++) row.Name = stem + n;
                    return row;
                }).ToArray();
                d.UpdateBoneRows(cloth, edits, false);
                if (mirrorX) SelectionMirror.Apply(d, cloth, Kind, result.ToHashSet(), 0, false, false);
            }
            if (_colliders.Length > 0)
            {
                if (d.GetParticleColliderOptions(cloth).Count + _colliders.Length > 31) throw new InvalidOperationException("The copied colliders would exceed the cloth's 31 collision-mask slots.");
                foreach (var source in _colliders)
                {
                    var index = d.AddCollider(cloth, source, d.GetBoneRows(cloth).FirstOrDefault(b => b.Index == source.BoneIndex));
                    var row = Clone(source); row.Index = index; row.ClothIndex = cloth;
                    row.Name = d.GetColliderRows(cloth).Single(c => c.Index == index).Name;
                    // AddCollider clones native shape metadata; commit the
                    // authored world geometry through the normal local writer.
                    if (mirrorX) row = SelectionMirror.ReflectCollider(row, 0, Vector3.Zero);
                    d.UpdateColliderRows([row]); result.Add(index);
                }
            }
        });
        _states.Add(State(doc));
        return result.ToArray();
    }
}
