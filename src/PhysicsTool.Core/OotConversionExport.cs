using HKCLTool;

namespace PhysicsTool.Core;

public static class OotConversionExport
{
    public const string OlderGameWarning = "OoT aerodynamic settings have no equivalent in BotW or TotK and are not transferred. " +
        "Bone names are preserved, not retargeted. Helper bones and actor wind parameters are separate files. Test the result in-game.";

    public static HkclService NormalizeForOlderGame(HkclService source)
    {
        if (!source.IsOot) return source.CopyForConversion();
        var bytes = OotClothConversion.Convert(source.CaptureBphclNativeDocument()!, OotClothConversion.CreateTotkReference());
        var converted = new HkclService(); converted.RestoreState("BPHCL:" + Convert.ToBase64String(bytes)); return converted;
    }

    public static int Export(HkclService document, string romfs, string path, string? sourcePath = null, bool targetOot = true)
    {
        var source = document.CaptureBphclNativeDocument();
        if (source == null && document.HasDocument && !document.IsReadOnlyExternal && !document.IsHkrg && !document.IsHelperBoneSidecar)
            source = NativeBphclTypeTable.CompleteTotkConversionLayouts(NativeBphclDocument.Parse(
                NativeBphclStandaloneGraphWriter.Write(HkclToBphclGameReadyGraphBuilder.Build(document)
                    with { NativeTypeSection = NativeBphclGameReadyReflectionCatalog.TypeSection })));
        if (source == null || (targetOot ? source.SdkVersion != "20220100" : !source.IsOot))
            throw new InvalidOperationException("Select cloth from a different supported source game.");
        var output = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.GetExtension(output).Equals(".bphcl", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a .bphcl output file.");
        if ((sourcePath ?? document.SourcePath) is { } original && output.Equals(Path.GetFullPath(original), comparison))
            throw new InvalidOperationException("Choose an output file different from the source.");
        if (!string.IsNullOrWhiteSpace(romfs) && output.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(romfs)) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Export outside the reference dump to keep the original OoT files unchanged.");
        var reference = targetOot ? OotClothConversion.ReadReference(source, romfs) : OotClothConversion.CreateTotkReference();
        var bytes = OotClothConversion.Convert(source, reference);
        // All structural validation is complete before AtomicFile replaces an output.
        AtomicFile.WriteAllBytes(output, bytes);
        return source.Cloths.Count;
    }
}
