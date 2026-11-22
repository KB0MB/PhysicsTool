using System.Numerics;
using System.Reflection;
using static HKCLTool.BotwAampWriter;

namespace HKCLTool;

internal static class SupportBoneSimulationChecks
{
    public static void Run(string directory)
    {
        var q = SidecarPreviewEvaluator.BuildBotwRotation(MathF.PI / 2, MathF.PI / 2, 0, Vector3.UnitZ, Vector3.UnitY);
        Near(Vector3.Transform(Vector3.UnitZ, q), new Vector3(2f / 3, 2f / 3, -1f / 3), "Native combined-bend direction");
        foreach (var axis in new[] { Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(0.7f, -0.4f, 0.2f) })
        foreach (var h in new[] { -2.1f, -0.6f, 0f, 1.7f })
        foreach (var v in new[] { -1.2f, 0f, 0.8f })
        foreach (var r in new[] { -2.9f, 0f, 2.8f })
        {
            var aim = Vector3.Transform(Vector3.UnitZ, axis);
            var up = Vector3.Transform(Vector3.UnitY, axis);
            var rotation = SidecarPreviewEvaluator.BuildBotwRotation(h, v, r, aim, up);
            Near(SidecarPreviewEvaluator.MeasureBotwRotation(rotation, aim, up), new(h, v, r), "Swing/twist round trip");
            Near(SidecarPreviewEvaluator.MeasureBotwRotation(-rotation, aim, up), new(h, v, r), "Quaternion sign changed helper output");
        }

        var source = Fixture("Source");
        CheckPose(source, "Source");
        var merged = BphyssbBridge.MergeDriverGroup(Fixture("Target"), source, 0);
        CheckPose(merged, "Source", 1);
        Require(merged.Archive!.Root.Objects[0].Find("connection_linear_num")!.AsInt32() == 2, "Linear header count was not updated.");
        var sourceOutput = merged.Archive.Root.Children[0].FindChild("output_single_list")!.Objects[2];
        Require(sourceOutput.Find("connection")!.AsInt32() == 1, "Linear connection was remapped as a curve.");
        var roundTrip = BphyssbBridge.LoadBytes(merged.Bytes, "roundtrip.bphyssb");
        CheckPose(roundTrip, "Source", 1);
        CheckPose(Fixture("LinearOnly", true), "LinearOnly", linearOnly: true);
        CheckPose(BphyssbBridge.MergeDriverGroup(Fixture("Target"), Fixture("LinearOnly", true), 0), "LinearOnly", 1, true);
        CheckUnconnectedChannels();

        var curve = source.Archive!.Root.Children[0].FindChild("connection_curve_list")!.Objects[0];
        Set(curve, "constant_in", 0, BitConverter.GetBytes(0));
        Set(curve, "constant_out", 0, BitConverter.GetBytes(0));
        Set(curve, "key_0", 5, Bytes(-1f, -2f, 3f, 7f));
        Set(curve, "key_1", 5, Bytes(0f, 0f, 5f, 11f));
        Set(curve, "key_2", 5, Bytes(1f, 4f, 2f, 17f));
        Near(SidecarPreviewEvaluator.EvaluateBotwCurve(curve, -2), -5, "Incoming extrapolation");
        Near(SidecarPreviewEvaluator.EvaluateBotwCurve(curve, 2), 6, "Outgoing extrapolation must use native Z tangent");
        Near(SidecarPreviewEvaluator.EvaluateBotwCurve(curve, -0.5f), -0.75f, "Hermite incoming/outgoing tangents");
        Near(SidecarPreviewEvaluator.EvaluateBotwCurve(curve, 0), 0, "Exact key value");
        Set(curve, "constant_in", 0, BitConverter.GetBytes(1));
        Set(curve, "constant_out", 0, BitConverter.GetBytes(1));
        Near(SidecarPreviewEvaluator.EvaluateBotwCurve(curve, -2), -2, "Constant input tail");
        Near(SidecarPreviewEvaluator.EvaluateBotwCurve(curve, 2), 4, "Constant output tail");
        Near(SidecarPreviewEvaluator.EvaluateBotwCurve(curve, float.NaN), 0, "NaN output guard");

        var pack = typeof(BphyssbBridge).GetMethod("PackBoneAttribute", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var channel in new[] { 3, 4, 5 })
            Require((int)pack.Invoke(null, new object[] { 7, channel })! == (7 << 16 | channel), "Conversion lost translation channel identity.");
        var edited = BphyssbBridge.UpdateBone(source, 1, "SourceHelper", -2, new(1, 2, 3), new(0, 0, 0, 2));
        Near(edited.Graph!.SupportBones[0].Rotation.W, 1, "Edited quaternion was not normalized");
        var changedDriver = BphyssbBridge.UpdateBone(source, 0, "SourceDriver", -2, new(9, 2, 3), Vector4.UnitW);
        Require(BphyssbBridge.MergeDriverGroup(source, changedDriver, 0).Graph!.MainBones.Count == 2,
            "Group import reused a driver with a different reference frame.");
        Reject(() => BphyssbBridge.UpdateBone(source, 1, "SourceHelper", -1, Vector3.Zero, Vector4.Zero));
        Reject(() => BphyssbBridge.UpdateBone(source, 1, "SourceHelper", 1000, Vector3.Zero, Vector4.UnitW));
        Reject(() => BphyssbBridge.UpdateBone(source, 1, "SourceHelper", 1, Vector3.Zero, Vector4.UnitW));
        File.WriteAllText(Path.Combine(directory, "support.txt"), "PASS: native swing/twist, quaternion sign, authored reference pose, model space, independent input channels, linear/curve/double outputs, group merge/round-trip, extrapolation, translation conversion, and edit validation.");
    }

