using System.Globalization;

namespace HKCLTool;

// Diagnostic-only ranking of a converted cloth against native BPHCL units.
// It scores semantic layout rather than byte offsets or ITEM ids.
internal static class BphclNativeFamilyMatcher
{
    public static void Write(string candidatePath, string clothSelector, string manifestPath, int resultCount = 12)
    {
        var candidateDocument = NativeBphclDocument.Open(candidatePath);
        var candidate = SelectCloth(candidateDocument, clothSelector);
        var target = Describe(candidateDocument, candidate);
        var results = new List<Match>();

        foreach (var path in File.ReadLines(manifestPath)
                     .Select(line => line.Trim())
                     .Where(line => line.Length > 0 && !line.StartsWith('#'))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var document = NativeBphclDocument.Open(path);
                foreach (var cloth in document.Cloths)
                {
                    var signature = Describe(document, cloth);
                    results.Add(new Match(path, cloth, signature, Score(target, signature)));
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"familyError|file={path}|error={exception.GetType().Name}:{exception.Message}");
            }
        }

        Console.WriteLine($"familyTarget|file={Path.GetFileName(candidatePath)}|cloth={candidate.Index}:{candidate.Name}|signature={target.Text}");
        foreach (var result in results
                     .OrderByDescending(result => result.Score.Total)
                     .ThenBy(result => result.Path, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(result => result.Cloth.Index)
                     .Take(Math.Max(1, resultCount)))
        {
            Console.WriteLine(
                $"familyMatch|score={result.Score.Total}|file={result.Path}|cloth={result.Cloth.Index}:{result.Cloth.Name}|" +
                $"exact=[{string.Join(',', result.Score.Exact)}]|delta=[{string.Join(',', result.Score.Deltas)}]|signature={result.Signature.Text}");
        }
    }

