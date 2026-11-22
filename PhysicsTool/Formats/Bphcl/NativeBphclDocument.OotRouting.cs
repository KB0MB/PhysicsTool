namespace HKCLTool;

public sealed partial class NativeBphclDocument
{
    internal IReadOnlyDictionary<int, bool> ReadOotShadowPositionFlags(int clothIndex)
    {
        var result = new Dictionary<int, bool>();
        foreach (var buffer in Cloths[clothIndex].BufferDefinitions.Where(b => b.ClassName == "hclShadowBufferDefinition"))
        {
            uint field = MemberAddress(GetItem(buffer.ItemIndex), "shadowPositions");
            if (field >= DataSection!.PayloadSize) throw new InvalidDataException("OoT shadow allocation flag exceeds DATA.");
            result.Add(buffer.Index, ReadByte(field) != 0);
        }
        return result;
    }

    internal IReadOnlyList<byte> ReadOotSidednessSetup(int simulationItemIndex)
    {
        uint field = MemberAddress(GetItem(simulationItemIndex), "setupCollidableSidedness");
        if ((ulong)field + 16 > (ulong)DataSection!.PayloadSize)
            throw new InvalidDataException("OoT collider sidedness field exceeds DATA.");
        var item = ReadArrayItem(field);
        if (item == null && (IsRelocationOffset(field) || ReadUInt32(field) != 0 || ReadUInt32(field + 8) != 0))
            throw new InvalidDataException("OoT collider sidedness has an invalid ITEM reference.");
        if (item != null && (ulong)item.DataOffset + item.Count > (ulong)DataSection!.PayloadSize)
            throw new InvalidDataException("OoT collider sidedness exceeds DATA.");
        return ReadByteArray(item);
    }
}
