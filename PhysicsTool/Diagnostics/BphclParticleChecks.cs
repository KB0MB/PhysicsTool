using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;

namespace HKCLTool;

internal static class BphclParticleChecks
{
    private static int _deletePassed, _deleteRejected;
    public static void Run(string directory, string[] paths)
    {
        Directory.CreateDirectory(directory);
        var log = new List<string>();
        try
        {
            ApplicationConfiguration.Initialize();
            AppLocalization.Initialize();
            _deletePassed = _deleteRejected = 0;
            CheckBlankDrafts(directory);
            CheckSimulationButtonRestore(Path.Combine(directory, "manual-0.hkcl"));
            CheckDocumentation(directory);
            log.Add("PASS UI: repeated simulation lock/start/stop restores constraint buttons and styling; documentation search, topics, and responsive layouts.");
            int clothCount = 0;
            foreach (var path in paths)
            {
                var hash = SHA256.HashData(File.ReadAllBytes(path));
                var service = new HkclService();
                service.Load(path);
                Require(service.IsBphcl, "Expected a BPHCL fixture.");
                var state = service.CaptureState();
                var original = service.CaptureBphclNativeDocument()!;
                CheckLinkPolicies(original);
                CheckBendRemoval(path, directory, original);
                try { CheckAuthoring(path, directory, original); }
                catch (Exception ex) { throw new InvalidOperationException($"Authoring fixture {Path.GetFileName(path)}: {ex.Message}", ex); }
                for (int cloth = 0; cloth < original.Cloths.Count; cloth++)
                {
                    service.RestoreState(state);
                    var rows = service.GetParticleRows(cloth);
                    if (rows.Count == 0) continue;
                    var added = service.AddParticle(cloth);
                    Require(added == rows.Count, "Append index");
                    Validate(original, service.CaptureBphclNativeDocument()!, cloth, rows.Count + 1);
                    var source = service.GetParticleRows(cloth).FirstOrDefault(p => p.Fixed) ?? service.GetParticleRows(cloth)[0];
                    var copied = service.AddParticle(cloth, source);
                    Validate(original, service.CaptureBphclNativeDocument()!, cloth, rows.Count + 2);
                    var copy = service.GetParticleRows(cloth)[copied];
                    Require(copy.Fixed == source.Fixed && copy.CollisionMask == source.CollisionMask, "Copy properties");
                    Require(Vector3.Distance(new(copy.X, copy.Y, copy.Z), new(source.X, source.Y, source.Z)) < 0.00001f, "Copy position");
                    copy.Fixed = true;
                    service.UpdateParticleRows(cloth, new[] { copy });
                    copy = service.GetParticleRows(cloth)[copied];
                    Require(copy.Fixed && copy.Mass == 0 && copy.InverseMass == 0, "Fixed particle normalization");
                    Require(service.CaptureBphclNativeDocument()!.Cloths[cloth].Operators
                        .Where(o => o.ClassName == "hclMoveParticlesOperator").Any(o => o.VertexParticlePairs.Any(p => p.ParticleIndex == copied)), "Fixed binding");
                    copy.Fixed = false;
                    copy.X += 0.02f;
                    copy.Mass = 0.25f;
                    copy.Radius = 0.031f;
                    copy.Friction = 0.4f;
                    copy.CollisionMask = 0;
                    service.UpdateParticleRows(cloth, new[] { copy });
                    var edited = service.CaptureBphclNativeDocument()!;
                    Validate(original, edited, cloth, rows.Count + 2);
                    Require(!edited.Cloths[cloth].Operators.Where(o => o.ClassName == "hclMoveParticlesOperator")
                        .Any(o => o.VertexParticlePairs.Any(p => p.ParticleIndex == copied)), "Unpin binding removal");
                    var beforeInvalid = service.CaptureState();
                    copy.X = float.NaN;
                    try { service.UpdateParticleRows(cloth, new[] { copy }); throw new Exception("Non-finite position accepted"); }
                    catch (ArgumentException) { }
                    Require(service.CaptureState() == beforeInvalid, "Failed edit mutated document");
                    var output = Path.Combine(directory, "roundtrip.bphcl");
                    service.SaveHkcl(output, HkclPlatform.Switch);
                    var reload = new HkclService(); reload.Load(output);
                    var reloaded = reload.GetParticleRows(cloth)[copied];
                    Require(reloaded.Mass == 0.25f && reloaded.InverseMass == 4 && reloaded.CollisionMask == 0 && !reloaded.Fixed, "Saved particle values");
                    Validate(original, reload.CaptureBphclNativeDocument()!, cloth, rows.Count + 2, saved: true);
                    if (edited.Cloths[cloth].SimCloths[0].ConstraintSets.Any(NativeBphclParticleEditor.SupportsLinkEditing))
                    {
                        service.LinkParticles(cloth, new[] { 0, copied });
                        var relation = service.GetParticleRelationships(cloth, copied).Single(r => r.Kind == "Link");
                        Require(service.CanDeleteParticleRelationship(cloth, relation), "New link cannot be removed");
                        service.SaveHkcl(output, HkclPlatform.Switch);
                        reload.Load(output);
                        Require(reload.GetParticleRelationships(cloth, copied).Count(r => r.Kind == "Link") == 1, "Link save/reload");
                        service.LinkParticles(cloth, new[] { copied, 0 });
                        var directed = edited.Cloths[cloth].SimCloths[0].ConstraintSets[relation.ConstraintSetIndex].ClassName == "hclStretchLinkConstraintSet";
                        Require(service.GetParticleRelationships(cloth, copied).Count(r => r.Kind == "Link") == (directed ? 2 : 1), "Link direction/duplicate policy");
                        if (directed)
                        {
                            var reverse = service.GetParticleRelationships(cloth, copied).Single(r => r.Kind == "Link" && r.LinkIndex != relation.LinkIndex);
                            service.DeleteParticleRelationship(cloth, reverse);
                        }
                        service.DeleteParticleRelationship(cloth, relation);
                        Require(service.GetParticleRelationships(cloth, copied).All(r => r.Kind != "Link"), "Link removal");
                    }
                    clothCount++;
                }
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Fixture modified");
                log.Add($"PASS {Path.GetFileName(path)}: add/copy, particle deletion or atomic unsupported-layout rejection, collider deletion, constraint creation/removal, local ranges, values, schema, save/reload.");
            }
            CheckUi(paths[0]);
            log.Add("PASS UI: BPHCL simulation shortcut guard, Add, copy/paste, mirror paste, fixed checkbox, collider selector, undo/redo.");
            log.Add($"PASS {clothCount} cloths in {paths.Length} files.");
            log.Add($"Particle deletion: {_deletePassed} successful cases; {_deleteRejected} unsupported-layout rejections verified atomic.");
            File.WriteAllLines(Path.Combine(directory, "result.txt"), log);
        }
        catch (Exception ex)
        {
            File.WriteAllLines(Path.Combine(directory, "result.txt"), log.Append("FAIL: " + ex));
            throw;
        }
    }

