using System.Numerics;
using HKCLTool;
using PhysicsTool.Core;

internal static class OotShapeConstraintChecks
{
    internal static void Run()
    {
        CheckFrames(); CheckVolumes(); CheckPlanes(); CheckVelocity(); CheckGuards();
        Console.WriteLine("PASS OoT volume/bone-plane equations: rigid/sheared/reflected/degenerate frames, weighted centers, ordered stiffness, animated planes, mass independence, reset and invalid input guards.");
    }

    private static void CheckFrames()
    {
        var fitter = new OotVolumeFrame();
        var rotation = Matrix4x4.CreateFromYawPitchRoll(.7f, -.3f, .2f);
        var center = new Vector3(2, -1, 3);
        var x = Vector3.TransformNormal(Vector3.UnitX, rotation);
        var y = Vector3.TransformNormal(Vector3.UnitY, rotation);
        var z = Vector3.TransformNormal(Vector3.UnitZ, rotation);
        var fit = fitter.Fit(x * 2, y * 3, z * 4, center);
        Near(Vector3.Transform(Vector3.One, fit), Vector3.TransformNormal(Vector3.One, rotation) + center, "Polar fit retained stretch");
        var reflection = fitter.Fit(-x * 2, y * 3, z * 4, center);
        Near(Vector3.Transform(Vector3.UnitX, reflection), center - x, "Native reflected frame was forced to proper rotation");
        // Analytic 2D polar factor for [[1,1],[0,1]], embedded in 3D.
        var shear = fitter.Fit(new(1, 0, 0), new(1, 1, 0), Vector3.UnitZ, Vector3.Zero);
        Near(Vector3.TransformNormal(Vector3.UnitX, shear), new(2 / MathF.Sqrt(5), -1 / MathF.Sqrt(5), 0), "Shear polar factor");
        var planar = fitter.Fit(x, y, Vector3.Zero, center);
        Near(Vector3.TransformNormal(Vector3.UnitZ, planar), Vector3.Zero, "Rank-two frame invented an axis");
        Near(Vector3.TransformNormal(Vector3.UnitX, planar), x, "Rank-two frame lost its valid axis");
        var collapsed = fitter.Fit(Vector3.Zero, Vector3.Zero, Vector3.Zero, center);
        Near(Vector3.Transform(Vector3.One, collapsed), center, "Collapsed frame was not finite/centered");
        fitter.Reset();
        Near(Vector3.Transform(Vector3.One, fitter.Fit(x * 2, y * 3, z * 4, center)),
            Vector3.Transform(Vector3.One, fit), "Reset changed shape fit");
    }

