using System.Buffers.Binary;
using static PhysicsTool.Core.BphscInstanceEditor;

namespace PhysicsTool.Core;

internal readonly record struct BphscActorCopy(ulong Source, ulong Hash, uint SrtHash);

internal sealed partial class BphscDocument
{
    internal BphscDocument EditActors(IReadOnlySet<ulong> deleted, IReadOnlyList<BphscActorCopy> copies)
    {
        if (deleted.Count == 0 && copies.Count == 0) return Parse(_bytes);
        if (Water.Count != 0 || ExternalMeshCount != 0)
            throw new NotSupportedException("Actor duplication and deletion are not supported in compounds with water or external meshes yet.");
        var links = ActorLinks.Where(l => !deleted.Contains(l.Hash)).Select(l => (Source: l, Hash: l.Hash, Srt: (uint?)null)).ToList();
        foreach (var copy in copies)
        {
            var source = ActorLinks.SingleOrDefault(l => l.Hash == copy.Source);
            if (source != null) links.Add((source, copy.Hash, copy.SrtHash));
        }
        if (links.Count > 65535 || links.Any(l => l.Hash == 0) || links.Select(l => l.Hash).Distinct().Count() != links.Count)
            throw new InvalidOperationException("Collision actor identifiers must be unique, with at most 65,535 entries.");
        foreach (var link in ActorLinks.Where(l => deleted.Contains(l.Hash) || copies.Any(c => c.Source == l.Hash)))
            if (link.GroupId != 0 || link.BodyIndex != 0)
                throw new NotSupportedException("This actor belongs to a moving collision group. Duplicate or delete support for grouped collision is not available yet.");

        var entries = BodyEntries().ToArray();
        var owners = new Dictionary<(int Slot, int Instance), uint>();
        var references = new Dictionary<int, List<(int Slot, int Instance, byte[] Bytes)>>();
        var usedReferences = new HashSet<uint>();
        foreach (var link in ActorLinks)
        {
            var refs = new List<(int, int, byte[])>(); references.Add(link.Index, refs);
            if (link.FirstInstance == uint.MaxValue) continue;
            if (link.EndInstance < link.FirstInstance || link.EndInstance >= U32(_image + 0x4014)) throw new InvalidDataException("Invalid actor collision range.");
            for (uint i = link.FirstInstance; i <= link.EndInstance; i++)
            {
                int at = checked(I32(28) + (int)i * 20);
                var slots = Enumerable.Range(0, entries.Length).Where(s => _bytes[entries[s] + 2] == link.BodyIndex && _bytes[entries[s] + 3] == _bytes[at + 5]).ToArray();
                if (slots.Length != 1 || !usedReferences.Add(i)) throw new NotSupportedException("Shared or ambiguous collision references cannot be resized.");
                int slot = slots[0], instance = U16(at + 10);
                if (U32(at) != i || U16(at + 8) != link.Index || !owners.TryAdd((slot, instance), i))
                    throw new NotSupportedException("Collision references do not have independent actor ownership.");
                refs.Add((slot, instance, _bytes.AsSpan(at, 20).ToArray()));
            }
        }
        if (usedReferences.Count != I32(_image + 0x4014)) throw new NotSupportedException("Unowned collision references cannot be resized.");
        var instances = Enumerable.Range(0, CompoundCount).ToDictionary(i => i, _ => new List<Instance>());
        var newLinks = new List<byte[]>(); var newRefs = new List<byte[]>();
        for (int i = 0; i < links.Count; i++)
        {
            var link = links[i]; var record = _bytes.AsSpan(_links + link.Source.Index * 32, 32).ToArray();
            Put64(record, 8, unchecked((long)link.Hash));
            if (link.Srt is { } srt) Put32(record, 16, unchecked((int)srt));
            var refs = references[link.Source.Index]; int first = newRefs.Count;
            foreach (var (slot, instance, bytes) in refs)
            {
                int newInstance = instances[slot].Count;
                if (newInstance >= 65535) throw new NotSupportedException("Too many collision instances in one body.");
                if (newRefs.Count >= 65535) throw new NotSupportedException("A compound cannot exceed 65,535 shape references.");
                instances[slot].Add(new(instance, checked((ushort)newRefs.Count)));
                var r = (byte[])bytes.Clone(); Put32(r, 0, newRefs.Count);
                BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(8), checked((ushort)i));
                BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(10), checked((ushort)newInstance));
                newRefs.Add(r);
            }
            Put32(record, 0, refs.Count == 0 ? -1 : first); Put32(record, 4, refs.Count == 0 ? -1 : newRefs.Count - 1);
            newLinks.Add(record);
        }
        var sections = Sections.ToDictionary(s => s.Index, s => ExtractSection(s.Index));
        for (int slot = 0; slot < CompoundCount; slot++)
        {
            // Validate ownership against the original shape array, including deleted shapes.
            var section = sections[slot]; int data = -1;
            for (int at = 8; at < section.Length;)
            {
                int size = (int)(BinaryPrimitives.ReadUInt32BigEndian(section.AsSpan(at)) & 0x3fffffff);
                if (size < 8 || at + size > section.Length) break;
                if (section.AsSpan(at + 4, 4).SequenceEqual("DATA"u8)) { data = at + 8; break; }
                at += size;
            }
            if (data < 0) throw new InvalidDataException("Missing collision DATA.");
            int count = BphscInstanceEditor.I32(section, data + 80), start = checked(data + 72 + (int)I64(section, data + 72));
            for (int i = 0; i < count; i++)
                if (!owners.TryGetValue((slot, i), out uint reference) || BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(start + i * 64 + 44)) != reference)
                    throw new NotSupportedException("Unowned collision shapes cannot be resized.");
            sections[slot] = BphscInstanceEditor.Rebuild(section, instances[slot]);
        }
        var hashes = new byte[links.Count * 16]; var indices = new byte[links.Count * 4]; var occupied = new bool[links.Count * 2];
        for (int i = 0; i < links.Count; i++)
        {
            int slot = (int)(links[i].Hash % (ulong)occupied.Length);
            while (occupied[slot]) slot = (slot + 1) % occupied.Length;
            occupied[slot] = true; Put64(hashes, slot * 8, unchecked((long)links[i].Hash));
            BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(slot * 2), checked((ushort)i));
        }
        var image = _bytes.AsSpan(_image, ImageSize).ToArray();
        Put32(image, 0x4010, links.Count); Put32(image, 0x4014, newRefs.Count);
        var regions = Enumerable.Range(0, 8).Select(i => _bytes.AsSpan(I32(12 + i * 4), I32(396 + i * 4)).ToArray()).ToArray();
        regions[0] = image; regions[1] = newLinks.SelectMany(b => b).ToArray(); regions[2] = hashes; regions[3] = indices; regions[4] = newRefs.SelectMany(b => b).ToArray();
        var header = _bytes.AsSpan(0, HeaderSize).ToArray();
        using var output = new MemoryStream(); output.Write(header);
        void Write(int offsetField, int? sizeField, byte[] bytes)
        {
            while (output.Position % 16 != 0) output.WriteByte(0);
            Put32(header, offsetField, checked((int)output.Position));
            if (sizeField is { } size) Put32(header, size, bytes.Length);
            output.Write(bytes);
        }
        for (int i = 0; i < 8; i++) Write(12 + i * 4, 396 + i * 4, regions[i]);
        for (int i = 0; i < 3; i++) Write(44 + i * 4, null, _bytes.AsSpan(I32(44 + i * 4), checked((int)U64(I32(40) + 32 + i * 8))).ToArray());
        foreach (var section in Sections) Write(56 + section.Index * 4, 440 + section.Index * 4, sections[section.Index]);
        // Empty section pointers must not retain offsets into the old file.
        for (int i = 0; i < 85; i++) if (!sections.ContainsKey(i)) Put32(header, 56 + i * 4, 0);
        output.Position = 0; output.Write(header);
        return Parse(output.ToArray());
    }
}
