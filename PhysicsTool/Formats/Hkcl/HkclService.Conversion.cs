namespace HKCLTool;

public sealed partial class HkclService
{
    internal HkclService CopyForConversion()
    {
        // Source identity is part of the measured BPHCL conversion profile lookup.
        var copy = new HkclService { _path = _path };
        copy.RestoreState(CaptureState());
        return copy;
    }
}
