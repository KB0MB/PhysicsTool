using System.Buffers.Binary;
using System.Reflection;
using HKCLTool;

internal static class OotWindDecodingChecks
{
    internal static void Run()
    {
        var bytes = new byte[512]; "20250200"u8.CopyTo(bytes);
        var tag = new NativeBphclSection("TAG0", 0, 0, 512, 0, 512);
        tag.Children.Add(new("SDKV", 0, 0, 8, 0, 8)); tag.Children.Add(new("DATA", 0, 16, 256, 16, 256));
        var doc = (NativeBphclDocument)typeof(NativeBphclDocument).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single().Invoke([bytes, new NativeBphclHeader(0, 0, 0, 3, 0, 0, 0, 512, 512, 0, 0), tag]);
        void Set(string name, object value) => typeof(NativeBphclDocument).GetProperty(name)!.SetValue(doc, value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 16, 4), value);
        void Real(int at, float value) => U32(at, BitConverter.SingleToUInt32Bits(value));
        var items = new NativeBphclItem[] { new(0x10000001, 1, 0, 1), new(0x20000007, 7, 192, 2),
            new(0x20000003, 3, 160, 2), new(0x20000003, 3, 168, 2) };
        Set(nameof(doc.Items), items);
        Set(nameof(doc.TypeNames), new[] { "", "hclSimClothData", "hclAerodynamicSimClothData", "hkReal", "bool", "hkUint32", "hkArray", "ParticleData" });
        Set(nameof(doc.TypeDefinitions), new NativeBphclTypeDefinition[] {
            new(1, 0, 0, 128, 8, [new("aerodynamicsData", 64, 2, 0), new("particleDatas", 32, 6, 0)]),
            new(2, 0, 0, 72, 8, [new("liftCoefficient", 0, 3, 0), new("dragCoefficient", 4, 3, 0), new("surfaceDensity", 8, 3, 0),
                new("useSurfaceDensityWithSimpleWind", 12, 4, 0), new("numParticles", 16, 5, 0), new("perParticleLiftFactor", 24, 6, 0),
                new("perParticleDragFactor", 40, 6, 0), new("constantParticleLiftFactor", 56, 3, 0), new("constantParticleDragFactor", 60, 3, 0),
                new("useConstantLiftFactor", 64, 4, 0), new("useConstantDragFactor", 65, 4, 0)]) });
        Set(nameof(doc.InternalPatches), new NativeBphclPatch[] { new(6, [32, 88, 104]) });
        U32(32, 1); Real(64, .2f); Real(68, 1); Real(72, 2); bytes[16 + 76] = 1;
        U32(80, 2); U32(88, 2); U32(104, 3); Real(120, 1); Real(124, 1);
        Real(160, .25f); Real(164, .5f); Real(168, .75f); Real(172, 1.25f);
        var result = doc.ReadOotAerodynamics(0);
        if (!result.LiftFactors.SequenceEqual(new[] { .25f, .5f }) || !result.DragFactors.SequenceEqual(new[] { .75f, 1.25f }))
            throw new Exception("OoT aerodynamic ITEM counts/values were lost");
        void Reject(Action change, Action restore)
        {
            bool rejected = false;
            try { change(); try { doc.ReadOotAerodynamics(0); } catch (InvalidDataException) { rejected = true; } }
            finally { restore(); }
            if (!rejected) throw new Exception("Invalid OoT aerodynamic array accepted");
        }
        var original = items[3];
        Reject(() => items[3] = original with { Count = 1 }, () => items[3] = original);
        Reject(() => items[3] = original with { DataOffset = 254 }, () => items[3] = original);
        Reject(() => items[3] = original with { TypeIndex = 5 }, () => items[3] = original);
        Reject(() => U32(104, 99), () => U32(104, 3));
        Reject(() => Real(168, float.NaN), () => Real(168, .75f));
        Reject(() => bytes[16 + 129] = 2, () => bytes[16 + 129] = 0);
        Console.WriteLine("PASS OoT aerodynamic DATA/ITEM arrays: per-particle factors, serialized zero counts, bounds, type, pointer and flag guards.");
    }
}
