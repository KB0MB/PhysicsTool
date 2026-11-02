using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;

internal static class BphclColliderDeletionChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static bool Enabled(uint mask, int slot) => (mask & (1u << Math.Min(slot, 30))) != 0;

    internal static void Synthetic()
    {
        var masks = Enumerable.Range(0, 32).Select(bit => 1u << bit).Concat([0u, uint.MaxValue, 0xaaaaaaaau, 0x55555555u]);
        foreach (var count in new[] { 1, 17, 30, 31, 32, 36, 64, 97 })
        foreach (var removed in Enumerable.Range(0, count).Select(slot => new HashSet<int> { slot })
            .Append(Enumerable.Range(0, count).Where(slot => slot % 3 == 0).ToHashSet())
            .Append(Enumerable.Range(0, count).ToHashSet()))
        {
            var kept = Enumerable.Range(0, count).Where(slot => !removed.Contains(slot)).ToArray();
            foreach (var mask in masks)
            {
                var actual = NativeBphclParticleEditor.RemapColliderMaskAfterDeletion(mask, kept);
                Check((actual & 0x80000000u) == (mask & 0x80000000u), "Deletion changed landscape collision");
                for (var slot = 0; slot < kept.Length; slot++)
                    Check(Enabled(actual, slot) == Enabled(mask, kept[slot]), $"Deletion changed collider eligibility at {kept[slot]} -> {slot}");
            }
        }
        Console.WriteLine("PASS BPHCL deletion masks: individual/shared boundary, multiple removals, empty result and landscape bit");
    }

    internal static void Run(string path, string output)
    {
        Synthetic(); Directory.CreateDirectory(output);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        var session = new EditorSession(); session.Load(path);
        var doc = session.Document;
        var native = doc.CaptureBphclNativeDocument()!;
        var cloth = native.Cloths.MaxBy(c => c.SimCloths[0].CollidableItemIndices.Count)!.Index;
        session.ClothIndex = cloth;
        var masks = new[] { 0u, uint.MaxValue, 1u, 1u << 29, 1u << 30, 1u << 31, 0xaaaaaaaau, 0x55555555u };
        session.Edit((d, c) => {
            var rows = d.GetParticleRows(c).ToArray();
            foreach (var row in rows) row.CollisionMask = masks[row.Index % masks.Length];
            d.UpdateParticleRows(c, rows);
        });
        var baseline = doc.CaptureState();
        var count = native.Cloths[cloth].SimCloths[0].CollidableItemIndices.Count;
        foreach (var slot in new[] { 0, 29, 30, 31, count - 1 }.Where(slot => slot < count).Distinct())
        {
            doc.RestoreState(baseline);
            DeleteAndCheck(session, slot);
            var result = doc.CaptureState();
            var saved = Path.Combine(output, $"deleted-slot-{slot}.bphcl"); doc.SaveHkcl(saved, HkclPlatform.Switch);
            var reload = new HkclService(); reload.Load(saved);
            Check(reload.GetParticleRows(cloth).Select(p => p.CollisionMask).SequenceEqual(doc.GetParticleRows(cloth).Select(p => p.CollisionMask)), "Masks changed on reopen");
            var preview = reload.GetTotkSimulationPreview(cloth);
            var solver = new HkclPreviewSimulator(preview);
            for (var frame = 0; frame < 120; frame++) solver.Step(1f / 60);
            Check(solver.NumericalResetCount == 0 && solver.GetPositions().Values.All(p => float.IsFinite(p.LengthSquared())), "Deleted-collider simulation became invalid");
            session.Undo(); Check(doc.CaptureState() == baseline, "Collider deletion undo failed");
            session.Redo(); Check(doc.CaptureState() == result, "Collider deletion redo failed");
        }
        doc.RestoreState(baseline);
        while (doc.CaptureBphclNativeDocument()!.Cloths[cloth].SimCloths[0].CollidableItemIndices.Count > 0)
            DeleteAndCheck(session, 0);
        var emptyPath = Path.Combine(output, "no-colliders.bphcl"); doc.SaveHkcl(emptyPath, HkclPlatform.Switch);
        var empty = new HkclService(); empty.Load(emptyPath);
        Check(empty.GetParticlePreview(cloth).Colliders.Count == 0 && empty.GetTotkSimulationSupport(cloth).Supported, "Final collider removal left invalid preview data");
        doc.RestoreState(baseline);
        var repeated = doc.CaptureBphclNativeDocument()!;
        var repeatedSim = repeated.Cloths[cloth].SimCloths[0];
        var bytes = repeated.Bytes.ToArray();
        var dataOffset = repeated.DataSection!.PayloadOffset;
        var references = repeated.GetReferenceArray(repeatedSim.DataOffset + 208).StorageItem;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(checked(dataOffset + (int)references.DataOffset + (count - 1) * 8)),
            (uint)repeatedSim.CollidableItemIndices[0]);
        foreach (var op in repeated.Cloths[cloth].Operators.Where(op => op.ClassName == "hclSimulateOperator"))
        {
            var configs = repeated.GetItem((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(checked(dataOffset + (int)op.DataOffset + 80))));
            for (var config = 0; config < configs.Count; config++)
            {
                var configOffset = checked(dataOffset + (int)configs.DataOffset + config * 48);
                var flagsItem = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(configOffset + 24));
                if (flagsItem == 0) continue;
                var flags = repeated.GetItem(flagsItem);
                for (var slot = 0; slot < flags.Count; slot++)
                    bytes[checked(dataOffset + (int)flags.DataOffset + slot)] = (byte)(slot % 2);
                bytes[configOffset + 42] = 0;
            }
        }
        var repeatedPath = Path.Combine(output, "repeated-reference-fixture.bphcl"); File.WriteAllBytes(repeatedPath, bytes);
        session.Load(repeatedPath); session.ClothIndex = cloth; doc = session.Document;
        DeleteAndCheck(session, 0);
        Check(doc.CaptureBphclNativeDocument()!.Cloths[cloth].SimCloths[0].CollidableItemIndices.Count == count - 2,
            "Repeated collider references were not removed together");
        Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Original BPHCL was changed");
        Ui(path, cloth);
        Console.WriteLine("PASS native BPHCL collider deletion: masks, attachments, pinch/config data, other cloths, save/reopen, undo/redo and simulation");
    }

    private static void DeleteAndCheck(EditorSession session, int removedSlot)
    {
        var doc = session.Document; var cloth = session.ClothIndex;
        var before = doc.CaptureBphclNativeDocument()!;
        var sim = before.Cloths[cloth].SimCloths[0];
        var item = sim.CollidableItemIndices[removedSlot];
        var kept = Enumerable.Range(0, sim.CollidableItemIndices.Count).Where(i => sim.CollidableItemIndices[i] != item).ToArray();
        var colliderIndex = before.Colliders.First(c => c.ItemIndex == item).Index;
        session.Edit((d, c) => d.DeleteCollider(c, colliderIndex));
        var after = doc.CaptureBphclNativeDocument()!;
        var actual = after.Cloths[cloth].SimCloths[0];
        Check(actual.CollidableItemIndices.SequenceEqual(kept.Select(i => sim.CollidableItemIndices[i])), "Collider reference order changed");
        Check(actual.CollidableTransformMap.TransformIndices.SequenceEqual(kept.Select(i => sim.CollidableTransformMap.TransformIndices[i])) &&
            actual.CollidableTransformMap.Offsets.SequenceEqual(kept.Select(i => sim.CollidableTransformMap.Offsets[i])), "Collider bone map lost alignment");
        Check(actual.Runtime.CollidablePinchingData.SequenceEqual(kept.Select(i => sim.Runtime.CollidablePinchingData[i])), "Pinch settings lost alignment");
        for (var p = 0; p < sim.StaticCollisionMasks.Count; p++)
        {
            Check((sim.StaticCollisionMasks[p] & 0x80000000u) == (actual.StaticCollisionMasks[p] & 0x80000000u), "Landscape bit changed");
            for (var slot = 0; slot < kept.Length; slot++)
                Check(Enabled(sim.StaticCollisionMasks[p], kept[slot]) == Enabled(actual.StaticCollisionMasks[p], slot), "Remaining collider eligibility changed");
        }
        var oldConfigs = before.Cloths[cloth].Operators.SelectMany(op => op.SimulateConfigs).ToArray();
        var newConfigs = after.Cloths[cloth].Operators.SelectMany(op => op.SimulateConfigs).ToArray();
        for (var i = 0; i < oldConfigs.Length; i++)
            Check(newConfigs[i].UseAllInstanceCollidables == oldConfigs[i].UseAllInstanceCollidables &&
                newConfigs[i].InstanceCollidablesUsed.SequenceEqual(kept.Select((old, index) => (old, index))
                    .Where(pair => oldConfigs[i].InstanceCollidablesUsed.Contains(pair.old)).Select(pair => pair.index)), "Config selection lost alignment");
        for (var other = 0; other < before.Cloths.Count; other++)
            if (other != cloth) Check(JsonSerializer.Serialize(before.Cloths[other]) == JsonSerializer.Serialize(after.Cloths[other]), "Deletion changed another cloth");
    }

    private static void Ui(string path, int cloth)
    {
        var window = new MainWindow(graphics: false); window.Show();
        try
        {
            window.LoadPath(path); window.Session.ClothIndex = cloth; window.ShowEditor(); window.Refresh(); Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<TabStrip>().Single().SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.SelectionMode.HasFlag(SelectionMode.Multiple));
            var count = list.ItemCount; list.SelectedIndex = 0; list.Focus();
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
            var confirm = window.OwnedWindows.Single();
            confirm.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
            Check(window.OwnedWindows.Count == 0 && list.ItemCount == count - 1, "UI collider deletion failed or opened an error dialog");
            window.ToggleSimulation(); Check(window.EditorSimulating, "Simulation did not start after collider deletion"); window.StopSimulation();
            window.Session.Undo(); window.Refresh(); Dispatcher.UIThread.RunJobs();
            Check(window.Session.Document.GetColliderRows(cloth).Count == count, "UI undo did not restore collider");
            Console.WriteLine("PASS editor collider Delete/Enter, simulation after deletion and undo");
        }
        finally { window.Session.Clear(); window.Close(); }
    }
}
