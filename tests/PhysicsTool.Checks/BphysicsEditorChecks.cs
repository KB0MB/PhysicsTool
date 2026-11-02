using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using static HKCLTool.BotwAampWriter;

internal static class BphysicsEditorChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Tree(AampReadList list) => $"L{list.Hash}[{string.Join(';', list.Objects.Select(o => $"O{o.Hash}[{string.Join(';', o.Parameters.Select(p => $"{p.Hash}:{p.Type}:{Convert.ToHexString(p.Bytes)}"))}]"))}][{string.Join(';', list.Children.Select(Tree))}]";

    internal static void Native(string directory, string output)
    {
        Directory.CreateDirectory(output);
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.sbactorpack"))
        {
            var pack = BotwSarcArchive.Open(path);
            foreach (var entry in pack.Entries.Where(e => e.Name.EndsWith(".bphysics")))
            {
                var bytes = pack.Read(entry).ToArray();
                var document = BphysicsEditingDocument.Parse(bytes);
                var before = BotwAampReader.Read(bytes).Root.FindChild("ParamSet")!;
                foreach (var (profile, i) in document.Settings.Cloths.Select((p, i) => (p, i)).ToArray())
                    document.Settings.Cloths[i] = profile with { WindDrag = profile.WindDrag * .5f };
                var after = BotwAampReader.Read(document.Snapshot()).Root.FindChild("ParamSet")!;
                foreach (var child in before.Children.Where(c => c.Hash != Crc32("Cloth")))
                    Check(Tree(child) == Tree(after.Children.Single(c => c.Hash == child.Hash)), "Native unrelated section changed: " + entry.Name);
                foreach (var node in before.Objects.Where(o => o.Hash != 1258832850 && o.Hash != Crc32("SupportBone")))
                {
                    var other = after.Objects.Single(o => o.Hash == node.Hash);
                    Check(node.Parameters.Select(p => (p.Hash, p.Type, Convert.ToHexString(p.Bytes))).SequenceEqual(other.Parameters.Select(p => (p.Hash, p.Type, Convert.ToHexString(p.Bytes)))), "Native unrelated object changed");
                }
                Check(document.Snapshot().AsSpan().SequenceEqual(BphysicsEditingDocument.Parse(document.Snapshot()).Snapshot()), "Native snapshot not stable");
                count++;
            }
        }
        Console.WriteLine($"PASS {count} native BPHYSICS files: editable tree round-trip and unrelated section preservation");
    }

    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var settings = new BphysicsDocument { HkclPath = "Test/Test.hkcl", UseSupportBone = true, SupportBonePath = "Test/Test.bphyssb" };
        settings.Cloths.Add(new("First", "Head")); settings.Cloths.Add(new("Second", "Waist"));
        var root = BphysicsService.BuildRoot(settings);
        var set = root.Children[0];
        var flags = set.Objects[0];
        var ragdollFlag = flags.Parameters.FindIndex(p => p.Name == "use_ragdoll"); flags.Parameters[ragdollFlag] = AampParameterNode.Boolean("use_ragdoll", true);
        var rigid = new AampListNode("RigidBodySet");
        var a = new AampListNode("BodyA"); var b = new AampListNode("BodyB");
        a.Children.Add(new("ShapeA")); b.Children.Add(new("ShapeB")); rigid.Children.AddRange([a, b]); set.Children.Add(rigid);
        var ragdoll = new AampObjectNode("Ragdoll"); ragdoll.Parameters.Add(AampParameterNode.String256Value("ragdoll_setup_file_path", "Keep/Keep.hkrg")); set.Objects.Add(ragdoll);
        set.Children[0].Objects[2].Parameters.Add(AampParameterNode.Integer("unknown_setting", 137));
        set.Children[0].Objects[3].Parameters.Add(AampParameterNode.Integer("unknown_setting", 246));
        var source = WriteXml(root); var path = Path.Combine(output, "editor.bphysics"); File.WriteAllBytes(path, source);
        var doc = BphysicsEditingDocument.Load(path);
        var original = Tree(BotwAampReader.Read(source).Root.FindChild("ParamSet")!.FindChild("RigidBodySet")!);
        doc.Settings.Cloths[0] = doc.Settings.Cloths[0] with { Name = "Link:Renamed", BaseBone = "Link:Head", WindDrag = .48f };
        doc.MoveSlot(0, 1); var duplicate = doc.AddSlot(1); doc.RemoveSlot(0);
        doc.Settings.UseCloth = false; doc.Settings.UseSupportBone = false;
        doc.Save(path);
        var read = BphysicsService.Load(path);
        Check(read.Cloths.Count == 2 && read.Cloths[0].Name == "Link:Renamed" && read.Cloths[0].BaseBone == "Link:Head" && read.Cloths[0].WindDrag == .48f,
            "Edited slot values or literal bindings lost");
        Check(!read.UseCloth && !read.UseSupportBone && read.SupportBonePath == settings.SupportBonePath, "Inactive settings were discarded");
        var parsed = BotwAampReader.Read(path).Root.FindChild("ParamSet")!;
        Check(Tree(parsed.FindChild("RigidBodySet")!) == original, "Nested sibling sections were corrupted");
        Check(parsed.Objects.Single(o => o.Hash == 1258832850).Find("use_ragdoll")!.AsBoolean(), "Ragdoll flag reset");
        Check(parsed.Objects.Single(o => o.Hash == Crc32("Ragdoll")).Find("ragdoll_setup_file_path")!.AsString() == "Keep/Keep.hkrg", "Ragdoll link lost");
        Check(parsed.FindChild("Cloth")!.Objects.Where(o => o.Find("name") != null).All(o => o.Find("unknown_setting")!.AsInt32() == 137), "Slot metadata lost on rename/move/copy/delete");
        var saved = File.ReadAllBytes(path); doc.Settings.Cloths[0] = doc.Settings.Cloths[0] with { WindDrag = float.NaN };
        try { doc.Save(path); throw new Exception("Invalid data saved"); } catch (InvalidOperationException) { }
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(saved), "Invalid save overwrote file");

        var window = new MainWindow(graphics: false) { Width = 1100, Height = 800 }; window.Show();
        window.Session.New("KeepMyCloth", 0); var editorState = window.Session.Document.CaptureState();
        window.LoadPath(path); Dispatcher.UIThread.RunJobs();
        var view = window.BphysicsWorkspace!;
        Check(view != null && window.IsToolWorkspaceVisible && MainWindow.IsPhysicsFilePath(path), "Open/drop routing missing");
        T Find<T>(string name) where T : Control => view!.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        Find<NumericUpDown>("BphysicsDrag").Value = .75m;
        Check(view!.Dirty && view.ApplyDraft() && view.Document.Settings.Cloths[0].WindDrag == .75f, "UI apply/dirty failure");
        view.Undo(); Check(view.Document.Settings.Cloths[0].WindDrag == .48f, "UI undo failed");
        view.Redo(); Check(view.Document.Settings.Cloths[0].WindDrag == .75f, "UI redo failed");
        Find<TextBox>("BphysicsSlotName").Text = "";
        Dispatcher.UIThread.RunJobs();
        Check(!view.ApplyDraft() && view.Dirty, "Invalid name accepted/lost draft"); view.Undo();
        Find<NumericUpDown>("BphysicsDrag").Value = .85m;
        Find<ListBox>("BphysicsSlots").SelectedIndex = 1;
        Check(view.Document.Settings.Cloths[0].WindDrag == .85f, "Switching slots lost pending edits");
        view.SaveAsync(false).GetAwaiter().GetResult(); Check(!view.Dirty, "Save kept dirty flag");
        Find<TabControl>("BphysicsTabs").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        using (var shot = window.CaptureRenderedFrame()!) shot.Save(Path.Combine(output, "bphysics-shared.png"));
        Find<TabControl>("BphysicsTabs").SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        using (var shot = window.CaptureRenderedFrame()!) shot.Save(Path.Combine(output, "bphysics-slots.png"));
        var back = window.GetVisualDescendants().OfType<Button>().Single(bu => bu.Name == "WorkspaceBack");
        back.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Check(!window.IsToolWorkspaceVisible && window.Session.Document.CaptureState() == editorState, "Back lost original cloth workspace");
        window.OpenDroppedPhysicsAsync(path).GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        Check(window.BphysicsWorkspace != null && window.IsToolWorkspaceVisible, "BPHYSICS drop did not open editor");
        window.Width = 900; window.Height = 640; Dispatcher.UIThread.RunJobs();
        using (var shot = window.CaptureRenderedFrame()!) shot.Save(Path.Combine(output, "bphysics-compact.png"));
        var next = window.BphysicsWorkspace!;
        next.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "BphysicsSlotName").Text = "Unsaved";
        var closing = next.CanCloseAsync(); Dispatcher.UIThread.RunJobs();
        Check(!closing.IsCompleted && window.OwnedWindows.Count == 1, "Dirty draft did not prompt before closing");
        window.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(bu => Equals(bu.Content, "Cancel"))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Check(!closing.GetAwaiter().GetResult() && next.Dirty, "Cancel discarded pending edits");
        next.Undo();
        File.AppendAllText(path, "external edit");
        try { next.SaveAsync(false).GetAwaiter().GetResult(); throw new Exception("External change overwritten"); }
        catch (InvalidOperationException) { }
        Check(File.ReadAllText(path).EndsWith("external edit"), "External changes lost");
        window.Session.Clear(); window.Close();
        Console.WriteLine("PASS BPHYSICS editor: slot operations, source preservation, nested AAMP siblings, validation, atomic save, UI apply/undo/redo/navigation and snapshots");
    }
}
