using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;
using static HKCLTool.BotwAampWriter;

internal static class SupportHierarchyChecks
{
    public static void Run()
    {
        TotkHierarchy();
        TotkNativeMath();
        var root = new AampListNode("param_root");
        var header = new AampObjectNode("support_bone_header"); root.Objects.Add(header);
        foreach (var (name, count) in new[] { ("bone_num", 4), ("connection_linear_num", 3), ("connection_curve_num", 0),
            ("output_single_num", 4), ("output_double_num", 1), ("main_bone_num", 2), ("support_bone_num", 3) })
            header.Parameters.Add(AampParameterNode.Integer(name, count));
        var data = new AampListNode("support_bone_data"); root.Children.Add(data);
        AampListNode List(string name) { var list = new AampListNode(name); data.Children.Add(list); return list; }
        var names = new[] { "Driver", "Parent", "Child", "Leaf" };
        var boneList = List("bone_list");
        foreach (var name in names) { var b = new AampObjectNode("bone_" + boneList.Objects.Count); b.Parameters.Add(AampParameterNode.String256Value("name", name)); boneList.Objects.Add(b); }
        var linears = List("connection_linear_list");
        var singles = List("output_single_list");
        for (int i = 0; i < 2; i++)
        {
            var linear = new AampObjectNode("connection_linear_" + i); linears.Objects.Add(linear);
            linear.Parameters.Add(AampParameterNode.Integer("bone_attribute", (i << 16) | 3));
            linear.Parameters.Add(AampParameterNode.Raw(Crc32("slope_intercept"), 3, new[] { 1f, 0f }.SelectMany(BitConverter.GetBytes).ToArray()));
            var output = new AampObjectNode("output_single_" + i); singles.Objects.Add(output);
            output.Parameters.Add(AampParameterNode.Integer("connection", i)); output.Parameters.Add(AampParameterNode.Single("weight", 1));
        }
        var main = List("main_bone_list"); var support = List("support_bone_list");
        var zero = new AampObjectNode("output_single_2"); singles.Objects.Add(zero);
        zero.Parameters.Add(AampParameterNode.Integer("connection", 0)); zero.Parameters.Add(AampParameterNode.Single("weight", 0));
        var rotationInput = new AampObjectNode("connection_linear_2"); linears.Objects.Add(rotationInput);
        rotationInput.Parameters.Add(AampParameterNode.Integer("bone_attribute", 1 << 16));
        rotationInput.Parameters.Add(AampParameterNode.Raw(Crc32("slope_intercept"), 3, new[] { 1f, 0f }.SelectMany(BitConverter.GetBytes).ToArray()));
        var rotationOutput = new AampObjectNode("output_single_3"); singles.Objects.Add(rotationOutput);
        rotationOutput.Parameters.Add(AampParameterNode.Integer("connection", 2)); rotationOutput.Parameters.Add(AampParameterNode.Single("weight", 0));
        var dual = new AampObjectNode("output_double_0"); List("output_double_list").Objects.Add(dual);
        dual.Parameters.Add(AampParameterNode.Integer("connection_0", 0)); dual.Parameters.Add(AampParameterNode.Single("weight_0", 0));
        dual.Parameters.Add(AampParameterNode.Integer("connection_1", 1)); dual.Parameters.Add(AampParameterNode.Single("weight_1", 1));
        void Pose(AampListNode list, int bone, int space, Vector3 position, int output = -1)
        {
            var p = new AampObjectNode((list == main ? "main_bone_" : "support_bone_") + list.Objects.Count); list.Objects.Add(p);
            p.Parameters.Add(AampParameterNode.Integer("bone", bone)); p.Parameters.Add(AampParameterNode.Integer("space", space));
            p.Parameters.Add(AampParameterNode.Vector3("base_translate", position)); p.Parameters.Add(AampParameterNode.Vector4("base_rotate", Vector4.UnitW));
            p.Parameters.Add(AampParameterNode.Integer("translateX", output));
        }
        Pose(main, 0, -2, Vector3.Zero); Pose(main, 2, -2, Vector3.Zero);
        Pose(support, 1, -2, Vector3.Zero, 0);
        // Sample Child rotation before it moves; its later translation sample
        // must remain independent of that rotation cache.
        support.Objects[0].Parameters.Add(AampParameterNode.Integer("bendH", 3));
        Pose(support, 2, 1, Vector3.UnitX, 2);
        Pose(support, 3, -2, Vector3.Zero, 0x10000);
        var document = BphyssbBridge.LoadBytes(WriteArchive("physsb", root), "hierarchy.bphyssb");
        var original = new[] {
            new VisualModelBone { Index = 0, Name = "Driver", Position = Vector3.UnitX },
            new VisualModelBone { Index = 1, Name = "Parent" },
            new VisualModelBone { Index = 2, ParentIndex = 1, Name = "Child", Position = Vector3.UnitX },
            new VisualModelBone { Index = 3, Name = "Leaf" },
            new VisualModelBone { Index = 4, ParentIndex = 2, Name = "UnlistedChild", Position = Vector3.UnitX * 2 }
        };
        var groups = BphyssbBridge.GetDriverGroups(document);
        var group = groups.Single(g => g.DrivenBoneNames.Contains("Child"));
        var result = SidecarPreviewEvaluator.Evaluate(document, group.Index, original, original);
        var cached = SidecarPreviewEvaluator.Evaluate(document, group.Index, original, original, groups);
        if (!result.Poses.SequenceEqual(cached.Poses)) throw new Exception("Cached helper metadata changed evaluated poses.");
        var child = result.Poses.Single(p => p.IsDriven && p.Name == "Child");
        var leaf = result.Poses.Single(p => p.IsDriven && p.Name == "Leaf");
        if (Vector3.Distance(child.Position, Vector3.UnitX * 2) > 1e-5f) throw new Exception("Helper child ignored an earlier parent output in the same group.");
        if (Vector3.Distance(leaf.Position, Vector3.UnitX * 2) > 1e-5f) throw new Exception("Helper driver sampled before its preceding support output.");
        if (original[1].Position != Vector3.Zero || original[2].Position != Vector3.UnitX || original[4].Position != Vector3.UnitX * 2)
            throw new Exception("Helper evaluation mutated the input/rest skeleton.");
        var service = new HkclService(); service.RestoreState("BPHYSSB:" + Convert.ToBase64String(WriteArchive("physsb", root)));
        var state = service.CaptureState();
        var preview = HelperPoseEvaluation.Evaluate(service, original, original);
        if (Vector3.Distance(preview.Poses.Single(b => b.Name == "UnlistedChild").Position, Vector3.UnitX * 3) > 1e-5f)
            throw new Exception("Standalone helper preview failed to move an unlisted child with its affected parent.");
        if (service.CaptureState() != state || preview.Missing.Count > 0) throw new Exception("Helper preview mutated the document or missed a present driver.");
        Console.WriteLine("PASS: same-group helper parents, dependent driver sampling and input-pose isolation.");
    }