    private static void CheckBlankDrafts(string directory)
    {
        foreach (var format in new[] { 0, 1, 2 })
        {
            var service = HkclService.CreateBlankCloth("ManualCloth", format == 2);
            var a = service.AddParticle(0); var b = service.AddParticle(0);
            var rows = service.GetParticleRows(0).ToArray(); rows[a].X = .1f; rows[b].X = .2f; rows[b].Y = .1f;
            service.UpdateParticleRows(0, rows);
            foreach (var kind in ClothConstraintCatalog.Kinds.Where(k => k.CanCreate))
                service.AddClothConstraint(0, new[] { a, b }, new ClothLinkOptions(kind.ClassName, .1f, .05f, .05f, .05f));
            service.LinkParticles(0, new[] { 0, a, b });
            var state = service.CaptureState();
            service.DeleteParticles(0, new[] { a });
            Require(service.GetParticleRows(0).Count == 2, "Draft deletion");
            service.RestoreState(state);
            var output = Path.Combine(directory, "manual-" + format + service.CurrentExtension);
            service.SaveHkcl(output, format == 0 ? HkclPlatform.WiiU : HkclPlatform.Switch);
            var reload = new HkclService(); reload.Load(output);
            Require(reload.GetParticleRows(0).Count == 3, "Draft reload");
            Require(reload.GetParticleRelationships(0, b).Count(r => r.Kind == "Link" && r.ParticleA == a && r.ParticleB == b) == 3, "Draft constraint reload");
            Require(reload.GetParticlePreview(0).Triangles.Count == 1, "Draft triangle reload");
            foreach (var buffer in reload.GetClothRuntimeData(0).Buffers.Where(b => b.Type is 1 or 2))
                Require(buffer.VertexCount == 3, "Draft buffer size");
            if (format == 2) Require(BphclSchemaAudit.ValidateArrayStorageTypes(reload.CaptureBphclNativeDocument()!).Count == 0, "Draft native schema");

            var shortened = HkclService.CreateBlankCloth("ShortenedLink", format == 2);
            shortened.AddParticle(0);
            var pair = shortened.GetParticleRows(0).ToArray(); pair[1].X = .2f;
            shortened.UpdateParticleRows(0, pair);
            using var shorterDialog = new ClothConstraintDialog(pair, new[] { 1 });
            Require(shorterDialog.ParticleIndices.Distinct().Count() == 2, "Single selection chose duplicate endpoints");
            var kinds = Field<ComboBox>(shorterDialog, "_kind");
            for (var kind = 0; kind < 2; kind++)
            {
                kinds.SelectedIndex = kind;
                Field<NumericUpDown>(shorterDialog, "_length").Value = .05m;
                Require(Field<Button>(shorterDialog, "_add").Enabled, "Shorter link disabled");
                Require(shorterDialog.Options.MinimumLength == 0 && shorterDialog.Options.StretchStiffness == 0, "Disabled constraint fields leaked into options");
                shortened.AddClothConstraint(0, shorterDialog.ParticleIndices, shorterDialog.Options);
            }
            kinds.SelectedIndex = 2;
            Field<NumericUpDown>(shorterDialog, "_length").Value = .05m;
            Require(!Field<Button>(shorterDialog, "_add").Enabled && Field<Label>(shorterDialog, "_validation").Text.Contains("Minimum length"), "Invalid bend range lacks feedback");
            kinds.SelectedIndex = 3;
            Require(!Field<Button>(shorterDialog, "_add").Enabled && Field<Label>(shorterDialog, "_validation").Text.Contains("inspect-only"), "Unsupported family lacks feedback");
        }
        using var blank = new NewClothDialog();
        blank.Opacity = 0; blank.Show(); Application.DoEvents(); blank.PerformLayout();
        using var image = new Bitmap(blank.Width, blank.Height); blank.DrawToBitmap(image, new Rectangle(0, 0, blank.Width, blank.Height));
        image.Save(Path.Combine(directory, "new-cloth-dialog.png"));
        var draft = HkclService.CreateBlankCloth("Dialog", false); draft.AddParticle(0);
        using var dialog = new ClothConstraintDialog(draft.GetParticleRows(0), new[] { 0, 1 });
        dialog.Opacity = 0; dialog.Show(); Application.DoEvents(); dialog.PerformLayout();
        using var constraintImage = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(constraintImage, new Rectangle(0, 0, dialog.Width, dialog.Height));
        constraintImage.Save(Path.Combine(directory, "constraint-dialog.png"));
    }

