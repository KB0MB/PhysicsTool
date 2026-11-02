using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

public static class WholeFileClothPreview
{
    // Display geometry only: local indices must not alias across cloths, and this is not solver input.
    public static ParticlePreviewData Create(HkclService document)
    {
        var result = new ParticlePreviewData();
        var colliderGeometry = new HashSet<(int, Vector3, Vector3, float, float, Vector3)>();
        for (var cloth = 0; cloth < document.GetClothSummaries().Count; cloth++)
        {
            var source = document.GetParticlePreview(cloth);
            var particles = source.Particles.Select((p, i) => (p.Index, Id: result.Particles.Count + i)).ToDictionary(p => p.Index, p => p.Id);
            var bones = source.Bones.Select((b, i) => (b.Index, Id: result.Bones.Count + i)).ToDictionary(b => b.Index, b => b.Id);
            foreach (var p in source.Particles)
                result.Particles.Add(new ParticlePreviewPoint { Index = particles[p.Index], Position = p.Position, Radius = p.Radius, Fixed = p.Fixed });
            foreach (var b in source.Bones)
                result.Bones.Add(new BonePreviewPoint {
                    Index = bones[b.Index], ParentIndex = bones.GetValueOrDefault(b.ParentIndex, -1),
                    Name = b.Name, Position = b.Position, AxisX = b.AxisX, AxisY = b.AxisY, AxisZ = b.AxisZ,
                    StretchScale = b.StretchScale, IsReference = b.IsReference, IsSelectable = false
                });
            foreach (var link in source.Links)
                if (particles.TryGetValue(link.ParticleA, out var a) && particles.TryGetValue(link.ParticleB, out var b))
                    result.Links.Add(new ParticlePreviewLink { ParticleA = a, ParticleB = b, Kind = link.Kind });
            foreach (var triangle in source.Triangles)
                if (particles.TryGetValue(triangle.ParticleA, out var a) && particles.TryGetValue(triangle.ParticleB, out var b)
                    && particles.TryGetValue(triangle.ParticleC, out var c))
                    result.Triangles.Add(new ParticlePreviewTriangle { ParticleA = a, ParticleB = b, ParticleC = c });
            foreach (var c in source.Colliders)
            {
                if (!colliderGeometry.Add((c.Index, c.Start, c.End, c.Radius, c.EndRadius, c.PlaneNormal))) continue;
                result.Colliders.Add(new ColliderPreviewShape {
                    Index = result.Colliders.Count, BoneIndex = bones.GetValueOrDefault(c.BoneIndex, -1),
                    Name = c.Name, Kind = c.Kind, Start = c.Start, End = c.End, Radius = c.Radius,
                    EndRadius = c.EndRadius, PlaneNormal = c.PlaneNormal, TransformOrigin = c.TransformOrigin
                });
            }
        }
        return result;
    }
}
