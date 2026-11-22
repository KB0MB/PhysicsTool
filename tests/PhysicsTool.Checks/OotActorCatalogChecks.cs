using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using BymlLibrary;
using HKCLTool;
using PhysicsTool.Core;

internal static class OotActorCatalogChecks
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static void Run(string root, string output)
    {
        Directory.CreateDirectory(output);
        Synthetic(Path.Combine(output, "synthetic-" + Guid.NewGuid().ToString("N")));
        var cache = Path.Combine(output, "catalog-" + Guid.NewGuid().ToString("N") + ".json");
        var clock = Stopwatch.StartNew();
        var first = OotActorResources.Scan(root, null, default, cache);
        var coldSeconds = clock.Elapsed.TotalSeconds;
        clock.Restart();
        var second = OotActorResources.Scan(root, null, default, cache);
        var warmSeconds = clock.Elapsed.TotalSeconds;
        Require(first.Actors.SequenceEqual(second.Actors) && first.Warnings.SequenceEqual(second.Warnings), "Cached scan differs from cold scan.");
        Require(first.Actors.Any(a => a.Name == "Player_Adult" && a.HasCloth && a.HasSupport), "Player_Adult missing physics.");
        Console.WriteLine($"PASS OoT catalog: {first.Actors.Count} actors, {first.Warnings.Count} warnings; cold {coldSeconds:0.00}s, warm {warmSeconds:0.00}s.");
    }

    private static void Synthetic(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Pack", "Actor"));
        Directory.CreateDirectory(Path.Combine(root, "Actor"));
        Directory.CreateDirectory(Path.Combine(root, "Phive", "Cloth"));
        var cache = Path.Combine(root, "catalog.json");
        var pack = Path.Combine(root, "Pack", "Actor", "Test.pack.zs");
        var cloth = Path.Combine(root, "Phive", "Cloth", "Test.Nin_Ounce_NVN2.bphcl");
        File.WriteAllBytes(cloth, [1]);
        File.WriteAllBytes(pack, Archive());
        Write("Actor/Test.engine__actor__ActorParam.bgyml", "Components: { PhysicsRef: physics.bgyml, ModelInfoRef: missing-model.bgyml }");
        Write("physics.bgyml", "ControllerSetPath: controller.bgyml");
        Write("controller.bgyml", "Cloth: [{FilePath: cloth.bgyml}]");
        Write("cloth.bgyml", "HktPath: Phive/Cloth/Test.hkt");
        Require(Scan().Actors.Single().HasCloth, "Cold discovery failed.");
        // Exclusive handles prove a warm scan does not reopen packs or read cloth contents.
        using (var lockedPack = File.Open(pack, FileMode.Open, FileAccess.Read, FileShare.None))
        using (var lockedCloth = File.Open(cloth, FileMode.Open, FileAccess.Read, FileShare.None))
            Require(Scan().Actors.Single().HasCloth, "Warm scan reread game assets.");
        Require(Scan().Warnings.Count == 0, "Catalog should not resolve models or animations.");

        Write("controller.bgyml", "HelperBoneList: [{FilePath: missing-helper.bgyml}]");
        Require(Scan().Actors.Count == 0, "Changed controller did not invalidate cache.");
        Write("missing-helper.bgyml", "Helpers: []");
        Require(Scan().Actors.Single().HasSupport, "New previously missing dependency was not discovered.");
        File.Delete(Path.Combine(root, "missing-helper.bgyml"));
        Require(Scan().Actors.Count == 0, "Deleted dependency retained cached actor.");

        var shared = Path.Combine(root, "Pack", "ResidentCommon.Test.pack.zs");
        File.WriteAllBytes(shared, Archive(("controller.bgyml", BymlBytes("Cloth: [{FilePath: cloth.bgyml}]"))));
        Require(Scan().Actors.Single().HasCloth, "New shared pack did not invalidate catalog.");
        File.WriteAllBytes(shared, Archive(("controller.bgyml", BymlBytes("Cloth: []"))));
        Require(Scan().Actors.Count == 0, "Changed shared pack did not invalidate catalog.");
        File.Delete(shared);
        Write("controller.bgyml", "Cloth: [{FilePath: cloth.bgyml}]");
        Require(Scan().Actors.Single().HasCloth, "Removed shared pack did not restore loose references.");

        File.WriteAllBytes(pack, Archive(("Actor/Test.engine__actor__ActorParam.bgyml", BymlBytes("Components: {}"))));
        Require(Scan().Actors.Count == 0, "Changed actor pack remained cached.");
        File.WriteAllBytes(pack, Archive());
        Require(Scan().Actors.Count == 1, "Restored actor pack remained cached.");
        var otherPack = Path.Combine(root, "Pack", "Actor", "Other.pack.zs");
        File.WriteAllBytes(otherPack, Archive(("Actor/Other.engine__actor__ActorParam.bgyml", BymlBytes("Components: {PhysicsRef: physics.bgyml}"))));
        Require(Scan().Actors.Count == 2, "New actor pack was not discovered.");
        File.Delete(pack);
        Require(Scan().Actors.Single().Name == "Other", "Deleted actor remained cached.");

        var before = File.ReadAllBytes(cache);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { OotActorResources.Scan(root, null, canceled.Token, cache); throw new Exception("Scan ignored cancellation."); }
        catch (OperationCanceledException) { }
        Require(before.SequenceEqual(File.ReadAllBytes(cache)), "Canceled scan changed cache.");
        File.WriteAllText(cache, "{broken");
        Require(Scan().Actors.Single().Name == "Other", "Corrupt cache did not rebuild.");
        var otherRoot = Path.Combine(root, "other-root");
        Directory.CreateDirectory(Path.Combine(otherRoot, "Pack", "Actor"));
        Directory.CreateDirectory(Path.Combine(otherRoot, "Phive", "Cloth"));
        File.WriteAllBytes(Path.Combine(otherRoot, "Phive", "Cloth", "Test.Nin_Ounce_NVN2.bphcl"), [1]);
        Require(OotActorResources.Scan(otherRoot, null, default, cache).Actors.Count == 0, "Cache leaked across roots.");
        Require(Scan().Actors.Count == 1, "Returning to original root did not rebuild.");
        Console.WriteLine("PASS OoT scan: metadata-only, persistent reuse, changed/missing/deleted dependencies, shared packs, actor additions/removals, cancellation, corrupt cache and root isolation.");

        BotwActorCatalogResult Scan() => OotActorResources.Scan(root, null, default, cache);
        void Write(string path, string yaml) => File.WriteAllBytes(Path.Combine(root, path), BymlBytes(yaml));
    }

    private static byte[] BymlBytes(string yaml) => Byml.FromText(yaml).ToBinary(Revrs.Endianness.Little, 7);

    private static byte[] Archive(params (string Name, byte[] Bytes)[] files)
    {
        var names = new List<byte>(); var offsets = new List<int>();
        foreach (var file in files)
        {
            offsets.Add(names.Count); names.AddRange(Encoding.UTF8.GetBytes(file.Name)); names.Add(0);
            while (names.Count % 4 != 0) names.Add(0);
        }
        int sfnt = 0x20 + files.Length * 16, data = sfnt + 8 + names.Count;
        var bytes = new byte[data + files.Sum(f => f.Bytes.Length)];
        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        "SARC"u8.CopyTo(bytes); U16(4, 0x14); U16(6, 0xfeff); U32(8, (uint)bytes.Length); U32(12, (uint)data); U16(16, 0x100);
        "SFAT"u8.CopyTo(bytes.AsSpan(0x14)); U16(0x18, 12); U16(0x1a, (ushort)files.Length); U32(0x1c, 0x65);
        "SFNT"u8.CopyTo(bytes.AsSpan(sfnt)); U16(sfnt + 4, 8); names.ToArray().CopyTo(bytes, sfnt + 8);
        int offset = 0;
        for (int i = 0; i < files.Length; i++)
        {
            int node = 0x20 + i * 16;
            U32(node + 4, 0x01000000u | (uint)(offsets[i] / 4)); U32(node + 8, (uint)offset);
            files[i].Bytes.CopyTo(bytes, data + offset); offset += files[i].Bytes.Length; U32(node + 12, (uint)offset);
        }
        return bytes;
    }
}
