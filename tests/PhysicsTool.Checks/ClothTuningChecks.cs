using HKCLTool;
using PhysicsTool.Core;

internal static class ClothTuningChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void Run(string output)
    {
        for (int format = 0; format < 3; format++)
        {
            var session = new EditorSession(); session.Load(Path.Combine(output, $"manual-{format}" + (format == 2 ? ".bphcl" : ".hkcl")));
            var before = session.Document.CaptureState(); var rows = session.Document.GetParticleRows(0);
            var fixedMass = rows[0].Mass; var fixedInverse = rows[0].InverseMass;
            Require(session.ScaleParticleMass(2, [0, 1]) == 1, "Mass scaling included fixed particles");
            var after = session.Document.GetParticleRows(0);
            Require(after[0].Fixed && after[0].Mass == fixedMass && after[0].InverseMass == fixedInverse, "Fixed anchor changed");
            Require(Math.Abs(after[1].Mass - rows[1].Mass * 2) < .00001 && Math.Abs(after[1].InverseMass - 1 / after[1].Mass) < .0001, "Mass/inverse mass mismatch");
            Require(after[2].Mass == rows[2].Mass, "Unselected mass changed");
            var massPath = Path.Combine(output, $"mass-scaled-{format}" + session.Document.CurrentExtension);
            session.ExportCopy(massPath); var massReload = new EditorSession(); massReload.Load(massPath);
            var savedMass = massReload.Document.GetParticleRows(0);
            Require(savedMass[1].Mass == after[1].Mass && savedMass[1].InverseMass == after[1].InverseMass && savedMass[0].Fixed,
                $"Scaled mass save/reload failed ({format}): {after[1].Mass:R}/{after[1].InverseMass:R} -> {savedMass[1].Mass:R}/{savedMass[1].InverseMass:R}, fixed={savedMass[0].Fixed}");
            session.Undo(); Require(session.Document.CaptureState() == before, "Mass undo failed");
            session.Redo(); Require(session.Document.GetParticleRows(0)[1].Mass == after[1].Mass, "Mass redo failed"); session.Undo();
            Require(session.ScaleParticleMass(.5f, []) == 2, "Whole-cloth mass scope incorrect"); session.Undo();
            foreach (var scale in new[] { 0f, -1, float.NaN, float.PositiveInfinity, float.Epsilon })
            {
                var rejected = false;
                try { session.ScaleParticleMass(scale, [1]); } catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException) { rejected = true; }
                Require(rejected && session.Document.CaptureState() == before, "Invalid mass scaling changed data");
            }
            var fixedRejected = false;
            try { session.ScaleParticleMass(2, [0]); } catch (InvalidOperationException) { fixedRejected = true; }
            Require(fixedRejected && !session.CanUndo, "Fixed-only mass scope changed history");
            var edited = session.Document.GetParticleRows(0)[1];
            edited.Fixed = true;
            session.Edit((doc, cloth) => ParticleEdits.Apply(doc, cloth, [edited]));
            Require(session.Document.GetParticleRows(0).Count(row => row.Fixed) == 2, "Toggling Fixed removed another anchor");
            session.Undo();
            edited = session.Document.GetParticleRows(0)[1]; edited.X += 3; edited.Radius = .4f;
            session.Edit((doc, cloth) => ParticleEdits.Apply(doc, cloth, [edited]));
            var moved = session.Document.GetParticleRows(0);
            Require(moved[0].Fixed && moved[1].X == edited.X && moved[2].X == rows[2].X, "Single-row edit changed unrelated particles");
            var runtime = session.Document.GetSimulationRuntimeData(0);
            Require(Math.Abs(runtime.TotalMass - moved.Sum(row => row.Mass)) < .00001 && Math.Abs(runtime.MaxParticleRadius - .4f) < .00001,
                "Single-row edit corrupted aggregate mass/radius");
            if (format != 2)
            {
                var links = session.Document.GetParticleRelationships(0, 1).Where(link => link.RestLength.HasValue).ToArray();
                Require(links.Length > 0, "No rest-length regression fixture");
                foreach (var link in links)
                {
                    var a = moved.Single(row => row.Index == link.ParticleA);
                    var b = moved.Single(row => row.Index == link.ParticleB);
                    var length = MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2) + MathF.Pow(a.Z - b.Z, 2));
                    Require(Math.Abs(link.RestLength!.Value - length) < .00001, "Edited particle retained old constraint rest length");
                }
            }
            session.Undo(); Require(session.Document.CaptureState() == before, "Particle edit undo lost native state");
            if (format == 2)
            {
                Require(!session.CanEditClothSettings, "BPHCL cloth settings enabled");
                var blocked = false;
                try { session.UpdateClothSettings(new()); } catch (InvalidOperationException) { blocked = true; }
                Require(blocked && session.Document.CaptureState() == before, "BPHCL settings guard failed"); continue;
            }
            var settings = session.Document.GetSimulationSettings(0);
            settings.DampingPerSecond = .37f; settings.GravityY = -3.5f; settings.CollisionTolerance = .003f;
            settings.TransferTranslationMotion = true; settings.MinTranslationSpeed = 1; settings.MaxTranslationSpeed = 3;
            settings.MinTranslationBlend = .2f; settings.MaxTranslationBlend = .8f;
            settings.TransferRotationMotion = true; settings.MinRotationSpeed = .5f; settings.MaxRotationSpeed = 2;
            settings.MinRotationBlend = .1f; settings.MaxRotationBlend = .6f;
            session.UpdateClothSettings(settings);
            var changed = session.Document.CaptureState(); session.Undo(); Require(session.Document.CaptureState() == before, "Cloth settings undo"); session.Redo();
            Require(session.Document.CaptureState() == changed, "Cloth settings redo");
            var path = Path.Combine(output, $"tuned-{format}.hkcl"); session.Save(path);
            var saved = new EditorSession(); saved.Load(path); var loaded = saved.Document.GetSimulationSettings(0);
            Require(loaded.DampingPerSecond == .37f && loaded.GravityY == -3.5f && loaded.CollisionTolerance == .003f, "Saved cloth settings lost");
            Require(loaded.TransferTranslationMotion && loaded.MinTranslationSpeed == 1 && loaded.MaxTranslationBlend == .8f && loaded.TransferRotationMotion && loaded.MaxRotationSpeed == 2, "Motion transfer settings lost");
            var clean = session.Document.CaptureState(); settings.MinTranslationSpeed = 4;
            var invalid = false; try { session.UpdateClothSettings(settings); } catch (ArgumentOutOfRangeException) { invalid = true; }
            Require(invalid && session.Document.CaptureState() == clean && !session.IsDirty, "Invalid range partially committed");
        }
        foreach (var speed in new[] { .5f, 1f, 2f, 4f })
        {
            var clock = new PreviewStepClock(); var steps = 0;
            for (int i = 0; i < 60; i++) steps += clock.Advance(1d / 60, speed);
            Require(steps == (int)(60 * speed), "Preview speed lost elapsed time");
            clock.Reset(); Require(clock.Advance(10, 4) == 12, "Unbounded stalled-UI catch-up");
            clock.Reset(); Require(clock.Advance(double.NaN, 1) == 0, "Invalid clock input advanced simulation");
        }
        var irregular = new PreviewStepClock(); var total = 0;
        for (int i = 0; i < 50; i++) { total += irregular.Advance(.005, 1); total += irregular.Advance(.015, 1); }
        Require(total == 60, "Jitter changed preview speed");
        var source = new EditorSession(); source.Load(Path.Combine(output, "manual-0.hkcl"));
        var sourceState = source.Document.CaptureState(); var solver = new HkclPreviewSimulator(source.Document.GetParticlePreview(0));
        var options = new PreviewOptions { Direction = WindAxis.NegativeZ, WindSpeed = 30, GravityScale = 2, Gustiness = .7f, SolverPasses = 3 };
        options.ApplyTo(solver);
        Require(!solver.RandomWindDirections && solver.WindDirection.Z == -1 && solver.WindSpeed == 30 && solver.GravityScale == 2 && solver.SolverIterations == 3, "Preview options not applied");
        solver.WindEnabled = true;
        for (int i = 0; i < 30; i++) solver.Step((float)PreviewStepClock.FixedStep);
        Require(solver.GetPositions().Values.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)), "Tuned solver non-finite");
        Require(source.Document.CaptureState() == sourceState && !source.IsDirty, "Preview modified native cloth");
        Console.WriteLine("PASS: fixed/selected/all mass scaling, inverse mass, cloth settings round-trips/guards, preview options and fixed-step speed.");
    }
}
