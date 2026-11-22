using System.Numerics;
using System.Buffers.Binary;
using HKCLTool;

internal static class OotWindChecks
{
    private static OotAerodynamicsData Data(int count = 1) => new(.2f, 2, 4, true, count, 1, 1, true, true, [], []);
    private static void Near(Vector3 actual, Vector3 expected, string message)
    {
        if (!float.IsFinite(actual.LengthSquared()) || Vector3.Distance(actual, expected) > 1e-5f)
            throw new Exception(message + $": {actual} != {expected}");
    }
    internal static void Run()
    {
        OotWindDecodingChecks.Run();
        var data = Data();
        Vector3 Eval(OotAerodynamicsData d, Vector3? normal = null, Vector3? velocity = null, float total = 10, float inv = .5f) =>
            OotClothWind.Acceleration(d, 0, new(10, 0, 0), Vector3.UnitX, velocity ?? Vector3.Zero, normal, 2, inv, total);
        Near(Eval(data), new(3.5f, 0, 0), "No-normal native exposure");
        Near(Eval(data, Vector3.UnitX), new(5, 0, 0), "Normal incidence");
        Near(Eval(data, -Vector3.UnitX), new(5, 0, 0), "Two-sided wind exposure");
        Near(Eval(data, Vector3.UnitY), Vector3.Zero, "Edge-on wind");
        Near(Eval(data, Vector3.Zero), Vector3.Zero, "Degenerate normal is not the missing-normal fallback");
        Near(Eval(data, velocity: new(10, 0, 0)), Vector3.Zero, "Air-relative velocity");
        Near(Eval(data, velocity: new(20, 0, 0)), new(-3.5f, 0, 0), "Air drag opposes excess velocity");
        Near(Eval(data with { SurfaceDensity = 8 }), new(1.75f, 0, 0), "Surface density");
        Near(Eval(data with { UseSurfaceDensity = false }), new(1.4f, 0, 0), "Total mass fallback");
        Near(Eval(data, total: 0), Vector3.Zero, "Native total-mass gate");
        Near(Eval(data, inv: 0), Vector3.Zero, "Fixed particle");
        Near(Eval(data with { LiftCoefficient = 100 }), Eval(data), "Simple wind incorrectly consumes lift");
        Near(Eval(data with { UseConstantDragFactor = false, DragFactors = [.25f] }), new(.875f, 0, 0), "Per-particle drag");
        Near(OotClothWind.Acceleration(data, 0, new(10, 0, 0), Vector3.UnitX, Vector3.Zero, null, 2, .5f, 10, 2, 3),
            new(21, 0, 0), "Runtime action multipliers");
        foreach (var invalid in new[] { data with { SurfaceDensity = 0 }, data with { DragCoefficient = float.NaN },
                     data with { ParticleCount = 2 }, data with { UseConstantDragFactor = false }, data with { DragFactors = [-1] } })
        {
            bool rejected = false;
            try { invalid.Validate(1); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid aerodynamic data accepted");
        }
        var preview = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental,
            Gravity = Vector3.Zero, OotAerodynamics = Data(2), TotalMass = 2 };
        preview.Particles.Add(new() { Index = 0, Position = Vector3.Zero, Mass = 1, InverseMass = 1 });
        preview.Particles.Add(new() { Index = 1, Position = Vector3.UnitY, Mass = 1, InverseMass = 0, Fixed = true });
        var solver = new HkclPreviewSimulator(preview) { WindEnabled = true, WindSpeed = 10, WindGustiness = 0, RandomWindDirections = false };
        solver.Step(1f / 60);
        Near(solver.GetPositions()[0], new(3.5f / 3600, 0, 0), "Integrated native wind");
        Near(solver.GetPositions()[1], Vector3.UnitY, "Wind moved fixed anchor");
        var first = solver.GetPositions()[0]; solver.Reset(); solver.Step(1f / 60);
        Near(solver.GetPositions()[0], first, "Wind reset");
        solver.Reset(); solver.WindEnabled = false; solver.Step(1f / 60);
        Near(solver.GetPositions()[0], Vector3.Zero, "Wind toggle");
        preview.OotAerodynamics = Data(2) with { SurfaceDensity = .000001f };
        solver = new(preview) { WindEnabled = true, WindSpeed = 10, WindGustiness = 0, RandomWindDirections = false };
        solver.Step(1f / 60);
        Near(solver.GetPositions()[0], new(10f / 60, 0, 0), "Preview-only wind stability cap");
        if (solver.NumericalResetCount != 0) throw new Exception("Wind caused a numerical reset");
        CheckNormalsAndIsolation();
        Console.WriteLine("PASS OoT simple wind: density/total mass, relative velocity, normal exposure, per-particle factors, fixed anchors, reset, toggle and preview safety cap.");
    }