    private static NativeBphclCloth SelectCloth(NativeBphclDocument document, string selector)
    {
        if (int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            return document.Cloths.ElementAtOrDefault(index)
                   ?? throw new ArgumentOutOfRangeException(nameof(selector), $"No cloth exists at index {index}.");

        static string Key(string name) => name.StartsWith("Link:", StringComparison.Ordinal) ? name[5..] : name;
        return document.Cloths.FirstOrDefault(cloth => string.Equals(Key(cloth.Name), Key(selector), StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidDataException($"No cloth named ''{selector}'' exists.");
    }

    private static Signature Describe(NativeBphclDocument document, NativeBphclCloth cloth)
    {
        var sim = cloth.SimCloths.SingleOrDefault()
                  ?? throw new InvalidDataException($"Cloth ''{cloth.Name}'' does not contain exactly one simulation cloth.");
        var referencedColliders = sim.CollidableItemIndices
            .Select(item => document.Colliders.FirstOrDefault(collider => collider.ItemIndex == item))
            .Where(collider => collider is not null)
            .Cast<NativeBphclCollider>()
            .ToArray();
        var constraintProgram = sim.ConstraintSets.Select(set =>
            $"{set.ClassName}:{set.Links.Count}/{set.LocalConstraints.Count}/{set.LocalStiffnessConstraints.Count}/{set.TransitionParticles.Count}").ToArray();
        var constraintMetadata = sim.ConstraintSets.Select(DescribeConstraintMetadata).ToArray();
        var constraintRecords = sim.ConstraintSets.Select(DescribeConstraintRecords).ToArray();
        var constraintFields = sim.ConstraintSets.Select(DescribeConstraintFields).ToArray();
        var operators = cloth.Operators.Select(op => op.ClassName).ToArray();
        var operatorRoutes = cloth.Operators.Select(DescribeOperatorRoute).ToArray();
        var simulate = cloth.Operators
            .Where(op => string.Equals(op.ClassName, "hclSimulateOperator", StringComparison.Ordinal))
            .SelectMany(op => op.SimulateConfigs)
            .Select(config =>
                $"exec={Join(config.ConstraintExecution)};coll={Join(config.InstanceCollidablesUsed)};" +
                $"steps={config.SubSteps};iters={config.NumberOfSolveIterations};all={config.UseAllInstanceCollidables};adapt={config.AdaptConstraintStiffness}")
            .ToArray();
        var gatherRoutes = cloth.Operators
            .Where(op => string.Equals(op.ClassName, "hclGatherAllVerticesOperator", StringComparison.Ordinal))
            .Select(op => $"{op.InputBufferIndex}>{op.OutputBufferIndex}:{Join(op.VertexInputFromVertexOutput)}")
            .ToArray();
        var states = cloth.States.Select(DescribeState).ToArray();
        var scratch = cloth.BufferDefinitions.FirstOrDefault(buffer => buffer.ClassName.Contains("Scratch", StringComparison.Ordinal))?.VertexCount ?? 0;
        var skin = cloth.ObjectSpaceSkins.Count > 0 ? "Object" : cloth.BoneSpaceSkins.Count > 0 ? "Bone" : "None";
        var blend = cloth.ObjectSpaceSkins.Select(DescribeSkin)
            .Concat(cloth.BoneSpaceSkins.Select(DescribeSkin))
            .ToArray();
        var shapes = referencedColliders.GroupBy(collider => collider.Shape.TypeName)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:{group.Count()}")
            .ToArray();

        return new Signature(
            sim.Particles.Count,
            sim.Particles.Count(particle => !particle.Fixed),
            sim.TriangleCount,
            sim.ConstraintSets.Sum(set => set.RecordCount),
            constraintProgram,
            referencedColliders.Length,
            shapes,
            operators,
            operatorRoutes,
            simulate,
            DescribeRuntime(sim.Runtime),
            sim.StaticCollisionMasks.Select(mask => $"{mask:X8}").ToArray(),
            constraintMetadata,
            constraintRecords,
            constraintFields,
            DescribeParticles(sim.Particles),
            DescribeTriangleTopology(sim.TriangleIndices, sim.TriangleFlips),
            gatherRoutes,
            states,
            skin,
            scratch,
            blend,
            cloth.SimpleMeshBoneDeformers.Sum(deformer => deformer.TriangleBonePairs.Count));
    }

    private static string DescribeSkin(NativeBphclObjectSpaceSkin value) =>
        $"8:{Count(value.Deformer.EightBlendEntries)},7:{Count(value.Deformer.SevenBlendEntries)},6:{Count(value.Deformer.SixBlendEntries)},5:{Count(value.Deformer.FiveBlendEntries)}," +
        $"4:{Count(value.Deformer.FourBlendEntries)},3:{Count(value.Deformer.ThreeBlendEntries)},2:{Count(value.Deformer.TwoBlendEntries)},1:{Count(value.Deformer.OneBlendEntries)};" +
        $"packed={value.LocalPs.Count};unpacked={value.LocalUnpackedPs.Count};control={value.Deformer.ControlBytes.Count}";

    private static string DescribeSkin(NativeBphclBoneSpaceSkin value) =>
        $"4:{Count(value.Deformer.FourBlendEntries)},3:{Count(value.Deformer.ThreeBlendEntries)},2:{Count(value.Deformer.TwoBlendEntries)},1:{Count(value.Deformer.OneBlendEntries)};" +
        $"local={value.LocalPs.Count};unpacked={value.LocalUnpackedPs.Count};control={value.Deformer.ControlBytes.Count}";

    private static int Count(IReadOnlyList<NativeBphclPackedArray> values) => values.Sum(value => value.Count);

    private static string DescribeConstraintMetadata(NativeBphclConstraintSet set)
    {
        var values = set.Values
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => $"{value.Key}={value.Value.ToString("G9", CultureInfo.InvariantCulture)}");
        return $"{set.ClassName}:id={set.ConstraintId};type={set.ConstraintType};anti={set.IsAntiPinch};[{string.Join(',', values)}]";
    }

    private static string DescribeOperatorRoute(NativeBphclOperatorLayout op) =>
        $"{op.ClassName}:{op.InputBufferIndex}>{op.OutputBufferIndex};ts={op.TransformSetIndex};sim={op.SimulationClothIndex};ref={op.ReferenceBufferIndex};" +
        $"v={op.VertexCount};range={op.StartVertexIn}>{op.StartVertexOut};pairs={string.Join(',', op.VertexParticlePairs.Select(pair => $"{pair.VertexIndex}>{pair.ParticleIndex}"))}";

    private static string DescribeConstraintFields(NativeBphclConstraintSet set)
    {
        var fields = EnumerateConstraintRecords(set)
            .SelectMany(record => record.Link.Values.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);
        return $"{set.ClassName}:[{string.Join(',', fields)}]";
    }

    private static string DescribeConstraintRecords(NativeBphclConstraintSet set)
    {
        var records = EnumerateConstraintRecords(set).ToArray();
        var pairShape = records
            .Select(record => $"{record.Kind}:{record.Link.ParticleA?.ToString(CultureInfo.InvariantCulture) ?? "-"}>{record.Link.ParticleB?.ToString(CultureInfo.InvariantCulture) ?? "-"}")
            .ToArray();
        var valueStats = records
            .SelectMany(record => record.Link.Values.Select(value => (value.Key, value.Value)))
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var values = group.Select(value => value.Value).ToArray();
                return $"{group.Key}:{values.Min().ToString("G9", CultureInfo.InvariantCulture)}/" +
                       $"{values.Max().ToString("G9", CultureInfo.InvariantCulture)}/" +
                       $"{values.Average().ToString("G9", CultureInfo.InvariantCulture)}x{values.Length}";
            });
        return $"{set.ClassName}:pairs=[{string.Join(',', pairShape)}];values=[{string.Join(',', valueStats)}]";
    }

