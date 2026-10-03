namespace HKCLTool;

/// <summary>
/// One independent support/helper-bone motion graph. Driver bones provide the
/// input pose; driven bones receive the graph's translated or rotated output.
/// </summary>
public sealed record SidecarDriverGroup(
    int Index,
    IReadOnlyList<int> DriverIndices,
    IReadOnlyList<int> DriverBoneIndices,
    IReadOnlyList<int> DrivenBoneIndices,
    IReadOnlyList<string> DriverBoneNames,
    IReadOnlyList<string> DrivenBoneNames,
    IReadOnlyList<int> RequiredBoneIndices,
    IReadOnlyList<string> RequiredBoneNames,
    int CurveCount,
    int OutputCount)
{
    public string DisplayName => DriverBoneNames.Count == 0
        ? $"Driver group {Index}"
        : string.Join(" + ", DriverBoneNames);
}
