using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using HKX2;

namespace HKCLTool;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		try
		{
            if (args.Length >= 3 && args[0] == "--self-test-helper-group-removal")
            {
                HelperGroupRemovalChecks.Run(args[1], args.Skip(2).ToArray());
                return;
            }
            if (args.Length >= 3 && args[0] == "--self-test-model-editing")
            {
                ModelEditingChecks.Run(args[1], args[2], args.Skip(3).ToArray());
                return;
            }
            if (args.Length == 3 && args[0] == "--self-test-conversion-fidelity")
            {
                ConversionFidelityChecks.Run(args[1], args[2]);
                return;
            }
            if (args.Length == 2 && args[0] == "--self-test-totk-polytopes")
            {
                TotkPolytopeChecks.Run(args[1]);
                return;
            }
            if (args.Length == 3 && args[0] == "--survey-totk-polytopes")
            {
                TotkPolytopeSurvey.Run(args[1], args[2]);
                return;
            }
            if (args.Length == 3 && args[0] == "--self-test-actor-catalog")
            {
                ActorCatalogChecks.Run(args[1], args[2]);
                return;
            }
            if (args.Length == 3 && args[0] == "--inspect-actor-viewer")
            {
                BotwActorInspection.Write(args[1], args[2]);
                return;
            }
            if (args.Length is 3 or 4 && args[0] == "--check-actor-resources")
            {
                BotwActorInspection.Check(args[1], args[2], args.Length == 4 ? args[3] : null);
                return;
            }
            if (args.Length is 3 or 4 && args[0] == "--self-test-actor-viewer")
            {
                ActorViewerChecks.Run(args[1], args[2], args.Length == 4 ? args[3] : null);
                return;
            }
            if (args.Length >= 3 && args[0] == "--self-test-bphcl-particles")
            {
                BphclParticleChecks.Run(args[1], args.Skip(2).ToArray());
                return;
            }
            if (args.Length >= 3 && args[0] == "--self-test-editing")
            {
                EditingChecks.Run(args[1], args.Skip(2).ToArray());
                return;
            }
            if (args.Length == 2 && args[0] == "--self-test-animation-preview")
            {
                AnimationPreviewChecks.Run(args[1]);
                return;
            }
			if (args.Length == 2 && string.Equals(args[0], "--inspect-botw-model-container", StringComparison.OrdinalIgnoreCase))
			{
				var source = File.ReadAllBytes(args[1]);
				var data = source.AsSpan().StartsWith("Yaz0"u8) ? BotwYaz0.Decode(source) : source;
				var havokMagic = new byte[] { 0x57, 0xe0, 0xe0, 0x57, 0x10, 0xc0, 0xc0, 0x10 };
				var offsets = new List<int>();
				for (var index = 0; index <= data.Length - havokMagic.Length; index++)
				{
					if (data.AsSpan(index, havokMagic.Length).SequenceEqual(havokMagic))
						offsets.Add(index);
				}
				var strings = new List<string>();
				var start = -1;
				for (var index = 0; index <= data.Length; index++)
				{
					var printable = index < data.Length && data[index] is >= 0x20 and <= 0x7e;
					if (printable && start < 0) start = index;
					if (!printable && start >= 0)
					{
						if (index - start >= 4)
						{
							var value = Encoding.ASCII.GetString(data, start, index - start);
							if (value.Contains("hkrb", StringComparison.OrdinalIgnoreCase) || value.Contains("hkp", StringComparison.Ordinal))
								strings.Add(value);
						}
						start = -1;
					}
				}
				Console.WriteLine($"MODEL-CONTAINER|file={Path.GetFileName(args[1])}|compressed={source.AsSpan().StartsWith("Yaz0"u8)}|decoded={data.Length}|havok=[{string.Join(',', offsets)}]|strings=[{string.Join(';', strings.Distinct(StringComparer.Ordinal))}]");
				foreach (var offset in offsets)
				{
					var header = Convert.ToHexString(data.AsSpan(offset, Math.Min(64, data.Length - offset)));
					var pointerSize = data.Length > offset + 17 ? data[offset + 16] : 0;
					var littleEndian = data.Length > offset + 17 && data[offset + 17] != 0;
					Console.WriteLine($"HAVOK|offset={offset}|pointer={pointerSize}|little={littleEndian}|header={header}");
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--scan-actor-physics", StringComparison.OrdinalIgnoreCase))
			{
				var values = new Dictionary<string, (int Count, string Sample)>(StringComparer.OrdinalIgnoreCase);
				var packCount = 0;
				var physicsCount = 0;
				foreach (var path in Directory.EnumerateFiles(args[1], "*.sbactorpack", SearchOption.AllDirectories))
				{
					packCount++;
					var archive = BotwSarcArchive.Open(path);
					foreach (var entry in archive.Entries.Where(entry => entry.Name.EndsWith(".bphysics", StringComparison.OrdinalIgnoreCase)))
					{
						physicsCount++;
						var aamp = BotwAampReader.Read(archive.Read(entry).ToArray());
						foreach (var value in EnumerateStrings(aamp.Root))
						{
							if (!IsCollisionIdentifier(value))
								continue;
							values[value] = values.TryGetValue(value, out var current)
								? (current.Count + 1, current.Sample)
								: (1, Path.GetFileName(path));
						}
					}
				}
				Console.WriteLine($"ACTOR-PHYSICS-SCAN|directory={Path.GetFileName(args[1])}|packs={packCount}|physics={physicsCount}|identifiers={values.Count}");
				foreach (var pair in values.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
					Console.WriteLine($"VALUE|count={pair.Value.Count}|value={pair.Key}|sample={pair.Value.Sample}");
				return;

				static IEnumerable<string> EnumerateStrings(AampReadList list)
				{
					foreach (var obj in list.Objects)
					foreach (var parameter in obj.Parameters.Where(parameter => parameter.Type is 7 or 8 or 15 or 20))
						yield return parameter.AsString();
					foreach (var child in list.Children)
					foreach (var value in EnumerateStrings(child))
						yield return value;
				}

				static bool IsCollisionIdentifier(string value)
				{
					var terms = new[] { "shape", "sphere", "capsule", "box", "poly", "prism", "rigid", "mesh", ".hkrb", ".hkcl", ".hkrg", "cylinder", "convex" };
					return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-actor-collision", StringComparison.OrdinalIgnoreCase))
			{
				var archive = BotwSarcArchive.Open(args[1]);
				Console.WriteLine($"ACTOR-COLLISION|file={Path.GetFileName(args[1])}|endian={(archive.LittleEndian ? "little" : "big")}");
				foreach (var entry in archive.Entries.Where(entry =>
					entry.Name.Contains("Physics", StringComparison.OrdinalIgnoreCase) ||
					entry.Name.EndsWith(".hkrg", StringComparison.OrdinalIgnoreCase) ||
					entry.Name.EndsWith(".hkcl", StringComparison.OrdinalIgnoreCase)))
				{
					var bytes = archive.Read(entry).Span;
					var header = Convert.ToHexString(bytes[..Math.Min(24, bytes.Length)]);
					var strings = new List<string>();
					var start = -1;
					for (var index = 0; index <= bytes.Length; index++)
					{
						var printable = index < bytes.Length && bytes[index] is >= 0x20 and <= 0x7e;
						if (printable && start < 0)
							start = index;
						if (!printable && start >= 0)
						{
							if (index - start >= 4)
								strings.Add(Encoding.ASCII.GetString(bytes[start..index]));
							start = -1;
						}
					}
					var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16];
					Console.WriteLine($"ENTRY|name={entry.Name}|bytes={entry.Length}|sha256={hash}|header={header}|strings=[{string.Join(';', strings.Distinct(StringComparer.Ordinal))}]");
					if (entry.Name.EndsWith(".hkrb", StringComparison.OrdinalIgnoreCase))
						Console.WriteLine($"HKRB-GRAPH|types=[{SummarizeHavokGraph(bytes)}]");
				}
				return;

				static string SummarizeHavokGraph(ReadOnlySpan<byte> bytes)
				{
					using var stream = new MemoryStream(bytes.ToArray(), writable: false);
					var root = new PackFileDeserializer().Deserialize(new BinaryReaderEx(stream));
					var counts = new Dictionary<string, int>(StringComparer.Ordinal);
					var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
					Visit(root);
					return string.Join(';', counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value}"));

					void Visit(object? value)
					{
						if (value == null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || value.GetType().IsValueType)
							return;
						if (!visited.Add(value))
							return;
						if (value is System.Collections.IEnumerable enumerable)
						{
							foreach (var item in enumerable) Visit(item);
							return;
						}
						var type = value.GetType();
						if (type.Namespace?.StartsWith("HKX2", StringComparison.Ordinal) == true)
							counts[type.Name] = counts.GetValueOrDefault(type.Name) + 1;
						foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
							Visit(field.GetValue(value));
					}
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-actor-pack", StringComparison.OrdinalIgnoreCase))
			{
				var archive = BotwSarcArchive.Open(args[1]);
				Console.WriteLine($"ACTOR-PACK|file={Path.GetFileName(args[1])}|endian={(archive.LittleEndian ? "little" : "big")}|entries={archive.Entries.Count}");
				foreach (var entry in archive.Entries)
					Console.WriteLine($"ENTRY|name={entry.Name}|bytes={entry.Length}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--extract-actor-collision", StringComparison.OrdinalIgnoreCase))
			{
				var archive = BotwSarcArchive.Open(args[1]);
				var entries = archive.Entries
					.Where(entry => entry.Name.EndsWith(".hkrb", StringComparison.OrdinalIgnoreCase))
					.ToArray();
				if (entries.Length == 0)
					throw new InvalidDataException("The actor pack contains no HKRB collision files.");

				Directory.CreateDirectory(args[2]);
				foreach (var entry in entries)
				{
					var outputPath = Path.Combine(args[2], Path.GetFileName(entry.Name));
					File.WriteAllBytes(outputPath, archive.Read(entry).ToArray());
					Console.WriteLine($"EXTRACTED|file={Path.GetFileName(outputPath)}|bytes={entry.Length}");
				}
				Console.WriteLine($"ACTOR-COLLISION-EXTRACT|pack={Path.GetFileName(args[1])}|files={entries.Length}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--decompress-yaz0", StringComparison.OrdinalIgnoreCase))
			{
				var source = File.ReadAllBytes(args[1]);
				if (!source.AsSpan().StartsWith("Yaz0"u8))
					throw new InvalidDataException("The input file is not Yaz0-compressed.");

				var outputPath = Path.GetFullPath(args[2]);
				Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
				var decoded = BotwYaz0.Decode(source);
				File.WriteAllBytes(outputPath, decoded);
				Console.WriteLine($"YAZ0-DECOMPRESS|file={Path.GetFileName(args[1])}|compressed={source.Length}|raw={decoded.Length}|output={Path.GetFileName(outputPath)}");
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-collision-model", StringComparison.OrdinalIgnoreCase))
			{
				var meshes = BotwCollisionMeshImporter.Import(args[1]);
				Console.WriteLine(
					$"COLLISION-MODEL|file={Path.GetFileName(args[1])}|meshes={meshes.Count}|vertices={meshes.Sum(mesh => mesh.Vertices.Count)}|triangles={meshes.Sum(mesh => mesh.TriangleCount)}|materials={string.Join(',', meshes.Select(mesh => mesh.Material).Distinct(StringComparer.Ordinal))}");
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-collision-file", StringComparison.OrdinalIgnoreCase))
			{
				var meshes = BotwCollisionFileImporter.Import(args[1]);
				Console.WriteLine(
					$"COLLISION-FILE|file={Path.GetFileName(args[1])}|meshes={meshes.Count}|vertices={meshes.Sum(mesh => mesh.Vertices.Count)}|triangles={meshes.Sum(mesh => mesh.TriangleCount)}|materials={string.Join(',', meshes.Select(mesh => mesh.Material).Distinct(StringComparer.Ordinal))}");
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-collision-geometry", StringComparison.OrdinalIgnoreCase))
			{
				var meshes = BotwCollisionFileImporter.Import(args[1]);
				var allVertices = meshes.SelectMany(mesh => mesh.Vertices).ToArray();
				var minimum = allVertices.Aggregate(Vector3.Min);
				var maximum = allVertices.Aggregate(Vector3.Max);
				Console.WriteLine($"COLLISION-BOUNDS|file={Path.GetFileName(args[1])}|min={minimum}|max={maximum}|size={maximum - minimum}");
				foreach (var item in meshes.Select(mesh =>
				{
					var meshMinimum = mesh.Vertices.Aggregate(Vector3.Min);
					var meshMaximum = mesh.Vertices.Aggregate(Vector3.Max);
					var maximumEdge = 0.0f;
					for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
					{
						var a = mesh.Vertices[mesh.Indices[index]];
						var b = mesh.Vertices[mesh.Indices[index + 1]];
						var c = mesh.Vertices[mesh.Indices[index + 2]];
						maximumEdge = Math.Max(maximumEdge, Math.Max(Vector3.Distance(a, b), Math.Max(Vector3.Distance(b, c), Vector3.Distance(c, a))));
					}
					return new { mesh.Name, Minimum = meshMinimum, Maximum = meshMaximum, MaximumEdge = maximumEdge, mesh.TriangleCount };
				}).OrderByDescending(item => item.MaximumEdge).Take(12))
					Console.WriteLine($"MESH-BOUNDS|name={item.Name}|min={item.Minimum}|max={item.Maximum}|edge={item.MaximumEdge:R}|triangles={item.TriangleCount}");
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-static-collision-data", StringComparison.OrdinalIgnoreCase))
			{
				var source = File.ReadAllBytes(args[1]);
				var data = source.AsSpan().StartsWith("Yaz0"u8) ? BotwYaz0.Decode(source) : source;
				var magic = new byte[] { 0x57, 0xe0, 0xe0, 0x57, 0x10, 0xc0, 0xc0, 0x10 };
				var packfileOffset = -1;
				for (var offset = 0; offset <= data.Length - magic.Length; offset++)
					if (data.AsSpan(offset, magic.Length).SequenceEqual(magic)) packfileOffset = offset;
				if (packfileOffset < 0) throw new InvalidDataException("No Havok packfile was found.");
				var root = new PackFileDeserializer().Deserialize(new BinaryReaderEx(data[packfileOffset..]));
				var compounds = FindGraphObjects<hkpStaticCompoundShape>(root).ToArray();
				Console.WriteLine($"STATIC-DATA|compounds={compounds.Length}");
				foreach (var compound in compounds)
				{
					Console.WriteLine($"COMPOUND|instances={compound.m_instances.Count}");
					foreach (var instance in compound.m_instances.Take(8))
					{
						var m = instance.m_transform;
						Console.WriteLine($"INSTANCE|shape={instance.m_shape?.GetType().Name}|matrix={m.M11:R},{m.M12:R},{m.M13:R},{m.M14:R};{m.M21:R},{m.M22:R},{m.M23:R},{m.M24:R};{m.M31:R},{m.M32:R},{m.M33:R},{m.M34:R};{m.M41:R},{m.M42:R},{m.M43:R},{m.M44:R}");
					}
				}
				foreach (var compressed in FindGraphObjects<hkpBvCompressedMeshShape>(root).Take(4))
				{
					var tree = compressed.m_tree;
					Console.WriteLine($"TREE|sections={tree.m_sections.Count}|packed={tree.m_packedVertices.Count}|shared={tree.m_sharedVertices.Count}|sharedIndices={tree.m_sharedVerticesIndex.Count}|primitives={tree.m_primitives.Count}|domain={tree.m_domain.m_min}->{tree.m_domain.m_max}");
					foreach (var section in tree.m_sections.Take(4))
						Console.WriteLine($"SECTION|first={section.m_firstPackedVertex}|count={section.m_numPackedVertices}|codec={section.m_codecParms_0:R},{section.m_codecParms_1:R},{section.m_codecParms_2:R},{section.m_codecParms_3:R},{section.m_codecParms_4:R},{section.m_codecParms_5:R}|primitives={section.m_primitives.m_data:X8}|shared={section.m_sharedVertices.m_data:X8}|sharedCount={section.m_numSharedIndices}");
					Console.WriteLine($"SHARED|values={string.Join(',', tree.m_sharedVertices.Take(8).Select(value => value.ToString("X16", CultureInfo.InvariantCulture)))}");
				}
				return;

				static IEnumerable<T> FindGraphObjects<T>(object graph) where T : class
				{
					var pending = new Stack<object>();
					var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
					pending.Push(graph);
					while (pending.Count > 0)
					{
						var value = pending.Pop();
						if (!visited.Add(value)) continue;
						if (value is T match) yield return match;
						if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || value.GetType().IsValueType) continue;
						if (value is System.Collections.IEnumerable enumerable)
						{
							foreach (var item in enumerable) if (item != null) pending.Push(item);
							continue;
						}
						foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
							if (field.GetValue(value) is { } child) pending.Push(child);
					}
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--self-test-collision-model", StringComparison.OrdinalIgnoreCase))
			{
				var outputPath = Path.Combine(Path.GetTempPath(), $"PhysicsTool-model-collision-{Guid.NewGuid():N}.hksc");
				try
				{
					var meshes = BotwCollisionMeshImporter.Import(args[1]);
					var result = BotwCollisionBuilder.Build(meshes, new(outputPath, 1, Vector3.Zero, false));
					Console.WriteLine($"COLLISION-MODEL-TEST|file={Path.GetFileName(args[1])}|meshes={meshes.Count}|shapes={result.ShapeCount}|triangles={result.TriangleCount}|bytes={result.FileSize}");
					return;
				}
				finally
				{
					if (File.Exists(outputPath)) File.Delete(outputPath);
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--render-collision-dialog", StringComparison.OrdinalIgnoreCase))
			{
				AppLocalization.Initialize();
				ApplicationConfiguration.Initialize();
				using var dialog = new BotwCollisionDialog();
				dialog.Show();
				Application.DoEvents();
				using var bitmap = new Bitmap(dialog.Width, dialog.Height);
				dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
				bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
				dialog.Close();
				Console.WriteLine($"COLLISION-DIALOG|size={bitmap.Width}x{bitmap.Height}|language={AppLocalization.CurrentLanguage}");
				return;
			}
			if (args.Length == 1 && string.Equals(args[0], "--self-test-collision", StringComparison.OrdinalIgnoreCase))
			{
				var rawPath = Path.Combine(Path.GetTempPath(), $"PhysicsTool-collision-{Guid.NewGuid():N}.hksc");
				var compressedPath = Path.ChangeExtension(rawPath, ".shksc");
				var actorWiiUPath = Path.Combine(Path.GetTempPath(), $"PhysicsTool-actor-wiiu-{Guid.NewGuid():N}.hkrb");
				var actorSwitchPath = Path.Combine(Path.GetTempPath(), $"PhysicsTool-actor-switch-{Guid.NewGuid():N}.hkrb");
				var modelPath = Path.ChangeExtension(rawPath, ".obj");
				var colladaPath = Path.ChangeExtension(rawPath, ".dae");
				var exportedDaePath = Path.ChangeExtension(rawPath, ".export.dae");
				var exportedFbxPath = Path.ChangeExtension(rawPath, ".export.fbx");
				try
				{
					File.WriteAllText(modelPath, "o Triangle[Stone]\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
					var imported = BotwCollisionMeshImporter.Import(modelPath);
					if (imported.Count != 1 || imported[0].TriangleCount != 1 || imported[0].Material != "Material_Stone")
						throw new InvalidDataException("Collision self-test model import did not preserve its mesh.");
					File.WriteAllText(colladaPath, """
						<COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
						  <asset><unit meter="1"/><up_axis>Y_UP</up_axis></asset>
						  <library_geometries><geometry id="Triangle"><mesh>
						    <source id="positions"><float_array id="positions-array" count="9">0 0 0 1 0 0 0 1 0</float_array><technique_common><accessor source="#positions-array" count="3" stride="3"><param name="X" type="float"/><param name="Y" type="float"/><param name="Z" type="float"/></accessor></technique_common></source>
						    <vertices id="vertices"><input semantic="POSITION" source="#positions"/></vertices>
						    <triangles count="1"><input semantic="VERTEX" source="#vertices" offset="0"/><p>0 1 2</p></triangles>
						  </mesh></geometry></library_geometries>
						  <library_visual_scenes><visual_scene id="Scene"><node id="Triangle"><instance_geometry url="#Triangle"/></node></visual_scene></library_visual_scenes>
						  <scene><instance_visual_scene url="#Scene"/></scene>
						</COLLADA>
						""");
					var collada = BotwCollisionMeshImporter.Import(colladaPath);
					if (collada.Count != 1 || collada[0].TriangleCount != 1)
						throw new InvalidDataException("Collision self-test Collada import did not preserve its mesh.");
					var stressVertices = new List<Vector3>();
					var stressIndices = new List<int>();
					for (var index = 0; index < 130; index++)
					{
						var start = stressVertices.Count;
						stressVertices.AddRange([new(index * 2, 0, 0), new(index * 2 + 1, 0, 0), new(index * 2, 1, 0)]);
						stressIndices.AddRange([start, start + 1, start + 2]);
					}
					var stressMesh = new BotwCollisionMesh
					{
						Name = "ChunkStress[Metal]",
						Vertices = stressVertices,
						Indices = stressIndices,
						Material = "Material_Metal"
					};
					var raw = BotwCollisionBuilder.Build([imported[0], stressMesh], new(rawPath, 1, Vector3.Zero, false));
					var compressed = BotwCollisionBuilder.Build([imported[0], stressMesh], new(compressedPath, 1, Vector3.Zero, true));
					var actorWiiU = BotwCollisionBuilder.Build([imported[0], stressMesh], new(
						actorWiiUPath, 1, Vector3.Zero, false,
						BotwCollisionPlatform.WiiU, BotwCollisionTarget.ActorRigidBody, "RigidBody_0"));
					var actorSwitch = BotwCollisionBuilder.Build([imported[0], stressMesh], new(
						actorSwitchPath, 1, Vector3.Zero, false,
						BotwCollisionPlatform.Switch, BotwCollisionTarget.ActorRigidBody, "RigidBody_0"));
					if (raw.ShapeCount <= 2)
						throw new InvalidDataException("Collision self-test did not split the oversized mesh.");
					var actorWiiUBytes = File.ReadAllBytes(actorWiiUPath);
					var actorSwitchBytes = File.ReadAllBytes(actorSwitchPath);
					if (actorWiiUBytes[16] != 4 || actorWiiUBytes[17] != 0)
						throw new InvalidDataException("Wii U actor collision did not use the expected Havok layout.");
					if (actorSwitchBytes[16] != 8 || actorSwitchBytes[17] != 1)
						throw new InvalidDataException("Switch actor collision did not use the expected Havok layout.");
					var reopenedStatic = BotwCollisionFileImporter.Import(rawPath);
					var reopenedWiiU = BotwCollisionFileImporter.Import(actorWiiUPath);
					var reopenedSwitch = BotwCollisionFileImporter.Import(actorSwitchPath);
					if (reopenedStatic.Sum(mesh => mesh.TriangleCount) == 0 ||
						reopenedWiiU.Sum(mesh => mesh.TriangleCount) == 0 ||
						reopenedSwitch.Sum(mesh => mesh.TriangleCount) == 0)
						throw new InvalidDataException("Collision self-test could not recover generated geometry.");
					var previewModel = BotwCollisionVisualModel.Create(reopenedSwitch, actorSwitchPath);
					if (previewModel.TriangleCount == 0 || previewModel.Meshes.Count != reopenedSwitch.Count)
						throw new InvalidDataException("Collision self-test could not create preview geometry.");
					BotwCollisionExporter.Export(reopenedWiiU, exportedDaePath);
					BotwCollisionExporter.Export(reopenedSwitch, exportedFbxPath);
					if (BotwCollisionMeshImporter.Import(exportedDaePath).Sum(mesh => mesh.TriangleCount) == 0 ||
						BotwCollisionMeshImporter.Import(exportedFbxPath).Sum(mesh => mesh.TriangleCount) == 0)
						throw new InvalidDataException("Collision self-test could not reopen exported geometry.");
					var decoded = BotwYaz0.Decode(File.ReadAllBytes(compressedPath));
					if (!decoded.AsSpan().SequenceEqual(File.ReadAllBytes(rawPath)))
						throw new InvalidDataException("Collision self-test Yaz0 round trip did not match.");
					Console.WriteLine($"COLLISION-SELF-TEST|obj={imported.Count}|dae={collada.Count}|raw={raw.FileSize}|compressed={compressed.FileSize}|actorWiiU={actorWiiU.FileSize}|actorSwitch={actorSwitch.FileSize}|shapes={raw.ShapeCount}|triangles={raw.TriangleCount}");
					return;
				}
				finally
				{
					if (File.Exists(rawPath)) File.Delete(rawPath);
					if (File.Exists(compressedPath)) File.Delete(compressedPath);
					if (File.Exists(actorWiiUPath)) File.Delete(actorWiiUPath);
					if (File.Exists(actorSwitchPath)) File.Delete(actorSwitchPath);
					if (File.Exists(modelPath)) File.Delete(modelPath);
					if (File.Exists(colladaPath)) File.Delete(colladaPath);
					if (File.Exists(exportedDaePath)) File.Delete(exportedDaePath);
					if (File.Exists(exportedFbxPath)) File.Delete(exportedFbxPath);
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--inspect-physics-visual-binds", StringComparison.OrdinalIgnoreCase))
			{
				var service = new HkclService();
				service.Load(args[1]);
				using var pose = VisualModelImporter.OpenPose(args[2]);
				var visualByName = pose.Bones
					.GroupBy(bone => Normalize(bone.Name), StringComparer.OrdinalIgnoreCase)
					.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
				for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
				{
					var preview = service.GetParticlePreview(clothIndex);
					var driven = preview.BoneBindings.Select(binding => binding.BoneIndex).ToHashSet();
					foreach (var physics in preview.Bones.Where(bone => driven.Contains(bone.Index)))
					{
						if (!visualByName.TryGetValue(Normalize(physics.Name), out var visual))
						{
							Console.WriteLine($"bindCompare|cloth={clothIndex}:{service.GetClothName(clothIndex)}|bone={physics.Index}:{physics.Name}|visual=missing");
							continue;
						}
						Console.WriteLine(
							$"bindCompare|cloth={clothIndex}:{service.GetClothName(clothIndex)}|bone={physics.Index}:{physics.Name}|position={Vector3.Distance(physics.Position, visual.Position):R}|axisDots={Vector3.Dot(physics.AxisX, visual.AxisX):R},{Vector3.Dot(physics.AxisY, visual.AxisY):R},{Vector3.Dot(physics.AxisZ, visual.AxisZ):R}");
					}
				}
				return;

				static string Normalize(string name)
				{
					var value = name ?? string.Empty;
					var pipe = value.LastIndexOf('|');
					if (pipe >= 0)
						value = value[(pipe + 1)..];
					var colon = value.LastIndexOf(':');
					return colon >= 0 ? value[(colon + 1)..] : value;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-cloth-bone-deformers", StringComparison.OrdinalIgnoreCase))
			{
				var service = new HkclService();
				service.Load(args[1]);
				for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
				{
					var deformers = service.GetSimpleMeshBoneDeformData(clothIndex);
					var preview = service.GetParticlePreview(clothIndex);
					var runtime = service.GetClothRuntimeData(clothIndex);
					var driven = preview.BoneBindings.Select(binding => binding.BoneIndex).ToHashSet();
					var inherited = preview.Bones.Where(bone =>
						!driven.Contains(bone.Index) &&
						HasDrivenAncestor(bone.ParentIndex, preview.Bones, driven)).ToArray();
					Console.WriteLine(
						$"clothBoneDeformers|index={clothIndex}|name={service.GetClothName(clothIndex)}|operators={deformers.Count}|values=[{string.Join(";", deformers.Select(deformer => $"op{deformer.OperatorIndex}:in{deformer.InputBufferIndex}:axis{deformer.BoneAxis}:pairs{deformer.TriangleBonePairs.Count}:locals{deformer.LocalBoneTransforms.Count}"))}]|buffers=[{string.Join(";", runtime.Buffers.Select(buffer => $"{buffer.Index}:{buffer.BufferName}:v{buffer.VertexCount}:t{buffer.TriangleCount}:indices{buffer.TriangleIndices.Count}"))}]|previewTriangles={preview.Triangles.Count}|driven=[{string.Join(";", preview.Bones.Where(bone => driven.Contains(bone.Index)).Select(bone => $"{bone.Index}:{bone.Name}"))}]|inherited=[{string.Join(";", inherited.Select(bone => $"{bone.Index}:{bone.Name}"))}]");
					var particles = preview.Particles.ToDictionary(particle => particle.Index);
					foreach (var deformer in deformers)
					{
						foreach (var pair in deformer.TriangleBonePairs)
						{
							var boneIndex = pair.BoneOffset / 64;
							var triangleIndex = pair.TriangleOffset / 6;
							if (triangleIndex >= preview.Triangles.Count)
								continue;
							var triangle = preview.Triangles[triangleIndex];
							if (!particles.TryGetValue(triangle.ParticleA, out var a) ||
								!particles.TryGetValue(triangle.ParticleB, out var b) ||
								!particles.TryGetValue(triangle.ParticleC, out var c))
								continue;
							var centroid = (a.Position + b.Position + c.Position) / 3.0f;
							var boneName = preview.Bones.FirstOrDefault(bone => bone.Index == boneIndex)?.Name ?? "?";
							Console.WriteLine($"triangleBone|cloth={clothIndex}|bone={boneIndex}:{boneName}|triangle={triangleIndex}:{triangle.ParticleA},{triangle.ParticleB},{triangle.ParticleC}|centroid={centroid.X:R},{centroid.Y:R},{centroid.Z:R}");
						}
					}
				}
				return;

				static bool HasDrivenAncestor(
					int boneIndex,
					IReadOnlyList<BonePreviewPoint> bones,
					IReadOnlySet<int> driven)
				{
					var byIndex = bones.ToDictionary(bone => bone.Index);
					var visited = new HashSet<int>();
					while (boneIndex >= 0 && visited.Add(boneIndex) && byIndex.TryGetValue(boneIndex, out var bone))
					{
						if (driven.Contains(boneIndex))
							return true;
						boneIndex = bone.ParentIndex;
					}
					return false;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-bone-scales", StringComparison.OrdinalIgnoreCase))
			{
				var document = NativeBphclDocument.Open(args[1]);
				foreach (var skeleton in document.Skeletons)
				{
					var scaled = skeleton.Bones.Where(bone =>
						MathF.Abs(bone.Scale.X - 1.0f) > 0.000001f ||
						MathF.Abs(bone.Scale.Y - 1.0f) > 0.000001f ||
						MathF.Abs(bone.Scale.Z - 1.0f) > 0.000001f).ToArray();
					Console.WriteLine($"boneScales|skeleton={skeleton.Index}:{skeleton.Name}|bones={skeleton.Bones.Count}|nonUnit={scaled.Length}|values=[{string.Join(";", scaled.Select(bone => $"{bone.Index}:{bone.Name}:{bone.Scale.X:R},{bone.Scale.Y:R},{bone.Scale.Z:R}"))}]");
				}
				return;
			}
			if (args.Length is 3 or 4 && string.Equals(args[0], "--validate-fbx-override-roundtrip", StringComparison.OrdinalIgnoreCase))
			{
				using var animation = VisualModelImporter.OpenAnimation(args[1], args[2]);
				var frame = args.Length == 4
					? double.Parse(args[3], CultureInfo.InvariantCulture)
					: animation.StartFrame;
				var baseline = animation.Sample(frame);
				var overrides = baseline.Bones.Select(bone => new VisualBoneOverride(
					bone.Name,
					bone.Position,
					bone.Rotation,
					bone.Scale)).ToArray();
				var roundTrip = animation.Sample(frame, boneOverrides: overrides);
				var maxPositionError = 0.0f;
				var maxNormalError = 0.0f;
				var comparedVertices = 0;
				for (var meshIndex = 0; meshIndex < Math.Min(baseline.Meshes.Count, roundTrip.Meshes.Count); meshIndex++)
				{
					var expected = baseline.Meshes[meshIndex].Vertices;
					var actual = roundTrip.Meshes[meshIndex].Vertices;
					for (var offset = 0; offset + 7 < Math.Min(expected.Length, actual.Length); offset += 8)
					{
						maxPositionError = Math.Max(maxPositionError, Vector3.Distance(
							new Vector3(expected[offset], expected[offset + 1], expected[offset + 2]),
							new Vector3(actual[offset], actual[offset + 1], actual[offset + 2])));
						maxNormalError = Math.Max(maxNormalError, Vector3.Distance(
							new Vector3(expected[offset + 3], expected[offset + 4], expected[offset + 5]),
							new Vector3(actual[offset + 3], actual[offset + 4], actual[offset + 5])));
						comparedVertices++;
					}
				}
				Console.WriteLine(
					$"fbxOverrideRoundTrip|frame={frame:R}|bones={overrides.Length}|meshes={baseline.Meshes.Count}/{roundTrip.Meshes.Count}|vertices={comparedVertices}|maxPositionError={maxPositionError:R}|maxNormalError={maxNormalError:R}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--validate-animation-tracks", StringComparison.OrdinalIgnoreCase))
			{
				using var animation = VisualModelImporter.OpenAnimation(args[1], args[2]);
				Console.WriteLine(
					$"animationTracks|start={animation.StartFrame:R}|end={animation.EndFrame:R}|fps={animation.FramesPerSecond:R}|bones={animation.AnimatedBoneNames.Count}|names=[{string.Join(";", animation.AnimatedBoneNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}]");
				return;
			}
			if (args.Length is >= 2 and <= 4 && string.Equals(args[0], "--validate-cloth-preview-simulation", StringComparison.OrdinalIgnoreCase))
			{
				var frames = args.Length >= 3
					? Math.Clamp(int.Parse(args[2], CultureInfo.InvariantCulture), 1, 3600)
					: 240;
				var stage = args.Length == 4 ? args[3] : "all";
				var diagnosticSolverIterations = 1;
				if (stage.StartsWith("solver", StringComparison.OrdinalIgnoreCase) &&
					int.TryParse(stage[6..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSolverIterations))
				{
					diagnosticSolverIterations = Math.Clamp(parsedSolverIterations, 1, 24);
					stage = "all";
				}
				var service = new HkclService();
				service.Load(args[1]);
				for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
				{
					var preview = service.GetParticlePreview(clothIndex);
					if (!string.Equals(stage, "all", StringComparison.OrdinalIgnoreCase))
					{
						preview.ConstraintExecution.Clear();
						switch (stage.ToLowerInvariant())
						{
							case "local": preview.ConstraintExecution.Add(2); break;
							case "standard": preview.ConstraintExecution.Add(0); break;
							case "stretch": preview.ConstraintExecution.Add(1); break;
							case "collisions": preview.ConstraintExecution.Add(-1); break;
							case "no-collisions":
								preview.ConstraintExecution.AddRange(new[] { 2, 3, 0, 0, 1 });
								break;
							default: throw new ArgumentException($"Unknown simulation stage '{stage}'.");
						}
					}
					var simulator = new HkclPreviewSimulator(preview);
					simulator.SolverIterations = diagnosticSolverIterations;
					var bindingFallbacks = 0;
					var bindingFlipCorrections = 0;
					var maxBonePositionStep = 0.0f;
					var maxBoneAngleStepDegrees = 0.0f;
					var maxBoneAngleName = string.Empty;
					var maxBoneAngleFrame = -1;
					var maxBoneAngleAxisSteps = Vector3.Zero;
					var boneAngleSpikes = 0;
					var maxJointLengthRatio = 1.0f;
					var maxJointLengthStep = 0.0f;
					var maxJointLengthBone = string.Empty;
					var maxJointLengthFrame = -1;
					var restBonesByIndex = preview.Bones.ToDictionary(bone => bone.Index);
					IReadOnlyDictionary<int, SimulatedBonePreviewPose>? previousBonePoses = null;
					for (var frame = 0; frame < frames; frame++)
					{
						simulator.Step(1.0f / 60.0f);
						var currentBonePoses = simulator.GetBonePoses();
						if (previousBonePoses != null)
						{
							foreach (var (boneIndex, currentPose) in currentBonePoses)
							{
								if (!previousBonePoses.TryGetValue(boneIndex, out var previousPose))
									continue;

								maxBonePositionStep = Math.Max(
									maxBonePositionStep,
									Vector3.Distance(previousPose.Position, currentPose.Position));
								var trace = Vector3.Dot(previousPose.AxisX, currentPose.AxisX) +
									Vector3.Dot(previousPose.AxisY, currentPose.AxisY) +
									Vector3.Dot(previousPose.AxisZ, currentPose.AxisZ);
								var angle = MathF.Acos(Math.Clamp((trace - 1.0f) * 0.5f, -1.0f, 1.0f)) *
									180.0f / MathF.PI;
								if (angle > maxBoneAngleStepDegrees)
								{
									maxBoneAngleStepDegrees = angle;
									maxBoneAngleName = restBonesByIndex.TryGetValue(boneIndex, out var angleBone)
										? angleBone.Name
										: boneIndex.ToString(CultureInfo.InvariantCulture);
									maxBoneAngleFrame = frame;
									maxBoneAngleAxisSteps = new Vector3(
										MathF.Acos(Math.Clamp(Vector3.Dot(previousPose.AxisX, currentPose.AxisX), -1.0f, 1.0f)) * 180.0f / MathF.PI,
										MathF.Acos(Math.Clamp(Vector3.Dot(previousPose.AxisY, currentPose.AxisY), -1.0f, 1.0f)) * 180.0f / MathF.PI,
										MathF.Acos(Math.Clamp(Vector3.Dot(previousPose.AxisZ, currentPose.AxisZ), -1.0f, 1.0f)) * 180.0f / MathF.PI);
								}
								if (angle > 45.0f)
									boneAngleSpikes++;

								if (!restBonesByIndex.TryGetValue(boneIndex, out var restBone) ||
									restBone.ParentIndex < 0 ||
									!restBonesByIndex.TryGetValue(restBone.ParentIndex, out var restParent) ||
									!currentBonePoses.TryGetValue(restBone.ParentIndex, out var currentParent) ||
									!previousBonePoses.TryGetValue(restBone.ParentIndex, out var previousParent))
								{
									continue;
								}

								var restLength = Vector3.Distance(restBone.Position, restParent.Position);
								var currentLength = Vector3.Distance(currentPose.Position, currentParent.Position);
								var previousLength = Vector3.Distance(previousPose.Position, previousParent.Position);
								if (restLength > 0.000001f && currentLength / restLength > maxJointLengthRatio)
								{
									maxJointLengthRatio = currentLength / restLength;
									maxJointLengthBone = $"{restParent.Name}>{restBone.Name}";
									maxJointLengthFrame = frame;
								}
								maxJointLengthStep = Math.Max(maxJointLengthStep, MathF.Abs(currentLength - previousLength));
							}
						}
						previousBonePoses = currentBonePoses;
						bindingFallbacks += simulator.LastBindingFallbackCount;
						bindingFlipCorrections += simulator.LastBindingFlipCorrectionCount;
					}
					var positions = simulator.GetPositions();
					var rest = preview.Particles.ToDictionary(particle => particle.Index, particle => particle.Position);
					var finite = positions.Values.All(value => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z));
					var maxExcursion = positions.Count == 0
						? 0.0f
						: positions.Max(entry => rest.TryGetValue(entry.Key, out var origin) ? Vector3.Distance(entry.Value, origin) : 0.0f);
					var bonePoses = simulator.GetBonePoses().Values.ToArray();
					var maxBoneOrthogonalityError = bonePoses.Length == 0 ? 0.0f : bonePoses.Max(pose => Math.Max(
						MathF.Abs(Vector3.Dot(pose.AxisX, pose.AxisY)),
						Math.Max(
							MathF.Abs(Vector3.Dot(pose.AxisX, pose.AxisZ)),
							MathF.Abs(Vector3.Dot(pose.AxisY, pose.AxisZ)))));
					var minBoneHandedness = bonePoses.Length == 0 ? 1.0f : bonePoses.Min(pose =>
						Vector3.Dot(Vector3.Cross(pose.AxisX, pose.AxisY), pose.AxisZ));
					Console.WriteLine($"clothPreviewSimulation|index={clothIndex}|name={service.GetClothName(clothIndex)}|frames={frames}|particles={positions.Count}|localRange={preview.LocalRanges.Count}|bend={preview.BendStiffnessConstraints.Count}|substeps={preview.SimulationSubSteps}|iterations={preview.SimulationSolveIterations}|adaptive={preview.AdaptConstraintStiffness}|execution=[{string.Join(',', preview.ConstraintExecution)}]|transfer={preview.TransferMotionEnabled}|finite={finite}|maxExcursion={maxExcursion:R}|bindingFallbacks={bindingFallbacks}|bindingFlipCorrections={bindingFlipCorrections}");
					Console.WriteLine($"clothPreviewBoneBasis|index={clothIndex}|maxOrthogonalityError={maxBoneOrthogonalityError:R}|minHandedness={minBoneHandedness:R}|maxPositionStep={maxBonePositionStep:R}|maxAngleStepDegrees={maxBoneAngleStepDegrees:R}|maxAngleBone={maxBoneAngleName}|maxAngleFrame={maxBoneAngleFrame}|maxAngleAxisSteps={maxBoneAngleAxisSteps.X:R},{maxBoneAngleAxisSteps.Y:R},{maxBoneAngleAxisSteps.Z:R}|angleSpikesOver45={boneAngleSpikes}|maxJointLengthRatio={maxJointLengthRatio:R}|maxJointLengthStep={maxJointLengthStep:R}|maxJoint={maxJointLengthBone}|maxJointFrame={maxJointLengthFrame}");
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--validate-cloth-preview-rest", StringComparison.OrdinalIgnoreCase))
			{
				var service = new HkclService();
				service.Load(args[1]);
				for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
				{
					var preview = service.GetParticlePreview(clothIndex);
					var simulator = new HkclPreviewSimulator(preview);
					var poses = simulator.GetBonePoses();
					var restBones = preview.Bones.ToDictionary(bone => bone.Index);
					var maxPositionError = 0.0f;
					var maxAxisErrorDegrees = 0.0f;
					var measured = 0;
					foreach (var boneIndex in simulator.DrivenBoneIndices)
					{
						if (!restBones.TryGetValue(boneIndex, out var rest) || !poses.TryGetValue(boneIndex, out var pose))
							continue;

						maxPositionError = Math.Max(maxPositionError, Vector3.Distance(rest.Position, pose.Position));
						foreach (var (restAxis, currentAxis) in new[]
						{
							(rest.AxisX, pose.AxisX),
							(rest.AxisY, pose.AxisY),
							(rest.AxisZ, pose.AxisZ)
						})
						{
							var dot = Math.Clamp(Vector3.Dot(Vector3.Normalize(restAxis), Vector3.Normalize(currentAxis)), -1.0f, 1.0f);
							maxAxisErrorDegrees = Math.Max(maxAxisErrorDegrees, MathF.Acos(dot) * 180.0f / MathF.PI);
						}
						measured++;
					}

					Console.WriteLine(
						$"clothPreviewRest|index={clothIndex}|name={service.GetClothName(clothIndex)}|bindings={preview.BoneBindings.Count}|localFirst={simulator.LocalFirstBoneBindingCount}|frameFirst={simulator.FrameFirstBoneBindingCount}|affine={simulator.AffineBoneBindingCount}|nativeCentroid={simulator.NativeCentroidBoneBindingCount}|bones={measured}|maxPositionError={maxPositionError:R}|maxAxisErrorDegrees={maxAxisErrorDegrees:R}");
				}
				return;
			}
			if (args.Length == 4 && string.Equals(args[0], "--delete-bphcl-cloth", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument source = NativeBphclDocument.Open(args[1]);
				int clothIndex = int.Parse(args[2], CultureInfo.InvariantCulture);
				if (clothIndex < 0 || clothIndex >= source.Cloths.Count)
				{
					throw new ArgumentOutOfRangeException("clothIndex");
				}
				string removedName = source.Cloths[clothIndex].Name;
				File.WriteAllBytes(args[3], NativeBphclWriter.DeleteClothAndCompact(source, clothIndex));
				NativeBphclDocument result = NativeBphclDocument.Open(args[3]);
				Console.WriteLine($"BPHCL-DELETE|removed={clothIndex}:{removedName}|before={source.Cloths.Count}|after={result.Cloths.Count}|output={args[3]}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--compare-bphcl-cloth-types", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument target = NativeBphclDocument.Open(args[1]);
				NativeBphclDocument source2 = NativeBphclDocument.Open(args[2]);
				{
					foreach (NativeBphclCloth cloth in source2.Cloths)
					{
						NativeBphclSkeleton skeleton = source2.Skeletons.ElementAtOrDefault(cloth.Index) ?? throw new InvalidDataException("Cloth '" + cloth.Name + "' has no paired skeleton.");
						IReadOnlyList<int> closure = source2.CollectItemClosure(new int[2] { cloth.ItemIndex, skeleton.ItemIndex });
						uint[] requiredTypes = closure.Select((int index9) => source2.GetItem(index9).TypeIndex).Concat(from nativeBphclPatch in source2.GetPatchesForItems(closure)
							select nativeBphclPatch.TypeIndex).Distinct()
							.ToArray();
						IReadOnlyList<string> missing = NativeBphclTypeTable.FindMissingRequiredTypes(target, source2, requiredTypes);
						bool targetMatch = target.Cloths.Any((NativeBphclCloth nativeBphclCloth) => string.Equals(ClothKey(nativeBphclCloth.Name), ClothKey(cloth.Name), StringComparison.Ordinal));
						Console.WriteLine($"clothType|cloth={cloth.Name}|targetMatch={targetMatch}|required={requiredTypes.Length}|incompatible={missing.Count}|types=[{string.Join(";", missing)}]");
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--prune-bphcl-colliders", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document = NativeBphclDocument.Open(args[1]);
				File.WriteAllBytes(args[2], NativeBphclMerge.PruneUnreferencedColliders(document));
				NativeBphclDocument pruned = NativeBphclDocument.Open(args[2]);
				Console.WriteLine($"BPHCL-PRUNE-COLLIDERS|before={document.Colliders.Count}|after={pruned.Colliders.Count}|output={args[2]}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--compact-bphcl", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document2 = NativeBphclDocument.Open(args[1]);
				NativeBphclWriter.SaveCompactedCopy(document2, args[2]);
				NativeBphclDocument compacted = NativeBphclDocument.Open(args[2]);
				Console.WriteLine($"BPHCL-COMPACT|beforeItems={document2.Items.Count}|afterItems={compacted.Items.Count}|cloths={compacted.Cloths.Count}|output={args[2]}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--materialize-bphcl-root-array-counts", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document3 = NativeBphclDocument.Open(args[1]);
				NativeBphclSection dataSection = document3.DataSection ?? throw new InvalidDataException("The BPHCL has no DATA section.");
				byte[] data = document3.Bytes.AsSpan(dataSection.PayloadOffset, dataSection.PayloadSize).ToArray();
				NativeBphclNamedVariant clothContainer = document3.FindRootVariants("hclClothContainer").Single();
				NativeBphclNamedVariant animationContainer = document3.FindRootVariants("hkaAnimationContainer").Single();
				NativeBphclReferenceArray[] arrays = new NativeBphclReferenceArray[3]
				{
					document3.GetReferenceArray(clothContainer.ObjectDataOffset + 24),
					document3.GetReferenceArray(clothContainer.ObjectDataOffset + 40),
					document3.GetReferenceArray(animationContainer.ObjectDataOffset + 24)
				};
				NativeBphclReferenceArray[] array = arrays;
				checked
				{
					foreach (NativeBphclReferenceArray array2 in array)
					{
						uint count = array2.StorageItem.Count;
						BitConverter.GetBytes(count).CopyTo(data, (int)array2.FieldOffset + 8);
						BitConverter.GetBytes((BitConverter.ToUInt32(data, (int)array2.FieldOffset + 12) & 0xC0000000u) | count).CopyTo(data, (int)array2.FieldOffset + 12);
					}
					byte[] rebuilt = NativeBphclTagFileBuilder.Rebuild(document3, data, document3.Items, document3.InternalPatches);
					File.WriteAllBytes(args[2], rebuilt);
					Console.WriteLine($"rootArrayCounts|colliders={arrays[0].StorageItem.Count}|cloths={arrays[1].StorageItem.Count}|skeletons={arrays[2].StorageItem.Count}|output={args[2]}");
					return;
				}
			}
			if (args.Length == 4 && string.Equals(args[0], "--align-bphcl-shared-order", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument target2 = NativeBphclDocument.Open(args[1]);
				NativeBphclDocument orderingSource = NativeBphclDocument.Open(args[2]);
				NativeBphclSection targetData = target2.DataSection ?? throw new InvalidDataException("The target BPHCL has no DATA section.");
				byte[] data2 = target2.Bytes.AsSpan(targetData.PayloadOffset, targetData.PayloadSize).ToArray();
				Dictionary<string, NativeBphclCloth> targetClothByName = target2.Cloths.ToDictionary<NativeBphclCloth, string, NativeBphclCloth>((NativeBphclCloth nativeBphclCloth) => Key(nativeBphclCloth.Name), (NativeBphclCloth result9) => result9, StringComparer.Ordinal);
				NativeBphclCloth[] orderedTargetCloths = orderingSource.Cloths.Select(delegate(NativeBphclCloth nativeBphclCloth)
				{
					if (!targetClothByName.TryGetValue(Key(nativeBphclCloth.Name), out var value2))
					{
						throw new InvalidDataException("The target BPHCL has no cloth matching '" + nativeBphclCloth.Name + "'.");
					}
					return value2;
				}).ToArray();
				if (orderedTargetCloths.Length != target2.Cloths.Count)
				{
					throw new InvalidDataException("The two BPHCL files do not contain the same cloth set.");
				}
				NativeBphclNamedVariant clothContainer2 = target2.FindRootVariants("hclClothContainer").Single();
				NativeBphclNamedVariant animationContainer2 = target2.FindRootVariants("hkaAnimationContainer").Single();
				RewriteReferenceOrder(data2, target2.GetReferenceArray(clothContainer2.ObjectDataOffset + 40), orderedTargetCloths.Select((NativeBphclCloth nativeBphclCloth) => nativeBphclCloth.ItemIndex).ToArray());
				RewriteReferenceOrder(data2, target2.GetReferenceArray(animationContainer2.ObjectDataOffset + 24), orderedTargetCloths.Select((NativeBphclCloth nativeBphclCloth) => target2.Skeletons[nativeBphclCloth.Index].ItemIndex).ToArray());
				Dictionary<string, Queue<NativeBphclCollider>> targetColliderQueues = target2.Colliders.GroupBy<NativeBphclCollider, string>((NativeBphclCollider nativeBphclCollider) => Key(nativeBphclCollider.Name), StringComparer.Ordinal).ToDictionary<IGrouping<string, NativeBphclCollider>, string, Queue<NativeBphclCollider>>((IGrouping<string, NativeBphclCollider> grouping) => grouping.Key, (IGrouping<string, NativeBphclCollider> collection) => new Queue<NativeBphclCollider>(collection), StringComparer.Ordinal);
				List<int> orderedColliderItems = new List<int>();
				foreach (NativeBphclCollider sourceCollider in orderingSource.Colliders)
				{
					if (!targetColliderQueues.TryGetValue(Key(sourceCollider.Name), out var matches) || matches.Count == 0)
					{
						throw new InvalidDataException("The target BPHCL has no collider matching '" + sourceCollider.Name + "'.");
					}
					orderedColliderItems.Add(matches.Dequeue().ItemIndex);
				}
				if (orderedColliderItems.Count != target2.Colliders.Count || targetColliderQueues.Values.Any((Queue<NativeBphclCollider> queue) => queue.Count != 0))
				{
					throw new InvalidDataException("The two BPHCL files do not contain the same collider multiset.");
				}
				RewriteReferenceOrder(data2, target2.GetReferenceArray(clothContainer2.ObjectDataOffset + 24), orderedColliderItems);
				byte[] replacementAamp = checked(orderingSource.Bytes.AsSpan((int)orderingSource.Header.ParameterOffset, (int)orderingSource.Header.ParameterSize)).ToArray();
				byte[] rebuilt2 = NativeBphclTagFileBuilder.Rebuild(target2, data2, target2.Items, target2.InternalPatches, null, replacementAamp);
				File.WriteAllBytes(args[3], rebuilt2);
				Console.WriteLine($"sharedOrder|cloths={orderedTargetCloths.Length}|colliders={orderedColliderItems.Count}|output={args[3]}");
				return;
			}
			if (args.Length == 4 && string.Equals(args[0], "--replace-bphcl-aamp", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument target3 = NativeBphclDocument.Open(args[1]);
				NativeBphclDocument source3 = NativeBphclDocument.Open(args[2]);
				if (source3.Header.ParameterSize == 0)
				{
					throw new InvalidDataException("The source BPHCL has no embedded AAMP block.");
				}
				int sourceOffset;
				int sourceSize;
				checked
				{
					sourceOffset = (int)source3.Header.ParameterOffset;
					sourceSize = (int)source3.Header.ParameterSize;
				}
				if (sourceOffset < 0 || sourceOffset > source3.Bytes.Length - sourceSize)
				{
					throw new InvalidDataException("The source BPHCL AAMP block lies outside the file.");
				}
				byte[] replacementAamp2 = source3.Bytes.AsSpan(sourceOffset, sourceSize).ToArray();
				NativeBphclSection data3 = target3.DataSection ?? throw new InvalidDataException("The target BPHCL has no DATA section.");
				byte[] rebuilt3 = NativeBphclTagFileBuilder.Rebuild(target3, target3.Bytes.AsSpan(data3.PayloadOffset, data3.PayloadSize), target3.Items, target3.InternalPatches, null, replacementAamp2);
				File.WriteAllBytes(args[3], rebuilt3);
				Console.WriteLine($"aampReplace|target={args[1]}|source={args[2]}|bytes={replacementAamp2.Length}|output={args[3]}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--compare-bphcl-patch-groups", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument native = NativeBphclDocument.Open(args[1]);
				NativeBphclDocument rebuilt4 = NativeBphclDocument.Open(args[2]);
				int maximum = Math.Max(native.InternalPatches.Count, rebuilt4.InternalPatches.Count);
				Console.WriteLine($"patchGroups|native={native.InternalPatches.Count}|rebuilt={rebuilt4.InternalPatches.Count}");
				for (int index = 0; index < maximum; index++)
				{
					bool hasNative = index < native.InternalPatches.Count;
					bool hasRebuilt = index < rebuilt4.InternalPatches.Count;
					if (!(hasNative & hasRebuilt) || native.InternalPatches[index].TypeIndex != rebuilt4.InternalPatches[index].TypeIndex || native.InternalPatches[index].Offsets.Count != rebuilt4.InternalPatches[index].Offsets.Count)
					{
						string nativeText = (hasNative ? $"{native.InternalPatches[index].TypeIndex}:{native.GetTypeName(native.InternalPatches[index].TypeIndex)}[{native.InternalPatches[index].Offsets.Count}]" : "<none>");
						string rebuiltText = (hasRebuilt ? $"{rebuilt4.InternalPatches[index].TypeIndex}:{rebuilt4.GetTypeName(rebuilt4.InternalPatches[index].TypeIndex)}[{rebuilt4.InternalPatches[index].Offsets.Count}]" : "<none>");
						Console.WriteLine($"patchGroup|index={index}|native={nativeText}|rebuilt={rebuiltText}");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-type-section", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document4 = NativeBphclDocument.Open(args[1]);
				NativeBphclSection section = document4.TypeSection ?? throw new InvalidDataException("BPHCL has no TYPE section.");
				byte[] embedded = NativeBphclReflectionCatalog.TypeSection;
				Console.WriteLine($"typeSection|nativeBytes={section.Size}|nativeHash={Convert.ToHexString(SHA256.HashData(document4.Bytes.AsSpan(section.Offset, section.Size)))}");
				Console.WriteLine($"typeSection|embeddedBytes={embedded.Length}|embeddedHash={Convert.ToHexString(SHA256.HashData(embedded))}");
				return;
			}
			checked
			{
				if (args.Length >= 4 && string.Equals(args[0], "--extend-embedded-game-ready-bphcl-type-catalog", StringComparison.OrdinalIgnoreCase))
				{
					NativeBphclDocument sourceDocument = NativeBphclDocument.Open(args[1]);
					string outputPath = args[2];
					HashSet<string> requestedTypes = args.Skip(3).ToHashSet(StringComparer.Ordinal);
					uint[] requiredSourceTypes = (from entry in sourceDocument.TypeNames.Select((name, index) => (name, index))
						where entry.index > 0 && requestedTypes.Contains(entry.name)
						select (uint)entry.index).ToArray();
					string[] missingTypes = requestedTypes
						.Where(typeName => !sourceDocument.TypeNames.Contains(typeName, StringComparer.Ordinal))
						.OrderBy(typeName => typeName, StringComparer.Ordinal)
						.ToArray();
					if (missingTypes.Length != 0)
					{
						throw new InvalidDataException("Source BPHCL does not define type(s): " + string.Join(", ", missingTypes));
					}
					byte[] replacement = NativeBphclGameReadyTypeTable
						.CreateEmbeddedCatalogUnion(sourceDocument, requiredSourceTypes)
						.ReplacementSection
						?? throw new InvalidDataException("The embedded game-ready TYPE catalog already resolves every requested type.");
					File.WriteAllBytes(outputPath, replacement);
					Console.WriteLine($"gameReadyTypeCatalog|types={requestedTypes.Count}|bytes={replacement.Length}|output={outputPath}");
					return;
				}
				if (args.Length == 4 && string.Equals(args[0], "--extend-embedded-bphcl-type-catalog", StringComparison.OrdinalIgnoreCase))
				{
					NativeBphclDocument nativeBphclDocument = NativeBphclDocument.Open(args[1]);
					string typeName = args[2];
					uint[] requiredSourceTypes = (from entry in nativeBphclDocument.TypeNames.Select((string item11, int item12) => (name: item11, index: item12))
						where entry.index > 0 && string.Equals(entry.name, typeName, StringComparison.Ordinal)
						select (uint)entry.index).ToArray();
					if (requiredSourceTypes.Length == 0)
					{
						throw new InvalidDataException("Source BPHCL does not define type '" + typeName + "'.");
					}
					byte[] replacement = NativeBphclTypeTable.CreateEmbeddedCatalogUnion(nativeBphclDocument, requiredSourceTypes).ReplacementSection ?? throw new InvalidDataException("The embedded TYPE catalog already resolves '" + typeName + "'.");
					File.WriteAllBytes(args[3], replacement);
					Console.WriteLine($"typeCatalog|type={typeName}|bytes={replacement.Length}|output={args[3]}");
					return;
				}
				if (args.Length == 5 && string.Equals(args[0], "--extend-bphcl-type-catalog", StringComparison.OrdinalIgnoreCase))
				{
					NativeBphclDocument target4 = NativeBphclDocument.Open(args[1]);
					NativeBphclDocument source4 = NativeBphclDocument.Open(args[2]);
					string typeName2 = args[3];
					uint[] requiredSourceTypes2 = (from entry in source4.TypeNames.Select((string item11, int item12) => (name: item11, index: item12))
						where entry.index > 0 && string.Equals(entry.name, typeName2, StringComparison.Ordinal)
						select (uint)entry.index).ToArray();
					if (requiredSourceTypes2.Length == 0)
					{
						throw new InvalidDataException("Source BPHCL does not define type '" + typeName2 + "'.");
					}
					byte[] replacement2 = NativeBphclTypeTable.Create(target4, source4, requiredSourceTypes2).ReplacementSection ?? throw new InvalidDataException("The target TYPE catalog already resolves '" + typeName2 + "'.");
					File.WriteAllBytes(args[4], replacement2);
					Console.WriteLine($"typeCatalog|type={typeName2}|bytes={replacement2.Length}|output={args[4]}");
					return;
				}
				if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-reflection", StringComparison.OrdinalIgnoreCase))
				{
					NativeBphclTypeLayout layout = NativeBphclReflectionCatalog.GetTypeLayout(args[1]);
					Console.WriteLine($"type|{layout.TypeIndex}|{layout.Name}|size=0x{layout.Size:X}");
					{
						foreach (NativeBphclTypeLayoutMember member in layout.Members.OrderBy((NativeBphclTypeLayoutMember nativeBphclTypeLayoutMember) => nativeBphclTypeLayoutMember.Offset))
						{
							Console.WriteLine($"member|0x{member.Offset:X2}|{member.Name}|{member.TypeIndex}:{member.TypeName}");
						}
						return;
					}
				}
				if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-file-type", StringComparison.OrdinalIgnoreCase))
				{
					NativeBphclDocument document5 = NativeBphclDocument.Open(args[1]);
					string typeName3 = args[2];
					NativeBphclTypeDefinition[] definitions = (from nativeBphclTypeDefinition in document5.TypeDefinitions
						where string.Equals(document5.GetTypeName(nativeBphclTypeDefinition.TypeIndex), typeName3, StringComparison.Ordinal)
						orderby nativeBphclTypeDefinition.TypeIndex
						select nativeBphclTypeDefinition).ToArray();
					Console.WriteLine($"fileType|name={typeName3}|definitions={definitions.Length}");
					NativeBphclTypeDefinition[] array3 = definitions;
					foreach (NativeBphclTypeDefinition definition in array3)
					{
						Console.WriteLine($"type|{definition.TypeIndex}|{typeName3}|parent={definition.ParentTypeIndex}:{document5.GetTypeName(definition.ParentTypeIndex)}|flags=0x{definition.Flags:X}|size=0x{definition.Size:X}|align={definition.Alignment}");
						foreach (NativeBphclTypeMember member2 in definition.Members.OrderBy((NativeBphclTypeMember nativeBphclTypeMember) => nativeBphclTypeMember.Offset))
						{
							Console.WriteLine($"member|0x{member2.Offset:X2}|{member2.Name}|flags=0x{member2.Flags:X}|{member2.TypeIndex}:{document5.GetTypeName(member2.TypeIndex)}");
						}
					}
					return;
				}
				if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-named-types", StringComparison.OrdinalIgnoreCase))
				{
					IReadOnlyList<string> lines = NativeBphclTypeTable.DescribeNamedTypes(NativeBphclDocument.Open(args[1]), args[2]);
					Console.WriteLine($"namedTypes|name={args[2]}|records={lines.Count}");
					{
						foreach (string item12 in lines)
						{
							Console.WriteLine(item12);
						}
						return;
					}
				}
				if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-type-index", StringComparison.OrdinalIgnoreCase))
				{
					NativeBphclDocument document6 = NativeBphclDocument.Open(args[1]);
					uint typeIndex = uint.Parse(args[2], CultureInfo.InvariantCulture);
					{
						foreach (string item13 in NativeBphclTypeTable.DescribeTypeIndex(document6, typeIndex))
						{
							Console.WriteLine(item13);
						}
						return;
					}
				}
				if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-type-key", StringComparison.OrdinalIgnoreCase))
				{
					IReadOnlyList<string> lines2 = NativeBphclTypeTable.DescribeTypeKeys(NativeBphclDocument.Open(args[1]), args[2]);
					Console.WriteLine($"typeKeys|name={args[2]}|records={lines2.Count}");
					{
						foreach (string item14 in lines2)
						{
							Console.WriteLine(item14);
						}
						return;
					}
				}
				if (args.Length == 3 && string.Equals(args[0], "--survey-bphcl-fields", StringComparison.OrdinalIgnoreCase))
				{
					BphclCorpusFieldSurvey.WriteFromManifest(args[1], args[2]);
					Console.WriteLine(args[2]);
					return;
				}
				if ((args.Length == 4 || args.Length == 5) && string.Equals(args[0], "--match-bphcl-native-family", StringComparison.OrdinalIgnoreCase))
				{
					int count2 = ((args.Length == 5) ? int.Parse(args[4], CultureInfo.InvariantCulture) : 12);
					BphclNativeFamilyMatcher.Write(args[1], args[2], args[3], count2);
					return;
				}
			}
			if (args.Length == 4 && string.Equals(args[0], "--dump-bphcl-closure-members", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document7 = NativeBphclDocument.Open(args[1]);
				int clothIndex2 = int.Parse(args[2], CultureInfo.InvariantCulture);
				string typeName4 = args[3];
				NativeBphclCloth cloth2 = document7.Cloths.ElementAtOrDefault(clothIndex2) ?? throw new ArgumentOutOfRangeException("clothIndex");
				NativeBphclSkeleton skeleton2 = document7.Skeletons.ElementAtOrDefault(clothIndex2) ?? throw new InvalidDataException("The cloth has no paired skeleton.");
				NativeBphclTypeLayout layout2 = NativeBphclReflectionCatalog.GetTypeLayout(typeName4);
				NativeBphclTypeLayoutMember[] members = layout2.Members.OrderBy((NativeBphclTypeLayoutMember nativeBphclTypeLayoutMember) => nativeBphclTypeLayoutMember.Offset).ToArray();
				NativeBphclSection data4 = document7.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
				{
					foreach (int itemIndex in from index9 in document7.CollectItemClosure(new int[2] { cloth2.ItemIndex, skeleton2.ItemIndex })
						where string.Equals(document7.GetTypeName(document7.GetItem(index9).TypeIndex), typeName4, StringComparison.Ordinal)
						orderby document7.GetItem(index9).DataOffset
						select index9)
					{
						NativeBphclItem item = document7.GetItem(itemIndex);
						for (uint element = 0u; element < item.Count; element++)
						{
							Console.WriteLine($"object|item={itemIndex}|element={element}|data=0x{item.DataOffset + element * layout2.Size:X}");
							for (int memberIndex = 0; memberIndex < members.Length; memberIndex++)
							{
								NativeBphclTypeLayoutMember member3 = members[memberIndex];
								int length;
								checked
								{
									length = (int)(unchecked((memberIndex + 1 < members.Length) ? members[memberIndex + 1].Offset : layout2.Size) - member3.Offset);
								}
								uint fieldOffset = item.DataOffset + element * layout2.Size + member3.Offset;
								int start = data4.PayloadOffset + checked((int)fieldOffset);
								string bytes = Convert.ToHexString(document7.Bytes.AsSpan(start, length));
								string reference = (document7.TryGetReferencedItem(fieldOffset, out var targetItem) ? $"->ITEM {targetItem}:{document7.GetTypeName(document7.GetItem(targetItem).TypeIndex)}" : string.Empty);
								Console.WriteLine($"member|0x{member3.Offset:X2}|{member3.Name}|{member3.TypeName}|{bytes}{reference}");
							}
						}
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-buffer-layouts", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document8 = NativeBphclDocument.Open(args[1]);
				NativeBphclSection data5 = document8.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
				{
					foreach (NativeBphclCloth cloth3 in document8.Cloths)
					{
						foreach (NativeBphclBufferDefinition buffer in cloth3.BufferDefinitions)
						{
							int start2 = data5.PayloadOffset + checked((int)(buffer.DataOffset + 56));
							string bytes2 = Convert.ToHexString(document8.Bytes.AsSpan(start2, 32));
							Console.WriteLine($"bufferLayout|cloth={cloth3.Index}:{cloth3.Name}|buffer={buffer.Index}:{buffer.ClassName}:{buffer.BufferName}|type={buffer.Type}|vertices={buffer.VertexCount}|triangles={buffer.TriangleCount}|{bytes2}");
						}
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--find-bphcl-bone-space-skins", StringComparison.OrdinalIgnoreCase))
			{
				foreach (string path in Directory.EnumerateFiles(args[1], "*.bphcl", SearchOption.AllDirectories).OrderBy<string, string>((string result9) => result9, StringComparer.OrdinalIgnoreCase))
				{
					foreach (NativeBphclCloth cloth4 in NativeBphclDocument.Open(path).Cloths)
					{
						if (cloth4.Operators.Any((NativeBphclOperatorLayout nativeBphclOperatorLayout) => string.Equals(nativeBphclOperatorLayout.ClassName, "hclBoneSpaceSkinPOperator", StringComparison.Ordinal)))
						{
							Console.WriteLine($"{path}|{cloth4.Index}|{cloth4.Name}");
						}
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-roots", StringComparison.OrdinalIgnoreCase))
			{
				foreach (NativeBphclNamedVariant variant in NativeBphclDocument.Open(args[1]).RootVariants)
				{
					Console.WriteLine($"root|{variant.Index}|name={variant.Name}|class={variant.ClassName}|item={variant.ObjectItemIndex}");
				}
				return;
			}
			NativeBphclDocument document9;
			int payloadOffset;
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-resource-container", StringComparison.OrdinalIgnoreCase))
			{
				document9 = NativeBphclDocument.Open(args[1]);
				payloadOffset = (document9.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.")).PayloadOffset;
				{
					foreach (NativeBphclNamedVariant variant2 in document9.RootVariants.Where((NativeBphclNamedVariant nativeBphclNamedVariant) => string.Equals(nativeBphclNamedVariant.ClassName, "hkMemoryResourceContainer", StringComparison.Ordinal)))
					{
						NativeBphclTypeLayout typeLayout = NativeBphclReflectionCatalog.GetTypeLayout(variant2.ClassName);
						Console.WriteLine($"resource|root={variant2.Index}|item={variant2.ObjectItemIndex}|data=0x{variant2.ObjectDataOffset:X}|size=0x{document9.GetItem(variant2.ObjectItemIndex).Count:X}");
						foreach (NativeBphclTypeLayoutMember member4 in typeLayout.Members)
						{
							uint fieldOffset2 = variant2.ObjectDataOffset + member4.Offset;
							int targetItemIndex;
							if (member4.TypeName == "hkArray")
							{
								uint declaredCount = ReadUInt(fieldOffset2 + 8);
								uint capacityAndFlags = ReadUInt(fieldOffset2 + 12);
								if (!document9.TryGetReferencedItem(fieldOffset2, out var storageItemIndex))
								{
									Console.WriteLine($"member|{member4.Name}|type=hkArray|storage=<null>|count={declaredCount}|capacity=0x{capacityAndFlags:X8}");
									continue;
								}
								NativeBphclItem storage = document9.GetItem(storageItemIndex);
								Console.WriteLine($"member|{member4.Name}|type=hkArray|storage={storageItemIndex}|actual={storage.TypeIndex}:{document9.GetTypeName(storage.TypeIndex)}|elements={storage.Count}|count={declaredCount}|capacity=0x{capacityAndFlags:X8}");
							}
							else if (document9.TryGetReferencedItem(fieldOffset2, out targetItemIndex))
							{
								NativeBphclItem target5 = document9.GetItem(targetItemIndex);
								Console.WriteLine($"member|{member4.Name}|type={member4.TypeName}|target={targetItemIndex}|actual={target5.TypeIndex}:{document9.GetTypeName(target5.TypeIndex)}|elements={target5.Count}");
							}
							else
							{
								Console.WriteLine($"member|{member4.Name}|type={member4.TypeName}|raw=0x{ReadUInt(fieldOffset2):X8}");
							}
						}
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--dump-bphcl-strings", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document10 = NativeBphclDocument.Open(args[1]);
				NativeBphclSection data6 = document10.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
				{
					foreach (uint offset in (document10.InternalPatches.FirstOrDefault((NativeBphclPatch nativeBphclPatch) => string.Equals(document10.GetTypeName(nativeBphclPatch.TypeIndex), "hkStringPtr", StringComparison.Ordinal)) ?? throw new InvalidDataException("BPHCL has no hkStringPtr patch group.")).Offsets.OrderBy((uint result9) => result9))
					{
						int itemIndex2 = checked((int)BitConverter.ToUInt32(document10.Bytes, data6.PayloadOffset + (int)offset));
						NativeBphclItem item2 = document10.GetItem(itemIndex2);
						int start3 = data6.PayloadOffset + checked((int)item2.DataOffset);
						int end = start3;
						for (int limit = Math.Min(document10.Bytes.Length, checked(start3 + (int)item2.Count)); end < limit && document10.Bytes[end] != 0; end++)
						{
						}
						Console.WriteLine($"string|field=0x{offset:X}|item={itemIndex2}|{Encoding.UTF8.GetString(document10.Bytes, start3, end - start3)}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-item-counts", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document11 = NativeBphclDocument.Open(args[1]);
				{
					foreach (var group in from nativeBphclItem in document11.Items
						group nativeBphclItem by new
						{
							TypeIndex = nativeBphclItem.TypeIndex,
							Name = (document11.GetTypeName(nativeBphclItem.TypeIndex) ?? "<unknown>")
						} into grouping
						orderby grouping.Key.TypeIndex
						select grouping)
					{
						Console.WriteLine($"type|{group.Key.TypeIndex}:{group.Key.Name}|items={group.Count()}|elements={group.Sum((NativeBphclItem nativeBphclItem) => nativeBphclItem.Count)}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--audit-bphcl-unreferenced", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document12 = NativeBphclDocument.Open(args[1]);
				HashSet<int> reachable = document12.CollectItemClosure(document12.RootVariants.Select((NativeBphclNamedVariant nativeBphclNamedVariant) => nativeBphclNamedVariant.ObjectItemIndex)).ToHashSet();
				int[] unreferenced = (from item11 in Enumerable.Range(0, document12.Items.Count)
					where !reachable.Contains(item11)
					select item11).ToArray();
				Console.WriteLine($"unreferenced|items={unreferenced.Length}|total={document12.Items.Count}");
				{
					foreach (var group2 in from index9 in unreferenced
						group index9 by new
						{
							Item = document12.GetItem(index9),
							Name = (document12.GetTypeName(document12.GetItem(index9).TypeIndex) ?? "<unknown>")
						} into grouping
						orderby grouping.Key.Item.TypeIndex, grouping.Key.Item.Flags, grouping.Key.Item.Count
						select grouping)
					{
						Console.WriteLine($"type|{group2.Key.Item.TypeIndex}:{group2.Key.Name}|flags=0x{group2.Key.Item.Flags:X8}|count={group2.Key.Item.Count}|items={group2.Count()}|indexes={string.Join(',', group2.Select((int result9) => result9))}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-items", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document13 = NativeBphclDocument.Open(args[1]);
				{
					foreach (var (item3, index2) in document13.Items.Take(128).Select((NativeBphclItem item11, int item12) => (item: item11, index: item12)))
					{
						Console.WriteLine($"item|{index2}|flags=0x{item3.Flags:X8}|type={item3.TypeIndex}:{document13.GetTypeName(item3.TypeIndex)}|data=0x{item3.DataOffset:X}|count={item3.Count}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--compare-bphcl-type-catalog", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document14 = NativeBphclDocument.Open(args[1]);
				int mismatches = 0;
				uint[] usedIndexes = (from result9 in document14.Items.Select((NativeBphclItem nativeBphclItem) => nativeBphclItem.TypeIndex).Concat(document14.InternalPatches.Select((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.TypeIndex)).Distinct()
					orderby result9
					select result9).ToArray();
				Console.WriteLine($"typeCatalog|documentTypes={document14.TypeNames.Count - 1}|usedIndexes={usedIndexes.Length}");
				uint[] array4 = usedIndexes;
				foreach (uint typeIndex2 in array4)
				{
					string nativeName = document14.GetTypeName(typeIndex2) ?? $"<missing:{typeIndex2}>";
					string embeddedName = NativeBphclReflectionCatalog.GetTypeName(typeIndex2);
					uint nativeSize = (document14.TypeDefinitions.FirstOrDefault((NativeBphclTypeDefinition nativeBphclTypeDefinition) => nativeBphclTypeDefinition.TypeIndex == typeIndex2)?.Size).GetValueOrDefault();
					uint embeddedSize = NativeBphclReflectionCatalog.GetTypeSize(typeIndex2);
					if (!string.Equals(nativeName, embeddedName, StringComparison.Ordinal) || nativeSize != embeddedSize)
					{
						mismatches++;
						Console.WriteLine($"mismatch|index={typeIndex2}|native={nativeName}|nativeSize=0x{nativeSize:X}|embedded={embeddedName}|embeddedSize=0x{embeddedSize:X}");
					}
				}
				array4 = usedIndexes;
				foreach (uint typeIndex3 in array4)
				{
					NativeBphclTypeDefinition native2 = document14.TypeDefinitions.FirstOrDefault((NativeBphclTypeDefinition nativeBphclTypeDefinition) => nativeBphclTypeDefinition.TypeIndex == typeIndex3);
					if ((object)native2 != null)
					{
						NativeBphclTypeLayout typeLayout2 = NativeBphclReflectionCatalog.GetTypeLayout(typeIndex3);
						string[] nativeMembers = native2.Members.Select((NativeBphclTypeMember nativeBphclTypeMember) => $"{nativeBphclTypeMember.Name}@0x{nativeBphclTypeMember.Offset:X}:{document14.GetTypeName(nativeBphclTypeMember.TypeIndex)}").OrderBy<string, string>((string result9) => result9, StringComparer.Ordinal).ToArray();
						string[] embeddedMembers = typeLayout2.Members.Select((NativeBphclTypeLayoutMember nativeBphclTypeLayoutMember) => $"{nativeBphclTypeLayoutMember.Name}@0x{nativeBphclTypeLayoutMember.Offset:X}:{nativeBphclTypeLayoutMember.TypeName}").OrderBy<string, string>((string result9) => result9, StringComparer.Ordinal).ToArray();
						if (!Enumerable.SequenceEqual<string>(nativeMembers, embeddedMembers, StringComparer.Ordinal))
						{
							mismatches++;
							Console.WriteLine($"layoutMismatch|index={typeIndex3}|type={document14.GetTypeName(typeIndex3)}|native=[{string.Join(',', nativeMembers)}]|embedded=[{string.Join(',', embeddedMembers)}]");
						}
					}
				}
				Console.WriteLine($"typeCatalog|mismatches={mismatches}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-root-closure", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document15 = NativeBphclDocument.Open(args[1]);
				int rootIndex = int.Parse(args[2], CultureInfo.InvariantCulture);
				NativeBphclNamedVariant root = document15.RootVariants.ElementAtOrDefault(rootIndex) ?? throw new ArgumentOutOfRangeException("rootIndex");
				IReadOnlyList<int> closure2 = document15.CollectItemClosure(new int[1] { root.ObjectItemIndex });
				Dictionary<int, (uint, uint)> ranges = document15.GetItemRanges();
				Dictionary<uint, uint> patchesByOffset = document15.InternalPatches.SelectMany((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.Offsets.Select((uint item11) => (offset: item11, TypeIndex: nativeBphclPatch.TypeIndex))).ToDictionary(((uint offset, uint TypeIndex) entry) => entry.offset, ((uint offset, uint TypeIndex) entry) => entry.TypeIndex);
				Console.WriteLine($"rootClosure|{root.Index}:{root.Name}:{root.ClassName}|items={closure2.Count}");
				{
					foreach (int itemIndex3 in closure2.OrderBy((int index9) => document15.GetItem(index9).DataOffset).ThenBy((int result9) => result9))
					{
						NativeBphclItem item4 = document15.GetItem(itemIndex3);
						(uint Start, uint End) range = ranges[itemIndex3];
						IEnumerable<string> pointerFields = (from entry in patchesByOffset
							where entry.Key >= range.Start && entry.Key < range.End
							orderby entry.Key
							select entry).Select(delegate(KeyValuePair<uint, uint> entry)
						{
							string value2 = (document15.TryGetReferencedItem(entry.Key, out var itemIndex6) ? itemIndex6.ToString(CultureInfo.InvariantCulture) : "?");
							return $"0x{entry.Key - item4.DataOffset:X}:{document15.GetTypeName(entry.Value)}->{value2}";
						});
						Console.WriteLine($"item|{itemIndex3}|{document15.GetTypeName(item4.TypeIndex)}|flags=0x{item4.Flags:X8}:{document15.GetTypeName(item4.Flags)}|data=0x{item4.DataOffset:X}|count={item4.Count}|range=0x{range.Start:X}-0x{range.End:X}|ptr=[{string.Join(',', pointerFields)}]");
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-closure", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document16 = NativeBphclDocument.Open(args[1]);
				int clothIndex3 = int.Parse(args[2], CultureInfo.InvariantCulture);
				NativeBphclCloth cloth5 = document16.Cloths.ElementAtOrDefault(clothIndex3) ?? throw new ArgumentOutOfRangeException("clothIndex");
				NativeBphclSkeleton skeleton3 = document16.Skeletons.ElementAtOrDefault(clothIndex3) ?? throw new InvalidDataException("The cloth has no paired skeleton.");
				IReadOnlyList<int> closure3 = document16.CollectItemClosure(new int[2] { cloth5.ItemIndex, skeleton3.ItemIndex });
				Console.WriteLine($"closure|cloth={clothIndex3}:{cloth5.Name}|items={closure3.Count}|patches={document16.GetPatchesForItems(closure3).Sum((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.Offsets.Count)}");
				{
					foreach (IGrouping<string, int> group3 in (from index9 in closure3
						group index9 by document16.GetTypeName(document16.GetItem(index9).TypeIndex) ?? $"type_{document16.GetItem(index9).TypeIndex}").OrderBy<IGrouping<string, int>, string>((IGrouping<string, int> grouping) => grouping.Key, StringComparer.Ordinal))
					{
						Console.WriteLine($"  {group3.Key}|items={group3.Count()}|elements={group3.Sum((int index9) => document16.GetItem(index9).Count)}|pointers={group3.Sum((int num2) => document16.GetPatchesForItems(new int[1] { num2 }).Sum((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.Offsets.Count))}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-closures", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document17 = NativeBphclDocument.Open(args[1]);
				Dictionary<int, (uint Start, uint End)> ranges2 = document17.GetItemRanges();
				Console.WriteLine($"closures|cloths={document17.Cloths.Count}|items={document17.Items.Count}|patches={document17.InternalPatches.Sum((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.Offsets.Count)}");
				for (int clothIndex4 = 0; clothIndex4 < document17.Cloths.Count; clothIndex4++)
				{
					NativeBphclCloth cloth6 = document17.Cloths[clothIndex4];
					NativeBphclSkeleton skeleton4 = document17.Skeletons.ElementAtOrDefault(clothIndex4) ?? throw new InvalidDataException("The cloth has no paired skeleton.");
					IReadOnlyList<int> closure4 = document17.CollectItemClosure(new int[2] { cloth6.ItemIndex, skeleton4.ItemIndex });
					int[] orderedIndexes = closure4.OrderBy((int result9) => result9).ToArray();
					int minItem = ((orderedIndexes.Length == 0) ? (-1) : orderedIndexes[0]);
					int maxItem = ((orderedIndexes.Length == 0) ? (-1) : orderedIndexes[^1]);
					int itemSpan = ((orderedIndexes.Length != 0) ? (maxItem - minItem + 1) : 0);
					uint dataStart = ((orderedIndexes.Length != 0) ? orderedIndexes.Min((int key3) => ranges2[key3].Start) : 0u);
					uint dataEnd = ((orderedIndexes.Length != 0) ? orderedIndexes.Max((int key3) => ranges2[key3].End) : 0u);
					int patchCount = document17.GetPatchesForItems(closure4).Sum((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.Offsets.Count);
					string typeSignature = string.Join(",", from grouping in (from index9 in closure4
							group index9 by document17.GetTypeName(document17.GetItem(index9).TypeIndex) ?? $"type_{document17.GetItem(index9).TypeIndex}").OrderBy<IGrouping<string, int>, string>((IGrouping<string, int> grouping) => grouping.Key, StringComparer.Ordinal)
						select $"{grouping.Key}:{grouping.Count()}:{grouping.Sum((int index9) => document17.GetItem(index9).Count)}");
					Console.WriteLine($"closureSummary|{clothIndex4}|{cloth6.Name}|items={closure4.Count}|patches={patchCount}|itemRange={minItem}-{maxItem}|span={itemSpan}|holes={itemSpan - closure4.Count}|data=0x{dataStart:X}-0x{dataEnd:X}|types={typeSignature}");
				}
				return;
			}
			if (args.Length == 5 && string.Equals(args[0], "--compare-bphcl-closure-bytes", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument native3 = NativeBphclDocument.Open(args[1]);
				int nativeClothIndex = int.Parse(args[2], CultureInfo.InvariantCulture);
				NativeBphclDocument generated = NativeBphclDocument.Open(args[3]);
				int generatedClothIndex = int.Parse(args[4], CultureInfo.InvariantCulture);
				NativeBphclCloth nativeCloth = native3.Cloths.ElementAtOrDefault(nativeClothIndex) ?? throw new ArgumentOutOfRangeException("nativeClothIndex");
				NativeBphclCloth generatedCloth = generated.Cloths.ElementAtOrDefault(generatedClothIndex) ?? throw new ArgumentOutOfRangeException("generatedClothIndex");
				NativeBphclSkeleton nativeSkeleton = native3.Skeletons.ElementAtOrDefault(nativeClothIndex) ?? throw new InvalidDataException("Native cloth has no paired skeleton.");
				NativeBphclSkeleton generatedSkeleton = generated.Skeletons.ElementAtOrDefault(generatedClothIndex) ?? throw new InvalidDataException("Generated cloth has no paired skeleton.");
				native3.GetItemRanges();
				generated.GetItemRanges();
				HashSet<uint> nativePointers = PointerBytes(native3);
				HashSet<uint> generatedPointers = PointerBytes(generated);
				Dictionary<string, (int, NativeBphclItem)[]> nativeGroups = (from num2 in native3.CollectItemClosure(new int[2] { nativeCloth.ItemIndex, nativeSkeleton.ItemIndex })
					select (index: num2, item: native3.GetItem(num2)) into entry
					where !string.Equals(native3.GetTypeName(entry.item.TypeIndex), "char", StringComparison.Ordinal)
					select entry).GroupBy<(int, NativeBphclItem), string>(((int index, NativeBphclItem item) entry) => ItemKey(native3, entry.item), StringComparer.Ordinal).ToDictionary<IGrouping<string, (int, NativeBphclItem)>, string, (int, NativeBphclItem)[]>((IGrouping<string, (int index, NativeBphclItem item)> grouping) => grouping.Key, (IGrouping<string, (int index, NativeBphclItem item)> source9) => source9.OrderBy(((int index, NativeBphclItem item) entry) => entry.item.DataOffset).ToArray(), StringComparer.Ordinal);
				Dictionary<string, (int, NativeBphclItem)[]> generatedGroups = (from num2 in generated.CollectItemClosure(new int[2] { generatedCloth.ItemIndex, generatedSkeleton.ItemIndex })
					select (index: num2, item: generated.GetItem(num2)) into entry
					where !string.Equals(generated.GetTypeName(entry.item.TypeIndex), "char", StringComparison.Ordinal)
					select entry).GroupBy<(int, NativeBphclItem), string>(((int index, NativeBphclItem item) entry) => ItemKey(generated, entry.item), StringComparer.Ordinal).ToDictionary<IGrouping<string, (int, NativeBphclItem)>, string, (int, NativeBphclItem)[]>((IGrouping<string, (int index, NativeBphclItem item)> grouping) => grouping.Key, (IGrouping<string, (int index, NativeBphclItem item)> source9) => source9.OrderBy(((int index, NativeBphclItem item) entry) => entry.item.DataOffset).ToArray(), StringComparer.Ordinal);
				Console.WriteLine("compare|native=" + nativeCloth.Name + "|generated=" + generatedCloth.Name);
				{
					foreach (string key in nativeGroups.Keys.Union<string>(generatedGroups.Keys, StringComparer.Ordinal).OrderBy<string, string>((string result9) => result9, StringComparer.Ordinal))
					{
						nativeGroups.TryGetValue(key, out var nativeItems);
						generatedGroups.TryGetValue(key, out var generatedItems);
						if (nativeItems == null)
						{
							nativeItems = Array.Empty<(int, NativeBphclItem)>();
						}
						if (generatedItems == null)
						{
							generatedItems = Array.Empty<(int, NativeBphclItem)>();
						}
						if (nativeItems.Length != generatedItems.Length)
						{
							Console.WriteLine($"count|{key}|native={nativeItems.Length}|generated={generatedItems.Length}");
							continue;
						}
						for (int ordinal = 0; ordinal < nativeItems.Length; ordinal++)
						{
							NativeBphclItem nativeItem = nativeItems[ordinal].Item2;
							NativeBphclItem generatedItem = generatedItems[ordinal].Item2;
							uint nativeElementSize = NativeBphclReflectionCatalog.GetTypeSize(nativeItem.TypeIndex);
							uint generatedElementSize = NativeBphclReflectionCatalog.GetTypeSize(generatedItem.TypeIndex);
							int nativeLength;
							int generatedLength;
							int comparedLength;
							List<string> differences;
							checked
							{
								nativeLength = (int)(nativeItem.Count * nativeElementSize);
								generatedLength = (int)(generatedItem.Count * generatedElementSize);
								comparedLength = Math.Min(nativeLength, generatedLength);
								differences = new List<string>();
							}
							for (int offset2 = 0; offset2 < comparedLength; offset2++)
							{
								uint nativeOffset = nativeItem.DataOffset + (uint)offset2;
								uint generatedOffset = generatedItem.DataOffset + (uint)offset2;
								if (nativePointers.Contains(nativeOffset) || generatedPointers.Contains(generatedOffset))
								{
									continue;
								}
								byte nativeByte = native3.Bytes[native3.DataSection.PayloadOffset + checked((int)nativeOffset)];
								byte generatedByte = generated.Bytes[generated.DataSection.PayloadOffset + checked((int)generatedOffset)];
								if (nativeByte != generatedByte)
								{
									differences.Add($"0x{offset2:X}:{nativeByte:X2}/{generatedByte:X2}");
									if (differences.Count == 12)
									{
										break;
									}
								}
							}
							if (nativeLength != generatedLength || differences.Count > 0)
							{
								Console.WriteLine($"diff|{key}|ordinal={ordinal}|bytes={nativeLength}/{generatedLength}|{string.Join(',', differences)}");
							}
						}
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-closure-layout", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document18 = NativeBphclDocument.Open(args[1]);
				int clothIndex5 = int.Parse(args[2], CultureInfo.InvariantCulture);
				NativeBphclCloth cloth7 = document18.Cloths.ElementAtOrDefault(clothIndex5) ?? throw new ArgumentOutOfRangeException("clothIndex");
				NativeBphclSkeleton skeleton5 = document18.Skeletons.ElementAtOrDefault(clothIndex5) ?? throw new InvalidDataException("The cloth has no paired skeleton.");
				IReadOnlyList<int> closure5 = document18.CollectItemClosure(new int[2] { cloth7.ItemIndex, skeleton5.ItemIndex });
				Dictionary<int, (uint, uint)> ranges3 = document18.GetItemRanges();
				Dictionary<uint, uint> patchesByOffset2 = document18.InternalPatches.SelectMany((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.Offsets.Select((uint item11) => (offset: item11, TypeIndex: nativeBphclPatch.TypeIndex))).ToDictionary(((uint offset, uint TypeIndex) entry) => entry.offset, ((uint offset, uint TypeIndex) entry) => entry.TypeIndex);
				Console.WriteLine($"layout|cloth={clothIndex5}:{cloth7.Name}|items={closure5.Count}");
				{
					foreach (int itemIndex4 in closure5.OrderBy((int index9) => document18.GetItem(index9).DataOffset).ThenBy((int result9) => result9))
					{
						NativeBphclItem item5 = document18.GetItem(itemIndex4);
						(uint Start, uint End) range2 = ranges3[itemIndex4];
						IEnumerable<string> pointerFields2 = (from entry in patchesByOffset2
							where entry.Key >= range2.Start && entry.Key < range2.End
							orderby entry.Key
							select entry).Select(delegate(KeyValuePair<uint, uint> entry)
						{
							string value2 = (document18.TryGetReferencedItem(entry.Key, out var itemIndex6) ? itemIndex6.ToString(CultureInfo.InvariantCulture) : "?");
							return $"0x{entry.Key - item5.DataOffset:X}:{document18.GetTypeName(entry.Value)}->{value2}";
						});
						Console.WriteLine($"item|{itemIndex4}|{document18.GetTypeName(item5.TypeIndex)}|flags=0x{item5.Flags:X8}:{document18.GetTypeName(item5.Flags)}|data=0x{item5.DataOffset:X}|count={item5.Count}|range=0x{range2.Start:X}-0x{range2.End:X}|ptr=[{string.Join(',', pointerFields2)}]");
					}
					return;
				}
			}
			NativeBphclDocument document19;
			NativeBphclSection dataSection2;
			if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-operator-access", StringComparison.OrdinalIgnoreCase))
			{
				document19 = NativeBphclDocument.Open(args[1]);
				int clothIndex6 = int.Parse(args[2], CultureInfo.InvariantCulture);
				NativeBphclCloth cloth8 = document19.Cloths.ElementAtOrDefault(clothIndex6) ?? throw new ArgumentOutOfRangeException("clothIndex");
				dataSection2 = document19.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
				Console.WriteLine($"operators|cloth={clothIndex6}:{cloth8.Name}");
				{
					foreach (NativeBphclOperatorLayout op in cloth8.Operators)
					{
						NativeBphclItem item6 = document19.GetItem(op.ItemIndex);
						Console.WriteLine($"op|{op.Index}|{op.ClassName}|item={op.ItemIndex}|id={ReadUInt32(item6.DataOffset + 32)}|type={ReadUInt32(item6.DataOffset + 36)}|name={ReadString(item6.DataOffset + 24)}|buffers={DescribeBuffers(item6.DataOffset + 40)}|transforms={DescribeTransformSets(item6.DataOffset + 56)}");
					}
					return;
				}
			}
			NativeBphclDocument document20;
			NativeBphclSection data7;
			if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-operator-trackers", StringComparison.OrdinalIgnoreCase))
			{
				document20 = NativeBphclDocument.Open(args[1]);
				int clothIndex7 = int.Parse(args[2], CultureInfo.InvariantCulture);
				NativeBphclCloth? obj = document20.Cloths.ElementAtOrDefault(clothIndex7) ?? throw new ArgumentOutOfRangeException("clothIndex");
				data7 = document20.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
				{
					foreach (NativeBphclOperatorLayout @operator in obj.Operators)
					{
						uint accessesOffset = document20.GetItem(@operator.ItemIndex).DataOffset + 56;
						if (!document20.TryGetReferencedItem(accessesOffset, out var accessItemIndex))
						{
							Console.WriteLine($"op={@operator.Index}:{@operator.ClassName}|transforms=[]");
							continue;
						}
						NativeBphclItem accesses = document20.GetItem(accessItemIndex);
						for (uint accessIndex = 0u; accessIndex < accesses.Count; accessIndex++)
						{
							uint accessOffset = accesses.DataOffset + accessIndex * 32;
							uint fieldCount = ReadUInt33(accessOffset + 24);
							if (!document20.TryGetReferencedItem(accessOffset + 16, out var trackerItemIndex))
							{
								Console.WriteLine($"op={@operator.Index}:{@operator.ClassName}|set={ReadUInt33(accessOffset)}|fieldCount={fieldCount}|trackers=<null>");
								continue;
							}
							NativeBphclItem trackers = document20.GetItem(trackerItemIndex);
							IEnumerable<string> values = Enumerable.Range(0, checked((int)trackers.Count)).Select(delegate(int num3)
							{
								uint num2 = trackers.DataOffset + checked((uint)(num3 * 72));
								return $"r{DescribeBitField(num2)};b{DescribeBitField(num2 + 24)};w{DescribeBitField(num2 + 48)}";
							});
							Console.WriteLine($"op={@operator.Index}:{@operator.ClassName}|set={ReadUInt33(accessOffset)}|fieldCount={fieldCount}|storageCount={trackers.Count}|trackers=[{string.Join("|", values)}]");
						}
					}
					return;
				}
			}
			if (args.Length == 4 && string.Equals(args[0], "--append-bphcl-cloths", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument target6 = NativeBphclDocument.Open(args[1]);
				NativeBphclDocument source5 = NativeBphclDocument.Open(args[2]);
				NativeBphclDocument merged = target6;
				for (int clothIndex8 = 0; clothIndex8 < source5.Cloths.Count; clothIndex8++)
				{
					merged = NativeBphclDocument.Parse(NativeBphclMerge.MergeCompleteCloth(merged, source5, clothIndex8));
				}
				File.WriteAllBytes(args[3], merged.Bytes);
				Console.WriteLine($"BPHCL-APPEND|baseCloths={target6.Cloths.Count}|importedCloths={source5.Cloths.Count}|totalCloths={merged.Cloths.Count}|output={args[3]}");
				return;
			}
			if (args.Length == 6 && string.Equals(args[0], "--replace-bphcl-cloth", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument target7 = NativeBphclDocument.Open(args[1]);
				NativeBphclDocument source6 = NativeBphclDocument.Open(args[2]);
				int targetClothIndex = int.Parse(args[4], CultureInfo.InvariantCulture);
				int sourceClothIndex = int.Parse(args[5], CultureInfo.InvariantCulture);
				NativeBphclWriter.SaveReplacedCloth(target7, source6, targetClothIndex, sourceClothIndex, args[3]);
				NativeBphclDocument result2 = NativeBphclDocument.Open(args[3]);
				Console.WriteLine($"BPHCL-REPLACE|target={targetClothIndex}|source={sourceClothIndex}|cloths={result2.Cloths.Count}|output={args[3]}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--compare-bphcl-closure-topology", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument native4 = NativeBphclDocument.Open(args[1]);
				NativeBphclDocument fresh = NativeBphclDocument.Open(args[2]);
				Dictionary<string, NativeBphclCloth> freshByName = fresh.Cloths.GroupBy<NativeBphclCloth, string>((NativeBphclCloth nativeBphclCloth) => Key2(nativeBphclCloth.Name), StringComparer.Ordinal).ToDictionary<IGrouping<string, NativeBphclCloth>, string, NativeBphclCloth>((IGrouping<string, NativeBphclCloth> grouping) => grouping.Key, (IGrouping<string, NativeBphclCloth> source9) => source9.Single(), StringComparer.Ordinal);
				foreach (NativeBphclCloth nativeCloth2 in native4.Cloths)
				{
					if (!freshByName.TryGetValue(Key2(nativeCloth2.Name), out var freshCloth))
					{
						Console.WriteLine("missing|cloth=" + nativeCloth2.Name);
						continue;
					}
					string nativeTopology = Describe(native4, nativeCloth2);
					string freshTopology = Describe(fresh, freshCloth);
					Console.WriteLine(string.Equals(nativeTopology, freshTopology, StringComparison.Ordinal) ? ("same|cloth=" + nativeCloth2.Name) : $"diff|cloth={nativeCloth2.Name}|native={nativeTopology}|fresh={freshTopology}");
				}
				{
					foreach (NativeBphclCloth freshCloth2 in fresh.Cloths.Where((NativeBphclCloth nativeBphclCloth) => !native4.Cloths.Any((NativeBphclCloth nativeBphclCloth2) => string.Equals(Key2(nativeBphclCloth2.Name), Key2(nativeBphclCloth.Name), StringComparison.Ordinal))))
					{
						Console.WriteLine("extra|cloth=" + freshCloth2.Name);
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--compare-bphcl-fresh-runtime", StringComparison.OrdinalIgnoreCase))
			{
				CompareBphclFreshRuntime(NativeBphclDocument.Open(args[1]), NativeBphclDocument.Open(args[2]));
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--audit-bphcl-fidelity", StringComparison.OrdinalIgnoreCase))
			{
				BphclFidelityAudit.Write(args[1], args[2]);
				return;
			}
			if (args.Length == 4 && string.Equals(args[0], "--diff-bphcl-cloth", StringComparison.OrdinalIgnoreCase))
			{
				CompareBphclClothSemantics(NativeBphclDocument.Open(args[1]), NativeBphclDocument.Open(args[2]), args[3]);
				return;
			}
			if (args.Length == 5 && string.Equals(args[0], "--compare-bphcl-reference-topology", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument native5 = NativeBphclDocument.Open(args[1]);
				int nativeClothIndex2 = int.Parse(args[2], CultureInfo.InvariantCulture);
				NativeBphclDocument rebuilt5 = NativeBphclDocument.Open(args[3]);
				int rebuiltClothIndex = int.Parse(args[4], CultureInfo.InvariantCulture);
				CompareBphclReferenceTopology(native5, nativeClothIndex2, rebuilt5, rebuiltClothIndex);
				return;
			}
			if (args.Length == 4 && string.Equals(args[0], "--compare-bphcl-root-topology", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument native6 = NativeBphclDocument.Open(args[1]);
				NativeBphclDocument rebuilt6 = NativeBphclDocument.Open(args[2]);
				int rootIndex2 = int.Parse(args[3], CultureInfo.InvariantCulture);
				CompareBphclRootReferenceTopology(native6, rebuilt6, rootIndex2);
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--inspect-skin-layout", StringComparison.OrdinalIgnoreCase))
			{
				InspectSkinLayout(args[1], args[2]);
				return;
			}
			if (args.Length == 5 && string.Equals(args[0], "--compare-skin-position-slots", StringComparison.OrdinalIgnoreCase))
			{
				CompareSkinPositionSlots(args[1], args[2], args[3], args[4]);
				return;
			}

			if (args.Length == 3 && string.Equals(args[0], "--export-hkcl-bphcl", StringComparison.OrdinalIgnoreCase))
			{
				HkclService hkclService = new HkclService();
				hkclService.Load(args[1]);
				StandaloneBphclDocumentGraph graph = HkclToBphclGameReadyGraphBuilder.Build(hkclService);
				File.WriteAllBytes(args[2], NativeBphclStandaloneGraphWriter.WriteGameReady(graph));
				NativeBphclDocument verification = NativeBphclDocument.Open(args[2]);
				Console.WriteLine($"HKCL->BPHCL|cloths={verification.Cloths.Count}|output={args[2]}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--export-hkcl-bphcl-first-pass", StringComparison.OrdinalIgnoreCase))
			{
				HkclService hkclService13 = new HkclService();
				hkclService13.Load(args[1]);
				StandaloneBphclDocumentGraph graph7 = HkclToBphclGameReadyGraphBuilder.Build(hkclService13);
				File.WriteAllBytes(args[2], NativeBphclStandaloneGraphWriter.Write(graph7));
				NativeBphclDocument verification10 = NativeBphclDocument.Open(args[2]);
				BphclStandaloneValidation.Validate(verification10, graph7);
				Console.WriteLine($"HKCL->BPHCL-FIRST-PASS|cloths={verification10.Cloths.Count}|output={args[2]}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--rebuild-native-bphcl-standalone", StringComparison.OrdinalIgnoreCase))
			{
				HkclService hkclService14 = new HkclService();
				hkclService14.Load(args[1]);
				StandaloneBphclDocumentGraph graph8 = HkclToBphclGameReadyGraphBuilder.BuildNativeReconstruction(hkclService14)with
				{
					NativeTypeSection = null
				};
				File.WriteAllBytes(args[2], NativeBphclStandaloneGraphWriter.Write(graph8));
				NativeBphclDocument verification11 = NativeBphclDocument.Open(args[2]);
				BphclStandaloneValidation.Validate(verification11, graph8);
				Console.WriteLine($"BPHCL-NATIVE-REBUILD|cloths={verification11.Cloths.Count}|output={args[2]}");
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-conversion-matches", StringComparison.OrdinalIgnoreCase))
			{
				Console.WriteLine(ConversionMatchAnalyzer.Format(ConversionMatchAnalyzer.Analyze(args[1])));
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--export-two-key-sidecar-tests", StringComparison.OrdinalIgnoreCase))
			{
				if (!Path.GetExtension(args[1]).Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("--export-two-key-sidecar-tests expects a .bphhb source file.");
				}
				string stem = Path.GetFileNameWithoutExtension(args[1]);
				{
					foreach (BphyssbExportSummary result3 in BphyssbBridge.ExportTwoKeyCurveTests(BphhbBridge.Load(args[1]), args[2], stem))
					{
						Console.WriteLine($"BPHHB-TWO-KEY-TEST|output={result3.Path}|curves={result3.CurveCount}");
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--export-symmetric-two-key-sidecar-test", StringComparison.OrdinalIgnoreCase))
			{
				if (!Path.GetExtension(args[1]).Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("--export-symmetric-two-key-sidecar-test expects a .bphhb source file.");
				}
				BphyssbExportSummary result4 = BphyssbBridge.ExportSymmetricTwoKeyCurveTest(BphhbBridge.Load(args[1]), args[2]);
				Console.WriteLine($"BPHHB-SYMMETRIC-TWO-KEY-TEST|output={result4.Path}|curves={result4.CurveCount}");
				return;
			}
			if ((args.Length == 3 || args.Length == 4) && string.Equals(args[0], "--export-constant-head-curve-test", StringComparison.OrdinalIgnoreCase))
			{
				if (!Path.GetExtension(args[1]).Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("--export-constant-head-curve-test expects a .bphhb source file.");
				}
				float bendRadians = ((args.Length == 4) ? float.Parse(args[3], CultureInfo.InvariantCulture) : 0.5f);
				BphyssbExportSummary result5 = BphyssbBridge.ExportConstantSupportCurveTest(BphhbBridge.Load(args[1]), args[2], "Head_B_Assist", bendRadians);
				Console.WriteLine($"BPHHB-CONSTANT-HEAD-CURVE-TEST|output={result5.Path}|curves={result5.CurveCount}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--export-armor182-head-curve-test", StringComparison.OrdinalIgnoreCase))
			{
				if (!Path.GetExtension(args[1]).Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("--export-armor182-head-curve-test expects a .bphhb source file.");
				}
				BphyssbExportSummary result6 = BphyssbBridge.ExportArmor182HeadCurveTest(BphhbBridge.Load(args[1]), args[2], "Head_B_Assist");
				Console.WriteLine($"BPHHB-ARMOR182-HEAD-CURVE-TEST|output={result6.Path}|curves={result6.CurveCount}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--export-sidecar-orientation-tests", StringComparison.OrdinalIgnoreCase))
			{
				if (!Path.GetExtension(args[1]).Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("--export-sidecar-orientation-tests expects a .bphhb source file.");
				}
				string stem2 = Path.GetFileNameWithoutExtension(args[1]);
				{
					foreach (BphyssbExportSummary result7 in BphyssbBridge.ExportSupportBoneOrientationTests(BphhbBridge.Load(args[1]), args[2], stem2, "Head_B_Assist"))
					{
						Console.WriteLine($"BPHHB-ORIENTATION-TEST|output={result7.Path}|curves={result7.CurveCount}");
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--convert-sidecar", StringComparison.OrdinalIgnoreCase))
			{
				string extension = Path.GetExtension(args[1]);
				if (extension.Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
				{
					BphyssbExportSummary result8 = BphyssbBridge.Export(BphhbBridge.Load(args[1]), args[2]);
					Console.WriteLine($"BPHHB->BPHYSSB|bones={result8.BoneCount}|curves={result8.CurveCount}|main={result8.MainBoneCount}|support={result8.SupportBoneCount}");
					return;
				}
				if (extension.Equals(".bphyssb", StringComparison.OrdinalIgnoreCase))
				{
					BphhbGraphSummary graph9 = BphhbBridge.Export(BphyssbBridge.Load(args[1]), args[2]).Graph;
					Console.WriteLine($"BPHYSSB->BPHHB|bones={graph9.Bones.Count}|curves={graph9.CurveCount}|drivers={graph9.Drivers.Count}|driven={graph9.Driven.Count}|outputs={graph9.Outputs.Count}");
					return;
				}
				throw new InvalidOperationException("--convert-sidecar expects a .bphhb or .bphyssb source file.");
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-sidecar", StringComparison.OrdinalIgnoreCase))
			{
				string extension2 = Path.GetExtension(args[1]);
				if (extension2.Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
				{
					BphhbGraphSummary graph10 = BphhbBridge.Load(args[1]).Graph ?? throw new InvalidOperationException("BPHHB graph could not be decoded.");
					Console.WriteLine($"BPHHB|bones={graph10.Bones.Count}|drivers={graph10.Drivers.Count}|driven={graph10.Driven.Count}|poses={graph10.PoseDriven.Count}|outputs={graph10.Outputs.Count}|curves={graph10.CurveCount}");
					foreach (var (bone, index3) in graph10.Bones.Select((string item11, int item12) => (bone: item11, index: item12)))
					{
						Console.WriteLine($"bone|{index3}|{bone}");
					}
					foreach (BphhbDriverBone driver in graph10.Drivers)
					{
						Console.WriteLine($"driver|bone={driver.BoneId}|base={driver.BaseBoneId}|translation={driver.Translation}|rotation={driver.Rotation}");
					}
					foreach (BphhbDrivenBone driven in graph10.Driven)
					{
						Console.WriteLine($"driven|bone={driven.BoneId}|translate={driven.TranslateDrivenId}|rotate={driven.RotateDrivenId}");
					}
					{
						foreach (var (pose, index4) in graph10.PoseDriven.Select((BphhbPoseDrivenBone item11, int item12) => (pose: item11, index: item12)))
						{
							Console.WriteLine($"pose|{index4}|base={pose.BaseBoneId}|translation={pose.Translation}|rotation={pose.Rotation}");
						}
						return;
					}
				}
				if (extension2.Equals(".bphyssb", StringComparison.OrdinalIgnoreCase))
				{
					BphyssbGraphSummary graph11 = BphyssbBridge.Load(args[1]).Graph ?? throw new InvalidOperationException("BPHYSSB graph could not be decoded.");
					Console.WriteLine($"BPHYSSB|bones={graph11.Bones.Count}|main={graph11.MainBones.Count}|support={graph11.SupportBones.Count}|single={graph11.OutputSingleCount}|double={graph11.OutputDoubleCount}|curves={graph11.CurveCount}");
					foreach (var (bone2, index5) in graph11.Bones.Select((string item11, int item12) => (bone: item11, index: item12)))
					{
						Console.WriteLine($"bone|{index5}|{bone2}");
					}
					foreach (BphyssbPoseBone main in graph11.MainBones)
					{
						Console.WriteLine($"main|bone={main.BoneId}|space={main.SpaceId}|translation={main.Translation}|rotation={main.Rotation}");
					}
					{
						foreach (BphyssbPoseBone driven2 in graph11.SupportBones)
						{
							Console.WriteLine($"support|bone={driven2.BoneId}|space={driven2.SpaceId}|translation={driven2.Translation}|rotation={driven2.Rotation}");
						}
						return;
					}
				}
				throw new InvalidOperationException("--inspect-sidecar expects a .bphhb or .bphyssb file.");
			}
			if (args.Length == 3 && string.Equals(args[0], "--compare-sidecar-wiring", StringComparison.OrdinalIgnoreCase))
			{
				foreach (string item15 in SidecarWiringDiagnostics.Compare(args[1], args[2]))
				{
					Console.WriteLine(item15);
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--audit-sidecar-curve-layouts", StringComparison.OrdinalIgnoreCase))
			{
				string root2 = args[1];
				string extension3 = (Directory.EnumerateFiles(root2, "*.bphhb", SearchOption.AllDirectories).Any() ? ".bphhb" : ".bphyssb");
				Dictionary<string, List<string>> layouts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
				foreach (string path2 in Directory.EnumerateFiles(root2, "*" + extension3, SearchOption.AllDirectories).OrderBy<string, string>((string result9) => result9, StringComparer.OrdinalIgnoreCase))
				{
					IEnumerable<int> keyCounts = ((extension3.Equals(".bphhb", StringComparison.OrdinalIgnoreCase) ? (BphhbBridge.Load(path2).Archive ?? throw new InvalidDataException("Could not decode " + path2 + ".")) : (BphyssbBridge.Load(path2).Archive ?? throw new InvalidDataException("Could not decode " + path2 + "."))).Root.Children.FirstOrDefault((AampReadList child) => child.FindChild("connection_curve_list") != null) ?? throw new InvalidDataException(path2 + " has no connection_curve_list.")).FindChild("connection_curve_list").Objects.Select(delegate(AampReadObject aampReadObject)
					{
						if (extension3.Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
						{
							byte[] array7 = aampReadObject.Parameters.FirstOrDefault((AampReadParameter parameter) => parameter.Hash == 139113150)?.Bytes;
							if (array7 == null || array7.Length < 4)
							{
								return -1;
							}
							return BitConverter.ToInt32(array7, 0);
						}
						return aampReadObject.Parameters.Count(delegate(AampReadParameter parameter)
						{
							uint hash = parameter.Hash;
							return (hash == 333816846 || hash == 1692570776 || hash == 2330785204u) ? true : false;
						});
					});
					string layout3 = string.Join(",", from result9 in keyCounts
						group result9 by result9 into grouping
						orderby grouping.Key
						select $"{grouping.Key}x{grouping.Count()}");
					if (!layouts.TryGetValue(layout3, out var paths))
					{
						layouts.Add(layout3, paths = new List<string>());
					}
					paths.Add(path2);
				}
				{
					foreach (KeyValuePair<string, List<string>> layout4 in layouts.OrderByDescending((KeyValuePair<string, List<string>> entry) => entry.Value.Count).ThenBy<KeyValuePair<string, List<string>>, string>((KeyValuePair<string, List<string>> entry) => entry.Key, StringComparer.Ordinal))
					{
						Console.WriteLine($"layout|{layout4.Key}|files={layout4.Value.Count}");
						foreach (string path3 in layout4.Value.Take(20))
						{
							Console.WriteLine("  " + Path.GetFileName(path3));
						}
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--compare-sidecar-curve-values", StringComparison.OrdinalIgnoreCase))
			{
				foreach (string item16 in SidecarWiringDiagnostics.CompareCurveValues(args[1], args[2]))
				{
					Console.WriteLine(item16);
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-sidecar-curves", StringComparison.OrdinalIgnoreCase))
			{
				string extension4 = Path.GetExtension(args[1]);
				object obj2;
				if (!extension4.Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
				{
					if (!extension4.Equals(".bphyssb", StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidOperationException("--inspect-sidecar-curves expects a .bphhb or .bphyssb file.");
					}
					obj2 = BphyssbBridge.Load(args[1]).Archive ?? throw new InvalidDataException("BPHYSSB graph could not be decoded.");
				}
				else
				{
					obj2 = BphhbBridge.Load(args[1]).Archive ?? throw new InvalidDataException("BPHHB graph could not be decoded.");
				}
				AampReadArchive archive = (AampReadArchive)obj2;
				AampReadList curves = (archive.Root.Children.FirstOrDefault((AampReadList child) => child.FindChild("connection_curve_list") != null) ?? throw new InvalidDataException("The sidecar contains no connection_curve_list.")).FindChild("connection_curve_list");
				Console.WriteLine($"format={archive.Type};curves={curves.Objects.Count}");
				{
					foreach (var item17 in curves.Objects.Select((AampReadObject item11, int item12) => (curve: item11, index: item12)))
					{
						AampReadObject curve = item17.curve;
						int index6 = item17.index;
						string fields = string.Join(",", curve.Parameters.Select(delegate(AampReadParameter parameter)
						{
							string text;
							switch (parameter.Type)
							{
							case 0:
							case 2:
								text = ((parameter.Bytes.Length >= 4) ? BitConverter.ToInt32(parameter.Bytes, 0).ToString(CultureInfo.InvariantCulture) : "<short>");
								break;
							case 1:
								text = ((parameter.Bytes.Length >= 4) ? BitConverter.ToSingle(parameter.Bytes, 0).ToString("G9", CultureInfo.InvariantCulture) : "<short>");
								break;
							case 5:
								text = ((parameter.Bytes.Length >= 16) ? $"<{BitConverter.ToSingle(parameter.Bytes, 0):G9};{BitConverter.ToSingle(parameter.Bytes, 4):G9};{BitConverter.ToSingle(parameter.Bytes, 8):G9};{BitConverter.ToSingle(parameter.Bytes, 12):G9}>" : "<short-vector>");
								break;
							default:
								text = $"type{parameter.Type}";
								break;
							}
							string value2 = text;
							return $"0x{parameter.Hash:X8}={value2}";
						}));
						Console.WriteLine($"{index6}|{fields}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-sidecar-wiring", StringComparison.OrdinalIgnoreCase))
			{
				string extension5 = Path.GetExtension(args[1]);
				object obj3;
				if (!extension5.Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
				{
					if (!extension5.Equals(".bphyssb", StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidOperationException("--inspect-sidecar-wiring expects a .bphhb or .bphyssb file.");
					}
					obj3 = BphyssbBridge.Load(args[1]).Archive ?? throw new InvalidDataException("BPHYSSB graph could not be decoded.");
				}
				else
				{
					obj3 = BphhbBridge.Load(args[1]).Archive ?? throw new InvalidDataException("BPHHB graph could not be decoded.");
				}
				AampReadArchive archive2 = (AampReadArchive)obj3;
				string[] listNames = ((!extension5.Equals(".bphhb", StringComparison.OrdinalIgnoreCase)) ? new string[4] { "output_single_list", "output_double_list", "main_bone_list", "support_bone_list" } : new string[4] { "output_list", "driver_bone_list", "driven_bone_list", "pose_driven_list" });
				AampReadList data8 = archive2.Root.Children.FirstOrDefault((AampReadList child) => listNames.Any((string name2) => child.FindChild(name2) != null)) ?? throw new InvalidDataException("The sidecar contains no readable output wiring lists.");
				Console.WriteLine("format=" + archive2.Type);
				string[] array5 = listNames;
				foreach (string name in array5)
				{
					AampReadList list = data8.FindChild(name);
					if (list == null)
					{
						continue;
					}
					Console.WriteLine($"{name}|count={list.Objects.Count}");
					foreach (var item18 in list.Objects.Select((AampReadObject item11, int item12) => (item: item11, index: item12)))
					{
						AampReadObject item7 = item18.item;
						int index7 = item18.index;
						string fields2 = string.Join(",", item7.Parameters.Select((AampReadParameter parameter) => parameter.Type switch
						{
							1 => $"0x{parameter.Hash:X8}={parameter.AsSingle():G8}",
							2 => $"0x{parameter.Hash:X8}={parameter.AsInt32()}",
							4 => $"0x{parameter.Hash:X8}={parameter.AsVector3()}",
							16 => $"0x{parameter.Hash:X8}={parameter.AsVector4()}",
							_ => $"0x{parameter.Hash:X8}:type{parameter.Type}",
						}));
						Console.WriteLine($"  {index7}|{fields2}");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-layout", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclTypeLayout layout5 = NativeBphclReflectionCatalog.GetTypeLayout(args[1]);
				Console.WriteLine($"{layout5.Name} | type={layout5.TypeIndex} | size=0x{layout5.Size:X}");
				{
					foreach (NativeBphclTypeLayoutMember member5 in layout5.Members)
					{
						Console.WriteLine($"+0x{member5.Offset:X4} | {member5.Name} | {member5.TypeName} ({member5.TypeIndex})");
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-member-type", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclTypeLayout layout6 = NativeBphclReflectionCatalog.GetTypeLayout(args[1]);
				uint offset3 = Convert.ToUInt32(args[2].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[2].Substring(2) : args[2], 16);
				uint? typeIndex4 = NativeBphclReflectionCatalog.GetMemberTypeIndex(layout6.TypeIndex, offset3);
				if (!typeIndex4.HasValue)
				{
					throw new InvalidDataException($"{layout6.Name} has no reflected member at 0x{offset3:X}.");
				}
				Console.WriteLine($"{layout6.Name}+0x{offset3:X}|type={typeIndex4}|name={NativeBphclReflectionCatalog.GetTypeName(typeIndex4.Value)}|size=0x{NativeBphclReflectionCatalog.GetTypeSize(typeIndex4.Value):X}");
				return;
			}
			if (args.Length == 1 && string.Equals(args[0], "--inspect-bphcl-type-templates", StringComparison.OrdinalIgnoreCase))
			{
				string[] array5 = new string[4] { "hkArray", "T*", "hkRefPtr", "hkRefVariant" };
				foreach (string typeName5 in array5)
				{
					foreach (uint typeIndex5 in NativeBphclReflectionCatalog.GetTypeIndexes(typeName5))
					{
						string templates = string.Join(",", from tuple7 in NativeBphclReflectionCatalog.GetTypeTemplates(typeIndex5)
							select $"{tuple7.NameIndex}>{tuple7.Value}:{NativeBphclReflectionCatalog.GetTypeName(tuple7.Value)}");
						Console.WriteLine($"{typeIndex5}|{typeName5}|templates=[{templates}]");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--audit-bphcl-array-types", StringComparison.OrdinalIgnoreCase))
			{
				IReadOnlyList<string> findings = BphclSchemaAudit.ValidateArrayStorageTypes(NativeBphclDocument.Open(args[1]));
				Console.WriteLine($"BPHCL array storage audit: {findings.Count} mismatch(es).");
				{
					foreach (string item19 in findings)
					{
						Console.WriteLine(item19);
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-array-headers", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document21 = NativeBphclDocument.Open(args[1]);
				int payloadOffset2 = (document21.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.")).PayloadOffset;
				List<(int ItemIndex, string TypeName, string MemberName, uint Count, uint CapacityAndFlags)> headers = new();
				foreach (var item20 in document21.Items.Select((NativeBphclItem item11, int item12) => (item: item11, index: item12)))
				{
					NativeBphclItem item8 = item20.item;
					int itemIndex5 = item20.index;
					string typeName6 = document21.GetTypeName(item8.TypeIndex);
					if (string.IsNullOrWhiteSpace(typeName6))
					{
						continue;
					}
					NativeBphclTypeLayout layout7;
					try
					{
						layout7 = NativeBphclReflectionCatalog.GetTypeLayout(typeName6);
					}
					catch (InvalidDataException)
					{
						continue;
					}
					uint stride = layout7.Size;
					if (stride == 0 || item8.Count == 0)
					{
						continue;
					}
					foreach (NativeBphclTypeLayoutMember member6 in layout7.Members.Where((NativeBphclTypeLayoutMember nativeBphclTypeLayoutMember) => nativeBphclTypeLayoutMember.TypeName == "hkArray"))
					{
						for (uint elementIndex = 0u; elementIndex < item8.Count; elementIndex++)
						{
							uint fieldOffset3 = item8.DataOffset + elementIndex * stride + member6.Offset;
							if (fieldOffset3 <= document21.DataSection.PayloadSize - 16)
							{
								headers.Add((itemIndex5, typeName6, member6.Name, BitConverter.ToUInt32(document21.Bytes, payloadOffset2 + checked((int)(fieldOffset3 + 8))), BitConverter.ToUInt32(document21.Bytes, payloadOffset2 + checked((int)(fieldOffset3 + 12)))));
							}
						}
					}
				}
				foreach (IGrouping<(uint, uint), (int, string, string, uint, uint)> group4 in from tuple7 in headers
					group tuple7 by (Count: tuple7.Count, CapacityAndFlags: tuple7.CapacityAndFlags) into grouping
					orderby grouping.Key.Count, grouping.Key.CapacityAndFlags
					select grouping)
				{
					Console.WriteLine($"arrayHeader|count={group4.Key.Item1}|capacityAndFlags=0x{group4.Key.Item2:X8}|occurrences={group4.Count()}");
				}
				{
					foreach (var header in headers.Where<(int, string, string, uint, uint)>(((int ItemIndex, string TypeName, string MemberName, uint Count, uint CapacityAndFlags) tuple7) => tuple7.Count != 0 || tuple7.CapacityAndFlags != 0).Take(32))
					{
						Console.WriteLine($"nonDefault|item={header.Item1}|{header.Item2}.{header.Item3}|count={header.Item4}|capacityAndFlags=0x{header.Item5:X8}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--audit-bphcl-reference-types", StringComparison.OrdinalIgnoreCase))
			{
				IReadOnlyList<string> findings2 = BphclSchemaAudit.ValidateDirectReferencePatchTypes(NativeBphclDocument.Open(args[1]));
				Console.WriteLine($"BPHCL direct reference audit: {findings2.Count} mismatch(es).");
				{
					foreach (string item21 in findings2)
					{
						Console.WriteLine(item21);
					}
					return;
				}
			}
			NativeBphclDocument document22;
			int payloadOffset3;
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-container-storage", StringComparison.OrdinalIgnoreCase))
			{
				document22 = NativeBphclDocument.Open(args[1]);
				payloadOffset3 = (document22.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.")).PayloadOffset;
				{
					foreach (NativeBphclNamedVariant variant3 in document22.RootVariants)
					{
						if (!string.Equals(variant3.ClassName, "hclClothContainer", StringComparison.Ordinal) && !string.Equals(variant3.ClassName, "hkaAnimationContainer", StringComparison.Ordinal))
						{
							continue;
						}
						foreach (NativeBphclTypeLayoutMember member7 in NativeBphclReflectionCatalog.GetTypeLayout(variant3.ClassName).Members.Where((NativeBphclTypeLayoutMember nativeBphclTypeLayoutMember) => nativeBphclTypeLayoutMember.TypeName == "hkArray"))
						{
							uint fieldOffset4 = variant3.ObjectDataOffset + member7.Offset;
							if (!document22.TryGetReferencedItem(fieldOffset4, out var storageItemIndex2))
							{
								Console.WriteLine(variant3.ClassName + "." + member7.Name + "|storage=<null>");
								continue;
							}
							NativeBphclItem storage2 = document22.GetItem(storageItemIndex2);
							uint? expected = NativeBphclReflectionCatalog.GetArrayElementType(member7.TypeIndex);
							uint? patchType = document22.InternalPatches.FirstOrDefault((NativeBphclPatch entry) => entry.Offsets.Contains(storage2.DataOffset))?.TypeIndex;
							uint declaredCount2 = ReadUInt34(fieldOffset4 + 8);
							uint capacityAndFlags2 = ReadUInt34(fieldOffset4 + 12);
							Console.WriteLine($"{variant3.ClassName}.{member7.Name}|item={storageItemIndex2}|count={storage2.Count}|declaredCount={declaredCount2}|capacityAndFlags=0x{capacityAndFlags2:X8}|actual={storage2.TypeIndex}:{document22.GetTypeName(storage2.TypeIndex)}|expected={expected}:{(expected.HasValue ? NativeBphclReflectionCatalog.GetTypeName(expected.Value) : "<none>")}|entryPatch={patchType}:{(patchType.HasValue ? document22.GetTypeName(patchType.Value) : "<none>")}");
						}
					}
					return;
				}
			}
			NativeBphclDocument document23;
			int payloadOffset4;
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-cloth-tail", StringComparison.OrdinalIgnoreCase))
			{
				document23 = NativeBphclDocument.Open(args[1]);
				payloadOffset4 = (document23.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.")).PayloadOffset;
				{
					foreach (NativeBphclCloth cloth9 in document23.Cloths)
					{
						uint offset4 = document23.GetItem(cloth9.ItemIndex).DataOffset;
						uint transitions = ReadUInt35(offset4 + 120);
						uint actions = ReadUInt35(offset4 + 136);
						byte generatedAtRuntime = document23.Bytes[payloadOffset4 + checked((int)(offset4 + 144))];
						uint targetPlatform = ReadUInt35(offset4 + 148);
						Console.WriteLine($"{cloth9.Index}|{cloth9.Name}|transitions={transitions}|actions={actions}|generatedAtRuntime={generatedAtRuntime}|targetPlatform=0x{targetPlatform:X8}");
					}
					return;
				}
			}
			NativeBphclDocument document24;
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-state-graphs", StringComparison.OrdinalIgnoreCase))
			{
				document24 = NativeBphclDocument.Open(args[1]);
				if (document24.DataSection == null)
				{
					throw new InvalidDataException("BPHCL has no DATA section.");
				}
				{
					foreach (NativeBphclCloth cloth10 in document24.Cloths)
					{
						foreach (NativeBphclClothState state in cloth10.States)
						{
							uint graphFieldOffset = document24.GetItem(state.ItemIndex).DataOffset + 96;
							if (!document24.TryGetReferencedItem(graphFieldOffset, out var graphItemIndex))
							{
								Console.WriteLine($"cloth={cloth10.Index}:{cloth10.Name}|state={state.Index}:{state.Name}|graph=<null>");
								continue;
							}
							NativeBphclItem graph12 = document24.GetItem(graphItemIndex);
							uint branchFieldOffset = graph12.DataOffset + 24;
							IReadOnlyList<int> roots = ReadIntArray(graph12.DataOffset + 40);
							IReadOnlyList<IReadOnlyList<int>> globalChildren = ReadNestedIntArrays(graph12.DataOffset + 56);
							IReadOnlyList<IReadOnlyList<int>> globalParents = ReadNestedIntArrays(graph12.DataOffset + 72);
							if (!document24.TryGetReferencedItem(branchFieldOffset, out var branchesItemIndex))
							{
								Console.WriteLine($"cloth={cloth10.Index}:{cloth10.Name}|state={state.Index}:{state.Name}|graph={graphItemIndex}|branches=<null>|roots=[{string.Join(',', roots)}]");
								continue;
							}
							NativeBphclItem branches = document24.GetItem(branchesItemIndex);
							Console.WriteLine($"cloth={cloth10.Index}:{cloth10.Name}|state={state.Index}:{state.Name}|graph={graphItemIndex}|branches={branches.Count}|roots=[{string.Join(',', roots)}]|children={FormatNestedIntArrays(globalChildren)}|parents={FormatNestedIntArrays(globalParents)}");
							for (uint branchIndex = 0u; branchIndex < branches.Count; branchIndex++)
							{
								uint branchOffset = branches.DataOffset + branchIndex * 56;
								int branchId = (int)ReadUInt36(branchOffset);
								IReadOnlyList<int> operators = ReadIntArray(branchOffset + 8);
								IReadOnlyList<int> parents = ReadIntArray(branchOffset + 24);
								IReadOnlyList<int> children = ReadIntArray(branchOffset + 40);
								Console.WriteLine($"  branch={branchId}|ops=[{string.Join(',', operators)}]|parents=[{string.Join(',', parents)}]|children=[{string.Join(',', children)}]");
							}
						}
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-state-accesses", StringComparison.OrdinalIgnoreCase))
			{
				foreach (NativeBphclCloth cloth11 in NativeBphclDocument.Open(args[1]).Cloths)
				{
					Dictionary<int, int> operatorIndices = cloth11.Operators.Select((NativeBphclOperatorLayout nativeBphclOperatorLayout, int item11) => (ItemIndex: nativeBphclOperatorLayout.ItemIndex, index: item11)).ToDictionary(((int ItemIndex, int index) entry) => entry.ItemIndex, ((int ItemIndex, int index) entry) => entry.index);
					foreach (NativeBphclClothState state2 in cloth11.States)
					{
						string operators2 = string.Join(",", state2.OperatorItemIndices.Select((int num2) => (!operatorIndices.TryGetValue(num2, out var value2)) ? $"item{num2}" : value2.ToString()));
						string buffers = string.Join(";", state2.BufferAccesses.Select((NativeBphclClothStateBufferAccess access) => $"{access.BufferIndex}:{access.ComponentFlags0}/{access.ComponentFlags1}/{access.ComponentFlags2}/{access.ComponentFlags3}:tri{(access.TrianglesRead ? 1 : 0)}:shadow{access.ShadowBufferIndex}"));
						string transforms = string.Join(";", state2.TransformSetAccesses.Select((NativeBphclClothStateTransformSetAccess access) => $"set{access.TransformSetIndex}:{access.ComponentFlags0}/{access.ComponentFlags1}:track[{string.Join("|", access.TransformTrackers.Select((NativeBphclTransformTracker tracker) => $"r{FormatBits(tracker.Read)},b{FormatBits(tracker.ReadBeforeWrite)},w{FormatBits(tracker.Written)}"))}]"));
						Console.WriteLine($"cloth={cloth11.Index}:{cloth11.Name}|state={state2.Index}:{state2.Name}|ops=[{operators2}]|buffers=[{buffers}]|transforms=[{transforms}]|sims=[{string.Join(",", state2.SimClothItemIndices)}]");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--dump-bphcl-operator-items", StringComparison.OrdinalIgnoreCase))
			{
				foreach (NativeBphclCloth cloth12 in NativeBphclDocument.Open(args[1]).Cloths)
				{
					Console.WriteLine($"cloth|{cloth12.Index}|{cloth12.Name}|item={cloth12.ItemIndex}|offset={cloth12.DataOffset}");
					foreach (NativeBphclOperatorLayout op2 in cloth12.Operators)
					{
						string configs = string.Join(";", op2.SimulateConfigs.Select((NativeBphclSimulateConfig nativeBphclSimulateConfig) => "[" + string.Join(",", nativeBphclSimulateConfig.ConstraintExecution) + "]"));
						Console.WriteLine($"  op|{op2.Index}|{op2.ClassName}|item={op2.ItemIndex}|offset={op2.DataOffset}|configs={configs}");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-sim-runtime", StringComparison.OrdinalIgnoreCase))
			{
				foreach (NativeBphclCloth cloth13 in NativeBphclDocument.Open(args[1]).Cloths)
				{
					foreach (NativeBphclSimCloth simulation in cloth13.SimCloths)
					{
						NativeBphclSimulationRuntime runtime = simulation.Runtime;
						NativeBphclTransferMotionData transfer = runtime.TransferMotionData;
						NativeBphclLandscapeCollisionData landscape = runtime.LandscapeCollisionData;
						NativeBphclParticle[] dynamicParticles = simulation.Particles.Where((NativeBphclParticle nativeBphclParticle) => !nativeBphclParticle.Fixed).ToArray();
						float dynamicMass = dynamicParticles.Sum((NativeBphclParticle nativeBphclParticle) => nativeBphclParticle.Mass);
						Console.WriteLine($"{cloth13.Index}|{cloth13.Name}|sim={simulation.Index}|particles={simulation.Particles.Count}:dynamic={dynamicParticles.Length}:dynamicMass={dynamicMass:G8}|normals={runtime.DoNormals}|radius={runtime.MaxParticleRadius:G8}|mass={runtime.TotalMass:G8}|transfer={runtime.TransferMotionEnabled}:{transfer.TransformSetIndex}/{transfer.TransformIndex}:T{transfer.TransferTranslationMotion}:{transfer.MinTranslationSpeed:G8}-{transfer.MaxTranslationSpeed:G8}:R{transfer.TransferRotationMotion}:{transfer.MinRotationSpeed:G8}-{transfer.MaxRotationSpeed:G8}|landscape={runtime.LandscapeCollisionEnabled}:{runtime.NumLandscapeCollidableParticles}:{landscape.LandscapeRadius:G8},{landscape.CollisionTolerance:G8}:stuck={landscape.EnableStuckParticleDetection}/{landscape.StuckParticlesStretchFactorSq:G8}:landPinch={landscape.PinchDetectionEnabled}/{landscape.PinchDetectionPriority}/{landscape.PinchDetectionRadius:G8}|pinch={runtime.PinchDetectionEnabled}:flags{runtime.PerParticlePinchDetectionEnabledFlags.Count}:colliders{runtime.CollidablePinchingData.Count}:range{runtime.MinPinchedParticleIndex}-{runtime.MaxPinchedParticleIndex}|pairs={runtime.MaxCollisionPairs}|flips={simulation.TriangleFlips.Count}|pinchData={string.Join(',', runtime.CollidablePinchingData.Select((NativeBphclCollidablePinchingData nativeBphclCollidablePinchingData) => $"{nativeBphclCollidablePinchingData.Enabled}/{nativeBphclCollidablePinchingData.Priority}/{nativeBphclCollidablePinchingData.Radius:G6}"))}");
					}
				}
				return;
			}
			NativeBphclDocument document25;
			NativeBphclSection data9;
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-virtual-collision", StringComparison.OrdinalIgnoreCase))
			{
				document25 = NativeBphclDocument.Open(args[1]);
				data9 = document25.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
				(string, uint)[] arrays2 = new(string, uint)[18]
				{
					("blocks", 0u),
					("landscapeBlocks", 24u),
					("edgeBarycentrics", 48u),
					("edgeDictionary", 64u),
					("triangleBarycentrics", 80u),
					("triangleDictionary", 96u),
					("edges", 112u),
					("edgeFans", 128u),
					("triangles", 144u),
					("triangleFans", 160u),
					("landscapeEdges", 176u),
					("landscapeEdgeFans", 192u),
					("landscapeTriangles", 208u),
					("landscapeTriangleFans", 224u),
					("edgeFanIndices", 240u),
					("triangleFanIndices", 256u),
					("landscapeEdgeFanIndices", 272u),
					("landscapeTriangleFanIndices", 288u)
				};
				{
					foreach (NativeBphclCloth cloth14 in document25.Cloths)
					{
						foreach (NativeBphclSimCloth simulation2 in cloth14.SimCloths)
						{
							uint start4 = checked(simulation2.DataOffset + 432);
							if (start4 > data9.PayloadSize - 304)
							{
								throw new InvalidDataException("Simulation '" + simulation2.Name + "' has a truncated virtual-collision block.");
							}
							IEnumerable<string> values2 = arrays2.Select(delegate((string Name, uint Offset) tuple7)
							{
								uint num2 = checked(start4 + tuple7.Offset);
								uint value2 = ReadUInt37(num2 + 8);
								string value3 = (document25.TryGetReferencedItem(num2, out var itemIndex6) ? itemIndex6.ToString() : "none");
								return $"{tuple7.Name}={value2}@{value3}";
							});
							Console.WriteLine($"{cloth14.Index}|{cloth14.Name}|sim={simulation2.Index}|points={ReadUInt38(start4 + 16)}|landscapePoints={ReadUInt38(start4 + 40)}|" + string.Join(",", values2));
						}
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-patch-owner", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document26 = NativeBphclDocument.Open(args[1]);
				string offsetText = (args[2].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[2].Substring(2) : args[2]);
				uint offset5 = Convert.ToUInt32(offsetText, 16);
				NativeBphclPatch patch = document26.InternalPatches.FirstOrDefault((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.Offsets.Contains(offset5));
				(int, (uint, uint)) owner = (from entry in document26.GetItemRanges()
					select (ItemIndex: entry.Key, Range: entry.Value)).FirstOrDefault(((int ItemIndex, (uint Start, uint End) Range) entry) => offset5 >= entry.Range.Start && offset5 < entry.Range.End);
				Console.WriteLine($"offset=0x{offset5:X}|patch={patch?.TypeIndex}:{(((object)patch == null) ? "<none>" : document26.GetTypeName(patch.TypeIndex))}|owner={owner.Item1}:{document26.GetTypeName(document26.GetItem(owner.Item1).TypeIndex)}|relative=0x{offset5 - owner.Item2.Item1:X}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-patch-type", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document27 = NativeBphclDocument.Open(args[1]);
				uint typeIndex6 = uint.Parse(args[2], CultureInfo.InvariantCulture);
				Dictionary<int, (uint, uint)> ranges4 = document27.GetItemRanges();
				NativeBphclPatch group5 = document27.InternalPatches.SingleOrDefault((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.TypeIndex == typeIndex6) ?? throw new InvalidDataException($"BPHCL has no PTCH group for type {typeIndex6}.");
				Console.WriteLine($"patchType|{typeIndex6}:{document27.GetTypeName(typeIndex6)}|count={group5.Offsets.Count}");
				{
					foreach (uint offset6 in group5.Offsets)
					{
						(int, (uint, uint)) owner2 = ranges4.Select<KeyValuePair<int, (uint, uint)>, (int, (uint, uint))>((KeyValuePair<int, (uint Start, uint End)> entry) => (ItemIndex: entry.Key, Range: entry.Value)).FirstOrDefault<(int, (uint, uint))>(((int ItemIndex, (uint Start, uint End) Range) entry) => offset6 >= entry.Range.Start && offset6 < entry.Range.End);
						NativeBphclItem item9 = document27.GetItem(owner2.Item1);
						string target8 = (document27.TryGetReferencedItem(offset6, out var targetIndex) ? targetIndex.ToString(CultureInfo.InvariantCulture) : "?");
						Console.WriteLine($"offset=0x{offset6:X}|owner={owner2.Item1}:{document27.GetTypeName(item9.TypeIndex)}+0x{offset6 - item9.DataOffset:X}|target={target8}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-patches", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document28 = NativeBphclDocument.Open(args[1]);
				Console.WriteLine($"PTCH|groups={document28.InternalPatches.Count}|fixups={document28.InternalPatches.Sum((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.Offsets.Count)}");
				{
					foreach (NativeBphclPatch group6 in document28.InternalPatches.OrderBy((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.TypeIndex))
					{
						string typeName7 = document28.GetTypeName(group6.TypeIndex) ?? $"<type {group6.TypeIndex}>";
						uint first = ((group6.Offsets.Count != 0) ? group6.Offsets.Min() : 0u);
						uint last = ((group6.Offsets.Count != 0) ? group6.Offsets.Max() : 0u);
						Console.WriteLine($"{group6.TypeIndex}|{typeName7}|count={group6.Offsets.Count}|range=0x{first:X}-0x{last:X}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-aamp", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument nativeBphclDocument2 = NativeBphclDocument.Open(args[1]);
				if (nativeBphclDocument2.Header.ParameterSize == 0)
				{
					throw new InvalidDataException("BPHCL does not contain an embedded AAMP block.");
				}
				int offset7;
				int size;
				checked
				{
					offset7 = (int)nativeBphclDocument2.Header.ParameterOffset;
					size = (int)nativeBphclDocument2.Header.ParameterSize;
				}
				int archiveSize = BitConverter.ToInt32(nativeBphclDocument2.Bytes, offset7 + 12);
				if (archiveSize <= 0 || archiveSize > size)
				{
					throw new InvalidDataException("BPHCL declares an invalid embedded AAMP size.");
				}
				AampReadArchive archive3 = BotwAampReader.Read(nativeBphclDocument2.Bytes.AsSpan(offset7, archiveSize).ToArray());
				string[] knownLists = new string[2] { "cloth_mesh_list", "collidable_list" };
				string[] knownParameters = new string[11]
				{
					"Name", "BaseBone", "BoneCorrection", "BoneCorrectionAxisOrder", "Twist", "TwistSwingAxis", "TwistAngleCoef", "TwistMaxAngle", "KeepBoneLength", "Preset",
					"ForReplace"
				};
				Console.WriteLine($"AAMP|type={archive3.Type}|size={size}");
				{
					foreach (AampReadList list2 in archive3.Root.Children)
					{
						string listName = knownLists.FirstOrDefault((string value2) => BotwAampWriter.Crc32(value2) == list2.Hash) ?? $"0x{list2.Hash:X8}";
						Console.WriteLine($"{listName}|objects={list2.Objects.Count}");
						foreach (var item22 in list2.Objects.Select((AampReadObject item11, int item12) => (item: item11, index: item12)))
						{
							AampReadObject item10 = item22.item;
							int index8 = item22.index;
							string fields3 = string.Join(", ", item10.Parameters.Select((AampReadParameter parameter) => (knownParameters.FirstOrDefault((string value2) => BotwAampWriter.Crc32(value2) == parameter.Hash) ?? $"0x{parameter.Hash:X8}") + "=" + DescribeValue(parameter)));
							Console.WriteLine($"  {index8}|{fields3}");
						}
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-packed-skins", StringComparison.OrdinalIgnoreCase))
			{
				foreach (NativeBphclCloth cloth15 in NativeBphclDocument.Open(args[1]).Cloths)
				{
					foreach (NativeBphclObjectSpaceSkin skin in cloth15.ObjectSpaceSkins)
					{
						NativeBphclObjectSpaceDeformer deformer = skin.Deformer;
						Console.WriteLine($"skin|cloth={cloth15.Index}:{cloth15.Name}|op={skin.OperatorIndex}|range={deformer.StartVertexIndex}-{deformer.EndVertexIndex}|partial={deformer.PartialWrite}|subset=[{string.Join(',', skin.TransformSubset)}]|localPs={skin.LocalPs.Count}|localUnpacked={skin.LocalUnpackedPs.Count}|controls={Convert.ToHexString(deformer.ControlBytes.ToArray())}");
						foreach (var (block, blockIndex) in skin.LocalPs.Select((NativeBphclPackedPositionBlock item11, int item12) => (block: item11, index: item12)))
						{
							Console.WriteLine($"  localPs|{blockIndex}|{string.Join(',', block.Positions)}");
						}
						DumpBlend("eight", deformer.EightBlendEntries);
						DumpBlend("seven", deformer.SevenBlendEntries);
						DumpBlend("six", deformer.SixBlendEntries);
						DumpBlend("five", deformer.FiveBlendEntries);
						DumpBlend("four", deformer.FourBlendEntries);
						DumpBlend("three", deformer.ThreeBlendEntries);
						DumpBlend("two", deformer.TwoBlendEntries);
						DumpBlend("one", deformer.OneBlendEntries);
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-skins", StringComparison.OrdinalIgnoreCase))
			{
				foreach (NativeBphclCloth cloth16 in NativeBphclDocument.Open(args[1]).Cloths)
				{
					string operators3 = string.Join(",", cloth16.Operators.Select((NativeBphclOperatorLayout nativeBphclOperatorLayout) => nativeBphclOperatorLayout.ClassName));
					string objectSkins = string.Join(",", cloth16.ObjectSpaceSkins.Select((NativeBphclObjectSpaceSkin nativeBphclObjectSpaceSkin) => $"op{nativeBphclObjectSpaceSkin.OperatorIndex}:buf{nativeBphclObjectSpaceSkin.OutputBufferIndex}:set{nativeBphclObjectSpaceSkin.TransformSetIndex}:subset{nativeBphclObjectSpaceSkin.TransformSubset.Count}:mat{nativeBphclObjectSpaceSkin.BoneFromSkinMeshTransforms.Count}:local{nativeBphclObjectSpaceSkin.LocalPs.Count}/{nativeBphclObjectSpaceSkin.LocalUnpackedPs.Count}"));
					string boneSkins = string.Join(",", cloth16.BoneSpaceSkins.Select((NativeBphclBoneSpaceSkin nativeBphclBoneSpaceSkin) => $"op{nativeBphclBoneSpaceSkin.OperatorIndex}:buf{nativeBphclBoneSpaceSkin.OutputBufferIndex}:set{nativeBphclBoneSpaceSkin.TransformSetIndex}:subset{nativeBphclBoneSpaceSkin.TransformSubset.Count}:local{nativeBphclBoneSpaceSkin.LocalPs.Count}/{nativeBphclBoneSpaceSkin.LocalUnpackedPs.Count}"));
					string deformers = string.Join(",", cloth16.SimpleMeshBoneDeformers.Select((NativeBphclSimpleMeshBoneDeform nativeBphclSimpleMeshBoneDeform) => $"op{nativeBphclSimpleMeshBoneDeform.OperatorIndex}:in{nativeBphclSimpleMeshBoneDeform.InputBufferIndex}:set{nativeBphclSimpleMeshBoneDeform.OutputTransformSetIndex}:pairs{nativeBphclSimpleMeshBoneDeform.TriangleBonePairs.Count}:local{nativeBphclSimpleMeshBoneDeform.LocalBoneTransforms.Count}"));
					string states = string.Join(";", cloth16.States.Select((NativeBphclClothState nativeBphclClothState) => $"#{nativeBphclClothState.Index}:{nativeBphclClothState.Name}:ops[{string.Join(',', nativeBphclClothState.OperatorItemIndices)}]:buf[{string.Join(',', nativeBphclClothState.BufferAccesses.Select((NativeBphclClothStateBufferAccess access) => $"{access.BufferIndex}/{access.ShadowBufferIndex}:{access.ComponentFlags0},{access.ComponentFlags1},{access.ComponentFlags2},{access.ComponentFlags3}:tri{access.TrianglesRead}"))}]:set[{string.Join(',', nativeBphclClothState.TransformSetAccesses.Select((NativeBphclClothStateTransformSetAccess access) => $"{access.TransformSetIndex}:{access.ComponentFlags0},{access.ComponentFlags1}:track{access.TransformTrackers.Count}"))}]:sim[{string.Join(',', nativeBphclClothState.SimClothItemIndices)}]"));
					Console.WriteLine($"{cloth16.Index}|{cloth16.Name}|ops={operators3}|objectSkins={objectSkins}|boneSkins={boneSkins}|deformers={deformers}|states={states}");
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-skeleton", StringComparison.OrdinalIgnoreCase))
			{
				foreach (NativeBphclSkeleton skeleton6 in NativeBphclDocument.Open(args[1]).Skeletons)
				{
					Console.WriteLine($"skeleton[{skeleton6.Index}]|{skeleton6.Name}|bones={skeleton6.Bones.Count}");
					foreach (NativeBphclBone bone3 in skeleton6.Bones)
					{
						Console.WriteLine($"  {bone3.Index}|parent={bone3.ParentIndex}|{bone3.Name}");
					}
				}
				return;
			}
			if (args.Length == 1 && string.Equals(args[0], "--inspect-hkx-simulate-operator", StringComparison.OrdinalIgnoreCase))
			{
				Type type = typeof(hclSimulateOperator);
				Console.WriteLine(type.FullName);
				FieldInfo[] fields4 = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
				foreach (FieldInfo field in fields4)
				{
					Console.WriteLine("field | " + field.FieldType.Name + " | " + field.Name);
				}
				PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
				foreach (PropertyInfo property in properties)
				{
					Console.WriteLine("property | " + property.PropertyType.Name + " | " + property.Name);
				}
				return;
			}
			if (args.Length == 1 && string.Equals(args[0], "--inspect-hkx-local-range", StringComparison.OrdinalIgnoreCase))
			{
				foreach (Type item23 in from type3 in typeof(hclLocalRangeConstraintSet).Assembly.GetTypes()
					where type3.Name.Contains("LocalRangeConstraintSet", StringComparison.Ordinal)
					select type3)
				{
					Console.WriteLine(item23.FullName);
					FieldInfo[] fields4 = item23.GetFields(BindingFlags.Instance | BindingFlags.Public);
					foreach (FieldInfo field2 in fields4)
					{
						Console.WriteLine("field | " + field2.FieldType.Name + " | " + field2.Name);
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-hkx-type", StringComparison.OrdinalIgnoreCase))
			{
				foreach (Type type2 in (from type3 in typeof(hclSimulateOperator).Assembly.GetTypes()
					where type3.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase)
					select type3).OrderBy<Type, string>((Type type3) => type3.FullName, StringComparer.Ordinal))
				{
					Console.WriteLine(type2.FullName);
					FieldInfo[] fields4 = type2.GetFields(BindingFlags.Instance | BindingFlags.Public);
					foreach (FieldInfo field3 in fields4)
					{
						Console.WriteLine("field | " + field3.FieldType.Name + " | " + field3.Name);
					}
					PropertyInfo[] properties = type2.GetProperties(BindingFlags.Instance | BindingFlags.Public);
					foreach (PropertyInfo property2 in properties)
					{
						Console.WriteLine("property | " + property2.PropertyType.Name + " | " + property2.Name);
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-constraint-program", StringComparison.OrdinalIgnoreCase))
			{
				InspectBphclConstraintProgram(NativeBphclDocument.Open(args[1]));
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-hkcl-constraint-program", StringComparison.OrdinalIgnoreCase))
			{
				InspectHkclConstraintProgram(args[1]);
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-collider-map", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument document29 = NativeBphclDocument.Open(args[1]);
				{
					foreach (var item24 in document29.Cloths.Select((NativeBphclCloth item11, int item12) => (cloth: item11, index: item12)))
					{
						NativeBphclCloth cloth17 = item24.cloth;
						int clothIndex9 = item24.index;
						NativeBphclSkeleton skeleton7 = document29.Skeletons.ElementAtOrDefault(clothIndex9);
						if (skeleton7 == null)
						{
							continue;
						}
						foreach (NativeBphclSimCloth simulation3 in cloth17.SimCloths)
						{
							int slot;
							for (slot = 0; slot < simulation3.CollidableItemIndices.Count; slot++)
							{
								if (slot < simulation3.CollidableTransformMap.TransformIndices.Count && slot < simulation3.CollidableTransformMap.Offsets.Count)
								{
									NativeBphclCollider collider = document29.Colliders.FirstOrDefault((NativeBphclCollider nativeBphclCollider) => nativeBphclCollider.ItemIndex == simulation3.CollidableItemIndices[slot]);
									if (!(collider == null))
									{
										int boneIndex = checked((int)simulation3.CollidableTransformMap.TransformIndices[slot]);
										Matrix4x4 boneWorld = GetBphclBoneWorld(skeleton7, boneIndex);
										Matrix4x4 offset8 = simulation3.CollidableTransformMap.Offsets[slot];
										Matrix4x4 stored = new Matrix4x4(collider.AxisX.X, collider.AxisX.Y, collider.AxisX.Z, collider.AxisX.W, collider.AxisY.X, collider.AxisY.Y, collider.AxisY.Z, collider.AxisY.W, collider.AxisZ.X, collider.AxisZ.Y, collider.AxisZ.Z, collider.AxisZ.W, collider.Translation.X, collider.Translation.Y, collider.Translation.Z, collider.Translation.W);
										Matrix4x4 offsetThenBone = offset8 * boneWorld;
										Matrix4x4 boneThenOffset = boneWorld * offset8;
										Console.WriteLine($"{clothIndex9}|{cloth17.Name}|slot={slot}|item={simulation3.CollidableItemIndices[slot]}|{collider.Name}|shape={collider.Shape.TypeName}|bone={boneIndex}|offset*bone={MatrixDistance(stored, offsetThenBone):G7}|bone*offset={MatrixDistance(stored, boneThenOffset):G7}|stored={stored.M41:G7},{stored.M42:G7},{stored.M43:G7}|left={offsetThenBone.M41:G7},{offsetThenBone.M42:G7},{offsetThenBone.M43:G7}|right={boneThenOffset.M41:G7},{boneThenOffset.M42:G7},{boneThenOffset.M43:G7}");
									}
								}
							}
						}
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--compare-bphcl-skeletons", StringComparison.OrdinalIgnoreCase))
			{
				NativeBphclDocument native7 = NativeBphclDocument.Open(args[1]);
				NativeBphclDocument fresh2 = NativeBphclDocument.Open(args[2]);
				int skeletonCount = Math.Min(native7.Skeletons.Count, fresh2.Skeletons.Count);
				for (int skeletonIndex = 0; skeletonIndex < skeletonCount; skeletonIndex++)
				{
					NativeBphclSkeleton nativeSkeleton2 = native7.Skeletons[skeletonIndex];
					Dictionary<string, NativeBphclBone> freshByName2 = fresh2.Skeletons[skeletonIndex].Bones.GroupBy<NativeBphclBone, string>((NativeBphclBone nativeBphclBone) => nativeBphclBone.Name, StringComparer.Ordinal).ToDictionary<IGrouping<string, NativeBphclBone>, string, NativeBphclBone>((IGrouping<string, NativeBphclBone> grouping) => grouping.Key, (IGrouping<string, NativeBphclBone> source9) => source9.First(), StringComparer.Ordinal);
					foreach (NativeBphclBone nativeBone in nativeSkeleton2.Bones)
					{
						if (!freshByName2.TryGetValue(nativeBone.Name, out var freshBone))
						{
							Console.WriteLine($"skeleton={skeletonIndex}|{nativeSkeleton2.Name}|bone={nativeBone.Index}|{nativeBone.Name}|missing=fresh");
							continue;
						}
						float translation = Vector4.Distance(nativeBone.Translation, freshBone.Translation);
						float rotation = MathF.Min(Vector4.Distance(nativeBone.Rotation, freshBone.Rotation), Vector4.Distance(nativeBone.Rotation, -freshBone.Rotation));
						if (nativeBone.ParentIndex != freshBone.ParentIndex || translation > 1E-06f || rotation > 1E-06f)
						{
							Console.WriteLine($"skeleton={skeletonIndex}|{nativeSkeleton2.Name}|bone={nativeBone.Index}|{nativeBone.Name}|fresh={freshBone.Index}|parent={nativeBone.ParentIndex}/{freshBone.ParentIndex}|translation={translation:G9}|rotation={rotation:G9}|nativeT={nativeBone.Translation.X:G7},{nativeBone.Translation.Y:G7},{nativeBone.Translation.Z:G7},{nativeBone.Translation.W:G7}|freshT={freshBone.Translation.X:G7},{freshBone.Translation.Y:G7},{freshBone.Translation.Z:G7},{freshBone.Translation.W:G7}|nativeR={nativeBone.Rotation.X:G7},{nativeBone.Rotation.Y:G7},{nativeBone.Rotation.Z:G7},{nativeBone.Rotation.W:G7}|freshR={freshBone.Rotation.X:G7},{freshBone.Rotation.Y:G7},{freshBone.Rotation.Z:G7},{freshBone.Rotation.W:G7}");
						}
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-bend-stiffness-pairs", StringComparison.OrdinalIgnoreCase))
			{
				foreach (NativeBphclCloth cloth18 in NativeBphclDocument.Open(args[1]).Cloths)
				{
					foreach (NativeBphclSimCloth simulation4 in cloth18.SimCloths)
					{
						NativeBphclConstraintSet bendSet = simulation4.ConstraintSets.FirstOrDefault((NativeBphclConstraintSet nativeBphclConstraintSet) => string.Equals(nativeBphclConstraintSet.ClassName, "hclBendLinkConstraintSet", StringComparison.Ordinal));
						NativeBphclConstraintSet bendStiffnessSet = simulation4.ConstraintSets.FirstOrDefault((NativeBphclConstraintSet nativeBphclConstraintSet) => string.Equals(nativeBphclConstraintSet.ClassName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal));
						if ((object)bendSet != null && (object)bendStiffnessSet != null)
						{
							Console.WriteLine("cloth=" + cloth18.Name + "|simulation=" + simulation4.Name);
							foreach (NativeBphclConstraintLink source7 in bendSet.Links)
							{
								int? particleA = source7.ParticleA;
								if (particleA.HasValue)
								{
									int particleA2 = particleA.GetValueOrDefault();
									particleA = source7.ParticleB;
									if (particleA.HasValue)
									{
										int particleB = particleA.GetValueOrDefault();
										if (source7.Values.TryGetValue("bendStiffness", out var sourceStiffness))
										{
											NativeBphclConstraintLink derived = bendStiffnessSet.Links.FirstOrDefault((NativeBphclConstraintLink nativeBphclConstraintLink) => nativeBphclConstraintLink.ParticleA == particleA2 && nativeBphclConstraintLink.ParticleB == particleB);
											if ((object)derived == null || !derived.Values.TryGetValue("bendStiffness", out var derivedStiffness))
											{
												Console.WriteLine($"  A={particleA2}|B={particleB}|source={sourceStiffness:G9}|derived=missing");
											}
											else
											{
												double divisor = ((Math.Abs(derivedStiffness) > 1E-09) ? (sourceStiffness / Math.Abs(derivedStiffness)) : 0.0);
												Console.WriteLine($"  A={particleA2}|B={particleB}|length={source7.Values.GetValueOrDefault("stretchMaxLength"):G9}|source={sourceStiffness:G9}|derived={derivedStiffness:G9}|divisor={divisor:G9}|curvature={derived.Values.GetValueOrDefault("restCurvature"):G9}");
											}
										}
									}
								}
							}
						}
					}
				}
				return;
			}
			checked
			{
				if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-bend-rest", StringComparison.OrdinalIgnoreCase))
				{
					foreach (NativeBphclCloth cloth19 in NativeBphclDocument.Open(args[1]).Cloths)
					{
						foreach (NativeBphclSimCloth simulation5 in cloth19.SimCloths)
						{
							foreach (NativeBphclConstraintSet set in simulation5.ConstraintSets.Where((NativeBphclConstraintSet nativeBphclConstraintSet) => string.Equals(nativeBphclConstraintSet.ClassName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal)))
							{
								double maximum2 = (set.Values.TryGetValue("maxRestPoseHeightSq", out var value) ? value : 0.0);
								float[] radii = simulation5.Particles.Select((NativeBphclParticle nativeBphclParticle) => nativeBphclParticle.Radius).ToArray();
								float[] triangles = (from triangle in simulation5.TriangleIndices.Chunk(3)
									where triangle.Length == 3 && triangle.All((ushort num2) => num2 < simulation5.Particles.Count)
									select triangle).Select(delegate(ushort[] triangle)
								{
									Vector3 vector2 = new Vector3(simulation5.Particles[triangle[0]].Position.X, simulation5.Particles[triangle[0]].Position.Y, simulation5.Particles[triangle[0]].Position.Z);
									Vector3 vector3 = new Vector3(simulation5.Particles[triangle[1]].Position.X, simulation5.Particles[triangle[1]].Position.Y, simulation5.Particles[triangle[1]].Position.Z);
									return Vector3.Cross(vector2: new Vector3(simulation5.Particles[triangle[2]].Position.X, simulation5.Particles[triangle[2]].Position.Y, simulation5.Particles[triangle[2]].Position.Z) - vector2, vector1: vector3 - vector2).Length() * 0.5f;
								}).ToArray();
								Console.WriteLine($"cloth={cloth19.Name}|simulation={simulation5.Name}|set={set.Index}|maxRestPoseHeightSq={maximum2:G9}|radius={radii.Min():G9}-{radii.Max():G9}|triangleArea={triangles.DefaultIfEmpty().Min():G9}-{triangles.DefaultIfEmpty().Max():G9}");
								foreach (NativeBphclConstraintLink link in set.Links)
								{
									int? particleA = link.ParticleA;
									if (particleA.HasValue)
									{
										int particleA3 = particleA.GetValueOrDefault();
										particleA = link.ParticleB;
										if (particleA.HasValue)
										{
											int particleB2 = particleA.GetValueOrDefault();
											if (link.Values.TryGetValue("particleC", out var particleCValue) && link.Values.TryGetValue("particleD", out var particleDValue))
											{
												int particleC = (int)particleCValue;
												int particleD = (int)particleDValue;
												if (particleA3 >= 0 && particleB2 >= 0 && particleC >= 0 && particleD >= 0 && particleA3 < simulation5.Particles.Count && particleB2 < simulation5.Particles.Count && particleC < simulation5.Particles.Count && particleD < simulation5.Particles.Count)
												{
													Vector3 a = new Vector3(simulation5.Particles[particleA3].Position.X, simulation5.Particles[particleA3].Position.Y, simulation5.Particles[particleA3].Position.Z);
													Vector3 b = new Vector3(simulation5.Particles[particleB2].Position.X, simulation5.Particles[particleB2].Position.Y, simulation5.Particles[particleB2].Position.Z);
													Vector3 c = new Vector3(simulation5.Particles[particleC].Position.X, simulation5.Particles[particleC].Position.Y, simulation5.Particles[particleC].Position.Z);
													Vector3 d = new Vector3(simulation5.Particles[particleD].Position.X, simulation5.Particles[particleD].Position.Y, simulation5.Particles[particleD].Position.Z);
													Vector3 edge = b - a;
													float edgeLengthSquared = edge.LengthSquared();
													float heightCSquared = ((edgeLengthSquared > 1E-09f) ? (Vector3.Cross(c - a, edge).LengthSquared() / edgeLengthSquared) : 0f);
													float heightDSquared = ((edgeLengthSquared > 1E-09f) ? (Vector3.Cross(d - a, edge).LengthSquared() / edgeLengthSquared) : 0f);
													Vector3 oppositeEdge = d - c;
													float edgeDot = Vector3.Dot(edge, oppositeEdge);
													float oppositeLengthSquared = oppositeEdge.LengthSquared();
													float denominator = edgeLengthSquared * oppositeLengthSquared - edgeDot * edgeDot;
													Vector3 ac = a - c;
													float closestA = ((denominator > 1E-10f) ? ((edgeDot * Vector3.Dot(oppositeEdge, ac) - oppositeLengthSquared * Vector3.Dot(edge, ac)) / denominator) : 0.5f);
													float closestC = ((denominator > 1E-10f) ? ((edgeLengthSquared * Vector3.Dot(oppositeEdge, ac) - edgeDot * Vector3.Dot(edge, ac)) / denominator) : 0.5f);
													Vector3 closestOnAb = a + closestA * edge;
													Vector3 closestOnCd = c + closestC * oppositeEdge;
													Vector3 cross = Vector3.Cross(edge, oppositeEdge);
													float crossLength = cross.Length();
													float signedDistance = ((crossLength > 1E-09f) ? Vector3.Dot(closestOnCd - closestOnAb, cross / crossLength) : 0f);
													Vector3 vector = Vector3.Normalize(Vector3.Cross(edge, c - a));
													Vector3 normalD = Vector3.Normalize(Vector3.Cross(d - a, edge));
													float sine = Vector3.Dot(Vector3.Cross(vector, normalD), Vector3.Normalize(edge));
													float cosine = Vector3.Dot(vector, normalD);
													float angle = MathF.Atan2(sine, cosine);
													IReadOnlyDictionary<string, double> values3 = link.Values;
													Console.WriteLine($"  link={link.Index}|A={particleA3}|B={particleB2}|C={particleC}|D={particleD}|heightC2={heightCSquared:G9}|heightD2={heightDSquared:G9}|CD2={Vector3.DistanceSquared(c, d):G9}|AB2={edgeLengthSquared:G9}|positions=A({Format(a)}) B({Format(b)}) C({Format(c)}) D({Format(d)})|weights={values3.GetValueOrDefault("weightA"):G9},{values3.GetValueOrDefault("weightB"):G9},{values3.GetValueOrDefault("weightC"):G9},{values3.GetValueOrDefault("weightD"):G9}|closest={1f - closestA:G9},{closestA:G9},{0f - (1f - closestC):G9},{0f - closestC:G9}|curvature={values3.GetValueOrDefault("restCurvature"):G9}|distance={signedDistance:G9}|angle={angle:G9}|sin={sine:G9}|cos={cosine:G9}|stiffness={values3.GetValueOrDefault("bendStiffness"):G9}");
												}
											}
										}
									}
								}
							}
						}
					}
					return;
				}
				if (args.Length == 2 && string.Equals(args[0], "--dump-bphcl-constraints", StringComparison.OrdinalIgnoreCase))
				{
					foreach (NativeBphclCloth cloth20 in NativeBphclDocument.Open(args[1]).Cloths)
					{
						foreach (NativeBphclSimCloth simulation6 in cloth20.SimCloths)
						{
							Console.WriteLine("cloth=" + cloth20.Name + " | simulation=" + simulation6.Name);
							foreach (NativeBphclConstraintSet set2 in simulation6.ConstraintSets)
							{
								Console.WriteLine($"  {set2.Index}: {set2.ClassName} | bucket={(set2.IsAntiPinch ? "anti-pinch" : "static")} | id={set2.ConstraintId} | type={set2.ConstraintType} | set=[{string.Join(", ", set2.Values.Select<KeyValuePair<string, double>, string>((KeyValuePair<string, double> keyValuePair) => $"{keyValuePair.Key}={keyValuePair.Value:G8}"))}]");
								DumpRecords("links", set2.Links);
								DumpRecords("local", set2.LocalConstraints);
								DumpRecords("local stiffness", set2.LocalStiffnessConstraints);
								DumpRecords("transition", set2.TransitionParticles);
							}
						}
					}
					return;
				}
				if (args.Length == 2 && string.Equals(args[0], "--summarize-bphcl-constraints", StringComparison.OrdinalIgnoreCase))
				{
					SummarizeBphclConstraints(NativeBphclDocument.Open(args[1]));
					return;
				}
				if (args.Length == 2 && string.Equals(args[0], "--inspect-conversion-constraints", StringComparison.OrdinalIgnoreCase))
				{
					Console.WriteLine(ConversionMatchAnalyzer.FormatConstraints(ConversionMatchAnalyzer.AnalyzeConstraints(args[1])));
					return;
				}
				if (args.Length == 2 && string.Equals(args[0], "--audit-collision-masks", StringComparison.OrdinalIgnoreCase))
				{
					Console.WriteLine(ConversionMatchAnalyzer.AnalyzeCollisionMasks(args[1]));
					return;
				}
				if (args.Length == 2 && string.Equals(args[0], "--audit-fresh-conversions", StringComparison.OrdinalIgnoreCase))
				{
					Console.WriteLine(ConversionMatchAnalyzer.FormatFreshExports(ConversionMatchAnalyzer.AnalyzeFreshExports(args[1])));
					return;
				}
				if (args.Length == 4 && string.Equals(args[0], "--diff-fresh-cloth", StringComparison.OrdinalIgnoreCase))
				{
					Console.WriteLine(ConversionMatchAnalyzer.DescribeFreshClothComparison(args[1], args[2], args[3]));
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-constraint-sets", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service = new HkclService();
				service.Load(args[1]);
				for (int clothIndex10 = 0; clothIndex10 < service.GetClothSummaries().Count; clothIndex10++)
				{
					foreach (ConstraintSetSummary set3 in service.GetConstraintSetSummaries(clothIndex10))
					{
						Console.WriteLine($"{clothIndex10}|{service.GetClothName(clothIndex10)}|{set3.Index}|{set3.ClassName}|{set3.Name}|id={set3.ConstraintId}|type={set3.ConstraintType}");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--dump-hkcl-bend-links", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service2 = new HkclService();
				service2.Load(args[1]);
				for (int clothIndex11 = 0; clothIndex11 < service2.GetClothSummaries().Count; clothIndex11++)
				{
					HashSet<int> bendSetIndices = (from constraintSetSummary in service2.GetConstraintSetSummaries(clothIndex11)
						where string.Equals(constraintSetSummary.ClassName, "hclBendLinkConstraintSet", StringComparison.Ordinal)
						select constraintSetSummary.Index).ToHashSet();
					if (bendSetIndices.Count == 0)
					{
						continue;
					}
					HashSet<(int Set, int Link)> emitted = new HashSet<(int, int)>();
					foreach (ParticleEditRow particle in service2.GetParticleRows(clothIndex11))
					{
						foreach (ParticleRelationshipRow link2 in from particleRelationshipRow in service2.GetParticleRelationships(clothIndex11, particle.Index)
							where bendSetIndices.Contains(particleRelationshipRow.ConstraintSetIndex) && particleRelationshipRow.LinkIndex >= 0 && emitted.Add((particleRelationshipRow.ConstraintSetIndex, particleRelationshipRow.LinkIndex))
							orderby particleRelationshipRow.ConstraintSetIndex, particleRelationshipRow.LinkIndex
							select particleRelationshipRow)
						{
							Console.WriteLine($"cloth={clothIndex11}|{service2.GetClothName(clothIndex11)}|set={link2.ConstraintSetIndex}|link={link2.LinkIndex}|a={link2.ParticleA}|b={link2.ParticleB}|min={link2.BendMinLength:G9}|max={link2.StretchMaxLength:G9}|bend={link2.BendStiffness:G9}|stretch={link2.StretchStiffness:G9}");
						}
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--dump-bphcl-vertex-routes", StringComparison.OrdinalIgnoreCase))
			{
				foreach (NativeBphclCloth cloth21 in NativeBphclDocument.Open(args[1]).Cloths)
				{
					foreach (NativeBphclOperatorLayout operator2 in cloth21.Operators.Where((NativeBphclOperatorLayout nativeBphclOperatorLayout) => string.Equals(nativeBphclOperatorLayout.ClassName, "hclGatherAllVerticesOperator", StringComparison.Ordinal) || string.Equals(nativeBphclOperatorLayout.ClassName, "hclCopyVerticesOperator", StringComparison.Ordinal)))
					{
						string map = ((operator2.VertexInputFromVertexOutput.Count == 0) ? string.Empty : string.Join(',', operator2.VertexInputFromVertexOutput));
						Console.WriteLine($"cloth={cloth21.Name}|index={operator2.Index}|class={operator2.ClassName}|name={operator2.Name}|input={operator2.InputBufferIndex}|output={operator2.OutputBufferIndex}|count={operator2.VertexCount}|startIn={operator2.StartVertexIn}|startOut={operator2.StartVertexOut}|map=[{map}]");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-bphysics", StringComparison.OrdinalIgnoreCase))
			{
				BphysicsDocument document30 = BphysicsService.Load(args[1]);
				Console.WriteLine($"UseCloth={document30.UseCloth};UseSupportBone={document30.UseSupportBone};HKCL={document30.HkclPath};SupportBone={document30.SupportBonePath};SubWind={document30.SubWindDirection.X:G7},{document30.SubWindDirection.Y:G7},{document30.SubWindDirection.Z:G7};Frequency={document30.SubWindFrequency:G7};Speed={document30.SubWindSpeed:G7}");
				{
					foreach (BphysicsClothProfile cloth22 in document30.Cloths)
					{
						Console.WriteLine($"{cloth22.Name}|{cloth22.BaseBone}|{cloth22.WindFrequency:G7}|{cloth22.WindDrag:G7}|{cloth22.WindMinSpeed:G7}|{cloth22.WindMaxSpeed:G7}|{cloth22.SubWindFactorMain:G7}|{cloth22.SubWindFactorAdd:G7}|{cloth22.WindEnabled}|{cloth22.WritebackToLocal}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-conversion-scales", StringComparison.OrdinalIgnoreCase))
			{
				HkclService hkclService15 = new HkclService();
				hkclService15.Load(args[1]);
				{
					foreach (BphclConversionScale scale in hkclService15.GetBphclConversionScaleSuggestions())
					{
						Console.WriteLine($"{scale.ClothIndex}|{scale.ClothName}|mass={scale.DefaultMassScale:G7}|constraints={scale.DefaultConstraintScale:G7}|{scale.SuggestionBasis}");
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-fresh-conversion", StringComparison.OrdinalIgnoreCase))
			{
				HkclService hkclService16 = new HkclService();
				hkclService16.Load(args[1]);
				HkclService fresh3 = hkclService16.CreateFreshHkclFromCurrentBphclDocument();
				{
					foreach (var cloth23 in fresh3.GetClothSummaries().Select((string item11, int item12) => (summary: item11, index: item12)))
					{
						IEnumerable<string> dynamicParticles2 = from particleEditRow in fresh3.GetParticleRows(cloth23.index)
							where !particleEditRow.Fixed
							select $"#{particleEditRow.Index}:mass={particleEditRow.Mass:G7},invMass={particleEditRow.InverseMass:G7}";
						Console.WriteLine($"{cloth23.index}|{cloth23.summary}|dynamic=[{string.Join(';', dynamicParticles2)}]");
						foreach (ClothSimulateConfigData config in fresh3.GetClothRuntimeData(cloth23.index).Operators.SelectMany((ClothOperatorData clothOperatorData) => clothOperatorData.SimulateConfigs))
						{
							Console.WriteLine($"{cloth23.index}|{cloth23.summary}|solver=sub={config.SubSteps},iter={config.NumberOfSolveIterations},adaptive={config.AdaptConstraintStiffness},allColliders={config.UseAllInstanceCollidables},selected=[{string.Join(',', config.InstanceCollidablesUsed)}],execution=[{string.Join(',', config.ConstraintExecution)}]");
						}
						HashSet<string> seenConstraints = new HashSet<string>(StringComparer.Ordinal);
						foreach (ParticleEditRow particle2 in fresh3.GetParticleRows(cloth23.index))
						{
							foreach (ParticleRelationshipRow relation in from particleRelationshipRow in fresh3.GetParticleRelationships(cloth23.index, particle2.Index)
								where particleRelationshipRow.IsEditable && (particleRelationshipRow.Stiffness.HasValue || particleRelationshipRow.BendStiffness.HasValue || particleRelationshipRow.StretchStiffness.HasValue)
								select particleRelationshipRow)
							{
								string key2 = $"{relation.ConstraintSetIndex}:{relation.LinkIndex}";
								if (seenConstraints.Add(key2))
								{
									Console.WriteLine($"{cloth23.index}|{cloth23.summary}|constraint={relation.Name}#{relation.LinkIndex}|stiff={relation.Stiffness?.ToString("G7") ?? "-"}|bend={relation.BendStiffness?.ToString("G7") ?? "-"}|stretch={relation.StretchStiffness?.ToString("G7") ?? "-"}");
								}
							}
						}
					}
					return;
				}
			}
			if (args.Length == 3 && string.Equals(args[0], "--export-fresh-hkcl", StringComparison.OrdinalIgnoreCase))
			{
				HkclService hkclService17 = new HkclService();
				hkclService17.Load(args[1]);
				hkclService17.CreateFreshHkclFromCurrentBphclDocument().SaveHkcl(args[2], HkclPlatform.WiiU);
				Console.WriteLine(args[2]);
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--export-raw-json", StringComparison.OrdinalIgnoreCase))
			{
				HkclService hkclService18 = new HkclService();
				hkclService18.Load(args[1]);
				hkclService18.ExportReadableJson(args[2]);
				Console.WriteLine(args[2]);
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--export-hkcl-to-bphcl", StringComparison.OrdinalIgnoreCase))
			{
				HkclService source8 = new HkclService();
				source8.Load(args[1]);
				if (source8.IsBphcl || source8.IsHelperBoneSidecar)
				{
					throw new InvalidOperationException("The source must be an HKCL file.");
				}
				StandaloneBphclDocumentGraph graph13 = HkclToBphclGameReadyGraphBuilder.Build(source8);
				File.WriteAllBytes(args[2], NativeBphclStandaloneGraphWriter.WriteGameReady(graph13));
				NativeBphclDocument verification12 = NativeBphclDocument.Open(args[2]);
				Console.WriteLine($"Exported {verification12.Cloths.Count} cloths to {args[2]}");
				return;
			}
			if (args.Length == 3 && string.Equals(args[0], "--audit-hkcl-bphcl-bones", StringComparison.OrdinalIgnoreCase))
			{
				BphclSourceBoneAudit.Write(args[1], args[2]);
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-runtime", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service3 = new HkclService();
				service3.Load(args[1]);
				{
					foreach (var (summary, clothIndex12) in service3.GetClothSummaries().Select((string item11, int item12) => (summary: item11, index: item12)))
					{
						foreach (ClothOperatorData operator3 in service3.GetClothRuntimeData(clothIndex12).Operators)
						{
							if (operator3.VertexParticlePairs.Count != 0 || operator3.SimulateConfigs.Count != 0)
							{
								string anchors = string.Join(',', operator3.VertexParticlePairs.Select((ClothVertexParticlePairData pair) => $"{pair.VertexIndex}:{pair.ParticleIndex}"));
								string configurations = string.Join(';', operator3.SimulateConfigs.Select((ClothSimulateConfigData clothSimulateConfigData) => $"sub={clothSimulateConfigData.SubSteps},iter={clothSimulateConfigData.NumberOfSolveIterations},adaptive={clothSimulateConfigData.AdaptConstraintStiffness},all={clothSimulateConfigData.UseAllInstanceCollidables},colliders=[{string.Join(',', clothSimulateConfigData.InstanceCollidablesUsed)}],constraints=[{string.Join(',', clothSimulateConfigData.ConstraintExecution)}]"));
								Console.WriteLine($"{clothIndex12}|{summary}|op={operator3.Index}:{operator3.ClassName}|sim={operator3.SimulationClothIndex}|ref={operator3.ReferenceBufferIndex}|anchors=[{anchors}]|{configurations}");
							}
						}
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-runtime-full", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service4 = new HkclService();
				service4.Load(args[1]);
				{
					foreach (var item25 in service4.GetClothSummaries().Select((string item11, int item12) => (summary: item11, index: item12)))
					{
						string summary2 = item25.summary;
						int clothIndex13 = item25.index;
						ClothRuntimeData runtime2 = service4.GetClothRuntimeData(clothIndex13);
						Console.WriteLine($"{clothIndex13}|{summary2}");
						Console.WriteLine("  buffers=" + string.Join(';', runtime2.Buffers.Select((ClothBufferDefinitionData clothBufferDefinitionData) => $"#{clothBufferDefinitionData.Index}:{clothBufferDefinitionData.ClassName}:v{clothBufferDefinitionData.VertexCount}:t{clothBufferDefinitionData.TriangleCount}")));
						Console.WriteLine("  ops=" + string.Join(';', runtime2.Operators.Select((ClothOperatorData clothOperatorData) => $"#{clothOperatorData.Index}:{clothOperatorData.ClassName}:in{clothOperatorData.InputBufferIndex}:out{clothOperatorData.OutputBufferIndex}:set{clothOperatorData.TransformSetIndex}:sim{clothOperatorData.SimulationClothIndex}:ref{clothOperatorData.ReferenceBufferIndex}:vp{clothOperatorData.VertexParticlePairs.Count}:cfg{clothOperatorData.SimulateConfigs.Count}")));
						Console.WriteLine("  states=" + string.Join(';', runtime2.States.Select((ClothStateRuntimeData clothStateRuntimeData) => $"#{clothStateRuntimeData.Index}:{clothStateRuntimeData.Name}:ops[{string.Join(',', clothStateRuntimeData.OperatorIndices)}]:buf[{string.Join(',', clothStateRuntimeData.BufferIndices)}]:set[{string.Join(',', clothStateRuntimeData.TransformSetIndices)}]:sim[{string.Join(',', clothStateRuntimeData.SimulationIndices)}]")));
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-simulation-settings", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service5 = new HkclService();
				service5.Load(args[1]);
				IReadOnlyList<string> cloths = service5.GetClothSummaries();
				for (int clothIndex14 = 0; clothIndex14 < cloths.Count; clothIndex14++)
				{
					ClothSimulationSettings settings = service5.GetSimulationSettings(clothIndex14);
					Console.WriteLine($"{clothIndex14}|{cloths[clothIndex14]}|gravity={settings.GravityX:G7},{settings.GravityY:G7},{settings.GravityZ:G7}|damping={settings.DampingPerSecond:G7}|collisionTolerance={settings.CollisionTolerance:G7}|translationTransfer={settings.TransferTranslationMotion}:{settings.MinTranslationSpeed:G7}-{settings.MaxTranslationSpeed:G7}:{settings.MinTranslationBlend:G7}-{settings.MaxTranslationBlend:G7}|rotationTransfer={settings.TransferRotationMotion}:{settings.MinRotationSpeed:G7}-{settings.MaxRotationSpeed:G7}:{settings.MinRotationBlend:G7}-{settings.MaxRotationBlend:G7}");
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-plane-colliders", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service6 = new HkclService();
				service6.Load(args[1]);
				IReadOnlyList<string> cloths2 = service6.GetClothSummaries();
				for (int clothIndex15 = 0; clothIndex15 < cloths2.Count; clothIndex15++)
				{
					IReadOnlyList<ParticleColliderOption> options = service6.GetParticleColliderOptions(clothIndex15);
					IReadOnlyList<ParticleEditRow> particles = service6.GetParticleRows(clothIndex15);
					foreach (ColliderEditRow plane in from row in service6.GetColliderRows(clothIndex15)
						where row.IsPlane
						select row)
					{
						ParticleColliderOption option = options.FirstOrDefault((ParticleColliderOption particleColliderOption) => particleColliderOption.ColliderIndex == plane.Index);
						int[] affected = ((option == null || option.BitIndex >= 31) ? Array.Empty<int>() : (from particleEditRow in particles
							where (particleEditRow.CollisionMask & (uint)(1 << option.BitIndex)) != 0
							select particleEditRow.Index).ToArray());
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(46, 12);
						defaultInterpolatedStringHandler.AppendFormatted(clothIndex15);
						defaultInterpolatedStringHandler.AppendLiteral("|");
						defaultInterpolatedStringHandler.AppendFormatted(service6.GetClothName(clothIndex15));
						defaultInterpolatedStringHandler.AppendLiteral("|plane=");
						defaultInterpolatedStringHandler.AppendFormatted(plane.Index);
						defaultInterpolatedStringHandler.AppendLiteral(":");
						defaultInterpolatedStringHandler.AppendFormatted(plane.Name);
						defaultInterpolatedStringHandler.AppendLiteral("|");
						defaultInterpolatedStringHandler.AppendLiteral("slot=");
						defaultInterpolatedStringHandler.AppendFormatted((option == null) ? "none" : ((object)option.BitIndex));
						defaultInterpolatedStringHandler.AppendLiteral("|affected=[");
						defaultInterpolatedStringHandler.AppendFormatted(string.Join(',', affected));
						defaultInterpolatedStringHandler.AppendLiteral("]|");
						defaultInterpolatedStringHandler.AppendLiteral("point=");
						defaultInterpolatedStringHandler.AppendFormatted(plane.StartX, "G6");
						defaultInterpolatedStringHandler.AppendLiteral(",");
						defaultInterpolatedStringHandler.AppendFormatted(plane.StartY, "G6");
						defaultInterpolatedStringHandler.AppendLiteral(",");
						defaultInterpolatedStringHandler.AppendFormatted(plane.StartZ, "G6");
						defaultInterpolatedStringHandler.AppendLiteral("|");
						defaultInterpolatedStringHandler.AppendLiteral("normal=");
						defaultInterpolatedStringHandler.AppendFormatted(plane.PlaneNormalX, "G6");
						defaultInterpolatedStringHandler.AppendLiteral(",");
						defaultInterpolatedStringHandler.AppendFormatted(plane.PlaneNormalY, "G6");
						defaultInterpolatedStringHandler.AppendLiteral(",");
						defaultInterpolatedStringHandler.AppendFormatted(plane.PlaneNormalZ, "G6");
						Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-collider-masks", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service7 = new HkclService();
				service7.Load(args[1]);
				IReadOnlyList<string> cloths3 = service7.GetClothSummaries();
				for (int clothIndex16 = 0; clothIndex16 < cloths3.Count; clothIndex16++)
				{
					IReadOnlyList<ParticleEditRow> particles2 = service7.GetParticleRows(clothIndex16);
					foreach (ParticleColliderOption option2 in service7.GetParticleColliderOptions(clothIndex16))
					{
						int[] affected2 = ((option2.BitIndex >= 31) ? Array.Empty<int>() : (from particleEditRow in particles2
							where (particleEditRow.CollisionMask & (uint)(1 << option2.BitIndex)) != 0
							select particleEditRow.Index).ToArray());
						Console.WriteLine($"{clothIndex16}|{service7.GetClothName(clothIndex16)}|slot={option2.BitIndex}|collider={option2.ColliderIndex}:{option2.Name}|affected=[{string.Join(',', affected2)}]");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-collision-state", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service8 = new HkclService();
				service8.Load(args[1]);
				for (int clothIndex17 = 0; clothIndex17 < service8.GetClothSummaries().Count; clothIndex17++)
				{
					foreach (string line in service8.GetCollisionDiagnostics(clothIndex17))
					{
						Console.WriteLine($"{clothIndex17}|{line}");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-hkcl-outer-colliders", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service9 = new HkclService();
				service9.Load(args[1]);
				ColliderEditRow[] outerColliders = (from colliderEditRow in service9.GetAllColliderRows()
					orderby colliderEditRow.Index
					select colliderEditRow).ToArray();
				Dictionary<int, string> namesByIndex = outerColliders.ToDictionary((ColliderEditRow colliderEditRow) => colliderEditRow.Index, (ColliderEditRow colliderEditRow) => colliderEditRow.Name);
				Console.WriteLine($"outer|count={outerColliders.Length}");
				ColliderEditRow[] array6 = outerColliders;
				foreach (ColliderEditRow collider2 in array6)
				{
					Console.WriteLine($"  collider|{collider2.Index}|{collider2.Name}|{collider2.ShapeType}|bone={collider2.BoneIndex}:{collider2.BoneName}");
				}
				for (int clothIndex18 = 0; clothIndex18 < service9.GetClothSummaries().Count; clothIndex18++)
				{
					IEnumerable<string> active = service9.GetStandaloneColliderBindings(clothIndex18).Select(delegate(StandaloneBphclColliderBinding binding)
					{
						string valueOrDefault = namesByIndex.GetValueOrDefault(binding.ColliderIndex, "<missing>");
						return $"{binding.ColliderIndex}:{valueOrDefault}@bone{binding.BoneIndex}";
					});
					Console.WriteLine($"cloth|{clothIndex18}|{service9.GetClothName(clothIndex18)}|active=[{string.Join(',', active)}]");
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-collider-shapes", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service10 = new HkclService();
				service10.Load(args[1]);
				for (int clothIndex19 = 0; clothIndex19 < service10.GetClothSummaries().Count; clothIndex19++)
				{
					foreach (ColliderEditRow collider3 in service10.GetColliderRows(clothIndex19))
					{
						Console.WriteLine($"{clothIndex19}|{service10.GetClothName(clothIndex19)}|{collider3.Index}|{collider3.Name}|{collider3.ShapeType}|bone={collider3.BoneIndex}|radius={collider3.Radius:G8}|start={collider3.StartX:G8},{collider3.StartY:G8},{collider3.StartZ:G8}|end={collider3.EndX:G8},{collider3.EndY:G8},{collider3.EndZ:G8}");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-local-ranges", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service11 = new HkclService();
				service11.Load(args[1]);
				for (int clothIndex20 = 0; clothIndex20 < service11.GetClothSummaries().Count; clothIndex20++)
				{
					var preview = service11.GetParticlePreview(clothIndex20);
					foreach (var range in preview.LocalRanges)
					{
						Console.WriteLine(
							$"{clothIndex20}|{service11.GetClothName(clothIndex20)}|particle={range.ParticleIndex}|set={range.ConstraintSetIndex}|referenceVertex={range.ReferenceVertex}|referenceBuffer={range.ReferenceBufferIndex}|weights={range.ReferenceBoneWeights.Count}|radius={range.MaximumDistance:G8}|minNormal={range.MinNormalDistance:G8}|maxNormal={range.MaxNormalDistance:G8}|stiffness={range.Stiffness:G8}|applyNormal={range.ApplyNormalComponent}");
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-particle-relationships", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service12 = new HkclService();
				service12.Load(args[1]);
				for (int clothIndex21 = 0; clothIndex21 < service12.GetClothSummaries().Count; clothIndex21++)
				{
					int particleCount2 = service12.GetParticleRows(clothIndex21).Count;
					for (int particleIndex2 = 0; particleIndex2 < particleCount2; particleIndex2++)
					{
						foreach (ParticleRelationshipRow relationship2 in service12.GetParticleRelationships(clothIndex21, particleIndex2))
						{
							Console.WriteLine($"{clothIndex21}|{service12.GetClothName(clothIndex21)}|particle={particleIndex2}|{relationship2.Kind}:{relationship2.Name}|other={relationship2.Particles}|{relationship2.Details}");
						}
					}
				}
				return;
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-collider-bindings", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service13 = new HkclService();
				service13.Load(args[1]);
				{
					foreach (var cloth24 in service13.GetClothSummaries().Select((string item11, int item12) => (summary: item11, index: item12)))
					{
						foreach (string line2 in service13.GetColliderBindingDiagnostics(cloth24.index))
						{
							Console.WriteLine($"{cloth24.index}|{cloth24.summary}|{line2}");
						}
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--validate-collider-roundtrip", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service14 = new HkclService();
				service14.Load(args[1]);
				for (int clothIndex22 = 0; clothIndex22 < service14.GetClothSummaries().Count; clothIndex22++)
				{
					service14.UpdateColliderRows(service14.GetColliderRows(clothIndex22));
				}
				{
					foreach (var cloth25 in service14.GetClothSummaries().Select((string item11, int item12) => (summary: item11, index: item12)))
					{
						foreach (string line3 in service14.GetColliderBindingDiagnostics(cloth25.index))
						{
							Console.WriteLine($"{cloth25.index}|{cloth25.summary}|{line3}");
						}
					}
					return;
				}
			}
			if (args.Length == 2 && string.Equals(args[0], "--inspect-runtime-graph", StringComparison.OrdinalIgnoreCase))
			{
				HkclService service15 = new HkclService();
				service15.Load(args[1]);
				{
					foreach (var item26 in service15.GetClothSummaries().Select((string item11, int item12) => (summary: item11, index: item12)))
					{
						string summary3 = item26.summary;
						int clothIndex23 = item26.index;
						ClothRuntimeData runtime3 = service15.GetClothRuntimeData(clothIndex23);
						Console.WriteLine($"cloth|{clothIndex23}|{summary3}|buffers={runtime3.Buffers.Count}|sets={runtime3.TransformSets.Count}|ops={runtime3.Operators.Count}|states={runtime3.States.Count}");
						foreach (ClothBufferDefinitionData buffer2 in runtime3.Buffers)
						{
							Console.WriteLine($"  buffer|{buffer2.Index}|{buffer2.ClassName}|mesh={buffer2.MeshName}|name={buffer2.BufferName}|type={buffer2.Type}/{buffer2.SubType}|vertices={buffer2.VertexCount}|triangles={buffer2.TriangleCount}");
						}
						foreach (ClothTransformSetDefinitionData transformSet in runtime3.TransformSets)
						{
							Console.WriteLine($"  set|{transformSet.Index}|{transformSet.Name}|type={transformSet.Type}|transforms={transformSet.TransformCount}");
						}
						foreach (ClothOperatorData op3 in runtime3.Operators)
						{
							Console.WriteLine($"  op|{op3.Index}|{op3.ClassName}|in={op3.InputBufferIndex}|out={op3.OutputBufferIndex}|set={op3.TransformSetIndex}|sim={op3.SimulationClothIndex}|ref={op3.ReferenceBufferIndex}|vertices={op3.VertexCount}|pairs={string.Join(',', op3.VertexParticlePairs.Select((ClothVertexParticlePairData pair) => $"{pair.VertexIndex}>{pair.ParticleIndex}"))}");
							foreach (var config2 in op3.SimulateConfigs.Select((ClothSimulateConfigData item11, int item12) => (config: item11, index: item12)))
							{
								Console.WriteLine($"    simconfig|{config2.index}|constraints=[{string.Join(',', config2.config.ConstraintExecution)}]|colliders=[{string.Join(',', config2.config.InstanceCollidablesUsed)}]|substeps={config2.config.SubSteps}|iterations={config2.config.NumberOfSolveIterations}|all={config2.config.UseAllInstanceCollidables}|adaptive={config2.config.AdaptConstraintStiffness}");
							}
						}
						foreach (ClothStateRuntimeData state3 in runtime3.States)
						{
							string buffers2 = string.Join(',', state3.BufferAccesses?.Select((ClothStateBufferAccessData access) => $"{access.BufferIndex}/{access.ShadowBufferIndex}:{access.ComponentFlags0},{access.ComponentFlags1},{access.ComponentFlags2},{access.ComponentFlags3}:tri{access.TrianglesRead}") ?? Array.Empty<string>());
							string sets = string.Join(',', state3.TransformSetAccesses?.Select((ClothStateTransformSetAccessData access) => $"{access.TransformSetIndex}:{access.ComponentFlags0},{access.ComponentFlags1}:track{access.TransformTrackers.Count}") ?? Array.Empty<string>());
							Console.WriteLine($"  state|{state3.Index}|{state3.Name}|ops=[{string.Join(',', state3.OperatorIndices)}]|buffers=[{buffers2}]|sets=[{sets}]|sim=[{string.Join(',', state3.SimulationIndices)}]");
						}
					}
					return;
				}
			}
			AppLocalization.Initialize();
			ApplicationConfiguration.Initialize();
			Application.Run(new MainForm());
			string DescribeBitField(uint num2)
			{
				uint numBits = ReadUInt33(num2 + 16);
				if (document20.TryGetReferencedItem(num2, out var wordsItemIndex))
				{
					NativeBphclItem words = document20.GetItem(wordsItemIndex);
					IEnumerable<string> values4 = from num3 in Enumerable.Range(0, checked((int)words.Count))
						select ReadUInt33(words.DataOffset + checked((uint)(num3 * 4))) into num3
						select num3.ToString("X8");
					return $"{numBits}:0x{string.Join(",", values4)}";
				}
				return $"{numBits}:<null>";
			}
			string DescribeBuffers(uint dataOffset)
			{
				if (!document19.TryGetReferencedItem(dataOffset, out var storageIndex))
				{
					return "[]";
				}
				NativeBphclItem storage3 = document19.GetItem(storageIndex);
				IEnumerable<string> values4 = Enumerable.Range(0, checked((int)storage3.Count)).Select(delegate(int num3)
				{
					uint num2 = storage3.DataOffset + checked((uint)(num3 * 16));
					return $"{ReadUInt32(num2)}:{ReadByte(num2 + 4)}/{ReadByte(num2 + 5)}/{ReadByte(num2 + 6)}/{ReadByte(num2 + 7)}:tri{ReadByte(num2 + 8)}:shadow{ReadUInt32(num2 + 12)}";
				});
				return "[" + string.Join(';', values4) + "]";
			}
			string DescribeTransformSets(uint dataOffset)
			{
				if (!document19.TryGetReferencedItem(dataOffset, out var storageIndex))
				{
					return "[]";
				}
				NativeBphclItem storage3 = document19.GetItem(storageIndex);
				IEnumerable<string> values4 = Enumerable.Range(0, checked((int)storage3.Count)).Select(delegate(int num3)
				{
					uint num2 = storage3.DataOffset + checked((uint)(num3 * 32));
					return $"{ReadUInt32(num2)}:{ReadByte(num2 + 8)}/{ReadByte(num2 + 9)}:trackers{ReadUInt32(num2 + 24)}";
				});
				return "[" + string.Join(';', values4) + "]";
			}
			byte ReadByte(uint num2)
			{
				return document19.Bytes[dataSection2.PayloadOffset + checked((int)num2)];
			}
			IReadOnlyList<int> ReadIntArray(uint dataOffset)
			{
				if (!document24.TryGetReferencedItem(dataOffset, out var arrayItemIndex))
				{
					return Array.Empty<int>();
				}
				NativeBphclItem array7 = document24.GetItem(arrayItemIndex);
				return (from num2 in Enumerable.Range(0, checked((int)array7.Count))
					select (int)ReadUInt36(array7.DataOffset + checked((uint)(num2 * 4)))).ToArray();
			}
			IReadOnlyList<IReadOnlyList<int>> ReadNestedIntArrays(uint dataOffset)
			{
				if (!document24.TryGetReferencedItem(dataOffset, out var arrayItemIndex))
				{
					return Array.Empty<IReadOnlyList<int>>();
				}
				NativeBphclItem array7 = document24.GetItem(arrayItemIndex);
				return (from num2 in Enumerable.Range(0, checked((int)array7.Count))
					select ReadIntArray(array7.DataOffset + checked((uint)num2) * 16)).ToArray();
			}
			string ReadString(uint dataOffset)
			{
				if (!document19.TryGetReferencedItem(dataOffset, out var stringItemIndex))
				{
					return string.Empty;
				}
				NativeBphclItem item11 = document19.GetItem(stringItemIndex);
				int start5 = dataSection2.PayloadOffset + checked((int)item11.DataOffset);
				int end2 = Array.IndexOf(document19.Bytes, (byte)0, start5);
				if (end2 >= start5)
				{
					return Encoding.UTF8.GetString(document19.Bytes, start5, end2 - start5);
				}
				return string.Empty;
			}
			uint ReadUInt(uint num2)
			{
				return BitConverter.ToUInt32(document9.Bytes, payloadOffset + checked((int)num2));
			}
			uint ReadUInt32(uint num2)
			{
				return BitConverter.ToUInt32(document19.Bytes, dataSection2.PayloadOffset + checked((int)num2));
			}
			uint ReadUInt33(uint num2)
			{
				return BitConverter.ToUInt32(document20.Bytes, data7.PayloadOffset + checked((int)num2));
			}
			uint ReadUInt34(uint num2)
			{
				return BitConverter.ToUInt32(document22.Bytes, payloadOffset3 + checked((int)num2));
			}
			uint ReadUInt35(uint num2)
			{
				return BitConverter.ToUInt32(document23.Bytes, payloadOffset4 + checked((int)num2));
			}
			uint ReadUInt36(uint num2)
			{
				return BitConverter.ToUInt32(document24.Bytes, document24.DataSection.PayloadOffset + checked((int)num2));
			}
			uint ReadUInt37(uint num2)
			{
				return BitConverter.ToUInt32(document25.Bytes, data9.PayloadOffset + checked((int)num2));
			}
			ushort ReadUInt38(uint num2)
			{
				return BitConverter.ToUInt16(document25.Bytes, data9.PayloadOffset + checked((int)num2));
			}
		}
		catch (Exception ex2)
		{
			if (args.Length != 0)
			{
				var message = UserFacingExceptionFormatter.Format(ex2);
				File.WriteAllText(Path.Combine(Path.GetTempPath(), "PhysicsTool-command-error.txt"), message);
				Console.Error.WriteLine(message);
				Environment.ExitCode = 1;
			}
			else
			{
				MessageBox.Show(UserFacingExceptionFormatter.Format(ex2), "PhysicsTool startup error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
		static string ClothKey(string text)
		{
			if (!text.StartsWith("Link:", StringComparison.Ordinal))
			{
				return text;
			}
			return text.Substring(5);
		}
		static string Describe(NativeBphclDocument nativeBphclDocument3, NativeBphclCloth nativeBphclCloth)
		{
			NativeBphclSkeleton skeleton8 = nativeBphclDocument3.Skeletons.ElementAtOrDefault(nativeBphclCloth.Index) ?? throw new InvalidDataException("Cloth '" + nativeBphclCloth.Name + "' has no paired skeleton.");
			IReadOnlyList<int> closure6 = nativeBphclDocument3.CollectItemClosure(new int[2] { nativeBphclCloth.ItemIndex, skeleton8.ItemIndex });
			return string.Join(";", from grouping in (from index9 in closure6
					group index9 by nativeBphclDocument3.GetTypeName(nativeBphclDocument3.GetItem(index9).TypeIndex) ?? $"type_{nativeBphclDocument3.GetItem(index9).TypeIndex}").OrderBy<IGrouping<string, int>, string>((IGrouping<string, int> grouping) => grouping.Key, StringComparer.Ordinal)
				select $"{grouping.Key}={grouping.Count()}");
		}
		static string DescribeValue(AampReadParameter parameter)
		{
			switch (parameter.Type)
			{
			case 0:
				return parameter.AsBoolean().ToString();
			case 1:
				return parameter.AsSingle().ToString("G9");
			case 2:
				return parameter.AsInt32().ToString();
			case 7:
			case 8:
			case 15:
			case 20:
				return parameter.AsString();
			default:
				return $"type={parameter.Type}, bytes={Convert.ToHexString(parameter.Bytes)}";
			}
		}
		static void DumpBlend(string value2, IReadOnlyList<NativeBphclPackedArray> source9)
		{
			foreach (var (array7, index9) in source9.Select((NativeBphclPackedArray item11, int item12) => (array: item11, index: item12)))
			{
				Console.WriteLine($"  blend|{value2}|{index9}|type={array7.ElementType}|count={array7.Count}|stride={array7.ElementSize}|bytes={array7.Bytes.Count}");
				for (int blockIndex2 = 0; blockIndex2 < array7.Count; blockIndex2++)
				{
					int offset9 = checked(blockIndex2 * array7.ElementSize);
					int length2 = Math.Min(array7.ElementSize, array7.Bytes.Count - offset9);
					string preview = Convert.ToHexString(array7.Bytes.Skip(offset9).Take(length2).ToArray());
					Console.WriteLine($"    block|{blockIndex2}|hex={preview}");
				}
			}
		}
		static void DumpRecords(string label, IReadOnlyList<NativeBphclConstraintLink> records)
		{
			if (records.Count == 0)
			{
				return;
			}
			foreach (NativeBphclConstraintLink record in records)
			{
				string particles3 = ((record.ParticleA.HasValue || record.ParticleB.HasValue) ? (" A=" + (record.ParticleA?.ToString() ?? "-") + " B=" + (record.ParticleB?.ToString() ?? "-")) : string.Empty);
				Console.WriteLine($"    {label}[{record.Index}]{particles3} | {string.Join(", ", record.Values.Select<KeyValuePair<string, double>, string>((KeyValuePair<string, double> keyValuePair) => $"{keyValuePair.Key}={keyValuePair.Value:G8}"))}");
			}
		}
		static string Format(Vector3 position)
		{
			return $"{position.X:G9},{position.Y:G9},{position.Z:G9}";
		}
		static string FormatBits(NativeBphclBitField bits)
		{
			return $"{bits.NumBits}:0x{string.Join(",", bits.Words.Select((uint word) => word.ToString("X8")))}";
		}
		static string FormatNestedIntArrays(IReadOnlyList<IReadOnlyList<int>> source9)
		{
			return "[" + string.Join(',', source9.Select((IReadOnlyList<int> values4) => "[" + string.Join(',', values4) + "]")) + "]";
		}
		static string ItemKey(NativeBphclDocument nativeBphclDocument3, NativeBphclItem nativeBphclItem)
		{
			return $"{nativeBphclDocument3.GetTypeName(nativeBphclItem.TypeIndex)}|count={nativeBphclItem.Count}";
		}
		static string Key(string text)
		{
			if (!text.StartsWith("Link:", StringComparison.Ordinal))
			{
				return text;
			}
			return text.Substring(5);
		}
		static string Key2(string text)
		{
			if (!text.StartsWith("Link:", StringComparison.Ordinal))
			{
				return text;
			}
			return text.Substring(5);
		}
		static HashSet<uint> PointerBytes(NativeBphclDocument nativeBphclDocument3)
		{
			return nativeBphclDocument3.InternalPatches.SelectMany((NativeBphclPatch nativeBphclPatch) => nativeBphclPatch.Offsets).SelectMany((uint num2) => from byteOffset in Enumerable.Range(0, 4)
				select num2 + (uint)byteOffset).ToHashSet();
		}
		static void RewriteReferenceOrder(byte[] array7, NativeBphclReferenceArray nativeBphclReferenceArray, IReadOnlyList<int> itemIndices)
		{
			if (nativeBphclReferenceArray.StorageItem.Count != itemIndices.Count)
			{
				throw new InvalidDataException("The aligned BPHCL reference-array count does not match its existing storage.");
			}
			for (int i = 0; i < itemIndices.Count; i++)
			{
				checked
				{
					BitConverter.GetBytes((uint)itemIndices[i]).CopyTo(array7, (int)nativeBphclReferenceArray.StorageItem.DataOffset + i * 8);
				}
			}
		}
	}

	private static void InspectSkinLayout(string filePath, string clothSelector)
	{
		HkclService service = new HkclService();
		service.Load(filePath);
		IReadOnlyList<string> cloths = service.GetClothSummaries();
		bool isIndex = int.TryParse(clothSelector, out var clothIndex);
		if (!isIndex)
		{
			clothIndex = Enumerable.Range(0, cloths.Count).FirstOrDefault((int index) => cloths[index].Contains(clothSelector, StringComparison.OrdinalIgnoreCase));
		}
		if ((uint)clothIndex >= (uint)cloths.Count || (!isIndex && !cloths[clothIndex].Contains(clothSelector, StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidDataException("No cloth matches '" + clothSelector + "'.");
		}
		ClothRuntimeData runtime = service.GetClothRuntimeData(clothIndex);
		Console.WriteLine($"cloth|{clothIndex}|{cloths[clothIndex]}");
		Console.WriteLine("buffers|" + string.Join(';', runtime.Buffers.Select((ClothBufferDefinitionData buffer) => $"{buffer.Index}:{buffer.VertexCount}:{buffer.BufferName}")));
		foreach (ClothOperatorData op in runtime.Operators.OrderBy((ClothOperatorData @operator) => @operator.Index))
		{
			Console.WriteLine($"op|{op.Index}|{op.ClassName}|in={op.InputBufferIndex}|out={op.OutputBufferIndex}|vertices={op.VertexCount}|start={op.StartVertexIn}>{op.StartVertexOut}|gather={string.Join(',', op.VertexInputFromVertexOutput)}");
		}
		foreach (ClothOperatorData op2 in runtime.Operators.Where((ClothOperatorData @operator) => @operator.VertexParticlePairs.Count > 0))
		{
			Console.WriteLine($"move|op={op2.Index}|pairs={string.Join(',', op2.VertexParticlePairs.Select((ClothVertexParticlePairData pair) => $"{pair.VertexIndex}>{pair.ParticleIndex}"))}");
		}
		foreach (SkinOperatorData skin in from skinOperatorData in service.GetSkinOperatorData(clothIndex)
			orderby skinOperatorData.OperatorIndex
			select skinOperatorData)
		{
			Console.WriteLine($"skin|op={skin.OperatorIndex}|{skin.ClassName}|range={skin.StartVertexIndex}-{skin.EndVertexIndex}|local={skin.LocalPositionBlocks.Count}/{skin.LocalUnpackedPositionBlocks.Count}|localSlots={string.Join(',', skin.LocalPositionBlocks.Select((SkinPositionBlockData skinPositionBlockData) => skinPositionBlockData.Positions.Count))}/{string.Join(',', skin.LocalUnpackedPositionBlocks.Select((SkinPositionBlockData skinPositionBlockData) => skinPositionBlockData.Positions.Count))}|packed={string.Join(',', skin.LocalPositionBlocks.Select((SkinPositionBlockData skinPositionBlockData) => skinPositionBlockData.NativePackedValues?.Count ?? 0))}");
			Console.WriteLine($"skinrefs|op={skin.OperatorIndex}|transformSet={skin.TransformSetIndex}|subset={string.Join(',', skin.TransformSubset)}|controls={string.Join(',', skin.ControlBytes)}|boneTransforms={skin.BoneFromSkinMeshTransforms.Count}");
			foreach (var item in skin.LocalPositionBlocks.Select((SkinPositionBlockData value, int index) => (value: value, index: index)))
			{
				SkinPositionBlockData block = item.value;
				int blockIndex = item.index;
				IReadOnlyList<short> packed = block.NativePackedValues;
				if (packed != null && packed.Count > 0)
				{
					Console.WriteLine($"packedPositions|op={skin.OperatorIndex}|block={blockIndex}|values={string.Join(',', packed)}");
				}
			}
			foreach (SkinBlendArrayData blend in skin.BlendArrays.Where((SkinBlendArrayData skinBlendArrayData) => skinBlendArrayData.Blocks.Count > 0))
			{
				Console.WriteLine($"blendmeta|{blend.MemberName}|blendCount={blend.BlendCount}|vertices={blend.VertexCount}|bones={blend.BoneCount}|blocks={blend.Blocks.Count}");
				for (int blockIndex2 = 0; blockIndex2 < blend.Blocks.Count; blockIndex2++)
				{
					SkinBlendBlockData block2 = blend.Blocks[blockIndex2];
					Console.WriteLine($"blend|{blend.MemberName}|block={blockIndex2}|vertices={string.Join(',', block2.VertexIndices)}|bones={string.Join(',', block2.BoneIndices)}|weights={string.Join(',', block2.BoneWeights)}");
				}
			}
		}
	}

	private static void CompareSkinPositionSlots(string sourcePath, string sourceCloth, string targetPath, string targetCloth)
	{
		(string Label, IReadOnlyList<Vector4> Positions) source = ReadSkinSlots(sourcePath, sourceCloth);
		(string Label, IReadOnlyList<Vector4> Positions) target = ReadSkinSlots(targetPath, targetCloth);
		Console.WriteLine($"source|{source.Label}|slots={source.Positions.Count}");
		Console.WriteLine($"target|{target.Label}|slots={target.Positions.Count}");
		int targetIndex;
		for (targetIndex = 0; targetIndex < target.Positions.Count; targetIndex++)
		{
			var candidate = (from sourceIndex in Enumerable.Range(0, source.Positions.Count)
				select new
				{
					sourceIndex = sourceIndex,
					distance = Vector3.Distance(ToVector3(target.Positions[targetIndex]), ToVector3(source.Positions[sourceIndex]))
				} into match
				orderby match.distance
				select match).First();
			Console.WriteLine($"map|target={targetIndex}|source={candidate.sourceIndex}|distance={candidate.distance:R}");
		}
	}

	private static (string Label, IReadOnlyList<Vector4> Positions) ReadSkinSlots(string path, string selector)
	{
		HkclService service = new HkclService();
		service.Load(path);
		IReadOnlyList<string> cloths = service.GetClothSummaries();
		bool isIndex = int.TryParse(selector, out var clothIndex);
		if (!isIndex)
		{
			clothIndex = Enumerable.Range(0, cloths.Count).FirstOrDefault((int index) => cloths[index].Contains(selector, StringComparison.OrdinalIgnoreCase));
		}
		if ((uint)clothIndex >= (uint)cloths.Count || (!isIndex && !cloths[clothIndex].Contains(selector, StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidDataException("No cloth matches '" + selector + "'.");
		}
		SkinOperatorData skin = service.GetSkinOperatorData(clothIndex).FirstOrDefault((SkinOperatorData value) => string.Equals(value.ClassName, "hclObjectSpaceSkinPOperator", StringComparison.Ordinal));
		if ((object)skin == null)
		{
			throw new InvalidDataException("Cloth '" + cloths[clothIndex] + "' has no object-space skin operator.");
		}
		Vector4[] slots = (skin.LocalPositionBlocks.Any(delegate(SkinPositionBlockData block)
		{
			if (block.Positions.Count <= 0)
			{
				IReadOnlyList<short> nativePackedValues = block.NativePackedValues;
				if (nativePackedValues != null)
				{
					return nativePackedValues.Count > 0;
				}
				return false;
			}
			return true;
		}) ? skin.LocalPositionBlocks : skin.LocalUnpackedPositionBlocks).SelectMany(DecodeSkinPositions).ToArray();
		return (Label: cloths[clothIndex], Positions: slots);
	}

	private static IEnumerable<Vector4> DecodeSkinPositions(SkinPositionBlockData block)
	{
		if (block.Positions.Count > 0)
		{
			return block.Positions;
		}
		IReadOnlyList<short> values = block.NativePackedValues;
		if (values == null || values.Count != 64)
		{
			return Array.Empty<Vector4>();
		}
		return Enumerable.Range(0, 16).Select(delegate(int index)
		{
			short num = values[index * 4 + 3];
			float num2 = MathF.Pow(2f, (float)num / 128f - 111f);
			return new Vector4((float)values[index * 4] * num2, (float)values[index * 4 + 1] * num2, (float)values[index * 4 + 2] * num2, 0f);
		});
	}

	private static Vector3 ToVector3(Vector4 value)
	{
		return new Vector3(value.X, value.Y, value.Z);
	}

	private static void InspectHkclConstraintProgram(string path)
	{
		HkclService service = new HkclService();
		service.Load(path);
		foreach (var (clothName, clothIndex) in service.GetClothSummaries().Select((string name, int index) => (name: name, index: index)))
		{
			Console.WriteLine($"cloth|{clothIndex}|{clothName}");
			foreach (ConstraintSetSummary set in from constraintSetSummary in service.GetConstraintSetSummaries(clothIndex)
				orderby constraintSetSummary.Index
				select constraintSetSummary)
			{
				Console.WriteLine($"  set|{set.Index}|{set.ClassName}|id={set.ConstraintId}|type={set.ConstraintType}");
			}
			foreach (ClothOperatorData @operator in service.GetClothRuntimeData(clothIndex).Operators)
			{
				foreach (var (config, configIndex) in @operator.SimulateConfigs.Select((ClothSimulateConfigData item, int index) => (config: item, index: index)))
				{
					Console.WriteLine($"  simulate|{@operator.Index}:{configIndex}|execution=[{string.Join(',', config.ConstraintExecution)}]|substeps={config.SubSteps}|iterations={config.NumberOfSolveIterations}");
				}
			}
		}
	}

	private static void InspectBphclConstraintProgram(NativeBphclDocument document)
	{
		foreach (NativeBphclCloth cloth in document.Cloths)
		{
			Console.WriteLine($"cloth|{cloth.Index}|{cloth.Name}");
			foreach (NativeBphclSimCloth simulation in cloth.SimCloths)
			{
				Console.WriteLine($"  simulation|{simulation.Index}|sets={simulation.ConstraintSets.Count}");
				foreach (NativeBphclConstraintSet set in simulation.ConstraintSets.OrderBy((NativeBphclConstraintSet nativeBphclConstraintSet) => nativeBphclConstraintSet.Index))
				{
					Console.WriteLine($"    set|{set.Index}|{set.ClassName}|id={set.ConstraintId}|type={set.ConstraintType}|links={set.Links.Count}|local={set.LocalConstraints.Count}|localStiff={set.LocalStiffnessConstraints.Count}|transition={set.TransitionParticles.Count}");
				}
			}
			foreach (NativeBphclOperatorLayout @operator in cloth.Operators)
			{
				if (@operator.ClassName == "hclMoveParticlesOperator")
				{
					Console.WriteLine($"  move|{@operator.Index}|pairs=[{string.Join(',', @operator.VertexParticlePairs.Select((NativeBphclVertexParticlePair pair) => $"{pair.VertexIndex}>{pair.ParticleIndex}"))}]");
				}
				foreach (var (config, configIndex) in @operator.SimulateConfigs.Select((NativeBphclSimulateConfig item, int index) => (config: item, index: index)))
				{
					Console.WriteLine($"  simulate|{@operator.Index}:{configIndex}|execution=[{string.Join(',', config.ConstraintExecution)}]|substeps={config.SubSteps}|iterations={config.NumberOfSolveIterations}|all={config.UseAllInstanceCollidables}|adaptive={config.AdaptConstraintStiffness}");
				}
			}
		}
	}

	private static void CompareBphclRootReferenceTopology(NativeBphclDocument native, NativeBphclDocument rebuilt, int rootIndex)
	{
		bool num = rootIndex < 0;
		NativeBphclNamedVariant nativeRoot = (num ? null : (native.RootVariants.ElementAtOrDefault(rootIndex) ?? throw new ArgumentOutOfRangeException("rootIndex")));
		NativeBphclNamedVariant rebuiltRoot = (num ? null : (rebuilt.RootVariants.ElementAtOrDefault(rootIndex) ?? throw new ArgumentOutOfRangeException("rootIndex")));
		int nativeStartItem = (num ? FindImplicitRoot(native) : nativeRoot.ObjectItemIndex);
		int rebuiltStartItem = (num ? FindImplicitRoot(rebuilt) : rebuiltRoot.ObjectItemIndex);
		string rootPath = (num ? "implicit-root" : ("root:" + nativeRoot.Name));
		Console.WriteLine(num ? ("root|native=implicit:" + Describe(native, nativeStartItem) + "|rebuilt=implicit:" + Describe(rebuilt, rebuiltStartItem)) : $"root|native={nativeRoot.Name}:{nativeRoot.ClassName}|rebuilt={rebuiltRoot.Name}:{rebuiltRoot.ClassName}");
		Dictionary<int, (uint, uint)> nativeRanges = native.GetItemRanges();
		Dictionary<int, (uint, uint)> rebuiltRanges = rebuilt.GetItemRanges();
		HashSet<uint> nativePointerBytes = native.InternalPatches.SelectMany((NativeBphclPatch group) => group.Offsets).SelectMany((uint num2) => from byteOffset in Enumerable.Range(0, 4)
			select num2 + (uint)byteOffset).ToHashSet();
		HashSet<uint> rebuiltPointerBytes = rebuilt.InternalPatches.SelectMany((NativeBphclPatch group) => group.Offsets).SelectMany((uint num2) => from byteOffset in Enumerable.Range(0, 4)
			select num2 + (uint)byteOffset).ToHashSet();
		int nativeDataStart = (native.DataSection ?? throw new InvalidDataException("Native BPHCL has no DATA section.")).PayloadOffset;
		int rebuiltDataStart = (rebuilt.DataSection ?? throw new InvalidDataException("Rebuilt BPHCL has no DATA section.")).PayloadOffset;
		Dictionary<int, int> nativeToRebuilt = new Dictionary<int, int>();
		Dictionary<int, int> rebuiltToNative = new Dictionary<int, int>();
		HashSet<(int Native, int Rebuilt)> visitedPairs = new HashSet<(int, int)>();
		Queue<(int Native, int Rebuilt, string Path)> pending = new Queue<(int, int, string)>();
		int printed = 0;
		int charAliases = 0;
		Pair(nativeStartItem, rebuiltStartItem, rootPath);
		while (pending.Count > 0)
		{
			(int, int, string) pair = pending.Dequeue();
			NativeBphclItem nativeItem = native.GetItem(pair.Item1);
			NativeBphclItem rebuiltItem = rebuilt.GetItem(pair.Item2);
			if (nativeItem.TypeIndex != rebuiltItem.TypeIndex || nativeItem.Count != rebuiltItem.Count)
			{
				Report($"node|native={Describe(native, pair.Item1)}|rebuilt={Describe(rebuilt, pair.Item2)}|path={pair.Item3}");
				continue;
			}
			if (nativeItem.Flags != rebuiltItem.Flags)
			{
				Report($"flags|path={pair.Item3}|native=0x{nativeItem.Flags:X8}|rebuilt=0x{rebuiltItem.Flags:X8}|node={Describe(native, pair.Item1)}");
			}
			(uint, uint) nativeRange = nativeRanges[pair.Item1];
			(uint, uint) rebuiltRange = rebuiltRanges[pair.Item2];
			uint nativeRangeLength = nativeRange.Item2 - nativeRange.Item1;
			uint rebuiltRangeLength = rebuiltRange.Item2 - rebuiltRange.Item1;
			if (nativeRangeLength != rebuiltRangeLength)
			{
				Report($"allocation|path={pair.Item3}|node={Describe(native, pair.Item1)}|native=0x{nativeRangeLength:X}|rebuilt=0x{rebuiltRangeLength:X}");
			}
			int payloadLength = checked((int)(nativeItem.Count * NativeBphclReflectionCatalog.GetTypeSize(nativeItem.TypeIndex)));
			List<string> differences = new List<string>();
			for (int offset = 0; offset < payloadLength; offset++)
			{
				uint nativeOffset = nativeItem.DataOffset + (uint)offset;
				uint rebuiltOffset = rebuiltItem.DataOffset + (uint)offset;
				if (nativePointerBytes.Contains(nativeOffset) || rebuiltPointerBytes.Contains(rebuiltOffset))
				{
					continue;
				}
				byte nativeByte = native.Bytes[nativeDataStart + checked((int)nativeOffset)];
				byte rebuiltByte = rebuilt.Bytes[rebuiltDataStart + checked((int)rebuiltOffset)];
				if (nativeByte != rebuiltByte)
				{
					differences.Add($"0x{offset:X}:{nativeByte:X2}/{rebuiltByte:X2}");
					if (differences.Count == 8)
					{
						break;
					}
				}
			}
			if (differences.Count > 0)
			{
				Report($"payload|path={pair.Item3}|node={Describe(native, pair.Item1)}|bytes={string.Join(',', differences)}");
			}
			Dictionary<uint, (int, uint)> nativeReferences = References(native, nativeRanges, pair.Item1);
			Dictionary<uint, (int, uint)> rebuiltReferences = References(rebuilt, rebuiltRanges, pair.Item2);
			foreach (uint offset2 in from result in nativeReferences.Keys.Union(rebuiltReferences.Keys)
				orderby result
				select result)
			{
				bool hasNative = nativeReferences.TryGetValue(offset2, out var nativeTarget);
				bool hasRebuilt = rebuiltReferences.TryGetValue(offset2, out var rebuiltTarget);
				if (!hasNative || !hasRebuilt)
				{
					Report($"pointer|path={pair.Item3}+0x{offset2:X}|native={(hasNative ? Describe(native, nativeTarget.Item1) : "<null>")}|rebuilt={(hasRebuilt ? Describe(rebuilt, rebuiltTarget.Item1) : "<null>")}");
					continue;
				}
				if (nativeTarget.Item2 != rebuiltTarget.Item2)
				{
					Report($"patchType|path={pair.Item3}+0x{offset2:X}|native={nativeTarget.Item2}:{native.GetTypeName(nativeTarget.Item2)}|rebuilt={rebuiltTarget.Item2}:{rebuilt.GetTypeName(rebuiltTarget.Item2)}");
				}
				Pair(nativeTarget.Item1, rebuiltTarget.Item1, $"{pair.Item3}+0x{offset2:X}");
			}
		}
		Console.WriteLine($"rootSummary|paired={visitedPairs.Count}|printed={printed}|charAliases={charAliases}");
		static string Describe(NativeBphclDocument document, int itemIndex)
		{
			NativeBphclItem item = document.GetItem(itemIndex);
			string typeName = document.GetTypeName(item.TypeIndex) ?? $"type_{item.TypeIndex}";
			if (string.Equals(typeName, "char", StringComparison.Ordinal))
			{
				NativeBphclSection data = document.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
				string text = Encoding.UTF8.GetString(document.Bytes, data.PayloadOffset + checked((int)item.DataOffset), checked((int)item.Count)).TrimEnd('\0');
				return $"{itemIndex}:char[{item.Count}]='{text}'";
			}
			return $"{itemIndex}:{typeName}[{item.Count}]";
		}
		static int FindImplicitRoot(NativeBphclDocument document)
		{
			return (from entry in document.Items.Select((NativeBphclItem item, int index) => (item: item, index: index))
				where entry.item.DataOffset == 0 && string.Equals(document.GetTypeName(entry.item.TypeIndex), "hkRootLevelContainer", StringComparison.Ordinal)
				select entry.index).Single();
		}
		void Pair(int nativeItemIndex, int rebuiltItemIndex, string path)
		{
			if (nativeToRebuilt.TryGetValue(nativeItemIndex, out var previousRebuilt) && previousRebuilt != rebuiltItemIndex)
			{
				if (string.Equals(native.GetTypeName(native.GetItem(nativeItemIndex).TypeIndex), "char", StringComparison.Ordinal))
				{
					charAliases++;
				}
				else
				{
					Report($"alias|native={Describe(native, nativeItemIndex)}|first={Describe(rebuilt, previousRebuilt)}|again={Describe(rebuilt, rebuiltItemIndex)}|path={path}");
				}
			}
			else
			{
				nativeToRebuilt[nativeItemIndex] = rebuiltItemIndex;
			}
			if (rebuiltToNative.TryGetValue(rebuiltItemIndex, out var previousNative) && previousNative != nativeItemIndex)
			{
				if (string.Equals(rebuilt.GetTypeName(rebuilt.GetItem(rebuiltItemIndex).TypeIndex), "char", StringComparison.Ordinal))
				{
					charAliases++;
				}
				else
				{
					Report($"alias|rebuilt={Describe(rebuilt, rebuiltItemIndex)}|first={Describe(native, previousNative)}|again={Describe(native, nativeItemIndex)}|path={path}");
				}
			}
			else
			{
				rebuiltToNative[rebuiltItemIndex] = nativeItemIndex;
			}
			if (visitedPairs.Add((nativeItemIndex, rebuiltItemIndex)))
			{
				pending.Enqueue((nativeItemIndex, rebuiltItemIndex, path));
			}
		}
		static Dictionary<uint, (int Target, uint PatchType)> References(NativeBphclDocument document, IReadOnlyDictionary<int, (uint Start, uint End)> ranges, int itemIndex)
		{
			(uint, uint) range = ranges[itemIndex];
			Dictionary<uint, (int, uint)> result = new Dictionary<uint, (int, uint)>();
			foreach (NativeBphclPatch patch in document.GetPatchesForItems(new int[1] { itemIndex }))
			{
				foreach (uint pointerOffset in patch.Offsets)
				{
					if (pointerOffset >= range.Item1 && pointerOffset < range.Item2 && document.TryGetReferencedItem(pointerOffset, out var target))
					{
						result[pointerOffset - range.Item1] = (target, patch.TypeIndex);
					}
				}
			}
			return result;
		}
		void Report(string message)
		{
			if (printed++ < 120)
			{
				Console.WriteLine(message);
			}
		}
	}

	private static void CompareBphclReferenceTopology(NativeBphclDocument native, int nativeClothIndex, NativeBphclDocument rebuilt, int rebuiltClothIndex)
	{
		NativeBphclCloth nativeCloth = native.Cloths.ElementAtOrDefault(nativeClothIndex) ?? throw new ArgumentOutOfRangeException("nativeClothIndex");
		NativeBphclCloth rebuiltCloth = rebuilt.Cloths.ElementAtOrDefault(rebuiltClothIndex) ?? throw new ArgumentOutOfRangeException("rebuiltClothIndex");
		NativeBphclSkeleton? obj = native.Skeletons.ElementAtOrDefault(nativeClothIndex) ?? throw new InvalidDataException("Native cloth has no paired skeleton.");
		NativeBphclSkeleton rebuiltSkeleton = rebuilt.Skeletons.ElementAtOrDefault(rebuiltClothIndex) ?? throw new InvalidDataException("Rebuilt cloth has no paired skeleton.");
		Dictionary<int, (uint, uint)> nativeRanges = native.GetItemRanges();
		Dictionary<int, (uint, uint)> rebuiltRanges = rebuilt.GetItemRanges();
		HashSet<uint> nativePointerBytes = native.InternalPatches.SelectMany((NativeBphclPatch patch) => patch.Offsets).SelectMany((uint num) => from byteOffset in Enumerable.Range(0, 4)
			select num + (uint)byteOffset).ToHashSet();
		HashSet<uint> rebuiltPointerBytes = rebuilt.InternalPatches.SelectMany((NativeBphclPatch patch) => patch.Offsets).SelectMany((uint num) => from byteOffset in Enumerable.Range(0, 4)
			select num + (uint)byteOffset).ToHashSet();
		int nativeDataStart = (native.DataSection ?? throw new InvalidDataException("Native BPHCL has no DATA section.")).PayloadOffset;
		int rebuiltDataStart = (rebuilt.DataSection ?? throw new InvalidDataException("Rebuilt BPHCL has no DATA section.")).PayloadOffset;
		Dictionary<int, int> nativeToRebuilt = new Dictionary<int, int>();
		Dictionary<int, int> rebuiltToNative = new Dictionary<int, int>();
		HashSet<(int Native, int Rebuilt)> visitedPairs = new HashSet<(int, int)>();
		Queue<(int Native, int Rebuilt, string Path)> pending = new Queue<(int, int, string)>();
		int reported = 0;
		Pair(nativeCloth.ItemIndex, rebuiltCloth.ItemIndex, "cloth");
		Pair(obj.ItemIndex, rebuiltSkeleton.ItemIndex, "skeleton");
		while (pending.Count > 0)
		{
			(int, int, string) pair = pending.Dequeue();
			NativeBphclItem nativeItem = native.GetItem(pair.Item1);
			NativeBphclItem rebuiltItem = rebuilt.GetItem(pair.Item2);
			if (nativeItem.TypeIndex != rebuiltItem.TypeIndex || nativeItem.Count != rebuiltItem.Count)
			{
				Report($"node|native={Describe(native, pair.Item1)}|rebuilt={Describe(rebuilt, pair.Item2)}|path={pair.Item3}");
				continue;
			}
			if (nativeItem.Flags != rebuiltItem.Flags)
			{
				Report($"flags|path={pair.Item3}|node={Describe(native, pair.Item1)}|native=0x{nativeItem.Flags:X8}:{native.GetTypeName(nativeItem.Flags)}|rebuilt=0x{rebuiltItem.Flags:X8}:{rebuilt.GetTypeName(rebuiltItem.Flags)}");
			}
			uint elementSize = NativeBphclReflectionCatalog.GetTypeSize(nativeItem.TypeIndex);
			int payloadLength = checked((int)(nativeItem.Count * elementSize));
			List<string> payloadDifferences = new List<string>();
			for (int byteIndex = 0; byteIndex < payloadLength; byteIndex++)
			{
				uint nativeOffset = nativeItem.DataOffset + (uint)byteIndex;
				uint rebuiltOffset = rebuiltItem.DataOffset + (uint)byteIndex;
				if (nativePointerBytes.Contains(nativeOffset) || rebuiltPointerBytes.Contains(rebuiltOffset))
				{
					continue;
				}
				byte nativeByte = native.Bytes[nativeDataStart + checked((int)nativeOffset)];
				byte rebuiltByte = rebuilt.Bytes[rebuiltDataStart + checked((int)rebuiltOffset)];
				if (nativeByte != rebuiltByte)
				{
					payloadDifferences.Add($"0x{byteIndex:X}:{nativeByte:X2}/{rebuiltByte:X2}");
					if (payloadDifferences.Count == 8)
					{
						break;
					}
				}
			}
			if (payloadDifferences.Count > 0)
			{
				Report($"payload|path={pair.Item3}|node={Describe(native, pair.Item1)}|bytes={string.Join(',', payloadDifferences)}");
			}
			Dictionary<uint, int> nativeReferences = GetReferences(native, nativeRanges, pair.Item1);
			Dictionary<uint, int> rebuiltReferences = GetReferences(rebuilt, rebuiltRanges, pair.Item2);
			foreach (uint offset in from result in nativeReferences.Keys.Union(rebuiltReferences.Keys)
				orderby result
				select result)
			{
				bool hasNative = nativeReferences.TryGetValue(offset, out var nativeTarget);
				bool hasRebuilt = rebuiltReferences.TryGetValue(offset, out var rebuiltTarget);
				if (!hasNative || !hasRebuilt)
				{
					Report($"pointer|path={pair.Item3}+0x{offset:X}|native={(hasNative ? Describe(native, nativeTarget) : "<null>")}|rebuilt={(hasRebuilt ? Describe(rebuilt, rebuiltTarget) : "<null>")}");
				}
				else
				{
					Pair(nativeTarget, rebuiltTarget, $"{pair.Item3}+0x{offset:X}");
				}
			}
		}
		Console.WriteLine($"summary|cloth={nativeCloth.Name}|paired={visitedPairs.Count}|mismatches={reported}");
		static string Describe(NativeBphclDocument document, int itemIndex)
		{
			NativeBphclItem item = document.GetItem(itemIndex);
			string name = document.GetTypeName(item.TypeIndex) ?? $"type_{item.TypeIndex}";
			checked
			{
				if (string.Equals(name, "char", StringComparison.Ordinal))
				{
					NativeBphclSection data = document.DataSection;
					if (data != null)
					{
						int length = (int)item.Count;
						int start = data.PayloadOffset + (int)item.DataOffset;
						string text = Encoding.UTF8.GetString(document.Bytes, start, length).TrimEnd('\0');
						return $"{itemIndex}:char[{item.Count}]='{text}'";
					}
					return $"{itemIndex}:char[{item.Count}]";
				}
				return $"{itemIndex}:{name}[{item.Count}]";
			}
		}
		static Dictionary<uint, int> GetReferences(NativeBphclDocument document, IReadOnlyDictionary<int, (uint Start, uint End)> ranges, int itemIndex)
		{
			(uint, uint) range = ranges[itemIndex];
			Dictionary<uint, int> references = new Dictionary<uint, int>();
			foreach (NativeBphclPatch patchesForItem in document.GetPatchesForItems(new int[1] { itemIndex }))
			{
				foreach (uint pointerOffset in patchesForItem.Offsets)
				{
					if (pointerOffset >= range.Item1 && pointerOffset < range.Item2 && document.TryGetReferencedItem(pointerOffset, out var targetItemIndex))
					{
						references[pointerOffset - range.Item1] = targetItemIndex;
					}
				}
			}
			return references;
		}
		void Pair(int nativeItemIndex, int rebuiltItemIndex, string path)
		{
			if (nativeToRebuilt.TryGetValue(nativeItemIndex, out var previousRebuilt) && previousRebuilt != rebuiltItemIndex)
			{
				Report($"alias|native={Describe(native, nativeItemIndex)}|first={Describe(rebuilt, previousRebuilt)}|again={Describe(rebuilt, rebuiltItemIndex)}|path={path}");
			}
			else
			{
				nativeToRebuilt[nativeItemIndex] = rebuiltItemIndex;
			}
			if (rebuiltToNative.TryGetValue(rebuiltItemIndex, out var previousNative) && previousNative != nativeItemIndex)
			{
				Report($"alias|rebuilt={Describe(rebuilt, rebuiltItemIndex)}|first={Describe(native, previousNative)}|again={Describe(native, nativeItemIndex)}|path={path}");
			}
			else
			{
				rebuiltToNative[rebuiltItemIndex] = nativeItemIndex;
			}
			if (visitedPairs.Add((nativeItemIndex, rebuiltItemIndex)))
			{
				pending.Enqueue((nativeItemIndex, rebuiltItemIndex, path));
			}
		}
		void Report(string text)
		{
			if (reported++ < 80)
			{
				Console.WriteLine(text);
			}
		}
	}

	private static void CompareBphclFreshRuntime(NativeBphclDocument native, NativeBphclDocument fresh)
	{
		Dictionary<string, NativeBphclCloth> freshByName = fresh.Cloths.ToDictionary<NativeBphclCloth, string>((NativeBphclCloth cloth) => cloth.Name, StringComparer.Ordinal);
		foreach (NativeBphclCloth nativeCloth in native.Cloths)
		{
			if (!freshByName.TryGetValue(nativeCloth.Name, out var freshCloth))
			{
				Console.WriteLine("missing|cloth=" + nativeCloth.Name);
				continue;
			}
			int nativeIndex = nativeCloth.Index;
			int freshIndex = freshCloth.Index;
			NativeBphclSkeleton nativeSkeleton = native.Skeletons.ElementAtOrDefault(nativeIndex);
			NativeBphclSkeleton freshSkeleton = fresh.Skeletons.ElementAtOrDefault(freshIndex);
			Console.WriteLine($"cloth|{nativeCloth.Name}|native={nativeIndex}|fresh={freshIndex}");
			CompareValue("buffers", string.Join(';', nativeCloth.BufferDefinitions.Select((NativeBphclBufferDefinition buffer) => $"{buffer.ClassName}/{buffer.MeshName}/{buffer.BufferName}/{buffer.Type}/{buffer.SubType}/{buffer.VertexCount}/{buffer.TriangleCount}")), string.Join(';', freshCloth.BufferDefinitions.Select((NativeBphclBufferDefinition buffer) => $"{buffer.ClassName}/{buffer.MeshName}/{buffer.BufferName}/{buffer.Type}/{buffer.SubType}/{buffer.VertexCount}/{buffer.TriangleCount}")));
			CompareValue("sets", string.Join(';', nativeCloth.TransformSetDefinitions.Select((NativeBphclTransformSetDefinition set) => $"{set.Name}/{set.Type}/{set.TransformCount}")), string.Join(';', freshCloth.TransformSetDefinitions.Select((NativeBphclTransformSetDefinition set) => $"{set.Name}/{set.Type}/{set.TransformCount}")));
			CompareValue("operators", string.Join("|", nativeCloth.Operators.Select(Operator)), string.Join("|", freshCloth.Operators.Select(Operator)));
			CompareValue("states", string.Join("||", nativeCloth.States.Select(State)), string.Join("||", freshCloth.States.Select(State)));
			CompareValue("simulations", string.Join("###", nativeCloth.SimCloths.Select(Runtime)), string.Join("###", freshCloth.SimCloths.Select(Runtime)));
			CompareValue("skeleton", ((object)nativeSkeleton == null) ? "<missing>" : string.Join(';', nativeSkeleton.Bones.Select((NativeBphclBone bone) => $"{bone.Name}/{bone.ParentIndex}/{V(bone.Translation)}/{V(bone.Rotation)}/{V(bone.Scale)}")), ((object)freshSkeleton == null) ? "<missing>" : string.Join(';', freshSkeleton.Bones.Select((NativeBphclBone bone) => $"{bone.Name}/{bone.ParentIndex}/{V(bone.Translation)}/{V(bone.Rotation)}/{V(bone.Scale)}")));
		}
		foreach (NativeBphclCloth extra in fresh.Cloths.Where((NativeBphclCloth cloth) => !native.Cloths.Any((NativeBphclCloth nativeBphclCloth) => string.Equals(nativeBphclCloth.Name, cloth.Name, StringComparison.Ordinal))))
		{
			Console.WriteLine("extra|cloth=" + extra.Name);
		}
		static void CompareValue(string section, string nativeValue, string freshValue)
		{
			if (string.Equals(nativeValue, freshValue, StringComparison.Ordinal))
			{
				Console.WriteLine("  same|" + section);
			}
			else
			{
				Console.WriteLine($"  diff|{section}|native={nativeValue}|fresh={freshValue}");
			}
		}
		static string Constraint(NativeBphclConstraintSet set)
		{
			return $"{set.ClassName}|{set.ConstraintId}/{set.ConstraintType}|values[{Numbers(set.Values)}]|links[{Links(set.Links)}]|local[{Links(set.LocalConstraints)}]|localStiff[{Links(set.LocalStiffnessConstraints)}]|transition[{Links(set.TransitionParticles)}]";
		}
		static string F(float value)
		{
			return value.ToString("G9", CultureInfo.InvariantCulture);
		}
		static string Links(IReadOnlyList<NativeBphclConstraintLink> links)
		{
			return string.Join(";", links.Select((NativeBphclConstraintLink link) => (link.ParticleA?.ToString(CultureInfo.InvariantCulture) ?? "-") + "/" + (link.ParticleB?.ToString(CultureInfo.InvariantCulture) ?? "-") + ":" + Numbers(link.Values)));
		}
		static string M(Matrix4x4 matrix)
		{
			return string.Join(",", new string[16]
			{
				F(matrix.M11),
				F(matrix.M12),
				F(matrix.M13),
				F(matrix.M14),
				F(matrix.M21),
				F(matrix.M22),
				F(matrix.M23),
				F(matrix.M24),
				F(matrix.M31),
				F(matrix.M32),
				F(matrix.M33),
				F(matrix.M34),
				F(matrix.M41),
				F(matrix.M42),
				F(matrix.M43),
				F(matrix.M44)
			});
		}
		static string Numbers(IReadOnlyDictionary<string, double> values)
		{
			return string.Join(",", from pair in values.OrderBy<KeyValuePair<string, double>, string>((KeyValuePair<string, double> pair) => pair.Key, StringComparer.Ordinal)
				select pair.Key + "=" + pair.Value.ToString("G12", CultureInfo.InvariantCulture));
		}
		static string Operator(NativeBphclOperatorLayout op)
		{
			return $"{op.ClassName}:in={op.InputBufferIndex}:out={op.OutputBufferIndex}:set={op.TransformSetIndex}:sim={op.SimulationClothIndex}:ref={op.ReferenceBufferIndex}:vertices={op.VertexCount}:{op.StartVertexIn}:{op.StartVertexOut}:pairs=[{string.Join(',', op.VertexParticlePairs.Select((NativeBphclVertexParticlePair pair) => $"{pair.VertexIndex}>{pair.ParticleIndex}"))}]:configs=[{string.Join(';', op.SimulateConfigs.Select((NativeBphclSimulateConfig config) => $"{string.Join(',', config.ConstraintExecution)}/{string.Join(',', config.InstanceCollidablesUsed)}/{config.SubSteps}/{config.NumberOfSolveIterations}/{config.UseAllInstanceCollidables}/{config.AdaptConstraintStiffness}"))}]";
		}
		static string Runtime(NativeBphclSimCloth sim)
		{
			return $"info={V(sim.SimulationInfo.Gravity)}:{F(sim.SimulationInfo.GlobalDampingPerSecond)}|particles=[{string.Join(";", sim.Particles.Select((NativeBphclParticle p) => $"{V(p.Position)}:{p.Fixed}:{F(p.Mass)}:{F(p.InverseMass)}:{F(p.Radius)}:{F(p.Friction)}"))}]|tri=[{string.Join(',', sim.TriangleIndices)}]|flips=[{string.Join(',', sim.TriangleFlips)}]|masks=[{string.Join(',', sim.StaticCollisionMasks)}]|runtime={sim.Runtime.DoNormals}:{F(sim.Runtime.MaxParticleRadius)}:{F(sim.Runtime.TotalMass)}:transfer={sim.Runtime.TransferMotionEnabled}/{sim.Runtime.TransferMotionData.TransformSetIndex}/{sim.Runtime.TransferMotionData.TransformIndex}/T{sim.Runtime.TransferMotionData.TransferTranslationMotion}:{F(sim.Runtime.TransferMotionData.MinTranslationSpeed)}:{F(sim.Runtime.TransferMotionData.MaxTranslationSpeed)}:R{sim.Runtime.TransferMotionData.TransferRotationMotion}:{F(sim.Runtime.TransferMotionData.MinRotationSpeed)}:{F(sim.Runtime.TransferMotionData.MaxRotationSpeed)}|land={sim.Runtime.LandscapeCollisionEnabled}:{sim.Runtime.NumLandscapeCollidableParticles}:{F(sim.Runtime.LandscapeCollisionData.CollisionTolerance)}|pinch={sim.Runtime.PinchDetectionEnabled}:{string.Join(',', sim.Runtime.PerParticlePinchDetectionEnabledFlags.Select((bool value) => (!value) ? '0' : '1'))}:{string.Join(';', sim.Runtime.CollidablePinchingData.Select((NativeBphclCollidablePinchingData value) => $"{value.Enabled}/{value.Priority}/{F(value.Radius)}"))}|map={sim.CollidableTransformMap.TransformSetIndex}/[{string.Join(',', sim.CollidableTransformMap.TransformIndices)}]/[{string.Join(';', sim.CollidableTransformMap.Offsets.Select(M))}]|constraintIds=[{string.Join(',', sim.SimulationOperatorIds)}]|sets=[{string.Join("||", sim.ConstraintSets.Select(Constraint))}]";
		}
		static string State(NativeBphclClothState state)
		{
			string buffers = string.Join(";", state.BufferAccesses.Select((NativeBphclClothStateBufferAccess access) => $"{access.BufferIndex}/{access.ComponentFlags0}/{access.ComponentFlags1}/{access.ComponentFlags2}/{access.ComponentFlags3}/{access.TrianglesRead}/{access.ShadowBufferIndex}"));
			string sets = string.Join(";", state.TransformSetAccesses.Select(delegate(NativeBphclClothStateTransformSetAccess access)
			{
				string value = string.Join("|", access.TransformTrackers.Select((NativeBphclTransformTracker tracker) => $"r{string.Join(',', tracker.Read.Words)}:{tracker.Read.NumBits};b{string.Join(',', tracker.ReadBeforeWrite.Words)}:{tracker.ReadBeforeWrite.NumBits};w{string.Join(',', tracker.Written.Words)}:{tracker.Written.NumBits}"));
				return $"{access.TransformSetIndex}/{access.ComponentFlags0}/{access.ComponentFlags1}/{value}";
			}));
			return $"{state.Name}|ops={string.Join(',', state.OperatorItemIndices)}|buffers={buffers}|sets={sets}";
		}
		static string V(Vector4 value)
		{
			return $"{F(value.X)},{F(value.Y)},{F(value.Z)},{F(value.W)}";
		}
	}

	private static void CompareBphclClothSemantics(NativeBphclDocument expected, NativeBphclDocument actual, string clothName)
	{
		NativeBphclCloth[] array = expected.Cloths.Where((NativeBphclCloth cloth) => string.Equals(Key(cloth.Name), Key(clothName), StringComparison.Ordinal)).ToArray();
		NativeBphclCloth[] actualCloths = actual.Cloths.Where((NativeBphclCloth cloth) => string.Equals(Key(cloth.Name), Key(clothName), StringComparison.Ordinal)).ToArray();
		if (array.Length != 1 || actualCloths.Length != 1)
		{
			throw new InvalidDataException("Cloth '" + clothName + "' must exist exactly once in each BPHCL file.");
		}
		NativeBphclCloth expectedCloth = array[0];
		NativeBphclCloth actualCloth = actualCloths[0];
		Console.WriteLine($"cloth|expected={expectedCloth.Name}:{expectedCloth.Index}|actual={actualCloth.Name}:{actualCloth.Index}");
		Compare("buffers", string.Join(';', expectedCloth.BufferDefinitions.Select((NativeBphclBufferDefinition buffer) => $"{buffer.ClassName}/{buffer.MeshName}/{buffer.BufferName}/{buffer.Type}/{buffer.SubType}/{buffer.VertexCount}/{buffer.TriangleCount}")), string.Join(';', actualCloth.BufferDefinitions.Select((NativeBphclBufferDefinition buffer) => $"{buffer.ClassName}/{buffer.MeshName}/{buffer.BufferName}/{buffer.Type}/{buffer.SubType}/{buffer.VertexCount}/{buffer.TriangleCount}")));
		Compare("transformSets", string.Join(';', expectedCloth.TransformSetDefinitions.Select((NativeBphclTransformSetDefinition set) => $"{set.Name}/{set.Type}/{set.TransformCount}")), string.Join(';', actualCloth.TransformSetDefinitions.Select((NativeBphclTransformSetDefinition set) => $"{set.Name}/{set.Type}/{set.TransformCount}")));
		Compare("operators", string.Join("||", expectedCloth.Operators.Select(Operator)), string.Join("||", actualCloth.Operators.Select(Operator)));
		Compare("states", string.Join("||", expectedCloth.States.Select(State)), string.Join("||", actualCloth.States.Select(State)));
		Compare("objectSpaceSkins", string.Join("||", expectedCloth.ObjectSpaceSkins.Select(ObjectSkin)), string.Join("||", actualCloth.ObjectSpaceSkins.Select(ObjectSkin)));
		Compare("boneSpaceSkins", string.Join("||", expectedCloth.BoneSpaceSkins.Select(BoneSkin)), string.Join("||", actualCloth.BoneSpaceSkins.Select(BoneSkin)));
		Compare("simpleMeshDeformers", string.Join("||", expectedCloth.SimpleMeshBoneDeformers.Select(MeshDeformer)), string.Join("||", actualCloth.SimpleMeshBoneDeformers.Select(MeshDeformer)));
		NativeBphclSkeleton? expectedSkeleton = expected.Skeletons.ElementAtOrDefault(expectedCloth.Index);
		NativeBphclSkeleton actualSkeleton = actual.Skeletons.ElementAtOrDefault(actualCloth.Index);
		CompareSkeleton(expectedSkeleton, actualSkeleton);
		int simulationCount = Math.Max(expectedCloth.SimCloths.Count, actualCloth.SimCloths.Count);
		for (int simulationIndex = 0; simulationIndex < simulationCount; simulationIndex++)
		{
			NativeBphclSimCloth expectedSimulation = expectedCloth.SimCloths.ElementAtOrDefault(simulationIndex);
			NativeBphclSimCloth actualSimulation = actualCloth.SimCloths.ElementAtOrDefault(simulationIndex);
			if ((object)expectedSimulation == null || (object)actualSimulation == null)
			{
				Console.WriteLine($"diff|simulation[{simulationIndex}]|expected={(((object)expectedSimulation == null) ? "<missing>" : expectedSimulation.Name)}|actual={(((object)actualSimulation == null) ? "<missing>" : actualSimulation.Name)}");
				continue;
			}
			Console.WriteLine($"simulation|{simulationIndex}|expected={expectedSimulation.Name}|actual={actualSimulation.Name}");
			Compare("  settings", "gravity=" + V(expectedSimulation.SimulationInfo.Gravity) + "|damping=" + F(expectedSimulation.SimulationInfo.GlobalDampingPerSecond), "gravity=" + V(actualSimulation.SimulationInfo.Gravity) + "|damping=" + F(actualSimulation.SimulationInfo.GlobalDampingPerSecond));
			CompareParticles(expectedSimulation.Particles, actualSimulation.Particles);
			Compare("  triangles", string.Join(',', expectedSimulation.TriangleIndices), string.Join(',', actualSimulation.TriangleIndices));
			Compare("  triangleFlips", string.Join(',', expectedSimulation.TriangleFlips), string.Join(',', actualSimulation.TriangleFlips));
			Compare("  collisionMasks", string.Join(',', expectedSimulation.StaticCollisionMasks), string.Join(',', actualSimulation.StaticCollisionMasks));
			Compare("  runtime", Runtime(expectedSimulation.Runtime), Runtime(actualSimulation.Runtime));
			CompareColliderMap(expectedSimulation.CollidableTransformMap, actualSimulation.CollidableTransformMap);
			CompareConstraintSets(expectedSimulation.ConstraintSets, actualSimulation.ConstraintSets);
		}
		static string BoneSkin(NativeBphclBoneSpaceSkin skin)
		{
			return $"op={skin.OperatorIndex}|out={skin.OutputBufferIndex}|set={skin.TransformSetIndex}|subset=[{string.Join(',', skin.TransformSubset)}]|deformer={Packed(skin.Deformer.FourBlendEntries)}|{Packed(skin.Deformer.ThreeBlendEntries)}|{Packed(skin.Deformer.TwoBlendEntries)}|{Packed(skin.Deformer.OneBlendEntries)}|controls=[{string.Join(',', skin.Deformer.ControlBytes)}]|range={skin.Deformer.StartVertexIndex}:{skin.Deformer.EndVertexIndex}:{skin.Deformer.BatchSizeSpu}:{skin.Deformer.PartialWrite}|localPs={Positions(skin.LocalPs)}|localUnpacked={Positions(skin.LocalUnpackedPs)}";
		}
		static void Compare(string label, string expectedValue, string actualValue)
		{
			Console.WriteLine(string.Equals(expectedValue, actualValue, StringComparison.Ordinal) ? ("same|" + label) : $"diff|{label}|expected={expectedValue}|actual={actualValue}");
		}
		static void CompareColliderMap(NativeBphclCollidableTransformMap expectedMap, NativeBphclCollidableTransformMap actualMap)
		{
			bool indexDifference = !expectedMap.TransformIndices.SequenceEqual(actualMap.TransformIndices) || expectedMap.TransformSetIndex != actualMap.TransformSetIndex;
			Math.Max(expectedMap.Offsets.Count, actualMap.Offsets.Count);
			float maximumDelta = 0f;
			for (int index = 0; index < Math.Min(expectedMap.Offsets.Count, actualMap.Offsets.Count); index++)
			{
				maximumDelta = Math.Max(maximumDelta, MatrixDelta(expectedMap.Offsets[index], actualMap.Offsets[index]));
			}
			Console.WriteLine((!indexDifference && expectedMap.Offsets.Count == actualMap.Offsets.Count && maximumDelta < 1E-06f) ? "  same|colliderMap" : $"  diff|colliderMap|set={expectedMap.TransformSetIndex}/{actualMap.TransformSetIndex}|indices=[{string.Join(',', expectedMap.TransformIndices)}]/[{string.Join(',', actualMap.TransformIndices)}]|offsets={expectedMap.Offsets.Count}/{actualMap.Offsets.Count}|maxMatrixDelta={F(maximumDelta)}");
		}
		static void CompareConstraintSets(IReadOnlyList<NativeBphclConstraintSet> expectedSets, IReadOnlyList<NativeBphclConstraintSet> actualSets)
		{
			if (expectedSets.Count != actualSets.Count)
			{
				Console.WriteLine($"  diff|constraintSets.count|expected={expectedSets.Count}|actual={actualSets.Count}");
			}
			else
			{
				for (int setIndex = 0; setIndex < expectedSets.Count; setIndex++)
				{
					NativeBphclConstraintSet left = expectedSets[setIndex];
					NativeBphclConstraintSet right = actualSets[setIndex];
					string identity = $"{left.ClassName}:{left.Index}";
					Compare("  constraintSet[" + identity + "].header", $"id={left.ConstraintId}|type={left.ConstraintType}|values={Numbers(left.Values)}", $"id={right.ConstraintId}|type={right.ConstraintType}|values={Numbers(right.Values)}");
					CompareLinks("  constraintSet[" + identity + "].links", left.Links, right.Links);
					CompareLinks("  constraintSet[" + identity + "].local", left.LocalConstraints, right.LocalConstraints);
					CompareLinks("  constraintSet[" + identity + "].localStiffness", left.LocalStiffnessConstraints, right.LocalStiffnessConstraints);
					CompareLinks("  constraintSet[" + identity + "].transition", left.TransitionParticles, right.TransitionParticles);
				}
			}
		}
		static void CompareLinks(string label, IReadOnlyList<NativeBphclConstraintLink> expectedLinks, IReadOnlyList<NativeBphclConstraintLink> actualLinks)
		{
			if (expectedLinks.Count != actualLinks.Count)
			{
				Console.WriteLine($"{label}|expectedCount={expectedLinks.Count}|actualCount={actualLinks.Count}");
			}
			else
			{
				List<string> differences = new List<string>();
				for (int index = 0; index < expectedLinks.Count; index++)
				{
					NativeBphclConstraintLink left = expectedLinks[index];
					NativeBphclConstraintLink right = actualLinks[index];
					string expectedText = $"{left.ParticleA}/{left.ParticleB}/{Numbers(left.Values)}";
					string actualText = $"{right.ParticleA}/{right.ParticleB}/{Numbers(right.Values)}";
					if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
					{
						differences.Add($"#{index}:{expectedText}/{actualText}");
					}
				}
				Console.WriteLine((differences.Count == 0) ? (label + "|same") : $"{label}|diff={differences.Count}|{string.Join(";", differences.Take(6))}{((differences.Count > 6) ? ";..." : string.Empty)}");
			}
		}
		static void CompareParticles(IReadOnlyList<NativeBphclParticle> expectedParticles, IReadOnlyList<NativeBphclParticle> actualParticles)
		{
			if (expectedParticles.Count != actualParticles.Count)
			{
				Console.WriteLine($"  diff|particles.count|expected={expectedParticles.Count}|actual={actualParticles.Count}");
			}
			else
			{
				List<string> differences = new List<string>();
				for (int index = 0; index < expectedParticles.Count; index++)
				{
					NativeBphclParticle left = expectedParticles[index];
					NativeBphclParticle right = actualParticles[index];
					List<string> fields = new List<string>();
					if (V(left.Position) != V(right.Position))
					{
						fields.Add("position=" + V(left.Position) + "/" + V(right.Position));
					}
					if (left.Fixed != right.Fixed)
					{
						fields.Add($"fixed={left.Fixed}/{right.Fixed}");
					}
					if (F(left.Mass) != F(right.Mass))
					{
						fields.Add("mass=" + F(left.Mass) + "/" + F(right.Mass));
					}
					if (F(left.InverseMass) != F(right.InverseMass))
					{
						fields.Add("inverseMass=" + F(left.InverseMass) + "/" + F(right.InverseMass));
					}
					if (F(left.Radius) != F(right.Radius))
					{
						fields.Add("radius=" + F(left.Radius) + "/" + F(right.Radius));
					}
					if (F(left.Friction) != F(right.Friction))
					{
						fields.Add("friction=" + F(left.Friction) + "/" + F(right.Friction));
					}
					if (fields.Count > 0)
					{
						differences.Add($"#{index}:{string.Join(',', fields)}");
					}
				}
				Console.WriteLine((differences.Count == 0) ? "  same|particles" : $"  diff|particles|count={differences.Count}|{string.Join(";", differences.Take(8))}{((differences.Count > 8) ? ";..." : string.Empty)}");
			}
		}
		static void CompareSkeleton(NativeBphclSkeleton? nativeBphclSkeleton, NativeBphclSkeleton? nativeBphclSkeleton2)
		{
			if ((object)nativeBphclSkeleton == null || (object)nativeBphclSkeleton2 == null)
			{
				Console.WriteLine("diff|skeleton|expected=" + (((object)nativeBphclSkeleton == null) ? "<missing>" : nativeBphclSkeleton.Bones.Count.ToString()) + "|actual=" + (((object)nativeBphclSkeleton2 == null) ? "<missing>" : nativeBphclSkeleton2.Bones.Count.ToString()));
			}
			else if (nativeBphclSkeleton.Bones.Count != nativeBphclSkeleton2.Bones.Count)
			{
				Console.WriteLine($"diff|skeleton.count|expected={nativeBphclSkeleton.Bones.Count}|actual={nativeBphclSkeleton2.Bones.Count}");
			}
			else
			{
				List<string> differences = new List<string>();
				for (int index = 0; index < nativeBphclSkeleton.Bones.Count; index++)
				{
					NativeBphclBone left = nativeBphclSkeleton.Bones[index];
					NativeBphclBone right = nativeBphclSkeleton2.Bones[index];
					if ($"{left.Name}/{left.ParentIndex}/{V(left.Translation)}/{V(left.Rotation)}/{V(left.Scale)}" != $"{right.Name}/{right.ParentIndex}/{V(right.Translation)}/{V(right.Rotation)}/{V(right.Scale)}")
					{
						differences.Add($"#{index}:{left.Name}/{right.Name}");
					}
				}
				Console.WriteLine((differences.Count == 0) ? "same|skeleton" : $"diff|skeleton|bones={differences.Count}|{string.Join(',', differences.Take(12))}{((differences.Count > 12) ? ",..." : string.Empty)}");
			}
		}
		static string Configs(NativeBphclOperatorLayout op)
		{
			return string.Join(";", op.SimulateConfigs.Select((NativeBphclSimulateConfig config) => $"constraints=[{string.Join(',', config.ConstraintExecution)}]|colliders=[{string.Join(',', config.InstanceCollidablesUsed)}]|substeps={config.SubSteps}|iterations={config.NumberOfSolveIterations}|all={config.UseAllInstanceCollidables}|adaptive={config.AdaptConstraintStiffness}"));
		}
		static string F(float value)
		{
			return value.ToString("G9", CultureInfo.InvariantCulture);
		}
		static string Key(string name)
		{
			if (!name.StartsWith("Link:", StringComparison.Ordinal))
			{
				return name;
			}
			return name.Substring(5);
		}
		static string M(Matrix4x4 matrix)
		{
			return string.Join(",", new string[16]
			{
				F(matrix.M11),
				F(matrix.M12),
				F(matrix.M13),
				F(matrix.M14),
				F(matrix.M21),
				F(matrix.M22),
				F(matrix.M23),
				F(matrix.M24),
				F(matrix.M31),
				F(matrix.M32),
				F(matrix.M33),
				F(matrix.M34),
				F(matrix.M41),
				F(matrix.M42),
				F(matrix.M43),
				F(matrix.M44)
			});
		}
		static float MatrixDelta(Matrix4x4 left, Matrix4x4 right)
		{
			float maximum = 0f;
			for (int row = 0; row < 4; row++)
			{
				for (int column = 0; column < 4; column++)
				{
					maximum = Math.Max(maximum, MathF.Abs(left[row, column] - right[row, column]));
				}
			}
			return maximum;
		}
		static string MeshDeformer(NativeBphclSimpleMeshBoneDeform deformer)
		{
			return $"op={deformer.OperatorIndex}|in={deformer.InputBufferIndex}|outSet={deformer.OutputTransformSetIndex}|axis={deformer.BoneAxis}|pairs=[{string.Join(';', deformer.TriangleBonePairs.Select((NativeBphclTriangleBonePair pair) => $"{pair.BoneOffset}:{pair.TriangleOffset}"))}]|local=[{string.Join(';', deformer.LocalBoneTransforms.Select(M))}]";
		}
		static string Numbers(IReadOnlyDictionary<string, double> values)
		{
			return string.Join(",", from pair in values.OrderBy<KeyValuePair<string, double>, string>((KeyValuePair<string, double> pair) => pair.Key, StringComparer.Ordinal)
				select pair.Key + "=" + pair.Value.ToString("G12", CultureInfo.InvariantCulture));
		}
		static string ObjectSkin(NativeBphclObjectSpaceSkin skin)
		{
			return $"op={skin.OperatorIndex}|out={skin.OutputBufferIndex}|set={skin.TransformSetIndex}|subset=[{string.Join(',', skin.TransformSubset)}]|boneFromSkin=[{string.Join(';', skin.BoneFromSkinMeshTransforms.Select(M))}]|deformer={Packed(skin.Deformer.EightBlendEntries)}|{Packed(skin.Deformer.SevenBlendEntries)}|{Packed(skin.Deformer.SixBlendEntries)}|{Packed(skin.Deformer.FiveBlendEntries)}|{Packed(skin.Deformer.FourBlendEntries)}|{Packed(skin.Deformer.ThreeBlendEntries)}|{Packed(skin.Deformer.TwoBlendEntries)}|{Packed(skin.Deformer.OneBlendEntries)}|controls=[{string.Join(',', skin.Deformer.ControlBytes)}]|range={skin.Deformer.StartVertexIndex}:{skin.Deformer.EndVertexIndex}:{skin.Deformer.PartialWrite}|localPs={PackedPositions(skin.LocalPs)}|localUnpacked={Positions(skin.LocalUnpackedPs)}";
		}
		static string Operator(NativeBphclOperatorLayout op)
		{
			return $"{op.ClassName}|in={op.InputBufferIndex}|out={op.OutputBufferIndex}|set={op.TransformSetIndex}|sim={op.SimulationClothIndex}|ref={op.ReferenceBufferIndex}|pairs=[{string.Join(',', op.VertexParticlePairs.Select((NativeBphclVertexParticlePair pair) => $"{pair.VertexIndex}>{pair.ParticleIndex}"))}]|{Configs(op)}";
		}
		static string Packed(IReadOnlyList<NativeBphclPackedArray> arrays)
		{
			return string.Join(";", arrays.Select((NativeBphclPackedArray nativeBphclPackedArray) => $"{nativeBphclPackedArray.ElementType}:{nativeBphclPackedArray.ElementSize}:{nativeBphclPackedArray.Count}:{Convert.ToHexString(SHA256.HashData(nativeBphclPackedArray.Bytes.ToArray()))}"));
		}
		static string PackedPositions(IReadOnlyList<NativeBphclPackedPositionBlock> blocks)
		{
			return string.Join(";", blocks.Select((NativeBphclPackedPositionBlock block) => string.Join(",", block.Positions)));
		}
		static string Positions(IReadOnlyList<NativeBphclPositionBlock> blocks)
		{
			return string.Join(";", blocks.Select((NativeBphclPositionBlock block) => string.Join(",", block.Positions.Select(V))));
		}
		static string Runtime(NativeBphclSimulationRuntime runtime)
		{
			return $"normals={runtime.DoNormals}|maxRadius={F(runtime.MaxParticleRadius)}|totalMass={F(runtime.TotalMass)}|transfer={runtime.TransferMotionEnabled}:{runtime.TransferMotionData.TransformSetIndex}/{runtime.TransferMotionData.TransformIndex}:T={runtime.TransferMotionData.TransferTranslationMotion},{F(runtime.TransferMotionData.MinTranslationSpeed)},{F(runtime.TransferMotionData.MaxTranslationSpeed)},{F(runtime.TransferMotionData.MinTranslationBlend)},{F(runtime.TransferMotionData.MaxTranslationBlend)}:R={runtime.TransferMotionData.TransferRotationMotion},{F(runtime.TransferMotionData.MinRotationSpeed)},{F(runtime.TransferMotionData.MaxRotationSpeed)},{F(runtime.TransferMotionData.MinRotationBlend)},{F(runtime.TransferMotionData.MaxRotationBlend)}|land={runtime.LandscapeCollisionEnabled}:{runtime.NumLandscapeCollidableParticles}:{F(runtime.LandscapeCollisionData.LandscapeRadius)}:{F(runtime.LandscapeCollisionData.CollisionTolerance)}|pinch={runtime.PinchDetectionEnabled}:{runtime.MinPinchedParticleIndex}:{runtime.MaxPinchedParticleIndex}:{runtime.MaxCollisionPairs}";
		}
		static string State(NativeBphclClothState state)
		{
			return $"{state.Name}|ops=[{string.Join(',', state.OperatorItemIndices)}]|buffers=[{string.Join(';', state.BufferAccesses.Select((NativeBphclClothStateBufferAccess access) => $"{access.BufferIndex}/{access.ShadowBufferIndex}:{access.ComponentFlags0},{access.ComponentFlags1},{access.ComponentFlags2},{access.ComponentFlags3}:{access.TrianglesRead}"))}]|sets=[{string.Join(';', state.TransformSetAccesses.Select((NativeBphclClothStateTransformSetAccess access) => $"{access.TransformSetIndex}:{access.ComponentFlags0},{access.ComponentFlags1}:trackers={access.TransformTrackers.Count}"))}]";
		}
		static string V(Vector4 value)
		{
			return $"{F(value.X)},{F(value.Y)},{F(value.Z)},{F(value.W)}";
		}
	}

	private static void SummarizeBphclConstraints(NativeBphclDocument document)
	{
		foreach (var (cloth, clothIndex) in document.Cloths.Select((NativeBphclCloth item, int index) => (cloth: item, index: index)))
		{
			foreach (NativeBphclSimCloth simulation in cloth.SimCloths)
			{
				Console.WriteLine($"cloth={clothIndex}|{cloth.Name}|simulation={simulation.Name}|particles={simulation.Particles.Count}|dynamic={simulation.Particles.Count((NativeBphclParticle particle) => !particle.Fixed)}|triangles={simulation.TriangleCount}|colliders={simulation.CollidableItemIndices.Count}|totalMass={simulation.Runtime.TotalMass:G9}|maxRadius={simulation.Runtime.MaxParticleRadius:G9}|sets={string.Join(',', simulation.ConstraintSets.Select((NativeBphclConstraintSet nativeBphclConstraintSet) => nativeBphclConstraintSet.ClassName))}");
				foreach (NativeBphclConstraintSet set in simulation.ConstraintSets)
				{
					int invalidRefs = CountInvalidParticleRefs(set.Links.Concat(set.LocalConstraints).Concat(set.LocalStiffnessConstraints).Concat(set.TransitionParticles)
						.ToArray(), simulation.Particles.Count);
					int duplicateLinks = CountDuplicateParticlePairs(set.Links);
					Console.WriteLine($"  set={set.Index}|{set.ClassName}|id={set.ConstraintId}|type={set.ConstraintType}|links={set.Links.Count}|local={set.LocalConstraints.Count}|localStiff={set.LocalStiffnessConstraints.Count}|transition={set.TransitionParticles.Count}|invalidRefs={invalidRefs}|duplicateLinks={duplicateLinks}|setValues={FormatNumericSummary(set.Values)}");
					if (set.Links.Count > 0)
					{
						Console.WriteLine("    links=" + FormatRecordSummary(set.Links));
					}
					if (set.LocalConstraints.Count > 0)
					{
						Console.WriteLine("    local=" + FormatRecordSummary(set.LocalConstraints));
					}
					if (set.LocalStiffnessConstraints.Count > 0)
					{
						Console.WriteLine("    localStiff=" + FormatRecordSummary(set.LocalStiffnessConstraints));
					}
					if (set.TransitionParticles.Count > 0)
					{
						Console.WriteLine("    transition=" + FormatRecordSummary(set.TransitionParticles));
					}
				}
			}
		}
		static int CountDuplicateParticlePairs(IReadOnlyList<NativeBphclConstraintLink> links)
		{
			HashSet<(int, int)> seen = new HashSet<(int, int)>();
			int duplicates = 0;
			foreach (NativeBphclConstraintLink link in links)
			{
				int? particleA = link.ParticleA;
				if (particleA.HasValue)
				{
					int a = particleA.GetValueOrDefault();
					particleA = link.ParticleB;
					if (particleA.HasValue)
					{
						int b = particleA.GetValueOrDefault();
						(int, int) key = ((a <= b) ? (a, b) : (b, a));
						if (!seen.Add(key))
						{
							duplicates++;
						}
					}
				}
			}
			return duplicates;
		}
		static int CountInvalidParticleRefs(IEnumerable<NativeBphclConstraintLink> records, int particleCount)
		{
			int count = 0;
			foreach (NativeBphclConstraintLink record in records)
			{
				foreach (int value in GetParticleRefs(record))
				{
					if (value < 0 || value >= particleCount)
					{
						count++;
					}
				}
			}
			return count;
		}
		static string FormatNumericSummary(IEnumerable<KeyValuePair<string, double>> values)
		{
			IEnumerable<string> summaries = values.GroupBy<KeyValuePair<string, double>, string>((KeyValuePair<string, double> value) => value.Key, StringComparer.Ordinal).OrderBy<IGrouping<string, KeyValuePair<string, double>>, string>((IGrouping<string, KeyValuePair<string, double>> group) => group.Key, StringComparer.Ordinal).Select(delegate(IGrouping<string, KeyValuePair<string, double>> group)
			{
				double[] array = group.Select((KeyValuePair<string, double> keyValuePair) => keyValuePair.Value).Where(double.IsFinite).ToArray();
				int value = group.Count((KeyValuePair<string, double> keyValuePair) => !double.IsFinite(keyValuePair.Value));
				if (array.Length != 0)
				{
					int value2 = array.Count((double num) => num < 0.0);
					return $"{group.Key}:n={group.Count()},min={array.Min():G9},max={array.Max():G9},neg={value2},nonfinite={value}";
				}
				return $"{group.Key}:count={group.Count()},nonfinite={value}";
			});
			return string.Join('|', summaries);
		}
		static string FormatRecordSummary(IReadOnlyList<NativeBphclConstraintLink> records)
		{
			IEnumerable<string> pairs = from record in records.Where((NativeBphclConstraintLink record) => record.ParticleA.HasValue || record.ParticleB.HasValue).Take(10)
				select $"{record.Index}:{record.ParticleA?.ToString() ?? "-"}>{record.ParticleB?.ToString() ?? "-"}";
			string pairSuffix = ((records.Count > 10) ? ";..." : string.Empty);
			return $"pairs=[{string.Join(';', pairs)}{pairSuffix}]|values={FormatNumericSummary(records.SelectMany((NativeBphclConstraintLink record) => record.Values))}";
		}
		static IEnumerable<int> GetParticleRefs(NativeBphclConstraintLink record)
		{
			if (record.ParticleA.HasValue)
			{
				yield return record.ParticleA.Value;
			}
			if (record.ParticleB.HasValue)
			{
				yield return record.ParticleB.Value;
			}
			foreach (KeyValuePair<string, double> value in record.Values)
			{
				if (value.Key.StartsWith("particle", StringComparison.OrdinalIgnoreCase))
				{
					yield return checked((int)value.Value);
				}
			}
		}
	}

	private static Matrix4x4 GetBphclBoneWorld(NativeBphclSkeleton skeleton, int boneIndex)
	{
		Dictionary<int, NativeBphclBone> bones = skeleton.Bones.ToDictionary((NativeBphclBone nativeBphclBone) => nativeBphclBone.Index);
		Stack<NativeBphclBone> chain = new Stack<NativeBphclBone>();
		HashSet<int> visited = new HashSet<int>();
		int current = boneIndex;
		NativeBphclBone bone;
		while (current >= 0 && visited.Add(current) && bones.TryGetValue(current, out bone))
		{
			chain.Push(bone);
			current = bone.ParentIndex;
		}
		Matrix4x4 world = Matrix4x4.Identity;
		while (chain.Count > 0)
		{
			NativeBphclBone bone2 = chain.Pop();
			Quaternion rotation = new Quaternion(bone2.Rotation.X, bone2.Rotation.Y, bone2.Rotation.Z, bone2.Rotation.W);
			rotation = ((rotation.LengthSquared() < 1E-06f) ? Quaternion.Identity : Quaternion.Normalize(rotation));
			world = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(bone2.Translation.X, bone2.Translation.Y, bone2.Translation.Z) * world;
		}
		return world;
	}

	private static float MatrixDistance(Matrix4x4 left, Matrix4x4 right)
	{
		float maximum = 0f;
		for (int row = 0; row < 4; row++)
		{
			for (int column = 0; column < 4; column++)
			{
				maximum = Math.Max(maximum, MathF.Abs(left[row, column] - right[row, column]));
			}
		}
		return maximum;
	}
}