    private static IEnumerable<(string Kind, NativeBphclConstraintLink Link)> EnumerateConstraintRecords(NativeBphclConstraintSet set)
    {
        foreach (var link in set.Links)
            yield return ("L", link);
        foreach (var link in set.LocalConstraints)
            yield return ("C", link);
        foreach (var link in set.LocalStiffnessConstraints)
            yield return ("S", link);
        foreach (var link in set.TransitionParticles)
            yield return ("T", link);
    }

    private static string DescribeParticles(IReadOnlyList<NativeBphclParticle> particles)
    {
        if (particles.Count == 0)
            return "empty";

        static string Stats(IEnumerable<float> source)
        {
            var values = source.Select(value => (double)value).ToArray();
            return $"{values.Min().ToString("G9", CultureInfo.InvariantCulture)}/" +
                   $"{values.Max().ToString("G9", CultureInfo.InvariantCulture)}/" +
                   values.Average().ToString("G9", CultureInfo.InvariantCulture);
        }

        return $"fixed=[{string.Join(',', particles.Where(particle => particle.Fixed).Select(particle => particle.Index))}];" +
               $"mass={Stats(particles.Select(particle => particle.Mass))};inv={Stats(particles.Select(particle => particle.InverseMass))};" +
               $"radius={Stats(particles.Select(particle => particle.Radius))};friction={Stats(particles.Select(particle => particle.Friction))};" +
               $"x={Stats(particles.Select(particle => particle.Position.X))};y={Stats(particles.Select(particle => particle.Position.Y))};" +
               $"z={Stats(particles.Select(particle => particle.Position.Z))}";
    }

    private static string DescribeTriangleTopology(IReadOnlyList<ushort> indices, IReadOnlyList<byte> flips)
    {
        if (indices.Count == 0)
            return "empty";

        var counts = new Dictionary<ushort, int>();
        var edges = new Dictionary<(ushort A, ushort B), int>();
        for (var index = 0; index + 2 < indices.Count; index += 3)
        {
            var triangle = new[] { indices[index], indices[index + 1], indices[index + 2] };
            foreach (var vertex in triangle)
                counts[vertex] = counts.GetValueOrDefault(vertex) + 1;
            AddEdge(triangle[0], triangle[1]);
            AddEdge(triangle[1], triangle[2]);
            AddEdge(triangle[2], triangle[0]);
        }

        return $"degree=[{string.Join(',', counts.Values.Order())}];edges={edges.Count};" +
               $"boundary={edges.Values.Count(count => count == 1)};shared={edges.Values.Count(count => count == 2)};" +
               $"flips=[{string.Join(',', flips)}]";

        void AddEdge(ushort left, ushort right)
        {
            var edge = left <= right ? (left, right) : (right, left);
            edges[edge] = edges.GetValueOrDefault(edge) + 1;
        }
    }

