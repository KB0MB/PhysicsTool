using HKCLTool;

namespace PhysicsTool.Core;

internal enum ActorExportKind { Bfres, Cloth, Bones, Bphysics }
internal sealed record ActorExportEntry(BotwActorResources Owner, string Resource, ActorExportKind Kind)
{
    public string FileName => Path.GetFileName(Resource.Replace('\\', '/')) + (Kind == ActorExportKind.Bfres ? ".bfres" : "");
    public string Label => Owner.ActorName + " / " + FileName;
}

internal static class ActorResourceExport
{
    internal static IReadOnlyList<ActorExportEntry> List(IEnumerable<BotwActorResources> owners)
    {
        var entries = new List<ActorExportEntry>();
        foreach (var owner in owners)
        {
            if (!owner.IsTotk && owner.PhysicsParameters != null && owner.PhysicsParametersPath is { } physicsPath)
                entries.Add(new(owner, physicsPath, ActorExportKind.Bphysics));
            entries.AddRange(owner.Models.Select(m => m.Resource).Distinct(StringComparer.Ordinal)
                .Select(r => new ActorExportEntry(owner, r, ActorExportKind.Bfres)));
            foreach (var path in owner.PhysicsFiles)
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension is ".hkcl" or ".bphcl") entries.Add(new(owner, path, ActorExportKind.Cloth));
                else if (extension is ".bphyssb" or ".bphhb") entries.Add(new(owner, path, ActorExportKind.Bones));
            }
        }
        return entries.DistinctBy(e => (e.Owner.SourcePath, e.Owner.SourceEntry, e.Resource, e.Kind)).ToArray();
    }

    internal static byte[] Read(ActorExportEntry entry)
    {
        var bytes = entry.Kind switch
        {
            ActorExportKind.Bfres => entry.Owner.ReadModel(entry.Resource),
            ActorExportKind.Bphysics => entry.Owner.PhysicsParameters?.ToArray(),
            _ => entry.Owner.Read(entry.Resource)
        };
        if (bytes == null) throw new FileNotFoundException("Actor resource is missing: " + entry.Resource);
        if (entry.Kind == ActorExportKind.Bfres)
        {
            if (bytes.AsSpan().StartsWith("Yaz0"u8)) bytes = BotwYaz0.Decode(bytes);
            if (!bytes.AsSpan().StartsWith("FRES"u8)) throw new InvalidDataException("The model did not decode to BFRES.");
        }
        return bytes;
    }

    internal static void Save(ActorExportEntry entry, string path) => AtomicFile.WriteAllBytes(path, Read(entry));
}