    private static void CheckNormalsAndIsolation()
    {
        var preview = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental,
            Gravity = Vector3.Zero, TotalMass = 3, OotAerodynamics = Data(3),
            OotWindSurface = new([new() { ParticleA = 0, ParticleB = 1, ParticleC = 2 }], [0], []) };
        preview.Particles.Add(new() { Index = 0, Position = Vector3.Zero, Mass = 1, InverseMass = 1 });
        preview.Particles.Add(new() { Index = 1, Position = Vector3.UnitY, Mass = 1, InverseMass = 0, Fixed = true });
        preview.Particles.Add(new() { Index = 2, Position = Vector3.UnitZ, Mass = 1, InverseMass = 0, Fixed = true });
        HkclPreviewSimulator Make() => new(preview) { WindEnabled = true, WindSpeed = 10, WindGustiness = 0, RandomWindDirections = false };
        var solver = Make(); solver.Step(1f / 60);
        Near(solver.GetPositions()[0], new(5f / 3600, 0, 0), "Computed wind normal");
        solver.Reset(); solver.Step(1f / 60);
        Near(solver.GetPositions()[0], new(5f / 3600, 0, 0), "Normal refresh on reset");
        preview.Particles[2].Position = Vector3.UnitX;
        solver = Make(); solver.Step(1f / 60);
        Near(solver.GetPositions()[0], Vector3.Zero, "Computed tangent exposure");
        foreach (var profile in new[] { ClothSolverProfile.BotwPreview, ClothSolverProfile.TotkExperimental })
        {
            preview.SolverProfile = profile;
            var withData = Make(); withData.Step(1f / 60);
            var data = preview.OotAerodynamics; preview.OotAerodynamics = null;
            var withoutData = Make(); withoutData.Step(1f / 60);
            Near(withData.GetPositions()[0], withoutData.GetPositions()[0], "Other-game wind changed");
            preview.OotAerodynamics = data;
        }
    }

    internal static void Native(string root)
    {
        int cloths = 0, normals = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Phive/Cloth"), "*.bphcl"))
        {
            var service = new HkclService(); service.Load(file);
            string before = service.CaptureState();
            var doc = NativeBphclDocument.Open(file);
            for (int c = 0; c < doc.Cloths.Count; c++)
            {
                var preview = service.GetTotkSimulationPreview(c);
                if (preview.OotAerodynamics == null) throw new Exception("Missing native aerodynamic input");
                var solver = new HkclPreviewSimulator(preview) { WindEnabled = true, WindSpeed = 12 };
                var baseline = new HkclPreviewSimulator(preview);
                baseline.Step(1f / 60);
                var fixedParticles = preview.Particles.Where(p => p.Fixed).ToDictionary(p => p.Index, p => baseline.GetPositions()[p.Index]);
                for (int frame = 0; frame < 120; frame++) solver.Step(1f / 60);
                if (solver.NumericalResetCount != 0 || solver.GetPositions().Values.Any(p => !float.IsFinite(p.LengthSquared())))
                    throw new Exception("Unstable native wind: " + file + " / " + c);
                foreach (var (p, position) in fixedParticles) Near(solver.GetPositions()[p], position, "Wind detached fixed anchor");
                cloths++; if (preview.OotWindSurface != null) normals++;
            }
            if (before != service.CaptureState()) throw new Exception("Wind mutated source");
            CheckMalformed(doc);
        }
        Console.WriteLine($"PASS native OoT wind: {cloths} cloths, {normals} with computed normals, 120 steps each, no numerical resets or source changes.");
    }

    private static void CheckMalformed(NativeBphclDocument doc)
    {
        var sim = doc.Cloths[0].SimCloths[0];
        var member = doc.TypeDefinitions.Single(t => t.TypeIndex == doc.GetItem(sim.ItemIndex).TypeIndex).Members.Single(m => m.Name == "aerodynamicsData");
        var layout = doc.TypeDefinitions.Single(t => t.TypeIndex == member.TypeIndex);
        foreach (var (field, value) in new[] { ("surfaceDensity", 0u), ("dragCoefficient", 0x7fc00000u), ("numParticles", uint.MaxValue) })
        {
            var bytes = doc.Bytes.ToArray();
            int at = checked(doc.DataSection!.PayloadOffset + (int)(sim.DataOffset + member.Offset + layout.Members.Single(m => m.Name == field).Offset));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at, 4), value);
            bool rejected = false;
            try { NativeBphclDocument.Parse(bytes).ReadOotAerodynamics(sim.ItemIndex); }
            catch (Exception ex) when (ex is InvalidDataException or OverflowException) { rejected = true; }
            if (!rejected) throw new Exception("Malformed aerodynamic field accepted: " + field);
        }
    }
}
