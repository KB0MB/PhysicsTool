using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using HKCLTool;

return HkclToBphclRegression.Run(args);

internal static class HkclToBphclRegression
{
    private const float Epsilon = 0.0001f;

    public static int Run(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--verify-bphyssb", StringComparison.OrdinalIgnoreCase)))
            return VerifyBphyssb(ReadArgument(args, "--bphyssb") ?? string.Empty);
        if (args.Any(argument => string.Equals(argument, "--compile-native-type-catalog", StringComparison.OrdinalIgnoreCase)))
            return CompileNativeTypeCatalog(ReadArgument(args, "--bphcl"), ReadArgument(args, "--output"));
        if (args.Any(argument => string.Equals(argument, "--audit-standalone-tree", StringComparison.OrdinalIgnoreCase)))
            return AuditStandaloneWriter(ReadArgument(args, "--root") ?? string.Empty, SearchOption.AllDirectories);
        if (args.Any(argument => string.Equals(argument, "--audit-hkrg-constraint-repair", StringComparison.OrdinalIgnoreCase)))
            return AuditHkrgConstraintRepair(ReadArgument(args, "--hkrg") ?? string.Empty);
        if (args.Any(argument => string.Equals(argument, "--list-hkrg-constraints", StringComparison.OrdinalIgnoreCase)))
            return ListHkrgConstraints(ReadArgument(args, "--hkrg") ?? string.Empty);
        if (args.Any(argument => string.Equals(argument, "--diagnose-hkrg-mappers", StringComparison.OrdinalIgnoreCase)))
            return DiagnoseHkrgMappers(ReadArgument(args, "--hkrg") ?? string.Empty);
        if (args.Any(argument => string.Equals(argument, "--diagnose-hkrg-mapper-pairs", StringComparison.OrdinalIgnoreCase)))
            return DiagnoseHkrgMapperPairs(ReadArgument(args, "--hkrg") ?? string.Empty);
        if (args.Any(argument => string.Equals(argument, "--diagnose-hkrg-motors", StringComparison.OrdinalIgnoreCase)))
            return DiagnoseHkrgMotors(ReadArgument(args, "--hkrg") ?? string.Empty);
        if (args.Any(argument => string.Equals(argument, "--diagnose-hkrg-hinges", StringComparison.OrdinalIgnoreCase)))
            return DiagnoseHkrgHinges(ReadArgument(args, "--hkrg") ?? string.Empty);
        if (args.Any(argument => string.Equals(argument, "--export-hkrg-raw", StringComparison.OrdinalIgnoreCase)))
            return ExportHkrgRaw(ReadArgument(args, "--hkrg") ?? string.Empty, ReadArgument(args, "--output") ?? string.Empty);        if (args.Any(argument => string.Equals(argument, "--make-mipha-hkrg-test", StringComparison.OrdinalIgnoreCase)))
            return MakeMiphaHkrgTestFile(ReadArgument(args, "--link") ?? string.Empty, ReadArgument(args, "--mipha") ?? string.Empty, ReadArgument(args, "--output") ?? string.Empty, args);
        if (args.Any(argument => string.Equals(argument, "--compare-hkrg-constraints", StringComparison.OrdinalIgnoreCase)))
            return CompareHkrgConstraints(ReadArgument(args, "--source") ?? string.Empty, ReadArgument(args, "--target") ?? string.Empty, ReadArgument(args, "--output") ?? string.Empty);
        if (args.Any(argument => string.Equals(argument, "--diff-hkrg-constraint-atoms", StringComparison.OrdinalIgnoreCase)))
            return DiffHkrgConstraintAtoms(ReadArgument(args, "--source") ?? string.Empty, ReadArgument(args, "--target") ?? string.Empty, ReadArgument(args, "--output") ?? string.Empty);
        var matchesPath = ReadArgument(args, "--matches")
            ?? Environment.GetEnvironmentVariable("PHYSICSTOOL_MATCHES");
        var keepOutput = args.Any(argument => string.Equals(argument, "--keep-output", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(matchesPath) || !Directory.Exists(matchesPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --matches <folder> [--keep-output]");
            return 2;
        }

        if (args.Any(argument => string.Equals(argument, "--audit-schema", StringComparison.OrdinalIgnoreCase)))
            return AuditBphclSchemas(matchesPath);

        if (args.Any(argument => string.Equals(argument, "--audit-type-conflicts", StringComparison.OrdinalIgnoreCase)))
            return AuditBphclTypeDefinitions(matchesPath);

        if (args.Any(argument => string.Equals(argument, "--audit-standalone-graph", StringComparison.OrdinalIgnoreCase)))
            return AuditStandaloneGraphExtraction(matchesPath);

        if (args.Any(argument => string.Equals(argument, "--audit-standalone-envelope", StringComparison.OrdinalIgnoreCase)))
            return AuditStandaloneEnvelope();

        if (args.Any(argument => string.Equals(argument, "--audit-standalone-write", StringComparison.OrdinalIgnoreCase)))
            return AuditStandaloneWriter(matchesPath);

        if (args.Any(argument => string.Equals(argument, "--audit-standalone-constraints", StringComparison.OrdinalIgnoreCase)))
            return AuditStandaloneConstraints(matchesPath);

        if (args.Any(argument => string.Equals(argument, "--audit-standalone-operators", StringComparison.OrdinalIgnoreCase)))
            return AuditStandaloneOperators(matchesPath);

        if (args.Any(argument => string.Equals(argument, "--audit-bphcl-skin-types", StringComparison.OrdinalIgnoreCase)))
            return AuditBphclSkinTypes(matchesPath);

        if (args.Any(argument => string.Equals(argument, "--audit-standalone-layout", StringComparison.OrdinalIgnoreCase)))
            return AuditStandaloneLayout();

        if (args.Any(argument => string.Equals(argument, "--audit-native-pipeline", StringComparison.OrdinalIgnoreCase)))
            return AuditNativePipeline(matchesPath, ReadArgument(args, "--bphcl"));

        if (args.Any(argument => string.Equals(argument, "--audit-hkx2-boneplanes", StringComparison.OrdinalIgnoreCase)))
            return AuditHkx2BonePlanes();

        var outputDirectory = Path.Combine(Path.GetTempPath(), "PhysicsTool-HkclToBphcl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        try
        {
            var pairs = FindFilePairs(matchesPath).ToArray();
            if (pairs.Length == 0)
            {
                Console.Error.WriteLine("No HKCL/BPHCL file pairs were found.");
                return 2;
            }

            var result = new TestResult();
            foreach (var pair in pairs)
                TestPair(pair, outputDirectory, result);

            Console.WriteLine();
            Console.WriteLine($"Files: {result.FilePairs}; matched cloths: {result.MatchedCloths}; converted: {result.ConvertedCloths}; skipped: {result.SkippedCloths}; failures: {result.Failures.Count}");
            foreach (var failure in result.Failures)
                Console.Error.WriteLine("FAIL: " + failure);

            return result.Failures.Count == 0 ? 0 : 1;
        }
        finally
        {
            if (keepOutput)
                Console.WriteLine($"Kept converted test files: {outputDirectory}");
            else
                Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static int VerifyBphyssb(string bphyssbPath)
    {
        if (string.IsNullOrWhiteSpace(bphyssbPath) || !File.Exists(bphyssbPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --verify-bphyssb --bphyssb <file.bphyssb>");
            return 2;
        }

        var document = new HkclService();
        document.Load(bphyssbPath);
        Require(document.IsBphyssb, "The file did not load as BPHYSSB.");
        var bones = document.GetBoneRows(0);
        Console.WriteLine($"BPHYSSB loaded: bones={bones.Count}; clothEntries={document.GetClothSummaries().Count}.");
        return 0;
    }

    private static int DiagnoseHkrgMappers(string hkrgPath)
    {
        if (string.IsNullOrWhiteSpace(hkrgPath) || !File.Exists(hkrgPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --diagnose-hkrg-mappers --hkrg <file.hkrg>");
            return 2;
        }

        try
        {
            var document = new HkclService();
            document.Load(hkrgPath);
            Require(document.IsHkrg, "Input did not load as HKRG.");

            foreach (var row in document.GetHkrgMapperPoseDiagnostics())
            {
                Console.WriteLine($"mapper {row.MapperIndex}, map {row.MappingIndex}: A[{row.BoneA}] <- B[{row.BoneB}] | B->A error pos={row.BToAPositionError:0.######} rot={row.BToARotationErrorDegrees:0.######}deg | A->B error pos={row.AToBPositionError:0.######} rot={row.AToBRotationErrorDegrees:0.######}deg");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int DiagnoseHkrgMapperPairs(string hkrgPath)
    {
        if (string.IsNullOrWhiteSpace(hkrgPath) || !File.Exists(hkrgPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --diagnose-hkrg-mapper-pairs --hkrg <file.hkrg>");
            return 2;
        }

        try
        {
            var document = new HkclService();
            document.Load(hkrgPath);
            Require(document.IsHkrg, "Input did not load as HKRG.");

            foreach (var row in document.GetHkrgMapperPairDiagnostics())
            {
                Console.WriteLine(
                    $"model[{row.ModelBone}] �w^~)�t ragdoll[{row.RagdollBone}] | " +
                    $"forward={row.ForwardMapperIndex}:{row.ForwardMappingIndex}, reverse={row.ReverseMapperIndex}:{row.ReverseMappingIndex} | " +
                    $"ragdoll round-trip pos={row.RagdollRoundTripPositionError:0.######}, rot={row.RagdollRoundTripRotationErrorDegrees:0.######}deg | " +
                    $"model round-trip pos={row.ModelRoundTripPositionError:0.######}, rot={row.ModelRoundTripRotationErrorDegrees:0.######}deg");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int DiagnoseHkrgHinges(string hkrgPath)
    {
        if (string.IsNullOrWhiteSpace(hkrgPath) || !File.Exists(hkrgPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --diagnose-hkrg-hinges --hkrg <file.hkrg>");
            return 2;
        }

        try
        {
            var document = new HkclService();
            document.Load(hkrgPath);
            Require(document.IsHkrg, "Input did not load as HKRG.");
            foreach (var row in document.GetHkrgHingeReferenceDiagnostics())
            {
                Console.WriteLine($"{row.ConstraintIndex,2}: {row.ConstraintName,-24} | reference={row.ReferenceAngleDegrees,8:0.###} deg | limit=[{row.MinimumAngleDegrees:0.###}, {row.MaximumAngleDegrees:0.###}] | off-axis={row.OffAxisDegrees:0.###} deg | inside={row.IsInsideLimit}");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
    private static int DiagnoseHkrgMotors(string hkrgPath)
    {
        if (string.IsNullOrWhiteSpace(hkrgPath) || !File.Exists(hkrgPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --diagnose-hkrg-motors --hkrg <file.hkrg>");
            return 2;
        }

        try
        {
            var document = new HkclService();
            document.Load(hkrgPath);
            Require(document.IsHkrg, "Input did not load as HKRG.");

            foreach (var row in document.GetHkrgMotorTargetDiagnostics())
            {
                Console.WriteLine($"{row.ConstraintIndex,2}: {row.ConstraintName,-24} | frameB={row.FrameBErrorDegrees:0.###} deg | frameA={row.FrameAErrorDegrees:0.###} deg | b*inv(a)={row.BTimesInverseAErrorDegrees:0.###} deg | inv(b)*a={row.InverseBTimesAErrorDegrees:0.###} deg | a*inv(b)={row.ATimesInverseBErrorDegrees:0.###} deg | inv(a)*b={row.InverseATimesBErrorDegrees:0.###} deg");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
    // Diagnostic only: writes the native object graph so two compatible HKRG
    // files can be compared without changing either input.
    private static int ExportHkrgRaw(string hkrgPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(hkrgPath) || !File.Exists(hkrgPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --export-hkrg-raw --hkrg <file.hkrg> --output <file.json>");
            return 2;
        }

        try
        {
            var document = new HkclService();
            document.Load(hkrgPath);
            Require(document.IsHkrg, "Input did not load as HKRG.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            document.ExportReadableJson(outputPath);
            Console.WriteLine($"Exported HKRG raw graph: {outputPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
    private static int ListHkrgConstraints(string hkrgPath)
    {
        if (string.IsNullOrWhiteSpace(hkrgPath) || !File.Exists(hkrgPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --list-hkrg-constraints --hkrg <file.hkrg>");
            return 2;
        }

        try
        {
            var document = new HkclService();
            document.Load(hkrgPath);
            Require(document.IsHkrg, "Input did not load as HKRG.");

            foreach (var row in document.GetHkrgConstraintRows())
            {
                Console.WriteLine($"{row.Index,2}: {row.ConstraintType,-14} {row.BodyA,2}:{row.BodyAName} -> {row.BodyB,2}:{row.BodyBName} | twist={Range(row.TwistMinAngle, row.TwistMaxAngle)} cone={Range(row.ConeMinAngle, row.ConeMaxAngle)} planes={Range(row.PlaneMinAngle, row.PlaneMaxAngle)} hinge={Range(row.HingeMinAngle, row.HingeMaxAngle)}");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
    private static int CompareHkrgConstraints(string sourcePath, string targetPath, string outputPath)
    {
        if (!File.Exists(sourcePath) || !File.Exists(targetPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --compare-hkrg-constraints --source <source.hkrg> --target <target.hkrg> --output <report.csv>");
            return 2;
        }

        try
        {
            var source = new HkclService();
            source.Load(sourcePath);
            var target = new HkclService();
            target.Load(targetPath);
            Require(source.IsHkrg && target.IsHkrg, "Both inputs must be HKRG files.");
            var sourceRows = source.GetHkrgConstraintPoseDiagnostics();
            var targetRows = target.GetHkrgConstraintPoseDiagnostics();
            Require(sourceRows.Count == targetRows.Count, $"Constraint count mismatch: source={sourceRows.Count}, target={targetRows.Count}.");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine("Index,Name,Bones,SourceFrameGap,TargetFrameGap,SourceFrameAngle,TargetFrameAngle,FrameRelationDelta,SkeletonRelationDelta,SourceSkeletonAngle,TargetSkeletonAngle,SourceMotorEnabled,TargetMotorEnabled,SourceMotorAngle,TargetMotorAngle,MotorRelationDelta,TwistRange,ConeRange,PlanesRange,HingeRange");
            for (var index = 0; index < sourceRows.Count; index++)
            {
                var sourceRow = sourceRows[index];
                var targetRow = targetRows[index];
                writer.WriteLine(string.Join(',', new[]
                {
                    index.ToString(CultureInfo.InvariantCulture),
                    Csv(sourceRow.Name),
                    Csv($"{sourceRow.BoneAName} -> {sourceRow.BoneBName}"),
                    Number(sourceRow.FramePositionGap), Number(targetRow.FramePositionGap),
                    Number(sourceRow.FrameRotationGapDegrees), Number(targetRow.FrameRotationGapDegrees),
                    Number(RotationDifferenceDegrees(sourceRow.FrameRelativeRotation, targetRow.FrameRelativeRotation)),
                    Number(RotationDifferenceDegrees(sourceRow.SkeletonRelativeRotation, targetRow.SkeletonRelativeRotation)),
                    Number(sourceRow.SkeletonRelativeRotationDegrees), Number(targetRow.SkeletonRelativeRotationDegrees),
                    sourceRow.MotorEnabled.ToString(), targetRow.MotorEnabled.ToString(),
                    Number(sourceRow.MotorTargetRotationDegrees), Number(targetRow.MotorTargetRotationDegrees),
                    Number(RotationDifferenceDegrees(sourceRow.MotorTargetRotation, targetRow.MotorTargetRotation)),
                    Range(sourceRow.TwistMin, sourceRow.TwistMax), Range(sourceRow.ConeMin, sourceRow.ConeMax),
                    Range(sourceRow.PlanesMin, sourceRow.PlanesMax), Range(sourceRow.HingeMin, sourceRow.HingeMax)
                }));
            }

            Console.WriteLine($"Wrote HKRG constraint pose comparison: {outputPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int DiffHkrgConstraintAtoms(string sourcePath, string targetPath, string outputPath)
    {
        if (!File.Exists(sourcePath) || !File.Exists(targetPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --diff-hkrg-constraint-atoms --source <source.hkrg> --target <target.hkrg> --output <report.csv>");
            return 2;
        }

        try
        {
            var source = new HkclService();
            source.Load(sourcePath);
            var target = new HkclService();
            target.Load(targetPath);
            Require(source.IsHkrg && target.IsHkrg, "Both inputs must be HKRG files.");

            var sourceValues = source.GetHkrgConstraintAtomValues()
                .ToDictionary(value => (value.ConstraintIndex, value.Path), value => value, EqualityComparer<(int, string)>.Default);
            var targetValues = target.GetHkrgConstraintAtomValues()
                .ToDictionary(value => (value.ConstraintIndex, value.Path), value => value, EqualityComparer<(int, string)>.Default);
            var keys = sourceValues.Keys.Union(targetValues.Keys)
                .OrderBy(key => key.Item1)
                .ThenBy(key => key.Item2, StringComparer.Ordinal)
                .ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine("Constraint,Name,AtomPath,SourceValue,TargetValue");
            var differences = 0;
            foreach (var key in keys)
            {
                sourceValues.TryGetValue(key, out var sourceValue);
                targetValues.TryGetValue(key, out var targetValue);
                var sourceText = sourceValue?.Value ?? "<missing>";
                var targetText = targetValue?.Value ?? "<missing>";
                if (string.Equals(sourceText, targetText, StringComparison.Ordinal))
                    continue;

                differences++;
                writer.WriteLine(string.Join(',', new[]
                {
                    key.Item1.ToString(CultureInfo.InvariantCulture),
                    Csv(sourceValue?.ConstraintName ?? targetValue?.ConstraintName ?? string.Empty),
                    Csv(key.Item2), Csv(sourceText), Csv(targetText)
                }));
            }

            Console.WriteLine($"Wrote {differences} raw HKRG constraint atom difference(s): {outputPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
    private static string Number(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static float RotationDifferenceDegrees(Quaternion from, Quaternion to)
    {
        var dot = Math.Clamp(MathF.Abs(Quaternion.Dot(from, to)), 0.0f, 1.0f);
        return 2.0f * MathF.Acos(dot) * (180.0f / MathF.PI);
    }
    private static string Range(float? min, float? max) => Csv($"{(min?.ToString("0.######", CultureInfo.InvariantCulture) ?? string.Empty)}..{(max?.ToString("0.######", CultureInfo.InvariantCulture) ?? string.Empty)}");
    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    private static int MakeMiphaHkrgTestFile(string linkPath, string miphaPath, string outputPath, string[] args)
    {
        if (string.IsNullOrWhiteSpace(linkPath) || !File.Exists(linkPath) ||
            string.IsNullOrWhiteSpace(miphaPath) || !File.Exists(miphaPath) ||
            string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --make-mipha-hkrg-test --link <Link.hkrg> --mipha <Mipha.hkrg> --output <test.hkrg>");
            return 2;
        }

        try
        {
            var link = new HkclService();
            link.Load(linkPath);
            Require(link.IsHkrg, "Base Link file did not load as HKRG.");

            var linkConstraintReference = new HkclService();
            linkConstraintReference.Load(linkPath);
            Require(linkConstraintReference.IsHkrg, "Link constraint reference did not load as HKRG.");

            var mipha = new HkclService();
            mipha.Load(miphaPath);
            Require(mipha.IsHkrg, "Mipha pose file did not load as HKRG.");

            var linkRagdollBones = link.GetBoneRows(0).ToArray();
            var miphaRagdollBones = mipha.GetBoneRows(0).ToArray();
            var linkModelBones = link.GetHkrgModelBoneRows().ToArray();
            var miphaModelBones = mipha.GetHkrgModelBoneRows().ToArray();
            var linkBodies = link.GetColliderRows(0).ToArray();
            var miphaBodies = mipha.GetColliderRows(0).ToArray();
            var linkConstraints = link.GetHkrgConstraintRows().Count;
            var adoptRagdollBones = !args.Any(argument => string.Equals(argument, "--keep-link-ragdoll-bones", StringComparison.OrdinalIgnoreCase));
            var adoptModelBones = !args.Any(argument => string.Equals(argument, "--keep-link-model-bones", StringComparison.OrdinalIgnoreCase));
            var adoptRigidBodies = !args.Any(argument => string.Equals(argument, "--keep-link-rigid-bodies", StringComparison.OrdinalIgnoreCase));
            var anchorOnly = args.Any(argument => string.Equals(argument, "--anchor-only", StringComparison.OrdinalIgnoreCase));
            var retargetDirectMapperOffsets = args.Any(argument => string.Equals(argument, "--retarget-direct-mapper-offsets", StringComparison.OrdinalIgnoreCase));
            var retargetLegMapperOffsets = args.Any(argument => string.Equals(argument, "--retarget-leg-mapper-offsets", StringComparison.OrdinalIgnoreCase));
            var retargetReverseLegMapperOffsets = args.Any(argument => string.Equals(argument, "--retarget-reverse-leg-mapper-offsets", StringComparison.OrdinalIgnoreCase));
            var retargetTwoWayLegMapperOffsets = args.Any(argument => string.Equals(argument, "--retarget-two-way-leg-mapper-offsets", StringComparison.OrdinalIgnoreCase));
            var retargetReverseLegMapperOutputLocalPivots = args.Any(argument => string.Equals(argument, "--retarget-reverse-leg-mapper-output-local-pivots", StringComparison.OrdinalIgnoreCase));
            var retargetTwoWayLegMapperOutputLocalPivots = args.Any(argument => string.Equals(argument, "--retarget-two-way-leg-mapper-output-local-pivots", StringComparison.OrdinalIgnoreCase));
            var rebuildLegMapperOffsets = args.Any(argument => string.Equals(argument, "--rebuild-leg-mapper-offsets", StringComparison.OrdinalIgnoreCase));
            var rebuildCapsuleCentersOfMass = args.Any(argument => string.Equals(argument, "--rebuild-hkrg-capsule-centers-of-mass", StringComparison.OrdinalIgnoreCase));
            if (new[]
                {
                    retargetLegMapperOffsets,
                    retargetReverseLegMapperOffsets,
                    retargetTwoWayLegMapperOffsets,
                    retargetReverseLegMapperOutputLocalPivots,
                    retargetTwoWayLegMapperOutputLocalPivots,
                    rebuildLegMapperOffsets
                }.Count(enabled => enabled) > 1)
            {
                throw new ArgumentException("Choose only one leg-mapper retargeting mode.");
            }

            Require(linkRagdollBones.Length == miphaRagdollBones.Length,
                $"Ragdoll bone count mismatch: Link={linkRagdollBones.Length}, Mipha={miphaRagdollBones.Length}.");
            Require(linkModelBones.Length == miphaModelBones.Length,
                $"Model bone count mismatch: Link={linkModelBones.Length}, Mipha={miphaModelBones.Length}.");
            Require(linkBodies.Length == miphaBodies.Length,
                $"Rigid body count mismatch: Link={linkBodies.Length}, Mipha={miphaBodies.Length}.");
            Require(linkConstraints > 0, "Link HKRG has no constraints to repair.");

            if (adoptRagdollBones)
                link.UpdateBoneRows(0, miphaRagdollBones);
            if (adoptModelBones)
                link.UpdateHkrgModelBoneRows(miphaModelBones);
            var directMapperOffsets = retargetDirectMapperOffsets
                ? link.RetargetHkrgDirectMapperOffsetsForCurrentPose(linkConstraintReference)
                : 0;
            var legMapperOffsets = retargetLegMapperOffsets
                ? link.RetargetHkrgAnimationToRagdollLegMapperOffsetsForCurrentPose(linkConstraintReference)
                : 0;
            var reverseLegMapperOffsets = retargetReverseLegMapperOffsets
                ? link.RetargetHkrgReverseLegMapperOffsetsForCurrentPose(linkConstraintReference)
                : 0;
            var twoWayLegMapperOffsets = retargetTwoWayLegMapperOffsets
                ? link.RetargetHkrgTwoWayLegMapperOffsetsForCurrentPose(linkConstraintReference)
                : 0;
            var reverseLegMapperOutputLocalPivots = retargetReverseLegMapperOutputLocalPivots
                ? link.RetargetHkrgReverseLegMapperOffsetsWithOutputLocalPivotsForCurrentPose(linkConstraintReference)
                : 0;
            var twoWayLegMapperOutputLocalPivots = retargetTwoWayLegMapperOutputLocalPivots
                ? link.RetargetHkrgTwoWayLegMapperOffsetsWithOutputLocalPivotsForCurrentPose(linkConstraintReference)
                : 0;
            var rebuiltLegMapperOffsets = rebuildLegMapperOffsets
                ? link.RebuildHkrgLegMapperOffsetsFromCurrentPose()
                : 0;
            var sourceSweepError = mipha.GetHkrgMotionSweepPositionError();
            if (adoptRigidBodies)
            {
                link.UpdateColliderRows(miphaBodies);
                link.AdoptHkrgRigidBodyMotionsFrom(mipha);
            }

            var rebuiltCapsuleCentersOfMass = rebuildCapsuleCentersOfMass
                ? link.RebuildHkrgCapsuleCentersOfMass()
                : 0;
            if (adoptRigidBodies || rebuildCapsuleCentersOfMass)
                link.RebuildHkrgMotionSweepsFromTransforms();

            var targetSweepError = link.GetHkrgMotionSweepPositionError();
            if (anchorOnly)
                link.RetargetHkrgConstraintAnchorsFrom(linkConstraintReference);
            else
                link.RetargetHkrgConstraintFramesForTargetPose(linkConstraintReference);

            var preSaveLargestGap = link.GetHkrgConstraintPoseDiagnostics().DefaultIfEmpty().Max(row => row?.FramePositionGap ?? 0.0f);
            Console.WriteLine($"Largest constraint-frame gap before save: {preSaveLargestGap.ToString("0.######", CultureInfo.InvariantCulture)}; swept-body mismatch source={sourceSweepError.ToString("0.######", CultureInfo.InvariantCulture)}, output={targetSweepError.ToString("0.######", CultureInfo.InvariantCulture)}.");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            if (args.Any(argument => string.Equals(argument, "--dump-json", StringComparison.OrdinalIgnoreCase)))
                link.ExportReadableJson(Path.ChangeExtension(outputPath, ".pre-save.json"));
            link.SaveHkcl(outputPath, HkclService.DetectHkclPlatform(linkPath));

            var reopened = new HkclService();
            reopened.Load(outputPath);
            Require(reopened.IsHkrg, "Generated test file did not reopen as HKRG.");
            Require(reopened.GetBoneRows(0).Count == linkRagdollBones.Length, "Generated ragdoll bone count changed after reopen.");
            Require(reopened.GetHkrgModelBoneRows().Count == linkModelBones.Length, "Generated model bone count changed after reopen.");
            Require(reopened.GetColliderRows(0).Count == linkBodies.Length, "Generated rigid body count changed after reopen.");
            Require(reopened.GetHkrgConstraintRows().Count == linkConstraints, "Generated constraint count changed after reopen.");
            Require(reopened.HkrgConstraintFramesAreFinite(), "Generated HKRG constraint frames contain invalid values.");
            if (args.Any(argument => string.Equals(argument, "--dump-json", StringComparison.OrdinalIgnoreCase)))
                reopened.ExportReadableJson(Path.ChangeExtension(outputPath, ".post-save.json"));

            Console.WriteLine($"Generated HKRG test file: {outputPath}");
            Console.WriteLine($"Adopted Mipha pose into Link base: ragdollBones={adoptRagdollBones}, modelBones={adoptModelBones}, rigidBodies={adoptRigidBodies}; {linkRagdollBones.Length} ragdoll bones, {linkModelBones.Length} model bones, {linkBodies.Length} rigid bodies, {linkConstraints} pose-retargeted constraints, directMapperOffsets={directMapperOffsets}; forwardLegMapperOffsets={legMapperOffsets}; reverseLegMapperOffsets={reverseLegMapperOffsets}; twoWayLegMapperOffsets={twoWayLegMapperOffsets}; reverseLegMapperOutputLocalPivots={reverseLegMapperOutputLocalPivots}; twoWayLegMapperOutputLocalPivots={twoWayLegMapperOutputLocalPivots}; rebuiltLegMapperOffsets={rebuiltLegMapperOffsets}; rebuiltCapsuleCentersOfMass={rebuiltCapsuleCentersOfMass}; anchorOnly={anchorOnly}.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: HKRG Mipha test export: " + exception);
            return 1;
        }
    }
    private static int AuditHkrgConstraintRepair(string hkrgPath)
    {
        if (string.IsNullOrWhiteSpace(hkrgPath) || !File.Exists(hkrgPath))
        {
            Console.Error.WriteLine("Usage: PhysicsTool.RegressionTests --audit-hkrg-constraint-repair --hkrg <file.hkrg>");
            return 2;
        }

        var outputPath = Path.Combine(Path.GetTempPath(), "PhysicsTool-HKRG-Repair-" + Guid.NewGuid().ToString("N") + ".hkrg");
        try
        {
            var source = new HkclService();
            source.Load(hkrgPath);
            Require(source.IsHkrg, "Input did not load as an HKRG document.");
            var sourceRigidBodies = source.GetColliderRows(0).Count;
            var sourceConstraints = source.GetHkrgConstraintRows().Count;
            Require(sourceRigidBodies > 0, "Mipha HKRG has no rigid bodies.");
            Require(sourceConstraints > 0, "Mipha HKRG has no ragdoll constraints.");

            source.RepairHkrgConstraintFramesForCurrentPose();
            source.SaveHkcl(outputPath, HkclService.DetectHkclPlatform(hkrgPath));

            var reopened = new HkclService();
            reopened.Load(outputPath);
            Require(reopened.IsHkrg, "Repaired output did not reopen as HKRG.");
            Require(reopened.GetColliderRows(0).Count == sourceRigidBodies, "Rigid body count changed after repair save.");
            Require(reopened.GetHkrgConstraintRows().Count == sourceConstraints, "Constraint count changed after repair save.");
            Require(reopened.HkrgConstraintFramesAreFinite(), "Repaired HKRG constraint frames contain invalid values.");

            Console.WriteLine($"HKRG constraint repair passed for {Path.GetFileName(hkrgPath)}: {sourceRigidBodies} rigid bodies, {sourceConstraints} constraints.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: HKRG constraint repair: " + exception);
            return 1;
        }
        finally
        {
            if (string.Equals(Environment.GetEnvironmentVariable("PHYSICSTOOL_KEEP_HKRG_REPAIR"), "1", StringComparison.Ordinal))
                Console.WriteLine($"Kept repaired HKRG: {outputPath}");
            else if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
    private static IEnumerable<FilePair> FindFilePairs(string matchesPath)
    {
        var bphclByKey = Directory.EnumerateFiles(matchesPath, "*.bphcl", SearchOption.TopDirectoryOnly)
            .GroupBy(path => NormalizeFileStem(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var hkclPath in Directory.EnumerateFiles(matchesPath, "*.hkcl", SearchOption.TopDirectoryOnly))
        {
            var key = NormalizeFileStem(hkclPath);
            if (!bphclByKey.TryGetValue(key, out var candidates) || candidates.Length != 1)
                continue;

            yield return new FilePair(hkclPath, candidates[0]);
        }
    }

    // A standalone writer needs one reproducible reflection schema, not a
    // hidden game-file donor. This reports whether the known BPHCL samples
    // agree on the semantic TYPE definitions that ITEM/PTCH records use.
    private static int AuditBphclSchemas(string matchesPath)
    {
        var files = Directory.EnumerateFiles(matchesPath, "*.bphcl", SearchOption.TopDirectoryOnly).ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine("No BPHCL files found for schema audit.");
            return 2;
        }

        var schemas = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var document = NativeBphclDocument.Open(file);
            var signature = BuildSchemaSignature(document);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
            if (!schemas.TryGetValue(key, out var names))
            {
                names = new List<string>();
                schemas[key] = names;
            }

            names.Add(Path.GetFileName(file));
        }

        Console.WriteLine($"BPHCL schema audit: {files.Length} files, {schemas.Count} semantic TYPE schema(s).");
        foreach (var schema in schemas.OrderByDescending(entry => entry.Value.Count))
        {
            Console.WriteLine($"{schema.Key[..12]}: {schema.Value.Count} file(s)");
            foreach (var name in schema.Value.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine("  " + name);
        }

        return 0;
    }

    // A standalone writer can ship one generated reflection catalog only if
    // every sample agrees on the layout for a given named Havok type. Full
    // TYPE sections naturally differ because each file uses a different
    // subset of types, so compare individual definitions rather than hashes
    // of the entire table.
    private static int AuditBphclTypeDefinitions(string matchesPath)
    {
        var files = Directory.EnumerateFiles(matchesPath, "*.bphcl", SearchOption.TopDirectoryOnly).ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine("No BPHCL files found for type-definition audit.");
            return 2;
        }

        var definitions = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var document = NativeBphclDocument.Open(file);
            foreach (var definition in document.TypeDefinitions.Where(definition => definition.TypeIndex != 0))
            {
                var name = document.GetTypeName(definition.TypeIndex) ?? $"type {definition.TypeIndex}";
                var signature = BuildTypeDefinitionSignature(document, definition);
                if (!definitions.TryGetValue(name, out var variants))
                {
                    variants = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    definitions[name] = variants;
                }

                if (!variants.TryGetValue(signature, out var owners))
                {
                    owners = new List<string>();
                    variants[signature] = owners;
                }

                owners.Add(Path.GetFileName(file));
            }
        }

        var conflicts = definitions
            .Where(entry => entry.Value.Count > 1)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine($"BPHCL type-definition audit: {files.Length} files, {definitions.Count} named types, {conflicts.Length} conflicting type layout(s).");
        foreach (var conflict in conflicts)
        {
            Console.WriteLine($"{conflict.Key}: {conflict.Value.Count} layouts");
            foreach (var layout in conflict.Value.OrderBy(entry => entry.Value[0], StringComparer.OrdinalIgnoreCase))
                Console.WriteLine("  " + string.Join(", ", layout.Value.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)));
        }

        return conflicts.Length == 0 ? 0 : 1;
    }

    // This is the first donor-free HKCL -> BPHCL gate. It deliberately does
    // not write binary yet: prove that the serializer input contains every
    // cloth-local graph edge before assigning ITEM records and PTCH fixups.
    private static int AuditStandaloneGraphExtraction(string matchesPath)
    {
        var files = Directory.EnumerateFiles(matchesPath, "*.hkcl", SearchOption.TopDirectoryOnly).ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine("No HKCL files found for standalone-graph audit.");
            return 2;
        }

        var failures = new List<string>();
        var clothCount = 0;
        foreach (var file in files)
        {
            try
            {
                var source = new HkclService();
                source.Load(file);
                var graph = HkclToBphclStandaloneGraphBuilder.Build(source);
                clothCount += graph.Cloths.Count;
                Console.WriteLine($"PASS {Path.GetFileName(file)}: {graph.Cloths.Count} cloth(s), " +
                    $"{graph.Cloths.Sum(cloth => cloth.Particles.Count)} particles, " +
                    $"{graph.Cloths.Sum(cloth => cloth.Constraints.Count)} links, " +
                    $"{graph.Cloths.Sum(cloth => cloth.Colliders.Count)} referenced colliders.");
            }
            catch (Exception exception)
            {
                failures.Add($"{Path.GetFileName(file)}: {exception.Message}");
            }
        }

        Console.WriteLine($"Standalone graph audit: {files.Length} HKCL file(s), {clothCount} cloth(s), {failures.Count} failure(s).");
        foreach (var failure in failures)
            Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static int AuditStandaloneEnvelope()
    {
        try
        {
            var bytes = NativeBphclStandaloneEnvelopeWriter.CreateEmptyForValidation();
            var document = NativeBphclDocument.Parse(bytes);
            Require(document.Cloths.Count == 0, "Standalone BPHCL envelope unexpectedly contains cloth data.");
            Require(document.Skeletons.Count == 0, "Standalone BPHCL envelope unexpectedly contains skeleton data.");
            Require(document.Items.Count == 0, "Standalone BPHCL envelope unexpectedly contains ITEM records.");
            Require(document.InternalPatches.Count == 0, "Standalone BPHCL envelope unexpectedly contains PTCH records.");
            Console.WriteLine($"Standalone BPHCL envelope round-trip passed ({bytes.Length} bytes).");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: standalone BPHCL envelope: " + exception);
            return 1;
        }
    }

    // The first native-write gate: the output is intentionally limited to
    // skeleton/particle/triangle/collider graph data, but it must still be a
    // completely fresh BPHCL that our native reader can reopen and inspect.
    private static int AuditStandaloneWriter(string matchesPath, SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        if (string.IsNullOrWhiteSpace(matchesPath) || !Directory.Exists(matchesPath))
        {
            Console.Error.WriteLine("Provide an existing --root folder for the standalone writer audit.");
            return 2;
        }

        var files = Directory.EnumerateFiles(matchesPath, "*.hkcl", searchOption).ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine("No HKCL files found for standalone-write audit.");
            return 2;
        }

        var failures = new List<string>();
        var clothCount = 0;
        var skipped = 0;
        foreach (var file in files)
        {
            try
            {
                var source = new HkclService();
                source.Load(file);
                var graph = HkclToBphclStandaloneGraphBuilder.Build(source);
                var bytes = NativeBphclStandaloneGraphWriter.Write(graph);
                var reopened = NativeBphclDocument.Parse(bytes);
                VerifyStandaloneCore(graph, reopened, Path.GetFileName(file));
                clothCount += graph.Cloths.Count;
                Console.WriteLine($"PASS {Path.GetFileName(file)}: {graph.Cloths.Count} cloth(s), {bytes.Length} bytes.");
            }
            catch (Exception exception)
            {
                if (exception.Message.Contains("does not contain any cloth units", StringComparison.Ordinal))
                {
                    skipped++;
                    Console.WriteLine($"SKIP {Path.GetFileName(file)}: no cloth units.");
                    continue;
                }
                failures.Add($"{Path.GetFileName(file)}: {exception.Message}");
            }
        }

        Console.WriteLine($"Standalone writer audit: {files.Length} HKCL file(s), {clothCount} cloth(s), {skipped} skipped, {failures.Count} failure(s).");
        foreach (var failure in failures)
            Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static int AuditStandaloneConstraints(string matchesPath)
    {
        var constraints = new Dictionary<string, int>(StringComparer.Ordinal);
        var localRanges = 0;
        var transitions = 0;
        foreach (var file in Directory.EnumerateFiles(matchesPath, "*.hkcl", SearchOption.TopDirectoryOnly))
        {
            var source = new HkclService();
            source.Load(file);
            var graph = HkclToBphclStandaloneGraphBuilder.Build(source);
            foreach (var cloth in graph.Cloths)
            {
                foreach (var constraintSet in cloth.ConstraintSets)
                    constraints[constraintSet.TypeName] = constraints.GetValueOrDefault(constraintSet.TypeName);
                foreach (var constraint in cloth.Constraints)
                    constraints[constraint.TypeName] = constraints.GetValueOrDefault(constraint.TypeName) + 1;
                localRanges += cloth.LocalRanges.Count;
                transitions += cloth.TransitionSets.Sum(set => set.Particles.Count);
            }
        }

        Console.WriteLine("Standalone HKCL constraint classes:");
        foreach (var constraint in constraints.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {constraint.Key}: {constraint.Value}");
        Console.WriteLine($"  hclLocalRangeConstraintSet: {localRanges}");
        Console.WriteLine($"  hclTransitionConstraintSet: {transitions}");
        return constraints.Count == 0 ? 1 : 0;
    }

    private static int AuditStandaloneOperators(string matchesPath)
    {
        var operators = new Dictionary<string, int>(StringComparer.Ordinal);
        var skinRoutes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(matchesPath, "*.hkcl", SearchOption.TopDirectoryOnly))
        {
            var source = new HkclService();
            source.Load(file);
            for (var clothIndex = 0; clothIndex < source.GetClothSummaries().Count; clothIndex++)
            {
                foreach (var @operator in source.GetOperatorClassNames(clothIndex))
                    operators[@operator] = operators.GetValueOrDefault(@operator) + 1;
                foreach (var skin in source.GetSkinOperatorData(clothIndex))
                {
                    var route = $"{skin.ClassName}: buffer {skin.OutputBufferIndex}, transforms {skin.TransformSetIndex}";
                    skinRoutes[route] = skinRoutes.GetValueOrDefault(route) + 1;
                }
            }
        }

        Console.WriteLine("Standalone HKCL runtime operator classes:");
        foreach (var @operator in operators.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {@operator.Key}: {@operator.Value}");
        Console.WriteLine("Standalone HKCL skin routes:");
        foreach (var route in skinRoutes.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {route.Key}: {route.Value}");
        return operators.Count == 0 ? 1 : 0;
    }

    private static int AuditBphclSkinTypes(string matchesPath)
    {
        var prefixes = new[] { "hclObjectSpaceDeformer::", "hclBoneSpaceDeformer::" };
        var summaries = new Dictionary<string, Dictionary<uint, List<string>>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(matchesPath, "*.bphcl", SearchOption.TopDirectoryOnly))
        {
            var document = NativeBphclDocument.Open(file);
            foreach (var definition in document.TypeDefinitions)
            {
                var name = document.GetTypeName(definition.TypeIndex) ?? string.Empty;
                if (!prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                    continue;
                var sizes = summaries.GetValueOrDefault(name);
                if (sizes is null)
                {
                    sizes = new Dictionary<uint, List<string>>();
                    summaries.Add(name, sizes);
                }
                var size = definition.Size ?? 0u;
                var owners = sizes.GetValueOrDefault(size);
                if (owners is null)
                {
                    owners = new List<string>();
                    sizes.Add(size, owners);
                }
                owners.Add(Path.GetFileName(file));
            }
        }

        Console.WriteLine("Observed BPHCL skin block sizes:");
        foreach (var summary in summaries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {summary.Key}: {string.Join(", ", summary.Value.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key} bytes [{string.Join(", ", entry.Value)}]"))}");
        return summaries.Count == 0 ? 1 : 0;
    }

    private static int AuditStandaloneLayout()
    {
        foreach (var (typeName, members) in new[]
                 {
                     ("hclObjectSpaceSkinPOperator", new[] { "boneFromSkinMeshTransforms", "transformSubset", "outputBufferIndex", "transformSetIndex", "objectSpaceDeformer", "localPs", "localUnpackedPs" }),
                     ("hclBoneSpaceSkinPOperator", new[] { "transformSubset", "outputBufferIndex", "transformSetIndex", "boneSpaceDeformer", "localPs", "localUnpackedPs" })
                 })
        {
            Console.WriteLine(typeName + " inherited offsets:");
            foreach (var member in members)
                Console.WriteLine($"  {member}: 0x{NativeBphclReflectionCatalog.GetMemberOffset(typeName, member):X}");
        }
        foreach (var typeName in new[]
                 {
                     "hclSimClothData",
                     "hclClothData",
                     "hclTransformSetDefinition",
                     "hclBufferDefinition",
                     "hclBufferLayout",
                     "hclScratchBufferDefinition",
                     "hclClothState",
                     "hclTransformSetUsage",
                     "hclTransformSetUsage::TransformTracker",
                     "hkBitField",
                     "hclMoveParticlesOperator",
                     "hclMoveParticlesOperator::VertexParticlePair",
                     "hclSimulateOperator",
                     "hclSimulateOperator::Config",
                     "hclBoneSpaceSkinPOperator",
                     "hclObjectSpaceSkinPOperator",
                     "hclObjectSpaceDeformer",
                     "hclObjectSpaceDeformer::EightBlendEntryBlock",
                     "hclObjectSpaceDeformer::SevenBlendEntryBlock",
                     "hclObjectSpaceDeformer::SixBlendEntryBlock",
                     "hclObjectSpaceDeformer::FiveBlendEntryBlock",
                     "hclObjectSpaceDeformer::FourBlendEntryBlock",
                     "hclObjectSpaceDeformer::ThreeBlendEntryBlock",
                     "hclObjectSpaceDeformer::TwoBlendEntryBlock",
                     "hclObjectSpaceDeformer::OneBlendEntryBlock",
                     "hclObjectSpaceDeformer::LocalBlockP",
                     "hclObjectSpaceDeformer::LocalBlockUnpackedP",
                     "hclBoneSpaceDeformer",
                     "hclBoneSpaceDeformer::FourBlendEntryBlock",
                     "hclBoneSpaceDeformer::ThreeBlendEntryBlock",
                     "hclBoneSpaceDeformer::TwoBlendEntryBlock",
                     "hclBoneSpaceDeformer::OneBlendEntryBlock",
                     "hclBoneSpaceDeformer::LocalBlockP",
                     "hclBoneSpaceDeformer::LocalBlockUnpackedP",
                     "hclGatherAllVerticesOperator",
                     "hclCopyVerticesOperator",
                     "hclSimpleMeshBoneDeformOperator",
                     "hclSimpleMeshBoneDeformOperator::TriangleBonePair",
                     "hclStandardLinkConstraintSet",
                     "hclStretchLinkConstraintSet",
                     "hclBendLinkConstraintSet",
                     "hclBendStiffnessConstraintSet",
                     "hclLocalRangeConstraintSet",
                     "hclTransitionConstraintSet",
                     "hclTransitionConstraintSet::PerParticle",
                     "hclBonePlanesConstraintSet",
                     "hclBonePlanesConstraintSet::BonePlane"
                 })
        {
            try
            {
                var layout = NativeBphclReflectionCatalog.GetTypeLayout(typeName);
                Console.WriteLine($"{layout.Name} ({layout.TypeIndex}, {layout.Size} bytes)");
                foreach (var member in layout.Members)
                    Console.WriteLine($"  +0x{member.Offset:X}: {member.Name} ({member.TypeName})");
            }
            catch (InvalidDataException)
            {
                Console.WriteLine($"{typeName} ({NativeBphclReflectionCatalog.GetTypeIndex(typeName)}, named helper with no standalone body)");
            }
        }
        return 0;
    }

    private static int AuditHkx2BonePlanes()
    {
        var types = new[]
        {
            typeof(HKX2.hclBonePlanesConstraintSet),
            typeof(HKX2.hclBonePlanesConstraintSetBonePlane)
        };
        foreach (var type in types)
        {
            Console.WriteLine(type.FullName);
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                Console.WriteLine($"  {field.FieldType.FullName} {field.Name}");
        }
        return 0;
    }

    // Development-only helper. It extends the checked-in static TYPE resource
    // from a known vanilla schema; released conversion never opens this file.
    private static int CompileNativeTypeCatalog(string? sourcePath, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            Console.Error.WriteLine("Provide --bphcl <vanilla BPHCL containing the required type>.");
            return 2;
        }
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Provide --output <TYPE resource path>.");
            return 2;
        }

        var source = NativeBphclDocument.Open(sourcePath);
        var required = source.TypeNames
            .Select((name, index) => (name, index))
            .Where(entry => entry.index > 0 && string.Equals(entry.name, "hclBonePlanesConstraintSet", StringComparison.Ordinal))
            .Select(entry => checked((uint)entry.index))
            .ToArray();
        if (required.Length == 0)
        {
            Console.Error.WriteLine("The supplied BPHCL TYPE table does not contain hclBonePlanesConstraintSet.");
            return 2;
        }

        var baseDocument = NativeBphclDocument.Parse(NativeBphclStandaloneEnvelopeWriter.Create(
            new byte[16],
            NativeBphclReflectionCatalog.TypeSection,
            Array.Empty<NativeBphclItem>(),
            Array.Empty<NativeBphclPatch>()));
        var merged = NativeBphclTypeTable.Create(baseDocument, source, required);
        var compiled = merged.ReplacementSection ?? NativeBphclReflectionCatalog.TypeSection;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, compiled);

        var verify = NativeBphclDocument.Parse(NativeBphclStandaloneEnvelopeWriter.Create(
            new byte[16],
            compiled,
            Array.Empty<NativeBphclItem>(),
            Array.Empty<NativeBphclPatch>()));
        if (!verify.TypeNames.Any(name => string.Equals(name, "hclBonePlanesConstraintSet", StringComparison.Ordinal)))
            throw new InvalidDataException("Compiled TYPE catalog did not retain hclBonePlanesConstraintSet.");

        Console.WriteLine($"Compiled static TYPE catalog: {compiled.Length} bytes -> {outputPath}");
        return 0;
    }

    private static int AuditNativePipeline(string matchesPath, string? explicitFile)
    {
        var files = string.IsNullOrWhiteSpace(explicitFile)
            ? Directory.EnumerateFiles(matchesPath, "*.bphcl", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : File.Exists(explicitFile)
                ? new[] { explicitFile }
                : Array.Empty<string>();
        if (files.Length == 0)
        {
            Console.Error.WriteLine("No BPHCL files found for runtime-pipeline audit.");
            return 2;
        }

        foreach (var file in files)
        {
            var document = NativeBphclDocument.Open(file);
            Console.WriteLine(Path.GetFileName(file));
            foreach (var cloth in document.Cloths)
            {
                Console.WriteLine(
                    $"  {cloth.Name}: sim={cloth.SimCloths.Count}, buffers=[{string.Join(", ", cloth.BufferDefinitions.Select(buffer => $"{buffer.ClassName}:type{buffer.Type}/sub{buffer.SubType}:{buffer.VertexCount}/{buffer.TriangleCount}"))}], " +
                    $"transformSets=[{string.Join(", ", cloth.TransformSetDefinitions.Select(set => $"{set.Name}:{set.Type}/{set.TransformCount}"))}], " +
                    $"operators=[{string.Join(", ", cloth.Operators.Select(op => op.ClassName))}], states={cloth.States.Count}, " +
                    $"simpleBoneDeformers={cloth.SimpleMeshBoneDeformers.Count}");
                foreach (var simulation in cloth.SimCloths)
                    Console.WriteLine($"    sim#{simulation.Index}: opIds=[{string.Join(",", simulation.SimulationOperatorIds)}], constraints=[{string.Join(",", simulation.ConstraintSets.Select(set => $"{set.Index}:{set.ClassName}"))}]");
                foreach (var op in cloth.Operators)
                {
                    var config = op.SimulateConfigs.FirstOrDefault();
                    Console.WriteLine(
                        $"    op#{op.Index} {op.ClassName}: in={op.InputBufferIndex}, out={op.OutputBufferIndex}, transforms={op.TransformSetIndex}, " +
                        $"sim={op.SimulationClothIndex}, ref={op.ReferenceBufferIndex}, pairs={op.VertexParticlePairs.Count}, " +
                        $"constraintExec=[{string.Join(",", config?.ConstraintExecution ?? Array.Empty<int>())}], " +
                        $"colliders=[{string.Join(",", config?.InstanceCollidablesUsed ?? Array.Empty<int>())}], " +
                        $"allColliders={config?.UseAllInstanceCollidables}, substeps={config?.SubSteps}, " +
                        $"iterations={config?.NumberOfSolveIterations}, adapt={config?.AdaptConstraintStiffness}");
                }
                foreach (var state in cloth.States)
                {
                    Console.WriteLine($"    state#{state.Index} {state.Name}: operators=[{string.Join(",", state.OperatorItemIndices)}], sims=[{string.Join(",", state.SimClothItemIndices)}]");
                    foreach (var access in state.TransformSetAccesses)
                    {
                        var trackers = string.Join("; ", access.TransformTrackers.Select((tracker, index) =>
                            $"c{index}:r={FormatBitField(tracker.Read)},rbw={FormatBitField(tracker.ReadBeforeWrite)},w={FormatBitField(tracker.Written)}"));
                        Console.WriteLine($"      transforms#{access.TransformSetIndex}: flags={access.ComponentFlags0}/{access.ComponentFlags1}, trackers=[{trackers}]");
                    }
                }
                foreach (var deformer in cloth.SimpleMeshBoneDeformers)
                    Console.WriteLine($"    boneDeform: input={deformer.InputBufferIndex}, transforms={deformer.OutputTransformSetIndex}, pairs={deformer.TriangleBonePairs.Count}, locals={deformer.LocalBoneTransforms.Count}, axis={deformer.BoneAxis}");
            }
        }
        return 0;
    }

    private static string FormatBitField(NativeBphclBitField bitField) =>
        $"{bitField.NumBits}[{string.Join(",", bitField.Words)}]";

    private static void VerifyStandaloneCore(StandaloneBphclDocumentGraph expected, NativeBphclDocument actual, string context)
    {
        Require(actual.Cloths.Count == expected.Cloths.Count, context + ": standalone cloth count changed after reopen.");
        Require(actual.Skeletons.Count == expected.Cloths.Count, context + ": standalone skeleton count changed after reopen.");
        var expectedColliderCount = expected.Cloths.Sum(cloth => cloth.Colliders.Count);
        Require(actual.Colliders.Count == expectedColliderCount, context + ": standalone collider count changed after reopen.");
        Require(actual.IsAampReadable, context + ": standalone AAMP cannot be read after reopen.");
        Require(
            actual.AampClothEntryNames.SequenceEqual(expected.Cloths.Select(cloth => cloth.Name)),
            context + ": standalone AAMP cloth registration changed after reopen.");

        for (var clothIndex = 0; clothIndex < expected.Cloths.Count; clothIndex++)
        {
            var input = expected.Cloths[clothIndex];
            var output = actual.Cloths[clothIndex];
            Require(string.Equals(input.Name, output.Name, StringComparison.Ordinal), context + $": cloth {clothIndex} name changed after reopen.");
            Require(output.SimCloths.Count == 1, context + $": cloth {clothIndex} should have one simulation.");
            Require(output.SimCloths[0].Particles.Count == input.Particles.Count, context + $": cloth {clothIndex} particle count changed after reopen.");
            Require(output.SimCloths[0].TriangleCount == input.Triangles.Count, context + $": cloth {clothIndex} triangle count changed after reopen.");
            Require(actual.Skeletons[clothIndex].BoneCount == input.Bones.Count, context + $": cloth {clothIndex} skeleton bone count changed after reopen.");
            var expectedBuffers = input.Runtime.Buffers;
            Require(output.BufferDefinitions.Count == expectedBuffers.Count, context + $": cloth {clothIndex} runtime buffer count changed after reopen.");
            for (var bufferIndex = 0; bufferIndex < expectedBuffers.Count; bufferIndex++)
            {
                var expectedBuffer = expectedBuffers[bufferIndex];
                var actualBuffer = output.BufferDefinitions[bufferIndex];
                Require(string.Equals(actualBuffer.ClassName, expectedBuffer.ClassName, StringComparison.Ordinal), context + $": cloth {clothIndex} buffer {bufferIndex} class changed after reopen.");
                Require(string.Equals(actualBuffer.MeshName, expectedBuffer.MeshName, StringComparison.Ordinal), context + $": cloth {clothIndex} buffer {bufferIndex} mesh name changed after reopen.");
                Require(string.Equals(actualBuffer.BufferName, expectedBuffer.BufferName, StringComparison.Ordinal), context + $": cloth {clothIndex} buffer {bufferIndex} name changed after reopen.");
                Require(actualBuffer.Type == expectedBuffer.Type && actualBuffer.SubType == expectedBuffer.SubType, context + $": cloth {clothIndex} buffer {bufferIndex} type changed after reopen.");
                Require(actualBuffer.VertexCount == expectedBuffer.VertexCount && actualBuffer.TriangleCount == expectedBuffer.TriangleCount, context + $": cloth {clothIndex} buffer {bufferIndex} topology changed after reopen.");
            }
            var expectedOperators = input.Runtime.Operators;
            Require(output.Operators.Count == expectedOperators.Count, context + $": cloth {clothIndex} runtime operator count changed after reopen.");
            for (var operatorIndex = 0; operatorIndex < expectedOperators.Count; operatorIndex++)
            {
                var expectedOperator = expectedOperators[operatorIndex];
                var actualOperator = output.Operators[operatorIndex];
                Require(string.Equals(actualOperator.ClassName, expectedOperator.ClassName, StringComparison.Ordinal), context + $": cloth {clothIndex} operator {operatorIndex} class changed after reopen.");
                Require(actualOperator.InputBufferIndex == expectedOperator.InputBufferIndex, context + $": cloth {clothIndex} operator {operatorIndex} input buffer changed after reopen.");
                Require(actualOperator.OutputBufferIndex == expectedOperator.OutputBufferIndex, context + $": cloth {clothIndex} operator {operatorIndex} output buffer changed after reopen.");
                Require(actualOperator.TransformSetIndex == expectedOperator.TransformSetIndex, context + $": cloth {clothIndex} operator {operatorIndex} transform set changed after reopen.");
                Require(actualOperator.VertexCount == expectedOperator.VertexCount, context + $": cloth {clothIndex} operator {operatorIndex} vertex count changed after reopen.");
                Require(actualOperator.StartVertexIn == expectedOperator.StartVertexIn && actualOperator.StartVertexOut == expectedOperator.StartVertexOut, context + $": cloth {clothIndex} operator {operatorIndex} copy range changed after reopen.");
                Require(actualOperator.VertexInputFromVertexOutput.SequenceEqual(expectedOperator.VertexInputFromVertexOutput), context + $": cloth {clothIndex} operator {operatorIndex} gather map changed after reopen.");
                Require(actualOperator.VertexParticlePairs.Select(pair => new ClothVertexParticlePairData(pair.VertexIndex, pair.ParticleIndex)).SequenceEqual(expectedOperator.VertexParticlePairs), context + $": cloth {clothIndex} operator {operatorIndex} particle pairs changed after reopen.");
                Require(actualOperator.SimulationClothIndex == expectedOperator.SimulationClothIndex && actualOperator.ReferenceBufferIndex == expectedOperator.ReferenceBufferIndex, context + $": cloth {clothIndex} operator {operatorIndex} simulation route changed after reopen.");
                Require(actualOperator.SimulateConfigs.Count == expectedOperator.SimulateConfigs.Count, context + $": cloth {clothIndex} operator {operatorIndex} simulate config count changed after reopen.");
                for (var configIndex = 0; configIndex < expectedOperator.SimulateConfigs.Count; configIndex++)
                {
                    var expectedConfig = expectedOperator.SimulateConfigs[configIndex];
                    var actualConfig = actualOperator.SimulateConfigs[configIndex];
                    Require(actualConfig.ConstraintExecution.SequenceEqual(expectedConfig.ConstraintExecution), context + $": cloth {clothIndex} operator {operatorIndex} config {configIndex} execution changed after reopen.");
                    Require(actualConfig.InstanceCollidablesUsed.SequenceEqual(expectedConfig.InstanceCollidablesUsed), context + $": cloth {clothIndex} operator {operatorIndex} config {configIndex} collider selection changed after reopen.");
                    Require(actualConfig.SubSteps == expectedConfig.SubSteps && actualConfig.NumberOfSolveIterations == expectedConfig.NumberOfSolveIterations, context + $": cloth {clothIndex} operator {operatorIndex} config {configIndex} solver settings changed after reopen.");
                    Require(actualConfig.UseAllInstanceCollidables == expectedConfig.UseAllInstanceCollidables && actualConfig.AdaptConstraintStiffness == expectedConfig.AdaptConstraintStiffness, context + $": cloth {clothIndex} operator {operatorIndex} config {configIndex} flags changed after reopen.");
                }
            }
            var expectedStates = input.Runtime.States;
            Require(output.States.Count == expectedStates.Count, context + $": cloth {clothIndex} runtime state count changed after reopen.");
            for (var stateIndex = 0; stateIndex < expectedStates.Count; stateIndex++)
            {
                var expectedState = expectedStates[stateIndex];
                var actualState = output.States[stateIndex];
                Require(string.Equals(actualState.Name, expectedState.Name, StringComparison.Ordinal), context + $": cloth {clothIndex} state {stateIndex} name changed after reopen.");
                Require(actualState.OperatorItemIndices.SequenceEqual(expectedState.OperatorIndices), context + $": cloth {clothIndex} state {stateIndex} operator route changed after reopen.");
                Require(actualState.BufferAccesses.Select(access => checked((int)access.BufferIndex)).SequenceEqual(expectedState.BufferIndices), context + $": cloth {clothIndex} state {stateIndex} buffer route changed after reopen.");
                Require(actualState.TransformSetAccesses.Select(access => checked((int)access.TransformSetIndex)).SequenceEqual(expectedState.TransformSetIndices), context + $": cloth {clothIndex} state {stateIndex} transform-set route changed after reopen.");
                Require(actualState.SimClothItemIndices.SequenceEqual(expectedState.SimulationIndices), context + $": cloth {clothIndex} state {stateIndex} simulation route changed after reopen.");
            }
            var expectedObjectSkins = input.SkinOperators.Count(skin => string.Equals(skin.ClassName, "hclObjectSpaceSkinPOperator", StringComparison.Ordinal));
            var expectedBoneSkins = input.SkinOperators.Count(skin => string.Equals(skin.ClassName, "hclBoneSpaceSkinPOperator", StringComparison.Ordinal));
            Require(output.ObjectSpaceSkins.Count == expectedObjectSkins, context + $": cloth {clothIndex} object-space skin count changed after reopen.");
            Require(output.BoneSpaceSkins.Count == expectedBoneSkins, context + $": cloth {clothIndex} bone-space skin count changed after reopen.");
            foreach (var expectedSkin in input.SkinOperators)
            {
                if (string.Equals(expectedSkin.ClassName, "hclObjectSpaceSkinPOperator", StringComparison.Ordinal))
                {
                    var actualSkin = output.ObjectSpaceSkins.Single();
                    Require(actualSkin.OutputBufferIndex == expectedSkin.OutputBufferIndex, context + $": cloth {clothIndex} object-space skin output buffer changed after reopen.");
                    Require(actualSkin.TransformSetIndex == expectedSkin.TransformSetIndex, context + $": cloth {clothIndex} object-space skin transform set changed after reopen.");
                    Require(actualSkin.BoneFromSkinMeshTransforms.Count == expectedSkin.BoneFromSkinMeshTransforms.Count, context + $": cloth {clothIndex} object-space skin matrix count changed after reopen.");
                    Require(actualSkin.TransformSubset.Count == expectedSkin.TransformSubset.Count, context + $": cloth {clothIndex} object-space skin subset count changed after reopen.");
                }
                else if (string.Equals(expectedSkin.ClassName, "hclBoneSpaceSkinPOperator", StringComparison.Ordinal))
                {
                    var actualSkin = output.BoneSpaceSkins.Single();
                    Require(actualSkin.OutputBufferIndex == expectedSkin.OutputBufferIndex, context + $": cloth {clothIndex} bone-space skin output buffer changed after reopen (expected {expectedSkin.OutputBufferIndex}, found {actualSkin.OutputBufferIndex}).");
                    Require(actualSkin.TransformSetIndex == expectedSkin.TransformSetIndex, context + $": cloth {clothIndex} bone-space skin transform set changed after reopen (expected {expectedSkin.TransformSetIndex}, found {actualSkin.TransformSetIndex}).");
                    Require(actualSkin.TransformSubset.Count == expectedSkin.TransformSubset.Count, context + $": cloth {clothIndex} bone-space skin subset count changed after reopen.");
                }
            }
            Require(output.SimpleMeshBoneDeformers.Count == input.BoneDeformers.Count, context + $": cloth {clothIndex} bone-deform stage count changed after reopen.");
            for (var deformerIndex = 0; deformerIndex < input.BoneDeformers.Count; deformerIndex++)
            {
                var expectedDeformer = input.BoneDeformers[deformerIndex];
                var actualDeformer = output.SimpleMeshBoneDeformers[deformerIndex];
                Require(actualDeformer.InputBufferIndex == expectedDeformer.InputBufferIndex, context + $": cloth {clothIndex} bone deformer {deformerIndex} input buffer changed after reopen.");
                Require(actualDeformer.OutputTransformSetIndex == expectedDeformer.OutputTransformSetIndex, context + $": cloth {clothIndex} bone deformer {deformerIndex} output transform set changed after reopen.");
                Require(actualDeformer.BoneAxis == expectedDeformer.BoneAxis, context + $": cloth {clothIndex} bone deformer {deformerIndex} axis changed after reopen.");
                Require(actualDeformer.TriangleBonePairs.Count == expectedDeformer.TriangleBonePairs.Count, context + $": cloth {clothIndex} bone deformer {deformerIndex} pair count changed after reopen.");
                Require(actualDeformer.LocalBoneTransforms.Count == expectedDeformer.LocalBoneTransforms.Count, context + $": cloth {clothIndex} bone deformer {deformerIndex} local-transform count changed after reopen.");
            }
            var expectedTransformSet = input.Runtime.TransformSets.SingleOrDefault();
            Require(output.TransformSetDefinitions.Count == 1, context + $": cloth {clothIndex} transform-set count changed after reopen.");
            Require(
                string.Equals(output.TransformSetDefinitions[0].Name, expectedTransformSet?.Name ?? input.SkeletonName, StringComparison.Ordinal),
                context + $": cloth {clothIndex} transform-set name changed after reopen.");
            Require(
                output.TransformSetDefinitions[0].Type == (expectedTransformSet?.Type ?? 1) &&
                output.TransformSetDefinitions[0].TransformCount == (expectedTransformSet?.TransformCount ?? input.Bones.Count),
                context + $": cloth {clothIndex} transform-set layout changed after reopen.");
            Require(
                output.SimCloths[0].ConstraintSets
                    .Where(set => !string.Equals(set.ClassName, "hclBonePlanesConstraintSet", StringComparison.Ordinal))
                    .Sum(set => set.Links.Count) == input.Constraints.Count,
                context + $": cloth {clothIndex} link-constraint count changed after reopen.");
            Require(
                output.SimCloths[0].ConstraintSets.Sum(set => set.LocalConstraints.Count) == input.LocalRanges.Count,
                context + $": cloth {clothIndex} local-range count changed after reopen.");
            Require(
                output.SimCloths[0].ConstraintSets.Sum(set => set.TransitionParticles.Count) == input.TransitionSets.Sum(set => set.Particles.Count),
                context + $": cloth {clothIndex} transition-constraint count changed after reopen.");
            var expectedBonePlaneSets = input.BonePlaneSets.ToDictionary(set => set.ConstraintSetIndex);
            foreach (var actualSet in output.SimCloths[0].ConstraintSets.Where(set =>
                         string.Equals(set.ClassName, "hclBonePlanesConstraintSet", StringComparison.Ordinal)))
            {
                if (!expectedBonePlaneSets.TryGetValue(actualSet.Index, out var expectedSet))
                    throw new InvalidOperationException(context + $": cloth {clothIndex} gained an unexpected bone-plane set.");
                Require(
                    Math.Abs(actualSet.Values.GetValueOrDefault("transformSetIndex") - expectedSet.TransformSetIndex) < Epsilon,
                    context + $": cloth {clothIndex} bone-plane transform set changed after reopen.");
                Require(actualSet.Links.Count == expectedSet.Planes.Count, context + $": cloth {clothIndex} bone-plane count changed after reopen.");
                for (var planeIndex = 0; planeIndex < expectedSet.Planes.Count; planeIndex++)
                {
                    var expectedPlane = expectedSet.Planes[planeIndex];
                    var actualPlane = actualSet.Links[planeIndex];
                    RequireClose(expectedPlane.Stiffness, (float)actualPlane.Values.GetValueOrDefault("stiffness"), context + $": cloth {clothIndex} bone plane {planeIndex} stiffness");
                    Require(expectedPlane.ParticleIndex == (int)actualPlane.Values.GetValueOrDefault("particleIndex"), context + $": cloth {clothIndex} bone plane {planeIndex} particle changed after reopen.");
                    Require(expectedPlane.TransformIndex == (int)actualPlane.Values.GetValueOrDefault("transformIndex"), context + $": cloth {clothIndex} bone plane {planeIndex} transform changed after reopen.");
                    var actualEquation = actualPlane.PlaneEquationBone;
                    Require(actualEquation.HasValue, context + $": cloth {clothIndex} bone plane {planeIndex} equation was not read after reopen.");
                    var equation = actualEquation.GetValueOrDefault();
                    RequireClose(expectedPlane.PlaneEquationBone.X, equation.X, context + $": cloth {clothIndex} bone plane {planeIndex} equation X");
                    RequireClose(expectedPlane.PlaneEquationBone.Y, equation.Y, context + $": cloth {clothIndex} bone plane {planeIndex} equation Y");
                    RequireClose(expectedPlane.PlaneEquationBone.Z, equation.Z, context + $": cloth {clothIndex} bone plane {planeIndex} equation Z");
                    RequireClose(expectedPlane.PlaneEquationBone.W, equation.W, context + $": cloth {clothIndex} bone plane {planeIndex} equation W");
                }
            }
            Require(
                output.SimCloths[0].ConstraintSets.Count(set => string.Equals(set.ClassName, "hclBonePlanesConstraintSet", StringComparison.Ordinal)) == expectedBonePlaneSets.Count,
                context + $": cloth {clothIndex} bone-plane set count changed after reopen.");
            Require(
                output.SimCloths[0].StaticCollisionMasks.Count == input.Particles.Count,
                context + $": cloth {clothIndex} collision-mask count changed after reopen.");
            var colliderMap = output.SimCloths[0].CollidableTransformMap;
            Require(
                colliderMap.TransformIndices.Count == input.Colliders.Count,
                context + $": cloth {clothIndex} collider transform-index count changed after reopen.");
            Require(
                colliderMap.Offsets.Count == input.Colliders.Count,
                context + $": cloth {clothIndex} collider offset count changed after reopen.");

            var firstCollider = expected.Cloths.Take(clothIndex).Sum(cloth => cloth.Colliders.Count);
            for (var colliderIndex = 0; colliderIndex < input.Colliders.Count; colliderIndex++)
            {
                var expectedCollider = input.Colliders[colliderIndex];
                var actualCollider = actual.Colliders[firstCollider + colliderIndex];
                Require(
                    string.Equals(expectedCollider.Name, actualCollider.Name, StringComparison.Ordinal),
                    context + $": cloth {clothIndex} collider {colliderIndex} name changed after reopen.");
                Require(
                    colliderMap.TransformIndices[colliderIndex] == (uint)Math.Max(0, expectedCollider.BoneIndex),
                    context + $": cloth {clothIndex} collider {colliderIndex} transform binding changed after reopen.");
                RequireClose(expectedCollider.Start.X, actualCollider.Shape.Start.X, context + $": cloth {clothIndex} collider {colliderIndex} start X");
                RequireClose(expectedCollider.Start.Y, actualCollider.Shape.Start.Y, context + $": cloth {clothIndex} collider {colliderIndex} start Y");
                RequireClose(expectedCollider.Start.Z, actualCollider.Shape.Start.Z, context + $": cloth {clothIndex} collider {colliderIndex} start Z");
                RequireClose(expectedCollider.Radius, actualCollider.Shape.Radius, context + $": cloth {clothIndex} collider {colliderIndex} radius");
            }

            for (var particleIndex = 0; particleIndex < input.Particles.Count; particleIndex++)
            {
                var expectedParticle = input.Particles[particleIndex];
                var actualParticle = output.SimCloths[0].Particles[particleIndex];
                RequireClose(expectedParticle.Position.X, actualParticle.Position.X, context + $": cloth {clothIndex} particle {particleIndex} X");
                RequireClose(expectedParticle.Position.Y, actualParticle.Position.Y, context + $": cloth {clothIndex} particle {particleIndex} Y");
                RequireClose(expectedParticle.Position.Z, actualParticle.Position.Z, context + $": cloth {clothIndex} particle {particleIndex} Z");
                RequireClose(expectedParticle.Mass, actualParticle.Mass, context + $": cloth {clothIndex} particle {particleIndex} mass");
                RequireClose(expectedParticle.Radius, actualParticle.Radius, context + $": cloth {clothIndex} particle {particleIndex} radius");
                Require(expectedParticle.Fixed == actualParticle.Fixed, context + $": cloth {clothIndex} particle {particleIndex} fixed state changed after reopen.");
                Require(
                    expectedParticle.CollisionMask == output.SimCloths[0].StaticCollisionMasks[particleIndex],
                    context + $": cloth {clothIndex} particle {particleIndex} collision mask changed after reopen.");
            }
        }
    }

    private static string BuildSchemaSignature(NativeBphclDocument document)
    {
        var builder = new StringBuilder();
        foreach (var definition in document.TypeDefinitions.OrderBy(definition => definition.TypeIndex))
        {
            builder.Append(document.GetTypeName(definition.TypeIndex)).Append('|')
                .Append(document.GetTypeName(definition.ParentTypeIndex)).Append('|')
                .Append(definition.Flags).Append('|')
                .Append(definition.Size).Append('|')
                .Append(definition.Alignment).AppendLine();
            foreach (var member in definition.Members.OrderBy(member => member.Offset))
            {
                builder.Append("  ").Append(member.Name).Append('|')
                    .Append(member.Offset).Append('|')
                    .Append(document.GetTypeName(member.TypeIndex)).Append('|')
                    .Append(member.Flags).AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string BuildTypeDefinitionSignature(
        NativeBphclDocument document,
        NativeBphclTypeDefinition definition)
    {
        var builder = new StringBuilder();
        builder.Append(document.GetTypeName(definition.ParentTypeIndex)).Append('|')
            .Append(definition.Flags).Append('|')
            .Append(definition.Size).Append('|')
            .Append(definition.Alignment).AppendLine();
        foreach (var member in definition.Members.OrderBy(member => member.Offset))
        {
            builder.Append(member.Name).Append('|')
                .Append(member.Offset).Append('|')
                .Append(document.GetTypeName(member.TypeIndex)).Append('|')
                .Append(member.Flags).AppendLine();
        }
        return builder.ToString();
    }

    private static string NormalizeFileStem(string path) =>
        Path.GetFileNameWithoutExtension(path).Replace("_RaulSkin", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static void TestPair(FilePair pair, string outputDirectory, TestResult result)
    {
        result.FilePairs++;
        var originalHash = HashFile(pair.BphclPath);
        var source = new HkclService();
        var templateCatalog = new HkclService();
        source.Load(pair.HkclPath);
        templateCatalog.Load(pair.BphclPath);

        Console.WriteLine($"{Path.GetFileName(pair.HkclPath)} -> {Path.GetFileName(pair.BphclPath)}");
        foreach (var sourceIndex in Enumerable.Range(0, source.GetClothSummaries().Count))
        {
            var sourceName = source.GetClothName(sourceIndex);
            var templateIndex = FindClothByName(templateCatalog, sourceName);
            if (templateIndex < 0)
            {
                result.SkippedCloths++;
                Console.WriteLine($"  SKIP {sourceName}: no BPHCL cloth with the same name.");
                continue;
            }

            var preflight = HkclToBphclTemplateConversion.Check(source, sourceIndex, templateCatalog, templateIndex);
            if (!preflight.CanConvert)
            {
                result.SkippedCloths++;
                Console.WriteLine($"  SKIP {sourceName}: {string.Join("; ", preflight.Errors)}");
                continue;
            }

            result.MatchedCloths++;
            try
            {
                // Each cloth starts from a fresh BPHCL template so a previous
                // successful conversion cannot mask an ownership or indexing bug.
                var template = new HkclService();
                template.Load(pair.BphclPath);
                var outputPath = Path.Combine(
                    outputDirectory,
                    $"{NormalizeFileStem(pair.HkclPath)}_{sourceIndex}_{MakeSafeFileName(sourceName)}.bphcl");
                var converted = HkclToBphclTemplateConversion.Apply(source, sourceIndex, template, templateIndex);
                BphclBridge.Save(converted, outputPath);

                var saved = new HkclService();
                saved.Load(outputPath);
                VerifyTransferredValues(source, sourceIndex, saved, templateIndex, sourceName);
                result.ConvertedCloths++;
                Console.WriteLine($"  PASS {sourceName}");
            }
            catch (Exception exception)
            {
                result.Failures.Add($"{Path.GetFileName(pair.HkclPath)} / {sourceName}: {exception.Message}");
            }
        }

        if (!string.Equals(originalHash, HashFile(pair.BphclPath), StringComparison.Ordinal))
            result.Failures.Add($"{Path.GetFileName(pair.BphclPath)} was modified by the regression test.");
    }

    private static void VerifyTransferredValues(HkclService source, int sourceClothIndex, HkclService saved, int targetClothIndex, string clothName)
    {
        var sourceParticles = source.GetParticleRows(sourceClothIndex);
        var savedParticles = saved.GetParticleRows(targetClothIndex);
        Require(sourceParticles.Count == savedParticles.Count, clothName + ": particle count changed after save.");
        for (var index = 0; index < sourceParticles.Count; index++)
        {
            var expected = sourceParticles[index];
            var actual = savedParticles[index];
            RequireClose(expected.X, actual.X, $"{clothName}: particle {index} X");
            RequireClose(expected.Y, actual.Y, $"{clothName}: particle {index} Y");
            RequireClose(expected.Z, actual.Z, $"{clothName}: particle {index} Z");
            RequireClose(expected.W, actual.W, $"{clothName}: particle {index} W");
            RequireClose(expected.Mass, actual.Mass, $"{clothName}: particle {index} mass");
            RequireClose(expected.Radius, actual.Radius, $"{clothName}: particle {index} radius");
            RequireClose(expected.Friction, actual.Friction, $"{clothName}: particle {index} friction");
            RequireClose(actual.Mass == 0.0f ? 0.0f : 1.0f / actual.Mass, actual.InverseMass, $"{clothName}: particle {index} inverse mass");
        }

        var sourceBones = source.GetBoneRows(sourceClothIndex);
        var savedBones = saved.GetBoneRows(targetClothIndex);
        Require(sourceBones.Count == savedBones.Count, clothName + ": bone count changed after save.");
        for (var index = 0; index < sourceBones.Count; index++)
        {
            var expected = sourceBones[index];
            var actual = savedBones[index];
            RequireClose(expected.X, actual.X, $"{clothName}: bone {index} X");
            RequireClose(expected.Y, actual.Y, $"{clothName}: bone {index} Y");
            RequireClose(expected.Z, actual.Z, $"{clothName}: bone {index} Z");
            RequireClose(expected.RotationX, actual.RotationX, $"{clothName}: bone {index} rotation X");
            RequireClose(expected.RotationY, actual.RotationY, $"{clothName}: bone {index} rotation Y");
            RequireClose(expected.RotationZ, actual.RotationZ, $"{clothName}: bone {index} rotation Z");
            RequireClose(expected.RotationW, actual.RotationW, $"{clothName}: bone {index} rotation W");
        }

        var sourceColliders = source.GetColliderRows(sourceClothIndex);
        var savedColliders = saved.GetColliderRows(targetClothIndex);
        Require(sourceColliders.Count == savedColliders.Count, clothName + ": collider count changed after save.");
        for (var index = 0; index < sourceColliders.Count; index++)
        {
            var expected = sourceColliders[index];
            var actual = savedColliders[index];
            Require(string.Equals(expected.ShapeType, actual.ShapeType, StringComparison.Ordinal), $"{clothName}: collider {index} shape changed.");
            RequireClose(expected.StartX, actual.StartX, $"{clothName}: collider {index} start X");
            RequireClose(expected.StartY, actual.StartY, $"{clothName}: collider {index} start Y");
            RequireClose(expected.StartZ, actual.StartZ, $"{clothName}: collider {index} start Z");
            RequireClose(expected.EndX, actual.EndX, $"{clothName}: collider {index} end X");
            RequireClose(expected.EndY, actual.EndY, $"{clothName}: collider {index} end Y");
            RequireClose(expected.EndZ, actual.EndZ, $"{clothName}: collider {index} end Z");
            RequireClose(expected.Radius, actual.Radius, $"{clothName}: collider {index} radius");
            RequireMatrixClose(expected.Transform, actual.Transform, $"{clothName}: collider {index} transform");
        }
    }

    private static int FindClothByName(HkclService document, string name)
    {
        for (var index = 0; index < document.GetClothSummaries().Count; index++)
        {
            if (string.Equals(document.GetClothName(index), name, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private static void RequireMatrixClose(Matrix4x4 expected, Matrix4x4 actual, string field)
    {
        // The W lanes in Havok's transform vectors are format-specific SIMD
        // storage, not part of the collider's spatial basis or translation.
        // BPHCL correctly retains the template's lanes when applying an HKCL
        // transform, so only compare the geometric 3x3 basis and position.
        var expectedValues = new[] { expected.M11, expected.M12, expected.M13, expected.M21, expected.M22, expected.M23, expected.M31, expected.M32, expected.M33, expected.M41, expected.M42, expected.M43 };
        var actualValues = new[] { actual.M11, actual.M12, actual.M13, actual.M21, actual.M22, actual.M23, actual.M31, actual.M32, actual.M33, actual.M41, actual.M42, actual.M43 };
        for (var index = 0; index < expectedValues.Length; index++)
            RequireClose(expectedValues[index], actualValues[index], field + $" matrix[{index}]");
    }

    private static void RequireClose(float expected, float actual, string field)
    {
        if (MathF.Abs(expected - actual) > Epsilon)
            throw new InvalidOperationException($"{field} expected {expected.ToString("R", CultureInfo.InvariantCulture)}, got {actual.ToString("R", CultureInfo.InvariantCulture)}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string? ReadArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private sealed record FilePair(string HkclPath, string BphclPath);

    private sealed class TestResult
    {
        public int FilePairs { get; set; }
        public int MatchedCloths { get; set; }
        public int ConvertedCloths { get; set; }
        public int SkippedCloths { get; set; }
        public List<string> Failures { get; } = new();
    }
}






