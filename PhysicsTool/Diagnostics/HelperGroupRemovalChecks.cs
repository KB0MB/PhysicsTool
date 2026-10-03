using System.Numerics;
using System.Reflection;
using System.Windows.Forms;

namespace HKCLTool;

internal static class HelperGroupRemovalChecks
{
    public static void Run(string outputDirectory, string[] paths)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (var path in paths)
        {
            Check(path, outputDirectory);
            CheckEditor(path);
        }
    }

    private static void Check(string path, string directory)
    {
        var service = new HkclService();
        service.Load(path);
        var original = service.CaptureState();
        var groups = service.GetSidecarDriverGroups();
        Require(groups.Count > 0, "Fixture must contain groups.");
        var names = groups.SelectMany(group => group.RequiredBoneNames).Distinct().ToArray();
        VisualModelBone[] Bones(float phase) => names.Select((name, index) => new VisualModelBone
        {
            Name = name, Index = index,
            Position = new Vector3(index * .02f, .1f + phase * (index % 3), -.03f * index),
            Rotation = Quaternion.CreateFromYawPitchRoll(phase * (index % 4), phase * .3f, -phase * .7f)
        }).ToArray();
        var rest = Bones(0);
        var samples = new[] { -.6f, 0f, .4f }.Select(Bones).ToArray();
        var baseline = groups.Select(group => samples.Select(bones =>
            service.EvaluateSidecarDriverGroup(group.Index, bones, rest)).ToArray()).ToArray();
        var extension = Path.GetExtension(path);
        var output = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + "-removed" + extension);
        for (var removed = 0; removed < groups.Count; removed++)
        {
            service.RestoreState(original);
            service.RemoveHelperGroup(removed);
            var after = service.CaptureState();
            var survivors = service.GetSidecarDriverGroups();
            Require(survivors.Count == groups.Count - 1, "Exactly one group must be removed.");
            for (int oldIndex = 0, newIndex = 0; oldIndex < groups.Count; oldIndex++)
            {
                if (oldIndex == removed) continue;
                var expected = groups[oldIndex];
                var actual = survivors[newIndex];
                Require(expected.DriverBoneNames.SequenceEqual(actual.DriverBoneNames) &&
                    expected.DrivenBoneNames.SequenceEqual(actual.DrivenBoneNames) &&
                    expected.RequiredBoneIndices.SequenceEqual(actual.RequiredBoneIndices) &&
                    expected.CurveCount == actual.CurveCount && expected.OutputCount == actual.OutputCount,
                    "A surviving group's dependencies changed.");
                for (var sample = 0; sample < samples.Length; sample++)
                {
                    var evaluation = service.EvaluateSidecarDriverGroup(newIndex, samples[sample], rest);
                    Require(baseline[oldIndex][sample].Poses.SequenceEqual(evaluation.Poses),
                        "A surviving group's evaluated motion changed.");
                    Require(evaluation.MissingDriverBones.Count == 0, "Missing synthetic driver bone.");
                }
                newIndex++;
            }
            service.RestoreState(original);
            Require(service.CaptureState() == original, "Undo must restore exact document bytes.");
            service.RestoreState(after);
            Require(service.CaptureState() == after, "Redo must restore exact document bytes.");
            service.SaveHkcl(output, HkclPlatform.WiiU);
            service.Load(output);
            Require(service.CaptureState() == after, "Save/reload changed the edited document.");
        }
        service.RestoreState(original);
        for (var remaining = groups.Count; remaining > 0; remaining--)
        {
            service.RemoveHelperGroup(remaining / 2);
            Require(service.GetSidecarDriverGroups().Count == remaining - 1, "Repeated removal failed.");
        }
        service.SaveHkcl(output, HkclPlatform.WiiU);
        service.Load(output);
        Require(service.GetSidecarDriverGroups().Count == 0, "Empty graph failed to round-trip.");
        var empty = service.CaptureState();
        foreach (var invalid in new[] { -1, 0, 999 })
        {
            try
            {
                service.RemoveHelperGroup(invalid);
                throw new Exception("Invalid group index was accepted.");
            }
            catch (ArgumentOutOfRangeException) { }
            Require(service.CaptureState() == empty, "Rejected deletion changed the document.");
        }
        service.RestoreState(original);
        Require(service.GetSidecarDriverGroups().Count == groups.Count, "Undo after last removal failed.");
        Console.WriteLine($"PASS|{Path.GetFileName(path)}|groups={groups.Count}|motionSamples=3|individual/all deletion, save/reload, undo/redo, invalid indices");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckEditor(string path)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = new MainForm();
        object? Call(string name, params object?[] args) => typeof(MainForm).GetMethod(name, flags)!.Invoke(form, args);
        T Field<T>(string name) => (T)typeof(MainForm).GetField(name, flags)!.GetValue(form)!;
        Call("LoadCurrent", path);
        Require(Field<Button>("_removeButton").Enabled, "List removal is disabled for helpers.");
        typeof(MainForm).GetField("_directEditMode", flags)!.SetValue(form, true);
        Call("UpdateModeLayout");
        Call("RefreshParticleGrid", false, null);
        Call("UpdateButtons");
        Require(Field<Button>("_helperRemoveGroupButton").Enabled, "Editor deletion is disabled.");
        var service = Field<HkclService>("_current");
        var original = service.CaptureState();
        var count = service.GetSidecarDriverGroups().Count;
        while (service.GetSidecarDriverGroups().Count > 0)
            Call("RemoveEntry", 0, "test group");
        Require(Field<ListBox>("_helperBoneList").Items.Count == 0, "Deleted groups remain in the inspector.");
        Require(!Field<Button>("_helperRemoveGroupButton").Enabled, "Deletion remains enabled for an empty graph.");
        Require(Field<Button>("_helperUndoButton").Enabled, "Cannot undo final group deletion.");
        for (var index = 0; index < count; index++) Call("UndoEditorChange");
        Require(service.CaptureState() == original, "Editor undo did not restore the original graph.");
        Require(Field<ListBox>("_helperBoneList").Items.Count > 0, "Undo did not restore the inspector.");
        for (var index = 0; index < count; index++) Call("RedoEditorChange");
        Require(service.GetSidecarDriverGroups().Count == 0, "Editor redo did not remove the groups.");
        Console.WriteLine($"PASS|editor|{Path.GetFileName(path)}|buttons, deletion, empty inspector, undo/redo");
    }
}
