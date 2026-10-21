using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

public sealed class ClothBranchResizePreview : IDisposable
{
    private readonly EditorSession _session;
    private readonly HkclService _document;
    private readonly int _cloth;
    private readonly IReadOnlySet<int> _bones;
    private readonly Vector3? _pivot;
    private readonly Guid _entry;
    private readonly string _original;
    private bool _finished;

    public ClothBranchResizePreview(EditorSession session, int bone)
        : this(session, new HashSet<int> { bone }) { }

    public ClothBranchResizePreview(EditorSession session, IReadOnlySet<int> bones, Vector3? pivot = null)
    {
        _session = session; _document = session.Document; _cloth = session.ClothIndex;
        _entry = session.SelectedEntryId; _bones = bones.ToHashSet(); _pivot = pivot;
        _document.ResizeClothBoneBranches(_cloth, _bones, Vector3.One);
        _original = _document.CaptureState();
    }
    private bool SameContext => ReferenceEquals(_session.Document, _document) && _session.SelectedEntryId == _entry && _session.ClothIndex == _cloth;
    public void Sample(float factor, bool colliders)
        => Sample(new Vector3(factor), colliders);

    public void Sample(Vector3 scale, bool colliders)
    {
        if (_finished || !SameContext) throw new InvalidOperationException("The resize no longer belongs to the selected cloth.");
        _document.RestoreState(_original);
        _document.ResizeClothBoneBranches(_cloth, _bones, scale, colliders, _pivot);
    }
    public void Commit()
    {
        if (_finished || !SameContext) throw new InvalidOperationException("The resize no longer belongs to the selected cloth.");
        var result = _document.CaptureState();
        _document.RestoreState(_original);
        _session.Edit((doc, _) => doc.RestoreState(result));
        _finished = true;
    }
    public void Dispose()
    {
        if (!_finished && SameContext) _document.RestoreState(_original);
        _finished = true;
    }
}