    private static string DescribeRuntime(NativeBphclSimulationRuntime runtime)
    {
        var transfer = runtime.TransferMotionData;
        var landscape = runtime.LandscapeCollisionData;
        return
            $"normals={runtime.DoNormals};transfer={runtime.TransferMotionEnabled}:" +
            $"{transfer.TransformSetIndex}/{transfer.TransformIndex}/{transfer.TransferTranslationMotion}/{transfer.TransferRotationMotion};" +
            $"landscape={runtime.LandscapeCollisionEnabled}/{runtime.NumLandscapeCollidableParticles}:" +
            $"{landscape.LandscapeRadius.ToString("G9", CultureInfo.InvariantCulture)}/" +
            $"{landscape.EnableStuckParticleDetection}/{landscape.StuckParticlesStretchFactorSq.ToString("G9", CultureInfo.InvariantCulture)}/" +
            $"{landscape.CollisionTolerance.ToString("G9", CultureInfo.InvariantCulture)};" +
            $"pinch={runtime.PinchDetectionEnabled}/{runtime.MinPinchedParticleIndex}/{runtime.MaxPinchedParticleIndex};" +
            $"pairs={runtime.MaxCollisionPairs}";
    }

    private static string DescribeState(NativeBphclClothState state)
    {
        var buffers = state.BufferAccesses.Select(access =>
            $"{access.BufferIndex}:{access.ComponentFlags0:X2}{access.ComponentFlags1:X2}{access.ComponentFlags2:X2}{access.ComponentFlags3:X2}:" +
            $"{access.TrianglesRead}:{access.ShadowBufferIndex}");
        var transforms = state.TransformSetAccesses.Select(access =>
            $"{access.TransformSetIndex}:{access.ComponentFlags0:X2}{access.ComponentFlags1:X2}:" +
            string.Join('/', access.TransformTrackers.Select(tracker =>
                $"{JoinHex(tracker.Read.Words)}>{JoinHex(tracker.ReadBeforeWrite.Words)}>{JoinHex(tracker.Written.Words)}")));
        return $"{state.Name}:ops={Join(state.OperatorItemIndices)};sim={Join(state.SimClothItemIndices)};buf=[{string.Join(',', buffers)}];ts=[{string.Join(',', transforms)}]";
    }

    private static string Join(IEnumerable<int> values) => string.Join(',', values);
    private static string Join(IEnumerable<short> values) => string.Join(',', values);
    private static string JoinHex(IEnumerable<uint> values) => string.Join('.', values.Select(value => value.ToString("X8", CultureInfo.InvariantCulture)));

