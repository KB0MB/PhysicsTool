using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using SkiaSharp;
using System.Numerics;
using System.Runtime.InteropServices;

internal sealed class OotShirtChecks : Application
{
    internal static string Root = "", Output = "";
    internal static int Result = 1;
    internal static bool MalonSit;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var viewport = new PhysicsViewport { ShowPhysics = false };
        var surface = new ModelGlControl(viewport);
        lifetime.MainWindow = new Window { Width = 1000, Height = 1000, Content = surface, ShowInTaskbar = false };
        var poses = new List<(string Name, VisualModelData Pose)>();
        int stage = 0;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        timeout.Tick += (_, _) => Finish("Shirt comparison timed out"); timeout.Start();
        surface.RenderFailed += Finish;
        lifetime.MainWindow.Opened += async (_, _) =>
        {
            try
            {
                await Task.Run(() =>
                {
                    if (MalonSit) { CompareMalonSit(poses); return; }
                    CheckOutputPolicy();
                    var rig = PortableModelImporter.LoadRig(Path.Combine(Root, "Model/Player_Adult.Player_Adult.bfres.zs"));
                    var resources = OotActorResources.Load(new("Player_Adult", Path.Combine(Root, "Pack/Actor/Player_Adult.pack.zs"), null, Root, true, true, true) { IsOot = true });
                    if (!resources.Assets.Any(a => a.KeepBoneLengthMeshes.Contains("SimulationMesh_Skirt")))
                        throw new Exception("Player_Adult skirt output policy was not loaded.");
                    var clip = resources.AnimationPaths.SelectMany(ModelAnimationClip.Load).First(c => c.Name == "Bottle_Drink");
                    var helper = new OotHelperEvaluator(resources.Assets.Single(a => !a.IsCloth).Bytes, rig.Rest.Bones);
                    var animated = helper.Evaluate(rig.AnimationSkeleton(clip, 181));
                    poses.Add(("animation-helpers", rig.Sample(animated.Bones.Select(b => new VisualBoneOverride(b.Name, b.Position, b.Rotation, b.Scale)).ToArray())));
                    foreach (bool preserve in new[] { false, true })
                    {
                        var assets = preserve ? resources.Assets : resources.Assets.Select(a => a with { KeepBoneLengthMeshes = new HashSet<string>() });
                        var scene = new OotActorPreview(assets);
                        IReadOnlyList<VisualBoneOverride> output = [];
                        for (int frame = 0; frame <= 181; frame++)
                        {
                            var input = helper.Evaluate(rig.AnimationSkeleton(clip, frame, scene.DrivenNames));
                            output = scene.Update(rig.Rest, input, true, frame == 0 ? 0 : 1f / 60, frame == 0, false, frame == 181);
                        }
                        if (preserve)
                        {
                            var byName = output.ToDictionary(b => b.Name);
                            foreach (var b in rig.Rest.Bones.Where(b => b.Name.StartsWith("Ph_SkirtAdult_")))
                            {
                                var parent = rig.Rest.Bones.FirstOrDefault(p => p.Index == b.ParentIndex);
                                if (parent == null || !parent.Name.StartsWith("Ph_SkirtAdult_")) continue;
                                if (Math.Abs(Vector3.Distance(byName[b.Name].Position, byName[parent.Name].Position) - Vector3.Distance(b.Position, parent.Position)) > .0001f)
                                    throw new Exception("Skirt bone shortened: " + b.Name);
                            }
                        }
                        poses.Add((preserve ? "simulation-corrected" : "simulation-without-actor-policy", rig.Sample(output)));
                    }
                    Console.WriteLine("PASS: actor policy loading, hierarchy order, scale, opt-out, source immutability and Player_Adult Bottle_Drink bone lengths.");
                });
                viewport.SetModel(poses[0].Pose); viewport.FrameModel();
                if (MalonSit) viewport.Orbit(.8f, 0);
                Request();
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        surface.FrameCaptured += (pixels, width, height) =>
        {
            try
            {
                using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, bitmap.GetPixels() + (height - 1 - y) * bitmap.RowBytes, width * 4);
                using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(Output, poses[stage].Name + ".png"), png.ToArray());
                if (++stage == poses.Count) { Result = 0; Finish(null); return; }
                viewport.SetModelPose(poses[stage].Pose);
                Dispatcher.UIThread.Post(Request, DispatcherPriority.Background);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        void Request() { surface.CaptureNextFrame = true; surface.RequestNextFrameRendering(); }
        void Finish(string? error) { timeout.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }

    private static void CompareMalonSit(List<(string Name, VisualModelData Pose)> poses)
    {
        var resources = OotActorResources.Load(new("Npc_Hylia_Malon_Adult", Path.Combine(Root, "Pack/Actor/Npc_Hylia_Malon_Adult.pack.zs"), null, Root, true, true, true) { IsOot = true });
        var rig = PortableModelImporter.LoadRig(resources.ModelPath!);
        var clip = resources.AnimationPaths.SelectMany(ModelAnimationClip.Load).Single(c => c.Name == "Act_SitChair_Wait");
        var helper = new OotHelperEvaluator(resources.Assets.Single(a => !a.IsCloth).Bytes, rig.Rest.Bones);
        VisualModelData Sample(VisualModelData skeleton) => rig.Sample(skeleton.Bones.Select(b => VisualBoneTransform.Override(b.Name, b.WorldMatrix)).ToArray());
        var raw = rig.AnimationSkeleton(clip, 70);
        poses.Add(("sit-raw-animation", Sample(raw)));
        poses.Add(("sit-animation-helpers", Sample(helper.Evaluate(raw))));
        var helped = helper.Evaluate(raw);
        foreach (var asset in resources.Assets.Where(a => a.IsCloth))
        {
            var document = new HkclService();
            document.RestoreState("BPHCL:" + Convert.ToBase64String(asset.Bytes));
            for (int i = 0; i < document.GetClothSummaries().Count; i++)
            {
                if (!document.GetClothName(i).Contains("Skirt")) continue;
                var source = document.GetTotkSimulationPreview(i);
                var clothBind = BoneEditGeometry.WorldTransforms(document.GetBoneRows(i));
                Vector3 Reference(ParticlePreviewLocalRange range, VisualModelData pose, bool useClothBind = false)
                {
                    Vector3 sum = default;
                    float total = 0;
                    foreach (var weight in range.ReferenceBoneWeights)
                    {
                        var name = source.Bones.Single(b => b.Index == weight.BoneIndex).Name;
                        var rest = rig.Rest.Bones.Single(b => b.Name == name);
                        var target = pose.Bones.Single(b => b.Name == name);
                        if (!Matrix4x4.Invert(useClothBind ? clothBind[weight.BoneIndex] : rest.WorldMatrix, out var inverse)) throw new Exception("Singular reference bone.");
                        sum += Vector3.Transform(weight.ReferencePosition!.Value, inverse * target.WorldMatrix) * weight.Weight;
                        total += weight.Weight;
                    }
                    return sum / total;
                }
                var referenceChanges = source.LocalRanges.Select(r => new
                {
                    r.ParticleIndex, r.MaximumDistance,
                    Raw = Reference(r, raw), Helped = Reference(r, helped),
                    HelperShift = Vector3.Distance(Reference(r, raw), Reference(r, helped)),
                    DirectClothBindReference = Reference(r, helped, true),
                    BindReferenceDifference = Vector3.Distance(Reference(r, helped), Reference(r, helped, true))
                }).ToArray();
                if (referenceChanges.Length == 0 || referenceChanges.Any(r =>
                    !float.IsFinite(r.BindReferenceDifference) || r.BindReferenceDifference > 1e-5f))
                    throw new Exception("Malon skirt cloth/model bind references no longer agree.");
                Console.WriteLine($"Malon local-range bind comparison: {referenceChanges.Length} references; " +
                    $"maximum difference={referenceChanges.Max(r => r.BindReferenceDifference):G9}");
                File.WriteAllText(Path.Combine(Output, "sit-skirt-routing.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    Runtime = document.GetClothRuntimeData(i),
                    Skin = document.GetSkinOperatorData(i),
                    Ranges = document.GetLocalRangeConstraintSets(i),
                    Preview = source,
                    ReferenceChanges = referenceChanges
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IncludeFields = true,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
            }
        }
        CheckMalonHelperLimits(resources.Assets.Single(a => !a.IsCloth).Bytes, helped);
        File.WriteAllText(Path.Combine(Output, "sit-helper-bones.json"), System.Text.Json.JsonSerializer.Serialize(
            raw.Bones.Zip(helped.Bones).Select(pair => new
            {
                pair.First.Name,
                TranslationDelta = Vector3.Distance(pair.First.Position, pair.Second.Position),
                RotationDeltaRadians = 2 * MathF.Acos(Math.Clamp(Math.Abs(Quaternion.Dot(pair.First.Rotation, pair.Second.Rotation)), 0, 1)),
                Before = pair.First.Position.ToString(), After = pair.Second.Position.ToString()
            }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        foreach (var mode in new[] { "current", "animated-input", "without-length-policy", "without-colliders", "without-helpers", "without-local-range", "without-links", "without-bending" })
        {
            bool excludeDriven = mode != "animated-input";
            var assets = mode == "without-length-policy" ? resources.Assets.Select(a => a with { KeepBoneLengthMeshes = new HashSet<string>() }) : resources.Assets;
            var scene = new OotActorPreview(assets);
            if (mode == "without-colliders")
            {
                // Diagnostic ablation only: do not introduce a production collision bypass.
                var field = typeof(HkclPreviewSimulator).GetField("_colliders", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                scene.Configure(s => field.SetValue(s, Array.Empty<ColliderPreviewShape>()));
            }
            if (mode is "without-local-range" or "without-links" or "without-bending" or "current")
                scene.Configure(s =>
                {
                    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var sets = (IReadOnlyDictionary<int, ParticlePreviewConstraintSet>)typeof(HkclPreviewSimulator).GetField("_constraintSets", flags)!.GetValue(s)!;
                    if (mode == "current") { Console.WriteLine("Constraint sets: " + string.Join(", ", sets.Values.Select(v => v.Kind))); return; }
                    var field = typeof(HkclPreviewSimulator).GetField("_constraintExecution", flags)!;
                    var order = (IReadOnlyList<int>)field.GetValue(s)!;
                    bool Remove(string kind) => mode switch
                    {
                        "without-local-range" => kind.Contains("LocalRange"),
                        "without-links" => kind.Contains("Link"),
                        "without-bending" => kind.Contains("Bend"),
                        _ => false
                    };
                    field.SetValue(s, order.Where(i => i < 0 || !sets.TryGetValue(i, out var set) || !Remove(set.Kind)).ToArray());
                });
            IReadOnlyList<VisualBoneOverride> output = [];
            for (int frame = 0; frame <= 70; frame++)
            {
                var input = rig.AnimationSkeleton(clip, frame, excludeDriven ? scene.DrivenNames : null);
                if (mode != "without-helpers") input = helper.Evaluate(input);
                output = scene.Update(rig.Rest, input, true, frame == 0 ? 0 : 1f / 60, frame == 0, false, frame == 70);
            }
            poses.Add(("sit-simulation-" + mode, rig.Sample(output)));
            if (mode == "current")
            {
                var input = helper.Evaluate(rig.AnimationSkeleton(clip, 70, scene.DrivenNames));
                for (int step = 0; step < 600; step++) output = scene.Update(rig.Rest, input, true, 1f / 60, false, false, step == 599);
                poses.Add(("sit-simulation-settled", rig.Sample(output)));
            }
            Console.WriteLine($"Sit frame 70: mode={mode}; cloths={scene.ClothCount}; simulated={scene.SimulatedClothCount}");
        }
        foreach (bool animatedInput in new[] { false, true })
        {
            var scene = new OotActorPreview(resources.Assets);
            var input = helper.Evaluate(rig.AnimationSkeleton(clip, 70, animatedInput ? null : scene.DrivenNames));
            var output = scene.Update(rig.Rest, input, true, 0, true, false, true);
            var prefix = animatedInput ? "sit-reset-animated" : "sit-reset-current";
            poses.Add((prefix, rig.Sample(output)));
            for (int step = 1; step <= 60; step++)
            {
                output = scene.Update(rig.Rest, input, true, 1f / 60, false, false, true);
                if (step is 1 or 3 or 60) poses.Add(($"{prefix}-step{step}", rig.Sample(output)));
            }
            scene.Configure(s => Console.WriteLine($"Reset diagnostic {prefix}: resets={s.NumericalResetCount}; bindingFallbacks={s.LastBindingFallbackCount}"));
        }
        if (poses.Any(p => p.Pose.Meshes.Any(m => m.Vertices.Any(v => !float.IsFinite(v)))))
            throw new Exception("Nonfinite sitting pose.");
    }

    private static void CheckMalonHelperLimits(byte[] bytes, VisualModelData pose)
    {
        var root = BymlLibrary.Byml.FromBinary(bytes).GetMap();
        var names = root["BoneNames"].GetArray().Select(n => n.GetString()).ToArray();
        var bones = pose.Bones.ToDictionary(b => b.Name);
        var drivers = root["DriverParams"].GetArray();
        uint[] attributes = [0x7273210a, 0x26df7675, 0x6de5cadd, 0xa13adc76, 0x509c8278, 0x3f3a1269, 0x4ee71a4f, 0x0bce334d, 0xbb47bf0a];
        Vector3 Vector(BymlLibrary.Byml n) { var m = n.GetMap(); return new(m["X"].GetFloat(), m["Y"].GetFloat(), m["Z"].GetFloat()); }
        float[] Driver(int id)
        {
            var d = drivers[id].GetMap();
            var b = bones[names[d["BoneNameId"].GetInt()]];
            int baseId = d["BaseBoneNameId"].GetInt();
            var relative = b.WorldMatrix;
            var basis = baseId == -2 ? pose.Bones.FirstOrDefault(p => p.Index == b.ParentIndex) : baseId >= 0 ? bones[names[baseId]] : null;
            if (basis != null && Matrix4x4.Invert(basis.WorldMatrix, out var inverse)) relative *= inverse;
            VisualBoneTransform.Decompose(relative, out var scale, out var rotation, out var position);
            var q = d["BaseRotate"].GetArray().Select(v => v.GetFloat()).ToArray();
            var angles = SidecarPreviewEvaluator.MeasureBotwRotation(Quaternion.Normalize(Quaternion.Inverse(Quaternion.Normalize(new(q[0], q[1], q[2], q[3]))) * rotation), Vector(d["AimAxis"]), Vector(d["UpAxis"]));
            var t = position - Vector(d["BaseTranslate"]);
            return [angles.X, angles.Y, angles.Z, t.X, t.Y, t.Z, scale.X, scale.Y, scale.Z];
        }
        foreach (var node in root["DrivenParams"].GetArray())
        {
            var group = node.GetMap();
            foreach (string name in OotHelperDocument.Channels)
            {
                if (!group.TryGetValue(name, out var channelNode)) continue;
                var channel = channelNode.GetMap();
                if (!channel.ContainsKey("MinValue") && !channel.ContainsKey("MaxValue")) continue;
                float sum = 0;
                if (channel.TryGetValue("InConnections", out var connections))
                    foreach (var item in connections.GetArray())
                    {
                        var c = item.GetMap();
                        var values = c["DrivenCurve"].GetMap()["Data"].GetArray().Select(v => v.GetFloat()).ToArray();
                        var keys = Enumerable.Range(0, values.Length / 4).Select(i => new Vector4(values[i * 4], values[i * 4 + 1], values[i * 4 + 2], values[i * 4 + 3])).ToArray();
                        sum += SidecarPreviewEvaluator.EvaluateTotkCurve(keys, Driver(c["DriverParamId"].GetInt())[Array.IndexOf(attributes, c["DriverAttribute"].GetUInt32())]);
                    }
                float min = channel.TryGetValue("MinValue", out var lo) ? lo.GetFloat() : float.NegativeInfinity;
                float max = channel.TryGetValue("MaxValue", out var hi) ? hi.GetFloat() : float.PositiveInfinity;
                Console.WriteLine($"Helper limit: {names[group["BoneNameId"].GetInt()]} {name}: sum={sum:R}, min={min:R}, max={max:R}, exceeds={sum < min || sum > max}");
            }
        }
    }

    internal static void CheckOutputPolicy()
    {
        var pose = new VisualModelData();
        pose.Bones.AddRange([
            new() { Index = 2, ParentIndex = 1, Name = "Tip", Position = new(4, 0, 0), Scale = new(2) },
            new() { Index = 1, ParentIndex = 0, Name = "Middle", Position = new(2, 0, 0), Scale = new(2) },
            new() { Index = 0, ParentIndex = -1, Name = "Root", Scale = new(2) }]);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var output = pose.Bones.ToDictionary(b => b.Name, b => new VisualBoneOverride(b.Name, new(10, 0, 0), rotation, new(2)));
        var original = new Dictionary<string, VisualBoneOverride>(output);
        OotActorPreview.PreserveBoneOffsets(pose, output, []);
        if (output.Any(p => p.Value != original[p.Key])) throw new Exception("Unrequested bone correction.");
        OotActorPreview.PreserveBoneOffsets(pose, output, [new(StringComparer.OrdinalIgnoreCase) { "Root", "Middle", "Tip" }]);
        if (Vector3.Distance(output["Middle"].Position, new(10, 2, 0)) > .0001f ||
            Vector3.Distance(output["Tip"].Position, new(10, 4, 0)) > .0001f || output["Root"] != original["Root"])
            throw new Exception("Bone offset correction lost hierarchy, anchor or scale.");
        if (pose.Bones[0].Position != new Vector3(4, 0, 0) || output.Values.Any(v => v.Rotation != rotation || v.Scale != new Vector3(2)))
            throw new Exception("Bone offset correction changed input, rotation or scale.");
        output = new(original);
        OotActorPreview.PreserveBoneOffsets(pose, output, [new() { "Root", "Middle" }, new() { "Tip" }]);
        if (output["Tip"] != original["Tip"] || output["Root"] != original["Root"])
            throw new Exception("Bone correction crossed cloth ownership or changed its root.");
        Console.WriteLine("PASS: OoT actor bone-length policy, hierarchy, scale, opt-out and cloth ownership.");
    }
}
