using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace HKCLTool;

public static class NativeBphclStandaloneGraphWriter
{
	private sealed record ClothScheduleReservations(ClothRootReservations Root, IReadOnlyList<DataBuilder.Reservation> TopLevelObjects);

	private sealed record ClothRootReservations(DataBuilder.Reservation Simulations, DataBuilder.Reservation Buffers, DataBuilder.Reservation TransformSets, DataBuilder.Reservation Operators, DataBuilder.Reservation States, DataBuilder.Reservation Name);

	private sealed record ClothRootData(DataBuilder.Allocation Data, DataBuilder.Allocation Simulations, DataBuilder.Allocation Buffers, DataBuilder.Allocation TransformSets, DataBuilder.Allocation Operators, DataBuilder.Allocation States);

	private sealed class RootScaffold
	{
		public uint ClothContainerDataOffset { get; }

		public uint ResourceContainerDataOffset { get; }

		public uint AnimationContainerDataOffset { get; set; }

		public DataBuilder.Reservation AnimationReservation { get; }

		public DataBuilder.Allocation CollidableStorage { get; }

		public DataBuilder.Allocation ClothStorage { get; }

		public DataBuilder.Allocation SkeletonStorage { get; set; }

		public RootScaffold(uint clothContainerDataOffset, uint resourceContainerDataOffset, DataBuilder.Reservation animationReservation, DataBuilder.Allocation collidableStorage, DataBuilder.Allocation clothStorage)
		{
			ClothContainerDataOffset = clothContainerDataOffset;
			ResourceContainerDataOffset = resourceContainerDataOffset;
			AnimationReservation = animationReservation;
			CollidableStorage = collidableStorage;
			ClothStorage = clothStorage;
		}
	}

	private sealed record ColliderReservation(DataBuilder.Reservation Object, DataBuilder.Reservation Shape, DataBuilder.Reservation Name);

	private sealed record RootVariant(string Name, string ClassName, int ClothItem, int SkeletonItem);

	private sealed record RuntimePipeline(IReadOnlyList<int> OperatorItems, IReadOnlyList<int> StateItems, int SimulateOperatorIndex);

	private sealed class DataBuilder
	{
		private sealed class AllocationInfo
		{
			public int ItemIndex { get; }

			public uint TypeIndex { get; set; }

			public uint Offset { get; }

			public int ByteLength { get; }

			public AllocationInfo(int itemIndex, uint typeIndex, uint offset, int byteLength)
			{
				ItemIndex = itemIndex;
				TypeIndex = typeIndex;
				Offset = offset;
				ByteLength = byteLength;
			}
		}

		public sealed class ObjectReservationScope : IDisposable
		{
			private DataBuilder? _owner;

			public ObjectReservationScope(DataBuilder owner)
			{
				_owner = owner;
			}

			public void Complete()
			{
				DataBuilder? owner = _owner;
				if (owner != null)
				{
					_owner = null;
					owner.CompleteObjectReservationScope();
				}
			}

			public void Dispose()
			{
				DataBuilder? owner = _owner;
				if (owner != null)
				{
					_owner = null;
					owner.AbandonObjectReservationScope();
				}
			}
		}

		public sealed record Reservation(int ItemIndex, uint TypeIndex);

		public sealed class Allocation
		{
			private readonly DataBuilder _owner;

			public int ItemIndex { get; }

			public uint DataOffset { get; }

			public int ByteLength { get; }

			public uint Count => _owner._items[ItemIndex].Count;

			internal Allocation(int itemIndex, uint dataOffset, int byteLength, DataBuilder owner)
			{
				ItemIndex = itemIndex;
				DataOffset = dataOffset;
				ByteLength = byteLength;
				_owner = owner;
			}

			public void SetByte(int relativeOffset, byte value)
			{
				if ((uint)relativeOffset >= (uint)ByteLength)
				{
					throw new ArgumentOutOfRangeException("relativeOffset");
				}
				_owner.GetSpan(DataOffset + checked((uint)relativeOffset), 1)[0] = value;
			}
		}

		private readonly List<byte> _data = new List<byte>(new byte[16]);

		private readonly List<NativeBphclItem> _items = new List<NativeBphclItem>();

		private readonly List<AllocationInfo> _allocations = new List<AllocationInfo>();

		private readonly List<uint> _referenceOffsets = new List<uint>();

		private readonly Dictionary<uint, uint> _referencePatchTypeOverrides = new Dictionary<uint, uint>();

		private readonly Dictionary<string, int> _stringItems = new Dictionary<string, int>(StringComparer.Ordinal);

		private Dictionary<uint, Queue<Reservation>>? _activeObjectReservations;

		public IReadOnlyList<NativeBphclItem> Items => _items;

		public DataBuilder()
		{
			uint typeIndex = NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkRootLevelContainer");
			_items.Add(new NativeBphclItem(0u, 0u, 0u, 0u));
			_items.Add(new NativeBphclItem(0x10000000 | typeIndex, typeIndex, 0u, 1u));
		}

		public Allocation AllocateObject(uint typeIndex, int size, int alignment = 8)
		{
			Dictionary<uint, Queue<Reservation>> activeObjectReservations = _activeObjectReservations;
			if (activeObjectReservations != null && activeObjectReservations.TryGetValue(typeIndex, out var value) && value.Count > 0)
			{
				return Materialize(value.Dequeue(), size, alignment);
			}
			return Allocate(typeIndex, 1u, size, alignment, 268435456u);
		}

		public ObjectReservationScope ActivateObjectReservations(IReadOnlyList<Reservation> reservations)
		{
			if (_activeObjectReservations != null)
			{
				throw new InvalidOperationException("BPHCL object reservation scope is already active.");
			}
			_activeObjectReservations = (from reservation in reservations
				group reservation by reservation.TypeIndex).ToDictionary((IGrouping<uint, Reservation> group) => group.Key, (IGrouping<uint, Reservation> group) => new Queue<Reservation>(group));
			return new ObjectReservationScope(this);
		}

		private void CompleteObjectReservationScope()
		{
			int[] array = _activeObjectReservations?.SelectMany((KeyValuePair<uint, Queue<Reservation>> pair) => pair.Value).Select((Reservation reservation) => reservation.ItemIndex).ToArray() ?? Array.Empty<int>();
			_activeObjectReservations = null;
			if (array.Length != 0)
			{
				throw new InvalidOperationException($"BPHCL top-level reservation scope left {array.Length} ITEM slot(s) unmaterialized: " + string.Join(", ", array.Select((int itemIndex) => $"{itemIndex}:{NativeBphclGameReadyReflectionCatalog.GetTypeName(_items[itemIndex].TypeIndex)}")));
			}
		}

		private void AbandonObjectReservationScope()
		{
			_activeObjectReservations = null;
		}

		public Reservation ReserveObject(uint typeIndex)
		{
			int count = _items.Count;
			_items.Add(new NativeBphclItem(0x10000000 | typeIndex, typeIndex, 0u, 1u));
			return new Reservation(count, typeIndex);
		}

		public Reservation ReserveArray(uint typeIndex, uint count)
		{
			int count2 = _items.Count;
			_items.Add(new NativeBphclItem(0x20000000 | typeIndex, typeIndex, 0u, count));
			return new Reservation(count2, typeIndex);
		}

		public Allocation Materialize(Reservation reservation, int size, int alignment = 8)
		{
			if ((uint)reservation.ItemIndex >= (uint)_items.Count || _items[reservation.ItemIndex].TypeIndex != reservation.TypeIndex || _allocations.Any((AllocationInfo candidate) => candidate.ItemIndex == reservation.ItemIndex))
			{
				throw new InvalidOperationException("Invalid or already materialized BPHCL ITEM reservation.");
			}
			if (size < 0)
			{
				throw new ArgumentOutOfRangeException("size");
			}
			Align(GetRequiredAlignment(reservation.TypeIndex, alignment));
			uint num = checked((uint)_data.Count);
			_data.AddRange(new byte[size]);
			_items[reservation.ItemIndex] = _items[reservation.ItemIndex]with
			{
				DataOffset = num
			};
			_allocations.Add(new AllocationInfo(reservation.ItemIndex, reservation.TypeIndex, num, size));
			return new Allocation(reservation.ItemIndex, num, size, this);
		}

		public Allocation AllocateArray(uint typeIndex, int count, int stride, int alignment = 8)
		{
			return checked(Allocate(typeIndex, (uint)count, count * stride, alignment, 536870912u));
		}

		public Allocation WriteReferenceArray(IReadOnlyList<int> itemIndices)
		{
			Allocation allocation = AllocateArray(StandaloneTypeCatalog.Pointer, itemIndices.Count, 8);
			for (int i = 0; i < itemIndices.Count; i++)
			{
				WriteReference(allocation.DataOffset + checked((uint)(i * 8)), itemIndices[i]);
			}
			return allocation;
		}

		public Allocation WriteUInt16Array(IReadOnlyList<ushort> values)
		{
			Allocation allocation = AllocateArray(StandaloneTypeCatalog.UInt16, values.Count, 2, 2);
			for (int i = 0; i < values.Count; i++)
			{
				WriteUInt16(allocation.DataOffset + checked((uint)(i * 2)), values[i]);
			}
			return allocation;
		}

		public Allocation WriteInt16Array(IReadOnlyList<short> values)
		{
			Allocation allocation = AllocateArray(StandaloneTypeCatalog.Int16, values.Count, 2, 2);
			for (int i = 0; i < values.Count; i++)
			{
				WriteInt16(allocation.DataOffset + checked((uint)(i * 2)), values[i]);
			}
			return allocation;
		}

		public Allocation WriteUInt32Array(IReadOnlyList<uint> values, uint? elementType = null)
		{
			Allocation allocation = AllocateArray(elementType ?? StandaloneTypeCatalog.UInt32, values.Count, 4, 4);
			for (int i = 0; i < values.Count; i++)
			{
				WriteUInt32(allocation.DataOffset + checked((uint)(i * 4)), values[i]);
			}
			return allocation;
		}

		public Allocation WriteInt32Array(IReadOnlyList<int> values, uint elementType)
		{
			Allocation allocation = AllocateArray(elementType, values.Count, 4, 4);
			for (int i = 0; i < values.Count; i++)
			{
				WriteInt32(allocation.DataOffset + checked((uint)(i * 4)), values[i]);
			}
			return allocation;
		}

		public Allocation WriteBoolArray(IReadOnlyList<bool> values, uint elementType)
		{
			Allocation allocation = AllocateArray(elementType, values.Count, 1, 1);
			for (int i = 0; i < values.Count; i++)
			{
				WriteByte(allocation.DataOffset + checked((uint)i), values[i] ? ((byte)1) : ((byte)0));
			}
			return allocation;
		}

		public Allocation WriteByteArray(IReadOnlyList<byte> values)
		{
			Allocation allocation = AllocateArray(StandaloneTypeCatalog.UInt8, values.Count, 1, 1);
			for (int i = 0; i < values.Count; i++)
			{
				WriteByte(allocation.DataOffset + checked((uint)i), values[i]);
			}
			return allocation;
		}

		public Allocation WriteVector4Array(IReadOnlyList<Vector4> values)
		{
			Allocation allocation = AllocateArray(StandaloneTypeCatalog.Vector4, values.Count, 16, 16);
			for (int i = 0; i < values.Count; i++)
			{
				WriteVector4(allocation.DataOffset + checked((uint)(i * 16)), values[i]);
			}
			return allocation;
		}

		public Allocation WriteMatrix4Array(IReadOnlyList<Matrix4x4> values)
		{
			Allocation allocation = AllocateArray(StandaloneTypeCatalog.Matrix4, values.Count, 64, 16);
			for (int i = 0; i < values.Count; i++)
			{
				uint num = allocation.DataOffset + checked((uint)(i * 64));
				Matrix4x4 matrix4x = values[i];
				WriteVector4(num, new Vector4(matrix4x.M11, matrix4x.M12, matrix4x.M13, matrix4x.M14));
				WriteVector4(num + 16, new Vector4(matrix4x.M21, matrix4x.M22, matrix4x.M23, matrix4x.M24));
				WriteVector4(num + 32, new Vector4(matrix4x.M31, matrix4x.M32, matrix4x.M33, matrix4x.M34));
				WriteVector4(num + 48, new Vector4(matrix4x.M41, matrix4x.M42, matrix4x.M43, matrix4x.M44));
			}
			return allocation;
		}

		public int WriteString(string value)
		{
			return WriteInternedString(value);
		}

		public int WriteInternedString(string value)
		{
			if (_stringItems.TryGetValue(value, out var value2))
			{
				return value2;
			}
			byte[] bytes = Encoding.UTF8.GetBytes(value + "\0");
			Allocation allocation = AllocateArray(StandaloneTypeCatalog.Char, bytes.Length, 1, 1);
			bytes.AsSpan().CopyTo(GetSpan(allocation.DataOffset, bytes.Length));
			_stringItems.Add(value, allocation.ItemIndex);
			return allocation.ItemIndex;
		}

		public int WriteString(Reservation reservation, string value)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(value + "\0");
			Allocation allocation = Materialize(reservation, bytes.Length, 1);
			if (allocation.Count != bytes.Length)
			{
				throw new InvalidOperationException("Reserved BPHCL string length does not match its UTF-8 payload.");
			}
			bytes.AsSpan().CopyTo(GetSpan(allocation.DataOffset, bytes.Length));
			_stringItems.TryAdd(value, allocation.ItemIndex);
			return allocation.ItemIndex;
		}

		public void WriteReference(uint dataOffset, int itemIndex, uint? patchTypeOverride = null)
		{
			if ((uint)itemIndex >= (uint)_items.Count)
			{
				throw new ArgumentOutOfRangeException("itemIndex");
			}
			WriteUInt32(dataOffset, checked((uint)itemIndex));
			PromoteReferenceArrayStorage(itemIndex, GetReferencePatchType(dataOffset));
			if (patchTypeOverride.HasValue)
			{
				uint valueOrDefault = patchTypeOverride.GetValueOrDefault();
				_referencePatchTypeOverrides[dataOffset] = valueOrDefault;
			}
			_referenceOffsets.Add(dataOffset);
		}

		public void WriteUInt16(uint dataOffset, ushort value)
		{
			BinaryPrimitives.WriteUInt16LittleEndian(GetSpan(dataOffset, 2), value);
		}

		public void WriteInt16(uint dataOffset, short value)
		{
			BinaryPrimitives.WriteInt16LittleEndian(GetSpan(dataOffset, 2), value);
		}

