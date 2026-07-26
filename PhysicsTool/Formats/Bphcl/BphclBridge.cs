using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HKCLTool;

public sealed class BphclDocumentSummary
{
    // The UI summary stays lightweight, while the native document provides
    // read-only geometry for the shared viewport/editor.
    internal NativeBphclDocument? NativeDocument { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public int FileSize { get; init; }
    public int ClothCount { get; init; }
    public int ColliderCount { get; init; }
    public int SkeletonCount { get; init; }
    public bool AampPresent { get; init; }
    public JArray Cloths { get; init; } = new();
    public JArray Skeletons { get; init; } = new();
    public JObject Raw { get; init; } = new();
}

public static class BphclBridge
{
    public static BphclDocumentSummary Load(string path)
    {
        return CreateSummary(NativeBphclDocument.Open(path), Path.GetFullPath(path));
    }

    internal static BphclDocumentSummary CreateSummary(NativeBphclDocument native, string sourcePath)
    {
        var aampClothNames = native.Aamp.ClothEntries
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);
        var unregisteredTag0Cloths = native.Cloths
            .Select(cloth => cloth.Name)
            .Where(name => !aampClothNames.Contains(name))
            .ToArray();
        var aampOnlyEntries = native.Aamp.ClothEntries
            .Select(entry => entry.Name)
            .Where(name => !native.Cloths.Any(cloth => string.Equals(cloth.Name, name, StringComparison.Ordinal)))
            .ToArray();
        var skeletons = new JArray(native.Skeletons.Select(skeleton => new JObject
        {
            ["index"] = skeleton.Index,
            ["name"] = skeleton.Name,
            ["boneCount"] = skeleton.BoneCount,
            ["bones"] = new JArray(skeleton.Bones.Select(bone => new JObject
            {
                ["index"] = bone.Index,
                ["name"] = bone.Name,
                ["parentIndex"] = bone.ParentIndex
            }))
        }));

        var cloths = new JArray(native.Cloths.Select(cloth => new JObject
        {
            ["index"] = cloth.Index,
            ["name"] = cloth.Name,
            ["class"] = native.GetTypeName(native.GetItem(cloth.ItemIndex).TypeIndex) ?? "hclClothData",
            ["particleCount"] = cloth.SimCloths.Sum(simulation => simulation.Particles.Count),
            // Kept for the existing details panel. Exact native counts will
            // follow when those lists are surfaced by the document adapter.
            ["operatorCount"] = 0,
            ["stateCount"] = 0,
            ["bufferCount"] = 0,
            ["transformSetCount"] = 0,
            ["skeleton"] = cloth.Index < skeletons.Count
                ? skeletons[cloth.Index]!.DeepClone()
                : null
        }));

        var raw = new JObject
        {
            ["format"] = "BPHCL",
            ["sourcePath"] = sourcePath,
            ["fileSize"] = native.Bytes.Length,
            ["clothCount"] = cloths.Count,
            ["colliderCount"] = native.CollidableCount,
            ["skeletonCount"] = skeletons.Count,
            ["aampPresent"] = native.Header.ParameterSize > 0,
            ["aamp"] = new JObject
            {
                ["readable"] = native.Aamp.IsReadable,
                ["diagnostic"] = native.Aamp.Diagnostic,
                ["clothMeshEntries"] = new JArray(native.Aamp.ClothEntries.Select(entry => new JObject
                {
                    ["index"] = entry.Index,
                    ["name"] = entry.Name
                })),
                ["allTag0ClothsRegistered"] = native.Aamp.IsReadable && unregisteredTag0Cloths.Length == 0,
                ["unregisteredTag0Cloths"] = new JArray(unregisteredTag0Cloths),
                ["aampOnlyEntries"] = new JArray(aampOnlyEntries),
            },
            ["nativeLayout"] = new JObject
            {
                ["tag0Size"] = native.TagFile.Size,
                ["dataSize"] = native.DataSection?.Size,
                ["typeSize"] = native.TypeSection?.Size,
                ["indexSize"] = native.IndexSection?.Size,
                ["itemCount"] = native.Items.Count,
                ["patchGroupCount"] = native.InternalPatches.Count,
                ["relocationCount"] = native.RelocationOffsets.Count(),
                ["typeCount"] = Math.Max(0, native.TypeNames.Count - 1),
                ["rootVariants"] = new JArray(native.RootVariants.Select(variant => new JObject
                {
                    ["index"] = variant.Index,
                    ["name"] = variant.Name,
                    ["className"] = variant.ClassName,
                    ["itemIndex"] = variant.ObjectItemIndex,
                    ["dataOffset"] = variant.ObjectDataOffset,
                    ["itemType"] = variant.ObjectTypeName
                }))
            }
        };