    private static void CheckPose(BphyssbDocumentSummary document, string prefix, int group = 0, bool linearOnly = false)
    {
        var reference = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.35f);
        var current = new[] {
            new VisualModelBone { Index = 0, Name = prefix + "Driver", Position = new(1.25f, 2.5f, 3.75f),
                Rotation = reference * SidecarPreviewEvaluator.BuildBotwRotation(0.4f, -0.2f, 0.1f, Vector3.UnitZ, Vector3.UnitY) },
            new VisualModelBone { Index = 1, ParentIndex = 2, Name = prefix + "Helper", Position = new(4, 5, 6) },
            new VisualModelBone { Index = 2, Name = "Parent", Position = new(30, 40, 50), Rotation = reference }
        };
        var result = SidecarPreviewEvaluator.Evaluate(document, group, current, current);
        var driven = result.Poses.Single(p => p.IsDriven);
        Near(driven.Position, linearOnly ? new(4, 5, 6) : new(4.25f, 5.95f, 6), "Helper channels or model-space sentinel");
        var expected = reference * SidecarPreviewEvaluator.BuildBotwRotation(0.9f, 0, 0, Vector3.UnitZ, Vector3.UnitY);
        Require(MathF.Abs(Quaternion.Dot(driven.Rotation, expected)) > 0.99999f, "Authored base rotation order or driver reference is wrong.");
    }

    private static void CheckUnconnectedChannels()
    {
        foreach (var space in new[] { -2, -1, 0 })
        {
            var document = Fixture("Masked");
            var output = document.Archive!.Root.Children[0].FindChild("support_bone_list")!.Objects[0];
            Set(output, "space", 2, BitConverter.GetBytes(space));
            Set(output, "bendH", 2, BitConverter.GetBytes(-1));
            Set(output, "translateY", 2, BitConverter.GetBytes(-1));
            var reference = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.35f);
            var driver = new VisualModelBone { Index = 0, Name = "MaskedDriver", Position = new(1.25f, 2, 3), Rotation = reference };
            var parent = new VisualModelBone { Index = 2, Name = "Parent", Position = new(30, 40, 50), Rotation = reference };
            var basis = space == -2 ? Matrix4x4.Identity : BotwActorScene.Matrix(space == -1 ? parent : driver);
            var incomingRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
            var local = Matrix4x4.CreateScale(1.5f) * Matrix4x4.CreateFromQuaternion(incomingRotation) * Matrix4x4.CreateTranslation(8, 9, 10);
            Matrix4x4.Decompose(local * basis, out var scale, out var rotation, out var position);
            var helper = new VisualModelBone { Index = 1, ParentIndex = 2, Name = "MaskedHelper", Position = position, Rotation = rotation, Scale = scale };
            var rest = new[] { driver, new VisualModelBone { Index = 1, ParentIndex = 2, Name = "MaskedHelper", Position = new(4, 5, 6) }, parent };
            var result = SidecarPreviewEvaluator.Evaluate(document, -1, new[] { driver, helper, parent }, rest).Poses.Single(p => p.IsDriven);
            Matrix4x4.Invert(basis, out var inverse);
            var world = Matrix4x4.CreateScale(result.Scale) * Matrix4x4.CreateFromQuaternion(result.Rotation) * Matrix4x4.CreateTranslation(result.Position);
            Matrix4x4.Decompose(world * inverse, out var actualScale, out var actualRotation, out var actualPosition);
            Near(actualPosition, new(4.25f, 9, 10), "Unconnected support translation must retain the current pose in its declared space");
            Near(actualScale, new(1.5f), "Support must preserve incoming scale");
            Require(MathF.Abs(Quaternion.Dot(actualRotation, incomingRotation)) > .99999f, "Unconnected support rotation must retain the current pose");
        }
    }

    private static BphyssbDocumentSummary Fixture(string prefix, bool linearOnly = false)
    {
        var root = new AampListNode("param_root");
        var header = new AampObjectNode("support_bone_header"); root.Objects.Add(header);
        foreach (var name in new[] { "bone_num", "connection_linear_num", "connection_curve_num", "output_single_num", "output_double_num", "main_bone_num", "support_bone_num" })
            header.Parameters.Add(AampParameterNode.Integer(name, name == "bone_num" || name == "output_single_num" ? 2 : 1));
        var data = new AampListNode("support_bone_data"); root.Children.Add(data);
        AampListNode List(string name) { var list = new AampListNode(name); data.Children.Add(list); return list; }
        var bones = List("bone_list");
        foreach (var suffix in new[] { "Driver", "Helper" }) {
            var bone = new AampObjectNode("bone_" + bones.Objects.Count);
            bone.Parameters.Add(AampParameterNode.String256Value("name", prefix + suffix)); bones.Objects.Add(bone);
        }
        var linear = new AampObjectNode("connection_linear_0"); List("connection_linear_list").Objects.Add(linear);
        linear.Parameters.Add(AampParameterNode.Integer("bone_attribute", 0));
        linear.Parameters.Add(AampParameterNode.Raw(Crc32("slope_intercept"), 3, Bytes(2f, 0.1f)));
        if (!linearOnly) {
            var curve = new AampObjectNode("connection_curve_0"); List("connection_curve_list").Objects.Add(curve);
            curve.Parameters.Add(AampParameterNode.Integer("bone_attribute", 3));
            for (var i = 0; i < 3; i++) curve.Parameters.Add(AampParameterNode.Vector4("key_" + i, new(i - 1, i - 1, 1, 1)));
        }
        var singles = List("output_single_list");
        for (var i = 0; i < (linearOnly ? 1 : 2); i++) {
            var single = new AampObjectNode("output_single_" + i); singles.Objects.Add(single);
            single.Parameters.Add(AampParameterNode.Integer("connection", i == 0 ? 0 : 0x10000));
            single.Parameters.Add(AampParameterNode.Single("weight", 1));
        }
        if (!linearOnly) {
            var dual = new AampObjectNode("output_double_0"); List("output_double_list").Objects.Add(dual);
            dual.Parameters.Add(AampParameterNode.Integer("connection_0", 0)); dual.Parameters.Add(AampParameterNode.Single("weight_0", 0.5f));
            dual.Parameters.Add(AampParameterNode.Integer("connection_1", 0x10000)); dual.Parameters.Add(AampParameterNode.Single("weight_1", 2));
        }
        for (var i = 0; i < 2; i++) {
            var pose = new AampObjectNode(i == 0 ? "main_bone_0" : "support_bone_0");
            List(i == 0 ? "main_bone_list" : "support_bone_list").Objects.Add(pose);
            pose.Parameters.Add(AampParameterNode.Integer("bone", i)); pose.Parameters.Add(AampParameterNode.Integer("space", -2));
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.35f);
            pose.Parameters.Add(AampParameterNode.Vector4("base_rotate", new(rotation.X, rotation.Y, rotation.Z, rotation.W)));
            pose.Parameters.Add(AampParameterNode.Vector3("base_translate", i == 0 ? new(1, 2, 3) : new(4, 5, 6)));
            if (i == 1) {
                pose.Parameters.Add(AampParameterNode.Integer("bendH", 0));
                pose.Parameters.Add(AampParameterNode.Integer("translateX", linearOnly ? -1 : 1));
                pose.Parameters.Add(AampParameterNode.Integer("translateY", linearOnly ? -1 : 0x10000));
            }
        }
        return BphyssbBridge.LoadBytes(WriteArchive("physsb", root), "fixture.bphyssb");
    }

    private static byte[] Bytes(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
    private static void Set(AampReadObject item, string name, byte type, byte[] bytes) {
        item.Parameters.RemoveAll(p => p.Hash == Crc32(name)); item.Parameters.Add(new(Crc32(name), type, bytes));
    }
    private static void Near(float actual, float expected, string message) => Require(MathF.Abs(actual - expected) < 0.0001f, message);
    private static void Near(Vector3 actual, Vector3 expected, string message) => Require(Vector3.Distance(actual, expected) < 0.0001f, message);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action) {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid helper edit was accepted.");
    }
}