		public void WriteUInt32(uint dataOffset, uint value)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(GetSpan(dataOffset, 4), value);
		}

		public void WriteInt32(uint dataOffset, int value)
		{
			BinaryPrimitives.WriteInt32LittleEndian(GetSpan(dataOffset, 4), value);
		}

		public void WriteByte(uint dataOffset, byte value)
		{
			GetSpan(dataOffset, 1)[0] = value;
		}

		public void WriteBytes(uint dataOffset, ReadOnlySpan<byte> values)
		{
			values.CopyTo(GetSpan(dataOffset, values.Length));
		}

		public void WriteSingle(uint dataOffset, float value)
		{
			BinaryPrimitives.WriteSingleLittleEndian(GetSpan(dataOffset, 4), value);
		}

		public void WriteVector4(uint dataOffset, Vector4 value)
		{
			WriteSingle(dataOffset, value.X);
			WriteSingle(dataOffset + 4, value.Y);
			WriteSingle(dataOffset + 8, value.Z);
			WriteSingle(dataOffset + 12, value.W);
		}

		public void WriteScalar(uint dataOffset, string typeName, object value)
		{
			switch (typeName)
			{
			case "hkUint8":
			case "hkBool":
			{
				Span<byte> span = GetSpan(dataOffset, 1);
				byte b = ((!(value is bool)) ? checked((byte)Convert.ToInt32(value, CultureInfo.InvariantCulture)) : (((bool)value) ? ((byte)1) : ((byte)0)));
				span[0] = b;
				break;
			}
			case "hkInt8":
				GetSpan(dataOffset, 1)[0] = (byte)Convert.ToSByte(value, CultureInfo.InvariantCulture);
				break;
			case "hkUint16":
				WriteUInt16(dataOffset, checked((ushort)Convert.ToInt32(value, CultureInfo.InvariantCulture)));
				break;
			case "hkInt16":
				WriteUInt16(dataOffset, (ushort)Convert.ToInt16(value, CultureInfo.InvariantCulture));
				break;
			case "hkUint32":
				WriteUInt32(dataOffset, checked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture)));
				break;
			case "hkInt32":
				WriteInt32(dataOffset, Convert.ToInt32(value, CultureInfo.InvariantCulture));
				break;
			case "hkReal":
				WriteSingle(dataOffset, Convert.ToSingle(value, CultureInfo.InvariantCulture));
				break;
			default:
				throw new InvalidDataException("Standalone BPHCL writer cannot serialize scalar field type '" + typeName + "'.");
			}
		}

		public void NormalizeHavokArrayHeaders()
		{
			NormalizeHavokArrayHeaders(0u, NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkRootLevelContainer"), 1u, 16);
			checked
			{
				foreach (AllocationInfo allocation in _allocations)
				{
					uint typeSize = NativeBphclGameReadyReflectionCatalog.GetTypeSize(allocation.TypeIndex);
					if (typeSize != 0)
					{
						uint elementCount = Math.Min(_items[allocation.ItemIndex].Count, (uint)unchecked(allocation.ByteLength / typeSize));
						NormalizeHavokArrayHeaders(allocation.Offset, allocation.TypeIndex, elementCount, allocation.ByteLength);
					}
				}
			}
		}

		public byte[] ToDataPayload()
		{
			return _data.ToArray();
		}

		public IReadOnlyList<NativeBphclPatch> BuildPatches()
		{
			return (from offset in _referenceOffsets
				group offset by (!_referencePatchTypeOverrides.TryGetValue(offset, out var value)) ? GetReferencePatchType(offset) : value into @group
				orderby @group.Key
				select new NativeBphclPatch(@group.Key, @group.OrderBy((uint offset) => offset).ToArray())).ToArray();
		}

		private void NormalizeHavokArrayHeaders(uint dataOffset, uint typeIndex, uint elementCount, int byteLength)
		{
			NativeBphclTypeLayout typeLayoutIncludingBaseTypes;
			try
			{
				typeLayoutIncludingBaseTypes = NativeBphclGameReadyReflectionCatalog.GetTypeLayoutIncludingBaseTypes(typeIndex);
			}
			catch (InvalidDataException)
			{
				return;
			}
			if (typeLayoutIncludingBaseTypes.Size == 0 || byteLength <= 0)
			{
				return;
			}
			if (string.Equals(typeLayoutIncludingBaseTypes.Name, "hkArray", StringComparison.Ordinal))
			{
				uint num2;
				checked
				{
					int num = (int)typeLayoutIncludingBaseTypes.Size;
					num2 = Math.Min((uint)unchecked(byteLength / num), uint.MaxValue);
				}
				for (uint num3 = 0u; num3 < num2; num3++)
				{
					uint num4 = checked(dataOffset + num3 * typeLayoutIncludingBaseTypes.Size);
					WriteUInt32(num4 + 8, 0u);
					WriteUInt32(num4 + 12, 0u);
				}
				return;
			}
			checked
			{
				uint num5 = dataOffset + (uint)byteLength;
				for (uint num6 = 0u; num6 < elementCount; num6 = unchecked(num6 + 1))
				{
					uint num7 = dataOffset + num6 * typeLayoutIncludingBaseTypes.Size;
					if (num7 < num5)
					{
						uint objectEnd = Math.Min(num5, num7 + typeLayoutIncludingBaseTypes.Size);
						NormalizeHavokArrayHeadersAt(num7, typeIndex, objectEnd, new HashSet<(uint, uint)>());
						continue;
					}
					break;
				}
			}
		}

		private void NormalizeHavokArrayHeadersAt(uint dataOffset, uint typeIndex, uint objectEnd, ISet<(uint Offset, uint TypeIndex)> visited)
		{
			if (!visited.Add((dataOffset, typeIndex)))
			{
				return;
			}
			NativeBphclTypeLayout typeLayoutIncludingBaseTypes;
			try
			{
				typeLayoutIncludingBaseTypes = NativeBphclGameReadyReflectionCatalog.GetTypeLayoutIncludingBaseTypes(typeIndex);
			}
			catch (InvalidDataException)
			{
				return;
			}
			foreach (NativeBphclTypeLayoutMember member in typeLayoutIncludingBaseTypes.Members)
			{
				uint num = checked(dataOffset + member.Offset);
				if (num >= objectEnd)
				{
					continue;
				}
				if (member.TypeName == "hkArray")
				{
					if ((ulong)((long)num + 16L) <= (ulong)objectEnd)
					{
						WriteUInt32(num + 8, 0u);
						WriteUInt32(num + 12, 0u);
					}
				}
				else
				{
					uint typeSize = NativeBphclGameReadyReflectionCatalog.GetTypeSize(member.TypeIndex);
					if (typeSize != 0 && (ulong)((long)num + (long)typeSize) <= (ulong)objectEnd)
					{
						NormalizeHavokArrayHeadersAt(num, member.TypeIndex, num + typeSize, visited);
					}
				}
			}
		}

		private Allocation Allocate(uint typeIndex, uint count, int byteLength, int alignment, uint itemCategory)
		{
			if (byteLength < 0)
			{
				throw new ArgumentOutOfRangeException("byteLength");
			}
			Align(GetRequiredAlignment(typeIndex, alignment));
			uint num = checked((uint)_data.Count);
			int num2 = ((count == 0 && byteLength == 0) ? 1 : byteLength);
			_data.AddRange(new byte[num2]);
			int count2 = _items.Count;
			_items.Add(new NativeBphclItem(itemCategory | typeIndex, typeIndex, num, count));
			_allocations.Add(new AllocationInfo(count2, typeIndex, num, num2));
			return new Allocation(count2, num, num2, this);
		}

		private uint GetReferencePatchType(uint dataOffset)
		{
			if (dataOffset == 0)
			{
				return StandaloneTypeCatalog.Array;
			}
			foreach (AllocationInfo allocation in _allocations)
			{
				if (dataOffset < allocation.Offset || dataOffset >= (uint)((int)allocation.Offset + allocation.ByteLength))
				{
					continue;
				}
				if (string.Equals(NativeBphclGameReadyReflectionCatalog.GetTypeName(allocation.TypeIndex), "hkRefPtr", StringComparison.Ordinal))
				{
					return allocation.TypeIndex;
				}
				uint typeSize = NativeBphclGameReadyReflectionCatalog.GetTypeSize(allocation.TypeIndex);
				if (typeSize != 0)
				{
					uint memberOffset = (dataOffset - allocation.Offset) % typeSize;
					uint? memberTypeIndex = NativeBphclGameReadyReflectionCatalog.GetMemberTypeIndex(allocation.TypeIndex, memberOffset);
					if (memberTypeIndex.HasValue)
					{
						return memberTypeIndex.Value;
					}
				}
				return allocation.TypeIndex;
			}
			throw new InvalidDataException($"BPHCL pointer at DATA+0x{dataOffset:X} is outside every ITEM allocation.");
		}

		private void PromoteReferenceArrayStorage(int itemIndex, uint fieldTypeIndex)
		{
			if (_items[itemIndex].TypeIndex != StandaloneTypeCatalog.Pointer)
			{
				return;
			}
			uint? arrayElementType = NativeBphclGameReadyReflectionCatalog.GetArrayElementType(fieldTypeIndex);
			if (arrayElementType.HasValue)
			{
				AllocationInfo allocationInfo = _allocations.FirstOrDefault((AllocationInfo candidate) => candidate.ItemIndex == itemIndex);
				if (allocationInfo != null)
				{
					allocationInfo.TypeIndex = arrayElementType.Value;
					_items[itemIndex] = _items[itemIndex]with
					{
						Flags = (0x20000000 | arrayElementType.Value),
						TypeIndex = arrayElementType.Value
					};
				}
			}
		}

		private void Align(int alignment)
		{
			int num = (_data.Count + alignment - 1) & -alignment;
			if (num > _data.Count)
			{
				_data.AddRange(new byte[num - _data.Count]);
			}
		}

		private static int GetRequiredAlignment(uint typeIndex, int requestedAlignment)
		{
			uint reflectedAlignment = NativeBphclGameReadyReflectionCatalog.GetTypeAlignment(typeIndex);
			return Math.Max(requestedAlignment, checked((int)Math.Max(1u, reflectedAlignment)));
		}

		private Span<byte> GetSpan(uint offset, int length)
		{
			int num = checked((int)offset);
			if (num < 0 || num > _data.Count - length)
			{
				throw new ArgumentOutOfRangeException("offset");
			}
			return CollectionsMarshal.AsSpan(_data).Slice(num, length);
		}
	}

	private static class StandaloneTypeCatalog
	{
		public static uint Pointer => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("T*");

		public static uint RefPtr => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkRefPtr");

		public static uint Array => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkArray");

		public static uint Char => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("char");

		public static uint NamedVariant => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkRootLevelContainer::NamedVariant");

		public static uint ClothContainer => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hclClothContainer");

		public static uint ClothData => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hclClothData");

		public static uint SimClothData => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hclSimClothData");

		public static uint SimClothPose => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hclSimClothPose");

		public static uint AnimationContainer => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkaAnimationContainer");

		public static uint Skeleton => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkaSkeleton");

		public static uint Bone => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkaBone");

		public static uint QsTransform => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkQsTransform");

		public static uint Collidable => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hclCollidable");

		public static uint CapsuleShape => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hclCapsuleShape");

		public static uint SphereShape => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hclSphereShape");

		public static uint PlaneShape => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hclPlaneShape");

		public static uint ParticleData => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hclSimClothData::ParticleData");

		public static uint Int16 => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkInt16");

		public static uint UInt16 => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkUint16");

		public static uint UInt8 => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkUint8");

		public static uint UInt32 => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkUint32");

		public static uint Vector4 => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkVector4");

		public static uint Matrix4 => NativeBphclGameReadyReflectionCatalog.GetTypeIndex("hkMatrix4");

		public static byte[] CreateSection()
		{
			return NativeBphclGameReadyReflectionCatalog.TypeSection;
		}
	}

	private const int ObjectHeaderSize = 24;

	/// <summary>
	/// Serializes a fresh graph, then runs it through the same merge and
	/// compaction path used for native edits. That final pass normalizes root
	/// references, collider ownership, ITEM ordering, and reflected type ids.
	/// </summary>
	public static byte[] WriteGameReady(StandaloneBphclDocumentGraph graph)
	{
		var firstPass = Write(graph);
		var parsed = NativeBphclDocument.Parse(firstPass);
		var normalized = firstPass;
		for (var clothIndex = 0; clothIndex < parsed.Cloths.Count; clothIndex++)
		{
			normalized = NativeBphclGameReadyMerge.ReplaceCompleteCloth(
				NativeBphclDocument.Parse(normalized),
				parsed,
				clothIndex,
				clothIndex);
		}

		var pruned = NativeBphclGameReadyMerge.PruneUnreferencedColliders(
			NativeBphclDocument.Parse(normalized));
		return NativeBphclGameReadyCompactor.Compact(NativeBphclDocument.Parse(pruned));
	}

	public static byte[] Write(StandaloneBphclDocumentGraph graph) => WriteCore(graph);

	private static byte[] WriteCore(StandaloneBphclDocumentGraph graph)
	{
		ArgumentNullException.ThrowIfNull(graph, "graph");
		graph.Validate();
		DataBuilder builder = new DataBuilder();
		StandaloneBphclCollider[] array = graph.OuterColliders.OrderBy((StandaloneBphclCollider collider) => collider.Index).ToArray();
		RootScaffold root = CreateRootScaffold(builder, graph.Cloths.Count, array.Length);
		Dictionary<int, DataBuilder.Reservation> colliderObjectReservations = array.ToDictionary((StandaloneBphclCollider collider) => collider.Index, (StandaloneBphclCollider collider) => builder.ReserveObject(StandaloneTypeCatalog.Collidable));
		DataBuilder.Reservation[] clothReservations = graph.Cloths.Select((StandaloneBphclClothGraph _) => builder.ReserveObject(NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclClothData").TypeIndex)).ToArray();
		DataBuilder.Reservation emptyResourceName = builder.ReserveArray(StandaloneTypeCatalog.Char, 1u);
		checked
		{
			DataBuilder.Reservation skeletonStorage = builder.ReserveArray(RequireArrayElementType(NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hkaAnimationContainer").TypeIndex, 24u), (uint)graph.Cloths.Count);
			DataBuilder.Reservation[] skeletonReservations = graph.Cloths.Select((StandaloneBphclClothGraph _) => builder.ReserveObject(NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hkaSkeleton").TypeIndex)).ToArray();
			Dictionary<int, ColliderReservation> colliderReservations = array.ToDictionary((StandaloneBphclCollider collider) => collider.Index, (StandaloneBphclCollider collider) => new ColliderReservation(colliderObjectReservations[collider.Index], builder.ReserveObject(GetColliderShapeType(collider)), builder.ReserveArray(StandaloneTypeCatalog.Char, (uint)Encoding.UTF8.GetByteCount(NormalizeColliderName(collider.Name)) + 1)));
			CompleteRootScaffold(builder, root, emptyResourceName, skeletonStorage, graph.Cloths.Count);
			ClothScheduleReservations[] clothSchedules = graph.Cloths.Select((StandaloneBphclClothGraph cloth2) => ReserveClothSchedule(builder, cloth2)).ToArray();
			Dictionary<int, int> colliderItemsByIndex = array.ToDictionary((StandaloneBphclCollider collider) => collider.Index, (StandaloneBphclCollider collider) => WriteCollider(builder, collider, colliderReservations[collider.Index]));
			int[] colliderItems = array.Select((StandaloneBphclCollider collider) => colliderItemsByIndex[collider.Index]).ToArray();
			ClothRootData[] array2 = graph.Cloths.Select((StandaloneBphclClothGraph cloth2, int index) => MaterializeClothRoot(builder, cloth2, clothReservations[index], clothSchedules[index].Root)).ToArray();
			int[] skeletonItems = graph.Cloths.Select((StandaloneBphclClothGraph cloth2, int index) => WriteSkeleton(builder, cloth2, skeletonReservations[index])).ToArray();
			StandaloneBphclCollider[] array3 = array;
			foreach (StandaloneBphclCollider standaloneBphclCollider in array3)
			{
				WriteColliderShape(builder, standaloneBphclCollider, colliderReservations[standaloneBphclCollider.Index].Shape);
			}
			List<RootVariant> list = new List<RootVariant>();
			for (int num2 = 0; num2 < graph.Cloths.Count; num2 = unchecked(num2 + 1))
			{
				StandaloneBphclClothGraph cloth = graph.Cloths[num2];
				ClothScheduleReservations clothScheduleReservations = clothSchedules[num2];
				int[] colliderItems2 = cloth.Colliders.Select(delegate(StandaloneBphclCollider collider)
				{
					if (!colliderItemsByIndex.TryGetValue(collider.Index, out var value))
					{
						throw new InvalidDataException($"Cloth '{cloth.Name}' references missing outer collider {collider.Index}.");
					}
					return value;
				}).ToArray();
				IReadOnlyList<int> readOnlyList = Array.Empty<int>();
				int simulationItem;
				IReadOnlyList<int> bufferItems;
				int transformSetItem;
				RuntimePipeline runtimePipeline;
				using (DataBuilder.ObjectReservationScope reservationScope = builder.ActivateObjectReservations(clothScheduleReservations.TopLevelObjects))
				{
					simulationItem = WriteSimulation(builder, cloth, colliderItems2, cloth.ConstraintSets.Count, GetSimulateOperatorIndex(cloth), out DataBuilder.Reservation constraintReferences);
					bufferItems = WriteBufferDefinitions(builder, cloth);
					transformSetItem = WriteTransformSetDefinition(builder, cloth, graph.PreservesNativeBphclSchedule);
					readOnlyList = WriteConstraintSets(builder, cloth);
					if (readOnlyList.Count != cloth.ConstraintSets.Count)
					{
						throw new InvalidDataException($"Cloth '{cloth.Name}' emitted {readOnlyList.Count} constraint sets but reserved {cloth.ConstraintSets.Count}.");
					}
					DataBuilder.Allocation storage = builder.Materialize(constraintReferences, readOnlyList.Count * 8);
					WriteReferenceArray(builder, storage, readOnlyList);
					runtimePipeline = WriteRuntimePipeline(builder, cloth, readOnlyList.Count, graph.PreservesNativeBphclSchedule);
					reservationScope.Complete();
				}
				int clothItem = CompleteClothRoot(builder, array2[num2], simulationItem, transformSetItem, bufferItems, runtimePipeline.OperatorItems, runtimePipeline.StateItems);
				list.Add(new RootVariant(cloth.Name, "hclClothContainer", clothItem, skeletonReservations[num2].ItemIndex));
			}
			FillClothContainer(builder, root, list.Select((RootVariant variant) => variant.ClothItem).ToArray(), colliderItems);
			FillAnimationContainer(builder, root, skeletonItems);
			builder.NormalizeHavokArrayHeaders();
			IReadOnlyList<NativeBphclItem> readOnlyList2 = builder.Items;
			IReadOnlyList<NativeBphclPatch> readOnlyList3 = builder.BuildPatches();
			byte[]? nativeTypeSection = graph.NativeTypeSection;
			byte[] typeSection;
			if (nativeTypeSection is { Length: > 0 })
			{
				// Fidelity reconstruction keeps the source TYPE bytes. Fresh
				// conversion instead emits the smallest dependency-complete
				// catalog so Havok's byte-sized runtime type ids stay valid.
				typeSection = nativeTypeSection;
			}
			else
			{
				NativeBphclGameReadyTypeTable compactTypeTable =
					NativeBphclGameReadyTypeTable.CreateCompactEmbeddedCatalog(
						readOnlyList2.Select(item => item.TypeIndex)
							.Concat(readOnlyList3.Select(patch => patch.TypeIndex)),
						readOnlyList2.Select(item => item.TypeIndex));
				typeSection = compactTypeTable.ReplacementSection
					?? throw new InvalidDataException("Compact BPHCL TYPE table was not generated.");
				readOnlyList2 = RemapTypeIndices(readOnlyList2, compactTypeTable.TargetToMerged);
				readOnlyList3 = RemapPatchTypeIndices(readOnlyList3, compactTypeTable.TargetToMerged);
			}
			byte[]? nativeAampPayload = graph.NativeAampPayload;
			byte[] aampPayload = ((nativeAampPayload != null && nativeAampPayload.Length > 0) ? nativeAampPayload : NativeAampGameReadyStandaloneWriter.Create(graph));
			return NativeBphclGameReadyStandaloneEnvelopeWriter.Create(builder.ToDataPayload(), typeSection, readOnlyList2, readOnlyList3, aampPayload);
		}
	}

	private static IReadOnlyList<NativeBphclItem> RemapTypeIndices(IReadOnlyList<NativeBphclItem> items, IReadOnlyDictionary<uint, uint> typeMap)
	{
		return items.Select(delegate(NativeBphclItem item)
		{
			uint num = RemapTypeIndex(item.TypeIndex, typeMap, "ITEM");
			return item with
			{
				TypeIndex = num,
				Flags = ((item.Flags & 0xF0000000u) | num)
			};
		}).ToArray();
	}

	private static IReadOnlyList<NativeBphclPatch> RemapPatchTypeIndices(IReadOnlyList<NativeBphclPatch> patches, IReadOnlyDictionary<uint, uint> typeMap)
	{
		return patches.Select((NativeBphclPatch patch) => patch with
		{
			TypeIndex = RemapTypeIndex(patch.TypeIndex, typeMap, "PTCH")
		}).ToArray();
	}

	private static uint RemapTypeIndex(uint typeIndex, IReadOnlyDictionary<uint, uint> typeMap, string recordKind)
	{
		if (typeMap.TryGetValue(typeIndex, out var value))
		{
			return value;
		}
		throw new InvalidDataException($"Compact BPHCL TYPE table is missing {recordKind} type {typeIndex}.");
	}

	private static RootScaffold CreateRootScaffold(DataBuilder builder, int clothCount, int collidableCount)
	{
		DataBuilder.Allocation allocation = builder.AllocateArray(StandaloneTypeCatalog.NamedVariant, 3, 24);
		builder.WriteReference(0u, allocation.ItemIndex);
		builder.WriteUInt32(8u, 3u);
		DataBuilder.Reservation reservation = builder.ReserveObject(StandaloneTypeCatalog.ClothContainer);
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hkMemoryResourceContainer");
		DataBuilder.Reservation reservation2 = builder.ReserveObject(typeLayout.TypeIndex);
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hkaAnimationContainer");
		DataBuilder.Reservation reservation3 = builder.ReserveObject(typeLayout2.TypeIndex);
		int nameItem = builder.WriteString("Cloth Container");
		int nameItem2 = builder.WriteString("Resource Data");
		int nameItem3 = builder.WriteString("Animation Container");
		int classItem = builder.WriteString("hclClothContainer");
		int classItem2 = builder.WriteString("hkMemoryResourceContainer");
		int classItem3 = builder.WriteString("hkaAnimationContainer");
		WriteNamedVariant(builder, allocation.DataOffset, nameItem, classItem, reservation.ItemIndex);
		WriteNamedVariant(builder, allocation.DataOffset + 24, nameItem2, classItem2, reservation2.ItemIndex);
		WriteNamedVariant(builder, allocation.DataOffset + 48, nameItem3, classItem3, reservation3.ItemIndex);
		DataBuilder.Allocation allocation2 = builder.Materialize(reservation, 56, 16);
		uint typeIndex = RequireArrayElementType(StandaloneTypeCatalog.ClothContainer, 24u);
		uint typeIndex2 = RequireArrayElementType(StandaloneTypeCatalog.ClothContainer, 40u);
		DataBuilder.Allocation allocation3 = builder.AllocateArray(typeIndex, collidableCount, 8, 16);
		DataBuilder.Allocation allocation4 = builder.AllocateArray(typeIndex2, clothCount, 8);
		DataBuilder.Allocation allocation5 = builder.Materialize(reservation2, RequireSize(typeLayout, 72));
		builder.WriteReference(allocation2.DataOffset + 24, allocation3.ItemIndex);
		builder.WriteUInt32(allocation2.DataOffset + 24 + 8, checked((uint)collidableCount));
		builder.WriteReference(allocation2.DataOffset + 24 + 16, allocation4.ItemIndex);
		builder.WriteUInt32(allocation2.DataOffset + 24 + 24, checked((uint)clothCount));
		return new RootScaffold(allocation2.DataOffset, allocation5.DataOffset, reservation3, allocation3, allocation4);
	}

	private static void CompleteRootScaffold(DataBuilder builder, RootScaffold root, DataBuilder.Reservation emptyResourceName, DataBuilder.Reservation skeletonStorage, int clothCount)
	{
		DataBuilder.Allocation allocation = builder.Materialize(emptyResourceName, 1, 1);
		builder.WriteByte(allocation.DataOffset, 0);
		builder.WriteReference(root.ResourceContainerDataOffset + 24, allocation.ItemIndex);
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hkaAnimationContainer");
		DataBuilder.Allocation allocation2 = builder.Materialize(root.AnimationReservation, RequireSize(typeLayout, 104));
		DataBuilder.Allocation allocation3 = builder.Materialize(skeletonStorage, checked(clothCount * 8));
		builder.WriteReference(allocation2.DataOffset + 24, allocation3.ItemIndex);
		builder.WriteUInt32(allocation2.DataOffset + 24 + 8, checked((uint)clothCount));
		root.AnimationContainerDataOffset = allocation2.DataOffset;
		root.SkeletonStorage = allocation3;
	}

	private static void WriteNamedVariant(DataBuilder builder, uint dataOffset, int nameItem, int classItem, int objectItem)
	{
		builder.WriteReference(dataOffset, nameItem);
		builder.WriteReference(dataOffset + 8, classItem);
		builder.WriteReference(dataOffset + 16, objectItem);
	}

	private static void FillClothContainer(DataBuilder builder, RootScaffold root, IReadOnlyList<int> clothItems, IReadOnlyList<int> colliderItems)
	{
		if (clothItems.Count != root.ClothStorage.Count || colliderItems.Count != root.CollidableStorage.Count)
		{
			throw new InvalidOperationException("BPHCL root reference storage does not match the generated cloth graph.");
		}
		for (int i = 0; i < colliderItems.Count; i++)
		{
			builder.WriteReference(root.CollidableStorage.DataOffset + checked((uint)(i * 8)), colliderItems[i]);
		}
		for (int j = 0; j < clothItems.Count; j++)
		{
			builder.WriteReference(root.ClothStorage.DataOffset + checked((uint)(j * 8)), clothItems[j]);
		}
	}

	private static void FillAnimationContainer(DataBuilder builder, RootScaffold root, IReadOnlyList<int> skeletonItems)
	{
		if (skeletonItems.Count != root.SkeletonStorage.Count)
		{
			throw new InvalidOperationException("BPHCL root skeleton storage does not match the generated cloth graph.");
		}
		for (int i = 0; i < skeletonItems.Count; i++)
		{
			builder.WriteReference(root.SkeletonStorage.DataOffset + checked((uint)(i * 8)), skeletonItems[i]);
		}
	}

	private static ClothScheduleReservations ReserveClothSchedule(DataBuilder builder, StandaloneBphclClothGraph cloth)
	{
		ClothRootReservations root = ReserveClothRoot(builder, cloth);
		List<DataBuilder.Reservation> list = new List<DataBuilder.Reservation> { builder.ReserveObject(NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclSimClothData").TypeIndex) };
		IEnumerable<string> enumerable2;
		if (cloth.Runtime.Buffers.Count <= 0)
		{
			IEnumerable<string> enumerable = new string[3] { "hclScratchBufferDefinition", "hclBufferDefinition", "hclBufferDefinition" };
			enumerable2 = enumerable;
		}
		else
		{
			enumerable2 = cloth.Runtime.Buffers.Select((ClothBufferDefinitionData buffer) => buffer.ClassName);
		}
		IEnumerable<string> source = enumerable2;
		list.AddRange(source.Select((string className) => builder.ReserveObject(NativeBphclGameReadyReflectionCatalog.GetTypeLayout(className).TypeIndex)));
		list.Add(builder.ReserveObject(NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclTransformSetDefinition").TypeIndex));
		IEnumerable<string> source2 = ((cloth.Runtime.Operators.Count > 0) ? (from @operator in cloth.Runtime.Operators
			orderby @operator.Index
			select @operator.ClassName) : cloth.SkinOperators.Select((SkinOperatorData skin) => skin.ClassName).Concat(new string[2] { "hclMoveParticlesOperator", "hclSimulateOperator" }).Concat(Enumerable.Repeat("hclSimpleMeshBoneDeformOperator", cloth.BoneDeformers.Count))
			.Concat(new string[2] { "hclCopyVerticesOperator", "hclCopyVerticesOperator" }));
		list.AddRange(source2.Select((string className) => builder.ReserveObject(NativeBphclGameReadyReflectionCatalog.GetTypeLayout(className).TypeIndex)));
		list.AddRange(from _ in Enumerable.Range(0, GetStateCount(cloth))
			select builder.ReserveObject(NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclClothState").TypeIndex));
		return new ClothScheduleReservations(root, list);
	}

	private static int GetSimulateOperatorIndex(StandaloneBphclClothGraph cloth)
	{
		if (cloth.Runtime.Operators.Count == 0)
		{
			return cloth.SkinOperators.Count + 1;
		}
		return (cloth.Runtime.Operators.SingleOrDefault((ClothOperatorData @operator) => string.Equals(@operator.ClassName, "hclSimulateOperator", StringComparison.Ordinal)) ?? throw new InvalidDataException("Cloth '" + cloth.Name + "' has no hclSimulateOperator.")).Index;
	}

	private static ClothRootReservations ReserveClothRoot(DataBuilder builder, StandaloneBphclClothGraph cloth)
	{
		return checked(new ClothRootReservations(builder.ReserveArray(RequireArrayElementType("hclClothData", "simClothDatas"), 1u), builder.ReserveArray(RequireArrayElementType("hclClothData", "bufferDefinitions"), (uint)GetBufferCount(cloth)), builder.ReserveArray(RequireArrayElementType("hclClothData", "transformSetDefinitions"), 1u), builder.ReserveArray(RequireArrayElementType("hclClothData", "operators"), (uint)GetOperatorCount(cloth)), builder.ReserveArray(RequireArrayElementType("hclClothData", "clothStateDatas"), (uint)GetStateCount(cloth)), builder.ReserveArray(StandaloneTypeCatalog.Char, (uint)Encoding.UTF8.GetByteCount(cloth.Name) + 1)));
	}

	private static ClothRootData MaterializeClothRoot(DataBuilder builder, StandaloneBphclClothGraph cloth, DataBuilder.Reservation clothReservation, ClothRootReservations reservations)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclClothData");
		DataBuilder.Allocation allocation = builder.Materialize(clothReservation, Math.Max(RequireSize(typeLayout, 112), 160), 16);
		DataBuilder.Allocation allocation2 = builder.Materialize(reservations.Simulations, 8, 16);
		DataBuilder.Allocation allocation3;
		DataBuilder.Allocation allocation4;
		DataBuilder.Allocation allocation5;
		DataBuilder.Allocation allocation6;
		int itemIndex;
		checked
		{
			allocation3 = builder.Materialize(reservations.Buffers, GetBufferCount(cloth) * 8, 16);
			allocation4 = builder.Materialize(reservations.TransformSets, 8, 16);
			allocation5 = builder.Materialize(reservations.Operators, GetOperatorCount(cloth) * 8, 16);
			allocation6 = builder.Materialize(reservations.States, GetStateCount(cloth) * 8, 16);
			itemIndex = builder.WriteString(reservations.Name, cloth.Name);
		}
		builder.WriteReference(allocation.DataOffset + 24, itemIndex);
		WriteArrayField(builder, allocation.DataOffset + 24 + 8, allocation2);
		WriteArrayField(builder, allocation.DataOffset + 48, allocation3);
		WriteArrayField(builder, allocation.DataOffset + 64, allocation4);
		WriteArrayField(builder, allocation.DataOffset + 80, allocation5);
		WriteArrayField(builder, allocation.DataOffset + 96, allocation6);
		builder.WriteUInt32(allocation.DataOffset + 148, 131072u);
		return new ClothRootData(allocation, allocation2, allocation3, allocation4, allocation5, allocation6);
	}

	private static int CompleteClothRoot(DataBuilder builder, ClothRootData root, int simulationItem, int transformSetItem, IReadOnlyList<int> bufferItems, IReadOnlyList<int> operatorItems, IReadOnlyList<int> stateItems)
	{
		WriteReferenceArray(builder, root.Simulations, new int[1] { simulationItem });
		WriteReferenceArray(builder, root.Buffers, bufferItems);
		WriteReferenceArray(builder, root.TransformSets, new int[1] { transformSetItem });
		WriteReferenceArray(builder, root.Operators, operatorItems);
		WriteReferenceArray(builder, root.States, stateItems);
		return root.Data.ItemIndex;
	}

	private static void WriteReferenceArray(DataBuilder builder, DataBuilder.Allocation storage, IReadOnlyList<int> itemIndices)
	{
		if (storage.Count != itemIndices.Count)
		{
			throw new InvalidDataException($"Reserved BPHCL reference array has {storage.Count} entries but requires {itemIndices.Count}.");
		}
		for (int i = 0; i < itemIndices.Count; i++)
		{
			builder.WriteReference(storage.DataOffset + checked((uint)(i * 8)), itemIndices[i]);
		}
	}

	private static int GetBufferCount(StandaloneBphclClothGraph cloth)
	{
		if (cloth.Runtime.Buffers.Count <= 0)
		{
			return 3;
		}
		return cloth.Runtime.Buffers.Count;
	}

	private static int GetOperatorCount(StandaloneBphclClothGraph cloth)
	{
		if (cloth.Runtime.Operators.Count <= 0)
		{
			return cloth.SkinOperators.Count + 2 + cloth.BoneDeformers.Count + 2;
		}
		return cloth.Runtime.Operators.Count;
	}

	private static int GetStateCount(StandaloneBphclClothGraph cloth)
	{
		if (cloth.Runtime.Operators.Count <= 0)
		{
			return 3;
		}
		return Math.Max(1, cloth.Runtime.States.Count);
	}

	private static int WriteTransformSetDefinition(DataBuilder builder, StandaloneBphclClothGraph cloth, bool preserveNativeBphclName)
	{
		ClothTransformSetDefinitionData clothTransformSetDefinitionData = cloth.Runtime.TransformSets.SingleOrDefault();
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclTransformSetDefinition");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 40));
		string value = ((!preserveNativeBphclName) ? ("cloth_skeleton_" + cloth.Name) : (clothTransformSetDefinitionData?.Name ?? cloth.SkeletonName));
		builder.WriteReference(allocation.DataOffset + 24, builder.WriteString(value));
		builder.WriteInt32(allocation.DataOffset + 32, clothTransformSetDefinitionData?.Type ?? 1);
		builder.WriteUInt32(allocation.DataOffset + 36, checked((uint)(clothTransformSetDefinitionData?.TransformCount ?? cloth.Bones.Count)));
		return allocation.ItemIndex;
	}

	private static IReadOnlyList<int> WriteBufferDefinitions(DataBuilder builder, StandaloneBphclClothGraph cloth)
	{
		if (cloth.Runtime.Buffers.Count > 0)
		{
			return (from buffer in cloth.Runtime.Buffers
				orderby buffer.Index
				select WriteSourceBufferDefinition(builder, cloth, buffer)).ToArray();
		}
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclScratchBufferDefinition");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 112), 16);
		WriteBufferBase(builder, allocation.DataOffset, cloth.Name, "SkinScratchBuf", 6, 0, cloth.Particles.Count, 0);
		return new int[3]
		{
			allocation.ItemIndex,
			WriteBufferDefinition(builder, cloth, "SimCurrentBuf", 1),
			WriteBufferDefinition(builder, cloth, "SimPrevBuf", 2)
		};
	}

	private static int WriteSourceBufferDefinition(DataBuilder builder, StandaloneBphclClothGraph cloth, ClothBufferDefinitionData source)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout(source.ClassName);
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 88), 16);
		string meshName = (string.IsNullOrWhiteSpace(source.MeshName) ? cloth.Name : source.MeshName);
		string bufferName = source.Type switch
		{
			6 => "SkinScratchBuf",
			1 => "SimCurrentBuf",
			2 => "SimPrevBuf",
			_ => string.IsNullOrWhiteSpace(source.BufferName) ? $"Buffer{source.Index}" : source.BufferName,
		};
		WriteBufferBase(builder, allocation.DataOffset, meshName, bufferName, source.Type, source.SubType, source.VertexCount, source.TriangleCount);
		return allocation.ItemIndex;
	}

	private static int WriteBufferDefinition(DataBuilder builder, StandaloneBphclClothGraph cloth, string bufferName, int type)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclBufferDefinition");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 88), 16);
		WriteBufferBase(builder, allocation.DataOffset, cloth.Name, bufferName, type, 0, cloth.Particles.Count, cloth.Triangles.Count);
		return allocation.ItemIndex;
	}

	private static void WriteBufferBase(DataBuilder builder, uint dataOffset, string meshName, string bufferName, int type, int subType, int vertexCount, int triangleCount)
	{
		builder.WriteReference(dataOffset + 24, builder.WriteString(meshName));
		builder.WriteReference(dataOffset + 32, builder.WriteString(bufferName));
		builder.WriteInt32(dataOffset + 40, type);
		builder.WriteInt32(dataOffset + 44, subType);
		builder.WriteUInt32(dataOffset + 48, checked((uint)vertexCount));
		builder.WriteUInt32(dataOffset + 52, checked((uint)triangleCount));
		builder.WriteUInt32(dataOffset + 56, 4096u);
		builder.WriteUInt32(dataOffset + 60, 250u);
		builder.WriteUInt32(dataOffset + 64, 250u);
		builder.WriteUInt32(dataOffset + 68, 250u);
		builder.WriteUInt32(dataOffset + 72, 4097u);
		builder.WriteUInt32(dataOffset + 76, 0u);
		builder.WriteByte(dataOffset + 80, 1);
		builder.WriteByte(dataOffset + 81, (byte)((type != 6) ? 1 : 2));
	}

	private static RuntimePipeline WriteRuntimePipeline(DataBuilder builder, StandaloneBphclClothGraph cloth, int constraintSetCount, bool preservesNativeBphclSchedule)
	{
		if (cloth.Runtime.Operators.Count > 0)
		{
			return WriteSourceRuntimePipeline(builder, cloth, constraintSetCount, preservesNativeBphclSchedule);
		}
		List<int> list = new List<int>();
		List<int> list2 = new List<int>();
		foreach (SkinOperatorData skinOperator in cloth.SkinOperators)
		{
			int count = list.Count;
			list2.Add(count);
			list.Add(WriteSkinOperator(builder, skinOperator, count));
		}
		int count2 = list.Count;
		list.Add(WriteMoveParticlesOperator(builder, cloth, count2));
		int count3 = list.Count;
		list.Add(WriteSimulateOperator(builder, cloth, constraintSetCount, count3));
		List<int> list3 = new List<int>();
		foreach (StandaloneBphclSimpleMeshBoneDeformer boneDeformer in cloth.BoneDeformers)
		{
			int count4 = list.Count;
			list3.Add(count4);
			list.Add(WriteSimpleMeshBoneDeformOperator(builder, boneDeformer, count4));
		}
		int count5 = list.Count;
		list.Add(WriteCopyVerticesOperator(builder, 0, 1, cloth.Particles.Count, 0, 0, count5));
		int count6 = list.Count;
		list.Add(WriteCopyVerticesOperator(builder, 0, 2, cloth.Particles.Count, 0, 0, count6));
		int[] operatorIndices = list2.Concat(new int[2] { count2, count3 }).Concat(list3).ToArray();
		int[] stateItems = new int[3]
		{
			WriteClothState(builder, "Default", operatorIndices, new uint[2] { 0u, 1u }, new uint[1], new uint[1]),
			WriteClothState(builder, "AnimToSimCurrent", list2.Concat(new int[1] { count5 }).ToArray(), new uint[2] { 0u, 1u }, new uint[1], new uint[1]),
			WriteClothState(builder, "AnimToSimPrev", list2.Concat(new int[1] { count6 }).ToArray(), new uint[2] { 0u, 2u }, new uint[1], new uint[1])
		};
		return new RuntimePipeline(list, stateItems, count3);
	}

	private static RuntimePipeline WriteSourceRuntimePipeline(DataBuilder builder, StandaloneBphclClothGraph cloth, int constraintSetCount, bool preservesNativeBphclSchedule)
	{
		Dictionary<int, SkinOperatorData> dictionary = cloth.SkinOperators.ToDictionary((SkinOperatorData skin) => skin.OperatorIndex);
		Dictionary<int, StandaloneBphclSimpleMeshBoneDeformer> dictionary2 = cloth.BoneDeformers.Where((StandaloneBphclSimpleMeshBoneDeformer deformer) => deformer.OperatorIndex >= 0).ToDictionary((StandaloneBphclSimpleMeshBoneDeformer deformer) => deformer.OperatorIndex);
		List<int> list = new List<int>(cloth.Runtime.Operators.Count);
		IReadOnlyDictionary<int, IReadOnlyList<ClothStateTransformTrackerData>> readOnlyDictionary = BuildSourceOperatorTransformTrackerPlans(cloth);
		int num = -1;
		foreach (ClothOperatorData item2 in cloth.Runtime.Operators.OrderBy((ClothOperatorData @operator) => @operator.Index))
		{
			if (item2.Index != list.Count)
			{
				throw new InvalidDataException("Cloth '" + cloth.Name + "' has non-contiguous runtime operator indices.");
			}
			int num2;
			switch (item2.ClassName)
			{
			case "hclObjectSpaceSkinPOperator":
			case "hclBoneSpaceSkinPOperator":
			{
				if (dictionary.TryGetValue(item2.Index, out var value2))
				{
					num2 = WriteSkinOperator(builder, value2, item2.Index, GetSourceOperatorTransformTrackers(readOnlyDictionary, item2.Index), GetOperatorTransformAccesses(item2, readOnlyDictionary, preservesNativeBphclSchedule));
					break;
				}
				goto default;
			}
			case "hclMoveParticlesOperator":
				num2 = WriteMoveParticlesOperator(builder, item2, item2.Index);
				break;
			case "hclSimulateOperator":
				num2 = WriteSimulateOperator(builder, cloth, item2, constraintSetCount, item2.Index, GetSourceOperatorTransformTrackers(readOnlyDictionary, item2.Index), GetOperatorTransformAccesses(item2, readOnlyDictionary, preservesNativeBphclSchedule), preservesNativeBphclSchedule);
				break;
			case "hclSimpleMeshBoneDeformOperator":
			{
				if (dictionary2.TryGetValue(item2.Index, out var value))
				{
					num2 = WriteSimpleMeshBoneDeformOperator(builder, value, item2.Index, GetSourceOperatorTransformTrackers(readOnlyDictionary, item2.Index), GetOperatorTransformAccesses(item2, readOnlyDictionary, preservesNativeBphclSchedule));
					break;
				}
				goto default;
			}
			case "hclGatherAllVerticesOperator":
				num2 = WriteGatherAllVerticesOperator(builder, item2);
				break;
			case "hclCopyVerticesOperator":
				num2 = WriteCopyVerticesOperator(builder, item2.InputBufferIndex.GetValueOrDefault(), item2.OutputBufferIndex.GetValueOrDefault(), item2.VertexCount.GetValueOrDefault(), item2.StartVertexIn.GetValueOrDefault(), item2.StartVertexOut.GetValueOrDefault(), item2.Index);
				break;
			default:
				throw new InvalidDataException($"Standalone BPHCL writer does not support source operator {item2.Index} '{item2.ClassName}'.");
			}
			int item = num2;
			if (item2.ClassName == "hclSimulateOperator")
			{
				num = item2.Index;
			}
			list.Add(item);
		}
		if (num < 0)
		{
			throw new InvalidDataException("Cloth '" + cloth.Name + "' has no hclSimulateOperator.");
		}
		IReadOnlyList<ClothStateRuntimeData> readOnlyList;
		if (cloth.Runtime.States.Count != 0)
		{
			readOnlyList = cloth.Runtime.States;
		}
		else
		{
			IReadOnlyList<ClothStateRuntimeData> readOnlyList2 = new ClothStateRuntimeData[1]
			{
				new ClothStateRuntimeData(0, "Default", Enumerable.Range(0, list.Count).ToArray(), cloth.Runtime.Buffers.Select((ClothBufferDefinitionData buffer) => buffer.Index).ToArray(), new int[1], new int[1])
			};
			readOnlyList = readOnlyList2;
		}
		IReadOnlyList<ClothStateRuntimeData> source = readOnlyList;
		if (!preservesNativeBphclSchedule)
		{
			source = source.Select(NormalizeFreshHkclStateTransformUsage).ToArray();
		}
		int[] stateItems = (from state in source
			orderby state.Index
			select WriteClothState(builder, state)).ToArray();
		return new RuntimePipeline(list, stateItems, num);
	}

	private static int WriteSkinOperator(DataBuilder builder, SkinOperatorData skin, int operatorId, IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers = null, IReadOnlyList<ClothStateTransformSetAccessData>? transformSetAccesses = null)
	{
		string className = skin.ClassName;
		if (!(className == "hclObjectSpaceSkinPOperator"))
		{
			if (className == "hclBoneSpaceSkinPOperator")
			{
				return WriteBoneSpaceSkinOperator(builder, skin, operatorId, transformTrackers, transformSetAccesses);
			}
			throw new InvalidDataException("Standalone BPHCL writer does not support skin operator '" + skin.ClassName + "'.");
		}
		return WriteObjectSpaceSkinOperator(builder, skin, operatorId, transformTrackers, transformSetAccesses);
	}

	private static int WriteObjectSpaceSkinOperator(DataBuilder builder, SkinOperatorData skin, int operatorId, IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers, IReadOnlyList<ClothStateTransformSetAccessData>? transformSetAccesses)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclObjectSpaceSkinPOperator");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 296), 16);
		WriteOperatorBase(builder, allocation.DataOffset, operatorId, "Skin", new ClothStateBufferAccessData[1] { CreateOperatorBufferAccess(skin.OutputBufferIndex, 6) }, transformSetAccesses ?? new ClothStateTransformSetAccessData[1] { CreateOperatorTransformAccess(skin.TransformSetIndex, 9, transformTrackers) });
		WriteSkinBaseArrays(builder, allocation.DataOffset, "hclObjectSpaceSkinPOperator", skin, includeBoneFromSkinMatrices: true);
		IReadOnlyList<SkinPositionBlockData> blocks = ((skin.LocalPositionBlocks.Count > 0) ? skin.LocalPositionBlocks : skin.LocalUnpackedPositionBlocks);
		uint num = RequireMemberOffset("hclObjectSpaceSkinPOperator", "objectSpaceDeformer");
		WriteObjectSpaceDeformer(builder, allocation.DataOffset + num, skin);
		WriteArrayField(builder, allocation.DataOffset + RequireMemberOffset("hclObjectSpaceSkinPOperator", "localPs"), WritePackedObjectPositionBlocks(builder, blocks));
		return allocation.ItemIndex;
	}

	private static int WriteBoneSpaceSkinOperator(DataBuilder builder, SkinOperatorData skin, int operatorId, IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers, IReadOnlyList<ClothStateTransformSetAccessData>? transformSetAccesses)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclBoneSpaceSkinPOperator");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 216), 16);
		WriteOperatorBase(builder, allocation.DataOffset, operatorId, "Skin", new ClothStateBufferAccessData[1] { CreateOperatorBufferAccess(skin.OutputBufferIndex, 6) }, transformSetAccesses ?? new ClothStateTransformSetAccessData[1] { CreateOperatorTransformAccess(skin.TransformSetIndex, 9, transformTrackers) });
		WriteSkinBaseArrays(builder, allocation.DataOffset, "hclBoneSpaceSkinPOperator", skin, includeBoneFromSkinMatrices: false);
		IReadOnlyList<SkinPositionBlockData> blocks = ((skin.LocalPositionBlocks.Count > 0) ? skin.LocalPositionBlocks : skin.LocalUnpackedPositionBlocks);
		uint num = RequireMemberOffset("hclBoneSpaceSkinPOperator", "boneSpaceDeformer");
		WriteBoneSpaceDeformer(builder, allocation.DataOffset + num, skin);
		WriteArrayField(builder, allocation.DataOffset + RequireMemberOffset("hclBoneSpaceSkinPOperator", "localPs"), WriteUnpackedPositionBlocks(builder, "hclBoneSpaceDeformer::LocalBlockP", blocks));
		return allocation.ItemIndex;
	}

	private static void WriteSkinBaseArrays(DataBuilder builder, uint dataOffset, string className, SkinOperatorData skin, bool includeBoneFromSkinMatrices)
	{
		if (includeBoneFromSkinMatrices)
		{
			WriteArrayField(builder, dataOffset + RequireMemberOffset(className, "boneFromSkinMeshTransforms"), builder.WriteMatrix4Array(skin.BoneFromSkinMeshTransforms));
		}
		WriteArrayField(builder, dataOffset + RequireMemberOffset(className, "transformSubset"), builder.WriteUInt16Array(skin.TransformSubset));
		builder.WriteUInt32(dataOffset + RequireMemberOffset(className, "outputBufferIndex"), checked((uint)skin.OutputBufferIndex));
		builder.WriteUInt32(dataOffset + RequireMemberOffset(className, "transformSetIndex"), checked((uint)skin.TransformSetIndex));
	}

	private static void WriteObjectSpaceDeformer(DataBuilder builder, uint dataOffset, SkinOperatorData skin)
	{
		foreach (SkinBlendArrayData blendArray in skin.BlendArrays)
		{
			WriteArrayField(builder, dataOffset + RequireMemberOffset("hclObjectSpaceDeformer", blendArray.MemberName), WriteBlendBlocks(builder, "hclObjectSpaceDeformer", blendArray), RequireArrayType("hclObjectSpaceDeformer", blendArray.MemberName));
		}
		WriteArrayField(builder, dataOffset + RequireMemberOffset("hclObjectSpaceDeformer", "controlBytes"), builder.WriteByteArray(skin.ControlBytes), RequireArrayType("hclObjectSpaceDeformer", "controlBytes"));
		builder.WriteUInt16(dataOffset + RequireMemberOffset("hclObjectSpaceDeformer", "startVertexIndex"), skin.StartVertexIndex);
		builder.WriteUInt16(dataOffset + RequireMemberOffset("hclObjectSpaceDeformer", "endVertexIndex"), skin.EndVertexIndex);
		builder.WriteByte(dataOffset + RequireMemberOffset("hclObjectSpaceDeformer", "partialWrite"), skin.PartialWrite ? ((byte)1) : ((byte)0));
	}

	private static void WriteBoneSpaceDeformer(DataBuilder builder, uint dataOffset, SkinOperatorData skin)
	{
		foreach (SkinBlendArrayData blendArray in skin.BlendArrays)
		{
			WriteArrayField(builder, dataOffset + RequireMemberOffset("hclBoneSpaceDeformer", blendArray.MemberName), WriteBlendBlocks(builder, "hclBoneSpaceDeformer", blendArray), RequireArrayType("hclBoneSpaceDeformer", blendArray.MemberName));
		}
		WriteArrayField(builder, dataOffset + RequireMemberOffset("hclBoneSpaceDeformer", "controlBytes"), builder.WriteByteArray(skin.ControlBytes), RequireArrayType("hclBoneSpaceDeformer", "controlBytes"));
		builder.WriteUInt16(dataOffset + RequireMemberOffset("hclBoneSpaceDeformer", "startVertexIndex"), skin.StartVertexIndex);
		builder.WriteUInt16(dataOffset + RequireMemberOffset("hclBoneSpaceDeformer", "endVertexIndex"), skin.EndVertexIndex);
		uint? memberOffset = NativeBphclGameReadyReflectionCatalog.GetMemberOffset("hclBoneSpaceDeformer", "batchSizeSpu");
		if (memberOffset.HasValue)
		{
			uint valueOrDefault = memberOffset.GetValueOrDefault();
			builder.WriteUInt16(dataOffset + valueOrDefault, (ushort)((skin.BatchSizeSpu == 0) ? 512 : skin.BatchSizeSpu));
		}
		builder.WriteByte(dataOffset + RequireMemberOffset("hclBoneSpaceDeformer", "partialWrite"), skin.PartialWrite ? ((byte)1) : ((byte)0));
	}

	private static DataBuilder.Allocation? WriteBlendBlocks(DataBuilder builder, string deformerClassName, SkinBlendArrayData blend)
	{
		if (blend.Blocks.Count == 0)
		{
			return null;
		}
		string typeName = deformerClassName + "::" + ToBlendBlockTypeName(blend.MemberName);
		bool flag = string.Equals(deformerClassName, "hclObjectSpaceDeformer", StringComparison.Ordinal);
		int num = AlignUp(checked((blend.VertexCount + blend.BoneCount) * 2 + ((flag && blend.BlendCount > 1) ? blend.BoneCount : 0)), 16);
		uint blockTypeIndex = flag
			? NativeBphclGameReadyReflectionCatalog.GetTypeLayout(typeName).TypeIndex
			: RequireArrayElementType(deformerClassName, blend.MemberName);
		DataBuilder.Allocation allocation = builder.AllocateArray(blockTypeIndex, blend.Blocks.Count, num, 16);
		for (int i = 0; i < blend.Blocks.Count; i++)
		{
			SkinBlendBlockData skinBlendBlockData = blend.Blocks[i];
			uint num2 = allocation.DataOffset + checked((uint)(i * num));
			for (int j = 0; j < skinBlendBlockData.VertexIndices.Count; j++)
			{
				builder.WriteUInt16(num2 + checked((uint)(j * 2)), skinBlendBlockData.VertexIndices[j]);
			}
			uint num3 = num2 + checked((uint)(skinBlendBlockData.VertexIndices.Count * 2));
			for (int k = 0; k < skinBlendBlockData.BoneIndices.Count; k++)
			{
				builder.WriteUInt16(num3 + checked((uint)(k * 2)), skinBlendBlockData.BoneIndices[k]);
			}
			uint num4 = num3 + checked((uint)(skinBlendBlockData.BoneIndices.Count * 2));
			for (int l = 0; l < skinBlendBlockData.BoneWeights.Count; l++)
			{
				builder.WriteByte(num4 + checked((uint)l), skinBlendBlockData.BoneWeights[l]);
			}
		}
		return allocation;
	}

	private static DataBuilder.Allocation? WritePackedObjectPositionBlocks(DataBuilder builder, IReadOnlyList<SkinPositionBlockData> blocks)
	{
		if (blocks.Count == 0)
		{
			return null;
		}
		DataBuilder.Allocation allocation = builder.AllocateArray(NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclObjectSpaceDeformer::LocalBlockP").TypeIndex, blocks.Count, 128, 16);
		short exponent = CalculatePackedObjectPositionExponent(blocks);
		for (int i = 0; i < blocks.Count; i++)
		{
			SkinPositionBlockData skinPositionBlockData = blocks[i];
			IReadOnlyList<short> nativePackedValues = skinPositionBlockData.NativePackedValues;
			byte[] array = ((nativePackedValues != null && nativePackedValues.Count == 64) ? WriteNativePackedObjectPositionBlock(nativePackedValues) : PackObjectPositionBlock(skinPositionBlockData, exponent));
			builder.WriteBytes(allocation.DataOffset + checked((uint)(i * 128)), array);
		}
		return allocation;
	}

	private static DataBuilder.Allocation? WriteUnpackedPositionBlocks(DataBuilder builder, string typeName, IReadOnlyList<SkinPositionBlockData> blocks)
	{
		if (blocks.Count == 0)
		{
			return null;
		}
		DataBuilder.Allocation allocation = builder.AllocateArray(NativeBphclGameReadyReflectionCatalog.GetTypeLayout(typeName).TypeIndex, blocks.Count, 256, 16);
		for (int i = 0; i < blocks.Count; i++)
		{
			uint num = allocation.DataOffset + checked((uint)(i * 256));
			for (int j = 0; j < 16; j++)
			{
				builder.WriteVector4(num + checked((uint)(j * 16)), blocks[i].Positions[j]);
			}
		}
		return allocation;
	}

	private static byte[] WriteNativePackedObjectPositionBlock(IReadOnlyList<short> values)
	{
		byte[] array = new byte[values.Count * 2];
		for (int i = 0; i < values.Count; i++)
		{
			BinaryPrimitives.WriteInt16LittleEndian(array.AsSpan(i * 2, 2), values[i]);
		}
		return array;
	}

	internal static short CalculatePackedObjectPositionExponent(IReadOnlyList<SkinPositionBlockData> blocks)
	{
		float num = blocks.SelectMany((SkinPositionBlockData block) => block.Positions).SelectMany((Vector4 position) => new float[3]
		{
			MathF.Abs(position.X),
			MathF.Abs(position.Y),
			MathF.Abs(position.Z)
		}).DefaultIfEmpty()
			.Max();
		if (num < 1E-10f)
		{
			return 0;
		}
		checked
		{
			return (short)Math.Clamp(unchecked((int)MathF.Ceiling(MathF.Log2(num / 32767f) + 111f)) * 128, -32768, 32767);
		}
	}

	internal static byte[] PackObjectPositionBlock(SkinPositionBlockData block, short exponent)
	{
		float scale = MathF.Pow(2f, (float)exponent / 128f - 111f);
		byte[] array = new byte[128];
		for (int i = 0; i < 16; i++)
		{
			Vector4 vector = block.Positions[i];
			WritePackedComponent(array, i * 4 * 2, vector.X, scale);
			WritePackedComponent(array, (i * 4 + 1) * 2, vector.Y, scale);
			WritePackedComponent(array, (i * 4 + 2) * 2, vector.Z, scale);
			BinaryPrimitives.WriteInt16LittleEndian(array.AsSpan((i * 4 + 3) * 2, 2), exponent);
		}
		return array;
	}

	private static void WritePackedComponent(byte[] bytes, int offset, float value, float scale)
	{
		int value2 = ((!(scale <= 0f)) ? ((int)MathF.Round(value / scale)) : 0);
		BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, 2), checked((short)Math.Clamp(value2, -32768, 32767)));
	}

	private static void WriteArrayField(DataBuilder builder, uint fieldOffset, DataBuilder.Allocation? storage, uint? patchTypeOverride = null)
	{
		if (storage != null)
		{
			builder.WriteReference(fieldOffset, storage.ItemIndex, patchTypeOverride);
			builder.WriteUInt32(fieldOffset + 8, storage.Count);
		}
	}

	private static uint RequireMemberOffset(string className, string memberName)
	{
		return NativeBphclGameReadyReflectionCatalog.GetMemberOffset(className, memberName) ?? throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no member '{className}.{memberName}'.");
	}

	private static uint RequireArrayElementType(string className, string memberName)
	{
		return NativeBphclGameReadyReflectionCatalog.GetArrayElementType(RequireArrayType(className, memberName)) ?? throw new InvalidDataException($"'{className}.{memberName}' is not a reflected hkArray.");
	}

	private static uint RequireArrayType(string className, string memberName)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout(className);
		uint num = NativeBphclGameReadyReflectionCatalog.GetMemberTypeIndex(memberOffset: RequireMemberOffset(className, memberName), typeIndex: typeLayout.TypeIndex) ?? throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no member type for '{className}.{memberName}'.");
		if (!string.Equals(NativeBphclGameReadyReflectionCatalog.GetTypeName(num), "hkArray", StringComparison.Ordinal))
		{
			throw new InvalidDataException($"'{className}.{memberName}' is not a reflected hkArray.");
		}
		return num;
	}

	private static string ToBlendBlockTypeName(string memberName)
	{
		return memberName switch
		{
			"eightBlendEntries" => "EightBlendEntryBlock",
			"sevenBlendEntries" => "SevenBlendEntryBlock",
			"sixBlendEntries" => "SixBlendEntryBlock",
			"fiveBlendEntries" => "FiveBlendEntryBlock",
			"fourBlendEntries" => "FourBlendEntryBlock",
			"threeBlendEntries" => "ThreeBlendEntryBlock",
			"twoBlendEntries" => "TwoBlendEntryBlock",
			"oneBlendEntries" => "OneBlendEntryBlock",
			_ => throw new InvalidDataException("Unknown skin blend array '" + memberName + "'."),
		};
	}

	private static int AlignUp(int value, int alignment)
	{
		checked
		{
			return unchecked(checked(value + alignment - 1) / alignment) * alignment;
		}
	}

	private static ClothStateBufferAccessData CreateOperatorBufferAccess(int bufferIndex, byte componentFlags, bool trianglesRead = false)
	{
		return new ClothStateBufferAccessData(bufferIndex, componentFlags, 0, 0, 0, trianglesRead, bufferIndex);
	}

	private static ClothStateTransformSetAccessData CreateOperatorTransformAccess(int transformSetIndex, byte componentFlags, IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers = null)
	{
		return new ClothStateTransformSetAccessData(transformSetIndex, componentFlags, 0, transformTrackers ?? Array.Empty<ClothStateTransformTrackerData>());
	}

	private static IReadOnlyDictionary<int, IReadOnlyList<ClothStateTransformTrackerData>> BuildSourceOperatorTransformTrackerPlans(StandaloneBphclClothGraph cloth)
	{
		IReadOnlyList<ClothStateRuntimeData> states = cloth.Runtime.States;
		if (states.Count == 0)
		{
			return new Dictionary<int, IReadOnlyList<ClothStateTransformTrackerData>>();
		}
		ClothStateRuntimeData state = states.FirstOrDefault((ClothStateRuntimeData clothStateRuntimeData2) => string.Equals(clothStateRuntimeData2.Name, "Default", StringComparison.OrdinalIgnoreCase)) ?? states.OrderBy((ClothStateRuntimeData clothStateRuntimeData2) => clothStateRuntimeData2.Index).First();
		ClothStateRuntimeData clothStateRuntimeData = states.FirstOrDefault((ClothStateRuntimeData clothStateRuntimeData2) => clothStateRuntimeData2.Name.Contains("AnimToSimCurrent", StringComparison.OrdinalIgnoreCase)) ?? states.FirstOrDefault((ClothStateRuntimeData clothStateRuntimeData2) => clothStateRuntimeData2.Name.Contains("AnimToSim", StringComparison.OrdinalIgnoreCase));
		Dictionary<int, IReadOnlyList<ClothStateTransformTrackerData>> dictionary = new Dictionary<int, IReadOnlyList<ClothStateTransformTrackerData>>();
		foreach (ClothOperatorData @operator in cloth.Runtime.Operators)
		{
			int sourceOperatorTransformSetIndex = GetSourceOperatorTransformSetIndex(cloth, @operator);
			IReadOnlyList<ClothStateTransformTrackerData> stateTransformTrackers = GetStateTransformTrackers(state, sourceOperatorTransformSetIndex);
			IReadOnlyList<ClothStateTransformTrackerData> readOnlyList;
			IReadOnlyList<ClothStateTransformTrackerData> readOnlyList2;
			if ((object)clothStateRuntimeData != null)
			{
				readOnlyList = GetStateTransformTrackers(clothStateRuntimeData, sourceOperatorTransformSetIndex);
			}
			else
			{
				readOnlyList2 = Array.Empty<ClothStateTransformTrackerData>();
				readOnlyList = readOnlyList2;
			}
			IReadOnlyList<ClothStateTransformTrackerData> readOnlyList3 = readOnlyList;
			switch (@operator.ClassName)
			{
			case "hclObjectSpaceSkinPOperator":
			case "hclBoneSpaceSkinPOperator":
				readOnlyList2 = ((readOnlyList3.Count > 0) ? ExtractTrackerReads(readOnlyList3) : ExtractTrackerReads(stateTransformTrackers));
				break;
			case "hclSimulateOperator":
				readOnlyList2 = BuildSimulateTransformTrackers(stateTransformTrackers, readOnlyList3, cloth, @operator);
				break;
			case "hclSimpleMeshBoneDeformOperator":
				readOnlyList2 = ExtractTrackerWrites(stateTransformTrackers);
				break;
			default:
				readOnlyList2 = Array.Empty<ClothStateTransformTrackerData>();
				break;
			}
			IReadOnlyList<ClothStateTransformTrackerData> readOnlyList4 = readOnlyList2;
			if (HasTrackedTransforms(readOnlyList4))
			{
				dictionary[@operator.Index] = readOnlyList4;
			}
		}
		return dictionary;
	}

	private static IReadOnlyList<ClothStateTransformSetAccessData> GetOperatorTransformAccesses(ClothOperatorData source, IReadOnlyDictionary<int, IReadOnlyList<ClothStateTransformTrackerData>> trackerPlans, bool preservesNativeBphclSchedule)
	{
		IReadOnlyList<ClothStateTransformSetAccessData> readOnlyList = source.OperatorTransformSetAccesses ?? Array.Empty<ClothStateTransformSetAccessData>();
		if (preservesNativeBphclSchedule)
		{
			return readOnlyList;
		}
		if (!trackerPlans.TryGetValue(source.Index, out IReadOnlyList<ClothStateTransformTrackerData> plannedTrackers))
		{
			if (!string.Equals(source.ClassName, "hclSimulateOperator", StringComparison.Ordinal))
			{
				return readOnlyList;
			}
			return Array.Empty<ClothStateTransformSetAccessData>();
		}
		if (readOnlyList.Count > 0)
		{
			return readOnlyList.Select((ClothStateTransformSetAccessData access) => access with
			{
				ComponentFlags0 = (byte)(string.Equals(source.ClassName, "hclSimpleMeshBoneDeformOperator", StringComparison.Ordinal) ? 2 : access.ComponentFlags0),
				TransformTrackers = plannedTrackers
			}).ToArray();
		}
		byte componentFlags = (byte)(string.Equals(source.ClassName, "hclSimpleMeshBoneDeformOperator", StringComparison.Ordinal) ? 2 : 9);
		return new ClothStateTransformSetAccessData[1] { CreateOperatorTransformAccess(source.TransformSetIndex.GetValueOrDefault(), componentFlags, plannedTrackers) };
	}

	private static IReadOnlyList<ClothStateTransformTrackerData>? GetSourceOperatorTransformTrackers(IReadOnlyDictionary<int, IReadOnlyList<ClothStateTransformTrackerData>> plans, int operatorIndex)
	{
		if (!plans.TryGetValue(operatorIndex, out IReadOnlyList<ClothStateTransformTrackerData> value))
		{
			return null;
		}
		return value;
	}

	private static int GetSourceOperatorTransformSetIndex(StandaloneBphclClothGraph cloth, ClothOperatorData source)
	{
		return cloth.SkinOperators.FirstOrDefault((SkinOperatorData candidate) => candidate.OperatorIndex == source.Index)?.TransformSetIndex ?? cloth.BoneDeformers.FirstOrDefault((StandaloneBphclSimpleMeshBoneDeformer candidate) => candidate.OperatorIndex == source.Index)?.OutputTransformSetIndex ?? source.TransformSetIndex.GetValueOrDefault();
	}

	private static IReadOnlyList<ClothStateTransformTrackerData> GetStateTransformTrackers(ClothStateRuntimeData state, int transformSetIndex)
	{
		return state.TransformSetAccesses?.FirstOrDefault((ClothStateTransformSetAccessData access) => access.TransformSetIndex == transformSetIndex)?.TransformTrackers ?? Array.Empty<ClothStateTransformTrackerData>();
	}

	private static IReadOnlyList<ClothStateTransformTrackerData> ExtractTrackerReads(IReadOnlyList<ClothStateTransformTrackerData> source)
	{
		return source.Select((ClothStateTransformTrackerData tracker) => new ClothStateTransformTrackerData(tracker.Read, tracker.ReadBeforeWrite, EmptyBitFieldLike(tracker.Written))).ToArray();
	}

	private static IReadOnlyList<ClothStateTransformTrackerData> BuildSimulateTransformTrackers(IReadOnlyList<ClothStateTransformTrackerData> defaultTrackers, IReadOnlyList<ClothStateTransformTrackerData> animationTrackers, StandaloneBphclClothGraph cloth, ClothOperatorData simulateOperator)
	{
		ClothStateTransformTrackerData[] array = SubtractTrackerReads(defaultTrackers, animationTrackers).ToArray();
		if (array.Length == 0 || cloth.ColliderBindings.Count == 0)
		{
			return array;
		}
		IReadOnlyList<int> usedColliderSlots = GetUsedColliderSlots(cloth, simulateOperator);
		if (usedColliderSlots.Count == 0)
		{
			return array;
		}
		int[] colliderBoneIndices = (from slot in usedColliderSlots
			where slot >= 0 && slot < cloth.ColliderBindings.Count
			select cloth.ColliderBindings[slot].BoneIndex).Distinct().ToArray();
		if (colliderBoneIndices.Length == 0)
		{
			return array;
		}
		return array.Select((ClothStateTransformTrackerData tracker, int index) => (index != 0) ? EmptyTransformTrackerLike(tracker) : new ClothStateTransformTrackerData(AddBitFieldIndices(tracker.Read, colliderBoneIndices), AddBitFieldIndices(tracker.ReadBeforeWrite, colliderBoneIndices), EmptyBitFieldLike(tracker.Written))).ToArray();
	}

	private static IReadOnlyList<int> GetUsedColliderSlots(StandaloneBphclClothGraph cloth, ClothOperatorData simulateOperator)
	{
		if (simulateOperator.SimulateConfigs.Count == 0)
		{
			return Array.Empty<int>();
		}
		HashSet<int> hashSet = new HashSet<int>();
		foreach (ClothSimulateConfigData simulateConfig in simulateOperator.SimulateConfigs)
		{
			if (simulateConfig.UseAllInstanceCollidables || simulateConfig.InstanceCollidablesUsed.Count == 0)
			{
				for (int i = 0; i < cloth.ColliderBindings.Count; i++)
				{
					hashSet.Add(i);
				}
				continue;
			}
			foreach (int item in simulateConfig.InstanceCollidablesUsed)
			{
				hashSet.Add(item);
			}
		}
		return hashSet.OrderBy((int slot) => slot).ToArray();
	}

	private static IReadOnlyList<ClothStateTransformTrackerData> ExtractTrackerWrites(IReadOnlyList<ClothStateTransformTrackerData> source)
	{
		return source.Select((ClothStateTransformTrackerData tracker) => new ClothStateTransformTrackerData(EmptyBitFieldLike(tracker.Read), EmptyBitFieldLike(tracker.ReadBeforeWrite), tracker.Written)).ToArray();
	}

	private static IReadOnlyList<ClothStateTransformTrackerData> SubtractTrackerReads(IReadOnlyList<ClothStateTransformTrackerData> whole, IReadOnlyList<ClothStateTransformTrackerData> consumed)
	{
		if (whole.Count == 0)
		{
			return Array.Empty<ClothStateTransformTrackerData>();
		}
		return whole.Select(delegate(ClothStateTransformTrackerData tracker, int index)
		{
			ClothStateTransformTrackerData clothStateTransformTrackerData = ((index < consumed.Count) ? consumed[index] : null);
			return new ClothStateTransformTrackerData(SubtractBitField(tracker.Read, clothStateTransformTrackerData?.Read), SubtractBitField(tracker.ReadBeforeWrite, clothStateTransformTrackerData?.ReadBeforeWrite), EmptyBitFieldLike(tracker.Written));
		}).ToArray();
	}

	private static ClothStateBitFieldData SubtractBitField(ClothStateBitFieldData whole, ClothStateBitFieldData? consumed)
	{
		return new ClothStateBitFieldData(whole.Words.Select((uint word, int index) => word & ~(consumed?.Words.ElementAtOrDefault(index) ?? 0)).ToArray(), whole.NumBits);
	}

	private static ClothStateBitFieldData AddBitFieldIndices(ClothStateBitFieldData source, IReadOnlyList<int> indices)
	{
		uint[] array = source.Words.ToArray();
		foreach (int index in indices)
		{
			if (index >= 0 && index < source.NumBits)
			{
				int num = index / 32;
				if (num < array.Length)
				{
					array[num] |= (uint)(1 << index % 32);
				}
			}
		}
		return new ClothStateBitFieldData(array, source.NumBits);
	}

	private static ClothStateBitFieldData EmptyBitFieldLike(ClothStateBitFieldData source)
	{
		return new ClothStateBitFieldData(Enumerable.Repeat(0u, source.Words.Count).ToArray(), source.NumBits);
	}

	private static bool HasTrackedTransforms(IReadOnlyList<ClothStateTransformTrackerData> trackers)
	{
		return trackers.Any((ClothStateTransformTrackerData tracker) => tracker.Read.Words.Any((uint word) => word != 0) || tracker.ReadBeforeWrite.Words.Any((uint word) => word != 0) || tracker.Written.Words.Any((uint word) => word != 0));
	}

	private static IReadOnlyList<ClothStateBufferAccessData> CreateMoveParticlesBufferAccesses()
	{
		return new ClothStateBufferAccessData[3]
		{
			CreateOperatorBufferAccess(0, 1),
			CreateOperatorBufferAccess(1, 2),
			CreateOperatorBufferAccess(2, 2)
		};
	}

	private static IReadOnlyList<ClothStateBufferAccessData> CreateSimulateBufferAccesses()
	{
		return new ClothStateBufferAccessData[3]
		{
			CreateOperatorBufferAccess(0, 1),
			CreateOperatorBufferAccess(1, 15),
			CreateOperatorBufferAccess(2, 15)
		};
	}

	private static void WriteOperatorBase(DataBuilder builder, uint dataOffset, int operatorId, string name, IReadOnlyList<ClothStateBufferAccessData> bufferAccesses, IReadOnlyList<ClothStateTransformSetAccessData> transformSetAccesses)
	{
		builder.WriteReference(dataOffset + 24, builder.WriteString(name));
		builder.WriteUInt32(dataOffset + 32, checked((uint)operatorId));
		builder.WriteUInt32(dataOffset + 36, 0u);
		if (bufferAccesses.Count > 0)
		{
			DataBuilder.Allocation allocation = WriteClothStateBufferAccesses(builder, bufferAccesses);
			builder.WriteReference(dataOffset + 40, allocation.ItemIndex);
			builder.WriteUInt32(dataOffset + 48, checked((uint)bufferAccesses.Count));
		}
		if (transformSetAccesses.Count > 0)
		{
			DataBuilder.Allocation allocation2 = WriteOperatorTransformSetAccesses(builder, transformSetAccesses);
			builder.WriteReference(dataOffset + 56, allocation2.ItemIndex);
			builder.WriteUInt32(dataOffset + 64, checked((uint)transformSetAccesses.Count));
		}
	}

	private static int WriteMoveParticlesOperator(DataBuilder builder, StandaloneBphclClothGraph cloth, int operatorId)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclMoveParticlesOperator");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 96), 16);
		WriteOperatorBase(builder, allocation.DataOffset, operatorId, "MoveFixedParticles", CreateMoveParticlesBufferAccesses(), Array.Empty<ClothStateTransformSetAccessData>());
		StandaloneBphclParticle[] array = cloth.Particles.Where((StandaloneBphclParticle particle) => particle.Fixed).ToArray();
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclMoveParticlesOperator::VertexParticlePair");
		DataBuilder.Allocation allocation2 = builder.AllocateArray(typeLayout2.TypeIndex, array.Length, RequireSize(typeLayout2, 4), 2);
		for (int num = 0; num < array.Length; num++)
		{
			uint num2 = allocation2.DataOffset + checked((uint)(num * RequireSize(typeLayout2, 4)));
			builder.WriteUInt16(num2, checked((ushort)array[num].Index));
			builder.WriteUInt16(num2 + 2, checked((ushort)array[num].Index));
		}
		builder.WriteReference(allocation.DataOffset + 72, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 80, checked((uint)array.Length));
		builder.WriteUInt32(allocation.DataOffset + 88, 0u);
		builder.WriteUInt32(allocation.DataOffset + 92, 0u);
		return allocation.ItemIndex;
	}

	private static int WriteMoveParticlesOperator(DataBuilder builder, ClothOperatorData source, int operatorId)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclMoveParticlesOperator");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 96), 16);
		WriteOperatorBase(builder, allocation.DataOffset, operatorId, "MoveFixedParticles", CreateMoveParticlesBufferAccesses(), Array.Empty<ClothStateTransformSetAccessData>());
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclMoveParticlesOperator::VertexParticlePair");
		DataBuilder.Allocation allocation2 = builder.AllocateArray(typeLayout2.TypeIndex, source.VertexParticlePairs.Count, RequireSize(typeLayout2, 4), 2);
		for (int i = 0; i < source.VertexParticlePairs.Count; i++)
		{
			ClothVertexParticlePairData clothVertexParticlePairData = source.VertexParticlePairs[i];
			uint num = allocation2.DataOffset + checked((uint)(i * RequireSize(typeLayout2, 4)));
			builder.WriteUInt16(num, clothVertexParticlePairData.VertexIndex);
			builder.WriteUInt16(num + 2, clothVertexParticlePairData.ParticleIndex);
		}
		builder.WriteReference(allocation.DataOffset + 72, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 80, checked((uint)source.VertexParticlePairs.Count));
		builder.WriteUInt32(allocation.DataOffset + 88, checked((uint)source.SimulationClothIndex.GetValueOrDefault()));
		builder.WriteUInt32(allocation.DataOffset + 92, checked((uint)source.ReferenceBufferIndex.GetValueOrDefault()));
		return allocation.ItemIndex;
	}

	private static int WriteSimulateOperator(DataBuilder builder, StandaloneBphclClothGraph cloth, int constraintSetCount, int operatorId, IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers = null, IReadOnlyList<ClothStateTransformSetAccessData>? transformSetAccesses = null)
	{
		if (cloth.GeneratedSimulateConfig is { } config)
		{
			var source = new ClothOperatorData(operatorId, "hclSimulateOperator", null, null, null, null, null, null,
				Array.Empty<short>(), false, false, Array.Empty<ClothVertexParticlePairData>(), 0, null, new[] { config });
			return WriteSimulateOperator(builder, cloth, source, constraintSetCount, operatorId, transformTrackers, transformSetAccesses, true);
		}
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclSimulateOperator");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 96), 16);
		IReadOnlyList<ClothStateTransformSetAccessData> transformSetAccesses2 = transformSetAccesses ??
			(cloth.Colliders.Count == 0 && cloth.BonePlaneSets.Count == 0 ? Array.Empty<ClothStateTransformSetAccessData>() :
			new[] { CreateOperatorTransformAccess(0, 9, transformTrackers) });
		WriteOperatorBase(builder, allocation.DataOffset, operatorId, "Simulate", CreateSimulateBufferAccesses(), transformSetAccesses2);
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclSimulateOperator::Config");
		DataBuilder.Allocation allocation2 = builder.AllocateArray(typeLayout2.TypeIndex, 1, RequireSize(typeLayout2, 48));
		int[] array = Enumerable.Range(0, constraintSetCount).Append(-1).ToArray();
		DataBuilder.Allocation allocation3 = builder.WriteInt32Array(array, RequireArrayElementType("hclSimulateOperator::Config", "constraintExecution"));
		builder.WriteUInt32(allocation.DataOffset + 72, 0u);
		builder.WriteReference(allocation.DataOffset + 80, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 88, 1u);
		builder.WriteReference(allocation2.DataOffset, builder.WriteString("Default Config"));
		builder.WriteReference(allocation2.DataOffset + 8, allocation3.ItemIndex);
		builder.WriteUInt32(allocation2.DataOffset + 16, checked((uint)array.Length));
		builder.WriteByte(allocation2.DataOffset + 40, 1);
		builder.WriteByte(allocation2.DataOffset + 41, 1);
		builder.WriteByte(allocation2.DataOffset + 42, 1);
		builder.WriteByte(allocation2.DataOffset + 43, 1);
		return allocation.ItemIndex;
	}

	private static int WriteSimulateOperator(DataBuilder builder, StandaloneBphclClothGraph cloth, ClothOperatorData source, int constraintSetCount, int operatorId, IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers, IReadOnlyList<ClothStateTransformSetAccessData>? transformSetAccesses, bool preservesNativeBphclSchedule)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclSimulateOperator");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 96), 16);
		IReadOnlyList<ClothStateTransformSetAccessData> transformSetAccesses2 = transformSetAccesses ??
			(cloth.Colliders.Count == 0 && cloth.BonePlaneSets.Count == 0 ? Array.Empty<ClothStateTransformSetAccessData>() :
			new[] { CreateOperatorTransformAccess(0, 9, transformTrackers) });
		WriteOperatorBase(builder, allocation.DataOffset, operatorId, "Simulate", CreateSimulateBufferAccesses(), transformSetAccesses2);
		IReadOnlyList<ClothSimulateConfigData> readOnlyList2;
		if (source.SimulateConfigs.Count <= 0)
		{
			IReadOnlyList<ClothSimulateConfigData> readOnlyList = new ClothSimulateConfigData[1]
			{
				new ClothSimulateConfigData(Enumerable.Range(0, constraintSetCount).Append(-1).ToArray(), Array.Empty<int>(), 1, 1, UseAllInstanceCollidables: true, AdaptConstraintStiffness: true)
			};
			readOnlyList2 = readOnlyList;
		}
		else
		{
			readOnlyList2 = source.SimulateConfigs;
		}
		IReadOnlyList<ClothSimulateConfigData> readOnlyList3 = readOnlyList2;
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclSimulateOperator::Config");
		DataBuilder.Allocation allocation2 = builder.AllocateArray(typeLayout2.TypeIndex, readOnlyList3.Count, RequireSize(typeLayout2, 48));
		builder.WriteUInt32(allocation.DataOffset + 72, checked((uint)source.SimulationClothIndex.GetValueOrDefault()));
		builder.WriteReference(allocation.DataOffset + 80, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 88, checked((uint)readOnlyList3.Count));
		for (int i = 0; i < readOnlyList3.Count; i++)
		{
			ClothSimulateConfigData config = readOnlyList3[i];
			uint num = allocation2.DataOffset + checked((uint)(i * RequireSize(typeLayout2, 48)));
			int[] array = ConvertHkclConstraintExecutionToBphcl(cloth, config.ConstraintExecution, constraintSetCount, preservesNativeBphclSchedule);
			DataBuilder.Allocation allocation3 = builder.WriteInt32Array(array, RequireArrayElementType("hclSimulateOperator::Config", "constraintExecution"));
			bool flag = config.InstanceCollidablesUsed.Count == 0 || config.UseAllInstanceCollidables;
			bool[] array2 = (flag ? Array.Empty<bool>() : (from colliderSlot in Enumerable.Range(0, cloth.ColliderBindings.Count)
				select config.InstanceCollidablesUsed.Contains(colliderSlot)).ToArray());
			DataBuilder.Allocation allocation4 = ((array2.Length != 0) ? builder.WriteBoolArray(array2, RequireArrayElementType("hclSimulateOperator::Config", "instanceCollidablesUsed")) : null);
			builder.WriteReference(num, builder.WriteString("Default Config"));
			builder.WriteReference(num + 8, allocation3.ItemIndex);
			builder.WriteUInt32(num + 16, checked((uint)array.Length));
			if (allocation4 != null)
			{
				builder.WriteReference(num + 24, allocation4.ItemIndex);
			}
			builder.WriteUInt32(num + 32, checked((uint)array2.Length));
			builder.WriteByte(num + 40, (byte)((config.SubSteps == 0) ? 1 : config.SubSteps));
			builder.WriteByte(num + 41, (byte)((config.NumberOfSolveIterations == 0) ? 1 : config.NumberOfSolveIterations));
			builder.WriteByte(num + 42, flag ? ((byte)1) : ((byte)0));
			builder.WriteByte(num + 43, config.AdaptConstraintStiffness ? ((byte)1) : ((byte)0));
		}
		return allocation.ItemIndex;
	}

	private static int[] ConvertHkclConstraintExecutionToBphcl(StandaloneBphclClothGraph cloth, IReadOnlyList<int> sourceExecution, int constraintSetCount, bool preservesNativeBphclSchedule)
	{
		if (preservesNativeBphclSchedule && sourceExecution.Count == 0)
			return Array.Empty<int>();
		HashSet<int> availableConstraintSetIndices = cloth.ConstraintSets.Select((StandaloneBphclConstraintSetMetadata constraintSet) => constraintSet.Index).ToHashSet();
		int[] array = sourceExecution.Where((int index) => index == -1 || availableConstraintSetIndices.Contains(index)).ToArray();
		if (array.Length != 0)
		{
			if (preservesNativeBphclSchedule)
			{
				return array;
			}
			return NormalizeFreshHkclConstraintExecution(cloth, array);
		}
		return Enumerable.Range(0, constraintSetCount).Where(availableConstraintSetIndices.Contains).Append(-1)
			.ToArray();
	}

	private static int[] NormalizeFreshHkclConstraintExecution(StandaloneBphclClothGraph cloth, IReadOnlyList<int> execution)
	{
		bool num = cloth.ConstraintSets.Any((StandaloneBphclConstraintSetMetadata set) => string.Equals(set.TypeName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal));
		HashSet<int> hashSet = (from set in cloth.ConstraintSets
			where string.Equals(set.TypeName, "hclBendLinkConstraintSet", StringComparison.Ordinal)
			select set.Index).ToHashSet();
		bool flag = execution.Count(hashSet.Contains) == 1;
		if (!num && !flag)
		{
			if (!execution.Contains(-1))
			{
				return execution.Append(-1).ToArray();
			}
			return execution.ToArray();
		}
		HashSet<int> transitionSetIndices = (from set in cloth.ConstraintSets
			where string.Equals(set.TypeName, "hclTransitionConstraintSet", StringComparison.Ordinal)
			select set.Index).ToHashSet();
		int[] array = execution.Where(transitionSetIndices.Contains).ToArray();
		if (array.Length == 0)
		{
			if (!execution.Contains(-1))
			{
				return execution.Append(-1).ToArray();
			}
			return execution.ToArray();
		}
		List<int> list = execution.Where((int index) => index >= 0 && !transitionSetIndices.Contains(index)).ToList();
		list.Add(-1);
		list.AddRange(array);
		return list.ToArray();
	}

	private static int WriteSimpleMeshBoneDeformOperator(DataBuilder builder, StandaloneBphclSimpleMeshBoneDeformer deformer, int operatorId, IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers = null, IReadOnlyList<ClothStateTransformSetAccessData>? transformSetAccesses = null)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclSimpleMeshBoneDeformOperator");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 120), 16);
		WriteOperatorBase(builder, allocation.DataOffset, operatorId, "MeshBone", new ClothStateBufferAccessData[1] { CreateOperatorBufferAccess(deformer.InputBufferIndex, 1, trianglesRead: true) }, transformSetAccesses ?? new ClothStateTransformSetAccessData[1] { CreateOperatorTransformAccess(deformer.OutputTransformSetIndex, 2, transformTrackers) });
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclSimpleMeshBoneDeformOperator::TriangleBonePair");
		DataBuilder.Allocation allocation2 = builder.AllocateArray(typeLayout2.TypeIndex, deformer.TriangleBonePairs.Count, RequireSize(typeLayout2, 4), 2);
		for (int i = 0; i < deformer.TriangleBonePairs.Count; i++)
		{
			SimpleMeshTriangleBonePairData simpleMeshTriangleBonePairData = deformer.TriangleBonePairs[i];
			uint num = allocation2.DataOffset + checked((uint)(i * RequireSize(typeLayout2, 4)));
			builder.WriteUInt16(num, simpleMeshTriangleBonePairData.BoneOffset);
			builder.WriteUInt16(num + 2, simpleMeshTriangleBonePairData.TriangleOffset);
		}
		IReadOnlyList<Matrix4x4> readOnlyList2;
		if (deformer.LocalBoneTransforms.Count != deformer.TriangleBonePairs.Count)
		{
			IReadOnlyList<Matrix4x4> readOnlyList = Enumerable.Repeat(Matrix4x4.Identity, deformer.TriangleBonePairs.Count).ToArray();
			readOnlyList2 = readOnlyList;
		}
		else
		{
			readOnlyList2 = deformer.LocalBoneTransforms;
		}
		IReadOnlyList<Matrix4x4> readOnlyList3 = readOnlyList2;
		DataBuilder.Allocation allocation3 = builder.WriteMatrix4Array(readOnlyList3);
		builder.WriteUInt32(allocation.DataOffset + 72, checked((uint)deformer.InputBufferIndex));
		builder.WriteUInt32(allocation.DataOffset + 76, checked((uint)deformer.OutputTransformSetIndex));
		builder.WriteReference(allocation.DataOffset + 80, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 88, checked((uint)deformer.TriangleBonePairs.Count));
		builder.WriteReference(allocation.DataOffset + 96, allocation3.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 104, checked((uint)readOnlyList3.Count));
		builder.WriteInt32(allocation.DataOffset + 112, deformer.BoneAxis);
		return allocation.ItemIndex;
	}

	private static int WriteGatherAllVerticesOperator(DataBuilder builder, ClothOperatorData source)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclGatherAllVerticesOperator");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 104), 16);
		string name = source.Name ?? string.Empty;
		WriteOperatorBase(builder, allocation.DataOffset, source.Index, name, new ClothStateBufferAccessData[2]
		{
			CreateOperatorBufferAccess(source.InputBufferIndex.GetValueOrDefault(), 1),
			CreateOperatorBufferAccess(source.OutputBufferIndex.GetValueOrDefault(), 6)
		}, Array.Empty<ClothStateTransformSetAccessData>());
		DataBuilder.Allocation allocation2 = builder.WriteInt16Array(source.VertexInputFromVertexOutput);
		builder.WriteReference(allocation.DataOffset + 72, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 80, checked((uint)source.VertexInputFromVertexOutput.Count));
		builder.WriteUInt32(allocation.DataOffset + 88, checked((uint)source.InputBufferIndex.GetValueOrDefault()));
		builder.WriteUInt32(allocation.DataOffset + 92, checked((uint)source.OutputBufferIndex.GetValueOrDefault()));
		builder.WriteByte(allocation.DataOffset + 96, source.GatherNormals ? ((byte)1) : ((byte)0));
		builder.WriteByte(allocation.DataOffset + 97, source.PartialGather ? ((byte)1) : ((byte)0));
		return allocation.ItemIndex;
	}

	private static int WriteCopyVerticesOperator(DataBuilder builder, int inputBufferIndex, int outputBufferIndex, int vertexCount, int startVertexIn = 0, int startVertexOut = 0, int operatorId = 0)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclCopyVerticesOperator");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 96), 16);
		WriteOperatorBase(builder, allocation.DataOffset, operatorId, outputBufferIndex switch
		{
			2 => "VertexGatherPrev",
			1 => "VertexGatherCurrent",
			_ => "CopyVertices",
		}, new ClothStateBufferAccessData[2]
		{
			CreateOperatorBufferAccess(inputBufferIndex, 1),
			CreateOperatorBufferAccess(outputBufferIndex, 6)
		}, Array.Empty<ClothStateTransformSetAccessData>());
		builder.WriteUInt32(allocation.DataOffset + 72, checked((uint)inputBufferIndex));
		builder.WriteUInt32(allocation.DataOffset + 76, checked((uint)outputBufferIndex));
		builder.WriteUInt32(allocation.DataOffset + 80, checked((uint)vertexCount));
		builder.WriteUInt32(allocation.DataOffset + 84, checked((uint)startVertexIn));
		builder.WriteUInt32(allocation.DataOffset + 88, checked((uint)startVertexOut));
		return allocation.ItemIndex;
	}

	private static int WriteClothState(DataBuilder builder, string name, IReadOnlyList<int> operatorIndices, IReadOnlyList<uint> bufferIndices, IReadOnlyList<uint> transformSetIndices, IReadOnlyList<uint> simulationIndices)
	{
		return WriteClothState(builder, checked(new ClothStateRuntimeData(0, name, operatorIndices, bufferIndices.Select((uint index) => (int)index).ToArray(), transformSetIndices.Select((uint index) => (int)index).ToArray(), simulationIndices.Select((uint index) => (int)index).ToArray(), CreateDefaultBufferAccesses(name, bufferIndices), CreateDefaultTransformSetAccesses(name, transformSetIndices))));
	}

	private static int WriteClothState(DataBuilder builder, ClothStateRuntimeData source)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclClothState");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 104));
		IReadOnlyList<ClothStateBufferAccessData>? bufferAccesses = source.BufferAccesses;
		IReadOnlyList<ClothStateBufferAccessData> accesses;
		IReadOnlyList<ClothStateTransformSetAccessData> readOnlyList;
		DataBuilder.Allocation allocation2;
		DataBuilder.Allocation allocation3;
		DataBuilder.Allocation allocation4;
		DataBuilder.Allocation allocation5;
		int itemIndex;
		checked
		{
			accesses = ((bufferAccesses != null && bufferAccesses.Count > 0) ? source.BufferAccesses : CreateDefaultBufferAccesses(source.Name, source.BufferIndices.Select((int index) => (uint)index).ToArray()));
			accesses = NormalizeNativeStateBufferAccesses(source.Name, accesses);
			IReadOnlyList<ClothStateTransformSetAccessData>? transformSetAccesses = source.TransformSetAccesses;
			readOnlyList = ((transformSetAccesses != null && transformSetAccesses.Count > 0) ? source.TransformSetAccesses : CreateDefaultTransformSetAccesses(source.Name, source.TransformSetIndices.Select((int index) => (uint)index).ToArray()));
			allocation2 = builder.WriteUInt32Array(source.OperatorIndices.Select((int index) => (uint)index).ToArray());
			allocation3 = WriteClothStateBufferAccesses(builder, accesses);
			allocation4 = WriteClothStateTransformSetAccesses(builder, readOnlyList);
			allocation5 = builder.WriteUInt32Array(source.SimulationIndices.Select((int index) => (uint)index).ToArray());
			itemIndex = WriteClothStateDependencyGraph(builder, source.OperatorIndices);
		}
		builder.WriteReference(allocation.DataOffset + 24, builder.WriteString(string.IsNullOrWhiteSpace(source.Name) ? $"State {source.Index}" : source.Name));
		builder.WriteReference(allocation.DataOffset + 32, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 40, checked((uint)source.OperatorIndices.Count));
		builder.WriteReference(allocation.DataOffset + 48, allocation3.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 56, checked((uint)accesses.Count));
		builder.WriteReference(allocation.DataOffset + 64, allocation4.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 72, checked((uint)readOnlyList.Count));
		builder.WriteReference(allocation.DataOffset + 80, allocation5.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 88, checked((uint)source.SimulationIndices.Count));
		builder.WriteReference(allocation.DataOffset + 96, itemIndex);
		return allocation.ItemIndex;
	}

	private static int WriteClothStateDependencyGraph(DataBuilder builder, IReadOnlyList<int> operatorIndices)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclStateDependencyGraph");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 96));
		uint typeIndex = NativeBphclGameReadyReflectionCatalog.GetArrayElementType(NativeBphclGameReadyReflectionCatalog.GetMemberTypeIndex(typeLayout.TypeIndex, 24u) ?? throw new InvalidDataException("hclStateDependencyGraph has no reflected branches field.")) ?? throw new InvalidDataException("hclStateDependencyGraph branches has no element type.");
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclStateDependencyGraph::Branch");
		DataBuilder.Allocation allocation2 = builder.AllocateArray(typeIndex, 1, RequireSize(typeLayout2, 56));
		DataBuilder.Allocation allocation3 = builder.AllocateArray(NativeBphclGameReadyReflectionCatalog.GetTypeIndex("int"), operatorIndices.Count, 4, 4);
		for (int i = 0; i < operatorIndices.Count; i++)
		{
			builder.WriteInt32(allocation3.DataOffset + checked((uint)(i * 4)), i);
		}
		DataBuilder.Allocation allocation4 = builder.AllocateArray(NativeBphclGameReadyReflectionCatalog.GetTypeIndex("int"), 1, 4, 4);
		builder.WriteInt32(allocation4.DataOffset, 0);
		uint nestedArrayType = NativeBphclGameReadyReflectionCatalog.GetMemberTypeIndex(typeLayout.TypeIndex, 56u) ?? throw new InvalidDataException("hclStateDependencyGraph has no reflected children field.");
		DataBuilder.Allocation allocation5 = WriteNestedIntArrays(builder, nestedArrayType, (from index in Enumerable.Range(0, operatorIndices.Count)
			select (index + 1 >= operatorIndices.Count) ? ((IReadOnlyList<int>)Array.Empty<int>()) : ((IReadOnlyList<int>)new int[1] { index + 1 })).ToArray());
		DataBuilder.Allocation allocation6 = WriteNestedIntArrays(builder, nestedArrayType, (from index in Enumerable.Range(0, operatorIndices.Count)
			select (index <= 0) ? ((IReadOnlyList<int>)Array.Empty<int>()) : ((IReadOnlyList<int>)new int[1] { index - 1 })).ToArray());
		uint dataOffset = allocation2.DataOffset;
		builder.WriteInt32(dataOffset, 0);
		builder.WriteReference(dataOffset + 8, allocation3.ItemIndex);
		builder.WriteUInt32(dataOffset + 16, checked((uint)operatorIndices.Count));
		builder.WriteReference(allocation.DataOffset + 24, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 32, 1u);
		builder.WriteReference(allocation.DataOffset + 40, allocation4.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 48, 1u);
		builder.WriteReference(allocation.DataOffset + 56, allocation5.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 64, checked((uint)operatorIndices.Count));
		builder.WriteReference(allocation.DataOffset + 72, allocation6.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 80, checked((uint)operatorIndices.Count));
		builder.WriteByte(allocation.DataOffset + 88, 0);
		return allocation.ItemIndex;
	}

	private static DataBuilder.Allocation WriteNestedIntArrays(DataBuilder builder, uint nestedArrayType, IReadOnlyList<IReadOnlyList<int>> values)
	{
		uint num = NativeBphclGameReadyReflectionCatalog.GetArrayElementType(nestedArrayType) ?? throw new InvalidDataException("BPHCL state dependency array has no nested list type.");
		int num2 = checked((int)NativeBphclGameReadyReflectionCatalog.GetTypeSize(num));
		if (num2 < 16)
		{
			throw new InvalidDataException("BPHCL state dependency nested list is smaller than hkArray.");
		}
		DataBuilder.Allocation allocation = builder.AllocateArray(num, values.Count, num2);
		uint typeIndex = NativeBphclGameReadyReflectionCatalog.GetTypeIndex("int");
		for (int i = 0; i < values.Count; i++)
		{
			IReadOnlyList<int> readOnlyList = values[i];
			if (readOnlyList.Count != 0)
			{
				DataBuilder.Allocation allocation2 = builder.AllocateArray(typeIndex, readOnlyList.Count, 4, 4);
				for (int j = 0; j < readOnlyList.Count; j++)
				{
					builder.WriteInt32(allocation2.DataOffset + checked((uint)(j * 4)), readOnlyList[j]);
				}
				uint dataOffset = allocation.DataOffset + checked((uint)(i * num2));
				builder.WriteReference(dataOffset, allocation2.ItemIndex, num);
			}
		}
		return allocation;
	}

	private static ClothStateRuntimeData NormalizeFreshHkclStateTransformUsage(ClothStateRuntimeData state)
	{
		byte componentFlags0 = (byte)(string.Equals(state.Name, "Default", StringComparison.OrdinalIgnoreCase) ? 11 : 9);
		ClothStateTransformSetAccessData[] transformSetAccesses = state.TransformSetAccesses?.Select((ClothStateTransformSetAccessData access) => new ClothStateTransformSetAccessData(access.TransformSetIndex, componentFlags0, 0, access.TransformTrackers.Select((ClothStateTransformTrackerData tracker, int index) => (index != 0) ? EmptyTransformTrackerLike(tracker) : tracker).ToArray())).ToArray();
		return state with
		{
			TransformSetAccesses = transformSetAccesses
		};
	}

	private static ClothStateTransformTrackerData EmptyTransformTrackerLike(ClothStateTransformTrackerData source)
	{
		return new ClothStateTransformTrackerData(EmptyBitFieldLike(source.Read), EmptyBitFieldLike(source.ReadBeforeWrite), EmptyBitFieldLike(source.Written));
	}

	private static IReadOnlyList<ClothStateBufferAccessData> CreateDefaultBufferAccesses(string stateName, IReadOnlyList<uint> bufferIndices)
	{
		bool isDefault = string.Equals(stateName, "Default", StringComparison.OrdinalIgnoreCase);
		return bufferIndices.Select((uint bufferIndex, int index) => new ClothStateBufferAccessData(checked((int)bufferIndex), (byte)((index == 0) ? 7 : (isDefault ? 15 : 6)), 0, 0, 0, (index > 0) & isDefault, checked((int)bufferIndex))).ToArray();
	}

	private static IReadOnlyList<ClothStateTransformSetAccessData> CreateDefaultTransformSetAccesses(string stateName, IReadOnlyList<uint> transformSetIndices)
	{
		byte componentFlags = (byte)(string.Equals(stateName, "Default", StringComparison.OrdinalIgnoreCase) ? 11 : 9);
		return transformSetIndices.Select((uint index) => new ClothStateTransformSetAccessData(checked((int)index), componentFlags, 0, Array.Empty<ClothStateTransformTrackerData>())).ToArray();
	}

	private static IReadOnlyList<ClothStateBufferAccessData> NormalizeNativeStateBufferAccesses(string stateName, IReadOnlyList<ClothStateBufferAccessData> accesses)
	{
		if (!string.Equals(stateName, "Default", StringComparison.OrdinalIgnoreCase))
		{
			return accesses;
		}
		return accesses.Select(delegate(ClothStateBufferAccessData access)
		{
			byte b = access.ComponentFlags0;
			byte b2 = access.ComponentFlags1;
			if (access.TrianglesRead && b == 9)
			{
				b = 15;
			}
			else if (!access.TrianglesRead && b == 7 && b2 == 9)
			{
				b2 = 0;
			}
			return new ClothStateBufferAccessData(access.BufferIndex, b, b2, access.ComponentFlags2, access.ComponentFlags3, access.TrianglesRead, access.ShadowBufferIndex);
		}).ToArray();
	}

	private static DataBuilder.Allocation WriteClothStateBufferAccesses(DataBuilder builder, IReadOnlyList<ClothStateBufferAccessData> accesses)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclClothState::BufferAccess");
		int num = RequireSize(typeLayout, 16);
		DataBuilder.Allocation allocation = builder.AllocateArray(typeLayout.TypeIndex, accesses.Count, num, 4);
		for (int i = 0; i < accesses.Count; i++)
		{
			ClothStateBufferAccessData clothStateBufferAccessData = accesses[i];
			uint num2 = allocation.DataOffset + checked((uint)(i * num));
			builder.WriteUInt32(num2, checked((uint)clothStateBufferAccessData.BufferIndex));
			builder.WriteByte(num2 + 4, clothStateBufferAccessData.ComponentFlags0);
			builder.WriteByte(num2 + 5, clothStateBufferAccessData.ComponentFlags1);
			builder.WriteByte(num2 + 6, clothStateBufferAccessData.ComponentFlags2);
			builder.WriteByte(num2 + 7, clothStateBufferAccessData.ComponentFlags3);
			builder.WriteByte(num2 + 8, clothStateBufferAccessData.TrianglesRead ? ((byte)1) : ((byte)0));
			builder.WriteUInt32(num2 + 12, checked((uint)clothStateBufferAccessData.ShadowBufferIndex));
		}
		return allocation;
	}

	private static DataBuilder.Allocation WriteOperatorTransformSetAccesses(DataBuilder builder, IReadOnlyList<ClothStateTransformSetAccessData> accesses)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclClothState::TransformSetAccess");
		int num = RequireSize(typeLayout, 32);
		DataBuilder.Allocation allocation = builder.AllocateArray(typeLayout.TypeIndex, accesses.Count, num);
		for (int i = 0; i < accesses.Count; i++)
		{
			ClothStateTransformSetAccessData clothStateTransformSetAccessData = accesses[i];
			uint num2 = allocation.DataOffset + checked((uint)(i * num));
			builder.WriteUInt32(num2, checked((uint)clothStateTransformSetAccessData.TransformSetIndex));
			builder.WriteByte(num2 + 8, clothStateTransformSetAccessData.ComponentFlags0);
			builder.WriteByte(num2 + 9, clothStateTransformSetAccessData.ComponentFlags1);
			if (clothStateTransformSetAccessData.TransformTrackers.Count > 0)
			{
				DataBuilder.Allocation allocation2 = WriteClothStateTransformTrackers(builder, clothStateTransformSetAccessData.TransformTrackers);
				builder.WriteReference(num2 + 16, allocation2.ItemIndex);
			}
			builder.WriteUInt32(num2 + 24, 0u);
		}
		return allocation;
	}

	private static DataBuilder.Allocation WriteClothStateTransformSetAccesses(DataBuilder builder, IReadOnlyList<ClothStateTransformSetAccessData> accesses)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclClothState::TransformSetAccess");
		int num = RequireSize(typeLayout, 32);
		DataBuilder.Allocation allocation = builder.AllocateArray(typeLayout.TypeIndex, accesses.Count, num);
		for (int i = 0; i < accesses.Count; i++)
		{
			ClothStateTransformSetAccessData clothStateTransformSetAccessData = accesses[i];
			uint num2 = allocation.DataOffset + checked((uint)(i * num));
			DataBuilder.Allocation allocation2 = WriteClothStateTransformTrackers(builder, clothStateTransformSetAccessData.TransformTrackers);
			builder.WriteUInt32(num2, checked((uint)clothStateTransformSetAccessData.TransformSetIndex));
			builder.WriteByte(num2 + 8, clothStateTransformSetAccessData.ComponentFlags0);
			builder.WriteByte(num2 + 9, clothStateTransformSetAccessData.ComponentFlags1);
			builder.WriteReference(num2 + 16, allocation2.ItemIndex);
			builder.WriteUInt32(num2 + 24, checked((uint)clothStateTransformSetAccessData.TransformTrackers.Count));
		}
		return allocation;
	}

	private static DataBuilder.Allocation WriteClothStateTransformTrackers(DataBuilder builder, IReadOnlyList<ClothStateTransformTrackerData> trackers)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclTransformSetUsage::TransformTracker");
		int num = RequireSize(typeLayout, 72);
		DataBuilder.Allocation allocation = builder.AllocateArray(typeLayout.TypeIndex, trackers.Count, num);
		for (int i = 0; i < trackers.Count; i++)
		{
			uint num2 = allocation.DataOffset + checked((uint)(i * num));
			ClothStateTransformTrackerData clothStateTransformTrackerData = trackers[i];
			WriteClothStateBitField(builder, num2, clothStateTransformTrackerData.Read);
			WriteClothStateBitField(builder, num2 + 24, clothStateTransformTrackerData.ReadBeforeWrite);
			WriteClothStateBitField(builder, num2 + 48, clothStateTransformTrackerData.Written);
		}
		return allocation;
	}

	private static void WriteClothStateBitField(DataBuilder builder, uint offset, ClothStateBitFieldData bitField)
	{
		DataBuilder.Allocation allocation = builder.WriteUInt32Array(bitField.Words, RequireArrayElementType("hkBitFieldStorage", "words"));
		builder.WriteReference(offset, allocation.ItemIndex, RequireArrayType("hkBitFieldStorage", "words"));
		builder.WriteUInt32(offset + 16, bitField.NumBits);
	}

	private static int WriteSimulation(DataBuilder builder, StandaloneBphclClothGraph cloth, IReadOnlyList<int> colliderItems, int constraintSetCount, int simulateOperatorIndex, out DataBuilder.Reservation constraintReferences)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclSimClothData");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 432), 16);
		DataBuilder.Allocation allocation2 = builder.AllocateArray(StandaloneTypeCatalog.ParticleData, cloth.Particles.Count, 16, 16);
		ushort[] array;
		DataBuilder.Allocation allocation3;
		float value;
		float value2;
		NativeBphclSimulationRuntime simulationRuntime;
		NativeBphclLandscapeCollisionData landscapeCollisionData;
		float collisionTolerance;
		DataBuilder.Allocation allocation4;
		uint num;
		bool flag;
		DataBuilder.Allocation allocation5;
		DataBuilder.Allocation allocation6;
		DataBuilder.Allocation allocation7;
		DataBuilder.Allocation allocation8;
		IReadOnlyList<StandaloneBphclColliderBinding> readOnlyList5;
		DataBuilder.Allocation allocation9;
		DataBuilder.Allocation allocation10;
		DataBuilder.Allocation allocation11;
		DataBuilder.Allocation allocation12;
		DataBuilder.Allocation allocation13;
		DataBuilder.Allocation allocation14;
		checked
		{
			array = (from particle in cloth.Particles
				where particle.Fixed
				select (ushort)particle.Index).ToArray();
			allocation3 = builder.WriteUInt16Array(array);
			StandaloneBphclParticle[] source = cloth.Particles.Where((StandaloneBphclParticle particle) => !particle.Fixed).ToArray();
			value = ((cloth.Particles.Count == 0) ? 0f : cloth.Particles.Max((StandaloneBphclParticle particle) => particle.Radius));
			value2 = source.Sum((StandaloneBphclParticle particle) => particle.Mass);
			simulationRuntime = cloth.SimulationRuntime;
			landscapeCollisionData = simulationRuntime.LandscapeCollisionData;
			collisionTolerance = cloth.Settings.CollisionTolerance;
			IReadOnlyList<bool> readOnlyList2;
			if (simulationRuntime.PerParticlePinchDetectionEnabledFlags.Count != cloth.Particles.Count)
			{
				IReadOnlyList<bool> readOnlyList = new bool[cloth.Particles.Count];
				readOnlyList2 = readOnlyList;
			}
			else
			{
				readOnlyList2 = simulationRuntime.PerParticlePinchDetectionEnabledFlags;
			}
			IReadOnlyList<bool> values = readOnlyList2;
			allocation4 = builder.WriteBoolArray(values, RequireArrayElementType("hclSimClothData", "perParticlePinchDetectionEnabledFlags"));
			num = (uint)cloth.Particles.Count((StandaloneBphclParticle particle) => !particle.Fixed && (particle.CollisionMask & 0x80000000u) != 0);
			flag = simulationRuntime.LandscapeCollisionEnabled || num != 0;
			allocation5 = builder.WriteUInt32Array(new uint[1] { (uint)simulateOperatorIndex }, RequireArrayElementType("hclSimClothData", "simOpIds"));
			allocation6 = builder.WriteVector4Array(cloth.Particles.Select((StandaloneBphclParticle particle) => particle.Position).ToArray());
			allocation7 = builder.AllocateObject(StandaloneTypeCatalog.SimClothPose, 48);
			allocation8 = builder.WriteReferenceArray(new int[1] { allocation7.ItemIndex });
			IReadOnlyList<StandaloneBphclColliderBinding> readOnlyList4;
			if (cloth.ColliderBindings.Count != cloth.Colliders.Count)
			{
				IReadOnlyList<StandaloneBphclColliderBinding> readOnlyList3 = cloth.Colliders.Select((StandaloneBphclCollider collider) => new StandaloneBphclColliderBinding(collider.Index, (collider.BoneIndex >= 0 && collider.BoneIndex < cloth.Bones.Count) ? collider.BoneIndex : 0, BuildColliderOffset(cloth, collider))).ToArray();
				readOnlyList4 = readOnlyList3;
			}
			else
			{
				readOnlyList4 = cloth.ColliderBindings;
			}
			readOnlyList5 = readOnlyList4;
			Dictionary<int, StandaloneBphclCollider> collidersByIndex = cloth.Colliders.ToDictionary((StandaloneBphclCollider collider) => collider.Index);
			if (readOnlyList5.Any((StandaloneBphclColliderBinding binding) => !collidersByIndex.ContainsKey(binding.ColliderIndex)))
			{
				throw new InvalidDataException("Cloth '" + cloth.Name + "' has a collider transform-map entry for a missing collider.");
			}
			if (readOnlyList5.Count != colliderItems.Count)
			{
				throw new InvalidDataException("Cloth '" + cloth.Name + "' has an incomplete collider transform map.");
			}
			Dictionary<int, int> colliderItemsByIndex = cloth.Colliders.Select((StandaloneBphclCollider collider, int index) => (Index: collider.Index, Item: colliderItems[index])).ToDictionary(((int Index, int Item) entry) => entry.Index, ((int Index, int Item) entry) => entry.Item);
			allocation9 = null;
			allocation10 = null;
			allocation11 = null;
			if (readOnlyList5.Count > 0)
			{
				allocation9 = builder.WriteReferenceArray(readOnlyList5.Select((StandaloneBphclColliderBinding binding) => colliderItemsByIndex[binding.ColliderIndex]).ToArray());
				allocation10 = builder.WriteUInt32Array(readOnlyList5.Select((StandaloneBphclColliderBinding binding) => (uint)binding.BoneIndex).ToArray());
				allocation11 = builder.WriteMatrix4Array(readOnlyList5.Select((StandaloneBphclColliderBinding binding) => binding.Offset).ToArray());
			}
			constraintReferences = builder.ReserveArray(RequireArrayElementType("hclSimClothData", "staticConstraintSets"), (uint)constraintSetCount);
			allocation12 = builder.WriteUInt16Array(cloth.Triangles.SelectMany((StandaloneBphclTriangle triangle) => new ushort[3]
			{
				(ushort)triangle.ParticleA,
				(ushort)triangle.ParticleB,
				(ushort)triangle.ParticleC
			}).ToArray());
			allocation13 = builder.WriteUInt32Array(cloth.Particles.Select((StandaloneBphclParticle particle) => particle.CollisionMask).ToArray(), RequireArrayElementType("hclSimClothData", "staticCollisionMasks"));
			allocation14 = null;
		}
		if (cloth.Colliders.Count > 0)
		{
			NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclSimClothData::CollidablePinchingData");
			allocation14 = builder.AllocateArray(typeLayout2.TypeIndex, cloth.Colliders.Count, RequireSize(typeLayout2, 8), 4);
			IReadOnlyList<NativeBphclCollidablePinchingData> readOnlyList6 = ((simulationRuntime.CollidablePinchingData.Count == cloth.Colliders.Count) ? simulationRuntime.CollidablePinchingData : null);
			for (int num2 = 0; num2 < cloth.Colliders.Count; num2++)
			{
				uint num3 = allocation14.DataOffset + checked((uint)(num2 * 8));
				NativeBphclCollidablePinchingData nativeBphclCollidablePinchingData = readOnlyList6?[num2] ?? new NativeBphclCollidablePinchingData(Enabled: false, 0, 0.01f);
				builder.WriteByte(num3, nativeBphclCollidablePinchingData.Enabled ? ((byte)1) : ((byte)0));
				builder.WriteByte(num3 + 1, (byte)nativeBphclCollidablePinchingData.Priority);
				builder.WriteSingle(num3 + 4, nativeBphclCollidablePinchingData.Radius);
			}
		}
		builder.WriteReference(allocation.DataOffset + 24, builder.WriteString("Simulation"));
		builder.WriteVector4(allocation.DataOffset + 32, new Vector4(cloth.Settings.Gravity, 0f));
		builder.WriteSingle(allocation.DataOffset + 48, cloth.Settings.DampingPerSecond);
		builder.WriteReference(allocation.DataOffset + 64, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 72, checked((uint)cloth.Particles.Count));
		builder.WriteReference(allocation.DataOffset + 80, allocation3.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 88, checked((uint)array.Length));
		builder.WriteByte(allocation.DataOffset + 96, simulationRuntime.DoNormals ? ((byte)1) : ((byte)0));
		builder.WriteReference(allocation.DataOffset + 104, allocation5.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 112, 1u);
		builder.WriteReference(allocation.DataOffset + 120, allocation8.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 128, 1u);
		builder.WriteReference(allocation.DataOffset + 136, constraintReferences.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 144, checked((uint)constraintSetCount));
		builder.WriteInt32(allocation.DataOffset + 168, (readOnlyList5.Count == 0) ? (-1) : 0);
		if (allocation10 != null)
		{
			builder.WriteReference(allocation.DataOffset + 176, allocation10.ItemIndex);
		}
		builder.WriteUInt32(allocation.DataOffset + 184, checked((uint)readOnlyList5.Count));
		if (allocation11 != null)
		{
			builder.WriteReference(allocation.DataOffset + 192, allocation11.ItemIndex);
		}
		builder.WriteUInt32(allocation.DataOffset + 200, checked((uint)readOnlyList5.Count));
		if (allocation9 != null)
		{
			builder.WriteReference(allocation.DataOffset + 208, allocation9.ItemIndex);
		}
		builder.WriteUInt32(allocation.DataOffset + 216, checked((uint)readOnlyList5.Count));
		builder.WriteSingle(allocation.DataOffset + 224, value);
		builder.WriteSingle(allocation.DataOffset + 264, value2);
		NativeBphclTransferMotionData transferMotionData = cloth.SimulationRuntime.TransferMotionData;
		builder.WriteInt32(allocation.DataOffset + 268, transferMotionData.TransformSetIndex);
		builder.WriteInt32(allocation.DataOffset + 272, transferMotionData.TransformIndex);
		builder.WriteByte(allocation.DataOffset + 276, transferMotionData.TransferTranslationMotion ? ((byte)1) : ((byte)0));
		builder.WriteSingle(allocation.DataOffset + 280, transferMotionData.MinTranslationSpeed);
		builder.WriteSingle(allocation.DataOffset + 284, transferMotionData.MaxTranslationSpeed);
		builder.WriteSingle(allocation.DataOffset + 288, transferMotionData.MinTranslationBlend);
		builder.WriteSingle(allocation.DataOffset + 292, transferMotionData.MaxTranslationBlend);
		builder.WriteByte(allocation.DataOffset + 296, transferMotionData.TransferRotationMotion ? ((byte)1) : ((byte)0));
		builder.WriteSingle(allocation.DataOffset + 300, transferMotionData.MinRotationSpeed);
		builder.WriteSingle(allocation.DataOffset + 304, transferMotionData.MaxRotationSpeed);
		builder.WriteSingle(allocation.DataOffset + 308, transferMotionData.MinRotationBlend);
		builder.WriteSingle(allocation.DataOffset + 312, transferMotionData.MaxRotationBlend);
		builder.WriteByte(allocation.DataOffset + 316, simulationRuntime.TransferMotionEnabled ? ((byte)1) : ((byte)0));
		builder.WriteByte(allocation.DataOffset + 317, flag ? ((byte)1) : ((byte)0));
		builder.WriteSingle(allocation.DataOffset + 320, landscapeCollisionData.LandscapeRadius);
		builder.WriteByte(allocation.DataOffset + 324, landscapeCollisionData.EnableStuckParticleDetection ? ((byte)1) : ((byte)0));
		builder.WriteSingle(allocation.DataOffset + 328, landscapeCollisionData.StuckParticlesStretchFactorSq);
		builder.WriteByte(allocation.DataOffset + 332, landscapeCollisionData.PinchDetectionEnabled ? ((byte)1) : ((byte)0));
		builder.WriteByte(allocation.DataOffset + 333, landscapeCollisionData.PinchDetectionPriority);
		builder.WriteSingle(allocation.DataOffset + 336, landscapeCollisionData.PinchDetectionRadius);
		builder.WriteSingle(allocation.DataOffset + 340, collisionTolerance);
		builder.WriteUInt32(allocation.DataOffset + 344, (simulationRuntime.NumLandscapeCollidableParticles == 0) ? num : simulationRuntime.NumLandscapeCollidableParticles);
		builder.WriteReference(allocation.DataOffset + 352, allocation12.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 360, checked((uint)(cloth.Triangles.Count * 3)));
		builder.WriteByte(allocation.DataOffset + 384, simulationRuntime.PinchDetectionEnabled ? ((byte)1) : ((byte)0));
		builder.WriteReference(allocation.DataOffset + 392, allocation4.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + 400, checked((uint)cloth.Particles.Count));
		if (allocation14 != null)
		{
			builder.WriteReference(allocation.DataOffset + 408, allocation14.ItemIndex);
			builder.WriteUInt32(allocation.DataOffset + 416, checked((uint)cloth.Colliders.Count));
		}
		builder.WriteUInt16(allocation.DataOffset + 424, simulationRuntime.MinPinchedParticleIndex);
		builder.WriteUInt16(allocation.DataOffset + 426, simulationRuntime.MaxPinchedParticleIndex);
		builder.WriteUInt32(allocation.DataOffset + 428, simulationRuntime.MaxCollisionPairs);
		uint? num4 = NativeBphclGameReadyReflectionCatalog.GetMemberOffset("hclSimClothData", "m_staticCollisionMasks") ?? NativeBphclGameReadyReflectionCatalog.GetMemberOffset("hclSimClothData", "staticCollisionMasks");
		if (num4.HasValue)
		{
			uint valueOrDefault = num4.GetValueOrDefault();
			builder.WriteReference(allocation.DataOffset + valueOrDefault, allocation13.ItemIndex);
			builder.WriteUInt32(allocation.DataOffset + valueOrDefault + 8, checked((uint)cloth.Particles.Count));
		}
		builder.WriteReference(allocation7.DataOffset + 24, builder.WriteString("DefaultClothPose"));
		builder.WriteReference(allocation7.DataOffset + 32, allocation6.ItemIndex);
		builder.WriteUInt32(allocation7.DataOffset + 40, checked((uint)cloth.Particles.Count));
		for (int num5 = 0; num5 < cloth.Particles.Count; num5++)
		{
			StandaloneBphclParticle standaloneBphclParticle = cloth.Particles[num5];
			uint num6 = allocation2.DataOffset + checked((uint)(num5 * 16));
			builder.WriteSingle(num6, standaloneBphclParticle.Mass);
			builder.WriteSingle(num6 + 4, standaloneBphclParticle.InverseMass);
			builder.WriteSingle(num6 + 8, standaloneBphclParticle.Radius);
			builder.WriteSingle(num6 + 12, standaloneBphclParticle.Friction);
		}
		return allocation.ItemIndex;
	}

	private static Matrix4x4 BuildColliderOffset(StandaloneBphclClothGraph cloth, StandaloneBphclCollider collider)
	{
		if (collider.BoneIndex < 0 || collider.BoneIndex >= cloth.Bones.Count)
		{
			return collider.Transform;
		}
		if (!Matrix4x4.Invert(GetBoneWorldTransform(cloth.Bones, collider.BoneIndex), out var result))
		{
			return collider.Transform;
		}
		return collider.Transform * result;
	}

	private static Matrix4x4 GetBoneWorldTransform(IReadOnlyList<StandaloneBphclBone> bones, int boneIndex)
	{
		Dictionary<int, StandaloneBphclBone> dictionary = bones.ToDictionary((StandaloneBphclBone bone) => bone.Index);
		Stack<StandaloneBphclBone> stack = new Stack<StandaloneBphclBone>();
		HashSet<int> hashSet = new HashSet<int>();
		int num = boneIndex;
		StandaloneBphclBone value;
		while (num >= 0 && hashSet.Add(num) && dictionary.TryGetValue(num, out value))
		{
			stack.Push(value);
			num = value.ParentIndex;
		}
		Matrix4x4 matrix4x = Matrix4x4.Identity;
		while (stack.Count > 0)
		{
			StandaloneBphclBone standaloneBphclBone = stack.Pop();
			Quaternion value2 = new Quaternion(standaloneBphclBone.Rotation.X, standaloneBphclBone.Rotation.Y, standaloneBphclBone.Rotation.Z, standaloneBphclBone.Rotation.W);
			Quaternion quaternion = ((value2.LengthSquared() < 1E-06f) ? Quaternion.Identity : Quaternion.Normalize(value2));
			matrix4x = Matrix4x4.CreateScale(standaloneBphclBone.Scale.X, standaloneBphclBone.Scale.Y, standaloneBphclBone.Scale.Z) * Matrix4x4.CreateFromQuaternion(quaternion) * Matrix4x4.CreateTranslation(standaloneBphclBone.Translation.X, standaloneBphclBone.Translation.Y, standaloneBphclBone.Translation.Z) * matrix4x;
		}
		return matrix4x;
	}

	private static IReadOnlyList<int> WriteConstraintSets(DataBuilder builder, StandaloneBphclClothGraph cloth)
	{
		Dictionary<int, StandaloneBphclConstraint[]> dictionary = (from constraint in cloth.Constraints
			group constraint by constraint.ConstraintSetIndex).ToDictionary((IGrouping<int, StandaloneBphclConstraint> group) => group.Key, (IGrouping<int, StandaloneBphclConstraint> group) => group.ToArray());
		Dictionary<int, StandaloneBphclLocalRange[]> dictionary2 = (from range in cloth.LocalRanges
			group range by range.ConstraintSetIndex).ToDictionary((IGrouping<int, StandaloneBphclLocalRange> group) => group.Key, (IGrouping<int, StandaloneBphclLocalRange> group) => group.ToArray());
		Dictionary<int, StandaloneBphclTransitionConstraintSet> dictionary3 = cloth.TransitionSets.ToDictionary((StandaloneBphclTransitionConstraintSet set) => set.ConstraintSetIndex);
		Dictionary<int, StandaloneBphclBonePlaneConstraintSet> dictionary4 = cloth.BonePlaneSets.ToDictionary((StandaloneBphclBonePlaneConstraintSet set) => set.ConstraintSetIndex);
		Dictionary<int, VolumeConstraintSetData> dictionary5 = cloth.VolumeSets.ToDictionary((VolumeConstraintSetData set) => set.ConstraintSetIndex);
		List<int> list = new List<int>();
		foreach (StandaloneBphclConstraintSetMetadata item in cloth.ConstraintSets.OrderBy((StandaloneBphclConstraintSetMetadata set) => set.Index))
		{
			int index = item.Index;
			bool num = dictionary.TryGetValue(index, out var value);
			bool flag = dictionary2.TryGetValue(index, out var value2);
			bool flag2 = dictionary3.TryGetValue(index, out var value3);
			bool flag3 = dictionary4.TryGetValue(index, out var value4);
			bool flag4 = dictionary5.TryGetValue(index, out var value5);
			if ((num ? 1 : 0) + (flag ? 1 : 0) + (flag2 ? 1 : 0) + (flag3 ? 1 : 0) + (flag4 ? 1 : 0) > 1)
			{
				throw new InvalidDataException($"Cloth '{cloth.Name}' constraint set {index} mixes incompatible record layouts.");
			}
			if (string.Equals(item.TypeName, "hclTransitionConstraintSet", StringComparison.Ordinal))
			{
				list.Add(WriteTransitionConstraintSet(builder, item, value3 ?? new StandaloneBphclTransitionConstraintSet(index, item.Name, item.ConstraintId, item.ConstraintType, 0f, 0f, 0f, 0f, 0u, Array.Empty<StandaloneBphclTransitionParticle>())));
			}
			else if (string.Equals(item.TypeName, "hclLocalRangeConstraintSet", StringComparison.Ordinal))
			{
				list.Add(WriteLocalRangeConstraintSet(builder, cloth.Name, item, value2 ?? Array.Empty<StandaloneBphclLocalRange>()));
			}
			else if (string.Equals(item.TypeName, "hclBonePlanesConstraintSet", StringComparison.Ordinal))
			{
				list.Add(WriteBonePlanesConstraintSet(builder, cloth.Name, item, value4 ?? new StandaloneBphclBonePlaneConstraintSet(index, item.Name, item.ConstraintId, item.ConstraintType, 0u, Array.Empty<StandaloneBphclBonePlane>())));
			}
			else if (string.Equals(item.TypeName, "hclVolumeConstraint", StringComparison.Ordinal))
			{
				list.Add(WriteVolumeConstraintSet(builder, item, value5 ?? new VolumeConstraintSetData(index, item.Name, item.ConstraintId, item.ConstraintType, Array.Empty<VolumeConstraintFrameData>(), Array.Empty<VolumeConstraintApplyData>())));
			}
			else
			{
				list.Add(WriteLinkConstraintSet(builder, cloth.Name, item, value ?? Array.Empty<StandaloneBphclConstraint>()));
			}
		}
		return list;
	}

	private static int WriteVolumeConstraintSet(DataBuilder builder, StandaloneBphclConstraintSetMetadata metadata, VolumeConstraintSetData volume)
	{
		const string className = "hclVolumeConstraint";
		NativeBphclTypeLayout layout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout(className);
		NativeBphclTypeLayout frameLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout(className + "::FrameData");
		NativeBphclTypeLayout applyLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout(className + "::ApplyData");
		DataBuilder.Allocation allocation = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 72), 8);
		DataBuilder.Allocation frames = builder.AllocateArray(frameLayout.TypeIndex, volume.Frames.Count, RequireSize(frameLayout, 32), 16);
		DataBuilder.Allocation applies = builder.AllocateArray(applyLayout.TypeIndex, volume.Applies.Count, RequireSize(applyLayout, 32), 16);
		builder.WriteReference(allocation.DataOffset + 24, builder.WriteString(volume.Name));
		builder.WriteUInt32(allocation.DataOffset + 32, volume.ConstraintId != 0 ? volume.ConstraintId : ResolveConstraintId(metadata));
		builder.WriteUInt32(allocation.DataOffset + 36, volume.ConstraintType);
		WriteArrayField(builder, allocation.DataOffset + RequireMemberOffset(className, "frameDatas"), frames, RequireArrayType(className, "frameDatas"));
		WriteArrayField(builder, allocation.DataOffset + RequireMemberOffset(className, "applyDatas"), applies, RequireArrayType(className, "applyDatas"));

		for (int index = 0; index < volume.Frames.Count; index++)
		{
			VolumeConstraintFrameData frame = volume.Frames[index];
			uint offset = frames.DataOffset + checked((uint)(index * RequireSize(frameLayout, 32)));
			builder.WriteVector4(offset + RequireMemberOffset(className + "::FrameData", "frameVector"), frame.FrameVector);
			builder.WriteUInt16(offset + RequireMemberOffset(className + "::FrameData", "particleIndex"), checked((ushort)frame.ParticleIndex));
			builder.WriteSingle(offset + RequireMemberOffset(className + "::FrameData", "weight"), frame.Weight);
		}
		for (int index = 0; index < volume.Applies.Count; index++)
		{
			VolumeConstraintApplyData apply = volume.Applies[index];
			uint offset = applies.DataOffset + checked((uint)(index * RequireSize(applyLayout, 32)));
			builder.WriteVector4(offset + RequireMemberOffset(className + "::ApplyData", "frameVector"), apply.FrameVector);
			builder.WriteUInt16(offset + RequireMemberOffset(className + "::ApplyData", "particleIndex"), checked((ushort)apply.ParticleIndex));
			builder.WriteSingle(offset + RequireMemberOffset(className + "::ApplyData", "stiffness"), apply.Stiffness);
		}
		return allocation.ItemIndex;
	}

	private static uint ResolveConstraintId(StandaloneBphclConstraintSetMetadata metadata)
	{
		if (metadata.ConstraintId == 0)
		{
			return checked((uint)metadata.Index);
		}
		return metadata.ConstraintId;
	}

	private static int WriteLinkConstraintSet(DataBuilder builder, string clothName, StandaloneBphclConstraintSetMetadata metadata, IReadOnlyList<StandaloneBphclConstraint> links)
	{
		string className = metadata.TypeName;
		if (links.Any((StandaloneBphclConstraint link) => !string.Equals(link.TypeName, className, StringComparison.Ordinal)))
		{
			throw new InvalidDataException($"Cloth '{clothName}' constraint set {metadata.Index} has inconsistent Havok link classes.");
		}
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout(className);
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout(className + "::Link");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 40));
		DataBuilder.Allocation allocation2 = builder.AllocateArray(typeLayout2.TypeIndex, links.Count, RequireSize(typeLayout2, 1), 4);
		uint num = NativeBphclGameReadyReflectionCatalog.GetMemberOffset(className, "links") ?? 40;
		builder.WriteReference(allocation.DataOffset + 24, builder.WriteString(metadata.Name));
		builder.WriteUInt32(allocation.DataOffset + 32, ResolveConstraintId(metadata));
		builder.WriteUInt32(allocation.DataOffset + 36, metadata.ConstraintType);
		WriteBendStiffnessSetValues(builder, allocation.DataOffset, typeLayout, metadata);
		builder.WriteReference(allocation.DataOffset + num, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + num + 8, checked((uint)links.Count));
		for (int num2 = 0; num2 < links.Count; num2++)
		{
			WriteLinkRecord(builder, allocation2.DataOffset + checked((uint)(num2 * RequireSize(typeLayout2, 1))), typeLayout2, links[num2]);
		}
		return allocation.ItemIndex;
	}

	private static int WriteBonePlanesConstraintSet(DataBuilder builder, string clothName, StandaloneBphclConstraintSetMetadata metadata, StandaloneBphclBonePlaneConstraintSet bonePlanes)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclBonePlanesConstraintSet");
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclBonePlanesConstraintSet::BonePlane");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 64));
		DataBuilder.Allocation allocation2 = builder.AllocateArray(typeLayout2.TypeIndex, bonePlanes.Planes.Count, RequireSize(typeLayout2, 24), 16);
		uint num = NativeBphclGameReadyReflectionCatalog.GetMemberOffset("hclBonePlanesConstraintSet", "bonePlanes") ?? 40;
		uint num2 = NativeBphclGameReadyReflectionCatalog.GetMemberOffset("hclBonePlanesConstraintSet", "transformSetIndex") ?? 56;
		builder.WriteReference(allocation.DataOffset + 24, builder.WriteString(bonePlanes.Name));
		builder.WriteUInt32(allocation.DataOffset + 32, (bonePlanes.ConstraintId != 0) ? bonePlanes.ConstraintId : checked((uint)bonePlanes.ConstraintSetIndex));
		builder.WriteUInt32(allocation.DataOffset + 36, bonePlanes.ConstraintType);
		builder.WriteReference(allocation.DataOffset + num, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + num + 8, checked((uint)bonePlanes.Planes.Count));
		builder.WriteUInt32(allocation.DataOffset + num2, bonePlanes.TransformSetIndex);
		for (int i = 0; i < bonePlanes.Planes.Count; i++)
		{
			StandaloneBphclBonePlane standaloneBphclBonePlane = bonePlanes.Planes[i];
			uint num3 = allocation2.DataOffset + checked((uint)(i * RequireSize(typeLayout2, 24)));
			foreach (NativeBphclTypeLayoutMember member in typeLayout2.Members)
			{
				if (member.Name == "planeEquationBone")
				{
					builder.WriteVector4(num3 + member.Offset, standaloneBphclBonePlane.PlaneEquationBone);
				}
				else if (member.Name == "particleIndex")
				{
					builder.WriteScalar(num3 + member.Offset, member.TypeName, standaloneBphclBonePlane.ParticleIndex);
				}
				else if (member.Name == "transformIndex")
				{
					builder.WriteScalar(num3 + member.Offset, member.TypeName, standaloneBphclBonePlane.TransformIndex);
				}
				else if (member.Name == "stiffness")
				{
					builder.WriteScalar(num3 + member.Offset, member.TypeName, standaloneBphclBonePlane.Stiffness);
				}
			}
		}
		if (!string.Equals(metadata.Name, bonePlanes.Name, StringComparison.Ordinal))
		{
			throw new InvalidDataException("Cloth '" + clothName + "' bone-plane metadata does not match its set name.");
		}
		return allocation.ItemIndex;
	}

	private static int WriteLocalRangeConstraintSet(DataBuilder builder, string clothName, StandaloneBphclConstraintSetMetadata metadata, IReadOnlyList<StandaloneBphclLocalRange> ranges)
	{
		string typeName = metadata.TypeName;
		if (!string.Equals(typeName, "hclLocalRangeConstraintSet", StringComparison.Ordinal))
		{
			throw new InvalidDataException($"Cloth '{clothName}' local range set {metadata.Index} has unsupported class '{typeName}'.");
		}
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout(typeName);
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout(typeName + "::LocalConstraint");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 40));
		DataBuilder.Allocation allocation2 = builder.AllocateArray(typeLayout2.TypeIndex, ranges.Count, RequireSize(typeLayout2, 1), 4);
		uint num = NativeBphclGameReadyReflectionCatalog.GetMemberOffset(typeName, "localConstraints") ?? 40;
		builder.WriteReference(allocation.DataOffset + 24, builder.WriteString(metadata.Name));
		builder.WriteUInt32(allocation.DataOffset + 32, ResolveConstraintId(metadata));
		builder.WriteUInt32(allocation.DataOffset + 36, metadata.ConstraintType);
		builder.WriteReference(allocation.DataOffset + num, allocation2.ItemIndex);
		builder.WriteUInt32(allocation.DataOffset + num + 8, checked((uint)ranges.Count));
		StandaloneBphclLocalRange standaloneBphclLocalRange = ranges.FirstOrDefault();
		if (standaloneBphclLocalRange != null)
		{
			uint? memberOffset = NativeBphclGameReadyReflectionCatalog.GetMemberOffset(typeName, "referenceMeshBufferIdx");
			if (memberOffset.HasValue)
			{
				uint valueOrDefault = memberOffset.GetValueOrDefault();
				builder.WriteUInt32(allocation.DataOffset + valueOrDefault, standaloneBphclLocalRange.ReferenceMeshBufferIndex);
			}
			uint? memberOffset2 = NativeBphclGameReadyReflectionCatalog.GetMemberOffset(typeName, "stiffness");
			if (memberOffset2.HasValue)
			{
				uint valueOrDefault2 = memberOffset2.GetValueOrDefault();
				builder.WriteSingle(allocation.DataOffset + valueOrDefault2, standaloneBphclLocalRange.Stiffness);
			}
			uint? memberOffset3 = NativeBphclGameReadyReflectionCatalog.GetMemberOffset(typeName, "shapeType");
			if (memberOffset3.HasValue)
			{
				uint valueOrDefault3 = memberOffset3.GetValueOrDefault();
				builder.WriteInt32(allocation.DataOffset + valueOrDefault3, standaloneBphclLocalRange.ShapeType);
			}
			uint? memberOffset4 = NativeBphclGameReadyReflectionCatalog.GetMemberOffset(typeName, "applyNormalComponent");
			if (memberOffset4.HasValue)
			{
				uint valueOrDefault4 = memberOffset4.GetValueOrDefault();
				builder.WriteByte(allocation.DataOffset + valueOrDefault4, standaloneBphclLocalRange.ApplyNormalComponent ? ((byte)1) : ((byte)0));
			}
		}
		for (int i = 0; i < ranges.Count; i++)
		{
			StandaloneBphclLocalRange standaloneBphclLocalRange2 = ranges[i];
			uint num2 = allocation2.DataOffset + checked((uint)(i * RequireSize(typeLayout2, 1)));
			foreach (NativeBphclTypeLayoutMember member in typeLayout2.Members)
			{
				switch (member.Name)
				{
				case "particleIndex":
					builder.WriteScalar(num2 + member.Offset, member.TypeName, standaloneBphclLocalRange2.ParticleIndex);
					break;
				case "referenceVertex":
					builder.WriteScalar(num2 + member.Offset, member.TypeName, standaloneBphclLocalRange2.ReferenceVertex);
					break;
				case "maximumDistance":
				case "maxDistance":
				case "shapeRadius":
					builder.WriteScalar(num2 + member.Offset, member.TypeName, standaloneBphclLocalRange2.ShapeRadius);
					break;
				case "maxNormalDistance":
					builder.WriteScalar(num2 + member.Offset, member.TypeName, standaloneBphclLocalRange2.MaxNormalDistance);
					break;
				case "minNormalDistance":
					builder.WriteScalar(num2 + member.Offset, member.TypeName, standaloneBphclLocalRange2.MinNormalDistance);
					break;
				}
			}
		}
		return allocation.ItemIndex;
	}

	private static int WriteTransitionConstraintSet(DataBuilder builder, StandaloneBphclConstraintSetMetadata metadata, StandaloneBphclTransitionConstraintSet transition)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclTransitionConstraintSet");
		NativeBphclTypeLayout typeLayout2 = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hclTransitionConstraintSet::PerParticle");
		DataBuilder.Allocation allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 80));
		DataBuilder.Allocation allocation2 = builder.AllocateArray(typeLayout2.TypeIndex, transition.Particles.Count, RequireSize(typeLayout2, 16), 4);
		uint dataOffset = allocation.DataOffset;
		builder.WriteReference(dataOffset + 24, builder.WriteString(transition.Name));
		builder.WriteUInt32(dataOffset + 32, (transition.ConstraintId != 0) ? transition.ConstraintId : checked((uint)transition.ConstraintSetIndex));
		builder.WriteUInt32(dataOffset + 36, transition.ConstraintType);
		builder.WriteReference(dataOffset + 40, allocation2.ItemIndex);
		builder.WriteUInt32(dataOffset + 48, checked((uint)transition.Particles.Count));
		builder.WriteSingle(dataOffset + 56, transition.ToAnimationPeriod);
		builder.WriteSingle(dataOffset + 60, transition.ToAnimationPlusDelayPeriod);
		builder.WriteSingle(dataOffset + 64, transition.ToSimulationPeriod);
		builder.WriteSingle(dataOffset + 68, transition.ToSimulationPlusDelayPeriod);
		builder.WriteUInt32(dataOffset + 72, transition.ReferenceMeshBufferIndex);
		for (int i = 0; i < transition.Particles.Count; i++)
		{
			StandaloneBphclTransitionParticle standaloneBphclTransitionParticle = transition.Particles[i];
			uint num = allocation2.DataOffset + checked((uint)(i * RequireSize(typeLayout2, 16)));
			builder.WriteUInt16(num, checked((ushort)standaloneBphclTransitionParticle.ParticleIndex));
			builder.WriteUInt16(num + 2, checked((ushort)standaloneBphclTransitionParticle.ReferenceVertex));
			builder.WriteSingle(num + 4, standaloneBphclTransitionParticle.ToAnimationDelay);
			builder.WriteSingle(num + 8, standaloneBphclTransitionParticle.ToSimulationDelay);
			builder.WriteSingle(num + 12, standaloneBphclTransitionParticle.ToSimulationMaximumDistance);
		}
		return allocation.ItemIndex;
	}

	private static void WriteBendStiffnessSetValues(DataBuilder builder, uint setOffset, NativeBphclTypeLayout layout, StandaloneBphclConstraintSetMetadata metadata)
	{
		if (!string.Equals(metadata.TypeName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal))
		{
			return;
		}
		foreach (NativeBphclTypeLayoutMember member in layout.Members)
		{
			object obj = member.Name switch
			{
				"clampBendStiffness" => metadata.ClampBendStiffness,
				"maxRestPoseHeightSq" => metadata.MaxRestPoseHeightSq,
				"useRestPoseConfig" => metadata.UseRestPoseConfig,
				_ => null,
			};
			if (obj != null)
			{
				builder.WriteScalar(setOffset + member.Offset, member.TypeName, obj);
			}
		}
	}

	private static void WriteLinkRecord(DataBuilder builder, uint recordOffset, NativeBphclTypeLayout layout, StandaloneBphclConstraint link)
	{
		foreach (NativeBphclTypeLayoutMember member in layout.Members)
		{
			object obj = member.Name switch
			{
				"particleA" => link.ParticleA,
				"particleB" => link.ParticleB,
				"particleC" => link.ParticleC,
				"particleD" => link.ParticleD,
				"restCurvature" => link.RestCurvature,
				"weightA" => link.WeightA,
				"weightB" => link.WeightB,
				"weightC" => link.WeightC,
				"weightD" => link.WeightD,
				"restLength" => link.RestLength,
				"bendMinLength" => link.BendMinLength,
				"stretchMaxLength" => link.StretchMaxLength,
				"stiffness" => link.Stiffness,
				"bendStiffness" => link.BendStiffness,
				"stretchStiffness" => link.StretchStiffness,
				"compressionLength" => link.CompressionLength,
				_ => null,
			};
			if (obj != null)
			{
				builder.WriteScalar(recordOffset + member.Offset, member.TypeName, obj);
			}
		}
	}

	private static uint RequireArrayElementType(uint ownerType, uint memberOffset)
	{
		return NativeBphclGameReadyReflectionCatalog.GetArrayElementType(NativeBphclGameReadyReflectionCatalog.GetMemberTypeIndex(ownerType, memberOffset) ?? throw new InvalidDataException($"BPHCL type {ownerType} has no array field at +0x{memberOffset:X}.")) ?? throw new InvalidDataException($"BPHCL array field at +0x{memberOffset:X} has no reflected element type.");
	}

	private static int RequireSize(NativeBphclTypeLayout layout, int minimumSize)
	{
		if (layout.Size < minimumSize || layout.Size > int.MaxValue)
		{
			throw new InvalidDataException($"Built-in BPHCL TYPE layout '{layout.Name}' has an invalid size {layout.Size}.");
		}
		return checked((int)layout.Size);
	}

	private static int WriteSkeleton(DataBuilder builder, StandaloneBphclClothGraph cloth, DataBuilder.Reservation? reservation = null)
	{
		NativeBphclTypeLayout typeLayout = NativeBphclGameReadyReflectionCatalog.GetTypeLayout("hkaSkeleton");
		DataBuilder.Allocation allocation;
		if ((object)reservation != null)
		{
			allocation = builder.Materialize(reservation, RequireSize(typeLayout, 144));
		}
		else
		{
			allocation = builder.AllocateObject(typeLayout.TypeIndex, RequireSize(typeLayout, 144));
		}
		DataBuilder.Allocation allocation2 = allocation;
		DataBuilder.Allocation allocation3 = builder.WriteInt16Array(cloth.Bones.Select((StandaloneBphclBone bone) => (short)((bone.ParentIndex >= 0) ? checked((short)bone.ParentIndex) : (-1))).ToArray());
		DataBuilder.Allocation allocation4 = builder.AllocateArray(StandaloneTypeCatalog.Bone, cloth.Bones.Count, 16);
		DataBuilder.Allocation allocation5 = builder.AllocateArray(StandaloneTypeCatalog.QsTransform, cloth.Bones.Count, 48, 16);
		builder.WriteReference(allocation2.DataOffset + 24, builder.WriteString(cloth.SkeletonName));
		builder.WriteReference(allocation2.DataOffset + 32, allocation3.ItemIndex);
		builder.WriteUInt32(allocation2.DataOffset + 40, checked((uint)cloth.Bones.Count));
		builder.WriteReference(allocation2.DataOffset + 48, allocation4.ItemIndex);
		builder.WriteUInt32(allocation2.DataOffset + 56, checked((uint)cloth.Bones.Count));
		builder.WriteReference(allocation2.DataOffset + 64, allocation5.ItemIndex);
		builder.WriteUInt32(allocation2.DataOffset + 72, checked((uint)cloth.Bones.Count));
		for (int num = 0; num < cloth.Bones.Count; num++)
		{
			StandaloneBphclBone standaloneBphclBone = cloth.Bones[num];
			uint dataOffset = allocation4.DataOffset + checked((uint)(num * 16));
			uint num2 = allocation5.DataOffset + checked((uint)(num * 48));
			builder.WriteReference(dataOffset, builder.WriteInternedString(standaloneBphclBone.Name));
			builder.WriteVector4(num2, standaloneBphclBone.Translation);
			builder.WriteVector4(num2 + 16, standaloneBphclBone.Rotation);
			builder.WriteVector4(num2 + 32, standaloneBphclBone.Scale);
		}
		return allocation2.ItemIndex;
	}

	private static int GetOrWriteSharedCollider(DataBuilder builder, StandaloneBphclCollider collider, IDictionary<int, (StandaloneBphclCollider Definition, int ItemIndex)> sharedColliders, ICollection<int> outerColliders)
	{
		if (sharedColliders.TryGetValue(collider.Index, out (StandaloneBphclCollider, int) value))
		{
			if (!ColliderDefinitionMatches(value.Item1, collider))
			{
				throw new InvalidDataException($"Collider {collider.Index} has incompatible geometry in more than one cloth. " + "A standalone BPHCL must preserve one collider definition for each HKCL object identity.");
			}
			return value.Item2;
		}
		int num = WriteCollider(builder, collider);
		sharedColliders.Add(collider.Index, (collider, num));
		outerColliders.Add(num);
		return num;
	}

	private static bool ColliderDefinitionMatches(StandaloneBphclCollider left, StandaloneBphclCollider right)
	{
		if (string.Equals(left.ShapeType, right.ShapeType, StringComparison.Ordinal) && left.Start == right.Start && left.End == right.End && left.Radius.Equals(right.Radius) && left.PlaneNormal == right.PlaneNormal)
		{
			return left.Transform.Equals(right.Transform);
		}
		return false;
	}

	private static string NormalizeColliderName(string name)
	{
		return name;
	}

	private static int WriteCollider(DataBuilder builder, StandaloneBphclCollider collider, ColliderReservation? reservation = null)
	{
		int num;
		if ((object)reservation != null)
		{
			num = reservation.Shape.ItemIndex;
		}
		else
		{
			num = WriteColliderShape(builder, collider);
		}
		int itemIndex = num;
		DataBuilder.Allocation allocation;
		if ((object)reservation != null)
		{
			allocation = builder.Materialize(reservation.Object, 160, 16);
		}
		else
		{
			allocation = builder.AllocateObject(StandaloneTypeCatalog.Collidable, 160, 16);
		}
		DataBuilder.Allocation allocation2 = allocation;
		Matrix4x4 transform = collider.Transform;
		builder.WriteVector4(allocation2.DataOffset + 32, collider.NativeAxisX ?? new Vector4(transform.M11, transform.M12, transform.M13, transform.M14));
		builder.WriteVector4(allocation2.DataOffset + 48, collider.NativeAxisY ?? new Vector4(transform.M21, transform.M22, transform.M23, transform.M24));
		builder.WriteVector4(allocation2.DataOffset + 64, collider.NativeAxisZ ?? new Vector4(transform.M31, transform.M32, transform.M33, transform.M34));
		builder.WriteVector4(allocation2.DataOffset + 80, collider.NativeTranslation ?? new Vector4(transform.M41, transform.M42, transform.M43, transform.M44));
		builder.WriteReference(allocation2.DataOffset + 136, itemIndex);
		int num2;
		if ((object)reservation != null)
		{
			num2 = builder.WriteString(reservation.Name, NormalizeColliderName(collider.Name));
		}
		else
		{
			num2 = builder.WriteString(NormalizeColliderName(collider.Name));
		}
		int itemIndex2 = num2;
		builder.WriteReference(allocation2.DataOffset + 144, itemIndex2);
		builder.WriteSingle(allocation2.DataOffset + 152, collider.PinchDetectionRadius);
		builder.WriteByte(allocation2.DataOffset + 156, (byte)collider.PinchDetectionPriority);
		builder.WriteByte(allocation2.DataOffset + 157, collider.PinchDetectionEnabled ? ((byte)1) : ((byte)0));
		builder.WriteByte(allocation2.DataOffset + 158, collider.VirtualCollisionPointCollisionEnabled ? ((byte)1) : ((byte)0));
		builder.WriteByte(allocation2.DataOffset + 159, collider.Enabled ? ((byte)1) : ((byte)0));
		return allocation2.ItemIndex;
	}

	private static uint GetColliderShapeType(StandaloneBphclCollider collider)
	{
		string shapeType = collider.ShapeType;
		if ((shapeType == "hclSphereShape" || shapeType == "Sphere") ? true : false)
		{
			return StandaloneTypeCatalog.SphereShape;
		}
		string shapeType2 = collider.ShapeType;
		bool flag = ((shapeType2 == "hclPlaneShape" || shapeType2 == "Plane") ? true : false);
		return flag ? StandaloneTypeCatalog.PlaneShape : StandaloneTypeCatalog.CapsuleShape;
	}

	private static int WriteColliderShape(DataBuilder builder, StandaloneBphclCollider collider, DataBuilder.Reservation? reservation = null)
	{
		string shapeType = collider.ShapeType;
		bool flag = ((shapeType == "hclSphereShape" || shapeType == "Sphere") ? true : false);
		bool flag2 = flag;
		shapeType = collider.ShapeType;
		flag = ((shapeType == "hclPlaneShape" || shapeType == "Plane") ? true : false);
		bool flag3 = flag;
		uint colliderShapeType = GetColliderShapeType(collider);
		DataBuilder.Allocation allocation;
		if ((object)reservation != null)
		{
			allocation = builder.Materialize(reservation, flag3 ? 48 : 96, 16);
		}
		else
		{
			allocation = builder.AllocateObject(colliderShapeType, flag3 ? 48 : 96, 16);
		}
		DataBuilder.Allocation allocation2 = allocation;
		builder.WriteInt32(allocation2.DataOffset + 24, collider.ShapeKind);
		if (flag3)
		{
			builder.WriteVector4(allocation2.DataOffset + 32, collider.NativePlaneEquation ?? new Vector4(collider.PlaneNormal, 0f));
		}
		else if (flag2)
		{
			builder.WriteVector4(allocation2.DataOffset + 32, collider.NativeStart ?? new Vector4(collider.Start, collider.Radius));
		}
		else
		{
			Vector4 value = collider.NativeStart ?? new Vector4(collider.Start, 0f);
			Vector4 value2 = collider.NativeEnd ?? new Vector4(collider.End, 0f);
			Vector4 value3 = collider.NativeDirection ?? Vector4.Zero;
			float valueOrDefault = collider.NativeCapsuleLengthSquaredInverse.GetValueOrDefault();
			builder.WriteVector4(allocation2.DataOffset + 32, value);
			builder.WriteVector4(allocation2.DataOffset + 48, value2);
			builder.WriteVector4(allocation2.DataOffset + 64, value3);
			builder.WriteSingle(allocation2.DataOffset + 80, collider.Radius);
			builder.WriteSingle(allocation2.DataOffset + 84, valueOrDefault);
		}
		return allocation2.ItemIndex;
	}
}