    private static ParticlePreviewData Volume(float stiffness = .5f)
    {
        var data = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental, Gravity = Vector3.Zero };
        Vector3[] corners = [new(1, 1, 1), new(1, -1, -1), new(-1, 1, -1), new(-1, -1, 1)];
        for (int i = 0; i < corners.Length; i++) data.Particles.Add(new() { Index = i, Position = corners[i], Fixed = true });
        data.Particles.Add(new() { Index = 4, Position = new(2, 0, 0), Mass = 100, InverseMass = .01f });
        data.ConstraintSets.Add(new() { Index = 3, Kind = "hclVolumeConstraint" });
        data.ConstraintExecution.Add(3);
        data.OotVolumeConstraints.Add(new(3, "volume", 0, 0,
            corners.Select((p, i) => new VolumeConstraintFrameData(i, new(p, 123), i, .25f)).ToArray(),
            [new(0, new(0, 0, 0, 99), 4, stiffness)]));
        return data;
    }

    private static void CheckVolumes()
    {
        var data = Volume(); var solver = new HkclPreviewSimulator(data);
        solver.Step(1f / 60);
        Near(solver.GetPositions()[4], Vector3.UnitX, "Authored volume stiffness");
        Near(data.Particles[4].Position, new(2, 0, 0), "Volume changed source");
        data.ConstraintExecution.Add(3);
        solver = new(data); solver.Step(1f / 60);
        Near(solver.GetPositions()[4], new(.5f, 0, 0), "Repeated volume pass skipped");
        data.Particles[4].Mass = 1; data.Particles[4].InverseMass = 1;
        var light = new HkclPreviewSimulator(data); light.Step(1f / 60);
        Near(light.GetPositions()[4], solver.GetPositions()[4], "Volume applied inverse mass twice");
        light.Reset(); light.Step(1f / 60);
        Near(light.GetPositions()[4], solver.GetPositions()[4], "Volume reset differed");
        data = Volume(0);
        solver = new(data); solver.Step(1f / 60);
        Near(solver.GetPositions()[4], new(2, 0, 0), "Zero volume stiffness moved particle");
        data = Volume(1);
        var set = data.OotVolumeConstraints.Single();
        data.OotVolumeConstraints[0] = set with { Frames = [set.Frames[0] with { Weight = .5f }] };
        solver = new(data); solver.Step(1f / 60);
        Near(solver.GetPositions()[4], new(.5f, .5f, .5f), "Authored frame weights were normalized");
    }

    private static ParticlePreviewData Plane(Vector3 position, float stiffness = .5f)
    {
        var data = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental, Gravity = Vector3.Zero };
        data.Bones.Add(new() { Index = 7 });
        data.Particles.Add(new() { Index = 0, Position = position, Mass = 1, InverseMass = 1 });
        data.ConstraintSets.Add(new() { Index = 2, Kind = "hclBonePlanesConstraintSet" });
        data.ConstraintExecution.Add(2);
        data.OotBonePlanes.Add(new(2, 0, 7, Vector3.UnitY, Vector3.Zero, 0, stiffness));
        return data;
    }

    private static void CheckPlanes()
    {
        foreach (var y in new[] { -2f, 0, 2 })
        {
            var data = Plane(new(0, y, 0));
            var solver = new HkclPreviewSimulator(data); solver.Step(1f / 60);
            Near(solver.GetPositions()[0], new(0, y < 0 ? y * .5f : y, 0), "Plane half-space");
            data.ConstraintExecution.Add(2);
            solver = new(data); solver.Step(1f / 60);
            Near(solver.GetPositions()[0], new(0, y < 0 ? y * .25f : y, 0), "Repeated plane pass");
        }
        var animated = Plane(new(2, 0, 0), 1);
        var moving = new HkclPreviewSimulator(animated);
        moving.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [7] = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(1, 0, 0) });
        moving.Step(1f / 60);
        Near(moving.GetPositions()[0], new(1, 0, 0), "Animated bone plane ignored rotation/translation");
        var offset = Plane(new(0, -2, 0), 1);
        offset.OotBonePlanes[0] = offset.OotBonePlanes[0] with { Offset = 1 };
        var solverOffset = new HkclPreviewSimulator(offset); solverOffset.Step(1f / 60);
        Near(solverOffset.GetPositions()[0], new(0, -1, 0), "Plane offset sign");
        var scaled = Plane(new(0, -2, 0), .25f);
        scaled.OotBonePlanes[0] = scaled.OotBonePlanes[0] with { RestNormal = Vector3.UnitY * 2 };
        var solverScaled = new HkclPreviewSimulator(scaled); solverScaled.Step(1f / 60);
        Near(solverScaled.GetPositions()[0], Vector3.Zero, "Plane normal was incorrectly normalized");
    }

    private static void CheckVelocity()
    {
        var none = OotConstraintVelocityMode.None;
        var preserve = OotConstraintVelocityMode.Preserve;
        var blend = OotConstraintVelocityMode.Blend;
        if (OotConstraintVelocity.ForStep(2, 1, 1, 1, 1, true) != none ||
            OotConstraintVelocity.ForStep(1, 4, .5f, .5f, .5f, true) != none ||
            OotConstraintVelocity.ForStep(2, 2, 1, 1, 1, true) != preserve ||
            OotConstraintVelocity.ForStep(2, 2, 1, 1, 1, false) != blend ||
            OotConstraintVelocity.ForStep(2, 1, .5f, 1, 1, true) != preserve ||
            OotConstraintVelocity.ForStep(2, 1, 1, .5f, 1, true) != preserve ||
            OotConstraintVelocity.ForStep(2, 1, 1, 1, .5f, true) != preserve)
            throw new Exception("OoT step-info preserveVelocity gate");
        var before = new Vector3(1, -2, 0); var previous = Vector3.Zero; var after = new Vector3(1, 0, 0);
        Near(OotConstraintVelocity.PreviousAfterCorrection(before, previous, after, none), previous, "Disabled compensation");
        Near(OotConstraintVelocity.PreviousAfterCorrection(before, previous, after, preserve), new(0, 2, 0), "Direct preservation changed velocity");
        Near(OotConstraintVelocity.PreviousAfterCorrection(before, previous, after, blend),
            new(0, 2 / (MathF.Sqrt(5) + 1), 0), "Length-weighted velocity blend");
        Near(OotConstraintVelocity.PreviousAfterCorrection(Vector3.Zero, Vector3.Zero, Vector3.UnitY, blend), Vector3.UnitY, "Stationary blended projection injected velocity");
        Near(OotConstraintVelocity.PreviousAfterCorrection(before, previous, before, blend), previous, "Inactive plane changed velocity");
        Near(OotConstraintVelocity.PreviousAfterCorrection(Vector3.Zero, Vector3.Zero, Vector3.Zero, blend), Vector3.Zero, "Zero-length blend produced NaN");
        foreach (bool direct in new[] { true, false })
        foreach (int substeps in new[] { 2, 4 })
        {
            var data = Plane(new(0, -1, 0), .5f);
            data.SimulationSubSteps = substeps; data.OotDirectConstraintVelocity = direct;
            var solver = new HkclPreviewSimulator(data); solver.Step(1f / 60);
            Near(solver.GetPositions()[0], new(0, -MathF.Pow(.5f, substeps), 0), "Plane substeps injected projection velocity");
            for (int i = 0; i < 20; i++) solver.Step(1f / 60);
            Near(solver.GetPositions()[0], Vector3.Zero, "Plane launched particle beyond boundary");
            solver.Reset(); solver.Step(1f / 60);
            Near(solver.GetPositions()[0], new(0, -MathF.Pow(.5f, substeps), 0), "Compensation reset leaked history");
        }
        Console.WriteLine("PASS OoT bone-plane compensation: step gates, direct and length-weighted modes, stationary/moving/zero-length cases, substeps and reset.");
    }

    private static void CheckGuards()
    {
        void Reject(ParticlePreviewData data)
        {
            try { _ = new HkclPreviewSimulator(data); throw new Exception("Invalid shape constraint accepted"); }
            catch (InvalidDataException) { }
        }
        var volume = Volume(); var set = volume.OotVolumeConstraints[0];
        volume.OotVolumeConstraints[0] = set with { Applies = [set.Applies[0] with { ParticleIndex = 999 }] }; Reject(volume);
        volume.OotVolumeConstraints[0] = set with { Frames = [set.Frames[0] with { Weight = float.NaN }] }; Reject(volume);
        var plane = Plane(Vector3.Zero); plane.OotBonePlanes[0] = plane.OotBonePlanes[0] with { BoneIndex = 99 }; Reject(plane);
        plane = Plane(Vector3.Zero); plane.OotBonePlanes[0] = plane.OotBonePlanes[0] with { Offset = float.PositiveInfinity }; Reject(plane);
        foreach (var profile in new[] { ClothSolverProfile.TotkExperimental, ClothSolverProfile.BotwPreview })
        {
            var isolated = Volume(); isolated.SolverProfile = profile;
            var solver = new HkclPreviewSimulator(isolated); solver.Step(1f / 60);
            Near(solver.GetPositions()[4], new(2, 0, 0), "OoT shape solver affected another game");
        }
    }

    internal static void Native(string root)
    {
        int volumes = 0, planes = 0, changed = 0;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Phive/Cloth"), "*.bphcl"))
        {
            var session = new EditorSession(); session.Load(path);
            var original = session.Document.CaptureState();
            foreach (var cloth in session.Document.CaptureBphclNativeDocument()!.Cloths)
            {
                var v = session.Document.GetVolumeConstraintSets(cloth.Index);
                var p = session.Document.GetBonePlaneConstraintSets(cloth.Index);
                if (v.Count + p.Count == 0) continue;
                var data = session.Document.GetTotkSimulationPreview(cloth.Index);
                volumes += v.Count; planes += p.Count;
                var solver = new HkclPreviewSimulator(data);
                var absentSets = v.Select(s => s.ConstraintSetIndex).Concat(p.Select(s => s.ConstraintSetIndex)).ToHashSet();
                data.OotVolumeConstraints.Clear(); data.OotBonePlanes.Clear();
                data.ConstraintSets.RemoveAll(s => absentSets.Contains(s.Index));
                var baseline = new HkclPreviewSimulator(data);
                for (int i = 0; i < 120; i++) { solver.Step(1f / 60); baseline.Step(1f / 60); }
                if (solver.GetPositions().Any(e => Vector3.Distance(e.Value, baseline.GetPositions()[e.Key]) > 1e-5)) changed++;
                if (solver.NumericalResetCount != 0) throw new Exception("Native shape constraint required numerical recovery: " + cloth.Name);
                for (int frame = 0; frame < 120; frame++)
                {
                    var delta = Matrix4x4.CreateRotationZ(.2f * MathF.Sin(frame * .03f)) *
                        Matrix4x4.CreateTranslation(.1f * MathF.Sin(frame * .04f), .05f, 0);
                    solver.SetInputBoneDeltas(data.Bones.ToDictionary(b => b.Index, _ => delta));
                    solver.Step(1f / 60);
                }
                if (solver.NumericalResetCount != 0 || solver.GetPositions().Values.Any(v => !float.IsFinite(v.LengthSquared())))
                    throw new Exception("Animated native shape constraint became unstable: " + cloth.Name);
                solver.Reset(); solver.Step(1f / 60);
                var reset = solver.GetPositions();
                solver.Reset(); solver.Step(1f / 60);
                foreach (var item in reset) Near(solver.GetPositions()[item.Key], item.Value, "Native shape reset was not repeatable");
                Console.WriteLine($"SHAPE {Path.GetFileName(path)}/{cloth.Name}: {v.Count} volume, {p.Count} bone-plane sets");
            }
            if (session.Document.CaptureState() != original) throw new Exception("Native simulation changed file data");
        }
        if (volumes == 0 || planes == 0 || changed == 0) throw new Exception("Native shape fixtures did not exercise both kernels");
        Console.WriteLine($"PASS native shape constraints: {volumes} volume sets, {planes} bone-plane sets, {changed} cloths differ from skipped-kernel baseline; sources unchanged.");
    }

    private static void Near(Vector3 actual, Vector3 expected, string message)
    {
        if (!float.IsFinite(actual.X) || !float.IsFinite(actual.Y) || !float.IsFinite(actual.Z) || Vector3.Distance(actual, expected) > 3e-5f)
            throw new Exception($"{message}: {actual}, expected {expected}");
    }
}
