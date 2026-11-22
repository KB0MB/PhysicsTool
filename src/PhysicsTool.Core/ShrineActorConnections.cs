namespace PhysicsTool.Core;

internal sealed class ShrineActorConnections
{
    private readonly Dictionary<ulong, ShrineActor[]> _actors;

    internal ShrineActorConnections(ShrineSceneDocument scene) =>
        _actors = scene.Actors.GroupBy(a => a.Hash).ToDictionary(g => g.Key, g => g.ToArray());

    internal ShrineActor? Resolve(ShrineActor source, ulong hash)
    {
        if (!_actors.TryGetValue(hash, out var candidates)) return null;
        var local = candidates.Where(a => a.SourceScene == source.SourceScene && a.ParentIndex == source.ParentIndex).ToArray();
        if (local.Length == 1) return local[0];
        // Paired static/dynamic files can reference each other, but repeated
        // merged scenes must never connect to another instance of themselves.
        var siblings = candidates.Where(a => a.ParentIndex == source.ParentIndex).ToArray();
        return siblings.Length == 1 ? siblings[0] : null;
    }

    internal IReadOnlyList<(int A, int B)> Lines(IEnumerable<ShrineActor> actors, IReadOnlySet<int> visible, IReadOnlySet<int> selected)
    {
        var lines = new HashSet<(int, int)>();
        var groups = new HashSet<(string, int, ShrineActorGroup)>();
        void Add(int a, int b)
        {
            if (a != b && visible.Contains(a) && visible.Contains(b)) lines.Add(a < b ? (a, b) : (b, a));
        }
        foreach (var actor in actors)
        {
            foreach (var link in actor.Links)
                if (Resolve(actor, link.Destination) is { } target) Add(actor.Index, target.Index);
            foreach (var group in actor.Groups)
            {
                if (!group.Members.Contains(actor.Hash) || !groups.Add((actor.SourceScene, actor.ParentIndex, group))) continue;
                var members = group.Members.Select(h => Resolve(actor, h)).OfType<ShrineActor>()
                    .Where(a => visible.Contains(a.Index)).ToArray();
                if (members.Length < 2) continue;
                // A star shows shared group membership, not an invented gameplay
                // sequence, and avoids quadratic line counts for large groups.
                var anchor = members.FirstOrDefault(a => selected.Contains(a.Index))
                    ?? members.FirstOrDefault(a => a.Name != "Area") ?? members[0];
                foreach (var member in members) Add(anchor.Index, member.Index);
            }
        }
        return lines.OrderBy(l => l.Item1).ThenBy(l => l.Item2).ToArray();
    }
}
