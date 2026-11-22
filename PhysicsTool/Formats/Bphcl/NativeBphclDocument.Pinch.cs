namespace HKCLTool;

public sealed record NativeAntiPinchParticle(int ParticleIndex, int ReferenceVertex);
public sealed record NativeAntiPinchSet(int Index, int ReferenceBufferIndex, float ToAnimationPeriod,
    float ToSimulationPeriod, float ToSimulationMaxDistance, IReadOnlyList<NativeAntiPinchParticle> Particles);

public sealed partial class NativeBphclDocument
{
    public IReadOnlyList<NativeAntiPinchSet> ReadAntiPinchSets(int clothIndex)
    {
        var cloth = Cloths[clothIndex];
        var result = new List<NativeAntiPinchSet>();
        foreach (var sim in cloth.SimCloths)
        foreach (var set in sim.ConstraintSets.Where(s => s.IsAntiPinch))
        {
            if (!IsOot || set.ClassName != "hclAntiPinchConstraintSet" || set.ConstraintType != 0x13)
                throw new InvalidDataException("Unsupported native anti-pinch constraint layout.");
            var item = GetItem(set.ItemIndex);
            uint At(string name)
            {
                uint at = MemberAddress(item, name);
                uint size = name == "perParticleData" ? 16u : 4u;
                if ((ulong)at + size > (ulong)DataSection!.PayloadSize)
                    throw new InvalidDataException("Anti-pinch field exceeds DATA.");
                return at;
            }
            float Duration(string name)
            {
                float value = ReadSingle(At(name));
                if (!float.IsFinite(value) || value < 0)
                    throw new InvalidDataException($"Invalid anti-pinch {name}.");
                return value;
            }
            int bufferIndex = ReadInt32(At("referenceMeshBufferIdx"));
            var buffer = cloth.BufferDefinitions.SingleOrDefault(b => b.Index == bufferIndex)
                ?? throw new InvalidDataException("Anti-pinch references a missing buffer.");
            uint referenceCount = buffer.VertexCount;
            if (cloth.States.FirstOrDefault()?.BufferAccesses.Any(a => a.BufferIndex != a.ShadowBufferIndex) == true)
                referenceCount = new OotReferenceBuffers(cloth.BufferDefinitions, cloth.States[0].BufferAccesses,
                    ReadOotShadowPositionFlags(clothIndex)).VertexCount(bufferIndex);
            var particles = new List<NativeAntiPinchParticle>();
            uint arrayField = At("perParticleData");
            var particleArray = ReadArrayItem(arrayField);
            if (particleArray == null && (IsRelocationOffset(arrayField) || ReadUInt32(arrayField) != 0 || ReadUInt32(arrayField + 8) != 0))
                throw new InvalidDataException("Anti-pinch particle array has an invalid ITEM reference.");
            if (particleArray is { Count: > 0 } array)
            {
                var type = TypeDefinitions.Single(t => t.TypeIndex == array.TypeIndex);
                var particleMember = type.Members.SingleOrDefault(m => m.Name == "particleIndex");
                var vertexMember = type.Members.SingleOrDefault(m => m.Name == "referenceVertex");
                // Executable reflection 0x71048ca7e0: two hkUint16 fields, stride 4.
                if (particleMember?.Offset != 0 || vertexMember?.Offset != 2 ||
                    GetTypeName(particleMember.TypeIndex) != "hkUint16" || GetTypeName(vertexMember.TypeIndex) != "hkUint16" ||
                    type.Size != 4 || array.Count > sim.Particles.Count ||
                    (ulong)array.DataOffset + (ulong)array.Count * 4 > (ulong)DataSection!.PayloadSize)
                    throw new InvalidDataException("Invalid anti-pinch particle array.");
                var seen = new HashSet<int>();
                for (uint i = 0; i < array.Count; i++)
                {
                    uint at = checked(array.DataOffset + i * 4);
                    int particle = ReadUInt16(at), vertex = ReadUInt16(at + 2);
                    if (particle >= sim.Particles.Count || vertex >= referenceCount || !seen.Add(particle))
                        throw new InvalidDataException("Anti-pinch references an invalid or repeated particle/reference vertex.");
                    particles.Add(new(particle, vertex));
                }
            }
            result.Add(new(set.Index, bufferIndex, Duration("toAnimPeriod"), Duration("toSimPeriod"),
                Duration("toSimMaxDistance"), particles));
        }
        return result;
    }
}
