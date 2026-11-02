using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Core;
using PhysicsTool.Desktop;

internal static class BphscChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or OverflowException) { return; }
        throw new Exception("Invalid edit was accepted.");
    }
    internal static void Run(string output, string? dump)
    {
        Directory.CreateDirectory(output);
        var minimal = new byte[19984]; "Phive\0"u8.CopyTo(minimal);
        void Put(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(minimal.AsSpan(offset), value);
        minimal[6] = 2; minimal[8] = 255; minimal[9] = 254; minimal[10] = 2; minimal[11] = 96;
        Put(12, 784); Put(396, 19120); Put(40, 19904); Put(424, 80);
        var empty = BphscDocument.Parse(minimal);
        Require(empty.Bodies.Count == 0 && empty.Snapshot().SequenceEqual(minimal), "Empty template round trip");
        Reject(() => BphscDocument.Parse(minimal[..100]));
        var malformed = (byte[])minimal.Clone(); malformed[6] = 3; Reject(() => BphscDocument.Parse(malformed));
        malformed = (byte[])minimal.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(40), 1000); Reject(() => BphscDocument.Parse(malformed));
        empty.SetName("TestCompound"); Reject(() => empty.SetName(new string('X', 128)));
        empty.SaveCopy(Path.Combine(output, "synthetic.bphsc.zs"), Path.Combine(output, "original.bphsc"));
        Require(BphscDocument.Load(Path.Combine(output, "synthetic.bphsc.zs")).Name == "TestCompound", "Compressed round trip");
        Reject(() => empty.SaveCopy(Path.Combine(output, "original.bphsc"), Path.Combine(output, "original.bphsc")));
        if (dump == null) { Console.WriteLine("BPHSC synthetic checks passed."); return; }

        var paths = Directory.GetFiles(dump, "*.bphsc*").Order().ToArray();
        Require(paths.Length > 0, "No BPHSC files found");
        foreach (string path in paths)
        {
            var sourceHash = SHA256.HashData(File.ReadAllBytes(path));
            var document = BphscDocument.Load(path);
            var original = document.Snapshot();
            Require(BphscDocument.Parse(original).Snapshot().SequenceEqual(original), "Unchanged round trip: " + path);
            var geometry = document.Sections.Where(s => s.Index < document.CompoundCount).Select(s => document.ExtractSection(s.Index)).ToArray();
            foreach (var body in document.Bodies.ToArray())
            {
                document.SetBody(body.Index, body.Position + new Vector3(1, 2, 3), body.Rotation);
                document.SetBody(body.Index, body.Position, body.Rotation);
            }
            Require(document.Snapshot().SequenceEqual(original), "Transform restore changed opaque bytes: " + path);
            foreach (var water in document.Water.ToArray())
                document.SetWater(water.Index, water.Position + Vector3.UnitY, water.Rotation, water.Size, water.Kind == "Waterfall" ? 0 : water.FlowSpeed, water.FluidType);
            foreach (var body in document.Bodies.Where(b => b.PlacementGroupId != 0 && b.GroupId != 0).ToArray())
            {
                var before = document.Snapshot();
                Reject(() => document.SetGroup(body.Index, 0, body.PlacementGroupId));
                Require(document.Snapshot().SequenceEqual(before), "Rejected group edit mutated data");
                document.SetGroup(body.Index, 60000, 0xfedcba9876543210);
                Require(document.ActorLinks.Where(l => l.BodyIndex == body.Index).All(l => l.GroupId == 60000), "Group link not synchronized");
                document.SetGroup(body.Index, body.GroupId, body.PlacementGroupId);
            }
            if (document.ActorLinks.Count > 0)
            {
                const ulong hash = 0xf0123456789abcde;
                document.SetActorHash(0, hash);
                var bytes = document.Snapshot();
                int hashAt = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20));
                int indexAt = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24));
                int capacity = document.ActorLinks.Count * 2;
                foreach (var link in document.ActorLinks)
                {
                    int slot = (int)(link.Hash % (ulong)capacity), attempts = 0;
                    while (BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(hashAt + slot * 8)) != link.Hash && ++attempts < capacity) slot = (slot + 1) % capacity;
                    Require(attempts < capacity && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(indexAt + slot * 2)) == link.Index, "Hash lookup mismatch");
                }
                var before = document.Snapshot();
                Reject(() => document.SetActorHash(0, 0));
                if (document.ActorLinks.Count > 1) Reject(() => document.SetActorHash(1, hash));
                Require(document.Snapshot().SequenceEqual(before), "Rejected hash edit mutated data");
            }
            for (int i = 0; i < geometry.Length; i++) Require(geometry[i].SequenceEqual(document.ExtractSection(i)), "Embedded shape changed");
            if (document.Bodies.Count > 0)
            {
                var before = document.Snapshot();
                Reject(() => document.SetBody(document.Bodies[0].Index, new(float.NaN, 0, 0), Vector3.Zero));
                Require(document.Snapshot().SequenceEqual(before), "Rejected transform mutated data");
            }
            Require(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Source was modified");
        }
        var example = paths.FirstOrDefault(p => Path.GetFileName(p).StartsWith("Dungeon004.")) ?? paths[0];
        var edited = BphscDocument.Load(example); edited.SetName("DungeonTest");
        string romfs = Path.GetFullPath(Path.Combine(dump, "..", "..", ".."));
        int checkedControls = 0;
        foreach (string path in paths)
        {
            string shrine = Path.GetFileName(path).Split('.')[0];
            string banc = Path.Combine(romfs, "Banc", "SmallDungeon", shrine + "_Static.bcett.byml.zs");
            if (!File.Exists(banc)) continue;
            var controls = BphscBancCheck.Read(banc); checkedControls += controls.Count;
            var report = BphscBancCheck.Report(BphscDocument.Load(path), controls);
            Require(report.Split("OK: linked to body ").Length - 1 == controls.Count, "Native GroundControl mismatch: " + report);
        }
        Console.WriteLine($"GroundControl: {checkedControls} native controller bindings matched.");
        var exampleBanc = Path.Combine(romfs, "Banc", "SmallDungeon", "Dungeon004_Static.bcett.byml.zs");
        if (File.Exists(exampleBanc))
        {
            var controls = BphscBancCheck.Read(exampleBanc);
            var inconsistent = BphscDocument.Load(example);
            var body = inconsistent.Bodies.First(b => b.PlacementGroupId != 0);
            inconsistent.SetGroup(body.Index, 60000, 0xfedcba9876543210);
            Require(BphscBancCheck.Report(inconsistent, controls).Contains("found 0"), "Missing group not diagnosed");
        }
        foreach (string ext in new[] { ".bphsc", ".bphsc.zs" })
        {
            var path = Path.Combine(output, "edited" + ext); edited.SaveCopy(path, example);
            Require(BphscDocument.Load(path).Snapshot().SequenceEqual(edited.Snapshot()), "Edited save round trip");
        }
        var window = new BphscWindow(); window.Show(); Await(window.LoadPathAsync(example));
        var snapshot = window.Document!.Snapshot();
        window.Edit(d => d.SetName("UI_Test")); window.Undo(); Require(window.Document!.Snapshot().SequenceEqual(snapshot), "UI undo");
        window.Redo(); Require(window.Document!.Name == "UI_Test", "UI redo"); window.Undo();
        Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()!.Save(Path.Combine(output, "bodies.png"));
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        for (int i = 1; i < 4; i++) { tabs.SelectedIndex = i; Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"tab-{i}.png")); }
        window.Width = 820; window.Height = 580; tabs.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "compact.png"));
        var waterPath = paths.FirstOrDefault(p => Path.GetFileName(p).StartsWith("Dungeon000."));
        if (waterPath != null)
        {
            Await(window.LoadPathAsync(waterPath)); tabs.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, "water.png"));
        }
        window.Close();
        Console.WriteLine($"BPHSC: {paths.Length} native files passed parsing, byte preservation, edits, geometry preservation, source checks; saves and UI undo/redo passed.");
    }
    private static void Await(Task task)
    {
        var until = DateTime.UtcNow.AddMinutes(2);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult();
    }
}
