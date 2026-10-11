using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;
using static HKCLTool.BotwAampWriter;

internal static class SupportHierarchyChecks
{
    public static void Run()
    {
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
}
