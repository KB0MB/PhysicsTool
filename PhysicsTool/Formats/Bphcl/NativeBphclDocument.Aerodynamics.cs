namespace HKCLTool;

public sealed record OotAerodynamicsData(float LiftCoefficient, float DragCoefficient, float SurfaceDensity,
    bool UseSurfaceDensity, int ParticleCount, float ConstantLiftFactor, float ConstantDragFactor,
    bool UseConstantLiftFactor, bool UseConstantDragFactor, IReadOnlyList<float> LiftFactors, IReadOnlyList<float> DragFactors)
{
    public float DragFactor(int particle) => UseConstantDragFactor ? ConstantDragFactor : DragFactors[particle];

    internal void Validate(int particleCount)
    {
        if (ParticleCount != particleCount || particleCount < 0 ||
            new[] { LiftCoefficient, DragCoefficient, SurfaceDensity, ConstantLiftFactor, ConstantDragFactor }
                .Concat(LiftFactors).Concat(DragFactors).Any(v => !float.IsFinite(v) || v < 0) ||
            UseSurfaceDensity && SurfaceDensity <= 0 ||
            (!UseConstantLiftFactor && LiftFactors.Count != particleCount) ||
            (!UseConstantDragFactor && DragFactors.Count != particleCount) ||
            LiftFactors.Count != 0 && LiftFactors.Count != particleCount ||
            DragFactors.Count != 0 && DragFactors.Count != particleCount)
            throw new InvalidDataException("Invalid OoT aerodynamic coefficients, density or particle factors.");
    }
}

public sealed partial class NativeBphclDocument
{
    public OotAerodynamicsData ReadOotAerodynamics(int simulationItemIndex)
    {
        if (!IsOot) throw new InvalidOperationException("Aerodynamic data requires an OoT cloth.");
        var sim = GetItem(simulationItemIndex);
        var member = TypeDefinitions.Single(t => t.TypeIndex == sim.TypeIndex).Members.Single(m => m.Name == "aerodynamicsData");
        var layout = TypeDefinitions.Single(t => t.TypeIndex == member.TypeIndex);
        uint At(string name, string type, uint size)
        {
            var field = layout.Members.Single(m => m.Name == name);
            uint at = checked(sim.DataOffset + member.Offset + field.Offset);
            if (GetTypeName(field.TypeIndex) != type || (ulong)at + size > (ulong)DataSection!.PayloadSize)
                throw new InvalidDataException("Invalid OoT aerodynamic field: " + name);
            return at;
        }
        float Real(string name) => ReadSingle(At(name, "hkReal", 4));
        bool Flag(string name)
        {
            byte value = ReadByte(At(name, "bool", 1));
            if (value > 1) throw new InvalidDataException("Invalid OoT aerodynamic flag: " + name);
            return value != 0;
        }
        uint declaredCount = ReadUInt32(At("numParticles", "hkUint32", 4));
        uint particleCount = ReadArrayItem(MemberAddress(sim, "particleDatas"))?.Count ?? 0;
        if (declaredCount != particleCount || declaredCount > int.MaxValue)
            throw new InvalidDataException("OoT aerodynamic particle count does not match the simulation mesh.");
        int count = (int)declaredCount;
        float[] Factors(string name)
        {
            uint at = At(name, "hkArray", 16);
            var item = ReadArrayItem(at);
            if (item == null)
            {
                if (IsRelocationOffset(at) || ReadUInt32(at) != 0 || ReadUInt32(at + 8) != 0)
                    throw new InvalidDataException("Invalid OoT aerodynamic array reference.");
                return [];
            }
            if (GetTypeName(item.TypeIndex) != "hkReal" || item.Count != count ||
                (ulong)item.DataOffset + (ulong)item.Count * 4 > (ulong)DataSection!.PayloadSize)
                throw new InvalidDataException("Invalid OoT aerodynamic factor array.");
            return Enumerable.Range(0, count).Select(i => ReadSingle(item.DataOffset + (uint)i * 4)).ToArray();
        }
        var result = new OotAerodynamicsData(Real("liftCoefficient"), Real("dragCoefficient"), Real("surfaceDensity"),
            Flag("useSurfaceDensityWithSimpleWind"), count, Real("constantParticleLiftFactor"), Real("constantParticleDragFactor"),
            Flag("useConstantLiftFactor"), Flag("useConstantDragFactor"), Factors("perParticleLiftFactor"), Factors("perParticleDragFactor"));
        result.Validate(count);
        return result;
    }
}
