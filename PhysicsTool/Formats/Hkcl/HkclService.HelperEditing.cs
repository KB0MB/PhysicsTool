using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    private BotwAampWriter.AampListNode HelperData() => SidecarConnectionEditing.Clone(
        (_bphhb?.Archive ?? _bphyssb?.Archive)?.Root.Children.Single()
        ?? throw new InvalidOperationException("Open a support/helper bone file first."));
    private void EditHelperData(Action<BotwAampWriter.AampListNode> edit)
    {
        if (_bphhb != null) _bphhb = BphhbBridge.EditConnections(_bphhb, edit);
        else if (_bphyssb != null) _bphyssb = BphyssbBridge.EditConnections(_bphyssb, edit);
        else throw new InvalidOperationException("Open a support/helper bone file first.");
    }
    public IReadOnlyList<HelperConnectionRow> GetHelperConnections(int boneIndex) =>
        SidecarConnectionEditing.Read(HelperData(), boneIndex, _bphhb != null);
    public IReadOnlyList<HelperConnectionSource> GetHelperInputs(int groupIndex)
    {
        var data = (_bphhb?.Archive ?? _bphyssb?.Archive)?.Root.Children.Single();
        var drivers = data?.FindChild(_bphhb != null ? "driver_bone_list" : "main_bone_list");
        var bones = GetSidecarBoneRows();
        return (GetSidecarDriverGroup(groupIndex)?.DriverIndices ?? []).Select(index => {
            var bone = drivers?.Objects.ElementAtOrDefault(index)?.Find(_bphhb != null ? 3407736066u : 893828983u)?.AsInt32() ?? -1;
            return new HelperConnectionSource(index, false, bones.ElementAtOrDefault(bone)?.Name ?? $"Input {index}");
        }).ToArray();
    }
    public IReadOnlyList<HelperConnectionSource> GetHelperConnectionSources(int groupIndex)
    {
        var sources = GetHelperCurves(groupIndex).Select(c => new HelperConnectionSource(
            _bphhb != null ? c.Index : (1 << 16) | c.Index, false, c.ToString())).ToList();
        if (_bphyssb?.Archive is { } archive)
        {
            var data = archive.Root.Children.Single();
            var drivers = data.FindChild("main_bone_list");
            var bones = GetSidecarBoneRows();
            var group = GetSidecarDriverGroup(groupIndex);
            var linear = data.FindChild("connection_linear_list");
            for (var i = 0; i < (linear?.Objects.Count ?? 0); i++)
            {
                var packed = linear!.Objects[i].Find(554147540u)?.AsInt32() ?? -1;
                var driver = (int)((uint)packed >> 16);
                var attribute = packed & 0xffff;
                if (group?.DriverIndices.Contains(driver) != true) continue;
                var bone = drivers?.Objects.ElementAtOrDefault(driver)?.Find(893828983u)?.AsInt32() ?? -1;
                sources.Add(new(i, true, $"{bones.ElementAtOrDefault(bone)?.Name ?? "Unknown input"} / {(attribute < 6 ? SidecarConnectionEditing.Channels[attribute] : "Unknown attribute")} (linear {i})"));
            }
        }
        return sources;
    }
    public void SetHelperConnection(int boneIndex, int channel, int first, int second, float firstWeight = 1, float secondWeight = 1) =>
        EditHelperData(data => SidecarConnectionEditing.Connect(data, _bphhb != null, boneIndex, channel, first, second, firstWeight, secondWeight));
    public int CreateHelperCurve(int groupIndex, int driverIndex, int attribute, IReadOnlyList<Vector4> keys)
    {
        if (GetSidecarDriverGroup(groupIndex)?.DriverIndices.Contains(driverIndex) != true)
            throw new ArgumentException("Choose an input from this driver group.");
        var index = -1;
        EditHelperData(data => index = SidecarConnectionEditing.CreateCurve(data, _bphhb != null, driverIndex, attribute, keys));
        return index;
    }
    public void DeleteHelper(int boneIndex) => EditHelperData(data => SidecarConnectionEditing.DeleteHelper(data, _bphhb != null, boneIndex));

    public IReadOnlyList<SidecarCurveRow> GetHelperCurves(int groupIndex)
    {
        var group = GetSidecarDriverGroup(groupIndex);
        var archive = _bphhb?.Archive ?? _bphyssb?.Archive;
        return group == null || archive == null ? [] : SidecarCurveEditing.Read(archive, group, _bphhb != null);
    }
    public void UpdateHelperCurve(int groupIndex, int curveIndex, IReadOnlyList<Vector4> keys)
    {
        var row = GetHelperCurves(groupIndex).SingleOrDefault(c => c.Index == curveIndex)
            ?? throw new ArgumentException("The curve is not part of this driver group.");
        SidecarCurveEditing.Validate(row, keys);
        if (_bphhb != null) _bphhb = BphhbBridge.UpdateCurveKeys(_bphhb, curveIndex, keys);
        else if (_bphyssb != null) _bphyssb = BphyssbBridge.UpdateCurveKeys(_bphyssb, curveIndex, keys);
    }
}
