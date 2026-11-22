using System.Numerics;
using HKCLTool;

internal static class OotReferenceBufferChecks
{
    internal static void Run()
    {
        static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        static void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { return; }
            throw new Exception(message);
        }
        var definitions = Enumerable.Range(0, 4).Select(i => new NativeBphclBufferDefinition(i, i, 0,
            i == 3 ? "hclSimClothBufferDefinition" : "hclScratchBufferDefinition", "", "", 0, 0, 8, 0)).ToArray();
        IReadOnlyList<ParticlePreviewBoneWeight> Weights(int bone) => new ParticlePreviewBoneWeight[]
        { new() { BoneIndex = bone, Weight = 1, ReferencePosition = new Vector3(bone, 0, 0) } };
        var first = Weights(1);
        var second = Weights(2);
        var replacement = Weights(3);
        var buffers = new OotReferenceBuffers(definitions);
        buffers.Skin(0, new Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> { [2] = first, [3] = second });
        buffers.Copy(0, 1, 2, 4, 2);
        buffers.Skin(2, new Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> { [1] = replacement });
        buffers.Gather(1, 2, [5, -1, 4, -32768]);
        Require(ReferenceEquals(buffers.Resolve(2, 0), second) && ReferenceEquals(buffers.Resolve(2, 2), first), "Copy offsets / gather direction");
        Require(ReferenceEquals(buffers.Resolve(2, 1), replacement), "Negative gather entries must preserve previous writes");
        Reject(() => buffers.Resolve(2, 3), "Negative gather entry invented a reference");
        buffers.Move(2, [new(0, 6)]);
        buffers.Skin(2, new Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> { [0] = replacement });
        Require(ReferenceEquals(buffers.Attachments[6], second), "MoveParticles must capture references at its execution point");
        Require(ReferenceEquals(buffers.Resolve(2, 0), replacement), "Later skin write did not replace reference");
        buffers.Move(2, [new(0, 6)]);
        Require(ReferenceEquals(buffers.Attachments[6], replacement), "Later MoveParticles must win");
        Require(ReferenceEquals(buffers.Resolve(1, 5), second), "Destination overwrite changed copied source");
        Reject(() => buffers.Copy(0, 1, 7, 0, 2), "Copy source range overflow");
        Reject(() => buffers.Copy(0, 1, 2, 7, 2), "Copy destination range overflow");
        Reject(() => buffers.Copy(0, 1, -1, 0, 1), "Negative copy range");
        Reject(() => buffers.Copy(0, 1, 2, 0, int.MaxValue), "Copy count overflow");
        Reject(() => buffers.Gather(0, 1, [8]), "Gather source range overflow");
        Reject(() => buffers.Gather(0, 1, new short[9]), "Gather destination range overflow");
        buffers.Copy(0, 0, 2, 3, 3);
        Require(ReferenceEquals(buffers.Resolve(0, 5), first), "Overlapping copy did not execute forward");
        buffers.Gather(1, 1, [4, 0, 1]);
        Require(ReferenceEquals(buffers.Resolve(1, 2), first), "In-place gather did not observe earlier writes");
        Reject(() => buffers.Copy(3, 1, 0, 0, 1), "Simulation buffer treated as scratch");
        Reject(() => buffers.Copy(0, 3, 2, 0, 1), "Simulation output write accepted");
        Reject(() => buffers.Resolve(99, 0), "Missing buffer accepted");
        Reject(() => buffers.Copy(0, 1, 0, 0, 1), "Unwritten source treated as rest pose");
        Require(first[0].ReferencePosition == Vector3.UnitX && second[0].ReferencePosition == new Vector3(2, 0, 0), "Routing mutated skin source data");
        NativeBphclClothStateBufferAccess Route(uint source, uint destination) => new(source, 0, 0, 0, 0, false, destination);
        foreach (bool ownsPositions in new[] { false, true })
        {
            var shadowDefinitions = definitions.ToArray();
            shadowDefinitions[1] = shadowDefinitions[1] with { ClassName = "hclShadowBufferDefinition", VertexCount = ownsPositions ? 8u : 0u };
            var alias = new OotReferenceBuffers(shadowDefinitions, [Route(0, 1)], new Dictionary<int, bool> { [1] = ownsPositions });
            alias.Skin(0, new Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> { [0] = first, [1] = second });
            Require(ReferenceEquals(alias.Resolve(1, 1), second), "Shadow did not share position storage");
            alias.Copy(1, 0, 0, 1, 3);
            Require(ReferenceEquals(alias.Resolve(1, 3), first), "Aliased forward copy used stale source storage");
            alias.Move(1, [new(3, 6)]);
            alias.Skin(0, new Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> { [3] = replacement });
            Require(ReferenceEquals(alias.Attachments[6], first), "Aliased later write changed captured attachment");
            Reject(() => alias.Resolve(1, 8), "Borrowed shadow bounds were ignored");
        }
        void Write(OotReferenceBuffers target, int buffer, IReadOnlyList<ParticlePreviewBoneWeight> value) =>
            target.Skin(buffer, new Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> { [0] = value });
        var chain = new OotReferenceBuffers(definitions, [Route(0, 1), Route(1, 2)]);
        Write(chain, 0, first); Write(chain, 1, second);
        Require(ReferenceEquals(chain.Resolve(0, 0), first) && ReferenceEquals(chain.Resolve(2, 0), second),
            "Owned-buffer chain was incorrectly flattened");
        var cycle = new OotReferenceBuffers(definitions, [Route(0, 1), Route(1, 0)]);
        Write(cycle, 0, first); Write(cycle, 1, second);
        Require(ReferenceEquals(cycle.Resolve(0, 0), first) && ReferenceEquals(cycle.Resolve(1, 0), second),
            "One-hop cyclic redirects did not keep physical storage separate");
        var shared = new OotReferenceBuffers(definitions, [Route(0, 2), Route(1, 2)]);
        Write(shared, 0, first); shared.Move(1, [new(0, 6)]); Write(shared, 1, second);
        Require(ReferenceEquals(shared.Resolve(0, 0), second) && ReferenceEquals(shared.Attachments[6], first),
            "Shared destination did not preserve ordered writes and attachment snapshots");
        var borrowedDefinitions = definitions.ToArray();
        for (int i = 1; i <= 2; i++) borrowedDefinitions[i] = borrowedDefinitions[i] with { ClassName = "hclShadowBufferDefinition", VertexCount = 0 };
        var flags = new Dictionary<int, bool> { [1] = false, [2] = false };
        var borrowedChain = new OotReferenceBuffers(borrowedDefinitions, [Route(0, 1), Route(1, 2)], flags);
        Write(borrowedChain, 0, first);
        Require(ReferenceEquals(borrowedChain.Resolve(2, 0), first), "Ordered shadow borrowing failed");
        var reversed = new OotReferenceBuffers(borrowedDefinitions, [Route(1, 2), Route(0, 1)], flags);
        Write(reversed, 0, first);
        Reject(() => reversed.Resolve(1, 0), "An early empty borrow was retroactively filled");
        Require(ReferenceEquals(reversed.Resolve(0, 0), first), "Later physical borrow was lost");
        borrowedDefinitions[2] = definitions[2] with { VertexCount = 2 };
        var sharedBorrow = new OotReferenceBuffers(borrowedDefinitions, [Route(0, 1), Route(2, 1)], flags);
        Write(sharedBorrow, 0, first); Write(sharedBorrow, 2, second);
        Require(sharedBorrow.VertexCount(2) == 8 && ReferenceEquals(sharedBorrow.Resolve(0, 0), second),
            "A later source replaced the first borrowed position component");
        var unequal = definitions.ToArray(); unequal[2] = unequal[2] with { VertexCount = 2 };
        var bounded = new OotReferenceBuffers(unequal, [Route(0, 2), Route(1, 2)]);
        Require(bounded.VertexCount(0) == 2, "Alias did not use destination capacity");
        Reject(() => bounded.Resolve(0, 2), "Alias used the logical source's larger capacity");
        var last = new OotReferenceBuffers(definitions, [Route(0, 1), Route(0, 2), Route(0, 0)]);
        Write(last, 0, replacement);
        Require(ReferenceEquals(last.Resolve(2, 0), replacement), "Identity access reset an earlier redirect");
        Reject(() => last.Resolve(1, 0), "Redirect rewrote the earlier target's storage");
        Reject(() => new OotReferenceBuffers(definitions, [Route(0, 99)]), "Missing shadow accepted");
        Reject(() => new OotReferenceBuffers(definitions, [Route(0, 3)]), "Simulation shadow accepted");
        var independent = new OotReferenceBuffers(definitions);
        Reject(() => independent.Resolve(0, 2), "Buffer storage leaked between instances");
        Console.WriteLine("PASS: OoT ordered copy/gather, one-hop chains/cycles, shared/borrowed shadows, setup order, snapshots and bounds");
    }
}
