namespace HKCLTool;

public sealed partial class HkclService
{
    private void ValidateOotPreviewSkinMapping(int clothIndex)
    {
        var cloth = _bphcl!.NativeDocument!.Cloths[clothIndex];
        foreach (var skin in GetSkinOperatorData(clothIndex).Where(s =>
            s.ClassName == "hclObjectSpaceSkinPOperator" && s.TransformSubset.Count == 0))
        {
            // 0x7101d99a00 selects the full transform set, in skeleton order,
            // when transformSubset is empty. Keep the serialized subset empty.
            var transforms = cloth.TransformSetDefinitions.ElementAtOrDefault(skin.TransformSetIndex);
            int boneCount = GetBoneRows(clothIndex).Count;
            ValidateOotDirectSkinMapping(skin, checked((int)(transforms?.TransformCount ?? 0)), boneCount);
        }
    }

    internal static void ValidateOotDirectSkinMapping(SkinOperatorData skin, int transformCount, int boneCount)
    {
        if (transformCount <= 0 || transformCount != boneCount || skin.BoneFromSkinMeshTransforms.Count != boneCount)
            throw new InvalidDataException("OoT direct skin mapping needs one bind matrix per skeleton transform.");
        foreach (var (array, blockIndex, _) in GetSkinBlockOrder(skin.BlendArrays, skin.ControlBytes))
        {
            var block = array.Blocks[blockIndex];
            for (int slot = 0; slot < array.VertexCount && slot < block.VertexIndices.Count; slot++)
            {
                if (block.VertexIndices[slot] == ushort.MaxValue) continue;
                for (int influence = 0; influence < array.BlendCount; influence++)
                {
                    int packed = slot * array.BlendCount + influence;
                    if (packed >= block.BoneIndices.Count || block.BoneIndices[packed] >= boneCount)
                        throw new InvalidDataException("OoT direct skin mapping references a missing skeleton transform.");
                }
            }
        }
    }
}
