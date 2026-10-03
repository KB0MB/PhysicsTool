using System.Globalization;

namespace HKCLTool;

// Developer-facing analysis for building verified BPHCL-to-HKCL profiles from
// a folder of vanilla counterparts. It never writes either source format.
internal static class ConversionMatchAnalyzer
{
    internal static IReadOnlyList<ConversionMatch> Analyze(string matchDirectory)
    {
        var results = new List<ConversionMatch>();
        foreach (var bphclPath in Directory.EnumerateFiles(matchDirectory, "*.bphcl", SearchOption.TopDirectoryOnly))
        {
            var baseName = Path.GetFileNameWithoutExtension(bphclPath)
                .Replace("RaulSkin_Upper", "Upper", StringComparison.Ordinal);
            var hkclPath = Path.Combine(matchDirectory, baseName + ".hkcl");
            if (!File.Exists(hkclPath))
                continue;

            var bphcl = NativeBphclDocument.Open(bphclPath);
            var hkcl = new HkclService();
            hkcl.Load(hkclPath);
            for (var sourceIndex = 0; sourceIndex < bphcl.Cloths.Count; sourceIndex++)
            {
                var sourceCloth = bphcl.Cloths[sourceIndex];
                if (sourceCloth.SimCloths.Count != 1)
                    continue;

                var normalizedName = StripPrefix(sourceCloth.Name);
                var targetIndex = Enumerable.Range(0, hkcl.GetClothSummaries().Count)
                    .FirstOrDefault(index => string.Equals(
                        StripPrefix(hkcl.GetClothName(index)),
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase), -1);
                if (targetIndex < 0)
                    continue;

                var sourceParticles = sourceCloth.SimCloths[0].Particles.Where(particle => !particle.Fixed).ToArray();
                var targetParticles = hkcl.GetParticleRows(targetIndex).Where(particle => !particle.Fixed).ToArray();
                var sourceMass = sourceParticles.Sum(particle => particle.Mass);
                var targetMass = targetParticles.Sum(particle => particle.Mass);
                if (sourceMass <= 0.0f || targetMass <= 0.0f)
                    continue;

                results.Add(new ConversionMatch(
                    baseName,
                    normalizedName,
                    sourceParticles.Length,
                    targetParticles.Length,
                    sourceMass,
                    targetMass,
                    targetMass / sourceMass,
                    ConversionStructuralFingerprint.Create(
                        sourceCloth.SimCloths[0],
                        bphcl.Skeletons.ElementAtOrDefault(sourceIndex)?.BoneCount ?? 0)));
            }
        }

        return results
            .OrderBy(match => match.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.ClothName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string Format(IReadOnlyList<ConversionMatch> matches) => string.Join(
        Environment.NewLine,
        matches.Select(match => string.Join('|',
            match.SourceFile,
            match.ClothName,
            match.SourceDynamicParticleCount.ToString(CultureInfo.InvariantCulture),
            match.TargetDynamicParticleCount.ToString(CultureInfo.InvariantCulture),
            match.SourceTotalMass.ToString("G9", CultureInfo.InvariantCulture),
            match.TargetTotalMass.ToString("G9", CultureInfo.InvariantCulture),
            match.Scale.ToString("G9", CultureInfo.InvariantCulture),
            match.Fingerprint.TotalParticleCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.FixedParticleCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.TriangleCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.ConstraintRecordCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.ConstraintSetCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.ColliderCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.BoneCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.AverageRadius.ToString("G9", CultureInfo.InvariantCulture),
            match.Fingerprint.AverageFriction.ToString("G9", CultureInfo.InvariantCulture),
            match.Fingerprint.ConstraintSignature)));

    // This intentionally compares known vanilla BPHCL/HKCL pairs rather than
    // generated output. It tells us which constraint fields actually changed
    // between TotK and BotW, and whether their ratio follows the mass scale.
    internal static ConstraintAuditReport AnalyzeConstraints(string matchDirectory)
    {
        var fields = new List<ConstraintAuditField>();

        foreach (var bphclPath in Directory.EnumerateFiles(matchDirectory, "*.bphcl", SearchOption.TopDirectoryOnly))
        {
            var sourceFile = Path.GetFileNameWithoutExtension(bphclPath)
                .Replace("RaulSkin_Upper", "Upper", StringComparison.Ordinal);
            var hkclPath = Path.Combine(matchDirectory, sourceFile + ".hkcl");
            if (!File.Exists(hkclPath))
                continue;

            var source = new HkclService();
            var target = new HkclService();
            source.Load(bphclPath);
            target.Load(hkclPath);

            for (var sourceIndex = 0; sourceIndex < source.GetClothSummaries().Count; sourceIndex++)
            {
                var clothName = StripPrefix(source.GetClothName(sourceIndex));
                var targetIndex = Enumerable.Range(0, target.GetClothSummaries().Count)
                    .FirstOrDefault(index => string.Equals(
                        StripPrefix(target.GetClothName(index)),
                        clothName,
                        StringComparison.OrdinalIgnoreCase), -1);
                if (targetIndex < 0)
                    continue;

                var sourceMass = source.GetParticleRows(sourceIndex)
                    .Where(particle => !particle.Fixed)
                    .Sum(particle => particle.Mass);
                var targetMass = target.GetParticleRows(targetIndex)
                    .Where(particle => !particle.Fixed)
                    .Sum(particle => particle.Mass);
                var massScale = sourceMass > 0.0f && targetMass > 0.0f
                    ? targetMass / sourceMass
                    : 1.0f;

                var sourceValues = CollectConstraintValues(source, sourceIndex);
                var targetValues = CollectConstraintValues(target, targetIndex);
                foreach (var key in sourceValues.Keys.Union(targetValues.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
                {
                    IReadOnlyList<float> sourceSet = sourceValues.GetValueOrDefault(key) ?? new List<float>();
                    IReadOnlyList<float> targetSet = targetValues.GetValueOrDefault(key) ?? new List<float>();
                    var separator = key.LastIndexOf('|');
                    var className = separator < 0 ? key : key[..separator];
                    var fieldName = separator < 0 ? string.Empty : key[(separator + 1)..];
                    var expectedScale = IsSoftCoefficient(className, fieldName) ? massScale : 1.0f;
                    var actualScale = CalculateMedianScale(sourceSet, targetSet);
                    var valuesMatch = sourceSet.Count == targetSet.Count && ValuesMatch(sourceSet, targetSet, expectedScale);

                    fields.Add(new ConstraintAuditField(
                        sourceFile,
                        clothName,
                        className,
                        fieldName,
                        sourceSet.Count,
                        targetSet.Count,
                        expectedScale,
                        actualScale,
                        valuesMatch));
                }
            }
        }

        return new ConstraintAuditReport(fields
            .OrderBy(field => field.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(field => field.ClothName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(field => field.ClassName, StringComparer.Ordinal)
            .ThenBy(field => field.FieldName, StringComparer.Ordinal)
            .ToArray());
    }

    internal static string FormatConstraints(ConstraintAuditReport report)
    {
        var lines = new List<string>
        {
            $"Audited {report.Fields.Count} constraint fields across matched vanilla cloths; {report.Fields.Count(field => !field.MatchesExpected)} differ from the expected BotW relationship."
        };

        foreach (var field in report.Fields.Where(field => !field.MatchesExpected))
        {
            lines.Add(string.Join('|',
                field.SourceFile,
                field.ClothName,
                field.ClassName,
                field.FieldName,
                $"sourceCount={field.SourceCount}",
                $"targetCount={field.TargetCount}",
                $"expectedScale={field.ExpectedScale.ToString("G7", CultureInfo.InvariantCulture)}",
                $"actualScale={(field.ActualScale.HasValue ? field.ActualScale.Value.ToString("G7", CultureInfo.InvariantCulture) : "n/a")}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    // Reports whether TotK's landscape-collision bit has a consistent BotW
    // translation across the vanilla counterpart samples. Bit 31 is not an
    // ordinary per-instance-collider bit, so it must be reviewed separately.
    internal static string AnalyzeCollisionMasks(string matchDirectory)
    {
        const uint landscapeBit = 0x80000000u;
        var lines = new List<string>();
        var matched = 0;
        var sourceLandscapeBits = 0;
        var targetLandscapeBits = 0;

        foreach (var bphclPath in Directory.EnumerateFiles(matchDirectory, "*.bphcl", SearchOption.TopDirectoryOnly))
        {
            var baseName = Path.GetFileNameWithoutExtension(bphclPath)
                .Replace("RaulSkin_Upper", "Upper", StringComparison.Ordinal);
            var hkclPath = Path.Combine(matchDirectory, baseName + ".hkcl");
            if (!File.Exists(hkclPath))
                continue;

            var source = new HkclService();
            source.Load(bphclPath);
            var target = new HkclService();
            target.Load(hkclPath);
            for (var sourceIndex = 0; sourceIndex < source.GetClothSummaries().Count; sourceIndex++)
            {
                var clothName = StripPrefix(source.GetClothName(sourceIndex));
                var targetIndex = FindClothIndex(target, clothName);
                if (targetIndex < 0)
                    continue;

                var sourceRows = source.GetParticleRows(sourceIndex);
                var targetRows = target.GetParticleRows(targetIndex);
                var sourceHigh = sourceRows.Count(row => (row.CollisionMask & landscapeBit) != 0);
                var targetHigh = targetRows.Count(row => (row.CollisionMask & landscapeBit) != 0);
                var sourceLow = sourceRows.Select(row => row.CollisionMask & ~landscapeBit).ToArray();
                var targetLow = targetRows.Select(row => row.CollisionMask & ~landscapeBit).ToArray();
                var lowBitsMatch = sourceLow.SequenceEqual(targetLow);

                matched++;
                sourceLandscapeBits += sourceHigh;
                targetLandscapeBits += targetHigh;
                if (sourceHigh != 0 || targetHigh != 0 || !lowBitsMatch)
                {
                    lines.Add($"{baseName}|{clothName}|sourceLandscape={sourceHigh}|targetLandscape={targetHigh}|lowBitsMatch={lowBitsMatch}");
                }
            }
        }

        lines.Insert(0,
            $"Mask audit: {matched} matched cloths; source landscape bits on {sourceLandscapeBits} particles; " +
            $"BotW landscape bits on {targetLandscapeBits} particles.");
        return string.Join(Environment.NewLine, lines);
    }

    // Builds the actual fresh HKCL export, writes it through the Wii U
    // serializer, reopens it, then compares each matched cloth with the
    // vanilla BotW counterpart. Temporary files are always deleted.
    internal static FreshConversionAuditReport AnalyzeFreshExports(string matchDirectory)
    {
        var issues = new List<FreshConversionAuditIssue>();
        var candidateFiles = 0;
        var generatedFiles = 0;
        var matchedCloths = 0;

        foreach (var bphclPath in Directory.EnumerateFiles(matchDirectory, "*.bphcl", SearchOption.TopDirectoryOnly))
        {
            var sourceFile = Path.GetFileNameWithoutExtension(bphclPath)
                .Replace("RaulSkin_Upper", "Upper", StringComparison.Ordinal);
            var hkclPath = Path.Combine(matchDirectory, sourceFile + ".hkcl");
            if (!File.Exists(hkclPath))
                continue;

            candidateFiles++;
            var temporaryPath = Path.Combine(Path.GetTempPath(), $"PhysicsTool-audit-{Guid.NewGuid():N}.hkcl");
            try
            {
                var source = new HkclService();
                source.Load(bphclPath);
                var generated = source.CreateFreshHkclFromCurrentBphclDocument();
                generated.SaveHkcl(temporaryPath, HkclPlatform.WiiU);

                var reopened = new HkclService();
                reopened.Load(temporaryPath);
                var vanilla = new HkclService();
                vanilla.Load(hkclPath);
                generatedFiles++;

                for (var vanillaIndex = 0; vanillaIndex < vanilla.GetClothSummaries().Count; vanillaIndex++)
                {
                    var clothName = StripPrefix(vanilla.GetClothName(vanillaIndex));
                    var generatedIndex = Enumerable.Range(0, reopened.GetClothSummaries().Count)
                        .FirstOrDefault(index => string.Equals(
                            StripPrefix(reopened.GetClothName(index)),
                            clothName,
                            StringComparison.OrdinalIgnoreCase), -1);
                    if (generatedIndex < 0)
                        continue;

                    matchedCloths++;
                    issues.AddRange(CompareFreshCloth(sourceFile, clothName, reopened, generatedIndex, vanilla, vanillaIndex));
                }
            }
            catch (Exception exception)
            {
                issues.Add(new FreshConversionAuditIssue(
                    sourceFile,
                    "<file>",
                    "Fresh export",
                    "Serializer/reopen succeeds",
                    exception.GetBaseException().Message));
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        return new FreshConversionAuditReport(candidateFiles, generatedFiles, matchedCloths, issues);
    }

    internal static string FormatFreshExports(FreshConversionAuditReport report)
    {
        var lines = new List<string>
        {
            $"Fresh export audit: {report.GeneratedFiles}/{report.CandidateFiles} paired BPHCL files serialized and reopened; " +
            $"{report.MatchedCloths} cloths matched by name; {report.Issues.Count} difference(s)."
        };
        foreach (var issue in report.Issues)
        {
            lines.Add(string.Join('|', issue.SourceFile, issue.ClothName, issue.Category,
                $"expected={issue.Expected}", $"actual={issue.Actual}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    // Emits a human-readable comparison for one known BPHCL/HKCL counterpart.
    // This is deliberately a diagnostic command rather than conversion logic:
    // it lets us identify a missing data mapping before changing the exporter.
    internal static string DescribeFreshClothComparison(string bphclPath, string hkclPath, string clothName)
    {
        var native = NativeBphclDocument.Open(bphclPath);
        var source = new HkclService();
        source.Load(bphclPath);
        var generated = source.CreateFreshHkclFromCurrentBphclDocument();
        var vanilla = new HkclService();
        vanilla.Load(hkclPath);

        var generatedIndex = FindClothIndex(generated, clothName);
        var vanillaIndex = FindClothIndex(vanilla, clothName);
        if (generatedIndex < 0 || vanillaIndex < 0)
        {
            throw new InvalidOperationException(
                $"Could not find '{clothName}' in both files. " +
                $"Fresh={string.Join(", ", generated.GetClothSummaries())}; " +
                $"vanilla={string.Join(", ", vanilla.GetClothSummaries())}.");
        }

        var lines = new List<string>
        {
            $"Fresh conversion comparison: {StripPrefix(clothName)}",
            $"BPHCL: {Path.GetFileName(bphclPath)}",
            $"HKCL: {Path.GetFileName(hkclPath)}",
            string.Empty
        };

        AppendParticleDifferences(lines, generated.GetParticleRows(generatedIndex), vanilla.GetParticleRows(vanillaIndex));
        AppendBoneDifferences(lines, generated.GetBoneRows(generatedIndex), vanilla.GetBoneRows(vanillaIndex));
        AppendColliderDifferences(lines, generated.GetColliderRows(generatedIndex), vanilla.GetColliderRows(vanillaIndex));
        AppendConstraintDifferences(lines, generated, generatedIndex, vanilla, vanillaIndex);
        AppendSimulationSettingDifferences(
            lines,
            generated.GetSimulationSettings(generatedIndex),
            vanilla.GetSimulationSettings(vanillaIndex));
        var nativeCloth = native.Cloths.FirstOrDefault(cloth => string.Equals(
            StripPrefix(cloth.Name), StripPrefix(clothName), StringComparison.OrdinalIgnoreCase));
        if (nativeCloth is not null)
        {
            AppendNativeSimulationSummary(lines, nativeCloth!);
            AppendNativeStateSummary(lines, nativeCloth!);
        }
        AppendRuntimeSummary(lines, generated.GetClothRuntimeData(generatedIndex), vanilla.GetClothRuntimeData(vanillaIndex));
        AppendSimpleMeshDeformerDifferences(
            lines,
            generated.GetSimpleMeshBoneDeformData(generatedIndex),
            vanilla.GetSimpleMeshBoneDeformData(vanillaIndex));
        AppendSkinOperatorDifferences(
            lines,
            generated.GetSkinOperatorData(generatedIndex),
            vanilla.GetSkinOperatorData(vanillaIndex));

        return string.Join(Environment.NewLine, lines);
    }

    private static int FindClothIndex(HkclService document, string name) =>
        Enumerable.Range(0, document.GetClothSummaries().Count)
            .FirstOrDefault(index => string.Equals(
                StripPrefix(document.GetClothName(index)),
                StripPrefix(name),
                StringComparison.OrdinalIgnoreCase), -1);

    private static void AppendParticleDifferences(
        ICollection<string> lines,
        IReadOnlyList<ParticleEditRow> fresh,
        IReadOnlyList<ParticleEditRow> vanilla)
    {
        var freshByIndex = fresh.ToDictionary(particle => particle.Index);
        var vanillaByIndex = vanilla.ToDictionary(particle => particle.Index);
        var differences = 0;
        foreach (var index in freshByIndex.Keys.Union(vanillaByIndex.Keys).OrderBy(index => index))
        {
            var hasActual = freshByIndex.TryGetValue(index, out var actual);
            var hasExpected = vanillaByIndex.TryGetValue(index, out var expected);
            if (!hasActual || !hasExpected)
            {
                lines.Add($"Particle #{index}: {(hasExpected ? "vanilla only" : "fresh only")}");
                differences++;
                continue;
            }

            var actualDescription = DescribeParticle(actual!);
            var expectedDescription = DescribeParticle(expected!);
            if (string.Equals(actualDescription, expectedDescription, StringComparison.Ordinal))
                continue;

            lines.Add($"Particle #{index}");
            lines.Add($"  vanilla: {expectedDescription}");
            lines.Add($"  fresh:   {actualDescription}");
            differences++;
        }

        lines.Add($"Particles: {differences} differing row(s) of fresh={fresh.Count}, vanilla={vanilla.Count}.");
        lines.Add(string.Empty);
    }

    private static void AppendSimulationSettingDifferences(
        ICollection<string> lines,
        ClothSimulationSettings fresh,
        ClothSimulationSettings vanilla)
    {
        var comparisons = new (string Name, string Fresh, string Vanilla, bool Matches)[]
        {
            ("gravity X", FormatFloat(fresh.GravityX), FormatFloat(vanilla.GravityX), NearlyEqual(fresh.GravityX, vanilla.GravityX)),
            ("gravity Y", FormatFloat(fresh.GravityY), FormatFloat(vanilla.GravityY), NearlyEqual(fresh.GravityY, vanilla.GravityY)),
            ("gravity Z", FormatFloat(fresh.GravityZ), FormatFloat(vanilla.GravityZ), NearlyEqual(fresh.GravityZ, vanilla.GravityZ)),
            ("damping", FormatFloat(fresh.DampingPerSecond), FormatFloat(vanilla.DampingPerSecond), NearlyEqual(fresh.DampingPerSecond, vanilla.DampingPerSecond)),
            ("collision tolerance", FormatFloat(fresh.CollisionTolerance), FormatFloat(vanilla.CollisionTolerance), NearlyEqual(fresh.CollisionTolerance, vanilla.CollisionTolerance)),
            ("translation motion", fresh.TransferTranslationMotion.ToString(), vanilla.TransferTranslationMotion.ToString(), fresh.TransferTranslationMotion == vanilla.TransferTranslationMotion),
            ("translation min speed", FormatFloat(fresh.MinTranslationSpeed), FormatFloat(vanilla.MinTranslationSpeed), NearlyEqual(fresh.MinTranslationSpeed, vanilla.MinTranslationSpeed)),
            ("translation max speed", FormatFloat(fresh.MaxTranslationSpeed), FormatFloat(vanilla.MaxTranslationSpeed), NearlyEqual(fresh.MaxTranslationSpeed, vanilla.MaxTranslationSpeed)),
            ("translation min blend", FormatFloat(fresh.MinTranslationBlend), FormatFloat(vanilla.MinTranslationBlend), NearlyEqual(fresh.MinTranslationBlend, vanilla.MinTranslationBlend)),
            ("translation max blend", FormatFloat(fresh.MaxTranslationBlend), FormatFloat(vanilla.MaxTranslationBlend), NearlyEqual(fresh.MaxTranslationBlend, vanilla.MaxTranslationBlend)),
            ("rotation motion", fresh.TransferRotationMotion.ToString(), vanilla.TransferRotationMotion.ToString(), fresh.TransferRotationMotion == vanilla.TransferRotationMotion),
            ("rotation min speed", FormatFloat(fresh.MinRotationSpeed), FormatFloat(vanilla.MinRotationSpeed), NearlyEqual(fresh.MinRotationSpeed, vanilla.MinRotationSpeed)),
            ("rotation max speed", FormatFloat(fresh.MaxRotationSpeed), FormatFloat(vanilla.MaxRotationSpeed), NearlyEqual(fresh.MaxRotationSpeed, vanilla.MaxRotationSpeed)),
            ("rotation min blend", FormatFloat(fresh.MinRotationBlend), FormatFloat(vanilla.MinRotationBlend), NearlyEqual(fresh.MinRotationBlend, vanilla.MinRotationBlend)),
            ("rotation max blend", FormatFloat(fresh.MaxRotationBlend), FormatFloat(vanilla.MaxRotationBlend), NearlyEqual(fresh.MaxRotationBlend, vanilla.MaxRotationBlend))
        };

        var mismatches = comparisons.Where(comparison => !comparison.Matches).ToArray();
        lines.Add("Simulation settings");
        if (mismatches.Length == 0)
        {
            lines.Add("  fresh values match the vanilla counterpart.");
        }
        else
        {
            foreach (var mismatch in mismatches)
                lines.Add($"  {mismatch.Name}: vanilla={mismatch.Vanilla}; fresh={mismatch.Fresh}");
        }
        lines.Add(string.Empty);
    }

    private static void AppendBoneDifferences(
        ICollection<string> lines,
        IReadOnlyList<BoneEditRow> fresh,
        IReadOnlyList<BoneEditRow> vanilla)
    {
        var freshByIndex = fresh.ToDictionary(bone => bone.Index);
        var vanillaByIndex = vanilla.ToDictionary(bone => bone.Index);
        var differences = 0;
        foreach (var index in freshByIndex.Keys.Union(vanillaByIndex.Keys).OrderBy(index => index))
        {
            var hasActual = freshByIndex.TryGetValue(index, out var actual);
            var hasExpected = vanillaByIndex.TryGetValue(index, out var expected);
            if (!hasActual || !hasExpected)
            {
                lines.Add($"Bone #{index}: {(hasExpected ? "vanilla only" : "fresh only")}");
                differences++;
                continue;
            }

            var actualDescription = DescribeBone(actual!);
            var expectedDescription = DescribeBone(expected!);
            if (string.Equals(actualDescription, expectedDescription, StringComparison.Ordinal))
                continue;

            lines.Add($"Bone #{index} ({expected!.Name})");
            lines.Add($"  vanilla: {expectedDescription}");
            lines.Add($"  fresh:   {actualDescription}");
            differences++;
        }

        lines.Add($"Bones: {differences} differing row(s) of fresh={fresh.Count}, vanilla={vanilla.Count}.");
        lines.Add(string.Empty);
    }

    private static void AppendColliderDifferences(
        ICollection<string> lines,
        IReadOnlyList<ColliderEditRow> fresh,
        IReadOnlyList<ColliderEditRow> vanilla)
    {
        var freshByName = fresh.ToDictionary(collider => collider.Name, StringComparer.OrdinalIgnoreCase);
        var vanillaByName = vanilla.ToDictionary(collider => collider.Name, StringComparer.OrdinalIgnoreCase);
        var differences = 0;
        foreach (var name in freshByName.Keys.Union(vanillaByName.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var hasActual = freshByName.TryGetValue(name, out var actual);
            var hasExpected = vanillaByName.TryGetValue(name, out var expected);
            if (!hasActual || !hasExpected)
            {
                lines.Add($"Collider {name}: {(hasExpected ? "vanilla only" : "fresh only")}");
                differences++;
                continue;
            }

            var actualDescription = DescribeCollider(actual!);
            var expectedDescription = DescribeCollider(expected!);
            if (string.Equals(actualDescription, expectedDescription, StringComparison.Ordinal))
                continue;

            lines.Add($"Collider {name}");
            lines.Add($"  vanilla: {expectedDescription}");
            lines.Add($"  fresh:   {actualDescription}");
            differences++;
        }

        lines.Add($"Colliders: {differences} differing row(s) of fresh={fresh.Count}, vanilla={vanilla.Count}.");
        lines.Add(string.Empty);
    }

    private static void AppendConstraintDifferences(
        ICollection<string> lines,
        HkclService fresh,
        int freshIndex,
        HkclService vanilla,
        int vanillaIndex)
    {
        var freshValues = CollectConstraintValues(fresh, freshIndex);
        var vanillaValues = CollectConstraintValues(vanilla, vanillaIndex);
        var differences = 0;
        foreach (var key in freshValues.Keys.Union(vanillaValues.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
        {
            var actual = freshValues.GetValueOrDefault(key) ?? new List<float>();
            var expected = vanillaValues.GetValueOrDefault(key) ?? new List<float>();
            if (actual.Count == expected.Count && actual.Zip(expected).All(pair => NearlyEqual(pair.First, pair.Second)))
                continue;

            lines.Add($"Constraint {key}");
            lines.Add($"  vanilla: {string.Join(", ", expected.Select(FormatFloat))}");
            lines.Add($"  fresh:   {string.Join(", ", actual.Select(FormatFloat))}");
            differences++;
        }

        lines.Add($"Constraints: {differences} differing field(s).");
        lines.Add(string.Empty);
    }

    private static void AppendRuntimeSummary(ICollection<string> lines, ClothRuntimeData fresh, ClothRuntimeData vanilla)
    {
        lines.Add("Runtime layout");
        lines.Add($"  vanilla buffers: {DescribeRuntimeBuffers(vanilla)}");
        lines.Add($"  fresh buffers:   {DescribeRuntimeBuffers(fresh)}");
        lines.Add($"  vanilla operators: {DescribeRuntimeOperators(vanilla)}");
        lines.Add($"  fresh operators:   {DescribeRuntimeOperators(fresh)}");
        lines.Add($"  vanilla simulation: {DescribeRuntimeSimulationConfigs(vanilla)}");
        lines.Add($"  fresh simulation:   {DescribeRuntimeSimulationConfigs(fresh)}");
        lines.Add($"  vanilla states: {DescribeRuntimeStates(vanilla)}");
        lines.Add($"  fresh states:   {DescribeRuntimeStates(fresh)}");
    }

    // These two payloads are where a simulated virtual mesh is fed from the
    // animation skeleton and written back to physics bones. A raw hash can
    // say they differ, but this report needs to say which native layer did.
    private static void AppendSimpleMeshDeformerDifferences(
        ICollection<string> lines,
        IReadOnlyList<SimpleMeshBoneDeformData> fresh,
        IReadOnlyList<SimpleMeshBoneDeformData> vanilla)
    {
        lines.Add(string.Empty);
        lines.Add("Mesh-to-bone deformation");

        var freshByOperator = fresh.ToDictionary(deformer => deformer.OperatorIndex);
        var vanillaByOperator = vanilla.ToDictionary(deformer => deformer.OperatorIndex);
        var differences = 0;
        foreach (var operatorIndex in freshByOperator.Keys.Union(vanillaByOperator.Keys).OrderBy(index => index))
        {
            var actual = freshByOperator.GetValueOrDefault(operatorIndex);
            var expected = vanillaByOperator.GetValueOrDefault(operatorIndex);
            if (actual is null || expected is null)
            {
                lines.Add($"  operator #{operatorIndex}: {(expected is null ? "fresh only" : "vanilla only")}");
                differences++;
                continue;
            }

            var headerMatches = actual.InputBufferIndex == expected.InputBufferIndex &&
                actual.OutputTransformSetIndex == expected.OutputTransformSetIndex &&
                actual.BoneAxis == expected.BoneAxis;
            var pairsMatch = actual.TriangleBonePairs.SequenceEqual(expected.TriangleBonePairs);
            var transformComparison = CompareMatrices(actual.LocalBoneTransforms, expected.LocalBoneTransforms);
            if (headerMatches && pairsMatch && transformComparison.Matches)
                continue;

            lines.Add($"  operator #{operatorIndex}");
            if (!headerMatches)
            {
                lines.Add($"    vanilla header: input={expected.InputBufferIndex}; outputSet={expected.OutputTransformSetIndex}; axis={expected.BoneAxis}");
                lines.Add($"    fresh header:   input={actual.InputBufferIndex}; outputSet={actual.OutputTransformSetIndex}; axis={actual.BoneAxis}");
            }
            if (!pairsMatch)
            {
                lines.Add($"    vanilla pairs: {DescribeTriangleBonePairs(expected.TriangleBonePairs)}");
                lines.Add($"    fresh pairs:   {DescribeTriangleBonePairs(actual.TriangleBonePairs)}");
            }
            if (!transformComparison.Matches)
                lines.Add($"    local transforms: {transformComparison.Description}");
            differences++;
        }

        lines.Add($"  differing operator(s): {differences}.");
    }

    private static void AppendSkinOperatorDifferences(
        ICollection<string> lines,
        IReadOnlyList<SkinOperatorData> fresh,
        IReadOnlyList<SkinOperatorData> vanilla)
    {
        lines.Add(string.Empty);
        lines.Add("Animation-to-cloth skinning");

        var freshByOperator = fresh.ToDictionary(skin => skin.OperatorIndex);
        var vanillaByOperator = vanilla.ToDictionary(skin => skin.OperatorIndex);
        var differences = 0;
        foreach (var operatorIndex in freshByOperator.Keys.Union(vanillaByOperator.Keys).OrderBy(index => index))
        {
            var actual = freshByOperator.GetValueOrDefault(operatorIndex);
            var expected = vanillaByOperator.GetValueOrDefault(operatorIndex);
            if (actual is null || expected is null)
            {
                lines.Add($"  operator #{operatorIndex}: {(expected is null ? "fresh only" : "vanilla only")}");
                differences++;
                continue;
            }

            var headerMatches = string.Equals(actual.ClassName, expected.ClassName, StringComparison.Ordinal) &&
                actual.OutputBufferIndex == expected.OutputBufferIndex &&
                actual.TransformSetIndex == expected.TransformSetIndex &&
                actual.StartVertexIndex == expected.StartVertexIndex &&
                actual.EndVertexIndex == expected.EndVertexIndex &&
                actual.BatchSizeSpu == expected.BatchSizeSpu &&
                actual.PartialWrite == expected.PartialWrite;
            var subsetMatches = actual.TransformSubset.SequenceEqual(expected.TransformSubset);
            var controlsMatch = actual.ControlBytes.SequenceEqual(expected.ControlBytes);
            var blendsMatch = BlendArraysEqual(actual.BlendArrays, expected.BlendArrays);
            var skinMatrices = CompareMatrices(actual.BoneFromSkinMeshTransforms, expected.BoneFromSkinMeshTransforms);
            var localPositions = ComparePositionBlocks(actual.LocalPositionBlocks, expected.LocalPositionBlocks);
            var unpackedPositions = ComparePositionBlocks(actual.LocalUnpackedPositionBlocks, expected.LocalUnpackedPositionBlocks);
            if (headerMatches && subsetMatches && controlsMatch && blendsMatch && skinMatrices.Matches &&
                localPositions.Matches && unpackedPositions.Matches)
                continue;

            lines.Add($"  operator #{operatorIndex} ({expected.ClassName})");
            if (!headerMatches)
            {
                lines.Add($"    vanilla header: {DescribeSkinHeader(expected)}");
                lines.Add($"    fresh header:   {DescribeSkinHeader(actual)}");
            }
            if (!subsetMatches)
            {
                lines.Add($"    vanilla transform subset: {string.Join(',', expected.TransformSubset)}");
                lines.Add($"    fresh transform subset:   {string.Join(',', actual.TransformSubset)}");
            }
            if (!controlsMatch)
            {
                lines.Add($"    vanilla controls: {string.Join(',', expected.ControlBytes)}");
                lines.Add($"    fresh controls:   {string.Join(',', actual.ControlBytes)}");
            }
            if (!blendsMatch)
            {
                lines.Add($"    vanilla blends: {DescribeSkinBlends(expected.BlendArrays)}");
                lines.Add($"    fresh blends:   {DescribeSkinBlends(actual.BlendArrays)}");
            }
            if (!skinMatrices.Matches)
                lines.Add($"    bone-from-mesh transforms: {skinMatrices.Description}");
            if (!localPositions.Matches)
                lines.Add($"    local positions: {localPositions.Description}");
            if (!unpackedPositions.Matches)
                lines.Add($"    unpacked positions: {unpackedPositions.Description}");
            differences++;
        }

        lines.Add($"  differing operator(s): {differences}.");
    }

    private static string DescribeTriangleBonePairs(IReadOnlyList<SimpleMeshTriangleBonePairData> pairs) =>
        string.Join(", ", pairs.Select(pair => $"{pair.BoneOffset}:{pair.TriangleOffset}"));

    private static string DescribeSkinHeader(SkinOperatorData skin) =>
        $"class={skin.ClassName}; output={skin.OutputBufferIndex}; set={skin.TransformSetIndex}; " +
        $"vertices={skin.StartVertexIndex}-{skin.EndVertexIndex}; batch={skin.BatchSizeSpu}; partial={skin.PartialWrite}";

    private static string DescribeSkinBlends(IReadOnlyList<SkinBlendArrayData> arrays) => string.Join("; ", arrays
        .Where(array => array.Blocks.Count > 0)
        .Select(array => DescribeBlendArray(array)));

    private static bool BlendArraysEqual(IReadOnlyList<SkinBlendArrayData> fresh, IReadOnlyList<SkinBlendArrayData> vanilla)
    {
        if (fresh.Count != vanilla.Count)
            return false;

        for (var arrayIndex = 0; arrayIndex < fresh.Count; arrayIndex++)
        {
            var actual = fresh[arrayIndex];
            var expected = vanilla[arrayIndex];
            if (!string.Equals(actual.MemberName, expected.MemberName, StringComparison.Ordinal) ||
                actual.BlendCount != expected.BlendCount ||
                actual.VertexCount != expected.VertexCount ||
                actual.BoneCount != expected.BoneCount ||
                actual.Blocks.Count != expected.Blocks.Count)
                return false;

            for (var blockIndex = 0; blockIndex < actual.Blocks.Count; blockIndex++)
            {
                var actualBlock = actual.Blocks[blockIndex];
                var expectedBlock = expected.Blocks[blockIndex];
                if (!actualBlock.VertexIndices.SequenceEqual(expectedBlock.VertexIndices) ||
                    !actualBlock.BoneIndices.SequenceEqual(expectedBlock.BoneIndices) ||
                    !actualBlock.BoneWeights.SequenceEqual(expectedBlock.BoneWeights))
                    return false;
            }
        }

        return true;
    }

    private static PayloadComparison CompareMatrices(
        IReadOnlyList<System.Numerics.Matrix4x4> fresh,
        IReadOnlyList<System.Numerics.Matrix4x4> vanilla)
    {
        if (fresh.Count != vanilla.Count)
            return new PayloadComparison(false, $"count vanilla={vanilla.Count}; fresh={fresh.Count}");

        var worstDifference = 0f;
        var worstIndex = -1;
        for (var index = 0; index < fresh.Count; index++)
        {
            var difference = MaxMatrixDifference(fresh[index], vanilla[index]);
            if (difference > worstDifference)
            {
                worstDifference = difference;
                worstIndex = index;
            }
        }

        return NearlyEqual(worstDifference, 0f)
            ? new PayloadComparison(true, string.Empty)
            : new PayloadComparison(false, $"count={fresh.Count}; largest delta={FormatFloat(worstDifference)} at #{worstIndex}; " +
                $"vanilla={DescribeMatrix(vanilla[worstIndex])}; fresh={DescribeMatrix(fresh[worstIndex])}");
    }

    private static PayloadComparison ComparePositionBlocks(
        IReadOnlyList<SkinPositionBlockData> fresh,
        IReadOnlyList<SkinPositionBlockData> vanilla)
    {
        if (fresh.Count != vanilla.Count)
            return new PayloadComparison(false, $"block count vanilla={vanilla.Count}; fresh={fresh.Count}");

        var worstDifference = 0f;
        var worstBlock = -1;
        var worstPosition = -1;
        for (var blockIndex = 0; blockIndex < fresh.Count; blockIndex++)
        {
            var actualBlock = fresh[blockIndex].Positions;
            var expectedBlock = vanilla[blockIndex].Positions;
            if (actualBlock.Count != expectedBlock.Count)
            {
                return new PayloadComparison(false,
                    $"block #{blockIndex} position count vanilla={expectedBlock.Count}; fresh={actualBlock.Count}");
            }

            for (var positionIndex = 0; positionIndex < actualBlock.Count; positionIndex++)
            {
                var difference = MaxVectorDifference(actualBlock[positionIndex], expectedBlock[positionIndex]);
                if (difference > worstDifference)
                {
                    worstDifference = difference;
                    worstBlock = blockIndex;
                    worstPosition = positionIndex;
                }
            }
        }

        return NearlyEqual(worstDifference, 0f)
            ? new PayloadComparison(true, string.Empty)
            : new PayloadComparison(false,
                $"blocks={fresh.Count}; largest delta={FormatFloat(worstDifference)} at block #{worstBlock}, position #{worstPosition}; " +
                $"vanilla={DescribeVector(vanilla[worstBlock].Positions[worstPosition])}; " +
                $"fresh={DescribeVector(fresh[worstBlock].Positions[worstPosition])}");
    }

    private static float MaxMatrixDifference(System.Numerics.Matrix4x4 left, System.Numerics.Matrix4x4 right) =>
        new[]
        {
            Math.Abs(left.M11 - right.M11), Math.Abs(left.M12 - right.M12), Math.Abs(left.M13 - right.M13), Math.Abs(left.M14 - right.M14),
            Math.Abs(left.M21 - right.M21), Math.Abs(left.M22 - right.M22), Math.Abs(left.M23 - right.M23), Math.Abs(left.M24 - right.M24),
            Math.Abs(left.M31 - right.M31), Math.Abs(left.M32 - right.M32), Math.Abs(left.M33 - right.M33), Math.Abs(left.M34 - right.M34),
            Math.Abs(left.M41 - right.M41), Math.Abs(left.M42 - right.M42), Math.Abs(left.M43 - right.M43), Math.Abs(left.M44 - right.M44)
        }.Max();

    private static float MaxVectorDifference(System.Numerics.Vector4 left, System.Numerics.Vector4 right) =>
        Math.Max(Math.Max(Math.Abs(left.X - right.X), Math.Abs(left.Y - right.Y)),
            Math.Max(Math.Abs(left.Z - right.Z), Math.Abs(left.W - right.W)));

    private static string DescribeVector(System.Numerics.Vector4 vector) =>
        string.Join(',', new[] { vector.X, vector.Y, vector.Z, vector.W }.Select(FormatFloat));

    private sealed record PayloadComparison(bool Matches, string Description);

    private static void AppendNativeStateSummary(ICollection<string> lines, NativeBphclCloth cloth)
    {
        lines.Add("BPHCL native buffers");
        foreach (var buffer in cloth.BufferDefinitions.OrderBy(buffer => buffer.Index))
        {
            lines.Add(
                $"  #{buffer.Index}: item={buffer.ItemIndex}; {buffer.ClassName}; type={buffer.Type}; " +
                $"vertices={buffer.VertexCount}; triangles={buffer.TriangleCount}");
        }

        lines.Add("BPHCL native state accesses");
        foreach (var state in cloth.States.OrderBy(state => state.Index))
        {
            lines.Add(
                $"  #{state.Index}:{state.Name}[" +
                $"ops={string.Join(',', state.OperatorItemIndices)}, " +
                $"buf={string.Join(',', state.BufferAccesses.Select(access => $"{access.BufferIndex}[{access.ComponentFlags0},{access.ComponentFlags1},{access.ComponentFlags2},{access.ComponentFlags3};tri={access.TrianglesRead};shadow={access.ShadowBufferIndex}]"))}, " +
                $"set={string.Join(',', state.TransformSetAccesses.Select(access => $"{access.TransformSetIndex}[{access.ComponentFlags0},{access.ComponentFlags1}]"))}, " +
                $"sim={string.Join(',', state.SimClothItemIndices)}]");
        }

        lines.Add(string.Empty);
    }

    private static void AppendNativeSimulationSummary(ICollection<string> lines, NativeBphclCloth cloth)
    {
        foreach (var simulation in cloth.SimCloths)
        {
            lines.Add(
                $"BPHCL simulation #{simulation.Index}: landscapeEnabled={simulation.Runtime.LandscapeCollisionEnabled}; " +
                $"landscapeParticles={simulation.Runtime.NumLandscapeCollidableParticles}; " +
                $"masks={string.Join(',', simulation.StaticCollisionMasks.Select(mask => $"0x{mask:X8}"))}");
        }

        lines.Add(string.Empty);
    }

    private static string DescribeParticle(ParticleEditRow particle) =>
        $"fixed={particle.Fixed}; pos={FormatFloat(particle.X)},{FormatFloat(particle.Y)},{FormatFloat(particle.Z)},{FormatFloat(particle.W)}; " +
        $"mass={FormatFloat(particle.Mass)}; invMass={FormatFloat(particle.InverseMass)}; radius={FormatFloat(particle.Radius)}; " +
        $"friction={FormatFloat(particle.Friction)}; mask={particle.CollisionMask}";

    private static string DescribeBone(BoneEditRow bone) =>
        $"name={bone.Name}; parent={bone.ParentIndex}; pos={FormatFloat(bone.X)},{FormatFloat(bone.Y)},{FormatFloat(bone.Z)}; " +
        $"rot={FormatFloat(bone.RotationX)},{FormatFloat(bone.RotationY)},{FormatFloat(bone.RotationZ)},{FormatFloat(bone.RotationW)}; " +
        $"scale={FormatFloat(bone.ScaleX)},{FormatFloat(bone.ScaleY)},{FormatFloat(bone.ScaleZ)}";

    private static string DescribeCollider(ColliderEditRow collider) =>
        $"type={collider.ShapeType}; bone={collider.BoneName}; start={FormatFloat(collider.StartX)},{FormatFloat(collider.StartY)},{FormatFloat(collider.StartZ)}; " +
        $"end={FormatFloat(collider.EndX)},{FormatFloat(collider.EndY)},{FormatFloat(collider.EndZ)}; radius={FormatFloat(collider.Radius)}; " +
        $"normal={FormatFloat(collider.PlaneNormalX)},{FormatFloat(collider.PlaneNormalY)},{FormatFloat(collider.PlaneNormalZ)}";

    private static string DescribeRuntimeBuffers(ClothRuntimeData runtime) => string.Join("; ", runtime.Buffers
        .OrderBy(buffer => buffer.Index)
        .Select(buffer => $"#{buffer.Index}:{buffer.ClassName}[v={buffer.VertexCount},t={buffer.TriangleCount},tri={string.Join(',', buffer.TriangleIndices)}]"));

    private static string DescribeRuntimeOperators(ClothRuntimeData runtime) => string.Join("; ", runtime.Operators
        .OrderBy(@operator => @operator.Index)
        .Select(@operator => $"#{@operator.Index}:{@operator.ClassName}[in={@operator.InputBufferIndex},out={@operator.OutputBufferIndex},set={@operator.TransformSetIndex}]"));

    private static string DescribeRuntimeSimulationConfigs(ClothRuntimeData runtime) => string.Join("; ", runtime.Operators
        .Where(@operator => @operator.SimulateConfigs.Count > 0 || @operator.VertexParticlePairs.Count > 0)
        .OrderBy(@operator => @operator.Index)
        .Select(@operator =>
        {
            var anchors = @operator.VertexParticlePairs.Count == 0
                ? string.Empty
                : $" anchors={string.Join(',', @operator.VertexParticlePairs.Select(pair => $"{pair.VertexIndex}:{pair.ParticleIndex}"))}";
            var configs = @operator.SimulateConfigs.Count == 0
                ? string.Empty
                : $" configs={string.Join(',', @operator.SimulateConfigs.Select(config =>
                    $"sub={config.SubSteps},iter={config.NumberOfSolveIterations},adaptive={config.AdaptConstraintStiffness},allColliders={config.UseAllInstanceCollidables},selected=[{string.Join(',', config.InstanceCollidablesUsed)}],constraints=[{string.Join(',', config.ConstraintExecution)}]"))}";
            return $"#{@operator.Index}:{@operator.ClassName}[sim={@operator.SimulationClothIndex},ref={@operator.ReferenceBufferIndex}{anchors}{configs}]";
        }));

    private static string DescribeRuntimeStates(ClothRuntimeData runtime) => string.Join("; ", runtime.States
        .OrderBy(state => state.Index)
        .Select(state => $"#{state.Index}:{state.Name}[ops={string.Join(',', state.OperatorIndices)},buf={string.Join(',', state.BufferIndices)},set={string.Join(',', state.TransformSetIndices)},sim={string.Join(',', state.SimulationIndices)}]"));

    private static IEnumerable<FreshConversionAuditIssue> CompareFreshCloth(
        string sourceFile,
        string clothName,
        HkclService generated,
        int generatedIndex,
        HkclService vanilla,
        int vanillaIndex)
    {
        var issues = new List<FreshConversionAuditIssue>();
        AddCountDifference("Skeleton bones", generated.GetBoneRows(generatedIndex).Count, vanilla.GetBoneRows(vanillaIndex).Count);

        var generatedParticles = generated.GetParticleRows(generatedIndex);
        var vanillaParticles = vanilla.GetParticleRows(vanillaIndex);
        AddCountDifference("Particles", generatedParticles.Count, vanillaParticles.Count);
        AddCountDifference("Fixed particles", generatedParticles.Count(particle => particle.Fixed), vanillaParticles.Count(particle => particle.Fixed));
        AddCountDifference("Surface triangles", GetTriangleCount(generated, generatedIndex), GetTriangleCount(vanilla, vanillaIndex));
        AddCountDifference("Active colliders", generated.GetParticleColliderOptions(generatedIndex).Count, vanilla.GetParticleColliderOptions(vanillaIndex).Count);

        var generatedMass = generatedParticles.Where(particle => !particle.Fixed).Sum(particle => particle.Mass);
        var vanillaMass = vanillaParticles.Where(particle => !particle.Fixed).Sum(particle => particle.Mass);
        if (!NearlyEqual(generatedMass, vanillaMass))
        {
            issues.Add(NewIssue(
                "Dynamic total mass",
                vanillaMass.ToString("G8", CultureInfo.InvariantCulture),
                generatedMass.ToString("G8", CultureInfo.InvariantCulture)));
        }

        var generatedSettings = generated.GetSimulationSettings(generatedIndex);
        var vanillaSettings = vanilla.GetSimulationSettings(vanillaIndex);
        if (!NearlyEqual(generatedSettings.GravityX, vanillaSettings.GravityX) ||
            !NearlyEqual(generatedSettings.GravityY, vanillaSettings.GravityY) ||
            !NearlyEqual(generatedSettings.GravityZ, vanillaSettings.GravityZ))
        {
            issues.Add(NewIssue(
                "Gravity",
                $"{vanillaSettings.GravityX:G7},{vanillaSettings.GravityY:G7},{vanillaSettings.GravityZ:G7}",
                $"{generatedSettings.GravityX:G7},{generatedSettings.GravityY:G7},{generatedSettings.GravityZ:G7}"));
        }

        if (!NearlyEqual(generatedSettings.DampingPerSecond, vanillaSettings.DampingPerSecond))
        {
            issues.Add(NewIssue(
                "Damping",
                vanillaSettings.DampingPerSecond.ToString("G7", CultureInfo.InvariantCulture),
                generatedSettings.DampingPerSecond.ToString("G7", CultureInfo.InvariantCulture)));
        }

        var generatedConstraintSets = ConstraintSignature(generated, generatedIndex);
        var vanillaConstraintSets = ConstraintSignature(vanilla, vanillaIndex);
        if (!string.Equals(generatedConstraintSets, vanillaConstraintSets, StringComparison.Ordinal))
            issues.Add(NewIssue("Constraint sets", vanillaConstraintSets, generatedConstraintSets));

        var generatedConstraints = CollectConstraintValues(generated, generatedIndex);
        var vanillaConstraints = CollectConstraintValues(vanilla, vanillaIndex);
        foreach (var key in generatedConstraints.Keys.Union(vanillaConstraints.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
        {
            IReadOnlyList<float> generatedValues = generatedConstraints.GetValueOrDefault(key) ?? new List<float>();
            IReadOnlyList<float> vanillaValues = vanillaConstraints.GetValueOrDefault(key) ?? new List<float>();
            if (ValuesMatch(vanillaValues, generatedValues, 1.0f))
                continue;

            var ratio = CalculateMedianScale(vanillaValues, generatedValues);
            issues.Add(NewIssue(
                $"Constraint {key}",
                $"{vanillaValues.Count} value(s)",
                $"{generatedValues.Count} value(s), ratio={(ratio.HasValue ? ratio.Value.ToString("G7", CultureInfo.InvariantCulture) : "n/a")}"));
        }

        // Numerical particle and constraint checks alone can look perfect
        // while the runtime graph driving those values differs. This is most
        // visible with short earrings: the game moves them, but not like the
        // vanilla reference. Fingerprint the remaining authored data so a
        // matched pair points to the missing conversion layer instead of
        // being incorrectly blamed on mass scaling.
        AddSignatureDifference("Particle state", DescribeParticles(generated, generatedIndex), DescribeParticles(vanilla, vanillaIndex));
        AddSignatureDifference("Skeleton local pose", DescribeBones(generated, generatedIndex), DescribeBones(vanilla, vanillaIndex));
        AddSignatureDifference("Active collider data", DescribeColliders(generated, generatedIndex), DescribeColliders(vanilla, vanillaIndex));
        AddSignatureDifference("Runtime buffers/operators/states", DescribeRuntime(generated, generatedIndex), DescribeRuntime(vanilla, vanillaIndex));
        AddSignatureDifference("Simple mesh bone deformation", DescribeSimpleMeshDeformers(generated, generatedIndex), DescribeSimpleMeshDeformers(vanilla, vanillaIndex));
        AddSignatureDifference("Skinning/deformation input", DescribeSkinOperators(generated, generatedIndex), DescribeSkinOperators(vanilla, vanillaIndex));

        return issues;

        FreshConversionAuditIssue NewIssue(string category, string expected, string actual) =>
            new(sourceFile, clothName, category, expected, actual);

        void AddCountDifference(string category, int generatedCount, int vanillaCount)
        {
            if (generatedCount != vanillaCount)
                issues.Add(NewIssue(category, vanillaCount.ToString(CultureInfo.InvariantCulture), generatedCount.ToString(CultureInfo.InvariantCulture)));
        }

        void AddSignatureDifference(string category, string generatedSignature, string vanillaSignature)
        {
            if (!string.Equals(generatedSignature, vanillaSignature, StringComparison.Ordinal))
                issues.Add(NewIssue(category, vanillaSignature, generatedSignature));
        }
    }

    private static string DescribeParticles(HkclService document, int clothIndex) => Fingerprint(
        document.GetParticleRows(clothIndex)
            .OrderBy(particle => particle.Index)
            .Select(particle => string.Join('|',
                particle.Index.ToString(CultureInfo.InvariantCulture),
                particle.Fixed.ToString(),
                FormatFloat(particle.X), FormatFloat(particle.Y), FormatFloat(particle.Z), FormatFloat(particle.W),
                FormatFloat(particle.Mass), FormatFloat(particle.InverseMass),
                FormatFloat(particle.Radius), FormatFloat(particle.Friction),
                particle.CollisionMask.ToString(CultureInfo.InvariantCulture))));

    private static string DescribeBones(HkclService document, int clothIndex) => Fingerprint(
        document.GetBoneRows(clothIndex)
            .OrderBy(bone => bone.Index)
            .Select(bone => string.Join('|',
                bone.Index.ToString(CultureInfo.InvariantCulture), bone.Name, bone.ParentIndex.ToString(CultureInfo.InvariantCulture),
                FormatFloat(bone.X), FormatFloat(bone.Y), FormatFloat(bone.Z),
                FormatFloat(bone.RotationX), FormatFloat(bone.RotationY), FormatFloat(bone.RotationZ), FormatFloat(bone.RotationW),
                FormatFloat(bone.ScaleX), FormatFloat(bone.ScaleY), FormatFloat(bone.ScaleZ))));

    private static string DescribeColliders(HkclService document, int clothIndex) => Fingerprint(
        document.GetColliderRows(clothIndex)
            .OrderBy(collider => collider.Name, StringComparer.Ordinal)
            .ThenBy(collider => collider.Index)
            .Select(collider => string.Join('|',
                collider.Name, collider.ShapeType, collider.BoneName,
                FormatFloat(collider.StartX), FormatFloat(collider.StartY), FormatFloat(collider.StartZ),
                FormatFloat(collider.EndX), FormatFloat(collider.EndY), FormatFloat(collider.EndZ),
                FormatFloat(collider.Radius),
                FormatFloat(collider.PlaneNormalX), FormatFloat(collider.PlaneNormalY), FormatFloat(collider.PlaneNormalZ))));

    private static string DescribeRuntime(HkclService document, int clothIndex)
    {
        var runtime = document.GetClothRuntimeData(clothIndex);
        var lines = new List<string>();
        lines.AddRange(runtime.Buffers.OrderBy(buffer => buffer.Index).Select(buffer =>
            $"buffer|{buffer.Index}|{buffer.ClassName}|{buffer.Type}|{buffer.SubType}|{buffer.VertexCount}|{buffer.TriangleCount}|{string.Join(',', buffer.TriangleIndices)}"));
        lines.AddRange(runtime.TransformSets.OrderBy(set => set.Index).Select(set =>
            $"transformSet|{set.Index}|{set.Type}|{set.TransformCount}"));
        lines.AddRange(runtime.Operators.OrderBy(@operator => @operator.Index).Select(@operator =>
            $"operator|{@operator.Index}|{@operator.ClassName}|{@operator.InputBufferIndex}|{@operator.OutputBufferIndex}|" +
            $"{@operator.TransformSetIndex}|{@operator.VertexCount}|{@operator.StartVertexIn}|{@operator.StartVertexOut}|" +
            $"{@operator.GatherNormals}|{@operator.PartialGather}|{string.Join(',', @operator.VertexInputFromVertexOutput)}|" +
            $"{string.Join(',', @operator.VertexParticlePairs.Select(pair => $"{pair.VertexIndex}:{pair.ParticleIndex}"))}|" +
            $"{string.Join(';', @operator.SimulateConfigs.Select(config => $"{config.SubSteps}:{config.NumberOfSolveIterations}:{config.AdaptConstraintStiffness}:{string.Join(',', config.ConstraintExecution)}"))}"));
        lines.AddRange(runtime.States.OrderBy(state => state.Index).Select(state =>
            $"state|{state.Index}|{string.Join(',', state.OperatorIndices)}|{string.Join(',', state.BufferIndices)}|" +
            $"{string.Join(',', state.TransformSetIndices)}|{string.Join(',', state.SimulationIndices)}"));
        return Fingerprint(lines);
    }

    private static string DescribeSimpleMeshDeformers(HkclService document, int clothIndex) => Fingerprint(
        document.GetSimpleMeshBoneDeformData(clothIndex)
            .OrderBy(deformer => deformer.OperatorIndex)
            .Select(deformer =>
                $"{deformer.OperatorIndex}|{deformer.InputBufferIndex}|{deformer.OutputTransformSetIndex}|{deformer.BoneAxis}|" +
                $"{string.Join(',', deformer.TriangleBonePairs.Select(pair => $"{pair.BoneOffset}:{pair.TriangleOffset}"))}|" +
                $"{string.Join(';', deformer.LocalBoneTransforms.Select(DescribeMatrix))}"));

    private static string DescribeSkinOperators(HkclService document, int clothIndex) => Fingerprint(
        document.GetSkinOperatorData(clothIndex)
            .OrderBy(skin => skin.OperatorIndex)
            .Select(skin =>
                $"{skin.OperatorIndex}|{skin.ClassName}|{skin.OutputBufferIndex}|{skin.TransformSetIndex}|" +
                $"{string.Join(';', skin.BoneFromSkinMeshTransforms.Select(DescribeMatrix))}|{string.Join(',', skin.TransformSubset)}|" +
                $"{string.Join(',', skin.ControlBytes)}|{skin.StartVertexIndex}|{skin.EndVertexIndex}|{skin.BatchSizeSpu}|{skin.PartialWrite}|" +
                $"{string.Join(';', skin.BlendArrays.Select(DescribeBlendArray))}|" +
                $"{string.Join(';', skin.LocalPositionBlocks.Select(DescribePositionBlock))}|" +
                $"{string.Join(';', skin.LocalUnpackedPositionBlocks.Select(DescribePositionBlock))}"));

    private static string DescribeBlendArray(SkinBlendArrayData array) =>
        $"{array.MemberName}:{array.BlendCount}:{array.VertexCount}:{array.BoneCount}:" +
        string.Join('/', array.Blocks.Select(block =>
            $"{string.Join(',', block.VertexIndices)}>{string.Join(',', block.BoneIndices)}>{string.Join(',', block.BoneWeights)}"));

    private static string DescribePositionBlock(SkinPositionBlockData block) =>
        string.Join(',', block.Positions.Select(position =>
            $"{FormatFloat(position.X)}:{FormatFloat(position.Y)}:{FormatFloat(position.Z)}:{FormatFloat(position.W)}"));

    private static string DescribeMatrix(System.Numerics.Matrix4x4 matrix) => string.Join(',', new[]
    {
        matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44
    }.Select(FormatFloat));

    private static string FormatFloat(float value) => value.ToString("G9", CultureInfo.InvariantCulture);

    private static string Fingerprint(IEnumerable<string> lines)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static int GetTriangleCount(HkclService document, int clothIndex)
    {
        return Enumerable.Range(0, document.GetParticleRows(clothIndex).Count)
            .SelectMany(particleIndex => document.GetParticleRelationships(clothIndex, particleIndex))
            .Where(row => string.Equals(row.Kind, "Triangle", StringComparison.Ordinal))
            .Select(row => row.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static string ConstraintSignature(HkclService document, int clothIndex) => string.Join(
        ",",
        document.GetConstraintSetSummaries(clothIndex)
            .GroupBy(set => set.ClassName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:{group.Count()}"));

    private static bool NearlyEqual(float left, float right) =>
        Math.Abs(left - right) <= Math.Max(0.00001f, Math.Max(Math.Abs(left), Math.Abs(right)) * 0.005f);

    private static Dictionary<string, List<float>> CollectConstraintValues(HkclService document, int clothIndex)
    {
        var classNames = document.GetConstraintSetSummaries(clothIndex)
            .ToDictionary(set => set.Index, set => set.ClassName);
        var values = new Dictionary<string, List<float>>(StringComparer.Ordinal);
        var seenLinks = new HashSet<(int Set, int Link)>();
        var seenLocals = new HashSet<(int Set, int Local)>();

        for (var particleIndex = 0; particleIndex < document.GetParticleRows(clothIndex).Count; particleIndex++)
        {
            foreach (var relationship in document.GetParticleRelationships(clothIndex, particleIndex))
            {
                if (!classNames.TryGetValue(relationship.ConstraintSetIndex, out var className))
                    continue;

                if (relationship.LinkIndex >= 0 && seenLinks.Add((relationship.ConstraintSetIndex, relationship.LinkIndex)))
                {
                    AddConstraintValue(values, className, "restLength", relationship.RestLength);
                    AddConstraintValue(values, className, "bendMinLength", relationship.BendMinLength);
                    AddConstraintValue(values, className, "stretchMaxLength", relationship.StretchMaxLength);
                    AddConstraintValue(values, className, "stiffness", relationship.Stiffness);
                    AddConstraintValue(values, className, "bendStiffness", relationship.BendStiffness);
                    AddConstraintValue(values, className, "stretchStiffness", relationship.StretchStiffness);
                }

                if (relationship.LocalConstraintIndex >= 0 && seenLocals.Add((relationship.ConstraintSetIndex, relationship.LocalConstraintIndex)))
                    AddConstraintValue(values, className, "maximumDistance", relationship.MaximumDistance);
            }
        }

        foreach (var value in values.Values)
            value.Sort();
        return values;
    }

    private static void AddConstraintValue(Dictionary<string, List<float>> values, string className, string fieldName, float? value)
    {
        if (!value.HasValue)
            return;

        var key = $"{className}|{fieldName}";
        if (!values.TryGetValue(key, out var list))
        {
            list = new List<float>();
            values.Add(key, list);
        }

        list.Add(value.Value);
    }

    private static bool IsSoftCoefficient(string className, string fieldName) => className switch
    {
        "hclStandardLinkConstraintSet" => string.Equals(fieldName, "stiffness", StringComparison.Ordinal),
        "hclBendLinkConstraintSet" =>
            string.Equals(fieldName, "bendStiffness", StringComparison.Ordinal) ||
            string.Equals(fieldName, "stretchStiffness", StringComparison.Ordinal),
        "hclBendStiffnessConstraintSet" => string.Equals(fieldName, "bendStiffness", StringComparison.Ordinal),
        _ => false
    };

    private static bool ValuesMatch(IReadOnlyList<float> source, IReadOnlyList<float> target, float expectedScale)
    {
        if (source.Count != target.Count)
            return false;

        for (var index = 0; index < source.Count; index++)
        {
            var expected = source[index] * expectedScale;
            var tolerance = Math.Max(0.00001f, Math.Abs(expected) * 0.005f);
            if (Math.Abs(target[index] - expected) > tolerance)
                return false;
        }

        return true;
    }

    private static float? CalculateMedianScale(IReadOnlyList<float> source, IReadOnlyList<float> target)
    {
        if (source.Count != target.Count)
            return null;

        var scales = source
            .Select((value, index) => Math.Abs(value) < 0.000001f ? float.NaN : target[index] / value)
            .Where(value => !float.IsNaN(value) && !float.IsInfinity(value))
            .OrderBy(value => value)
            .ToArray();
        if (scales.Length == 0)
            return null;

        var middle = scales.Length / 2;
        return scales.Length % 2 == 0
            ? (scales[middle - 1] + scales[middle]) * 0.5f
            : scales[middle];
    }

    private static string StripPrefix(string name) => name.StartsWith("Link:", StringComparison.OrdinalIgnoreCase)
        ? name[5..]
        : name;
}

internal sealed record ConversionMatch(
    string SourceFile,
    string ClothName,
    int SourceDynamicParticleCount,
    int TargetDynamicParticleCount,
    float SourceTotalMass,
    float TargetTotalMass,
    float Scale,
    ConversionStructuralFingerprint Fingerprint);

internal sealed record ConstraintAuditReport(IReadOnlyList<ConstraintAuditField> Fields);

internal sealed record ConstraintAuditField(
    string SourceFile,
    string ClothName,
    string ClassName,
    string FieldName,
    int SourceCount,
    int TargetCount,
    float ExpectedScale,
    float? ActualScale,
    bool MatchesExpected);

internal sealed record FreshConversionAuditReport(
    int CandidateFiles,
    int GeneratedFiles,
    int MatchedCloths,
    IReadOnlyList<FreshConversionAuditIssue> Issues);

internal sealed record FreshConversionAuditIssue(
    string SourceFile,
    string ClothName,
    string Category,
    string Expected,
    string Actual);

internal sealed record ConversionStructuralFingerprint(
    int TotalParticleCount,
    int DynamicParticleCount,
    int FixedParticleCount,
    int TriangleCount,
    int ConstraintRecordCount,
    int ConstraintSetCount,
    int ColliderCount,
    int BoneCount,
    float AverageRadius,
    float AverageFriction,
    string ConstraintSignature)
{
    public static ConversionStructuralFingerprint Create(NativeBphclSimCloth simulation, int boneCount)
    {
        var dynamicParticles = simulation.Particles.Where(particle => !particle.Fixed).ToArray();
        var constraintSets = simulation.ConstraintSets;
        var records = constraintSets.Sum(set => set.RecordCount);
        var signature = string.Join(",", constraintSets
            .GroupBy(set => set.ClassName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:{group.Sum(set => set.RecordCount)}"));

        return new ConversionStructuralFingerprint(
            simulation.Particles.Count,
            dynamicParticles.Length,
            simulation.Particles.Count - dynamicParticles.Length,
            simulation.TriangleCount,
            records,
            constraintSets.Count,
            simulation.CollidableItemIndices.Count,
            boneCount,
            dynamicParticles.Length == 0 ? 0.0f : dynamicParticles.Average(particle => particle.Radius),
            dynamicParticles.Length == 0 ? 0.0f : dynamicParticles.Average(particle => particle.Friction),
            signature);
    }
}