    private static void TotkNativeMath()
    {
        foreach (var space in new[] { -2, -1, 0 })
        foreach (var sign in new[] { -1f, 1f })
        {
            var root = new AampListNode("param_root");
            var data = new AampListNode("helper_bone_data"); root.Children.Add(data);
            AampListNode List(string name) { var list = new AampListNode(name); data.Children.Add(list); return list; }
            void Int(AampObjectNode obj, uint hash, int value) => obj.Parameters.Add(AampParameterNode.Raw(hash, 2, BitConverter.GetBytes(value)));
            var names = new[] { "Root", "Driver", "Helper" };
            var bones = List("bone_list");
            foreach (var name in names)
            {
                var b = new AampObjectNode("bone_" + bones.Objects.Count); bones.Objects.Add(b);
                b.Parameters.Add(AampParameterNode.String256Value("name", name));
            }
            var bind = Quaternion.CreateFromYawPitchRoll(.7f, -.3f, .4f);
            var parentRotation = Quaternion.CreateFromYawPitchRoll(-.2f, .5f, -.1f);
            var parentPosition = new Vector3(2, 3, 4);
            var position = new Vector3(.1f, .2f, .3f);
            var shift = new Vector3(.25f, -.15f, .35f);
            const float h = .9f, v = -.6f, roll = .45f;
            var a = MathF.Tan(h / 2); var bnd = MathF.Tan(v / 2);
            var k = 2 / (1 + a * a + bnd * bnd);
            var direction = new Vector3(a * k, bnd * k, k - 1);
            var swing = Quaternion.Normalize(new Quaternion(-direction.Y, direction.X, 0, 1 + direction.Z));
            var relative = Quaternion.Normalize(bind * swing * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll));
            var worldRotation = space == -1 ? relative : parentRotation * relative;
            var worldPosition = space == -1 ? position + shift : Vector3.Transform(position + shift, parentRotation) + parentPosition;
            var driver = new AampObjectNode("driver_bone_0"); List("driver_bone_list").Objects.Add(driver);
            driver.Parameters.Add(AampParameterNode.Integer("bone_id", 1));
            driver.Parameters.Add(AampParameterNode.Integer("base_bone_id", space));
            driver.Parameters.Add(AampParameterNode.Vector3("base_translate", position));
            driver.Parameters.Add(AampParameterNode.Vector4("base_rotate", new(bind.X, bind.Y, bind.Z, bind.W)));
            var curves = List("connection_curve_list"); var outputs = List("output_list");
            var pose = new AampObjectNode("pose_driven_0"); List("pose_driven_list").Objects.Add(pose);
            pose.Parameters.Add(AampParameterNode.Integer("base_bone_id", space));
            pose.Parameters.Add(AampParameterNode.Vector3("base_translate", position));
            pose.Parameters.Add(AampParameterNode.Vector4("base_rotate", new(bind.X, bind.Y, bind.Z, bind.W)));
            var channels = new uint[] { 2869652774, 3079588026, 122218518, 3595547632, 1861473429, 2084993915 };
            for (int channel = 0; channel < 6; channel++)
            {
                var curve = new AampObjectNode("connection_curve_" + channel); curves.Objects.Add(curve);
                Int(curve, 249727330, 0); Int(curve, 75375380, channel); Int(curve, 139113150, 2);
                curve.Parameters.Add(AampParameterNode.Raw(1692570776, 5, new[] { -2f, -2f, 1f, 1f }.SelectMany(BitConverter.GetBytes).ToArray()));
                curve.Parameters.Add(AampParameterNode.Raw(333816846, 5, new[] { 2f, 2f, 1f, 1f }.SelectMany(BitConverter.GetBytes).ToArray()));
                var output = new AampObjectNode("output_" + channel); outputs.Objects.Add(output);
                Int(output, 2390703269, channel); Int(output, 918772672, -1); Int(pose, channels[channel], channel);
            }
            var driven = new AampObjectNode("driven_bone_0"); List("driven_bone_list").Objects.Add(driven);
            driven.Parameters.Add(AampParameterNode.Integer("bone_id", 2));
            driven.Parameters.Add(AampParameterNode.Integer("rotate_driven_type", 0));
            driven.Parameters.Add(AampParameterNode.Integer("rotate_driven_id", 0));
            var document = BphhbBridge.LoadBytes(WriteArchive("phhb", root), "native-math.bphhb");
            VisualModelBone[] current = [
                new() { Index = 0, ParentIndex = -1, Name = "Root", Position = parentPosition, Rotation = parentRotation },
                new() { Index = 1, ParentIndex = 0, Name = "Driver", Position = worldPosition, Rotation = worldRotation * sign },
                new() { Index = 2, ParentIndex = 0, Name = "Helper", Position = parentPosition, Rotation = parentRotation }
            ];
            // Deliberately unrelated BFRES rest transforms: native drivers use BPHHB's authored base.
            var rest = current.Select(b => new VisualModelBone { Index = b.Index, ParentIndex = b.ParentIndex, Name = b.Name }).ToArray();
            SidecarDriverGroup[] groups = [new(0, [0], [1], [2], ["Driver"], ["Helper"], [0, 1, 2], names, 6, 6)];
            var result = SidecarPreviewEvaluator.Evaluate(document, 0, current, rest, groups).Poses.Single(p => p.IsDriven);
            if (Vector3.Distance(result.Position, worldPosition) > 1e-4f || MathF.Abs(Quaternion.Dot(result.Rotation, worldRotation)) < .99999f)
                throw new Exception($"TotK authored rest/swing-twist/space mismatch: space={space}, sign={sign}, actual={result.Position}, expected={worldPosition}");
        }
        Vector4[] keys = [new(0, 2, 3, 4), new(1, 7, 5, 6)];
        if (SidecarPreviewEvaluator.EvaluateTotkCurve(keys, -1) != -1 || SidecarPreviewEvaluator.EvaluateTotkCurve(keys, 2) != 12)
            throw new Exception("TotK endpoint slope extrapolation changed");
        Console.WriteLine("PASS: TotK authored driver rests, compound swing/twist, quaternion signs, local/world/explicit bases, and curve extrapolation.");
    }

    private static void TotkHierarchy()
    {
        foreach (var duplicateSkeleton in new[] { false, true })
        foreach (var childFirst in new[] { false, true })
        {
            var root = new AampListNode("param_root");
            var data = new AampListNode("helper_bone_data"); root.Children.Add(data);
            AampListNode List(string name) { var list = new AampListNode(name); data.Children.Add(list); return list; }
            var names = new[] { "Root", "Parent", "Child" };
            var list = List("bone_list");
            foreach (var name in names)
            {
                var record = new AampObjectNode("bone_" + list.Objects.Count);
                record.Parameters.Add(AampParameterNode.String256Value("name", name)); list.Objects.Add(record);
            }
            List("connection_curve_list"); List("output_list"); List("driver_bone_list");
            var driven = List("driven_bone_list"); var poses = List("pose_driven_list");
            foreach (var index in childFirst ? new[] { 2, 1 } : new[] { 1, 2 })
            {
                var record = new AampObjectNode("driven_bone_" + driven.Objects.Count); driven.Objects.Add(record);
                record.Parameters.Add(AampParameterNode.Integer("bone_id", index));
                record.Parameters.Add(AampParameterNode.Integer("rotate_driven_type", 0));
                record.Parameters.Add(AampParameterNode.Integer("rotate_driven_id", poses.Objects.Count));
                var pose = new AampObjectNode("pose_driven_" + poses.Objects.Count); poses.Objects.Add(pose);
                pose.Parameters.Add(AampParameterNode.Integer("base_bone_id", index - 1));
                pose.Parameters.Add(AampParameterNode.Vector3("base_translate", Vector3.UnitX));
                pose.Parameters.Add(AampParameterNode.Vector4("base_rotate", Vector4.UnitW));
            }
            var document = BphhbBridge.LoadBytes(WriteArchive("phhb", root), "hierarchy.bphhb");
            var original = new List<VisualModelBone> {
                new() { Index = 0, ParentIndex = -1, Name = "Root" },
                new() { Index = 1, ParentIndex = 0, Name = "Parent" },
                new() { Index = 2, ParentIndex = duplicateSkeleton ? 11 : 1, Name = "Child", Position = Vector3.UnitX }
            };
            if (duplicateSkeleton) original.Add(new() { Index = 11, ParentIndex = 0, Name = "Parent" });
            SidecarDriverGroup[] groups = [new(0, [], [], [1, 2], [], ["Parent", "Child"], [0, 1, 2], names, 0, 0)];
            var result = SidecarPreviewEvaluator.Evaluate(document, 0, original, original, groups);
            SidecarDriverGroup[] split = [
                new(0, [], [], [2], [], ["Child"], [0, 1, 2], names, 0, 0),
                new(1, [], [], [1], [], ["Parent"], [0, 1, 2], names, 0, 0)
            ];
            var whole = SidecarPreviewEvaluator.Evaluate(document, -1, original, original, split);
            if (!whole.Poses.SequenceEqual(result.Poses)) throw new Exception("TotK UI groups changed native helper execution order");
            if (Vector3.Distance(result.Poses.Single(p => p.Name == "Child").Position, Vector3.UnitX * 2) > 1e-5f)
                throw new Exception($"TotK child helper lost its updated parent: duplicate={duplicateSkeleton}, childFirst={childFirst}");
            if (original[1].Position != Vector3.Zero || original[2].Position != Vector3.UnitX)
                throw new Exception("TotK helper evaluation mutated the source skeleton");
        }
        Console.WriteLine("PASS: TotK helper parent/child ordering, equipped skeleton aliases, and source-pose isolation.");
    }
}
