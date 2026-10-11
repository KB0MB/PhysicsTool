using System.Numerics;
using HKCLTool;
using static HKCLTool.BotwAampWriter;

internal static class HelperConnectionChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string path, string output)
    {
        var doc = new HkclService(); doc.Load(path);
        var original = doc.CaptureState(); var totk = doc.IsBphhb;
        var group = doc.GetSidecarDriverGroups().First(g => g.DrivenBoneIndices.Count > 0 && doc.GetHelperCurves(g.Index).Count > 0);
        var bone = group.DrivenBoneIndices.First();
        var before = doc.GetSidecarDriverGroups().SelectMany(g => g.DrivenBoneIndices).Distinct()
            .ToDictionary(i => i, i => doc.GetHelperConnections(i).ToArray());
        var keys = new[] { new Vector4(-1, -.5f, .5f, .5f), new Vector4(0, 0, .5f, .5f), new Vector4(1, .5f, .5f, .5f) };
        var curve = doc.CreateHelperCurve(group.Index, group.DriverIndices[0], 3, keys);
        var source = totk ? curve : 65536 | curve;
        Require(doc.GetHelperCurves(group.Index).Single(c => c.Index == curve).Keys.SequenceEqual(keys), "New native curve keys lost");
        doc.SetHelperConnection(bone, 3, source, source, totk ? 1 : -.5f, totk ? 1 : .25f);
        var row = doc.GetHelperConnections(bone)[3];
        Require(row.First == source && row.Second == source && row.FirstWeight == (totk ? 1 : -.5f), "New two-response output was not connected");
        foreach (var (other, connections) in before.Where(p => p.Key != bone))
            Require(doc.GetHelperConnections(other).SequenceEqual(connections), "Rewiring a helper changed another helper's shared output");
        var state = doc.CaptureState();
        void Reject(Action operation)
        {
            try { operation(); throw new Exception("Invalid helper authoring operation accepted"); }
            catch (ArgumentException) { }
            Require(doc.CaptureState() == state, "Invalid authoring operation changed native data");
        }
        Reject(() => doc.SetHelperConnection(bone, 7, source, -1));
        Reject(() => doc.SetHelperConnection(bone, 1, int.MaxValue, -1));
        Reject(() => doc.CreateHelperCurve(group.Index, group.DriverIndices[0], 0, [new(float.NaN), new(0), new(1)]));
        var saved = Path.Combine(output, "helper-connections." + (totk ? "bphhb" : "bphyssb")); doc.SaveHkcl(saved, HkclPlatform.WiiU);
        var reloaded = new HkclService(); reloaded.Load(saved);
        Require(reloaded.GetHelperConnections(bone).SequenceEqual(doc.GetHelperConnections(bone)), "Helper connections did not survive native save/reload");
        for (var channel = 0; channel < 6; channel++) doc.SetHelperConnection(bone, channel, -1, -1);
        Require(doc.GetHelperConnections(bone).All(r => r.First == -1 && r.Second == -1), "Disconnect left an active output");
        Require(doc.GetSidecarDriverGroups().Any(g => g.DrivenBoneIndices.Contains(bone)), "Disconnected helper disappeared from the editor");
        doc.SetHelperConnection(bone, 3, source, -1);
        doc.DeleteHelper(bone);
        Require(!doc.GetSidecarDriverGroups().Any(g => g.DrivenBoneIndices.Contains(bone)), "Individual helper was not removed");
        Require(doc.GetSidecarBoneRows().Count == reloaded.GetSidecarBoneRows().Count, "Deleting helper behavior deleted model bone references");
        foreach (var (other, connections) in before.Where(p => p.Key != bone))
            Require(doc.GetHelperConnections(other).SequenceEqual(connections), "Individual deletion changed a surviving helper");
        doc.SaveHkcl(saved, HkclPlatform.WiiU); reloaded.Load(saved);
        Require(!reloaded.GetSidecarDriverGroups().Any(g => g.DrivenBoneIndices.Contains(bone)), "Deleted helper returned after save");
        doc.RestoreState(original);
        if (totk) CheckReorderedAndShared(doc, path, output);
        Console.WriteLine($"PASS: {(totk ? "TotK" : "BotW")} new curves, two-input connections, disconnect/reconnect, independent deletion and native reload.");
    }
    private static void CheckReorderedAndShared(HkclService doc, string path, string output)
    {
        var document = BphhbBridge.LoadBytes(File.ReadAllBytes(path), path);
        var originalRows = doc.GetSidecarBoneRows().ToDictionary(b => b.Index);
        var edited = BphhbBridge.EditConnections(document, data => {
            var poses = SidecarConnectionEditing.List(data, "pose_driven_list");
            poses.Objects.Reverse();
            foreach (var record in SidecarConnectionEditing.List(data, "driven_bone_list").Objects)
                foreach (var channel in new[] { "translate", "rotate", "aim" })
                {
                    var hash = Crc32(channel + "_driven_id"); var id = SidecarConnectionEditing.Int(record, hash);
                    if (id >= 0) SidecarConnectionEditing.Set(record, hash, poses.Objects.Count - 1 - id);
                }
        });
        doc.RestoreState("BPHHB:" + Convert.ToBase64String(edited.Bytes));
        foreach (var b in doc.GetSidecarBoneRows())
        {
            var old = originalRows[b.Index];
            Require(b.X == old.X && b.Y == old.Y && b.Z == old.Z && b.RotationW == old.RotationW, "Inspector ignored explicit reordered pose reference");
        }
        var group = doc.GetSidecarDriverGroups().First(g => g.DrivenBoneIndices.Count >= 2);
        var bone = group.DrivenBoneIndices[0]; var other = group.DrivenBoneIndices[1];
        var prior = doc.CaptureState();
        var converted = Path.Combine(output, "helper-reordered-converted.bphyssb");
        BphyssbBridge.Export(edited, converted);
        var botw = new HkclService(); botw.Load(converted);
        foreach (var b in botw.GetSidecarBoneRows())
        {
            var old = originalRows.Values.Single(r => r.Name == b.Name);
            Require(b.X == old.X && b.Y == old.Y && b.Z == old.Z && b.RotationW == old.RotationW, "TotK conversion ignored explicit pose reference");
        }
        doc.DuplicateHelperBone(bone); Require(doc.GetSidecarBoneRows().Count == originalRows.Count + 1, "Duplicate rejected explicit reordered pose"); doc.RestoreState(prior);
        var otherRows = doc.GetSidecarBoneRows().ToDictionary(b => b.Index, b => (b.X, b.Y, b.Z, b.RotationW));
        var target = doc.GetSidecarBoneRows().Single(b => b.Index == bone); target.X += .25f;
        doc.UpdateBoneRows(group.Index, [target]);
        Require(doc.GetSidecarBoneRows().Single(b => b.Index == bone).X == target.X, "Reordered pose edit changed wrong record");
        foreach (var b in doc.GetSidecarBoneRows().Where(b => b.Index != bone)) Require((b.X, b.Y, b.Z, b.RotationW) == otherRows[b.Index], "Pose edit changed another bone");
        doc.RestoreState(prior);
        edited = BphhbBridge.EditConnections(edited, data => {
            var driven = SidecarConnectionEditing.List(data, "driven_bone_list");
            var first = driven.Objects.Single(o => SidecarConnectionEditing.Int(o, 3407736066) == bone);
            var second = driven.Objects.Single(o => SidecarConnectionEditing.Int(o, 3407736066) == other);
            var pose = SidecarConnectionEditing.SinglePose(first, SidecarConnectionEditing.List(data, "pose_driven_list").Objects.Count);
            foreach (var channel in new[] { "translate", "rotate", "aim" })
                if (SidecarConnectionEditing.Int(second, Crc32(channel + "_driven_id")) >= 0)
                    SidecarConnectionEditing.Set(second, Crc32(channel + "_driven_id"), pose);
        });
        doc.RestoreState("BPHHB:" + Convert.ToBase64String(edited.Bytes));
        var shared = doc.GetHelperConnections(other).ToArray();
        doc.SetHelperConnection(bone, 0, -1, -1);
        Require(doc.GetHelperConnections(other).SequenceEqual(shared), "Rewiring did not isolate a shared pose");
        doc.DeleteHelper(bone);
        Require(doc.GetHelperConnections(other).SequenceEqual(shared), "Deleting helper damaged a shared pose or its remapped reference");
        var summary = BphhbBridge.LoadBytes(Convert.FromBase64String(doc.CaptureState()[6..]), path);
        BphyssbBridge.Export(summary, converted); botw.Load(converted);
        Require(!botw.GetSidecarDriverGroups().SelectMany(g => g.DrivenBoneIndices).Any(i => botw.GetSidecarBoneRows()[i].Name == originalRows[bone].Name), "Deleted helper returned after conversion");
        summary = BphhbBridge.EditConnections(summary, data => {
            var record = SidecarConnectionEditing.List(data, "driven_bone_list").Objects.Single(o => SidecarConnectionEditing.Int(o, 3407736066) == other);
            var pose = SidecarConnectionEditing.SinglePose(record, SidecarConnectionEditing.List(data, "pose_driven_list").Objects.Count);
            SidecarConnectionEditing.Set(record, Crc32("rotate_driven_id"), pose);
            SidecarConnectionEditing.Set(record, Crc32("rotate_driven_type"), 0);
            SidecarConnectionEditing.Set(record, Crc32("translate_driven_id"), -1);
            SidecarConnectionEditing.Set(record, Crc32("translate_driven_type"), -1);
        });
        doc.RestoreState("BPHHB:" + Convert.ToBase64String(summary.Bytes));
        var connectedGroup = doc.GetSidecarDriverGroups().First(g => g.DrivenBoneIndices.Contains(other));
        var response = doc.GetHelperCurves(connectedGroup.Index).First().Index;
        doc.SetHelperConnection(other, 3, response, -1);
        summary = BphhbBridge.LoadBytes(Convert.FromBase64String(doc.CaptureState()[6..]), path);
        var enabled = summary.Archive!.Root.Children.Single().FindChild("driven_bone_list")!.Objects.Single(o => o.Find(3407736066)?.AsInt32() == other);
        Require(enabled.Find("translate_driven_id")!.AsInt32() >= 0 && enabled.Find("translate_driven_type")!.AsInt32() == 0, "New translation response did not enable native translation binding");
        Console.WriteLine("PASS: reordered and shared TotK pose references survive inspect/edit/duplicate/rewire/delete.");
    }
}
