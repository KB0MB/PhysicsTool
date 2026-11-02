using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

public sealed class WholeFileClothResizePreview : IDisposable
{
    private readonly EditorSession _session;
    private readonly HkclService _document;
    private readonly string _original;
    private bool _finished;
    private bool _valid;

    public WholeFileClothResizePreview(EditorSession session)
    {
        if (!session.AllClothsSelected) throw new InvalidOperationException("Select All cloths before resizing the file.");
        _session = session;
        _document = session.Document;
        _original = _document.CaptureState();
    }

    private void RequireContext()
    {
        if (_finished || !ReferenceEquals(_session.Document, _document) || !_session.AllClothsSelected)
            throw new InvalidOperationException("The resize no longer belongs to the selected file.");
    }

    public int Sample(Vector3 scale, bool colliders = true)
    {
        RequireContext();
        _valid = false;
        _document.RestoreState(_original);
        try
        {
            var moved = _document.ResizeAllCloths(scale, colliders);
            _valid = true;
            return moved;
        }
        catch
        {
            _document.RestoreState(_original);
            throw;
        }
    }

    public void Commit()
    {
        RequireContext();
        if (!_valid) throw new InvalidOperationException("Preview a valid scale before applying.");
        var result = _document.CaptureState();
        _document.RestoreState(_original);
        _session.Edit((document, _) => document.RestoreState(result));
        _finished = true;
    }

    public void Dispose()
    {
        // The captured document still needs rollback if selection changed while the popup was open.
        if (!_finished) _document.RestoreState(_original);
        _finished = true;
    }
}
