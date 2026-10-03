using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Buffers.Binary;
using System.Text;

namespace HKCLTool;

internal static class ActorCatalogChecks
{
    internal static void Run(string root, string output)
    {
        Directory.CreateDirectory(output);
        ApplicationConfiguration.Initialize(); AppLocalization.Initialize();
        CheckCacheInvalidation(output);
        var catalog = new BotwActorCatalog();
        var clock = Stopwatch.StartNew();
        var result = catalog.Scan(new[] { root }, null, CancellationToken.None);
        var scanTime = clock.Elapsed.TotalSeconds;
        File.WriteAllText(Path.Combine(output, "catalog.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        if (result.Actors.Count == 0) throw new InvalidOperationException("No physics actors found.");
        if (result.Actors.Any(a => !a.HasCloth && !a.HasSupport)) throw new InvalidOperationException("Non-physics actor included.");
        clock.Restart();
        var cached = catalog.Scan(new[] { root }, null, CancellationToken.None);
        var cachedTime = clock.Elapsed.TotalSeconds;
        if (!result.Actors.SequenceEqual(cached.Actors) || !result.Warnings.SequenceEqual(cached.Warnings))
            throw new InvalidOperationException("Cached catalog differs from first scan.");
        var reopened = new BotwActorCatalog().Scan(new[] { root }, null, CancellationToken.None);
        if (!result.Actors.SequenceEqual(reopened.Actors)) throw new InvalidOperationException("Persistent actor cache differs.");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var rejected = false;
        try { catalog.Scan(new[] { root }, null, canceled.Token); }
        catch (OperationCanceledException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Scan cancellation ignored.");
        foreach (var kind in new[] { 1, 2, 3 })
        {
            var filtered = BotwActorBrowserDialog.Match(result.Actors, "", kind).ToArray();
            if (filtered.Any(a => kind == 1 ? !a.HasCloth : kind == 2 ? !a.HasSupport : !a.HasCloth || !a.HasSupport))
                throw new InvalidOperationException("Physics filter is incorrect.");
        }
        if (BotwActorBrowserDialog.Match(result.Actors, "ZELDA", 0).Any(a => !a.Name.Contains("zelda", StringComparison.OrdinalIgnoreCase) && !a.Source.Contains("zelda", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Actor search is incorrect.");
        foreach (var size in new[] { new Size(850, 600), new Size(620, 380) })
        {
            using var dialog = new BotwActorBrowserDialog(catalog, new[] { root }, scanOnShow: false);
            dialog.SetResults(result); dialog.ClientSize = size; dialog.Show(); Application.DoEvents();
            using var image = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(output, $"browser-{size.Width}.png"));
            if (dialog.SelectedActor == null) throw new InvalidOperationException("Browser has no selection.");
            dialog.Close();
        }
        File.WriteAllText(Path.Combine(output, "result.txt"),
            $"PASS: {result.Actors.Count} actors, {result.Actors.Count(a => a.IsSwitch)} Switch, {result.Actors.Count(a => !a.IsSwitch)} Wii U, " +
            $"{result.Actors.Count(a => a.ArchiveEntry != null)} embedded, {result.Warnings.Count} warnings.\n" +
            $"First scan {scanTime:0.00}s, cached scan {cachedTime:0.00}s. Search, filters, cancellation, persistent cache consistency/invalidation, embedded discovery and browser layouts passed.");
    }

    private static void CheckCacheInvalidation(string output)
    {
        var root = Path.Combine(output, "synthetic");
        var actors = Path.Combine(root, "Actor", "Pack"); var packs = Path.Combine(root, "Pack");
        Directory.CreateDirectory(actors); Directory.CreateDirectory(packs);
        var path = Path.Combine(actors, "Test.bactorpack");
        File.WriteAllBytes(path, Sarc(true, ("Physics/Cloth/Test.hkcl", new byte[] { 1 })));
        File.WriteAllBytes(Path.Combine(actors, "NoPhysics.bactorpack"), Sarc(true, ("Dummy.txt", new byte[] { 1 })));
        File.WriteAllBytes(Path.Combine(packs, "TitleBG.pack"), Sarc(false,
            ("Actor/Pack/Embedded.bactorpack", Sarc(false, ("Physics/SupportBone/Test.bphyssb", new byte[] { 1 })))));
        var cache = Path.Combine(output, "synthetic-cache.json");
        var catalog = new BotwActorCatalog(cache);
        var first = catalog.Scan(new[] { root }, null, CancellationToken.None);
        if (first.Actors.Count != 2 || !first.Actors.Any(a => a.ArchiveEntry != null && !a.IsSwitch && a.HasSupport) ||
            !first.Actors.Any(a => a.Name == "Test" && a.HasCloth && !a.HasSupport)) throw new InvalidOperationException("Synthetic discovery failed.");
        File.WriteAllBytes(path, Sarc(true, ("Physics/SupportBone/Changed.bphyssb", new byte[] { 1, 2, 3, 4 })));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
        var second = new BotwActorCatalog(cache).Scan(new[] { root }, null, CancellationToken.None);
        var changed = second.Actors.Single(a => a.Name == "Test");
        if (changed.HasCloth || !changed.HasSupport) throw new InvalidOperationException("Changed actor retained stale cached metadata.");
        var missing = catalog.Scan(new[] { Path.Combine(root, "Missing") }, null, CancellationToken.None);
        if (missing.Actors.Count != 0 || missing.Warnings.Count == 0) throw new InvalidOperationException("Missing folder was not reported.");
    }

    private static byte[] Sarc(bool littleEndian, params (string Name, byte[] Data)[] entries)
    {
        var names = new List<byte>(); var nameOffsets = new List<int>();
        foreach (var entry in entries)
        {
            nameOffsets.Add(names.Count); names.AddRange(Encoding.UTF8.GetBytes(entry.Name)); names.Add(0);
            while (names.Count % 4 != 0) names.Add(0);
        }
        var sfnt = 0x20 + entries.Length * 16;
        var dataOffset = (sfnt + 8 + names.Count + 15) & ~15;
        var bytes = new byte[dataOffset + entries.Sum(e => e.Data.Length)];
        void U16(int at, ushort value) { if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value); else BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), value); }
        void U32(int at, uint value) { if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value); else BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), value); }
        "SARC"u8.CopyTo(bytes); U16(4, 0x14); U16(6, 0xfeff); U32(8, (uint)bytes.Length); U32(12, (uint)dataOffset); U16(16, 0x100);
        "SFAT"u8.CopyTo(bytes.AsSpan(0x14)); U16(0x18, 12); U16(0x1a, (ushort)entries.Length); U32(0x1c, 0x65);
        "SFNT"u8.CopyTo(bytes.AsSpan(sfnt)); U16(sfnt + 4, 8); names.ToArray().CopyTo(bytes, sfnt + 8);
        var offset = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            var node = 0x20 + i * 16; U32(node + 4, 0x01000000u | (uint)(nameOffsets[i] / 4));
            U32(node + 8, (uint)offset); entries[i].Data.CopyTo(bytes, dataOffset + offset); offset += entries[i].Data.Length; U32(node + 12, (uint)offset);
        }
        return bytes;
    }
}
