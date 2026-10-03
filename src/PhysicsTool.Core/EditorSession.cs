using HKCLTool;

namespace PhysicsTool.Core;

public sealed class EditorSession
{
    private readonly Stack<string> _undo = new(), _redo = new();
    private string? _savedState;
    public HkclService Document { get; private set; } = new();
    public HkclPlatform Platform { get; set; } = HkclPlatform.WiiU;
    public string? Path { get; private set; }
    public int ClothIndex { get; set; }
    public bool IsDirty => Document.HasDocument && Document.CaptureState() != _savedState;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool CanSimulate => Document.HasDocument && !Document.IsReadOnlyExternal && Document.GetParticleRows(ClothIndex).Any(p => !p.Fixed);

    public void New(string name, int format)
    {
        var document = HkclService.CreateBlankCloth(name, format == 2);
        Document = document; Platform = format == 0 ? HkclPlatform.WiiU : HkclPlatform.Switch;
        Path = null; ClothIndex = 0; _savedState = null; _undo.Clear(); _redo.Clear();
    }
    public void Load(string path)
    {
        var document = new HkclService(); document.Load(path);
        var platform = document.IsReadOnlyExternal ? HkclPlatform.Switch : HkclService.DetectHkclPlatform(path);
        Document = document; Platform = platform; Path = path; ClothIndex = 0;
        _undo.Clear(); _redo.Clear(); _savedState = document.CaptureState();
    }
    public void Save(string path)
    {
        Document.SaveHkcl(path, Platform); Path = path; _savedState = Document.CaptureState();
    }
    public void Edit(Action<HkclService, int> action)
    {
        var before = Document.CaptureState();
        try { action(Document, ClothIndex); }
        catch { Document.RestoreState(before); throw; }
        if (Document.CaptureState() == before) return;
        _undo.Push(before); _redo.Clear();
        if (_undo.Count > 50)
        {
            var keep = _undo.Take(50).Reverse().ToArray(); _undo.Clear();
            foreach (var snapshot in keep) _undo.Push(snapshot);
        }
    }
    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);
    private void Restore(Stack<string> source, Stack<string> target)
    {
        if (source.Count == 0) return;
        var before = Document.CaptureState(); Document.RestoreState(source.Peek());
        source.Pop(); target.Push(before);
        ClothIndex = Math.Clamp(ClothIndex, 0, Math.Max(0, Document.GetClothSummaries().Count - 1));
    }
}
