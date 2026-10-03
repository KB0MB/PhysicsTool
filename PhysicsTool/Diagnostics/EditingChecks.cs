using System.Numerics;
using System.Security.Cryptography;
using System.Reflection;
using Newtonsoft.Json;

namespace HKCLTool;

internal static class EditingChecks
{
    private static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static Vector3 Start(ColliderEditRow c) => new(c.StartX, c.StartY, c.StartZ);
    private static void Near(Vector3 actual, Vector3 expected, string message) =>
        Require(Vector3.Distance(actual, expected) < 0.002f, $"{message}: {actual} != {expected}");

    public static void Run(string outputDirectory, string[] paths)
    {
        Directory.CreateDirectory(outputDirectory);
        var log = new List<string>();
        try
        {
            ApplicationConfiguration.Initialize();
            AppLocalization.Initialize();
            CheckAtomicSave(outputDirectory);
            CheckUndoLimit();
            log.Add("PASS: failed-save preservation, temporary cleanup, and latest-50 undo order.");
            CheckHierarchy();
            log.Add("PASS: inherited collider transforms, unrelated bones, padding, immutable inputs, cycle rejection.");
            foreach (var path in paths)
            {
                var originalHash = SHA256.HashData(File.ReadAllBytes(path));
                var probe = new HkclService(); probe.Load(path);
                if (!Enumerable.Range(0, probe.GetClothSummaries().Count).Any(i => probe.GetColliderRows(i).Any(c => c.BoneIndex >= 0)))
                {
                    if (probe.IsBphcl) CheckNativeShapes(path);
                    log.Add($"SKIP binding/viewport: {Path.GetFileName(path)} has no cloth with a bound collider; native shape checks only.");
                    continue;
                }
                CheckDocument(path, outputDirectory);
                CheckViewport(path);
                CheckPositionFields(path);
                CheckColliderSelection(path);
                if (!probe.IsBphcl && !probe.IsHkrg)
                    CheckParticleSimulationEdits(path, outputDirectory);
                Require(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Source fixture was modified.");
                log.Add($"PASS: {Path.GetFileName(path)} bone/collider editing, roundtrip, live drag, position fields, follow toggle, collider box/group transforms, cancel, undo, redo.");
                if (!probe.IsBphcl && !probe.IsHkrg)
                    log.Add("PASS: HKCL particle movement/Attach/field edits preserve simulation references through reset, undo/redo, and save/reload.");
                if (Path.GetExtension(path).Equals(".bphcl", StringComparison.OrdinalIgnoreCase))
                    log.Add("Shape coverage: " + string.Join(", ", NativeBphclDocument.Open(path).Colliders.GroupBy(c => c.Shape.TypeName).Select(g => $"{g.Key}={g.Count()}")));
            }
            File.WriteAllLines(Path.Combine(outputDirectory, "result.txt"), log);
        }
        catch (Exception ex)
        {
            File.WriteAllLines(Path.Combine(outputDirectory, "result.txt"), log.Append("FAIL: " + ex));
            throw;
        }
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object instance, string method, params object?[] args) =>
        instance.GetType().GetMethod(method, Private)!.Invoke(instance, args);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;

    private static void CheckAtomicSave(string directory)
    {
        var path = Path.Combine(directory, "atomic-save.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        try { AtomicFile.Write(path, stream => { stream.WriteByte(99); throw new IOException("Injected serializer failure"); }); }
        catch (IOException) { }
        Require(File.ReadAllBytes(path).SequenceEqual(new byte[] { 1, 2, 3 }), "Failed save replaced the existing file.");
        AtomicFile.WriteAllBytes(path, new byte[] { 4, 5 });
        Require(File.ReadAllBytes(path).SequenceEqual(new byte[] { 4, 5 }), "Successful atomic save failed.");
        Require(!Directory.GetFiles(directory, ".atomic-save.bin.*.tmp").Any(), "Atomic save leaked a temporary file.");
    }

    private static void CheckUndoLimit()
    {
        using var main = new MainForm();
        var push = typeof(MainForm).GetMethod("PushUndo", Private)!;
        var type = push.GetParameters()[0].ParameterType;
        for (var i = 0; i < 55; i++)
        {
            var snapshot = Activator.CreateInstance(type, nonPublic: true)!;
            type.GetProperty("SelectedIndex")!.SetValue(snapshot, i);
            push.Invoke(main, new[] { snapshot });
        }
        var indices = Field<System.Collections.IEnumerable>(main, "_undoStack").Cast<object>()
            .Select(s => (int)type.GetProperty("SelectedIndex")!.GetValue(s)!).ToArray();
        Require(indices.SequenceEqual(Enumerable.Range(5, 50).Reverse()), "Undo limit lost or reordered recent edits.");
    }

    private static void CheckViewport(string path)
    {
        using var main = new MainForm();
        Call(main, "LoadCurrent", path);
        var service = Field<HkclService>(main, "_current");
        Field<ListBox>(main, "_clothList").SelectedIndex = Enumerable.Range(0, service.GetClothSummaries().Count)
            .First(i => service.GetColliderRows(i).Any(c => c.BoneIndex >= 0));
        typeof(MainForm).GetField("_directEditMode", Private)!.SetValue(main, true);
        Call(main, "UpdateModeLayout");
        var pageType = typeof(MainForm).GetMethod("SelectEditorTabForPage", Private)!.GetParameters()[0].ParameterType;
        Call(main, "SelectEditorTabForPage", Enum.Parse(pageType, "Bones"), false);
        Call(main, "MoveEditorContentToSelectedTab");
        Call(main, "RefreshParticleGrid", false, null);
        var cloth = Field<ListBox>(main, "_clothList").SelectedIndex;
        var bound = service.GetColliderRows(cloth).First(c => c.BoneIndex >= 0);
        var bones = service.GetBoneRows(cloth);
        var root = bones.Single(b => b.Index == bound.BoneIndex);
        while (root.ParentIndex >= 0) root = bones.Single(b => b.Index == root.ParentIndex);
        Field<ListBox>(main, "_editorIndexList").SelectedIndex = bones.ToList().FindIndex(b => b.Index == root.Index);
        if (service.IsBphcl)
        {
            Call(main, "FillBoneDetailGrid", root);
            var grid = Field<DataGridView>(main, "_editorDetailGrid");
            foreach (var label in new[] { "Parent", "Rot X (rad)", "Rot Y (rad)", "Rot Z (rad)", "Scale X", "Scale Y", "Scale Z" })
                Require(grid.Rows.Cast<DataGridViewRow>().Any(r => (string?)r.Cells[0].Value == label && !r.Cells[1].ReadOnly), $"BPHCL field {label} is not editable.");
        }
        var preview = Field<ParticlePreviewControl>(main, "_particlePreview");
        var origin = Start(bound);
        Vector3 DisplayStart() => Field<ParticlePreviewData>(preview, "_data").Colliders.First(c => c.Index == bound.Index).Start;
        var delta = new Vector3(0.02f, 0.01f, 0.03f);
        var particles = service.GetParticleRows(cloth);
        var attachments = service.GetParticleBoneAttachments(cloth);
        var editedBones = Copy(bones);
        var editedRoot = editedBones.Single(b => b.Index == root.Index);
        editedRoot.X += delta.X; editedRoot.Y += delta.Y; editedRoot.Z += delta.Z;
        var expectedParticles = BoneEditGeometry.FollowParticles(bones, editedBones, particles, attachments);
        void CheckParticles(bool moved, bool display)
        {
            foreach (var expected in expectedParticles)
            {
                var row = moved ? expected : particles.Single(p => p.Index == expected.Index);
                var point = display ? Field<ParticlePreviewData>(preview, "_data").Particles.Single(p => p.Index == row.Index).Position
                    : Position(service.GetParticleRows(cloth).Single(p => p.Index == row.Index));
                Near(point, Position(row), "Weighted particle UI transform/undo");
            }
        }
        static Vector3 Position(ParticleEditRow p) => new(p.X, p.Y, p.Z);
        Call(main, "BeginViewportParticleMove");
        Call(main, "MoveSelectedParticles", delta, false);
        CheckParticles(true, true); CheckParticles(false, false);
        Near(DisplayStart(), origin + delta, "Live drag collider");
        Near(Start(service.GetColliderRows(cloth).Single(c => c.Index == bound.Index)), origin, "Drag mutated document before commit");
        Call(main, "MoveSelectedParticles", -delta, false);
        Near(DisplayStart(), origin, "Dragging back to baseline left collider displaced");
        Call(main, "CancelViewportParticleMove");
        CheckParticles(false, true);
        Near(DisplayStart(), origin, "Cancel collider");
        Call(main, "BeginViewportParticleMove");
        Call(main, "MoveSelectedParticles", delta, false);
        Call(main, "EndViewportParticleMove");
        CheckParticles(true, true); CheckParticles(true, false);
        Near(Start(service.GetColliderRows(cloth).Single(c => c.Index == bound.Index)), origin + delta, "UI commit collider");
        Call(main, "UndoEditorChange");
        CheckParticles(false, true); CheckParticles(false, false);
        Near(DisplayStart(), origin, "UI undo collider");
        Call(main, "RedoEditorChange");
        CheckParticles(true, true); CheckParticles(true, false);
        Near(DisplayStart(), origin + delta, "UI redo collider");
        Call(main, "UndoEditorChange");
        var follow = Field<CheckBox>(main, "_particlesFollowBonesCheck");
        follow.Checked = false;
        Call(main, "BeginViewportParticleMove");
        Call(main, "MoveSelectedParticles", delta, false);
        CheckParticles(false, true); CheckParticles(false, false);
        Near(DisplayStart(), origin + delta, "Follow disabled stopped collider movement");
        follow.Checked = true;
        CheckParticles(true, true);
        follow.Checked = false;
        CheckParticles(false, true);
        Call(main, "EndViewportParticleMove");
        CheckParticles(false, true); CheckParticles(false, false);
        Near(Start(service.GetColliderRows(cloth).Single(c => c.Index == bound.Index)), origin + delta, "Follow disabled collider commit");
        Call(main, "UndoEditorChange");
        Near(DisplayStart(), origin, "Follow disabled undo");
        CheckParticles(false, true);
    }

    private static void CheckPositionFields(string path)
    {
        using var main = new MainForm();
        Call(main, "LoadCurrent", path);
        var service = Field<HkclService>(main, "_current");
        var cloths = Field<ListBox>(main, "_clothList");
        var noseCloth = Enumerable.Range(0, service.GetClothSummaries().Count)
            .FirstOrDefault(i => service.GetBoneRows(i).Any(b => b.Name == "Nose_Earring_L"), -1);
        cloths.SelectedIndex = Math.Max(0, noseCloth);
        typeof(MainForm).GetField("_directEditMode", Private)!.SetValue(main, true);
        Call(main, "UpdateModeLayout");
        var pageType = typeof(MainForm).GetMethod("SelectEditorTabForPage", Private)!.GetParameters()[0].ParameterType;
        Call(main, "SelectEditorTabForPage", Enum.Parse(pageType, "Bones"), false);
        Call(main, "MoveEditorContentToSelectedTab");
        Call(main, "RefreshParticleGrid", false, null);
        var preview = Field<ParticlePreviewControl>(main, "_particlePreview");
        foreach (var follow in new[] { true, false })
        foreach (var name in noseCloth >= 0 ? new[] { "Nose_Earring_L", "Head" } : new[] { service.GetBoneRows(cloths.SelectedIndex)[0].Name })
        {
            Field<CheckBox>(main, "_particlesFollowBonesCheck").Checked = follow;
            var bones = service.GetBoneRows(cloths.SelectedIndex);
            var original = service.GetParticleRows(cloths.SelectedIndex);
            var index = bones.ToList().FindIndex(b => b.Name == name);
            Field<ListBox>(main, "_editorIndexList").SelectedIndex = index;
            Call(main, "FillBoneDetailGrid", bones[index]);
            var after = Copy(bones);
            after[index].X += 0.025f;
            var expected = BoneEditGeometry.FollowParticles(bones, after, original, service.GetParticleBoneAttachments(cloths.SelectedIndex))
                .ToDictionary(p => p.Index);
            Require(expected.Count > 0, $"Position-field fixture has no particles influenced by {name}.");
            if (!follow) expected.Clear();
            var colliders = service.GetColliderRows(cloths.SelectedIndex);
            var followingColliders = BoneEditGeometry.FollowColliders(bones, after, colliders).ToDictionary(c => c.Index);
            typeof(MainForm).GetField("_pendingEditSnapshot", Private)!.SetValue(main, Call(main, "CaptureCurrentEditorSnapshot"));
            var grid = Field<DataGridView>(main, "_editorDetailGrid");
            grid.Rows.Cast<DataGridViewRow>().Single(r => (string?)r.Cells[0].Value == "X").Cells[1].Value =
                after[index].X.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            Call(main, "CommitEditorDetailChange");
            void Check(bool moved)
            {
                var displayed = Field<ParticlePreviewData>(preview, "_data");
                var saved = service.GetParticleRows(cloths.SelectedIndex);
                foreach (var c in colliders)
                {
                    var expectedCollider = moved ? followingColliders.GetValueOrDefault(c.Index, c) : c;
                    Near(displayed.Colliders.First(v => v.Index == c.Index).Start, Start(expectedCollider), "Follow toggle changed collider following");
                }
                foreach (var p in original)
                {
                    var row = moved ? expected.GetValueOrDefault(p.Index, p) : p;
                    var position = new Vector3(row.X, row.Y, row.Z);
                    Near(displayed.Particles.Single(v => v.Index == p.Index).Position, position, $"{name} field edit particle {p.Index} display");
                    var s = saved.Single(v => v.Index == p.Index);
                    Near(new(s.X, s.Y, s.Z), position, $"{name} field edit particle {p.Index} document");
                }
            }
            Check(true);
            Call(main, "UndoEditorChange"); Check(false);
            Call(main, "RedoEditorChange"); Check(true);
            Call(main, "UndoEditorChange"); Check(false);
        }
    }

    private static void CheckColliderSelection(string path)
    {
        using var main = new MainForm();
        Call(main, "LoadCurrent", path);
        var service = Field<HkclService>(main, "_current");
        var cloth = Enumerable.Range(0, service.GetClothSummaries().Count)
            .FirstOrDefault(i => service.GetColliderRows(i).Count >= 3, -1);
        if (cloth < 0) return;
        Field<ListBox>(main, "_clothList").SelectedIndex = cloth;
        typeof(MainForm).GetField("_directEditMode", Private)!.SetValue(main, true);
        Call(main, "UpdateModeLayout");
        var pageType = typeof(MainForm).GetMethod("SelectEditorTabForPage", Private)!.GetParameters()[0].ParameterType;
        Call(main, "SelectEditorTabForPage", Enum.Parse(pageType, "Colliders"), false);
        Call(main, "MoveEditorContentToSelectedTab");
        Call(main, "RefreshParticleGrid", false, null);
        var before = service.GetColliderRows(cloth);
        var ids = before.Take(3).Select(c => c.Index).ToArray();
        var preview = Field<ParticlePreviewControl>(main, "_particlePreview");
        void Select(int[] indices, ParticleSelectionOperation operation) => Call(main, "SelectCollidersFromViewport", indices, operation);
        void Selection(params int[] expected)
        {
            Require(Field<HashSet<int>>(main, "_selectedColliderIndices").SetEquals(expected), "Collider selection mismatch.");
            Require(Field<HashSet<int>>(preview, "_selectedColliderIndices").SetEquals(expected), "Collider highlights mismatch.");
        }
        Select(new[] { ids[0] }, ParticleSelectionOperation.Replace); Selection(ids[0]);
        Select(new[] { ids[1], ids[2] }, ParticleSelectionOperation.Add); Selection(ids);
        Select(new[] { ids[1] }, ParticleSelectionOperation.Remove); Selection(ids[0], ids[2]);
        Call(main, "HandlePreviewPick", new PreviewPickEventArgs(PreviewPickKind.Collider, ids[1], ParticleSelectionOperation.Toggle)); Selection(ids);
        Call(main, "HandlePreviewPick", new PreviewPickEventArgs(PreviewPickKind.Collider, ids[1], ParticleSelectionOperation.Toggle)); Selection(ids[0], ids[2]);
        Select(ids, ParticleSelectionOperation.Replace);
        var delta = new Vector3(0.02f, -0.03f, 0.01f);
        void Check(bool moved, bool document)
        {
            foreach (var c in before)
            {
                var offset = moved && ids.Contains(c.Index) ? delta : Vector3.Zero;
                var actual = document ? Start(service.GetColliderRows(cloth).Single(v => v.Index == c.Index))
                    : Field<ParticlePreviewData>(preview, "_data").Colliders.First(v => v.Index == c.Index).Start;
                Near(actual, Start(c) + offset, "Collider group translation");
            }
        }
        Call(main, "BeginViewportParticleMove");
        Call(main, "MoveSelectedParticles", delta, false); Check(true, false); Check(false, true);
        Call(main, "MoveSelectedParticles", -delta, false); Check(false, false);
        Call(main, "CancelViewportParticleMove"); Check(false, false); Selection(ids);
        Call(main, "BeginViewportParticleMove");
        Call(main, "MoveSelectedParticles", delta, false);
        Call(main, "EndViewportParticleMove"); Check(true, false); Check(true, true);
        Call(main, "UndoEditorChange"); Check(false, false); Check(false, true);
        Call(main, "RedoEditorChange"); Check(true, false); Check(true, true);
        Call(main, "UndoEditorChange");
        Select(ids, ParticleSelectionOperation.Replace);
        var center = before.Where(c => ids.Contains(c.Index)).Aggregate(Vector3.Zero, (sum, c) => sum + Center(c)) / ids.Length;
        Call(main, "BeginViewportParticleMove");
        Call(main, "RotateSelectedParticles", 0.2f, Vector3.UnitY, false);
        foreach (var c in Field<List<ColliderEditRow>>(main, "_colliderRows").Where(c => ids.Contains(c.Index)))
            Near(Center(c), center + Vector3.Transform(Center(before.Single(v => v.Index == c.Index)) - center, Matrix4x4.CreateRotationY(0.2f)), "Group rotation pivot");
        Call(main, "CancelViewportParticleMove"); Check(false, true);
        Call(main, "BeginViewportParticleMove");
        Call(main, "ScaleSelectedParticles", 1.1f, null, false, false);
        foreach (var c in Field<List<ColliderEditRow>>(main, "_colliderRows").Where(c => ids.Contains(c.Index)))
            Near(Center(c), center + (Center(before.Single(v => v.Index == c.Index)) - center) * 1.1f, "Group scale pivot");
        Call(main, "CancelViewportParticleMove"); Check(false, true);

        // Exercise the actual rectangle-selection event through MainForm.
        preview.Size = new Size(800, 600);
        var data = new ParticlePreviewData();
        data.Colliders.Add(new ColliderPreviewShape { Index = ids[0], Start = new(-0.1f, 0, 0), End = new(0.1f, 0, 0) });
        data.Colliders.Add(new ColliderPreviewShape { Index = ids[1], Start = Vector3.Zero, End = Vector3.Zero });
        preview.SetData(data);
        Call(preview, "SelectParticlesInRectangle", new Rectangle(0, 0, 800, 600)); Selection(ids[0], ids[1]);
        Call(preview, "SelectParticlesInRectangle", Rectangle.Empty); Selection();
        var intersection = typeof(ParticlePreviewControl).GetMethod("SegmentIntersectsRectangle", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Hit(PointF a, PointF b) => (bool)intersection.Invoke(null, new object[] { a, b, new Rectangle(10, 10, 20, 20) })!;
        Require(Hit(new(0, 20), new(40, 20)) && Hit(new(20, 20), new(20, 20)) && !Hit(new(0, 5), new(40, 5)), "Collider box segment intersection failed.");
        static Vector3 Center(ColliderEditRow c) => new((c.StartX + c.EndX) * 0.5f, (c.StartY + c.EndY) * 0.5f, (c.StartZ + c.EndZ) * 0.5f);
    }

    private static void CheckParticleSimulationEdits(string path, string output)
    {
        using var main = new MainForm();
        Call(main, "LoadCurrent", path);
        var service = Field<HkclService>(main, "_current");
        typeof(MainForm).GetField("_directEditMode", Private)!.SetValue(main, true);
        Call(main, "UpdateModeLayout");
        var pageType = typeof(MainForm).GetMethod("SelectEditorTabForPage", Private)!.GetParameters()[0].ParameterType;
        Call(main, "SelectEditorTabForPage", Enum.Parse(pageType, "Particles"), false);
        for (var cloth = 0; cloth < service.GetClothSummaries().Count; cloth++)
        {
            Field<ListBox>(main, "_clothList").SelectedIndex = cloth;
            Call(main, "RefreshParticleGrid", false, null);
            var before = service.GetParticlePreview(cloth);
            var anchor = before.Particles.FirstOrDefault(p => p.Fixed && before.ParticleAttachments.Any(a => a.ParticleIndex == p.Index));
            if (anchor == null) continue;
            var rows = service.GetParticleRows(cloth);
            Field<ListBox>(main, "_editorIndexList").SelectedIndex = rows.ToList().FindIndex(p => p.Index == anchor.Index);
            IReadOnlyDictionary<int, Vector3> ResetPositions(HkclService document)
            {
                var solver = new HkclPreviewSimulator(document.GetParticlePreview(cloth));
                solver.Reset(); return solver.GetPositions();
            }
            var oldSimulation = ResetPositions(service);
            var delta = new Vector3(0.03f, -0.02f, 0.01f);
            void Check(HkclService document, Vector3 shift)
            {
                var positions = ResetPositions(document);
                foreach (var p in before.Particles)
                    Near(positions[p.Index], oldSimulation[p.Index] + (p.Index == anchor.Index ? shift : Vector3.Zero), "Particle edit simulation reset/reference");
            }
            void UndoRedo(Vector3 shift)
            {
                Check(service, shift);
                Call(main, "UndoEditorChange"); Check(service, Vector3.Zero);
                Call(main, "RedoEditorChange"); Check(service, shift);
                var saved = Path.Combine(output, $"particle-reference-{Path.GetFileNameWithoutExtension(path)}-{cloth}.hkcl");
                service.SaveHkcl(saved, HkclService.DetectHkclPlatform(path));
                var loaded = new HkclService(); loaded.Load(saved); Check(loaded, shift);
                Call(main, "UndoEditorChange"); Check(service, Vector3.Zero);
            }
            Call(main, "BeginViewportParticleMove");
            Call(main, "MoveSelectedParticles", delta, false);
            Call(main, "EndViewportParticleMove"); UndoRedo(delta);
            var target = before.Bones.First(b => b.Name == "Head" || b.ParentIndex < 0);
            Call(main, "AttachParticlesToBone", new[] { anchor.Index }, target.Index);
            UndoRedo(target.Position - anchor.Position);
            typeof(MainForm).GetField("_pendingEditSnapshot", Private)!.SetValue(main, Call(main, "CaptureCurrentEditorSnapshot"));
            var grid = Field<DataGridView>(main, "_editorDetailGrid");
            grid.Rows.Cast<DataGridViewRow>().Single(r => (string?)r.Cells[0].Value == "X").Cells[1].Value =
                (anchor.Position.X + delta.X).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            Call(main, "CommitEditorDetailChange"); UndoRedo(new(delta.X, 0, 0));
        }
    }

    private static void CheckHierarchy()
    {
        var before = new[] {
            new BoneEditRow { Index = 0, ParentIndex = -1, RotationW = 1 },
            new BoneEditRow { Index = 1, ParentIndex = 0, X = 2, RotationW = 1 },
            new BoneEditRow { Index = 2, ParentIndex = -1, RotationW = 1 }
        };
        var after = Copy(before);
        after[0].X = 4;
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        after[0].RotationZ = q.Z; after[0].RotationW = q.W;
        after[1].Y = 1;
        var transform = Matrix4x4.CreateTranslation(3, 0, 0); transform.M14 = 9; transform.M44 = 0;
        var rows = new[] {
            new ColliderEditRow { Index = 0, BoneIndex = 1, StartX = 3, EndX = 4, Transform = transform },
            new ColliderEditRow { Index = 1, BoneIndex = 2, Transform = Matrix4x4.Identity },
            new ColliderEditRow { Index = 2, BoneIndex = -1, Transform = Matrix4x4.Identity }
        };
        var result = BoneEditGeometry.FollowColliders(before, after, rows);
        Require(result.Count == 1, "Only the descendant's collider should change.");
        Near(Start(result[0]), new Vector3(3, 3, 0), "Parent rotation plus child movement must apply once");
        Require(result[0].Transform.M14 == 9 && result[0].Transform.M44 == 0, "Havok padding changed.");
        Require(before[0].X == 0 && rows[0].StartX == 3, "Following changed the input rows.");
        after[0].ParentIndex = 1;
        var rejected = false;
        try { BoneEditGeometry.FollowColliders(before, after, rows); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Cyclic skeleton was accepted.");
    }

    private static void CheckDocument(string path, string outputDirectory)
    {
        var service = new HkclService(); service.Load(path);
        var cloth = Enumerable.Range(0, service.GetClothSummaries().Count).First(i =>
            service.GetColliderRows(i).Any(c => c.BoneIndex >= 0) && service.GetBoneRows(i).Count > 0);
        var before = service.GetBoneRows(cloth).ToArray();
        var colliders = service.GetColliderRows(cloth).ToArray();
        var bound = colliders.First(c => c.BoneIndex >= 0);
        var target = before.Single(b => b.Index == bound.BoneIndex);
        while (target.ParentIndex >= 0) target = before.Single(b => b.Index == target.ParentIndex);
        var state = service.CaptureState();
        if (!service.IsBphcl)
        {
            var jsonPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(path) + ".json");
            service.ExportReadableJson(jsonPath);
            var jsonReload = new HkclService(); jsonReload.Load(jsonPath);
            Require(jsonReload.GetBoneRows(cloth).Count == before.Length, "Havok JSON roundtrip failed.");
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(jsonPath));
            json.Remove("$type");
            json.AddFirst(new Newtonsoft.Json.Linq.JProperty("$type", "System.Version, System.Private.CoreLib"));
            File.WriteAllText(jsonPath, json.ToString());
            var typeRejected = false;
            try { jsonReload.Load(jsonPath); }
            catch (JsonSerializationException) { typeRejected = true; }
            Require(typeRejected, "Non-Havok JSON type was accepted.");
        }
        var moved = Copy(target); moved.X += 0.23f; moved.Y -= 0.17f; moved.Z += 0.31f;
        var after = before.Select(b => b.Index == moved.Index ? moved : b).ToArray();
        var expected = BoneEditGeometry.FollowColliders(before, after, colliders);
        service.UpdateBoneRows(cloth, new[] { moved });
        foreach (var c in expected)
            Near(Start(service.GetColliderRows(cloth).Single(r => r.Index == c.Index)), Start(c), "Committed descendant collider");
        var output = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(path) + "-edited" + Path.GetExtension(path));
        var platform = service.IsBphcl ? HkclPlatform.Switch : HkclService.DetectHkclPlatform(path);
        service.SaveHkcl(output, platform);
        var reload = new HkclService(); reload.Load(output);
        foreach (var c in expected)
            Near(Start(reload.GetColliderRows(cloth).Single(r => r.Index == c.Index)), Start(c), "Reloaded descendant collider");
        service.RestoreState(state);
        Near(Start(service.GetColliderRows(cloth).Single(c => c.Index == bound.Index)), Start(bound), "Undo collider");

        // Editing a non-root bone also follows its rotated parent frame.
        var direct = Copy(before.Single(b => b.Index == bound.BoneIndex)); direct.X += 0.13f;
        var directExpected = BoneEditGeometry.FollowColliders(before,
            before.Select(b => b.Index == direct.Index ? direct : b).ToArray(), colliders);
        service.UpdateBoneRows(cloth, new[] { direct });
        foreach (var c in directExpected)
            Near(Start(service.GetColliderRows(cloth).Single(r => r.Index == c.Index)), Start(c), "Direct bone collider");
        service.RestoreState(state);

        var rotated = Copy(target);
        var rotation = new Quaternion(rotated.RotationX, rotated.RotationY, rotated.RotationZ, rotated.RotationW);
        rotation = Quaternion.Normalize(rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f));
        rotated.RotationX = rotation.X; rotated.RotationY = rotation.Y; rotated.RotationZ = rotation.Z; rotated.RotationW = rotation.W;
        var rotatedExpected = BoneEditGeometry.FollowColliders(before,
            before.Select(b => b.Index == rotated.Index ? rotated : b).ToArray(), colliders);
        service.UpdateBoneRows(cloth, new[] { rotated });
        foreach (var c in rotatedExpected)
            Near(Start(service.GetColliderRows(cloth).Single(r => r.Index == c.Index)), Start(c), "Rotated ancestor collider");
        service.RestoreState(state);

        var bad = Copy(target); bad.ParentIndex = bad.Index;
        var rejected = false;
        try { service.UpdateBoneRows(cloth, new[] { bad }); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected && service.CaptureState() == state, "Invalid edit changed the document.");
        if (!service.IsBphcl) return;

        var boundAncestors = new HashSet<int>();
        foreach (var collider in colliders.Where(c => c.BoneIndex >= 0))
            for (var index = collider.BoneIndex; index >= 0; index = before.Single(b => b.Index == index).ParentIndex)
                boundAncestors.Add(index);
        var scalableBone = before.Last(b => !boundAncestors.Contains(b.Index));
        var scaled = Copy(scalableBone); scaled.ScaleX = 1.1f; scaled.ParentIndex = -1;
        service.UpdateBoneRows(cloth, new[] { scaled });
        service.SaveHkcl(output, platform); reload.Load(output);
        var savedBone = reload.GetBoneRows(cloth).Single(b => b.Index == scaled.Index);
        Require(MathF.Abs(savedBone.ScaleX - 1.1f) < 0.00001f && savedBone.ParentIndex == -1, "BPHCL parent/scale was not saved.");
        service.RenameCloth(cloth, "EditedCloth");
        Require(service.GetBoneRows(cloth).Single(b => b.Index == scaled.Index).ScaleX == scaled.ScaleX, "Rename discarded unsaved edits.");
        // Change again after save: both target and donor must use in-memory state.
        scaled.ScaleX = 1.2f; service.UpdateBoneRows(cloth, new[] { scaled });
        var donor = new HkclService(); donor.Load(path);
        var donorBone = Copy(donor.GetBoneRows(cloth).Single(b => b.Index == scalableBone.Index)); donorBone.ScaleY = 1.3f;
        donor.UpdateBoneRows(cloth, new[] { donorBone });
        var count = service.GetClothSummaries().Count;
        service.MergeClothFrom(donor, cloth, cloth);
        Require(service.GetBoneRows(cloth).Single(b => b.Index == scaled.Index).ScaleX == 1.2f, "Merge discarded target edits.");
        Require(service.GetBoneRows(count).Single(b => b.Index == donorBone.Index).ScaleY == 1.3f, "Merge discarded donor edits.");
        service.RemoveCloth(count);
        Require(service.GetBoneRows(cloth).Single(b => b.Index == scaled.Index).ScaleX == 1.2f, "Delete discarded survivor edits.");
        CheckNativeShapes(path);
    }