    private static MatchScore Score(Signature target, Signature candidate)
    {
        var exact = new List<string>();
        var deltas = new List<string>();
        var score = 0;

        AddDistance("particles", target.Particles, candidate.Particles, 36, 3);
        AddDistance("dynamic", target.DynamicParticles, candidate.DynamicParticles, 24, 3);
        AddDistance("triangles", target.Triangles, candidate.Triangles, 30, 2);
        AddDistance("records", target.ConstraintRecords, candidate.ConstraintRecords, 10, 1);
        AddExact("constraints", target.ConstraintProgram, candidate.ConstraintProgram, 55);
        AddDistance("colliders", target.Colliders, candidate.Colliders, 16, 2);
        AddExact("shapes", target.ColliderShapes, candidate.ColliderShapes, 18);
        AddExact("operators", target.Operators, candidate.Operators, 42);
        AddExact("routes", target.OperatorRoutes, candidate.OperatorRoutes, 36);
        AddExact("simulate", target.Simulate, candidate.Simulate, 45);
        AddExact("runtime", new[] { target.Runtime }, new[] { candidate.Runtime }, 32);
        AddExact("masks", target.CollisionMasks, candidate.CollisionMasks, 28);
        AddExact("setMeta", target.ConstraintMetadata, candidate.ConstraintMetadata, 40);
        AddExact("recordFields", target.ConstraintFields, candidate.ConstraintFields, 24);
        AddExact("recordStats", target.ConstraintRecordStats, candidate.ConstraintRecordStats, 36);
        AddExact("particleValues", new[] { target.ParticleSummary }, new[] { candidate.ParticleSummary }, 30);
        AddExact("topology", new[] { target.TriangleTopology }, new[] { candidate.TriangleTopology }, 24);
        AddExact("gather", target.GatherRoutes, candidate.GatherRoutes, 36);
        AddExact("states", target.States, candidate.States, 36);
        AddExact("skin", new[] { target.Skin }, new[] { candidate.Skin }, 18);
        AddDistance("scratch", checked((int)target.ScratchVertices), checked((int)candidate.ScratchVertices), 14, 1);
        AddExact("blend", target.Blend, candidate.Blend, 28);
        AddDistance("bonePairs", target.BonePairs, candidate.BonePairs, 10, 1);

        return new MatchScore(score, exact, deltas);

        void AddExact(string name, IReadOnlyList<string> left, IReadOnlyList<string> right, int weight)
        {
            if (!left.SequenceEqual(right, StringComparer.Ordinal))
                return;
            score += weight;
            exact.Add(name);
        }

        void AddDistance(string name, int left, int right, int weight, int penalty)
        {
            var delta = Math.Abs(left - right);
            score += Math.Max(0, weight - delta * penalty);
            if (delta == 0)
                exact.Add(name);
            else
                deltas.Add($"{name}:{delta}");
        }
    }

    private sealed record Match(string Path, NativeBphclCloth Cloth, Signature Signature, MatchScore Score);
    private sealed record MatchScore(int Total, IReadOnlyList<string> Exact, IReadOnlyList<string> Deltas);

    private sealed record Signature(
        int Particles,
        int DynamicParticles,
        int Triangles,
        int ConstraintRecords,
        IReadOnlyList<string> ConstraintProgram,
        int Colliders,
        IReadOnlyList<string> ColliderShapes,
        IReadOnlyList<string> Operators,
        IReadOnlyList<string> OperatorRoutes,
        IReadOnlyList<string> Simulate,
        string Runtime,
        IReadOnlyList<string> CollisionMasks,
        IReadOnlyList<string> ConstraintMetadata,
        IReadOnlyList<string> ConstraintRecordStats,
        IReadOnlyList<string> ConstraintFields,
        string ParticleSummary,
        string TriangleTopology,
        IReadOnlyList<string> GatherRoutes,
        IReadOnlyList<string> States,
        string Skin,
        uint ScratchVertices,
        IReadOnlyList<string> Blend,
        int BonePairs)
    {
        public string Text =>
            $"p={Particles}/{DynamicParticles};tri={Triangles};records={ConstraintRecords};sets=[{string.Join(',', ConstraintProgram)}];" +
            $"colliders={Colliders}[{string.Join(',', ColliderShapes)}];ops=[{string.Join(',', Operators)}];skin={Skin};" +
            $"routes=[{string.Join('|', OperatorRoutes)}];simulate=[{string.Join('|', Simulate)}];runtime={Runtime};" +
            $"masks=[{string.Join(',', CollisionMasks)}];setMeta=[{string.Join('|', ConstraintMetadata)}];" +
            $"recordFields=[{string.Join('|', ConstraintFields)}];recordStats=[{string.Join('|', ConstraintRecordStats)}];" +
            $"particles=[{ParticleSummary}];topology=[{TriangleTopology}];" +
            $"gather=[{string.Join('|', GatherRoutes)}];states=[{string.Join('|', States)}];" +
            $"scratch={ScratchVertices};blend=[{string.Join('/', Blend)}];bonePairs={BonePairs}";
    }
}