    private static void CheckAuthoring(string path, string directory, NativeBphclDocument original)
    {
        var service = new HkclService(); service.Load(path);
        var state = service.CaptureState();
        for (var cloth = 0; cloth < original.Cloths.Count; cloth++)
        {
            var before = original.Cloths[cloth].SimCloths[0];
            if (before.Particles.Count < 4) continue;
            foreach (var deleted in new[] { new[] { 0 }, new[] { before.Particles.Count / 2 }, new[] { before.Particles.Count - 1 }, new[] { 0, 2 } })
            {
                service.RestoreState(state);
                var beforeDelete = service.CaptureState();
                try { service.DeleteParticles(cloth, deleted); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("packed virtual-collision") || ex.Message.Contains("cannot yet remap"))
                {
                    Require(service.CaptureState() == beforeDelete, "Rejected topology edit mutated the document");
                    _deleteRejected++;
                    continue;
                }
                var doc = service.CaptureBphclNativeDocument()!;
                var sim = doc.Cloths[cloth].SimCloths[0];
                Require(sim.Particles.Count == before.Particles.Count - deleted.Length, "Delete count");
                Require(BphclSchemaAudit.ValidateArrayStorageTypes(doc).Count == 0, "Delete schema");
                Require(sim.Particles.Select(p => p.Position).SequenceEqual(before.Particles.Where(p => !deleted.Contains(p.Index)).Select(p => p.Position)), "Delete survivor positions");
                Require(sim.TriangleIndices.All(i => i < sim.Particles.Count), "Delete triangle range");
                foreach (var set in sim.ConstraintSets)
                    foreach (var link in set.Links)
                        Require((link.ParticleA == null || link.ParticleA >= 0 && link.ParticleA < sim.Particles.Count) &&
                            (link.ParticleB == null || link.ParticleB >= 0 && link.ParticleB < sim.Particles.Count) &&
                            link.Values.Where(v => v.Key is "particleA" or "particleB" or "particleC" or "particleD" or "particleIndex")
                                .All(v => v.Value >= 0 && v.Value < sim.Particles.Count),
                            $"Delete link range: cloth {cloth}, removed {string.Join(',', deleted)}, {set.ClassName} #{set.Index}, link {link.ParticleA}/{link.ParticleB}, count {sim.Particles.Count}");
                Require(doc.Cloths[cloth].Operators.SelectMany(o => o.VertexParticlePairs).All(p => p.ParticleIndex < sim.Particles.Count), "Delete fixed mapping range");
                var output = Path.Combine(directory, "authoring-roundtrip.bphcl");
                service.SaveHkcl(output, HkclPlatform.Switch);
                var reload = new HkclService(); reload.Load(output);
                Require(reload.GetParticleRows(cloth).Count == sim.Particles.Count, "Delete reload");
                _deletePassed++;
            }
            foreach (var kind in ClothConstraintCatalog.Kinds.Where(k => k.CanCreate))
            {
                service.RestoreState(state);
                var a = service.AddParticle(cloth); var b = service.AddParticle(cloth);
                service.AddClothConstraint(cloth, new[] { a, b }, new ClothLinkOptions(kind.ClassName, .1f, .2f, .05f, .3f));
                var doc = service.CaptureBphclNativeDocument()!;
                Require(BphclSchemaAudit.ValidateArrayStorageTypes(doc).Count == 0, "Constraint schema");
                var set = doc.Cloths[cloth].SimCloths[0].ConstraintSets.Single(s => s.ClassName == kind.ClassName && s.Links.Any(l => l.ParticleA == a && l.ParticleB == b));
                Require(doc.Cloths[cloth].Operators.SelectMany(o => o.SimulateConfigs).Any(c => NativeBphclParticleEditor.ExecutesSet(c, set.Index)), "New constraint schedule");
                var output = Path.Combine(directory, "constraint-roundtrip.bphcl"); service.SaveHkcl(output, HkclPlatform.Switch);
                var reload = new HkclService(); reload.Load(output);
                Require(reload.GetParticleRelationships(cloth, b).Any(r => r.Kind == "Link"), "Constraint reload");
                var valid = service.CaptureState();
                try { service.AddClothConstraint(cloth, new[] { a, b }, new ClothLinkOptions(kind.ClassName, .1f, .2f)); throw new Exception("Duplicate constraint accepted"); }
                catch (InvalidOperationException) { }
                Require(service.CaptureState() == valid, "Duplicate mutated document");
            }
            service.RestoreState(state);
            var colliders = service.GetColliderRows(cloth);
            if (colliders.Count > 0 && before.CollidableItemIndices.Count <= 31)
            {
                var removed = colliders[colliders.Count / 2];
                var native = service.CaptureBphclNativeDocument()!;
                var item = native.Colliders[removed.Index].ItemIndex;
                var oldSlot = before.CollidableItemIndices.ToList().IndexOf(item);
                service.DeleteCollider(cloth, removed.Index);
                var doc = service.CaptureBphclNativeDocument()!;
                Require(BphclSchemaAudit.ValidateArrayStorageTypes(doc).Count == 0, "Collider delete schema");
                Require(!doc.Cloths[cloth].SimCloths[0].CollidableItemIndices.Contains(item), "Collider still in cloth");
                for (var other = 0; other < original.Cloths.Count; other++)
                    if (other != cloth) Require(doc.Cloths[other].SimCloths[0].CollidableItemIndices.SequenceEqual(original.Cloths[other].SimCloths[0].CollidableItemIndices), "Shared collider modified");
                var mask = doc.Cloths[cloth].SimCloths[0].StaticCollisionMasks;
                for (var i = 0; i < mask.Count; i++)
                    Require((mask[i] & 0x80000000u) == (before.StaticCollisionMasks[i] & 0x80000000u), "Landscape bit changed");
                var output = Path.Combine(directory, "collider-delete.bphcl"); service.SaveHkcl(output, HkclPlatform.Switch);
                var reload = new HkclService(); reload.Load(output);
                Require(reload.GetColliderRows(cloth).Count == colliders.Count - 1, "Collider deletion reload");
            }
            service.RestoreState(state);
            var ranges = service.GetParticleRows(cloth).SelectMany(p => service.GetParticleRelationships(cloth, p.Index))
                .Where(r => r.LocalConstraintIndex >= 0).ToArray();
            if (ranges.FirstOrDefault() is { } range)
            {
                range.MaximumDistance = .123f;
                service.UpdateParticleRelationshipRows(cloth, new[] { range });
                Require(service.GetParticleRelationships(cloth, range.ParticleA).Any(r => r.LocalConstraintIndex == range.LocalConstraintIndex && r.ConstraintSetIndex == range.ConstraintSetIndex && r.MaximumDistance == .123f), "Local range edit");
                Require(service.CanDeleteParticleRelationship(cloth, range), "Local range delete disabled");
                service.DeleteParticleRelationship(cloth, range);
                Require(BphclSchemaAudit.ValidateArrayStorageTypes(service.CaptureBphclNativeDocument()!).Count == 0, "Range removal schema");
            }
        }
    }

