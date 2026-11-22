using System.Numerics;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BymlLibrary;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using Revrs;

internal static class MapPlacementOnlyChecks
{
    internal static void Run(string output, string? nativeRoot)
    {
        string root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "Banc/NormalStage/Test_Static.bcett.byml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Byml.FromText("""
            Actors:
            - {Gyaml: Test, Hash: !ul 1, Translate: [1.0, 2.0, 3.0], Dynamic: {Unknown: KeepMe}}
            - {Gyaml: Linked, Hash: !ul 2, Links: [{Dst: !ul 3, Name: Reference}]}
            - {Gyaml: Target, Hash: !ul 3}
            - {Gyaml: RailActor, Hash: !ul 4, Rails: []}
            - {Gyaml: GroundControlActor_SimpleMoveTo, Hash: !ul 5, Phive: {Placement: {GroupID: 8, GroupGUID: !ul 77}}}
            - {Gyaml: FldObj_Tar_MergeParts_A_01, Hash: !ul 6}
            UnknownRoot: {Keep: 57}
            """).ToBinary(Endianness.Little, 7));
        string companion = path.Replace("_Static", "_Dynamic");
        File.WriteAllBytes(companion, Byml.FromText("Rails: []").ToBinary(Endianness.Little, 7));
        var original = File.ReadAllBytes(path); var paired = File.ReadAllBytes(companion);
        var assets = ShrineSceneAssets.Load(path, root);
        var mesh = new VisualModelMesh { Name = "0:Test/mesh", Vertices = [1,2,3,0,1,0,0,0, 2,2,3,0,1,0,1,0, 1,3,3,0,1,0,0,1], Indices = [0,1,2] };
        assets.Model.Meshes.Add(mesh); assets.Model.TriangleCount++;
        var editor = new ShrinePlacementEditor(assets, root);
        Require(editor.EditRestriction(0) != null, "Unconfirmed missing-collision edit enabled");
        editor.SetPlacementOnly(true);
        Require(editor.EditRestriction(0) == null, "Placement-only transform still blocked");
        foreach (int blocked in new[] { 3,4,5 }) Require(editor.EditRestriction(blocked) != null, "Protected placement became editable");
        Require(editor.PopulationRestriction(1, true) != null && editor.PopulationRestriction(2, false) != null, "Linked population edit became enabled");
        Require(editor.GroundControlParameterRestriction(4) != null, "GroundControl ignored edit mode");
        var target = new ShrineTransform(new(8,9,10), new(.1f,.2f,.3f), new(1.2f));
        editor.SetTransform(0, target);
        Require(editor.Scene.Actors[0].LocalPosition == target.Position && !editor.Model.Meshes[0].Vertices.SequenceEqual(mesh.Vertices), "Placement/model did not move");
        try { editor.SetPlacementOnly(false); throw new Exception("Mode changed with editing history"); } catch (InvalidOperationException) { }
        editor.Undo(); Require(editor.Scene.Actors[0].LocalPosition == new Vector3(1,2,3), "Undo changed rest placement");
        editor.Redo(); Require(editor.Scene.Actors[0].LocalPosition == target.Position, "Redo lost transform");
        int copy = editor.Duplicate([0]).Single(); ulong hash = editor.Scene.Actors[copy].Hash;
        Require(hash != 1 && editor.Scene.Actors[copy].LocalPosition == target.Position, "Copy lost transform/identity");
        editor.Delete([copy]); editor.Undo();
        Require(editor.Scene.Actors.Last().Hash == hash, "Undo deletion lost copy");
        editor.Redo(); editor.Delete([0]); editor.Undo();
        string saved = editor.Save(output);
        string savedPath = Path.Combine(saved, "romfs", Path.GetRelativePath(root, path));
        var reopened = ShrineSceneDocument.Load(savedPath);
        Require(ShrineTransform.Local(reopened.Actors[0] with { LocalPosition = reopened.Actors[0].Position }) == target, "Saved transform drifted");
        var changed = Byml.FromBinary(File.ReadAllBytes(savedPath));
        var source = Byml.FromBinary(original);
        var changedActor = changed.GetMap()["Actors"].GetArray()[0].GetMap();
        var sourceActor = source.GetMap()["Actors"].GetArray()[0].GetMap();
        foreach (string field in new[] { "Translate", "Rotate", "Scale" })
            if (sourceActor.TryGetValue(field, out var value)) changedActor[field] = value; else changedActor.Remove(field);
        Require(changed.ToBinary(Endianness.Little, 7).SequenceEqual(source.ToBinary(Endianness.Little, 7)), "Unknown BANC data changed");
        Require(File.ReadAllBytes(Path.Combine(saved, "romfs", Path.GetRelativePath(root, companion))).SequenceEqual(paired), "Empty companion was altered");
        Require(!Directory.Exists(Path.Combine(saved, "romfs/Phive")), "Placement-only save wrote collision");
        Require(File.ReadAllBytes(path).SequenceEqual(original), "Original BANC changed");
        var adding = new ShrinePlacementEditor(assets, root); adding.SetPlacementOnly(true);
        var actorModel = new VisualModelData(); actorModel.Meshes.Add(mesh);
        int addedStatic = adding.AddActor("NewActor", path, new(20,30,40), actorModel);
        int addedDynamic = adding.AddActor("OtherActor", companion, new(50,60,70), new());
        ulong staticHash = adding.Scene.Actors[addedStatic].Hash, dynamicHash = adding.Scene.Actors[addedDynamic].Hash;
        Require(adding.EditRestriction(addedStatic) == null && adding.PopulationRestriction(addedStatic, true) == null, "New actor editing blocked");
        adding.Move(addedStatic, new(25,35,45));
        int newCopy = adding.Duplicate([addedStatic]).Single();
        Require(adding.Scene.Actors[newCopy].LocalPosition == new Vector3(25,35,45), "New actor duplicate lost its position");
        adding.Delete([newCopy]); adding.Undo(); adding.Redo();
        adding.Delete([addedDynamic]); adding.Undo();
        Require(adding.Scene.Actors.Any(a => a.Hash == dynamicHash), "Undo lost added dynamic actor");
        string addedOutput = adding.Save(output);
        var staticResult = ShrineSceneDocument.Load(Path.Combine(addedOutput, "romfs", Path.GetRelativePath(root,path)));
        var dynamicResult = ShrineSceneDocument.Load(Path.Combine(addedOutput, "romfs", Path.GetRelativePath(root,companion)));
        Require(staticResult.Actors.Single(a=>a.Hash==staticHash).Position == new Vector3(25,35,45), "Added Static actor did not roundtrip");
        Require(dynamicResult.Actors.Single().Hash == dynamicHash && dynamicResult.Actors.Single().Name == "OtherActor", "Added Dynamic actor did not roundtrip");
        Require(!Directory.Exists(Path.Combine(addedOutput,"romfs/Phive")), "Adding actors wrote collision");
        Require(File.ReadAllBytes(path).SequenceEqual(original) && File.ReadAllBytes(companion).SequenceEqual(paired), "Add changed source files");
        Console.WriteLine("PASS new Static/Dynamic actors, unique IDs, transforms, duplicate/delete, undo/redo and save/reopen without collision output.");
        Ui(path, output);
        if (nativeRoot != null)
        {
            string nativePath = Path.Combine(nativeRoot, "Banc/MainField/F-4_Static.bcett.byml.zs");
            byte[] fingerprint = SHA256.HashData(File.ReadAllBytes(nativePath));
            var scene = ShrineSceneDocument.Load(nativePath, [nativeRoot]);
            var actors = scene.Actors.Select(a => a with { SourceScene = nativePath, SourceIndex = a.Index, LocalPosition = a.Position, WorldTransform = ShrineSceneAssets.Placement(a) }).ToList();
            var native = new ShrineSceneAssets(new(actors), new VisualModelData { SourcePath = nativePath }, null, null, [], 0);
            var edit = new ShrinePlacementEditor(native, nativeRoot); edit.SetPlacementOnly(true);
            var actor = edit.Scene.Actors.First(a => a.MergedPath.Length == 0 && edit.EditRestriction(a.Index) == null);
            edit.Move(actor.Index, actor.LocalPosition + Vector3.UnitY);
            string result = edit.Save(output);
            var check = ShrineSceneDocument.Load(Path.Combine(result, "romfs", Path.GetRelativePath(nativeRoot, nativePath)), [nativeRoot]);
            Require(check.Actors.Single(a => a.Hash == actor.Hash).Position == actor.LocalPosition + Vector3.UnitY, "Native compressed save lost position");
            Require(SHA256.HashData(File.ReadAllBytes(nativePath)).SequenceEqual(fingerprint), "Native source changed");
            string actorName = "Obj_TreeApple_A_M_01";
            var nativeModel = Task.Run(()=>ShrineSceneAssets.LoadModel(Path.Combine(nativeRoot,"Pack/Actor",actorName+".pack.zs"),nativeRoot,new(),[],default)).GetAwaiter().GetResult();
            Require(nativeModel.Meshes.Count > 0, "New actor model did not load");
            int placed = edit.AddActor(actorName, edit.AdditionSources.Single(p=>p.Contains("_Dynamic.")), actor.Position + Vector3.UnitX * 2, nativeModel);
            ulong placedHash = edit.Scene.Actors[placed].Hash;
            Require(edit.Model.Meshes.Any(m=>ShrineSceneAssets.MeshActorIndex(m)==placed), "Added native model was not assigned to its placement");
            result = edit.Save(output);
            string placedSource = edit.Scene.Actors[placed].SourceScene;
            var placedCheck = ShrineSceneDocument.Load(Path.Combine(result,"romfs",Path.GetRelativePath(nativeRoot,placedSource)),[nativeRoot]);
            Require(placedCheck.Actors.Single(a=>a.Hash==placedHash).Name==actorName, "Native Dynamic actor addition did not reopen");
            var compoundPath = Path.Combine(nativeRoot, "Phive/StaticCompoundBody/SmallDungeon/Dungeon004.Nin_NX_NVN.bphsc.zs");
            var compound = BphscDocument.Load(compoundPath); var bytes = compound.Snapshot();
            var collisionEditor = new ShrinePlacementEditor(assets with { Compound = compound, CompoundPath = compoundPath }, root);
            collisionEditor.SetPlacementOnly(true); collisionEditor.Move(0, new(4,5,6));
            Require(collisionEditor.Compound!.Snapshot().SequenceEqual(bytes), "Placement-only mode mutated collision");
            result = collisionEditor.Save(output);
            Require(!Directory.Exists(Path.Combine(result, "romfs/Phive")), "Unchanged BPHSC was exported in placement-only mode");
            string shrinePath = Path.Combine(nativeRoot, "Banc/SmallDungeon/Dungeon004_Static.bcett.byml.zs");
            var shrine = ShrineSceneDocument.Load(shrinePath, [nativeRoot]);
            var shrineActors = shrine.Actors.Select(a => a with { SourceScene = shrinePath, SourceIndex = a.Index, LocalPosition = a.Position, WorldTransform = ShrineSceneAssets.Placement(a) }).ToList();
            var coordinated = new ShrinePlacementEditor(new(new(shrineActors), new VisualModelData { SourcePath = shrinePath }, compoundPath, compound, [], 0), nativeRoot);
            var baked = coordinated.Scene.Actors.First(a => a.MergedPath.Length == 0 && coordinated.EditRestriction(a.Index) == null &&
                compound.ActorLinks.Any(l => l.Hash == (a.PlacementId != 0 ? a.PlacementId : a.Hash) && l.FirstInstance != uint.MaxValue));
            coordinated.Move(baked.Index, baked.LocalPosition + Vector3.UnitY);
            Require(!coordinated.Compound!.Snapshot().SequenceEqual(bytes), "Coordinated mode stopped updating collision");
            result = coordinated.Save(output);
            Require(File.Exists(Path.Combine(result, "romfs", Path.GetRelativePath(nativeRoot, compoundPath))), "Coordinated save omitted BPHSC");
            coordinated.Undo(); Require(coordinated.Compound!.Snapshot().SequenceEqual(bytes), "Coordinated undo did not restore collision");
            Console.WriteLine($"PASS native F-4 placement edit/save: {actor.Name}; loaded BPHSC remained byte-exact and unexported.");
            Console.WriteLine($"PASS coordinated Dungeon004 editing/export/undo: {baked.Name}.");
        }
        Console.WriteLine("PASS placement-only transforms, models, copy/delete, undo/redo, BANC preservation, mode guard, UI confirmation and protected actors.");
    }

    private static void Ui(string path, string output)
    {
        var owner = new Window { Width = 1440, Height = 900 };
        var view = new ShrineSceneView(owner, graphics: false); owner.Content = view; owner.Show(); Dispatcher.UIThread.RunJobs();
        while (view.Busy) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        var load = view.LoadPathAsync(path);
        while (!load.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        load.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        var toggle = view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "PlacementOnly");
        toggle.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Require(owner.OwnedWindows.Count == 1, "Placement-only warning missing");
        owner.OwnedWindows.Single().Close(false); Dispatcher.UIThread.RunJobs();
        Require(toggle.IsChecked == false && !view.Editing!.PlacementOnly, "Cancelled mode was applied");
        toggle.IsChecked = true; Dispatcher.UIThread.RunJobs(); owner.OwnedWindows.Single().Close(true); Dispatcher.UIThread.RunJobs();
        Require(view.Editing!.PlacementOnly && view.Viewport.CanTransform, "Mode did not enable viewport editing");
        using (var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(owner)!) frame.Save(Path.Combine(output, "placement-only.png"));
        owner.Close();
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}
