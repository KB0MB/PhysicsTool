using System.Buffers.Binary;
using HKCLTool;
using System.Numerics;

internal static class OotVirtualCollisionChecks
{
    internal static void Equations()
    {
        static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        foreach (var kind in new[] { ColliderPreviewKind.Sphere, ColliderPreviewKind.Capsule })
        foreach (var profile in new[] { ClothSolverProfile.OotExperimental, ClothSolverProfile.TotkExperimental, ClothSolverProfile.BotwPreview })
        foreach (bool enabled in new[] { true, false })
        foreach (bool fixedOwner in new[] { true, false })
        foreach (uint mask in new uint[] { 0, 1 })
        {
            var source = new ParticlePreviewData { SolverProfile = profile, Gravity = Vector3.Zero };
            source.Particles.Add(new() { Index = 0, Position = new(-2, .5f, 0), Mass = 10, InverseMass = .1f, Fixed = fixedOwner, CollisionMask = mask });
            source.Particles.Add(new() { Index = 1, Position = new(2, .5f, 0), Mass = 1, InverseMass = 1, CollisionMask = 0 });
            source.VirtualCollisionData = new([new(0, 1, -1, new(.5f, .5f, 0))], 1);
            source.Colliders.Add(new() { Index = 0, CollisionBit = 0, VirtualCollisionEnabled = enabled, Kind = kind,
                Radius = 1, Start = kind == ColliderPreviewKind.Sphere ? Vector3.Zero : -Vector3.UnitZ, End = Vector3.UnitZ });
            var solver = new HkclPreviewSimulator(source); solver.Step(1f / 60);
            bool acts = enabled && !fixedOwner && mask != 0 && profile == ClothSolverProfile.OotExperimental;
            Require(Vector3.Distance(solver.GetPositions()[0], new(-2, acts ? 1 : .5f, 0)) < 1e-5f, "Virtual edge owner correction/gating");
            Require(solver.GetPositions()[1] == source.Particles[1].Position, "Virtual contact moved the opposite endpoint");
            Require(solver.VirtualCollisionCorrectionCount == (acts ? 1 : 0), "Virtual correction counter");
            solver.Reset(); Require(solver.VirtualCollisionCorrectionCount == 0, "Virtual correction history survived reset");
        }
        foreach (bool reverse in new[] { false, true })
        {
            var source = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental, Gravity = Vector3.Zero };
            source.Particles.Add(new() { Index = 0, Position = new(-2, 0, .5f), Mass = 1, InverseMass = 1, Radius = .1f, CollisionMask = 1 });
            source.Particles.Add(new() { Index = 1, Position = new(2, 0, .5f), Fixed = true });
            source.Particles.Add(new() { Index = 2, Position = new(0, 3, .5f), Fixed = true });
            source.VirtualCollisionData = new([new(0, reverse ? 2 : 1, reverse ? 1 : 2, reverse ? new(.25f, .5f, .25f) : new(.25f, .25f, .5f))], 2);
            source.Colliders.Add(new() { Index = 0, BoneIndex = 5, CollisionBit = 0, VirtualCollisionEnabled = true,
                Kind = ColliderPreviewKind.Sphere, Radius = 1, Start = new(-.25f, 1.5f, -2) });
            var solver = new HkclPreviewSimulator(source);
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [5] = Matrix4x4.CreateTranslation(0, 0, 2) });
            solver.Reset(); solver.Step(1f / 60);
            float penetration = 1.2f - MathF.Sqrt(.25f * .25f + .5f * .5f);
            Require(Vector3.Distance(solver.GetPositions()[0], new(-2, 0, .5f + penetration)) < 1e-5f,
                "Triangle must use oriented face normal, owner radius scale and animated collider");
            Require(source.Particles[0].Position == new Vector3(-2, 0, .5f), "Virtual preview modified source data");
        }
        foreach (var kind in new[] { ColliderPreviewKind.Sphere, ColliderPreviewKind.Capsule })
        foreach (bool triangle in new[] { false, true })
        {
            var source = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental, Gravity = Vector3.Zero };
            source.Particles.Add(new() { Index = 0, Position = triangle ? new(-2, -1, 0) : new(-2, 0, 0), Mass = 1, InverseMass = 1, CollisionMask = 1 });
            source.Particles.Add(new() { Index = 1, Position = triangle ? new(2, -1, 0) : new(2, 0, 0), Fixed = true });
            source.Particles.Add(new() { Index = 2, Position = new(0, 3, 0), Fixed = true });
            source.VirtualCollisionData = new([new(0, 1, triangle ? 2 : -1, triangle ? new(.375f, .375f, .25f) : new(.5f, .5f, 0))], 1);
            source.Colliders.Add(new() { Index = 0, CollisionBit = 0, VirtualCollisionEnabled = true, Kind = kind,
                Radius = 1, Start = kind == ColliderPreviewKind.Sphere ? Vector3.Zero : -Vector3.UnitY, End = Vector3.UnitY });
            var solver = new HkclPreviewSimulator(source); solver.Step(1f / 60);
            var expected = source.Particles[0].Position + (triangle ? Vector3.UnitZ : Vector3.Zero);
            Require(Vector3.Distance(solver.GetPositions()[0], expected) < 1e-5f, "Center/axis virtual contact must preserve native edge/face direction behavior");
        }
        var ordered = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental, Gravity = Vector3.Zero };
        ordered.Particles.Add(new() { Index = 0, Position = new(-2, .5f, 0), Mass = 1, InverseMass = 1, CollisionMask = 3 });
        ordered.Particles.Add(new() { Index = 1, Position = new(2, .5f, 0), Fixed = true });
        ordered.VirtualCollisionData = new([new(0, 1, -1, new(.5f, .5f, 0))], 1);
        ordered.Colliders.Add(new() { Index = 0, CollisionBit = 0, VirtualCollisionEnabled = true, Kind = ColliderPreviewKind.Sphere, Radius = 1 });
        ordered.Colliders.Add(new() { Index = 1, CollisionBit = 1, VirtualCollisionEnabled = true, Kind = ColliderPreviewKind.Plane,
            Start = new(0, .75f, 0), PlaneNormal = Vector3.UnitY });
        var orderedSolver = new HkclPreviewSimulator(ordered); orderedSolver.Step(1f / 60);
        Require(Vector3.Distance(orderedSolver.GetPositions()[0], new(-2, 1, 0)) < 1e-5f,
            "A collider's virtual contacts must precede the next collider's ordinary contacts");
        Require(orderedSolver.GetPositions()[1] == ordered.Particles[1].Position, "Ordered contacts moved a fixed anchor");
        Console.WriteLine("PASS: virtual edge/triangle owner corrections, collider ordering, face winding, radius scale, animated colliders, masks, fixed anchors and game isolation");
    }

    internal static void Survey(string root)
    {
        int cloths = 0, active = 0, pinch = 0, antiPinch = 0;
        var patterns = new SortedDictionary<string, int>();
        var layouts = new HashSet<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Phive/Cloth"), "*.bphcl"))
        {
            var doc = NativeBphclDocument.Open(path);
            uint U32(uint at) => BinaryPrimitives.ReadUInt32LittleEndian(doc.Bytes.AsSpan(doc.DataSection!.PayloadOffset + checked((int)at), 4));
            foreach (var sim in doc.Cloths.SelectMany(c => c.SimCloths))
            {
                cloths++;
                var expanded = doc.ReadVirtualCollisionData(sim.ItemIndex);
                if (sim.Runtime.PinchDetectionEnabled) pinch++;
                if (sim.ConstraintSets.Any(c => c.IsAntiPinch)) antiPinch++;
                var member = doc.TypeDefinitions.Single(t => t.TypeIndex == doc.GetItem(sim.ItemIndex).TypeIndex).Members.Single(m => m.Name == "virtualCollisionPointsData");
                var type = doc.TypeDefinitions.Single(t => t.TypeIndex == member.TypeIndex);
                uint field = sim.DataOffset + member.Offset;
                var count = expanded.Points.Count;
                var pattern = string.Join(", ", type.Members.Select(m => m.Name + "=" +
                    (doc.GetTypeName(m.TypeIndex) == "hkArray" ? (doc.TryGetReferencedItem(field + m.Offset, out var a) ? doc.GetItem(a).Count : 0) : U32(field + m.Offset))));
                patterns[pattern] = patterns.GetValueOrDefault(pattern) + 1;
                if (count == 0) continue;
                active++;
                if (active == 1) Malformed(doc, sim);
                var sided = doc.TypeDefinitions.Single(t => t.TypeIndex == doc.GetItem(sim.ItemIndex).TypeIndex).Members.Single(m => m.Name == "setupCollidableSidedness");
                string sides = doc.TryGetReferencedItem(sim.DataOffset + sided.Offset, out var sideItem) ?
                    Convert.ToHexString(doc.Bytes.AsSpan(doc.DataSection!.PayloadOffset + (int)doc.GetItem(sideItem).DataOffset, (int)doc.GetItem(sideItem).Count)) : "none";
                Console.WriteLine($"SAMPLES {Path.GetFileName(path)}/{sim.Name}: {expanded.Points.Count}, radius scale={expanded.RadiusScale}, sidedness={sides}, eligible colliders={sim.CollidableItemIndices.Count(i => doc.Colliders.Any(c => c.ItemIndex == i && c.VirtualCollisionPointCollisionEnabled))}");
                if (active <= 3)
                {
                    Console.WriteLine(Path.GetFileName(path) + "/" + sim.Name + ": " + pattern);
                    foreach (var m in type.Members.Where(m => doc.GetTypeName(m.TypeIndex) == "hkArray"))
                    {
                        if (!doc.TryGetReferencedItem(field + m.Offset, out var itemIndex)) continue;
                        var item = doc.GetItem(itemIndex);
                        var element = doc.TypeDefinitions.Single(t => t.TypeIndex == item.TypeIndex);
                        string layout = doc.GetTypeName(item.TypeIndex) + " size=" + element.Size + ": " + string.Join(", ", element.Members.Select(x => x.Name + "@" + x.Offset));
                        if (layouts.Add(layout)) Console.WriteLine(layout);
                        uint Size(NativeBphclTypeDefinition t) => t.Size ?? Size(doc.TypeDefinitions.Single(v => v.TypeIndex == t.ParentTypeIndex));
                        int length = checked((int)Math.Min(item.Count, 12) * (int)Size(element));
                        Console.WriteLine(m.Name + ": " + Convert.ToHexString(doc.Bytes.AsSpan(doc.DataSection!.PayloadOffset + (int)item.DataOffset, length)));
                    }
                }
            }
        }
        foreach (var pair in patterns.OrderByDescending(p => p.Value).Take(10)) Console.WriteLine(pair.Value + " x " + pair.Key);
        Console.WriteLine($"Cloths={cloths}, virtual={active}, pinch={pinch}, anti-pinch={antiPinch}");
    }

    private static void Malformed(NativeBphclDocument doc, NativeBphclSimCloth sim)
    {
        var type = doc.TypeDefinitions.Single(t => t.TypeIndex == doc.GetItem(sim.ItemIndex).TypeIndex);
        var data = type.Members.Single(m => m.Name == "virtualCollisionPointsData");
        var layout = doc.TypeDefinitions.Single(t => t.TypeIndex == data.TypeIndex);
        uint At(string name) => sim.DataOffset + data.Offset + layout.Members.Single(m => m.Name == name).Offset;
        NativeBphclItem Array(string name) => doc.TryGetReferencedItem(At(name), out int i) ? doc.GetItem(i) : throw new Exception(name);
        void Reject(uint at, byte[] bytes)
        {
            int absolute = doc.DataSection!.PayloadOffset + checked((int)at);
            var original = doc.Bytes.AsSpan(absolute, bytes.Length).ToArray();
            bool rejected = false;
            try
            {
                bytes.CopyTo(doc.Bytes, absolute);
                try { doc.ReadVirtualCollisionData(sim.ItemIndex); } catch (InvalidDataException) { rejected = true; }
            }
            finally { original.CopyTo(doc.Bytes, absolute); }
            if (!rejected) throw new Exception("Malformed virtual collision data accepted at " + at);
        }
        Reject(At("numVCPoints"), BitConverter.GetBytes(ushort.MaxValue));
        Reject(At("numLandscapeVCPoints"), BitConverter.GetBytes((ushort)1));
        Reject(Array("triangleFans").DataOffset, BitConverter.GetBytes(ushort.MaxValue));
        Reject(Array("triangles").DataOffset + 4, BitConverter.GetBytes(ushort.MaxValue));
        Reject(Array("triangleBarycentricsDictionary").DataOffset, BitConverter.GetBytes(float.NaN));
        Reject(sim.DataOffset + type.Members.Single(m => m.Name == "virtualCollisionPointsRadiusScale").Offset, BitConverter.GetBytes(-1f));
        foreach (string name in new[] { "numVCPoints", "numLandscapeVCPoints" })
        {
            int padding = doc.DataSection!.PayloadOffset + checked((int)At(name)) + 2;
            var original = doc.Bytes.AsSpan(padding, 2).ToArray();
            try
            {
                doc.Bytes[padding] = doc.Bytes[padding + 1] = 0xff;
                doc.ReadVirtualCollisionData(sim.ItemIndex);
            }
            finally { original.CopyTo(doc.Bytes, padding); }
        }
        Console.WriteLine("PASS: virtual collision malformed counts, indices, dictionaries, weights, radius and unsupported landscape guards");
    }
}