    private static void CheckSimulationButtonRestore(string path)
    {
        using var main = new MainForm();
        Call(main, "LoadCurrent", path);
        Field<ListBox>(main, "_clothList").SelectedIndex = 0;
        typeof(MainForm).GetField("_directEditMode", Private)!.SetValue(main, true);
        Call(main, "UpdateModeLayout"); Call(main, "RefreshParticleGrid", false, null);
        Field<ListBox>(main, "_editorIndexList").SelectedIndex = 1;
        Call(main, "UpdateButtons");
        var add = Field<Button>(main, "_addRelationshipButton");
        var remove = Field<Button>(main, "_removeRelationshipButton");
        Require(add.Enabled && remove.Enabled, "Initial relationship buttons unavailable");
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Call(main, "SetSimulationButtonLock", true);
            Call(main, "SetSimulationButtonLock", true);
            Require(!add.Enabled && !remove.Enabled, "Relationship buttons available while locked");
            Call(main, "SetSimulationButtonLock", false);
            Require(add.Enabled && remove.Enabled, "Nested button state was not restored");
            Require(!Field<Button>(main, "_helperAddBoneButton").Enabled, "Originally disabled button became enabled");
            Call(main, "ToggleSimulation");
            Require(Field<System.Windows.Forms.Timer>(main, "_simulationTimer").Enabled, "HKCL simulation did not start");
            Require(!add.Enabled && !remove.Enabled, "Running simulation left relationship buttons enabled");
            Require(Field<Button>(main, "_documentationButton").Enabled, "Documentation locked during simulation");
            Call(main, "StopSimulation", false);
            Require(add.Enabled && remove.Enabled, "Stopped simulation left relationship buttons disabled");
            Require(add.ForeColor == Color.Gainsboro && remove.ForeColor == Color.Gainsboro, "Restored buttons retain disabled styling");
        }
        Field<DataGridView>(main, "_particleRelationshipGrid").ClearSelection();
        Call(main, "UpdateButtons");
        Require(!remove.Enabled && add.Enabled, "No-selection relationship eligibility");
        main.Opacity = 0; main.Show(); main.WindowState = FormWindowState.Normal;
        foreach (var width in new[] { 1200, 1600 })
        {
            main.Width = width; main.PerformLayout(); Application.DoEvents();
            var documentation = Field<Button>(main, "_documentationButton");
            var right = documentation.Parent!;
            var toolbar = right.Parent!;
            Require(right.ClientRectangle.Contains(documentation.Bounds), "Documentation toolbar button clipped");
            Require(right.ClientRectangle.Contains(Field<Button>(main, "_settingsButton").Bounds), "Settings toolbar button clipped");
            foreach (var panel in toolbar.Controls.OfType<FlowLayoutPanel>())
                foreach (Control control in panel.Controls)
                    if (control.Visible) Require(panel.ClientRectangle.Contains(control.Bounds), $"Toolbar clips {control.Text} at width {width}");
            using var bitmap = new Bitmap(toolbar.Width, toolbar.Height);
            toolbar.DrawToBitmap(bitmap, new Rectangle(Point.Empty, toolbar.Size));
            bitmap.Save(Path.Combine(Path.GetDirectoryName(path)!, $"documentation-toolbar-{width}.png"));
        }
    }

    private static void CheckDocumentation(string directory)
    {
        using var dialog = new ClothDocumentationDialog();
        dialog.Opacity = 0; dialog.Show(); Application.DoEvents();
        var topics = Field<ListBox>(dialog, "_topics");
        var search = Field<TextBox>(dialog, "_search");
        var article = Field<RichTextBox>(dialog, "_article");
        void CheckHeading()
        {
            article.Select(0, ((ClothHelpTopic)topics.SelectedItem!).Title.Length);
            Require(article.SelectionFont is { Bold: true } titleFont && titleFont.SizeInPoints > article.Font.SizeInPoints,
                "Documentation heading lost its first-open formatting");
            article.Select(0, 0);
        }
        CheckHeading();
        Call(dialog, "ApplyLocalization");
        CheckHeading();
        using (var firstOpen = new Bitmap(dialog.Width, dialog.Height))
        {
            dialog.DrawToBitmap(firstOpen, new Rectangle(Point.Empty, dialog.Size));
            firstOpen.Save(Path.Combine(directory, "documentation-first-open.png"));
        }
        Require(topics.Items.Count == ClothDocumentationDialog.Topics.Count && topics.Items.Count >= 20, "Documentation topics missing");
        foreach (var kind in ClothConstraintCatalog.Kinds)
            Require(ClothDocumentationDialog.Topics.Any(t => t.Body.Contains(kind.ClassName)), "Constraint documentation missing");
        search.Text = "inverse mass";
        Require(topics.Items.Count > 0 && topics.Items.Count < ClothDocumentationDialog.Topics.Count, "Documentation search did not filter");
        search.Text = "no-such-topic-xyz";
        Require(topics.Items.Count == 0 && article.Text.Contains("No matching topics"), "Empty search state");
        search.Text = "";
        topics.SelectedIndex = 7;
        foreach (var size in new[] { new Size(1000, 680), new Size(764, 461) })
        {
            dialog.ClientSize = size; dialog.PerformLayout(); Application.DoEvents();
            Require(article.Width > 300 && article.Height > 250, "Documentation article clipped");
            Require(search.Parent!.ClientRectangle.Contains(search.Bounds), "Search outside window");
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
            bitmap.Save(Path.Combine(directory, $"documentation-{size.Width}.png"));
        }
    }

    private static void Validate(NativeBphclDocument original, NativeBphclDocument document, int cloth, int count, bool saved = false)
    {
        Require(BphclSchemaAudit.ValidateArrayStorageTypes(document).Count == 0, "Array storage type mismatch");
        var before = original.Cloths[cloth].SimCloths[0];
        var sim = document.Cloths[cloth].SimCloths[0];
        Require(sim.Particles.Count == count, "Particle count");
        Require(sim.TriangleIndices.SequenceEqual(before.TriangleIndices), "Triangles modified");
        Require(sim.ConstraintSets.Count == before.ConstraintSets.Count, "Constraints modified");
        Require(sim.StaticCollisionMasks.Count == count || sim.StaticCollisionMasks.Count == 0, "Mask count");
        Require(sim.Runtime.PerParticlePinchDetectionEnabledFlags.Count == count || sim.Runtime.PerParticlePinchDetectionEnabledFlags.Count == 0, "Pinch count");
        foreach (var member in new uint[] { 24, 240, 256, 272, 288 })
        {
            if (!document.TryGetReferencedItem(sim.DataOffset + 432 + member, out var arrayIndex)) continue;
            var array = document.GetItem(arrayIndex);
            Require(array.Count == count, "Virtual-collision lookup length");
            for (int particle = before.Particles.Count; particle < count; particle++)
                Require(BitConverter.ToUInt16(document.Bytes, document.DataSection!.PayloadOffset + (int)array.DataOffset + particle * 2) == ushort.MaxValue,
                    "New particle points into an unrelated virtual-collision fan");
        }
        Require(Math.Abs(sim.Runtime.TotalMass - sim.Particles.Sum(p => p.Mass)) < 0.0001, "Total mass");
        Require(Math.Abs(sim.Runtime.MaxParticleRadius - sim.Particles.Max(p => p.Radius)) < 0.0001, "Maximum radius");
        for (int i = 0; i < before.Particles.Count; i++)
            Require(document.Cloths[cloth].SimCloths[0].Particles[i].Position == before.Particles[i].Position, "Existing pose changed");
        var aampBefore = saved ? NativeAampClothMerger.RepairSuffixedClothBaseBones(original)
            : original.Bytes.AsSpan((int)original.Header.ParameterOffset, (int)original.Header.ParameterSize).ToArray();
        Require(aampBefore.AsSpan().SequenceEqual(document.Bytes.AsSpan((int)document.Header.ParameterOffset, (int)document.Header.ParameterSize)), "AAMP metadata changed");
        var mutable = document.Cloths[cloth].Operators.Where(o => o.ClassName is "hclGatherAllVerticesOperator" or "hclCopyVerticesOperator" or "hclMoveParticlesOperator")
            .Select(o => o.ItemIndex).Concat(document.Cloths[cloth].BufferDefinitions.Where(b => b.Type is 1 or 2).Select(b => b.ItemIndex))
            .Append(sim.ItemIndex).ToHashSet();
        // Every pre-existing object outside the explicitly edited records remains byte-identical.
        // Existing array payloads are kept at their original addresses; new allocations replace pointers.
        for (int index = 1; index < original.Items.Count; index++)
        {
            var item = original.Items[index];
            if (mutable.Contains(index) || item.Count != 1) continue;
            var name = original.GetTypeName(item.TypeIndex);
            if (name is "hclSimClothPose") continue;
            var definition = original.TypeDefinitions.FirstOrDefault(t => t.TypeIndex == item.TypeIndex);
            if (definition?.Size is not uint size || size == 0) continue;
            var next = document.GetItem(index);
            Require(original.Bytes.AsSpan(original.DataSection!.PayloadOffset + (int)item.DataOffset, (int)size)
                .SequenceEqual(document.Bytes.AsSpan(document.DataSection!.PayloadOffset + (int)next.DataOffset, (int)size)), $"Untouched ITEM {index} ({name}) modified");
        }
        foreach (var buffer in document.Cloths[cloth].BufferDefinitions.Where(b => b.Type is 1 or 2 && b.SubType == 0))
        {
            Require(buffer.VertexCount == count, "Buffer length");
            foreach (var op in document.Cloths[cloth].Operators.Where(o => o.OutputBufferIndex == buffer.Index))
            {
                Require(op.ClassName == "hclGatherAllVerticesOperator" && op.VertexInputFromVertexOutput.Count == count, "Initialization mapping length");
                var input = document.Cloths[cloth].BufferDefinitions.Single(b => b.Index == op.InputBufferIndex);
                Require(op.VertexInputFromVertexOutput.All(v => v >= 0 && v < input.VertexCount), "Initialization mapping range");
            }
        }
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void CheckLinkPolicies(NativeBphclDocument document)
    {
        var config = new NativeBphclSimulateConfig(Array.Empty<int>(), Array.Empty<int>(), 1, 1, true, false);
        Require(NativeBphclParticleEditor.ExecutesSet(config, 3), "Empty schedule must execute all static sets");
        Require(!NativeBphclParticleEditor.ExecutesSet(config with { ConstraintExecution = new[] { -1, 2, 2 } }, 3), "Collision/repeated entries must not activate another set");
        foreach (var set in document.Cloths.SelectMany(c => c.SimCloths).SelectMany(s => s.ConstraintSets)
                     .Where(NativeBphclParticleEditor.SupportsLinkEditing))
        {
            var link = set.Links.FirstOrDefault(l => l.ParticleA.HasValue && l.ParticleB.HasValue && l.ParticleA != l.ParticleB);
            if (link is null) continue;
            var single = set with { Links = new[] { link } };
            Require(NativeBphclParticleEditor.ContainsLink(single, link.ParticleA!.Value, link.ParticleB!.Value), "Forward link missing");
            Require(NativeBphclParticleEditor.ContainsLink(single, link.ParticleB.Value, link.ParticleA.Value)
                == (set.ClassName != "hclStretchLinkConstraintSet"), "Stretch direction was discarded");
        }
    }

    private static void CheckBendRemoval(string path, string directory, NativeBphclDocument original)
    {
        var service = new HkclService(); service.Load(path);
        var snapshot = service.CaptureState();
        for (int cloth = 0; cloth < original.Cloths.Count; cloth++)
        foreach (var set in original.Cloths[cloth].SimCloths.FirstOrDefault()?.ConstraintSets ?? Array.Empty<NativeBphclConstraintSet>())
        {
            if (set.ClassName != "hclBendLinkConstraintSet" || set.IsAntiPinch || set.Links.Count == 0) continue;
            foreach (var index in new[] { 0, set.Links.Count / 2, set.Links.Count - 1 }.Distinct())
            {
                service.RestoreState(snapshot);
                var row = service.GetParticleRelationships(cloth, set.Links[index].ParticleA!.Value)
                    .Single(r => r.ConstraintSetIndex == set.Index && r.LinkIndex == index);
                Require(service.CanDeleteParticleRelationship(cloth, row), "Bend-link removal disabled");
                service.DeleteParticleRelationship(cloth, row);
                var changed = service.CaptureBphclNativeDocument()!;
                var schemaErrors = BphclSchemaAudit.ValidateArrayStorageTypes(changed);
                Require(schemaErrors.Count == 0, $"Bend removal changed array schema in {path}, cloth {cloth}, set {set.Index}, link {index}: {string.Join(';', schemaErrors)}");
                var output = Path.Combine(directory, "bend-removal.bphcl");
                service.SaveHkcl(output, HkclPlatform.Switch);
                var reload = new HkclService(); reload.Load(output);
                var result = reload.CaptureBphclNativeDocument()!.Cloths[cloth].SimCloths[0].ConstraintSets[set.Index];
                var expected = set.Links.Where((_, i) => i != index).ToArray();
                Require(result.Links.Count == expected.Length, "Bend removal count");
                for (int i = 0; i < expected.Length; i++)
                    Require(result.Links[i].ParticleA == expected[i].ParticleA && result.Links[i].ParticleB == expected[i].ParticleB &&
                        result.Links[i].Values.OrderBy(p => p.Key).SequenceEqual(expected[i].Values.OrderBy(p => p.Key)), "Surviving bend link changed");
                // Every existing DATA byte is preserved except the edited set's links hkArray.
                var beforeData = original.Bytes.AsSpan(original.DataSection!.PayloadOffset, original.DataSection.PayloadSize);
                var afterData = changed.Bytes.AsSpan(changed.DataSection!.PayloadOffset, beforeData.Length);
                for (int i = 0; i < beforeData.Length; i++)
                    if (i < set.DataOffset + 40 || i >= set.DataOffset + 56)
                        Require(beforeData[i] == afterData[i], "Bend deletion modified unrelated DATA");
                service.RestoreState(snapshot);
                Require(service.CaptureBphclNativeDocument()!.Bytes.AsSpan().SequenceEqual(original.Bytes), "Bend deletion snapshot restore");
            }
        }
    }
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void CheckUi(string path)
    {
        using var main = new MainForm();
        Call(main, "LoadCurrent", path);
        Field<ListBox>(main, "_clothList").SelectedIndex = 0;
        typeof(MainForm).GetField("_directEditMode", Private)!.SetValue(main, true);
        Call(main, "UpdateModeLayout");
        Call(main, "RefreshParticleGrid", false, null);
        Call(main, "UpdateButtons");
        Require(Field<Button>(main, "_addEditorItemButton").Enabled, "Add button disabled");
        Require(!(bool)Call(main, "CanRunSimulation")!, "BPHCL Space shortcut permits simulation");
        Require(!Field<Button>(main, "_simulationButton").Enabled, "BPHCL simulation button enabled");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Call(main, "ToggleSimulation");
            Call(main, "UpdateButtons");
            Require(!Field<System.Windows.Forms.Timer>(main, "_simulationTimer").Enabled, "BPHCL simulation timer started");
            Require(Field<Dictionary<Button, bool>>(main, "_simulationButtonStates").Count == 0, "BPHCL simulation locked editor controls");
            Require(Field<Button>(main, "_addEditorItemButton").Enabled, "BPHCL simulation attempt disabled editing");
        }
        var service = Field<HkclService>(main, "_current");
        var count = service.GetParticleRows(0).Count;
        Call(main, "AddEditorItemForCurrentTab", new object?[] { null });
        Require(service.GetParticleRows(0).Count == count + 1, "UI Add did not add");
        Call(main, "CopySelectedEditorItem");
        Call(main, "PasteEditorItem", true);
        Require(service.GetParticleRows(0).Count == count + 2, "UI paste did not add");
        Call(main, "UndoEditorChange");
        Require(service.GetParticleRows(0).Count == count + 1, "Undo");
        Call(main, "RedoEditorChange");
        Require(service.GetParticleRows(0).Count == count + 2, "Redo");
        Field<ListBox>(main, "_editorIndexList").SelectedIndex = 0;
        Call(main, "FillParticleDetailGrid", service.GetParticleRows(0)[0]);
        var grid = Field<DataGridView>(main, "_editorDetailGrid");
        Require(grid.Rows.Cast<DataGridViewRow>().Any(r => (string?)r.Cells[0].Value == "Fixed" && !r.Cells[1].ReadOnly), "Fixed disabled");
        Require(grid.Rows.Cast<DataGridViewRow>().Any(r => (string?)r.Cells[0].Value == "Colliders" && r.Cells[1] is DataGridViewButtonCell), "Collider selector missing");
        var originalFixed = service.GetParticleRows(0)[0].Fixed;
        var undoCount = Field<System.Collections.IEnumerable>(main, "_undoStack").Cast<object>().Count();
        for (var click = 0; click < 2; click++)
        {
            var fixedRow = grid.Rows.Cast<DataGridViewRow>().Single(r => (string?)r.Cells[0].Value == "Fixed");
            typeof(DataGridView).GetMethod("OnCellMouseClick", Private)!.Invoke(grid, new object[] {
                new DataGridViewCellMouseEventArgs(1, fixedRow.Index, 5, 5, new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)) });
            Require(service.GetParticleRows(0)[0].Fixed == (click == 0 ? !originalFixed : originalFixed), "Fixed checkbox needs a second click");
            Require(!grid.IsCurrentCellInEditMode, "Checkbox entered deferred text edit mode");
            Require(Field<System.Collections.IEnumerable>(main, "_undoStack").Cast<object>().Count() == undoCount + click + 1, "Checkbox committed more than once");
        }
        Call(main, "UndoEditorChange");
        Require(service.GetParticleRows(0)[0].Fixed != originalFixed, "Checkbox undo");
        Call(main, "RedoEditorChange");
        Require(service.GetParticleRows(0)[0].Fixed == originalFixed, "Checkbox redo");
        var page = typeof(MainForm).GetNestedType("EditorPage", BindingFlags.NonPublic)!;
        var snapshot = Call(main, "CaptureFullEditorSnapshot", Enum.Parse(page, "Particles"), 0, 0)!;
        var beforeDelete = service.CaptureState();
        service.DeleteParticles(0, new[] { count, count + 1 });
        var afterDelete = service.CaptureState();
        Call(main, "PushUndo", snapshot);
        Call(main, "UndoEditorChange");
        Require(service.CaptureState() == beforeDelete, "Topology UI undo");
        Call(main, "RedoEditorChange");
        Require(service.CaptureState() == afterDelete, "Topology UI redo");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
