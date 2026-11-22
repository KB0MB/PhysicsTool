using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using HKCLTool;

internal static class OotPinchDecodingChecks
{
    // A small in-memory DATA/ITEM fixture with the executable-reflected layout.
    // No vanilla file in the supplied OoT corpus contains an anti-pinch set.
    internal static void Run()
    {
        var bytes = new byte[512];
        "20250200"u8.CopyTo(bytes);
        var tag = new NativeBphclSection("TAG0", 0, 0, 512, 0, 512);
        tag.Children.Add(new("SDKV", 0, 0, 8, 0, 8));
        tag.Children.Add(new("DATA", 0, 16, 256, 16, 256));
        var header = new NativeBphclHeader(0, 0, 0, 3, 0, 0, 0, 512, 512, 0, 0);
        var constructor = typeof(NativeBphclDocument).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var doc = (NativeBphclDocument)constructor.Invoke([bytes, header, tag]);
        void Set(string name, object value) => typeof(NativeBphclDocument).GetProperty(name)!.SetValue(doc, value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16 + at), value);
        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16 + at), value);
        void F32(int at, float value) => U32(at, BitConverter.SingleToUInt32Bits(value));
        var items = new NativeBphclItem[] { new(0x10000001, 1, 0, 1), new(0x20000002, 2, 128, 1) };
        Set(nameof(doc.Items), items);
        Set(nameof(doc.TypeNames), new[] { "", "hclAntiPinchConstraintSet", "hclAntiPinchConstraintSet::PerParticle", "hkUint16", "hkReal", "hkInt32", "hkArray" });
        var types = new NativeBphclTypeDefinition[] {
            new(1, 0, 0, 72, 8, [new("perParticleData", 40, 6, 0), new("toAnimPeriod", 56, 4, 0),
                new("toSimPeriod", 60, 4, 0), new("toSimMaxDistance", 64, 4, 0), new("referenceMeshBufferIdx", 68, 5, 0)]),
            new(2, 0, 0, 4, 2, [new("particleIndex", 0, 3, 0), new("referenceVertex", 2, 3, 0)]) };
        Set(nameof(doc.TypeDefinitions), types);
        Set(nameof(doc.InternalPatches), new NativeBphclPatch[] { new(6, [40]) });
        var set = new NativeBphclConstraintSet(0, "Recovery", "hclAntiPinchConstraintSet", 3, 0x13, 0, 0, true,
            [], [], [], [], [], [], new Dictionary<string, double>());
        var sim = new NativeBphclSimCloth(0, "Test", 0, 0, null!,
            [new(0, Vector4.Zero, false, 1, 1, 0, 0, 0, 0), new(1, Vector4.Zero, false, 1, 1, 0, 0, 0, 0)],
            [], [], [set], [], [], null!, null!, []);
        Set(nameof(doc.Cloths), new NativeBphclCloth[] { new(0, "Test", 0, 0, [sim],
            [new(3, 0, 0, "hclScratchBufferDefinition", "", "Reference", 0, 0, 10, 0)], [], [], [], [], [], []) });
        U32(40, 1); F32(56, .25f); F32(60, .5f); F32(64, 1); U32(68, 3); U16(128, 1); U16(130, 9);
        var decoded = doc.ReadAntiPinchSets(0).Single();
        if (decoded.Particles.Single() != new NativeAntiPinchParticle(1, 9) || decoded.ReferenceBufferIndex != 3 ||
            decoded.ToAnimationPeriod != .25f || decoded.ToSimulationPeriod != .5f || decoded.ToSimulationMaxDistance != 1)
            throw new Exception("Native anti-pinch decoding lost its particle/reference indices or periods");
        void Reject(Action change, Action restore)
        {
            bool rejected = false;
            try { change(); try { doc.ReadAntiPinchSets(0); } catch (InvalidDataException) { rejected = true; } }
            finally { restore(); }
            if (!rejected) throw new Exception("Malformed native anti-pinch data accepted");
        }
        Reject(() => U16(128, 2), () => U16(128, 1));
        Reject(() => U16(130, 10), () => U16(130, 9));
        Reject(() => U32(68, 4), () => U32(68, 3));
        Reject(() => F32(56, float.NaN), () => F32(56, .25f));
        Reject(() => F32(60, -1), () => F32(60, .5f));
        Reject(() => F32(64, float.PositiveInfinity), () => F32(64, 1));
        Reject(() => U32(40, 99), () => U32(40, 1));
        var original = items[1];
        Reject(() => items[1] = original with { Count = 3 }, () => items[1] = original);
        Reject(() => items[1] = original with { DataOffset = 254 }, () => items[1] = original);
        Reject(() => { items[1] = original with { Count = 2 }; U16(132, 1); U16(134, 9); }, () => items[1] = original);
        var recordType = types[1];
        Reject(() => types[1] = recordType with { Size = 8 }, () => types[1] = recordType);
        Console.WriteLine("PASS: native anti-pinch DATA/ITEM decoding and malformed array/index/period guards");

        var originalCloth = doc.Cloths[0];
        Set(nameof(doc.Items), items.Append(new NativeBphclItem(0x10000007, 7, 160, 1)).ToArray());
        Set(nameof(doc.TypeDefinitions), types.Append(new NativeBphclTypeDefinition(7, 0, 0, 16, 8,
            [new("shadowPositions", 8, 3, 0)])).ToArray());
        Set(nameof(doc.Cloths), new[] { originalCloth with {
            BufferDefinitions = [.. originalCloth.BufferDefinitions, new(4, 2, 160, "hclShadowBufferDefinition", "", "", 0, 0, 0, 0)],
            States = [new(0, 0, "Default", [], [new(3, 0, 0, 0, 0, false, 4)], [], [])] } });
        U32(68, 4); bytes[16 + 168] = 0;
        if (doc.ReadAntiPinchSets(0).Single().Particles.Single().ReferenceVertex != 9)
            throw new Exception("Anti-pinch did not use the borrowed shadow's effective vertex count");
        U16(130, 10);
        try { doc.ReadAntiPinchSets(0); throw new Exception("Borrowed anti-pinch reference exceeded backing storage"); }
        catch (InvalidDataException) { }
        U16(130, 9); U32(68, 3);

        items = [new(0x10000001, 1, 0, 1), new(0x20000002, 2, 128, 2), new(0x10000003, 3, 160, 1)];
        Set(nameof(doc.Items), items);
        Set(nameof(doc.TypeDefinitions), new NativeBphclTypeDefinition[] {
            new(1, 0, 0, 56, 8, [new("setupCollidableSidedness", 40, 6, 0)]),
            new(3, 0, 0, 16, 8, [new("shadowPositions", 8, 2, 0)]) });
        Set(nameof(doc.Cloths), new[] { doc.Cloths[0] with { BufferDefinitions =
            [new(1, 2, 160, "hclShadowBufferDefinition", "", "", 0, 0, 0, 0)] } });
        U32(40, 1); bytes[16 + 128] = 0; bytes[16 + 129] = 1; bytes[16 + 168] = 1;
        if (!doc.ReadOotSidednessSetup(0).SequenceEqual(new byte[] { 0, 1 }) || !doc.ReadOotShadowPositionFlags(0)[1])
            throw new Exception("OoT setup flags were not decoded through reflected members");
        bytes[16 + 168] = 0;
        if (doc.ReadOotShadowPositionFlags(0)[1]) throw new Exception("Borrowed shadow positions treated as owned");
        void RejectRouting(Action action)
        {
            try { action(); } catch (InvalidDataException) { return; }
            throw new Exception("Malformed OoT routing metadata accepted");
        }
        original = items[1]; items[1] = original with { DataOffset = 255 };
        RejectRouting(() => doc.ReadOotSidednessSetup(0)); items[1] = original;
        U32(40, 99); RejectRouting(() => doc.ReadOotSidednessSetup(0)); U32(40, 1);
        items[2] = items[2] with { DataOffset = 255 };
        RejectRouting(() => doc.ReadOotShadowPositionFlags(0));
        Console.WriteLine("PASS: OoT shadow-allocation/sidedness decoding and invalid DATA/ITEM guards");
    }
}