    private static void CheckNativeShapes(string path)
    {
        var document = NativeBphclDocument.Open(path);
        foreach (var c in document.Colliders)
        {
            var transformOnly = new NativeBphclColliderEdit(c.AxisX, c.AxisY, c.AxisZ, c.Translation + new Vector4(0.2f, 0, 0, 0));
            var updated = NativeBphclWriter.ApplyColliderEdits(document, new Dictionary<int, NativeBphclColliderEdit> { [c.Index] = transformOnly });
            Require(updated.Colliders[c.Index].Shape == c.Shape, "Moving collider changed its shape.");
            if (c.Shape.TypeName == "hclCapsuleShape")
            {
                var end = c.Shape.End + new Vector4(0.03f, 0.05f, 0.07f, 0);
                updated = NativeBphclWriter.ApplyColliderEdits(document, new Dictionary<int, NativeBphclColliderEdit> { [c.Index] = transformOnly with { End = end } });
                var delta = new Vector3(end.X - c.Shape.Start.X, end.Y - c.Shape.Start.Y, end.Z - c.Shape.Start.Z);
                var shape = updated.Colliders[c.Index].Shape;
                Near(new(shape.Direction.X, shape.Direction.Y, shape.Direction.Z), Vector3.Normalize(delta), "Capsule derived direction");
                Require(MathF.Abs(shape.CapsuleLengthSquaredInverse * delta.LengthSquared() - 1) < 0.0001f, "Capsule derived inverse length is stale.");
            }
            if (c.Shape.TypeName == "hclTaperedCapsuleShape")
            {
                var rejected = false;
                try { NativeBphclWriter.ApplyColliderEdits(document, new Dictionary<int, NativeBphclColliderEdit> { [c.Index] = transformOnly with { Radius = c.Shape.Radius + 0.1f } }); }
                catch (NotSupportedException) { rejected = true; }
                Require(rejected, "Unsupported taper edit was accepted.");
            }
            if (c.Shape.TypeName == "hclPlaneShape")
            {
                var plane = new Vector4(0, 1, 0, -0.25f);
                updated = NativeBphclWriter.ApplyColliderEdits(document, new Dictionary<int, NativeBphclColliderEdit> { [c.Index] = transformOnly with { PlaneEquation = plane } });
                Require(updated.Colliders[c.Index].Shape.PlaneEquation == plane, "Plane equation edit was not persisted.");
            }
        }
    }
}
