using System.Numerics;

namespace HKCLTool;

// Evaluate the authored face caster offset before skinning. The visible surface is unchanged.
internal static class ToolboxShadowGeometry
{
    internal static Vector3[] Offsets(VisualModelMesh mesh)
    {
        var totk = mesh.Material.Totk;
        var botw = totk == null ? mesh.Material.Botw : null;
        if (totk == null && botw == null) return [];
        string prefix = totk != null ? "o_" : "uking_";
        long Option(string key, long fallback = 0) => totk != null
            ? totk.Options.GetValueOrDefault(prefix + key, fallback)
            : botw!.Options.GetValueOrDefault(prefix + key, (int)fallback);
        long type = Option("modify_position_type"), operation = Option("vtx_position_modify_calc_type");
        if (Option("vtx_position_modify_shadow_only") != 1 || type is not (1 or 2) || operation is not (2 or 8)) return [];
        long vector = Option("vtx_position_modify_vector", 100);
        // Constant-vector face adjustments; normal/texture-driven modes need their own source evaluation.
        if (vector is not (114 or 115)) return [];
        bool KnownSource(long id) => id is 300 or >= 100 and <= 115 or >= 400 and <= 406;
        if (!KnownSource(Option("vtx_position_modify_A", 100)) || !KnownSource(Option("vtx_position_modify_B", 100)) ||
            operation == 8 && !KnownSource(Option("vtx_position_modify_C", 100))) return [];
        var parameters = totk?.Parameters ?? botw!.Parameters;
        var colors = totk?.VertexColors ?? botw!.VertexColors;
        string paramPrefix = totk != null ? "p_" : "";
        Vector4 Parameter(string key, bool scalar = false)
        {
            if (!parameters.TryGetValue(paramPrefix + key, out var v) || v.Length == 0) return Vector4.Zero;
            return scalar ? new(v[0]) : new(v[0], v.Length > 1 ? v[1] : 0, v.Length > 2 ? v[2] : 0, v.Length > 3 ? v[3] : 0);
        }
        Vector4 Source(long id, int vertex) => id switch
        {
            300 => vertex < colors.Length ? colors[vertex] : Vector4.One,
            >= 104 and <= 111 => Parameter("const_value" + (id - 104), true),
            >= 100 and <= 103 => Parameter("const_color" + (id - 100)),
            112 or 113 => Parameter("const_color" + (id - 108)),
            114 or 115 => Parameter("const_vector" + (id - 114)),
            400 => Vector4.Zero, 401 => new(.5f), 402 => Vector4.One,
            403 => new(2), 404 => new(4), 405 => new(.25f), 406 => new(.75f),
            _ => Vector4.Zero
        };
        (long Id, long Channel, Vector4 Constant) ReadInput(string input)
        {
            long id = Option("vtx_position_modify_" + input, 100);
            return (id, Option("vtx_position_modify_" + input + "_channel"), Source(id, 0));
        }
        Vector4 Input((long Id, long Channel, Vector4 Constant) input, int vertex)
        {
            var value = input.Id == 300 ? vertex < colors.Length ? colors[vertex] : Vector4.One : input.Constant;
            long channel = input.Channel;
            value = (channel % 1000 / 10) switch { 1 => new(value.X), 2 => new(value.Y), 3 => new(value.Z), 4 => new(value.W), _ => value };
            return (channel % 10) switch { 1 => Vector4.One - value, 2 => -value, 3 => value - Vector4.One, _ => value };
        }
        var a = ReadInput("A"); var b = ReadInput("B"); var c = ReadInput("C");
        var direction = Source(vector, 0);
        bool clamp = Option("vtx_position_modify_clamp") != 0;
        var result = new Vector3[mesh.Vertices.Length / 8];
        for (int i = 0; i < result.Length; i++)
        {
            var amount = Input(a, i) * Input(b, i);
            if (operation == 8) amount += Input(c, i);
            if (clamp) amount = Vector4.Clamp(amount, Vector4.Zero, Vector4.One);
            var offset = amount * direction;
            result[i] = new(offset.X, offset.Y, offset.Z);
        }
        return result;
    }
}
