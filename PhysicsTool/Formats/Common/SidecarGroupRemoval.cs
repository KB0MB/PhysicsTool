using System;
using System.Collections.Generic;
using System.IO;

namespace HKCLTool;

internal static class SidecarGroupRemoval
{
    // AAMP object names encode their table index, so removal must rename survivors too.
    internal static Dictionary<int, int> Compact(
        BotwAampWriter.AampListNode? list, string prefix, Func<int, bool> remove)
    {
        var map = new Dictionary<int, int>();
        if (list == null)
            return map;
        var survivors = new List<BotwAampWriter.AampObjectNode>();
        for (var index = 0; index < list.Objects.Count; index++)
        {
            if (remove(index))
                continue;
            map.Add(index, survivors.Count);
            var copy = new BotwAampWriter.AampObjectNode($"{prefix}_{survivors.Count}");
            foreach (var parameter in list.Objects[index].Parameters)
                copy.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(
                    parameter.NameHash ?? BotwAampWriter.Crc32(parameter.Name),
                    parameter.Type, parameter.RawBytes ?? parameter.ToBinary()));
            survivors.Add(copy);
        }
        list.Objects.Clear();
        list.Objects.AddRange(survivors);
        return map;
    }

    internal static int Remap(int index, IReadOnlyDictionary<int, int> map)
    {
        if (index < 0)
            return index;
        return map.TryGetValue(index, out var result) ? result
            : throw new InvalidDataException("A remaining helper record references the removed group or an invalid table index. No changes were applied.");
    }
}