        return new BphclDocumentSummary
        {
            NativeDocument = native,
            SourcePath = sourcePath,
            FileSize = native.Bytes.Length,
            ClothCount = cloths.Count,
            ColliderCount = native.CollidableCount,
            SkeletonCount = skeletons.Count,
            AampPresent = native.Header.ParameterSize > 0,
            Cloths = cloths,
            Skeletons = skeletons,
            Raw = raw
        };
    }

    public static BphclDocumentSummary Save(string inputPath, string outputPath)
    {
        var document = NativeBphclDocument.Open(inputPath);
        NativeBphclWriter.SaveRebuiltCopy(document, outputPath);
        return Load(outputPath);
    }

    public static BphclDocumentSummary Save(NativeBphclDocument document, string outputPath)
    {
        // Particle-only edits have already updated the native DATA payload.
        // Rebuild the native container from that document so the saved file
        // continues through the same BPHCL packer used by merge/delete.
        NativeBphclWriter.SaveRebuiltCopy(document, outputPath);
        return Load(outputPath);
    }

    public static BphclDocumentSummary MergeTypeMetadataTest(
        string inputPath,
        string referencePath,
        string outputPath)
    {
        var target = NativeBphclDocument.Open(inputPath);
        var reference = NativeBphclDocument.Open(referencePath);
        NativeBphclWriter.SaveWithMergedTypeMetadata(target, reference, outputPath);
        return Load(outputPath);
    }

    public static BphclDocumentSummary DeleteCloth(string inputPath, string outputPath, int clothIndex)
    {
        var target = NativeBphclDocument.Open(inputPath);
        NativeBphclWriter.SaveWithoutCloth(target, clothIndex, outputPath);
        return Load(outputPath);
    }

    public static BphclDocumentSummary PruneUnreferencedColliders(string inputPath, string outputPath)
    {
        var target = NativeBphclDocument.Open(inputPath);
        NativeBphclWriter.SaveWithUnreferencedCollidersPruned(target, outputPath);
        return Load(outputPath);
    }

    public static BphclDocumentSummary StripLinkBonePrefixes(string inputPath, string outputPath)
    {
        var target = NativeBphclDocument.Open(inputPath);
        NativeBphclWriter.SaveWithoutLinkBonePrefixes(target, outputPath);
        return Load(outputPath);
    }

    public static BphclDocumentSummary RenameCloth(string inputPath, string outputPath, int clothIndex, string name)
    {
        var target = NativeBphclDocument.Open(inputPath);
        NativeBphclWriter.SaveRenamedCloth(target, clothIndex, name, outputPath);
        return Load(outputPath);
    }

    public static BphclDocumentSummary MergeCloth(string inputPath, string referencePath, string outputPath, int clothIndex)
    {
        var target = NativeBphclDocument.Open(inputPath);
        var reference = NativeBphclDocument.Open(referencePath);
        NativeBphclWriter.SaveMergedCloth(target, reference, clothIndex, outputPath);

        // The legacy bridge still provides the UI summary for now. The binary
        // merge above is fully native and no longer relies on reusable slots.
        return Load(outputPath);
    }

    public static BphclDocumentSummary DuplicateCloth(string inputPath, string renamedDonorPath, string outputPath, int clothIndex)
    {
        var target = NativeBphclDocument.Open(inputPath);
        var donor = NativeBphclDocument.Open(renamedDonorPath);
        NativeBphclWriter.SaveDuplicatedCloth(target, donor, clothIndex, outputPath);
        return Load(outputPath);
    }

    public static BphclDocumentSummary ReplaceCloth(
        string inputPath,
        string referencePath,
        string outputPath,
        int targetClothIndex,
        int sourceClothIndex)
    {
        var target = NativeBphclDocument.Open(inputPath);
        var reference = NativeBphclDocument.Open(referencePath);
        NativeBphclWriter.SaveReplacedCloth(
            target,
            reference,
            targetClothIndex,
            sourceClothIndex,
            outputPath);
        return Load(outputPath);
    }

    public static JObject ExportSummary(BphclDocumentSummary summary)
    {
        return new JObject
        {
            ["format"] = "BPHCLTool.Readable",
            ["version"] = 1,
            ["notes"] = "BPHCL support is currently a native BPHCL open/save adapter. Editing will use a shared logical cloth model later.",
            ["bphcl"] = summary.Raw
        };
    }

}

