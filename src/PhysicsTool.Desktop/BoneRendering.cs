using Avalonia.Media;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class PhysicsViewport
{
    private void DrawBones(DrawingContext context)
    {
        var bones = Data.Bones.GroupBy(b => b.Index).ToDictionary(g => g.Key, g => g.First());
        // World-space joint dimensions match the original viewport and never depend on zoom.
        var size = Math.Clamp(_extent * .004f, .0025f, .025f);
        Vector3 Normal(Vector3 axis, Vector3 fallback) => axis.LengthSquared() > 1e-10f ? Vector3.Normalize(axis) : fallback;
        (Vector3 X, Vector3 Y, Vector3 Z) Axes(BonePreviewPoint b) => SimulatedBones?.GetValueOrDefault(b.Index) is { } p
            ? (p.AxisX, p.AxisY, p.AxisZ) : (b.AxisX, b.AxisY, b.AxisZ);
        Vector3[] Ring(BonePreviewPoint b)
        {
            var center = BonePosition(b); var axes = Axes(b);
            var x = Normal(axes.X, Vector3.UnitX) * size; var z = Normal(axes.Z, Vector3.UnitZ) * size;
            return [center - z, center + x, center + z, center - x];
        }
        IPen Pen(BonePreviewPoint b, bool selected) => new Pen(selected ? SelectionBrush : b.IsReference
            ? new SolidColorBrush(ViewportColor("#F24D12", "#AC4118")) : new SolidColorBrush(ViewportColor("#FFEB00", "#846500")), selected ? 2.2 : 1.5);
        foreach (var b in Data.Bones)
        {
            var selected = SelectedBones.Contains(b.Index); var pen = Pen(b, selected);
            var ring = Ring(b); var y = Normal(Axes(b).Y, Vector3.UnitY) * size; var center = BonePosition(b);
            for (var i = 0; i < 4; i++)
            {
                context.DrawLine(pen, Project(ring[i]), Project(ring[(i + 1) % 4]));
                context.DrawLine(pen, Project(ring[i]), Project(center + y));
                context.DrawLine(pen, Project(ring[i]), Project(center - y));
            }
            if (!bones.TryGetValue(b.ParentIndex, out var parent)) continue;
            pen = Pen(b, selected || SelectedBones.Contains(parent.Index));
            foreach (var tip in Ring(parent)) context.DrawLine(pen, Project(tip), Project(center));
        }
    }
}
