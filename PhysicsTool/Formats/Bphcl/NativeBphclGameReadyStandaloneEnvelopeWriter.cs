using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HKCLTool;

public static class NativeBphclGameReadyStandaloneEnvelopeWriter
{
	private static readonly byte[] SdkVersion = "20220100"u8.ToArray();

	private const int HeaderSize = 48;

	public static byte[] CreateEmptyForValidation()
	{
		return Create(new byte[16], CreateMinimalTypeSection(), Array.Empty<NativeBphclItem>(), Array.Empty<NativeBphclPatch>());
	}

	public static byte[] Create(ReadOnlySpan<byte> dataPayload, byte[] typeSection, IReadOnlyList<NativeBphclItem> items, IReadOnlyList<NativeBphclPatch> patches, byte[]? aampPayload = null)
	{
		if (typeSection == null)
		{
			throw new ArgumentNullException("typeSection");
		}
		if (typeSection.Length < 8 || !typeSection.AsSpan(4, 4).SequenceEqual("TYPE"u8))
		{
			throw new ArgumentException("A standalone BPHCL TYPE section must include its TYPE header.", "typeSection");
		}
		if (aampPayload != null && aampPayload.Length > 0 && !aampPayload.AsSpan(0, Math.Min(4, aampPayload.Length)).SequenceEqual("AAMP"u8))
		{
			throw new ArgumentException("Standalone BPHCL AAMP data must begin with AAMP.", "aampPayload");
		}
		List<byte> list = new List<byte>();
		list.AddRange(BuildSection("SDKV", SdkVersion));
		list.AddRange(BuildSection("DATA", dataPayload));
		list.AddRange(typeSection);
		list.AddRange(BuildIndexSection(items, patches));
		byte[] array = BuildSection("TAG0", CollectionsMarshal.AsSpan(list), 0u);
		byte[] array2 = aampPayload ?? Array.Empty<byte>();
		int num = Align(48 + array.Length, 8);
		byte[] array3 = new byte[checked(num + array2.Length)];
		WritePhiveHeader(array3, array.Length, num, array2.Length);
		array.CopyTo(array3, 48);
		if (array2.Length != 0)
		{
			array2.CopyTo(array3, num);
		}
		return array3;
	}

	private static byte[] CreateMinimalTypeSection()
	{
		List<byte> list = new List<byte>();
		list.AddRange(BuildSection("TST1", ReadOnlySpan<byte>.Empty));
		list.AddRange(BuildSection("FST1", ReadOnlySpan<byte>.Empty));
		list.AddRange(BuildSection("TNA1", new byte[1] { 1 }));
		list.AddRange(BuildSection("TBDY", ReadOnlySpan<byte>.Empty));
		list.AddRange(BuildSection("THSH", new byte[1] { 0 }));
		list.AddRange(BuildSection("TPTR", ReadOnlySpan<byte>.Empty));
		return BuildSection("TYPE", CollectionsMarshal.AsSpan(list), 0u);
	}

	private static byte[] BuildIndexSection(IReadOnlyList<NativeBphclItem> items, IReadOnlyList<NativeBphclPatch> patches)
	{
		byte[] array = new byte[checked(items.Count * 12)];
		for (int i = 0; i < items.Count; i++)
		{
			NativeBphclItem nativeBphclItem = items[i];
			int num = i * 12;
			BinaryPrimitives.WriteUInt32LittleEndian(array.AsSpan(num, 4), nativeBphclItem.Flags);
			BinaryPrimitives.WriteUInt32LittleEndian(array.AsSpan(num + 4, 4), nativeBphclItem.DataOffset);
			BinaryPrimitives.WriteUInt32LittleEndian(array.AsSpan(num + 8, 4), nativeBphclItem.Count);
		}
		List<byte> list = new List<byte>();
		foreach (NativeBphclPatch patch in patches)
		{
			WriteUInt32(list, patch.TypeIndex);
			WriteUInt32(list, checked((uint)patch.Offsets.Count));
			foreach (uint offset in patch.Offsets)
			{
				WriteUInt32(list, offset);
			}
		}
		List<byte> list2 = new List<byte>();
		list2.AddRange(BuildSection("ITEM", array));
		list2.AddRange(BuildSection("PTCH", CollectionsMarshal.AsSpan(list)));
		return BuildSection("INDX", CollectionsMarshal.AsSpan(list2), 0u);
	}

	private static byte[] BuildSection(string signature, ReadOnlySpan<byte> payload, uint chunkKind = 1u)
	{
		if (signature.Length != 4)
		{
			throw new ArgumentException("BPHCL section signatures must have four ASCII characters.", "signature");
		}
		int num = checked(payload.Length + 8);
		if (num > 1073741823)
		{
			throw new InvalidDataException("Standalone BPHCL section exceeds the 30-bit size limit.");
		}
		byte[] array = new byte[num];
		if (chunkKind > 3)
		{
			throw new ArgumentOutOfRangeException("chunkKind");
		}
		BinaryPrimitives.WriteUInt32BigEndian(array.AsSpan(0, 4), (chunkKind << 30) | (uint)num);
		Encoding.ASCII.GetBytes(signature, array.AsSpan(4, 4));
		payload.CopyTo(array.AsSpan(8));
		return array;
	}

	private static void WritePhiveHeader(byte[] bytes, int tagSize, int parameterOffset, int parameterSize)
	{
		"Phive\0"u8.CopyTo(bytes);
		bytes[6] = 1;
		bytes[7] = 0;
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), 65279);
		bytes[10] = 3;
		bytes[11] = 3;
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), 48u);
		checked
		{
			BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), (uint)parameterOffset);
			BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), (uint)bytes.Length);
			BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24, 4), (uint)(parameterOffset - 48));
			BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), (uint)parameterSize);
			BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32, 4), 0u);
		}
	}

	private static int Align(int value, int alignment)
	{
		return (value + alignment - 1) & -alignment;
	}

	private static void WriteUInt32(List<byte> destination, uint value)
	{
		destination.Add((byte)value);
		destination.Add((byte)(value >> 8));
		destination.Add((byte)(value >> 16));
		destination.Add((byte)(value >> 24));
	}
}
