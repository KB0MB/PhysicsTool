using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.IO.Compression;
using HKX2;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace HKCLTool;

public enum HkclPlatform
{
    WiiU,
    Switch
}

public sealed class HkclService
{
    private hkRootLevelContainer? _root;
    private BphclDocumentSummary? _bphcl;
    private BphhbDocumentSummary? _bphhb;
    private string? _path;

    public bool HasDocument => _root != null || _bphcl != null || _bphhb != null;
    public string? SourcePath => _path;
    public bool IsBphcl => _bphcl != null;
    public bool IsBphhb => _bphhb != null;
    public bool IsReadOnlyExternal => IsBphcl || IsBphhb;
    public string CurrentExtension => IsBphcl ? ".bphcl" : IsBphhb ? ".bphhb" : ".hkcl";

    public void Load(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".bphcl")
        {
            _bphcl = BphclBridge.Load(path);
            _bphhb = null;
            _root = null;
            _path = path;
            return;
        }

        if (extension == ".bphhb")
        {
            _bphhb = BphhbBridge.Load(path);
            _bphcl = null;
            _root = null;
            _path = path;
            return;
        }

        _bphcl = null;
        _bphhb = null;
        _root = extension == ".json" ? LoadJson(path) : LoadHkcl(path);
        _path = path;
    }

    public void ExportReadableJson(string path)
    {
        if (_bphcl != null)
        {
            File.WriteAllText(path, BphclBridge.ExportSummary(_bphcl).ToString(Formatting.Indented));
            return;
        }

        if (_bphhb != null)
        {
            File.WriteAllText(path, BphhbBridge.ExportSummary(_bphhb).ToString(Formatting.Indented));
            return;
        }

        RequireRoot();
        File.WriteAllText(path, BuildReadableJson().ToString(Formatting.Indented));
    }

    public void SaveHkcl(string path, HkclPlatform platform)
    {
        if (_bphcl != null)
        {
            if (!Path.GetExtension(path).Equals(".bphcl", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("BPHCL documents must be saved with the .bphcl extension.");

            _bphcl = BphclBridge.Save(_bphcl.SourcePath, path);
            _path = path;
            return;
        }

        if (_bphhb != null)
        {
            _bphhb = BphhbBridge.Save(_bphhb, path);
            _path = path;
            return;
        }

        var root = RequireRoot();
        var header = platform == HkclPlatform.WiiU
            ? HKXHeader.BotwWiiu()
            : HKXHeader.BotwNx();

        using var stream = File.Create(path);
        var writer = new BinaryWriterEx(stream);
        var serializer = new PackFileSerializer();
        serializer.Serialize(root, writer, header);
    }

    public string CaptureState()
    {
        if (IsReadOnlyExternal)
            throw new InvalidOperationException("External physics document state snapshots are not implemented yet.");

        return SerializeRaw(RequireRoot());
    }

    public void RestoreState(string state)
    {
        _bphcl = null;
        _bphhb = null;
        _root = DeserializeRoot(state);
    }

    public string SuggestFileName(string extension)
    {
        var baseName = string.IsNullOrWhiteSpace(_path)
            ? "Physics"
            : Path.GetFileNameWithoutExtension(_path);

        return baseName + extension;
    }

    public IReadOnlyList<string> GetClothSummaries()
    {
        if (_bphcl != null)
            return _bphcl.Cloths.Select((cloth, i) => $"{i}: {cloth.Value<string>("name") ?? $"Cloth {i}"}  |  particles: {cloth.Value<int?>("particleCount") ?? 0}  |  bones: {cloth["skeleton"]?["boneCount"]?.Value<int>() ?? 0}  |  BPHCL").ToList();

        if (_bphhb != null)
            return new[] { $"0: Helper bone configuration  |  bones: {_bphhb.HelperBoneNames.Count}  |  BPHHB" };

        if (_root == null)
            return Array.Empty<string>();

        var cloths = GetClothDatas(_root).ToList();
        var skeletons = GetSkeletons(_root).ToList();
        var result = new List<string>();

        for (var i = 0; i < cloths.Count; i++)
        {
            var name = GetString(cloths[i], "name") ?? $"Cloth {i}";
            var particleCount = GetParticleCount(cloths[i]);
            var boneCount = i < skeletons.Count ? GetList(GetValue(skeletons[i], "bones"))?.Count ?? 0 : 0;
            result.Add($"{i}: {name}  |  particles: {particleCount}  |  bones: {boneCount}");
        }

        return result;
    }

    public IReadOnlyList<string> GetSkeletonBones(int clothIndex)
    {
        if (_bphcl != null)
        {
            var bphclSkeleton = _bphcl.Cloths.ElementAtOrDefault(clothIndex)?["skeleton"];
            var bphclBones = bphclSkeleton?["bones"] as JArray;
            if (bphclBones == null)
                return Array.Empty<string>();
            return bphclBones.Select(b =>
            {
                var index = b.Value<int?>("index") ?? 0;
                var name = b.Value<string>("name") ?? $"Bone {index}";
                var parent = b.Value<int?>("parentIndex") ?? -1;
                var parentName = parent >= 0 && parent < bphclBones.Count ? bphclBones[parent]?["name"]?.Value<string>() ?? "none" : "none";
                return $"{index}: {name}  |  parent: {parentName} ({parent})";
            }).ToList();
        }

        if (_bphhb != null)
            return _bphhb.HelperBoneNames.Select((name, index) => $"{index}: {name}  |  helper bone").ToArray();

        if (_root == null)
            return Array.Empty<string>();

        var skeleton = GetSkeletons(_root).ElementAtOrDefault(clothIndex);
        if (skeleton == null)
            return Array.Empty<string>();

        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var result = new List<string>();

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var name = GetString(bone, "name") ?? $"Bone {i}";
            var parentIndex = ToInt(parents.ElementAtOrDefault(i), -1);
            var parentName = parentIndex >= 0 && parentIndex < bones.Count
                ? GetString(bones[parentIndex], "name") ?? parentIndex.ToString(CultureInfo.InvariantCulture)
                : "none";

            result.Add($"{i}: {name}  |  parent: {parentName} ({parentIndex})");
        }

        return result;
    }

    public string GetClothDetails(int clothIndex)
    {
        if (_bphcl != null)
        {
            var bphclCloth = _bphcl.Cloths.ElementAtOrDefault(clothIndex) as JObject;
            if (bphclCloth == null)
                return string.Empty;
            var bphclBuilder = new StringBuilder();
            bphclBuilder.AppendLine(bphclCloth.Value<string>("name") ?? $"Cloth {clothIndex}");
            bphclBuilder.AppendLine(new string('-', 64));
            bphclBuilder.AppendLine("Format: BPHCL / Phive TAG0");
            bphclBuilder.AppendLine($"Class: {bphclCloth.Value<string>("class")}");
            bphclBuilder.AppendLine($"Particles: {bphclCloth.Value<int?>("particleCount") ?? 0}");
            bphclBuilder.AppendLine($"Operators: {bphclCloth.Value<int?>("operatorCount") ?? 0}");
            bphclBuilder.AppendLine($"States: {bphclCloth.Value<int?>("stateCount") ?? 0}");
            bphclBuilder.AppendLine($"Buffers: {bphclCloth.Value<int?>("bufferCount") ?? 0}");
            bphclBuilder.AppendLine($"Transform sets: {bphclCloth.Value<int?>("transformSetCount") ?? 0}");
            bphclBuilder.AppendLine();
            bphclBuilder.AppendLine("File");
            bphclBuilder.AppendLine($"  Cloths: {_bphcl.ClothCount}");
            bphclBuilder.AppendLine($"  Colliders: {_bphcl.ColliderCount}");
            bphclBuilder.AppendLine($"  Skeletons: {_bphcl.SkeletonCount}");
            bphclBuilder.AppendLine($"  AAMP: {(_bphcl.AampPresent ? "present" : "missing")}");
            var aamp = _bphcl.Raw["aamp"] as JObject;
            if (aamp != null)
            {
                var registered = aamp.Value<bool?>("allTag0ClothsRegistered") ?? false;
                bphclBuilder.AppendLine($"  AAMP live cloth registrations: {(registered ? "complete" : "incomplete")}");
            }
            bphclBuilder.AppendLine();
            bphclBuilder.AppendLine("BPHCL direct viewport editing is read-only. Saving, renaming, removal, and complete-cloth merge use the native BPHCL serializer.");
            return bphclBuilder.ToString();
        }

        if (_bphhb != null)
        {
            var helperBuilder = new StringBuilder();
            helperBuilder.AppendLine("Helper bone configuration");
            helperBuilder.AppendLine(new string('-', 64));
            helperBuilder.AppendLine("Format: BPHHB / AAMP phhb");
            helperBuilder.AppendLine($"Archive version: {_bphhb.ArchiveVersion}");
            helperBuilder.AppendLine($"Helper bones: {_bphhb.HelperBoneNames.Count}");
            helperBuilder.AppendLine($"Lists: {_bphhb.ListCount}");
            helperBuilder.AppendLine($"Objects: {_bphhb.ObjectCount}");
            helperBuilder.AppendLine($"Parameters: {_bphhb.ParameterCount}");
            helperBuilder.AppendLine();
            helperBuilder.AppendLine("This is a native BPHHB inspector. Save is byte-preserving until the AAMP writer and helper-bone editor are implemented.");
            return helperBuilder.ToString();
        }

        if (_root == null)
            return string.Empty;

        var cloth = GetClothDatas(_root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine(GetString(cloth, "name") ?? $"Cloth {clothIndex}");
        builder.AppendLine(new string('-', 64));
        builder.AppendLine($"Class: {cloth.GetType().Name}");
        builder.AppendLine($"Particles: {GetParticleCount(cloth)}");
        builder.AppendLine($"Constraint sets: {GetList(GetValue(cloth, "constraintSets"))?.Count ?? 0}");
        builder.AppendLine($"Operators: {GetList(GetValue(cloth, "operators"))?.Count ?? 0}");
        builder.AppendLine($"Transform sets: {GetList(GetValue(cloth, "transformSetDefinitions"))?.Count ?? 0}");
        builder.AppendLine($"Buffer definitions: {GetList(GetValue(cloth, "bufferDefinitions"))?.Count ?? 0}");
        builder.AppendLine();

        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simData != null)
        {
            builder.AppendLine("Simulation");
            builder.AppendLine($"  Gravity: {FormatSimpleValue(GetValue(simData, "gravity"))}");
            builder.AppendLine($"  Total mass: {FormatSimpleValue(GetValue(simData, "totalMass"))}");
            builder.AppendLine($"  Max particle radius: {FormatSimpleValue(GetValue(simData, "maxParticleRadius"))}");
            builder.AppendLine($"  Collision tolerance: {FormatSimpleValue(GetValue(simData, "collisionTolerance"))}");
            builder.AppendLine();
        }

        var skeleton = GetSkeletons(_root).ElementAtOrDefault(clothIndex);
        if (skeleton != null)
        {
            builder.AppendLine("Skeleton");
            builder.AppendLine($"  Name: {GetString(skeleton, "name") ?? "(unnamed)"}");
            builder.AppendLine($"  Bones: {GetList(GetValue(skeleton, "bones"))?.Count ?? 0}");
        }

        return builder.ToString();
    }

    public IReadOnlyList<ParticleEditRow> GetParticleRows(int clothIndex)
    {
        if (_bphhb != null)
            return Array.Empty<ParticleEditRow>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var simulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            return simulation?.Particles.Select(particle => new ParticleEditRow
            {
                Index = particle.Index,
                Fixed = particle.Fixed,
                X = particle.Position.X,
                Y = particle.Position.Y,
                Z = particle.Position.Z,
                W = particle.Position.W,
                Mass = particle.Mass,
                InverseMass = particle.InverseMass,
                Radius = particle.Radius,
                Friction = particle.Friction,
                CollisionMask = 0
            }).ToArray() ?? Array.Empty<ParticleEditRow>();
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return Array.Empty<ParticleEditRow>();

        return GetParticleRowsForCloth(cloth);
    }

    private static IReadOnlyList<ParticleEditRow> GetParticleRowsForCloth(object cloth)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var particles = GetList(GetValue(simData, "particleDatas")) ?? Array.Empty<object>();
        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetList(GetValue(pose, "positions")) ?? Array.Empty<object>();
        var fixedParticles = new HashSet<int>((GetList(GetValue(simData, "fixedParticles")) ?? Array.Empty<object>()).Select(x => ToInt(x, -1)));
        var collisionMasks = GetList(GetValue(simData, "staticCollisionMasks")) ?? Array.Empty<object>();
        var rows = new List<ParticleEditRow>();

        for (var i = 0; i < particles.Count; i++)
        {
            var particle = particles[i];
            var position = positions.ElementAtOrDefault(i) is Vector4 vector ? vector : Vector4.Zero;
            rows.Add(new ParticleEditRow
            {
                Index = i,
                Fixed = fixedParticles.Contains(i),
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                W = position.W,
                Mass = Convert.ToSingle(GetValue(particle, "mass") ?? 0.0f, CultureInfo.InvariantCulture),
                InverseMass = Convert.ToSingle(GetValue(particle, "invMass") ?? 0.0f, CultureInfo.InvariantCulture),
                Radius = Convert.ToSingle(GetValue(particle, "radius") ?? 0.0f, CultureInfo.InvariantCulture),
                Friction = Convert.ToSingle(GetValue(particle, "friction") ?? 0.0f, CultureInfo.InvariantCulture),
                CollisionMask = ToInt(collisionMasks.ElementAtOrDefault(i), 0)
            });
        }

        return rows;
    }

    public IReadOnlyList<ParticleRelationshipRow> GetParticleRelationships(int clothIndex, int particleIndex)
    {
        if (_bphhb != null)
            return Array.Empty<ParticleRelationshipRow>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var simulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            if (simulation is null || particleIndex < 0)
                return Array.Empty<ParticleRelationshipRow>();

            var nativeRows = new List<ParticleRelationshipRow>
            {
                new()
                {
                    Kind = "State",
                    Name = simulation.Particles.ElementAtOrDefault(particleIndex)?.Fixed == true ? "Fixed anchor" : "Dynamic particle",
                    Particles = particleIndex.ToString(CultureInfo.InvariantCulture),
                    Details = "Native BPHCL simulation particle."
                }
            };

            foreach (var constraintSet in simulation.ConstraintSets)
            {
                foreach (var link in constraintSet.Links.Where(link => link.ParticleA == particleIndex || link.ParticleB == particleIndex))
                {
                    nativeRows.Add(new ParticleRelationshipRow
                    {
                        Kind = "Link",
                        Name = constraintSet.Name,
                        Particles = $"{link.ParticleA}-{link.ParticleB}",
                        Details = string.Join("; ", link.Values.Select(value =>
                            $"{value.Key}={value.Value.ToString("G7", CultureInfo.InvariantCulture)}"))
                    });
                }
            }

            return nativeRows;
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null || particleIndex < 0)
            return Array.Empty<ParticleRelationshipRow>();

        var rows = new List<ParticleRelationshipRow>();
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var fixedParticles = new HashSet<int>((GetList(GetValue(simData, "fixedParticles")) ?? Array.Empty<object>()).Select(x => ToInt(x, -1)));

        rows.Add(new ParticleRelationshipRow
        {
            Kind = "State",
            Name = fixedParticles.Contains(particleIndex) ? "Fixed anchor" : "Dynamic particle",
            Particles = particleIndex.ToString(CultureInfo.InvariantCulture),
            Details = fixedParticles.Contains(particleIndex)
                ? "Pinned to animation/root motion; mass and inverse mass are normally 0."
                : "Simulated by cloth constraints; mass, radius, and links affect motion."
        });

        AddTriangleRelationships(rows, simData, particleIndex);
        AddConstraintRelationships(rows, simData, cloth, particleIndex);

        return rows;
    }

    public IReadOnlyList<BoneEditRow> GetBoneRows(int clothIndex)
    {
        if (_bphhb != null)
            return Array.Empty<BoneEditRow>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var nativeSkeleton = bphcl.Skeletons.ElementAtOrDefault(clothIndex);
            return nativeSkeleton?.Bones.Select(bone => new BoneEditRow
            {
                Index = bone.Index,
                Name = bone.Name,
                ParentIndex = bone.ParentIndex,
                X = bone.Translation.X,
                Y = bone.Translation.Y,
                Z = bone.Translation.Z,
                RotationX = bone.Rotation.X,
                RotationY = bone.Rotation.Y,
                RotationZ = bone.Rotation.Z,
                RotationW = bone.Rotation.W
            }).ToArray() ?? Array.Empty<BoneEditRow>();
        }

        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex);
        if (skeleton == null)
            return Array.Empty<BoneEditRow>();

        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var poses = GetList(GetValue(skeleton, "referencePose")) ?? Array.Empty<object>();
        var result = new List<BoneEditRow>();

        for (var i = 0; i < bones.Count; i++)
        {
            var pose = poses.ElementAtOrDefault(i) is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
            result.Add(new BoneEditRow
            {
                Index = i,
                Name = GetString(bones[i], "name") ?? $"Bone {i}",
                ParentIndex = ToInt(parents.ElementAtOrDefault(i), -1),
                X = pose.M11,
                Y = pose.M12,
                Z = pose.M13,
                RotationX = pose.M21,
                RotationY = pose.M22,
                RotationZ = pose.M23,
                RotationW = pose.M24,
                ScaleX = pose.M31,
                ScaleY = pose.M32,
                ScaleZ = pose.M33
            });
        }

        return result;
    }

    public void UpdateBoneRows(int clothIndex, IEnumerable<BoneEditRow> rows)
    {
        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetValue(skeleton, "parentIndices") as IList;
        var poses = GetValue(skeleton, "referencePose") as IList;

        foreach (var row in rows)
        {
            if (row.Index < 0 || row.Index >= bones.Count)
                continue;

            SetValue(bones[row.Index], "name", row.Name);
            if (parents != null && row.Index < parents.Count)
                SetListItem(parents, row.Index, row.ParentIndex);

            if (poses != null && row.Index < poses.Count)
            {
                var existing = poses[row.Index] is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
                SetListItem(poses, row.Index, new Matrix4x4(
                    row.X, row.Y, row.Z, existing.M14,
                    row.RotationX, row.RotationY, row.RotationZ, row.RotationW,
                    row.ScaleX, row.ScaleY, row.ScaleZ, existing.M34,
                    existing.M41, existing.M42, existing.M43, existing.M44));
            }
        }
    }

    public IReadOnlyList<ColliderEditRow> GetColliderRows(int clothIndex)
    {
        if (_bphhb != null)
            return Array.Empty<ColliderEditRow>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var nativeBones = bphcl.Skeletons.ElementAtOrDefault(clothIndex)?.Bones ?? Array.Empty<NativeBphclBone>();
            var referencedColliderItems = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths
                .SelectMany(simulation => simulation.CollidableItemIndices)
                .ToHashSet() ?? new HashSet<int>();
            return bphcl.Colliders
                .Where(collider => referencedColliderItems.Contains(collider.ItemIndex))
                .Select(collider =>
            {
                var boneIndex = ResolveColliderBoneIndex(collider.Name, nativeBones.Select(bone => (bone.Index, bone.Name)), -1);
                return new ColliderEditRow
                {
                    Index = collider.Index,
                    Name = collider.Name,
                    BoneIndex = boneIndex,
                    BoneName = nativeBones.FirstOrDefault(bone => bone.Index == boneIndex)?.Name ?? string.Empty,
                    StartX = TransformNativeColliderPoint(collider, collider.Shape.Start).X,
                    StartY = TransformNativeColliderPoint(collider, collider.Shape.Start).Y,
                    StartZ = TransformNativeColliderPoint(collider, collider.Shape.Start).Z,
                    EndX = TransformNativeColliderPoint(collider, collider.Shape.End).X,
                    EndY = TransformNativeColliderPoint(collider, collider.Shape.End).Y,
                    EndZ = TransformNativeColliderPoint(collider, collider.Shape.End).Z,
                    Radius = collider.Shape.Radius
                };
            }).ToArray();
        }

        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex);
        var bones = skeleton == null
            ? Array.Empty<object>()
            : GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var referencedCollidables = new HashSet<object>(EnumerateReferencedCollidables(GetClothDatas(root).ElementAtOrDefault(clothIndex)!), ReferenceEquality.Instance);
        var collidables = GetCollidables(root);
        var result = new List<ColliderEditRow>();

        for (var i = 0; i < collidables.Count; i++)
        {
            var collidable = collidables[i];
            if (!referencedCollidables.Contains(collidable))
                continue;
            var shape = GetValue(collidable, "shape");
            var start = GetValue(shape, "start") is Vector4 startVector ? startVector : Vector4.Zero;
            var end = GetValue(shape, "end") is Vector4 endVector ? endVector : Vector4.Zero;
            var name = GetString(collidable, "name") ?? $"Collider {i}";
            var boneIndex = ResolveColliderBoneIndex(name, bones, ToInt(GetValue(collidable, "transformIndex"), -1));
            result.Add(new ColliderEditRow
            {
                Index = i,
                Name = name,
                BoneIndex = boneIndex,
                BoneName = boneIndex >= 0 && boneIndex < bones.Count ? GetString(bones[boneIndex], "name") ?? string.Empty : string.Empty,
                StartX = start.X,
                StartY = start.Y,
                StartZ = start.Z,
                EndX = end.X,
                EndY = end.Y,
                EndZ = end.Z,
                Radius = Convert.ToSingle(GetValue(shape, "radius") ?? 0.0f, CultureInfo.InvariantCulture)
            });
        }

        return result;
    }

    public void UpdateColliderRows(IEnumerable<ColliderEditRow> rows)
    {
        var root = RequireRoot();
        var collidables = GetCollidables(root);
        foreach (var row in rows)
        {
            if (row.Index < 0 || row.Index >= collidables.Count)
                continue;

            var collidable = collidables[row.Index];
            SetValue(collidable, "name", row.Name);
            SetValue(collidable, "transformIndex", row.BoneIndex);

            var shape = GetValue(collidable, "shape");
            if (shape == null)
                continue;

            var start = new Vector4(row.StartX, row.StartY, row.StartZ, 0.0f);
            var end = new Vector4(row.EndX, row.EndY, row.EndZ, 0.0f);
            SetValue(shape, "start", start);
            SetValue(shape, "end", end);
            SetValue(shape, "radius", row.Radius);

            UpdateCapsuleDerivedValues(shape, start, end);
        }
    }

    public ParticlePreviewData GetParticlePreview(int clothIndex)
    {
        var preview = new ParticlePreviewData();
        if (_bphhb != null)
            return preview;

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var nativeCloth = bphcl.Cloths.ElementAtOrDefault(clothIndex);
            var simulation = nativeCloth?.SimCloths.FirstOrDefault();
            if (simulation is null)
                return preview;

            foreach (var particle in simulation.Particles)
            {
                preview.Particles.Add(new ParticlePreviewPoint
                {
                    Index = particle.Index,
                    Fixed = particle.Fixed,
                    Position = new Vector3(particle.Position.X, particle.Position.Y, particle.Position.Z),
                    Radius = particle.Radius
                });
            }

            foreach (var constraintSet in simulation.ConstraintSets)
            {
                foreach (var link in constraintSet.Links.Where(link => link.ParticleA.HasValue && link.ParticleB.HasValue))
                {
                    preview.Links.Add(new ParticlePreviewLink
                    {
                        ParticleA = link.ParticleA!.Value,
                        ParticleB = link.ParticleB!.Value,
                        Kind = constraintSet.ClassName
                    });
                }
            }

            AddNativeBphclPreviewSkeleton(preview, bphcl.Skeletons.ElementAtOrDefault(clothIndex));
            AddNativeBphclPreviewColliders(preview, bphcl, clothIndex);
            return preview;
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return preview;

        foreach (var row in GetParticleRows(clothIndex))
        {
            preview.Particles.Add(new ParticlePreviewPoint
            {
                Index = row.Index,
                Fixed = row.Fixed,
                Position = new Vector3(row.X, row.Y, row.Z),
                Radius = row.Radius
            });
        }

        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        AddPreviewTriangles(preview, simData);
        AddPreviewLinks(preview, simData, cloth);
        AddPreviewSkeleton(preview, root, clothIndex);
        AddPreviewColliders(preview, root, cloth);
        return preview;
    }

    public void UpdateParticleRows(int clothIndex, IEnumerable<ParticleEditRow> rows)
    {
        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        ApplyParticleRows(cloth, rows);
    }

    public int AddParticle(int clothIndex, ParticleEditRow? sourceRow = null)
    {
        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simData = GetFirst(GetValue(cloth, "simClothDatas")) ?? throw new InvalidOperationException("No simulation cloth data found.");
        var particles = GetMutableList(GetValue(simData, "particleDatas"), "particleDatas");
        if (particles.Count == 0)
            throw new InvalidOperationException("Cannot add a particle because this cloth has no particle template to clone.");

        var newIndex = particles.Count;
        var sourceIndex = sourceRow?.Index >= 0 && sourceRow.Index < particles.Count ? sourceRow.Index : particles.Count - 1;
        var template = CloneForCurrentGraph(particles[sourceIndex]!);
        particles.Add(template);

        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetValue(pose, "positions") as IList;
        var sourcePosition = positions != null && sourceIndex < positions.Count && positions[sourceIndex] is Vector4 vector
            ? vector
            : Vector4.Zero;
        AddListItem(positions, sourceRow == null
            ? new Vector4(sourcePosition.X + 0.03f, sourcePosition.Y, sourcePosition.Z, sourcePosition.W == 0.0f ? 1.0f : sourcePosition.W)
            : new Vector4(sourceRow.X + 0.03f, sourceRow.Y, sourceRow.Z, sourceRow.W == 0.0f ? 1.0f : sourceRow.W));

        var collisionMasks = GetValue(simData, "staticCollisionMasks") as IList;
        AddListItem(collisionMasks, sourceRow?.CollisionMask ?? 255);

        AddMatchingListDefault(simData, "perParticlePinchDetectionEnabledFlags", false);
        AddMatchingListDefault(simData, "particlePinchDetectionEnabledFlags", false);

        var rows = GetParticleRows(clothIndex).ToList();
        var row = rows.FirstOrDefault(x => x.Index == newIndex);
        if (row != null)
        {
            row.Fixed = sourceRow?.Fixed ?? false;
            row.Mass = sourceRow?.Mass ?? row.Mass;
            row.InverseMass = sourceRow?.InverseMass ?? row.InverseMass;
            row.Radius = sourceRow?.Radius ?? row.Radius;
            row.Friction = sourceRow?.Friction ?? row.Friction;
            row.CollisionMask = sourceRow?.CollisionMask ?? row.CollisionMask;
        }
        ApplyParticleRows(cloth, rows);
        return newIndex;
    }

    public int AddBone(int clothIndex, BoneEditRow? sourceRow = null)
    {
        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var bones = GetMutableList(GetValue(skeleton, "bones"), "bones");
        var parents = GetValue(skeleton, "parentIndices") as IList ?? throw new InvalidOperationException("Skeleton parent list is not editable.");
        var poses = GetValue(skeleton, "referencePose") as IList ?? throw new InvalidOperationException("Skeleton reference pose list is not editable.");
        if (bones.Count == 0)
            throw new InvalidOperationException("Cannot add a bone because this skeleton has no bone template to clone.");

        var newIndex = bones.Count;
        var sourceIndex = sourceRow?.Index >= 0 && sourceRow.Index < bones.Count ? sourceRow.Index : bones.Count - 1;
        var template = CloneForCurrentGraph(bones[sourceIndex]!);
        SetValue(template, "name", $"New_Bone_{newIndex}");
        bones.Add(template);

        var parentIndex = sourceRow?.Index ?? ToInt(parents.Cast<object>().ElementAtOrDefault(sourceIndex), -1);
        AddListItem(parents, parentIndex);

        var sourcePose = poses[sourceIndex] is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
        var offsetPose = sourceRow == null
            ? new Matrix4x4(
                sourcePose.M11, sourcePose.M12 + 0.03f, sourcePose.M13, sourcePose.M14,
                sourcePose.M21, sourcePose.M22, sourcePose.M23, sourcePose.M24,
                sourcePose.M31, sourcePose.M32, sourcePose.M33, sourcePose.M34,
                sourcePose.M41, sourcePose.M42, sourcePose.M43, sourcePose.M44)
            : new Matrix4x4(
                sourceRow.X, sourceRow.Y + 0.03f, sourceRow.Z, sourcePose.M14,
                sourceRow.RotationX, sourceRow.RotationY, sourceRow.RotationZ, sourceRow.RotationW,
                sourceRow.ScaleX, sourceRow.ScaleY, sourceRow.ScaleZ, sourcePose.M34,
                sourcePose.M41, sourcePose.M42, sourcePose.M43, sourcePose.M44);
        AddListItem(poses, offsetPose);
        return newIndex;
    }

    public int AddCollider(int clothIndex, ColliderEditRow? sourceRow = null, BoneEditRow? targetBone = null)
    {
        var root = RequireRoot();
        var collidables = GetMutableCollidables(root);
        if (collidables.Count == 0)
            throw new InvalidOperationException("Cannot add a collider because this file has no collider template to clone.");

        var newIndex = collidables.Count;
        var sourceIndex = sourceRow?.Index >= 0 && sourceRow.Index < collidables.Count ? sourceRow.Index : collidables.Count - 1;
        var template = CloneForCurrentGraph(collidables[sourceIndex]!);
        var boneIndex = targetBone?.Index ?? sourceRow?.BoneIndex ?? 0;
        var boneName = targetBone?.Name ?? sourceRow?.BoneName ?? "Root";
        SetValue(template, "name", $"Collidable_{boneName}_{newIndex}");
        SetValue(template, "transformIndex", boneIndex);

        var shape = GetValue(template, "shape");
        if (shape != null)
        {
            var start = sourceRow == null ? Vector4.Zero : new Vector4(sourceRow.StartX, sourceRow.StartY, sourceRow.StartZ, 0.0f);
            var end = sourceRow == null ? new Vector4(0.0f, 0.08f, 0.0f, 0.0f) : new Vector4(sourceRow.EndX, sourceRow.EndY + 0.02f, sourceRow.EndZ, 0.0f);
            SetValue(shape, "start", start);
            SetValue(shape, "end", end);
            SetValue(shape, "radius", sourceRow?.Radius ?? 0.05f);
            UpdateCapsuleDerivedValues(shape, start, end);
        }

        collidables.Add(template);

        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var perInstance = GetValue(simData, "perInstanceCollidables") as IList;
        AddListItem(perInstance, template);
        return newIndex;
    }
    public void DeleteParticle(int clothIndex, int particleIndex)
    {
        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simData = GetFirst(GetValue(cloth, "simClothDatas")) ?? throw new InvalidOperationException("No simulation cloth data found.");
        if (ParticleIsReferenced(simData, cloth, particleIndex))
            throw new InvalidOperationException("That particle is used by triangles or constraints. Delete the links/triangles first, then delete the particle.");

        RemoveParticleFromSimulation(simData, particleIndex);
        ReindexFixedParticles(simData, particleIndex);
    }

    public void DeleteBone(int clothIndex, int boneIndex)
    {
        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var bones = GetMutableList(GetValue(skeleton, "bones"), "bones");
        var parents = GetValue(skeleton, "parentIndices") as IList ?? throw new InvalidOperationException("Skeleton parent list is not editable.");
        var poses = GetValue(skeleton, "referencePose") as IList ?? throw new InvalidOperationException("Skeleton reference pose list is not editable.");
        if (boneIndex < 0 || boneIndex >= bones.Count)
            throw new ArgumentOutOfRangeException(nameof(boneIndex));

        for (var i = 0; i < parents.Count; i++)
        {
            if (ToInt(parents[i], -1) == boneIndex)
                throw new InvalidOperationException("That bone has child bones. Delete or reparent the child bones first.");
        }

        foreach (var collider in GetColliderRows(clothIndex))
        {
            if (collider.BoneIndex == boneIndex)
                throw new InvalidOperationException("That bone has a collider bound to it. Delete or move the collider first.");
        }

        bones.RemoveAt(boneIndex);
        parents.RemoveAt(boneIndex);
        poses.RemoveAt(boneIndex);
        for (var i = 0; i < parents.Count; i++)
        {
            var parent = ToInt(parents[i], -1);
            if (parent > boneIndex)
                SetListItem(parents, i, parent - 1);
        }
    }

    public void DeleteCollider(int clothIndex, int colliderIndex)
    {
        var root = RequireRoot();
        var collidables = GetMutableCollidables(root);
        if (colliderIndex < 0 || colliderIndex >= collidables.Count)
            throw new ArgumentOutOfRangeException(nameof(colliderIndex));

        var target = collidables[colliderIndex];
        foreach (var cloth in GetClothDatas(root))
        {
            foreach (var simData in GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>())
            {
                if (GetValue(simData, "perInstanceCollidables") is not IList perInstance)
                    continue;

                for (var i = perInstance.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(perInstance[i], target))
                        perInstance.RemoveAt(i);
                }
            }
        }

        collidables.RemoveAt(colliderIndex);
    }

    public void LinkParticles(int clothIndex, IReadOnlyList<int> particleIndices)
    {
        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simData = GetFirst(GetValue(cloth, "simClothDatas")) ?? throw new InvalidOperationException("No simulation cloth data found.");
        if (particleIndices.Count == 2)
        {
            AddConstraintLink(simData, cloth, particleIndices[0], particleIndices[1]);
            return;
        }

        if (particleIndices.Count == 3)
        {
            AddTriangle(simData, particleIndices[0], particleIndices[1], particleIndices[2]);
            AddConstraintLink(simData, cloth, particleIndices[0], particleIndices[1]);
            AddConstraintLink(simData, cloth, particleIndices[1], particleIndices[2]);
            AddConstraintLink(simData, cloth, particleIndices[2], particleIndices[0]);
            return;
        }

        throw new InvalidOperationException("Select exactly 2 particles for a link, or exactly 3 particles for a triangle.");
    }

    private static bool ParticleIsReferenced(object simData, object cloth, int particleIndex)
    {
        var triangles = GetList(GetValue(simData, "triangleIndices")) ?? Array.Empty<object>();
        if (triangles.Any(x => ToInt(x, -1) == particleIndex))
            return true;

        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        foreach (var constraint in constraints)
        {
            var links = GetList(GetValue(constraint, "links"));
            if (links != null && links.Any(link => ToInt(GetValue(link, "particleA"), -1) == particleIndex || ToInt(GetValue(link, "particleB"), -1) == particleIndex))
                return true;

            var locals = GetList(GetValue(constraint, "localConstraints"));
            if (locals != null && locals.Any(local => ToInt(GetValue(local, "particleIndex"), -1) == particleIndex))
                return true;
        }

        return false;
    }

    private static void RemoveParticleFromSimulation(object simData, int particleIndex)
    {
        var particles = GetMutableList(GetValue(simData, "particleDatas"), "particleDatas");
        if (particleIndex < 0 || particleIndex >= particles.Count)
            throw new ArgumentOutOfRangeException(nameof(particleIndex));
        particles.RemoveAt(particleIndex);

        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        if (GetValue(pose, "positions") is IList positions && particleIndex < positions.Count)
            positions.RemoveAt(particleIndex);
        if (GetValue(simData, "staticCollisionMasks") is IList masks && particleIndex < masks.Count)
            masks.RemoveAt(particleIndex);
        if (GetValue(simData, "perParticlePinchDetectionEnabledFlags") is IList pinch && particleIndex < pinch.Count)
            pinch.RemoveAt(particleIndex);
        if (GetValue(simData, "particlePinchDetectionEnabledFlags") is IList pinch2 && particleIndex < pinch2.Count)
            pinch2.RemoveAt(particleIndex);
    }

    private static void ReindexFixedParticles(object simData, int removedIndex)
    {
        if (GetValue(simData, "fixedParticles") is not IList fixedParticles)
            return;

        var values = fixedParticles.Cast<object>()
            .Select(x => ToInt(x, -1))
            .Where(x => x >= 0 && x != removedIndex)
            .Select(x => x > removedIndex ? x - 1 : x)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        fixedParticles.Clear();
        foreach (var value in values)
            AddListItem(fixedParticles, value);
    }

    private static void AddTriangle(object simData, int a, int b, int c)
    {
        var indices = GetValue(simData, "triangleIndices") as IList;
        if (indices == null)
            throw new InvalidOperationException("This cloth does not expose editable triangle indices.");

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var existing = new[] { ToInt(indices[i], -1), ToInt(indices[i + 1], -1), ToInt(indices[i + 2], -1) }.OrderBy(x => x).ToArray();
            var incoming = new[] { a, b, c }.OrderBy(x => x).ToArray();
            if (existing.SequenceEqual(incoming))
                return;
        }

        AddListItem(indices, a);
        AddListItem(indices, b);
        AddListItem(indices, c);
    }

    private static void AddConstraintLink(object simData, object cloth, int a, int b)
    {
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        var target = constraints.FirstOrDefault(c => GetList(GetValue(c, "links"))?.Count > 0 && c.GetType().Name.Contains("Standard", StringComparison.OrdinalIgnoreCase))
            ?? constraints.FirstOrDefault(c => GetList(GetValue(c, "links"))?.Count > 0);
        if (target == null)
            throw new InvalidOperationException("No editable link constraint set was found.");

        var links = GetMutableList(GetValue(target, "links"), "links");
        foreach (var link in links)
        {
            var existingA = ToInt(GetValue(link, "particleA"), -1);
            var existingB = ToInt(GetValue(link, "particleB"), -1);
            if ((existingA == a && existingB == b) || (existingA == b && existingB == a))
                return;
        }

        var template = CloneForCurrentGraph(links[0]!);
        SetValue(template, "particleA", a);
        SetValue(template, "particleB", b);
        SetValue(template, "restLength", GetParticleDistance(simData, a, b));
        SetValue(template, "stiffness", 1.0f);
        links.Add(template);
    }

    private static float GetParticleDistance(object simData, int a, int b)
    {
        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetList(GetValue(pose, "positions"));
        if (positions == null || a < 0 || b < 0 || a >= positions.Count || b >= positions.Count)
            return 0.0f;

        var pa = positions[a] is Vector4 va ? va : Vector4.Zero;
        var pb = positions[b] is Vector4 vb ? vb : Vector4.Zero;
        var dx = pa.X - pb.X;
        var dy = pa.Y - pb.Y;
        var dz = pa.Z - pb.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static void AddMatchingListDefault(object owner, string fieldName, object defaultValue)
    {
        if (GetValue(owner, fieldName) is IList list)
            AddListItem(list, defaultValue);
    }

    private static void UpdateCapsuleDerivedValues(object shape, Vector4 start, Vector4 end)
    {
        var delta = end - start;
        var lengthSq = delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;
        if (lengthSq <= 0.000001f)
            return;

        var length = MathF.Sqrt(lengthSq);
        SetValue(shape, "dir", new Vector4(delta.X / length, delta.Y / length, delta.Z / length, 0.0f));
        SetValue(shape, "capLenSqrdInv", 1.0f / lengthSq);
    }

    public void RemoveCloth(int index)
    {
        if (_bphhb != null)
            throw new InvalidOperationException("BPHHB contains helper-bone rules, not removable cloth entries.");

        if (_bphcl != null)
        {
            if (index < 0 || index >= _bphcl.ClothCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            var tempPath = CreateTemporaryBphclPath();
            _bphcl = BphclBridge.DeleteCloth(_bphcl.SourcePath, tempPath, index);
            return;
        }

        var root = RequireRoot();
        var cloths = GetMutableClothDatas(root);
        if (index < 0 || index >= cloths.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        cloths.RemoveAt(index);

        var skeletons = GetMutableSkeletons(root);
        if (index >= 0 && index < skeletons.Count)
            skeletons.RemoveAt(index);
    }

    public void RenameCloth(int index, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A cloth name cannot be empty.", nameof(name));

        if (_bphhb != null)
            throw new InvalidOperationException("BPHHB helper-bone names are read-only until the native AAMP writer is implemented.");

        if (_bphcl != null)
        {
            if (index < 0 || index >= _bphcl.ClothCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            var tempPath = CreateTemporaryBphclPath();
            _bphcl = BphclBridge.RenameCloth(_bphcl.SourcePath, tempPath, index, name.Trim());
            return;
        }

        var cloths = GetMutableClothDatas(RequireRoot());
        if (index < 0 || index >= cloths.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        SetValue(cloths[index], "name", name.Trim());
    }

    private string CreateTemporaryBphclPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PhysicsTool");
        Directory.CreateDirectory(directory);
        var baseName = string.IsNullOrWhiteSpace(_path)
            ? "Edited"
            : Path.GetFileNameWithoutExtension(_path);
        return Path.Combine(directory, $"{baseName}.{Guid.NewGuid():N}.bphcl");
    }

    // Returns a human-readable gate for the conservative BPHCL -> HKCL
    // converter. It is deliberately based on the same requirements used by
    // the writer, so a green result means the conversion is eligible rather
    // than merely visually similar in the viewport.
    public string DescribeMergePreflight(HkclService reference, int clothIndex, int templateClothIndex = 0)
    {
        if (_root != null && reference._bphcl?.NativeDocument is { } sourceDocument)
            return DescribeBphclToHkclPreflight(sourceDocument, clothIndex, templateClothIndex);

        if (_bphcl != null && reference._bphcl != null)
            return GetBphclMergePreflight(reference, clothIndex).ToDisplayText();

        return "The selected physics files use the same native format. " +
               "The selected cloth will be copied with its paired skeleton and referenced colliders.";
    }

    internal NativeBphclMergePreflight GetBphclMergePreflight(HkclService reference, int clothIndex)
    {
        if (_bphcl?.NativeDocument is not { } target || reference._bphcl?.NativeDocument is not { } source)
            throw new InvalidOperationException("BPHCL merge preflight requires a BPHCL target and reference.");
        return NativeBphclMergePreflight.Analyze(target, source, clothIndex);
    }

    private string DescribeBphclToHkclPreflight(
        NativeBphclDocument sourceDocument,
        int sourceClothIndex,
        int templateClothIndex)
    {
        var root = RequireRoot();
        var sourceCloth = sourceDocument.Cloths.ElementAtOrDefault(sourceClothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(sourceClothIndex));
        var sourceSkeleton = sourceDocument.Skeletons.ElementAtOrDefault(sourceClothIndex);
        var sourceSimulation = sourceCloth.SimCloths.FirstOrDefault();
        var template = GetClothDatas(root).ElementAtOrDefault(templateClothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(templateClothIndex));
        var templateSkeleton = GetSkeletons(root).ElementAtOrDefault(templateClothIndex);

        var templateName = GetString(template, "name") ?? $"cloth {templateClothIndex}";
        var lines = new List<string>
        {
            "BPHCL -> HKCL conversion preflight",
            string.Empty,
            $"Source: {StripBphclPrefix(sourceCloth.Name)}",
            $"HKCL template: {templateName}",
            string.Empty
        };
        var ready = true;

        void Check(bool passed, string description)
        {
            lines.Add((passed ? "OK" : "Needs template") + ": " + description);
            ready &= passed;
        }

        Check(sourceCloth.SimClothCount == 1,
            $"one simulation cloth (source has {sourceCloth.SimClothCount})");
        if (sourceSimulation == null)
        {
            lines.Add("Needs template: source has no readable simulation data.");
            ready = false;
        }
        else
        {
            Check(sourceSimulation.Particles.Count == GetParticleCount(template),
                $"particle count ({sourceSimulation.Particles.Count} source / {GetParticleCount(template)} template)");
        }

        var templateBones = templateSkeleton is null
            ? Array.Empty<object>()
            : (GetList(GetValue(templateSkeleton, "bones")) ?? Array.Empty<object>());
        var sourceBoneNames = sourceSkeleton?.Bones
            .Select(bone => StripBphclPrefix(bone.Name))
            .ToArray() ?? Array.Empty<string>();
        var templateBoneNames = templateBones
            .Select(bone => GetString(bone, "name") ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingBones = sourceBoneNames
            .Where(name => !templateBoneNames.Contains(name))
            .ToArray();
        Check(sourceSkeleton != null && sourceBoneNames.Length == templateBones.Count,
            $"bone count ({sourceBoneNames.Length} source / {templateBones.Count} template)");
        Check(missingBones.Length == 0,
            missingBones.Length == 0
                ? "every source bone name exists in the template"
                : $"missing bone names: {string.Join(", ", missingBones.Take(6))}{(missingBones.Length > 6 ? ", ..." : string.Empty)}");

        var sourceColliders = sourceSimulation == null
            ? Array.Empty<NativeBphclCollider>()
            : sourceDocument.Colliders
                .Where(collider => sourceSimulation.CollidableItemIndices.Contains(collider.ItemIndex))
                .ToArray();
        var templateColliderCount = EnumerateReferencedCollidables(template)
            .Distinct(ReferenceEquality.Instance)
            .Count();
        Check(sourceColliders.Length == templateColliderCount,
            $"referenced colliders ({sourceColliders.Length} source / {templateColliderCount} template)");
        var unsupportedShapes = sourceColliders
            .Select(collider => collider.Shape.TypeName)
            .Where(type => type is not ("hclCapsuleShape" or "hclSphereShape" or "hclPlaneShape"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Check(unsupportedShapes.Length == 0,
            unsupportedShapes.Length == 0
                ? "collider shapes are supported by this converter"
                : $"unsupported shapes: {string.Join(", ", unsupportedShapes)}");

        if (sourceSimulation != null)
        {
            var targetSimulation = GetFirst(GetValue(template, "simClothDatas"));
            var targetSets = GetList(GetValue(targetSimulation, "staticConstraintSets"))
                ?? GetList(GetValue(template, "constraintSets"))
                ?? Array.Empty<object>();
            foreach (var sourceSet in sourceSimulation.ConstraintSets.Where(set => set.Links.Count > 0))
            {
                var matches = targetSets.Any(set =>
                    string.Equals(set.GetType().Name, sourceSet.ClassName, StringComparison.Ordinal) &&
                    (GetList(GetValue(set, "links"))?.Count ?? 0) == sourceSet.Links.Count);
                Check(matches, $"{sourceSet.ClassName} link layout ({sourceSet.Links.Count} links)");
            }
        }

        lines.Add(string.Empty);
        lines.Add(ready
            ? "Ready: conversion will clone the template and replace its skeleton, particles, matching collider values, and matching constraint links."
            : "Not ready: choose a closer HKCL template. No conversion will be attempted until every check is OK.");
        return string.Join(Environment.NewLine, lines);
    }

    public string MergeClothFrom(HkclService reference, int clothIndex, int templateClothIndex = 0)
    {
        if (_bphhb != null || reference._bphhb != null)
            throw new InvalidOperationException("BPHHB helper-bone merging is not implemented yet. It needs its own AAMP merge path, separate from cloth merging.");

        if (_bphcl != null || reference._bphcl != null)
        {
            // This is deliberately a template conversion, not a binary-format
            // transplant. The selected HKCL cloth keeps the data BPHCL does not
            // expose yet (deformers, buffers, and operator layout).
            if (_bphcl == null && reference._bphcl?.NativeDocument is { } bphcl)
                return ConvertBphclClothFrom(reference, bphcl, clothIndex, templateClothIndex);

            if (_bphcl == null || reference._bphcl == null)
                throw new InvalidOperationException("BPHCL -> HKCL conversion requires an HKCL target and a BPHCL reference. The reverse direction is not implemented yet.");
            if (clothIndex < 0 || clothIndex >= reference._bphcl.ClothCount)
                throw new ArgumentOutOfRangeException(nameof(clothIndex));

            var tempPath = CreateTemporaryBphclPath();
            _bphcl = BphclBridge.MergeCloth(_bphcl.SourcePath, reference._bphcl.SourcePath, tempPath, clothIndex);
            return "Merged selected BPHCL cloth.";
        }

        var root = RequireRoot();
        var refRoot = reference.RequireRoot();

        var sourceCloths = GetClothDatas(refRoot).ToList();
        if (clothIndex < 0 || clothIndex >= sourceCloths.Count)
            throw new ArgumentOutOfRangeException(nameof(clothIndex));

        var targetCloths = GetMutableClothDatas(root);
        targetCloths.Add(CloneForCurrentGraph(sourceCloths[clothIndex]));

        var sourceSkeleton = GetSkeletons(refRoot).ElementAtOrDefault(clothIndex);
        if (sourceSkeleton != null)
            GetMutableSkeletons(root).Add(CloneForCurrentGraph(sourceSkeleton));

        var targetCollidables = GetMutableCollidables(root);
        foreach (var collidable in EnumerateReferencedCollidables(sourceCloths[clothIndex]))
            targetCollidables.Add(CloneForCurrentGraph(collidable));

        return "Merged selected HKCL cloth.";
    }

    private string ConvertBphclClothFrom(
        HkclService reference,
        NativeBphclDocument sourceDocument,
        int sourceClothIndex,
        int templateClothIndex)
    {
        var root = RequireRoot();
        var sourceCloth = sourceDocument.Cloths.ElementAtOrDefault(sourceClothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(sourceClothIndex));
        var sourceSimulation = sourceCloth.SimCloths.FirstOrDefault()
            ?? throw new InvalidOperationException("The selected BPHCL cloth has no simulation data to convert.");
        var sourceSkeleton = sourceDocument.Skeletons.ElementAtOrDefault(sourceClothIndex)
            ?? throw new InvalidOperationException("The selected BPHCL cloth has no matching skeleton.");

        var templates = GetClothDatas(root).ToList();
        var template = templates.ElementAtOrDefault(templateClothIndex)
            ?? throw new InvalidOperationException("Select an HKCL cloth to use as the conversion template.");
        var templateSkeleton = GetSkeletons(root).ElementAtOrDefault(templateClothIndex)
            ?? throw new InvalidOperationException("The selected HKCL template has no matching skeleton.");

        var templateParticleCount = GetParticleCount(template);
        var templateBones = GetList(GetValue(templateSkeleton, "bones")) ?? Array.Empty<object>();
        var sourceColliders = sourceDocument.Colliders
            .Where(collider => sourceSimulation.CollidableItemIndices.Contains(collider.ItemIndex))
            .ToList();
        var templateColliders = EnumerateReferencedCollidables(template)
            .Distinct(ReferenceEquality.Instance)
            .ToList();

        if (sourceCloth.SimClothCount != 1)
            throw new InvalidOperationException("This first BPHCL -> HKCL pass supports one simulation cloth per imported unit.");
        if (sourceSimulation.Particles.Count != templateParticleCount)
            throw new InvalidOperationException($"Particle count does not match the template ({sourceSimulation.Particles.Count} BPHCL vs {templateParticleCount} HKCL). Choose a closer HKCL template.");
        if (sourceSkeleton.Bones.Count != templateBones.Count)
            throw new InvalidOperationException($"Bone count does not match the template ({sourceSkeleton.Bones.Count} BPHCL vs {templateBones.Count} HKCL). This strict conversion pass only supports exact skeleton templates.");
        if (sourceColliders.Count != templateColliders.Count)
            throw new InvalidOperationException($"Referenced collider count does not match the template ({sourceColliders.Count} BPHCL vs {templateColliders.Count} HKCL). This strict conversion pass needs a one-to-one collider template.");
        var boneMap = BuildBphclBoneMap(sourceSkeleton, templateBones);
        ValidateBphclConstraintTemplate(sourceSimulation, template);
        if (sourceColliders.Any(collider => collider.Shape.TypeName is not ("hclCapsuleShape" or "hclSphereShape" or "hclPlaneShape")))
        {
            var unsupported = sourceColliders
                .Select(collider => collider.Shape.TypeName)
                .Where(type => type is not ("hclCapsuleShape" or "hclSphereShape" or "hclPlaneShape"))
                .Distinct();
            throw new InvalidOperationException($"This conversion pass cannot create these BPHCL collider shape(s) yet: {string.Join(", ", unsupported)}.");
        }

        var convertedCloth = CloneForCurrentGraph(template);
        var convertedSkeleton = CloneForCurrentGraph(templateSkeleton);
        var colliderTemplates = EnumerateReferencedCollidables(convertedCloth)
            .Distinct(ReferenceEquality.Instance)
            .ToList();
        if (sourceColliders.Count > 0 && colliderTemplates.Count == 0)
            throw new InvalidOperationException("The selected HKCL template has no collider object to use as a serialization template.");

        ApplyBphclSkeleton(convertedSkeleton, sourceSkeleton, boneMap);
        ApplyBphclParticles(convertedCloth, sourceSimulation);
        var convertedColliders = CreateBphclColliders(convertedCloth, colliderTemplates, sourceColliders, sourceSkeleton, boneMap);
        var constraintNotes = ApplyBphclConstraintLinks(convertedCloth, sourceSimulation);

        SetValue(convertedCloth, "name", StripBphclPrefix(sourceCloth.Name));
        SetValue(convertedSkeleton, "name", StripBphclPrefix(sourceSkeleton.Name));

        GetMutableClothDatas(root).Add(convertedCloth);
        GetMutableSkeletons(root).Add(convertedSkeleton);
        var targetCollidables = GetMutableCollidables(root);
        foreach (var collidable in convertedColliders)
            targetCollidables.Add(collidable);

        var notes = new List<string>
        {
            "Strict BPHCL -> HKCL conversion created from the selected matching HKCL template.",
            "Skeleton, particles, one-to-one colliders, and verified matching link values were copied.",
            "Template deformers, buffers, triangle topology, local ranges, transform sets, and operators were retained."
        };
        notes.AddRange(constraintNotes);
        return string.Join(" ", notes);
    }

    private static void ValidateBphclConstraintTemplate(NativeBphclSimCloth source, object template)
    {
        var targetSimulation = GetFirst(GetValue(template, "simClothDatas"));
        var targetSets = GetList(GetValue(targetSimulation, "staticConstraintSets"))
            ?? GetList(GetValue(template, "constraintSets"))
            ?? Array.Empty<object>();

        foreach (var sourceSet in source.ConstraintSets.Where(set => set.Links.Count > 0))
        {
            var matches = targetSets.Any(set =>
                string.Equals(set.GetType().Name, sourceSet.ClassName, StringComparison.Ordinal) &&
                (GetList(GetValue(set, "links"))?.Count ?? 0) == sourceSet.Links.Count);
            if (!matches)
            {
                throw new InvalidOperationException(
                    $"Constraint layout does not match the template for {sourceSet.ClassName} ({sourceSet.Links.Count} link(s)). Choose the exact HKCL counterpart before converting.");
            }
        }
    }

    private static IReadOnlyDictionary<int, int> BuildBphclBoneMap(
        NativeBphclSkeleton source,
        IReadOnlyList<object> templateBones)
    {
        var targetByName = templateBones
            .Select((bone, index) => (Name: GetString(bone, "name") ?? string.Empty, Index: index))
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<int, int>();
        var missing = new List<string>();

        foreach (var bone in source.Bones)
        {
            var name = StripBphclPrefix(bone.Name);
            if (!targetByName.TryGetValue(name, out var targetIndex))
            {
                missing.Add(name);
                continue;
            }
            map.Add(bone.Index, targetIndex);
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The HKCL template is missing {missing.Count} BPHCL skeleton bone name(s): {string.Join(", ", missing.Take(8))}. Choose a closer template.");
        }

        return map;
    }

    private static void ApplyBphclSkeleton(
        object skeleton,
        NativeBphclSkeleton source,
        IReadOnlyDictionary<int, int> sourceToTargetBone)
    {
        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetValue(skeleton, "parentIndices") as IList;
        var poses = GetValue(skeleton, "referencePose") as IList;

        foreach (var sourceBone in source.Bones)
        {
            var targetIndex = sourceToTargetBone[sourceBone.Index];
            SetValue(bones[targetIndex], "name", StripBphclPrefix(sourceBone.Name));
            SetValue(bones[targetIndex], "lockTranslation", sourceBone.LockTranslation);
            if (parents != null)
            {
                var targetParent = sourceBone.ParentIndex < 0
                    ? -1
                    : sourceToTargetBone[sourceBone.ParentIndex];
                SetListItem(parents, targetIndex, targetParent);
            }

            if (poses != null && targetIndex < poses.Count)
            {
                var existing = poses[targetIndex] is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
                SetListItem(poses, targetIndex, new Matrix4x4(
                    sourceBone.Translation.X, sourceBone.Translation.Y, sourceBone.Translation.Z, existing.M14,
                    sourceBone.Rotation.X, sourceBone.Rotation.Y, sourceBone.Rotation.Z, sourceBone.Rotation.W,
                    existing.M31, existing.M32, existing.M33, existing.M34,
                    existing.M41, existing.M42, existing.M43, existing.M44));
            }
        }
    }

    private static void ApplyBphclParticles(object cloth, NativeBphclSimCloth source)
    {
        var templateRows = GetParticleRowsForCloth(cloth);
        var rows = source.Particles.Select((particle, index) =>
        {
            var template = templateRows[index];
            return new ParticleEditRow
            {
                Index = index,
                Fixed = particle.Fixed,
                X = particle.Position.X,
                Y = particle.Position.Y,
                Z = particle.Position.Z,
                W = particle.Position.W,
                Mass = particle.Mass,
                InverseMass = particle.InverseMass,
                Radius = particle.Radius,
                Friction = particle.Friction,
                // Collision masks are not exposed by the native BPHCL reader yet.
                CollisionMask = template.CollisionMask
            };
        });

        ApplyParticleRows(cloth, rows);
    }

    private static IReadOnlyList<object> CreateBphclColliders(
        object cloth,
        IReadOnlyList<object> colliderTemplates,
        IReadOnlyList<NativeBphclCollider> sourceColliders,
        NativeBphclSkeleton skeleton,
        IReadOnlyDictionary<int, int> sourceToTargetBone)
    {
        var references = GetPrimaryMutableCollidableReferences(cloth);
        references.Clear();
        var result = new List<object>(sourceColliders.Count);

        for (var index = 0; index < sourceColliders.Count; index++)
        {
            var source = sourceColliders[index];
            var target = CloneForCurrentGraph(colliderTemplates[index % colliderTemplates.Count]);
            var targetShape = CreateHkclColliderShape(target, source.Shape);

            SetValue(target, "name", StripBphclPrefix(source.Name));
            SetValue(target, "transform", new Matrix4x4(
                source.AxisX.X, source.AxisX.Y, source.AxisX.Z, 0.0f,
                source.AxisY.X, source.AxisY.Y, source.AxisY.Z, 0.0f,
                source.AxisZ.X, source.AxisZ.Y, source.AxisZ.Z, 0.0f,
                source.Translation.X, source.Translation.Y, source.Translation.Z, 1.0f));
            SetValue(target, "shape", targetShape);
            ApplyBphclColliderShape(targetShape, source.Shape);

            var sourceBoneIndex = ResolveColliderBoneIndex(StripBphclPrefix(source.Name), skeleton.Bones);
            if (sourceBoneIndex >= 0 && sourceToTargetBone.TryGetValue(sourceBoneIndex, out var targetBoneIndex))
                SetValue(target, "transformIndex", targetBoneIndex);

            AddListItem(references, target);
            result.Add(target);
        }

        return result;
    }

    private static IList GetPrimaryMutableCollidableReferences(object cloth)
    {
        IList? primary = null;
        if (GetValue(cloth, "perInstanceCollidables") is IList direct)
        {
            direct.Clear();
            primary = direct;
        }

        foreach (var simulation in GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>())
        {
            if (GetValue(simulation, "perInstanceCollidables") is not IList references)
                continue;
            references.Clear();
            primary ??= references;
        }

        return primary ?? throw new InvalidOperationException("The HKCL template has no mutable per-instance collider reference list.");
    }

    private static object CreateHkclColliderShape(object targetCollider, NativeBphclColliderShape source)
    {
        var templateShape = GetValue(targetCollider, "shape")
            ?? throw new InvalidOperationException("The HKCL collider template has no shape.");
        var targetType = templateShape.GetType().Assembly
            .GetTypes()
            .FirstOrDefault(type => string.Equals(type.Name, source.TypeName, StringComparison.Ordinal));
        if (targetType == null)
            throw new InvalidOperationException($"HKCLTool does not contain the {source.TypeName} class required by this BPHCL collider.");

        return Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Could not create HKCL collider shape {source.TypeName}.");
    }

    private static void ApplyBphclColliderShape(object target, NativeBphclColliderShape source)
    {
        switch (source.TypeName)
        {
            case "hclCapsuleShape":
                SetValue(target, "start", source.Start);
                SetValue(target, "end", source.End);
                SetValue(target, "radius", source.Radius);
                UpdateCapsuleDerivedValues(target, source.Start, source.End);
                break;
            case "hclSphereShape":
            {
                var sphere = GetValue(target, "sphere")
                    ?? throw new InvalidOperationException("The HKCL sphere shape has no hkSphere value.");
                SetValue(sphere, "pos", source.Start);
                break;
            }
            case "hclPlaneShape":
                SetValue(target, "planeEquation", source.PlaneEquation);
                break;
            default:
                throw new InvalidOperationException($"Unsupported BPHCL collider shape {source.TypeName}.");
        }
    }

    private static IReadOnlyList<string> ApplyBphclConstraintLinks(object cloth, NativeBphclSimCloth source)
    {
        var targetSimulation = GetFirst(GetValue(cloth, "simClothDatas"));
        var targetSets = GetList(GetValue(targetSimulation, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        var notes = new List<string>();

        foreach (var sourceSet in source.ConstraintSets.Where(set => set.Links.Count > 0))
        {
            var targetSet = targetSets.FirstOrDefault(set =>
                string.Equals(set.GetType().Name, sourceSet.ClassName, StringComparison.Ordinal) &&
                (GetList(GetValue(set, "links"))?.Count ?? 0) == sourceSet.Links.Count);
            if (targetSet == null)
            {
                notes.Add($"{sourceSet.ClassName} links were left template-owned because their layout did not match.");
                continue;
            }

            var targetLinks = GetList(GetValue(targetSet, "links")) ?? Array.Empty<object>();
            for (var index = 0; index < sourceSet.Links.Count; index++)
            {
                var sourceLink = sourceSet.Links[index];
                var targetLink = targetLinks[index];
                if (sourceLink.ParticleA is int particleA)
                    SetValue(targetLink, "particleA", particleA);
                if (sourceLink.ParticleB is int particleB)
                    SetValue(targetLink, "particleB", particleB);

                foreach (var value in sourceLink.Values)
                    SetValue(targetLink, value.Key, value.Value);
            }
        }

        return notes;
    }

    private static int ResolveColliderBoneIndex(string colliderName, IReadOnlyList<NativeBphclBone> bones)
    {
        const string prefix = "Collidable_";
        if (!colliderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return -1;

        var suffix = colliderName[prefix.Length..];
        var exact = bones.FirstOrDefault(bone => string.Equals(StripBphclPrefix(bone.Name), suffix, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact.Index;

        // Names such as Collidable_Spine_2_1 still belong to Spine_2.
        for (var end = suffix.Length; end > 0; end = suffix.LastIndexOf('_', end - 1))
        {
            if (end <= 0)
                break;
            var candidate = suffix[..end];
            var match = bones.FirstOrDefault(bone => string.Equals(StripBphclPrefix(bone.Name), candidate, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match.Index;
        }

        return -1;
    }

    private static string StripBphclPrefix(string name) =>
        name.StartsWith("Link:", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;

    private hkRootLevelContainer RequireRoot()
    {
        return _root ?? throw new InvalidOperationException("No physics file is loaded.");
    }

    private static hkRootLevelContainer LoadHkcl(string path)
    {
        using var stream = File.OpenRead(path);
        var reader = new BinaryReaderEx(stream);
        var deserializer = new PackFileDeserializer();
        return (hkRootLevelContainer)deserializer.Deserialize(reader);
    }

    private static hkRootLevelContainer LoadJson(string path)
    {
        var text = File.ReadAllText(path);
        var token = JToken.Parse(text);

        if (token is not JObject obj)
            return DeserializeRoot(text);

        if (obj["technical"] is JObject technical)
        {
            hkRootLevelContainer? root = null;

            if (technical["hkxObjectGraphCompressed"] is JValue compressedToken)
                root = DeserializeRoot(DecompressJson(compressedToken.Value<string>() ?? string.Empty));
            else if (technical["hkxObjectGraph"] is JToken graphToken)
                root = DeserializeRoot(graphToken.ToString(Formatting.None));

            if (root != null)
            {
                ApplyAuthoring(root, obj);
                return root;
            }
        }

        if (obj.TryGetValue("raw", out var rawToken))
        {
            var root = DeserializeRoot(rawToken.ToString(Formatting.None));
            if (obj["authoring"] is JObject authoring)
                ApplyAuthoring(root, authoring);
            return root;
        }

        if (obj.TryGetValue("authoring", out var nestedAuthoring) && nestedAuthoring is JObject nestedAuthoringObj)
        {
            var sourceRoot = LoadLegacyAuthoringBase(path, obj);
            ApplyAuthoring(sourceRoot, nestedAuthoringObj);
            return sourceRoot;
        }

        if ((string?)obj["format"] == "HKCLTool.Authoring" || obj["cloths"] is JArray)
        {
            var sourceRoot = LoadLegacyAuthoringBase(path, obj);
            ApplyAuthoring(sourceRoot, obj);
            return sourceRoot;
        }

        return DeserializeRoot(text);
    }

    private static hkRootLevelContainer LoadLegacyAuthoringBase(string jsonPath, JObject document)
    {
        var sourceFile = (string?)document["sourceFile"];
        if (string.IsNullOrWhiteSpace(sourceFile))
            throw new InvalidOperationException("This JSON does not contain a technical.hkxObjectGraph section. Older authoring JSON files also need sourceFile so HKCLTool can load the original HKCL base.");

        var basePath = Path.IsPathRooted(sourceFile)
            ? sourceFile
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(jsonPath) ?? Environment.CurrentDirectory, sourceFile));

        if (!File.Exists(basePath))
            throw new FileNotFoundException("The source HKCL referenced by this legacy authoring JSON could not be found.", basePath);

        return LoadHkcl(basePath);
    }

    private JObject BuildReadableJson()
    {
        var root = RequireRoot();
        var authoring = BuildAuthoring(root);

        return new JObject
        {
            ["format"] = "HKCLTool.SelfContainedAuthoring",
            ["version"] = 4,
            ["notes"] = "Self-contained editable JSON. Edit the readable cloths section. The compact technical payload preserves the remaining HKX2 data needed to rebuild without the original HKCL file.",
            ["cloths"] = authoring["cloths"],
            ["technical"] = new JObject
            {
                ["encoding"] = "gzip+base64-json",
                ["warning"] = "Do not delete this compact payload yet. It contains required HKX2 fields that have not all been promoted into the readable authoring schema.",
                ["hkxObjectGraphCompressed"] = CompressJson(SerializeRaw(root))
            }
        };
    }

    private static JObject BuildAuthoring(hkRootLevelContainer root)
    {
        var cloths = GetClothDatas(root).ToList();
        var skeletons = GetSkeletons(root).ToList();
        var collidables = GetCollidables(root).ToList();
        var clothArray = new JArray();

        for (var i = 0; i < cloths.Count; i++)
        {
            var cloth = cloths[i];
            var skeleton = i < skeletons.Count ? skeletons[i] : null;
            clothArray.Add(new JObject
            {
                ["index"] = i,
                ["name"] = GetString(cloth, "name") ?? $"Cloth {i}",
                ["class"] = cloth.GetType().Name,
                ["skeleton"] = skeleton == null ? null : BuildSkeleton(skeleton),
                ["simulation"] = BuildSimulation(cloth),
                ["particles"] = BuildParticles(cloth),
                ["triangles"] = BuildTriangles(cloth),
                ["constraints"] = BuildConstraintSummary(cloth),
                ["colliders"] = BuildColliders(collidables, skeleton)
            });
        }

        return new JObject
        {
            ["notes"] = "These values are applied onto the source HKCL when this authoring JSON is reopened.",
            ["cloths"] = clothArray
        };
    }

    private static JObject BuildSkeleton(object skeleton)
    {
        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var poses = GetList(GetValue(skeleton, "referencePose")) ?? Array.Empty<object>();
        var boneArray = new JArray();

        for (var i = 0; i < bones.Count; i++)
        {
            var parentIndex = ToInt(parents.ElementAtOrDefault(i), -1);
            boneArray.Add(new JObject
            {
                ["index"] = i,
                ["name"] = GetString(bones[i], "name") ?? $"Bone {i}",
                ["parentIndex"] = parentIndex,
                ["parent"] = parentIndex >= 0 && parentIndex < bones.Count ? GetString(bones[parentIndex], "name") : null,
                ["lockTranslation"] = ToBool(GetValue(bones[i], "lockTranslation")),
                ["referencePose"] = BuildReferencePose(poses.ElementAtOrDefault(i))
            });
        }

        return new JObject
        {
            ["name"] = GetString(skeleton, "name"),
            ["boneCount"] = bones.Count,
            ["bones"] = boneArray
        };
    }

    private static JToken? BuildReferencePose(object? pose)
    {
        if (pose is Matrix4x4 matrix)
        {
            return new JObject
            {
                ["translation"] = Vector(matrix.M11, matrix.M12, matrix.M13, matrix.M14),
                ["rotationQuaternion"] = Vector(matrix.M21, matrix.M22, matrix.M23, matrix.M24),
                ["scale"] = Vector(matrix.M31, matrix.M32, matrix.M33, matrix.M34),
                ["rawRows"] = MatrixRows(matrix)
            };
        }

        return ToToken(pose);
    }

    private static JObject BuildSimulation(object cloth)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simData == null)
            return new JObject();

        return new JObject
        {
            ["gravity"] = ToToken(GetValue(simData, "gravity")),
            ["globalDampingPerSecond"] = ToToken(GetValue(simData, "globalDampingPerSecond")),
            ["collisionTolerance"] = ToToken(GetValue(simData, "collisionTolerance")),
            ["totalMass"] = ToToken(GetValue(simData, "totalMass")),
            ["maxParticleRadius"] = ToToken(GetValue(simData, "maxParticleRadius")),
            ["numberOfParticles"] = GetList(GetValue(simData, "particleDatas"))?.Count ?? 0
        };
    }

    private static JArray BuildParticles(object cloth)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var particles = GetList(GetValue(simData, "particleDatas")) ?? Array.Empty<object>();
        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetList(GetValue(pose, "positions")) ?? Array.Empty<object>();
        var fixedParticles = new HashSet<int>((GetList(GetValue(simData, "fixedParticles")) ?? Array.Empty<object>()).Select(x => ToInt(x, -1)));
        var collisionMasks = GetList(GetValue(simData, "staticCollisionMasks")) ?? Array.Empty<object>();
        var result = new JArray();

        for (var i = 0; i < particles.Count; i++)
        {
            var particle = particles[i];
            result.Add(new JObject
            {
                ["index"] = i,
                ["position"] = ToToken(positions.ElementAtOrDefault(i)),
                ["fixed"] = fixedParticles.Contains(i),
                ["mass"] = ToToken(GetValue(particle, "mass")),
                ["inverseMass"] = ToToken(GetValue(particle, "invMass")),
                ["radius"] = ToToken(GetValue(particle, "radius")),
                ["friction"] = ToToken(GetValue(particle, "friction")),
                ["collisionMask"] = ToToken(collisionMasks.ElementAtOrDefault(i))
            });
        }

        return result;
    }

    private static JArray BuildTriangles(object cloth)
    {
        var result = new JArray();
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var indices = GetList(GetValue(simData, "triangleIndices"));

        if (indices == null || indices.Count < 3)
        {
            foreach (var buffer in EnumerateObjects(cloth).Where(x => x.GetType().Name.Contains("BufferDefinition", StringComparison.Ordinal)))
            {
                indices = GetList(GetValue(buffer, "triangleIndices"));
                if (indices is { Count: >= 3 })
                    break;
            }
        }

        if (indices == null)
            return result;

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            result.Add(new JObject
            {
                ["index"] = i / 3,
                ["particles"] = new JArray(
                    ToInt(indices[i], 0),
                    ToInt(indices[i + 1], 0),
                    ToInt(indices[i + 2], 0))
            });
        }

        return result;
    }

    private static JArray BuildConstraintSummary(object cloth)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        var result = new JArray();

        for (var i = 0; i < constraints.Count; i++)
        {
            var constraint = constraints[i];
            var links = GetList(GetValue(constraint, "links"));
            var ranges = GetList(GetValue(constraint, "localConstraints"));
            result.Add(new JObject
            {
                ["index"] = i,
                ["class"] = constraint.GetType().Name,
                ["name"] = GetString(constraint, "name"),
                ["linkCount"] = links?.Count,
                ["localConstraintCount"] = ranges?.Count,
                ["links"] = BuildConstraintLinks(links),
                ["localConstraints"] = BuildLocalConstraints(ranges)
            });
        }

        return result;
    }

    private static JArray BuildConstraintLinks(IReadOnlyList<object>? links)
    {
        var result = new JArray();
        if (links == null)
            return result;

        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            result.Add(new JObject
            {
                ["index"] = i,
                ["particleA"] = ToToken(GetValue(link, "particleA")),
                ["particleB"] = ToToken(GetValue(link, "particleB")),
                ["restLength"] = ToToken(GetValue(link, "restLength")),
                ["stiffness"] = ToToken(GetValue(link, "stiffness")),
                ["bendMinLength"] = ToToken(GetValue(link, "bendMinLength")),
                ["stretchMaxLength"] = ToToken(GetValue(link, "stretchMaxLength")),
                ["bendStiffness"] = ToToken(GetValue(link, "bendStiffness"))
            });
        }

        return result;
    }

    private static JArray BuildLocalConstraints(IReadOnlyList<object>? constraints)
    {
        var result = new JArray();
        if (constraints == null)
            return result;

        for (var i = 0; i < constraints.Count; i++)
        {
            var constraint = constraints[i];
            result.Add(new JObject
            {
                ["index"] = i,
                ["particleIndex"] = ToToken(GetValue(constraint, "particleIndex")),
                ["maximumDistance"] = ToToken(GetValue(constraint, "maximumDistance")),
                ["maxDistance"] = ToToken(GetValue(constraint, "maxDistance"))
            });
        }

        return result;
    }

    private static JArray BuildColliders(IReadOnlyList<object> collidables, object? skeleton)
    {
        var bones = skeleton == null
            ? Array.Empty<object>()
            : GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();

        var result = new JArray();

        for (var i = 0; i < collidables.Count; i++)
        {
            var collidable = collidables[i];
            var boneIndex = ToInt(GetValue(collidable, "transformIndex"), -1);
            var shape = GetValue(collidable, "shape");

            result.Add(new JObject
            {
                ["index"] = i,
                ["name"] = GetString(collidable, "name"),
                ["boneIndex"] = boneIndex,
                ["bone"] = boneIndex >= 0 && boneIndex < bones.Count ? GetString(bones[boneIndex], "name") : null,
                ["transform"] = ToToken(GetValue(collidable, "transform")),
                ["boneOffset"] = ToToken(GetValue(collidable, "boneOffset")),
                ["shape"] = shape == null ? null : new JObject
                {
                    ["class"] = shape.GetType().Name,
                    ["start"] = ToToken(GetValue(shape, "start")),
                    ["end"] = ToToken(GetValue(shape, "end")),
                    ["dir"] = ToToken(GetValue(shape, "dir")),
                    ["radius"] = ToToken(GetValue(shape, "radius")),
                    ["capLenSqrdInv"] = ToToken(GetValue(shape, "capLenSqrdInv"))
                }
            });
        }

        return result;
    }

    private static void ApplyAuthoring(hkRootLevelContainer root, JObject authoring)
    {
        if (authoring["cloths"] is not JArray clothArray)
            return;

        var cloths = GetClothDatas(root).ToList();
        var skeletons = GetSkeletons(root).ToList();
        var collidables = GetCollidables(root).ToList();

        foreach (var clothToken in clothArray.OfType<JObject>())
        {
            var clothIndex = (int?)clothToken["index"] ?? clothArray.IndexOf(clothToken);
            if (clothIndex < 0 || clothIndex >= cloths.Count)
                continue;

            var cloth = cloths[clothIndex];
            SetValue(cloth, "name", (string?)clothToken["name"]);

            if (clothToken["skeleton"] is JObject skeletonToken && clothIndex < skeletons.Count)
                ApplySkeleton(skeletons[clothIndex], skeletonToken);

            if (clothToken["particles"] is JArray particleArray)
                ApplyParticles(cloth, particleArray);

            if (clothToken["simulation"] is JObject simulationToken)
                ApplySimulation(cloth, simulationToken);

            if (clothToken["constraints"] is JArray constraintArray)
                ApplyConstraints(cloth, constraintArray);

            if (clothToken["colliders"] is JArray colliderArray)
                ApplyColliders(collidables, colliderArray);
        }
    }

    private static void ApplySkeleton(object skeleton, JObject skeletonToken)
    {
        SetValue(skeleton, "name", (string?)skeletonToken["name"]);

        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parentList = GetValue(skeleton, "parentIndices") is IList parents ? parents : null;
        var poses = GetValue(skeleton, "referencePose") is IList poseList ? poseList : null;

        if (skeletonToken["bones"] is not JArray boneArray)
            return;

        foreach (var boneToken in boneArray.OfType<JObject>())
        {
            var index = (int?)boneToken["index"] ?? -1;
            if (index < 0 || index >= bones.Count)
                continue;

            var bone = bones[index];
            SetValue(bone, "name", (string?)boneToken["name"]);
            SetValue(bone, "lockTranslation", (bool?)boneToken["lockTranslation"]);

            if (parentList != null && boneToken["parentIndex"] != null)
                SetListItem(parentList, index, boneToken["parentIndex"]!.Value<int>());

            if (poses != null && boneToken["referencePose"] is JObject poseToken)
                SetListItem(poses, index, ReadReferencePose(poseToken, poses[index]));
        }
    }

    private static Matrix4x4 ReadReferencePose(JObject poseToken, object? existing)
    {
        if (poseToken["rawRows"] is JArray rows)
            return ReadMatrixRows(rows);

        var fallback = existing is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
        var translation = ReadVector(poseToken["translation"] as JObject, new Vector4(fallback.M11, fallback.M12, fallback.M13, fallback.M14));
        var rotation = ReadVector(poseToken["rotationQuaternion"] as JObject, new Vector4(fallback.M21, fallback.M22, fallback.M23, fallback.M24));
        var scale = ReadVector(poseToken["scale"] as JObject, new Vector4(fallback.M31, fallback.M32, fallback.M33, fallback.M34));

        return new Matrix4x4(
            translation.X, translation.Y, translation.Z, translation.W,
            rotation.X, rotation.Y, rotation.Z, rotation.W,
            scale.X, scale.Y, scale.Z, scale.W,
            fallback.M41, fallback.M42, fallback.M43, fallback.M44);
    }

    private static void ApplySimulation(object cloth, JObject simulationToken)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simData == null)
            return;

        SetValue(simData, "gravity", ReadVectorToken(simulationToken["gravity"], GetValue(simData, "gravity")));
        SetValue(simData, "globalDampingPerSecond", ReadFloatToken(simulationToken["globalDampingPerSecond"], GetValue(simData, "globalDampingPerSecond")));
        SetValue(simData, "collisionTolerance", ReadFloatToken(simulationToken["collisionTolerance"], GetValue(simData, "collisionTolerance")));
        SetValue(simData, "totalMass", ReadFloatToken(simulationToken["totalMass"], GetValue(simData, "totalMass")));
        SetValue(simData, "maxParticleRadius", ReadFloatToken(simulationToken["maxParticleRadius"], GetValue(simData, "maxParticleRadius")));
    }

    private static void AddTriangleRelationships(List<ParticleRelationshipRow> rows, object? simData, int particleIndex)
    {
        var indices = GetList(GetValue(simData, "triangleIndices"));
        if (indices == null)
            return;

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = ToInt(indices[i], -1);
            var b = ToInt(indices[i + 1], -1);
            var c = ToInt(indices[i + 2], -1);
            if (a != particleIndex && b != particleIndex && c != particleIndex)
                continue;

            rows.Add(new ParticleRelationshipRow
            {
                Kind = "Triangle",
                Name = $"Triangle {i / 3}",
                Particles = $"{a}, {b}, {c}",
                Details = "Virtual cloth surface used by mesh/bone deformation."
            });
        }
    }

    private static void AddConstraintRelationships(List<ParticleRelationshipRow> rows, object? simData, object cloth, int particleIndex)
    {
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();

        for (var setIndex = 0; setIndex < constraints.Count; setIndex++)
        {
            var constraint = constraints[setIndex];
            var setName = GetString(constraint, "name") ?? constraint.GetType().Name;
            var links = GetList(GetValue(constraint, "links"));
            if (links != null)
            {
                for (var linkIndex = 0; linkIndex < links.Count; linkIndex++)
                {
                    var link = links[linkIndex];
                    var a = ToInt(GetValue(link, "particleA"), -1);
                    var b = ToInt(GetValue(link, "particleB"), -1);
                    if (a != particleIndex && b != particleIndex)
                        continue;

                    rows.Add(new ParticleRelationshipRow
                    {
                        Kind = "Link",
                        Name = $"{setName} #{linkIndex}",
                        Particles = $"{a} - {b}",
                        Details = BuildLinkDetails(link)
                    });
                }
            }

            var locals = GetList(GetValue(constraint, "localConstraints"));
            if (locals == null)
                continue;

            for (var localIndex = 0; localIndex < locals.Count; localIndex++)
            {
                var local = locals[localIndex];
                var p = ToInt(GetValue(local, "particleIndex"), -1);
                if (p != particleIndex)
                    continue;

                rows.Add(new ParticleRelationshipRow
                {
                    Kind = "Local",
                    Name = $"{setName} #{localIndex}",
                    Particles = p.ToString(CultureInfo.InvariantCulture),
                    Details = BuildLocalConstraintDetails(local)
                });
            }
        }
    }

    private static void AddPreviewTriangles(ParticlePreviewData preview, object? simData)
    {
        var indices = GetList(GetValue(simData, "triangleIndices"));
        if (indices == null)
            return;

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            preview.Triangles.Add(new ParticlePreviewTriangle
            {
                ParticleA = ToInt(indices[i], -1),
                ParticleB = ToInt(indices[i + 1], -1),
                ParticleC = ToInt(indices[i + 2], -1)
            });
        }
    }

    private static void AddPreviewLinks(ParticlePreviewData preview, object? simData, object cloth)
    {
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constraint in constraints)
        {
            var kind = GetString(constraint, "name") ?? constraint.GetType().Name;
            var links = GetList(GetValue(constraint, "links"));
            if (links == null)
                continue;

            foreach (var link in links)
            {
                var a = ToInt(GetValue(link, "particleA"), -1);
                var b = ToInt(GetValue(link, "particleB"), -1);
                if (a < 0 || b < 0)
                    continue;

                var key = a < b ? $"{a}:{b}:{kind}" : $"{b}:{a}:{kind}";
                if (!seen.Add(key))
                    continue;

                preview.Links.Add(new ParticlePreviewLink
                {
                    ParticleA = a,
                    ParticleB = b,
                    Kind = kind
                });
            }
        }
    }

    private static void AddPreviewSkeleton(ParticlePreviewData preview, hkRootLevelContainer root, int clothIndex)
    {
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex);
        if (skeleton == null)
            return;

        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var poses = GetList(GetValue(skeleton, "referencePose")) ?? Array.Empty<object>();
        var worldTransforms = new Matrix4x4[bones.Count];

        for (var i = 0; i < bones.Count; i++)
        {
            var local = PoseToLocalMatrix(poses.ElementAtOrDefault(i));
            var parentIndex = ToInt(parents.ElementAtOrDefault(i), -1);
            var world = parentIndex >= 0 && parentIndex < i
                ? local * worldTransforms[parentIndex]
                : local;

            worldTransforms[i] = world;
            preview.Bones.Add(new BonePreviewPoint
            {
                Index = i,
                ParentIndex = parentIndex,
                Name = GetString(bones[i], "name") ?? $"Bone {i}",
                Position = new Vector3(world.M41, world.M42, world.M43),
                AxisX = NormalizeOr(new Vector3(world.M11, world.M12, world.M13), Vector3.UnitX),
                AxisY = NormalizeOr(new Vector3(world.M21, world.M22, world.M23), Vector3.UnitY),
                AxisZ = NormalizeOr(new Vector3(world.M31, world.M32, world.M33), Vector3.UnitZ)
            });
        }

        var rootBone = preview.Bones.FirstOrDefault(x => x.ParentIndex < 0)
            ?? preview.Bones.FirstOrDefault(x => x.Index == 0);
        if (rootBone != null)
        {
            preview.ViewRoot = rootBone.Position;
            preview.HasViewRoot = true;
        }
    }

    private static void AddNativeBphclPreviewSkeleton(ParticlePreviewData preview, NativeBphclSkeleton? skeleton)
    {
        if (skeleton is null)
            return;

        var worldTransforms = new Matrix4x4[skeleton.Bones.Count];
        for (var index = 0; index < skeleton.Bones.Count; index++)
        {
            var bone = skeleton.Bones[index];
            var rotation = new Quaternion(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W);
            if (rotation.LengthSquared() < 0.000001f)
                rotation = Quaternion.Identity;
            else
                rotation = Quaternion.Normalize(rotation);

            var local = Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(bone.Translation.X, bone.Translation.Y, bone.Translation.Z);
            var world = bone.ParentIndex >= 0 && bone.ParentIndex < index
                ? local * worldTransforms[bone.ParentIndex]
                : local;
            worldTransforms[index] = world;

            preview.Bones.Add(new BonePreviewPoint
            {
                Index = bone.Index,
                ParentIndex = bone.ParentIndex,
                Name = bone.Name,
                Position = new Vector3(world.M41, world.M42, world.M43),
                AxisX = NormalizeOr(new Vector3(world.M11, world.M12, world.M13), Vector3.UnitX),
                AxisY = NormalizeOr(new Vector3(world.M21, world.M22, world.M23), Vector3.UnitY),
                AxisZ = NormalizeOr(new Vector3(world.M31, world.M32, world.M33), Vector3.UnitZ)
            });
        }

        var rootBone = preview.Bones.FirstOrDefault(bone => bone.ParentIndex < 0)
            ?? preview.Bones.FirstOrDefault(bone => bone.Index == 0);
        if (rootBone is not null)
        {
            preview.ViewRoot = rootBone.Position;
            preview.HasViewRoot = true;
        }
    }

    private static void AddNativeBphclPreviewColliders(ParticlePreviewData preview, NativeBphclDocument document, int clothIndex)
    {
        var bones = document.Skeletons.ElementAtOrDefault(clothIndex)?.Bones ?? Array.Empty<NativeBphclBone>();
        var referencedColliderItems = document.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths
            .SelectMany(simulation => simulation.CollidableItemIndices)
            .ToHashSet() ?? new HashSet<int>();
        foreach (var collider in document.Colliders.Where(collider => collider.Enabled && referencedColliderItems.Contains(collider.ItemIndex)))
        {
            var boneIndex = ResolveColliderBoneIndex(collider.Name, bones.Select(bone => (bone.Index, bone.Name)), -1);
            var shape = collider.Shape;
            var kind = shape.TypeName switch
            {
                "hclSphereShape" => ColliderPreviewKind.Sphere,
                "hclTaperedCapsuleShape" => ColliderPreviewKind.TaperedCapsule,
                "hclPlaneShape" => ColliderPreviewKind.Plane,
                "hclCapsuleShape" => ColliderPreviewKind.Capsule,
                _ => ColliderPreviewKind.Point
            };

            var start = TransformNativeColliderPoint(collider, shape.Start);
            var end = TransformNativeColliderPoint(collider, shape.End);
            var planeNormal = TransformNativeColliderDirection(collider, new Vector3(shape.PlaneEquation.X, shape.PlaneEquation.Y, shape.PlaneEquation.Z));
            if (kind == ColliderPreviewKind.Plane)
            {
                var localNormal = new Vector3(shape.PlaneEquation.X, shape.PlaneEquation.Y, shape.PlaneEquation.Z);
                start = TransformNativeColliderPoint(collider, new Vector4(localNormal * -shape.PlaneEquation.W, 0.0f));
                end = start;
            }

            preview.Colliders.Add(new ColliderPreviewShape
            {
                Index = collider.Index,
                Name = collider.Name,
                BoneIndex = boneIndex,
                Start = start,
                End = end,
                Radius = shape.Radius,
                EndRadius = shape.EndRadius,
                PlaneNormal = NormalizeOr(planeNormal, Vector3.UnitY),
                Kind = kind
            });
        }
    }

    private static void AddPreviewColliders(ParticlePreviewData preview, hkRootLevelContainer root, object cloth)
    {
        var referencedCollidables = new HashSet<object>(EnumerateReferencedCollidables(cloth), ReferenceEquality.Instance);
        var collidables = GetCollidables(root);

        for (var i = 0; i < collidables.Count; i++)
        {
            var collidable = collidables[i];
            if (!referencedCollidables.Contains(collidable))
                continue;
            var shape = GetValue(collidable, "shape");
            if (shape == null)
                continue;

            var name = GetString(collidable, "name") ?? $"Collider {i}";
            var boneIndex = ResolveColliderBoneIndex(name, preview.Bones.AsEnumerable(), ToInt(GetValue(collidable, "transformIndex"), -1));
            var transform = GetValue(collidable, "transform") is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
            var shapeType = shape.GetType().Name;
            var kind = shapeType switch
            {
                "hclSphereShape" => ColliderPreviewKind.Sphere,
                "hclTaperedCapsuleShape" => ColliderPreviewKind.TaperedCapsule,
                "hclPlaneShape" => ColliderPreviewKind.Plane,
                "hclCapsuleShape" => ColliderPreviewKind.Capsule,
                _ => ColliderPreviewKind.Point
            };

            var localStart = Vector4.Zero;
            var localEnd = Vector4.Zero;
            var radius = 0.0f;
            var endRadius = 0.0f;
            var planeEquation = Vector4.Zero;
            switch (kind)
            {
                case ColliderPreviewKind.Capsule:
                    localStart = GetValue(shape, "start") is Vector4 start ? start : Vector4.Zero;
                    localEnd = GetValue(shape, "end") is Vector4 end ? end : Vector4.Zero;
                    radius = Convert.ToSingle(GetValue(shape, "radius") ?? 0.0f, CultureInfo.InvariantCulture);
                    endRadius = radius;
                    break;
                case ColliderPreviewKind.Sphere:
                    localStart = GetValue(GetValue(shape, "sphere"), "pos") is Vector4 sphere ? sphere : Vector4.Zero;
                    radius = localStart.W;
                    endRadius = radius;
                    break;
                case ColliderPreviewKind.TaperedCapsule:
                    localStart = GetValue(shape, "small") is Vector4 small ? small : Vector4.Zero;
                    localEnd = GetValue(shape, "big") is Vector4 big ? big : Vector4.Zero;
                    radius = Convert.ToSingle(GetValue(shape, "smallRadius") ?? 0.0f, CultureInfo.InvariantCulture);
                    endRadius = Convert.ToSingle(GetValue(shape, "bigRadius") ?? radius, CultureInfo.InvariantCulture);
                    break;
                case ColliderPreviewKind.Plane:
                    planeEquation = GetValue(shape, "planeEquation") is Vector4 plane ? plane : Vector4.Zero;
                    localStart = new Vector4(new Vector3(planeEquation.X, planeEquation.Y, planeEquation.Z) * -planeEquation.W, 0.0f);
                    break;
            }

            preview.Colliders.Add(new ColliderPreviewShape
            {
                Index = i,
                Name = name,
                BoneIndex = boneIndex,
                Start = TransformHkclColliderPoint(transform, localStart),
                End = TransformHkclColliderPoint(transform, localEnd),
                Radius = radius,
                EndRadius = endRadius,
                PlaneNormal = TransformHkclColliderDirection(transform, new Vector3(planeEquation.X, planeEquation.Y, planeEquation.Z)),
                Kind = kind
            });
        }
    }

    private static int ResolveColliderBoneIndex(string? colliderName, IReadOnlyList<object> bones, int fallbackIndex)
    {
        return ResolveColliderBoneIndex(
            colliderName,
            Enumerable.Range(0, bones.Count)
                .Select(i => (Index: i, Name: GetString(bones[i], "name") ?? string.Empty)),
            fallbackIndex);
    }

    private static int ResolveColliderBoneIndex(string? colliderName, IEnumerable<BonePreviewPoint> bones, int fallbackIndex)
    {
        return ResolveColliderBoneIndex(colliderName, bones.Select(b => (b.Index, b.Name)), fallbackIndex);
    }

    private static int ResolveColliderBoneIndex(string? colliderName, IEnumerable<(int Index, string Name)> bones, int fallbackIndex)
    {
        if (string.IsNullOrWhiteSpace(colliderName))
            return fallbackIndex;

        var byName = bones
            .Where(b => !string.IsNullOrWhiteSpace(b.Name))
            .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Index, StringComparer.OrdinalIgnoreCase);

        var candidate = colliderName.StartsWith("Collidable_", StringComparison.OrdinalIgnoreCase)
            ? colliderName["Collidable_".Length..]
            : colliderName;

        while (!string.IsNullOrWhiteSpace(candidate))
        {
            if (byName.TryGetValue(candidate, out var resolved))
                return resolved;

            var lastUnderscore = candidate.LastIndexOf('_');
            if (lastUnderscore < 0)
                break;

            var suffix = candidate[(lastUnderscore + 1)..];
            if (suffix.Length == 0 || !suffix.All(char.IsDigit))
                break;

            candidate = candidate[..lastUnderscore];
        }

        return fallbackIndex;
    }

    private static Vector3 TransformByBone(Vector3 origin, Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 local)
    {
        return origin + axisX * local.X + axisY * local.Y + axisZ * local.Z;
    }

    private static Vector3 TransformNativeColliderPoint(NativeBphclCollider collider, Vector4 local) =>
        TransformByBone(
            new Vector3(collider.Translation.X, collider.Translation.Y, collider.Translation.Z),
            NormalizeOr(new Vector3(collider.AxisX.X, collider.AxisX.Y, collider.AxisX.Z), Vector3.UnitX),
            NormalizeOr(new Vector3(collider.AxisY.X, collider.AxisY.Y, collider.AxisY.Z), Vector3.UnitY),
            NormalizeOr(new Vector3(collider.AxisZ.X, collider.AxisZ.Y, collider.AxisZ.Z), Vector3.UnitZ),
            new Vector3(local.X, local.Y, local.Z));

    private static Vector3 TransformNativeColliderDirection(NativeBphclCollider collider, Vector3 direction)
    {
        var transformed = new Vector3(
            collider.AxisX.X * direction.X + collider.AxisY.X * direction.Y + collider.AxisZ.X * direction.Z,
            collider.AxisX.Y * direction.X + collider.AxisY.Y * direction.Y + collider.AxisZ.Y * direction.Z,
            collider.AxisX.Z * direction.X + collider.AxisY.Z * direction.Y + collider.AxisZ.Z * direction.Z);
        return NormalizeOr(transformed, Vector3.UnitY);
    }

    // hkTransform stores three rotation rows followed by a translation row.
    // Shape points are row vectors in that local collider space.
    private static Vector3 TransformHkclColliderPoint(Matrix4x4 transform, Vector4 local)
    {
        return new Vector3(
            transform.M11 * local.X + transform.M21 * local.Y + transform.M31 * local.Z + transform.M41,
            transform.M12 * local.X + transform.M22 * local.Y + transform.M32 * local.Z + transform.M42,
            transform.M13 * local.X + transform.M23 * local.Y + transform.M33 * local.Z + transform.M43);
    }

    private static Vector3 TransformHkclColliderDirection(Matrix4x4 transform, Vector3 direction)
    {
        var transformed = new Vector3(
            transform.M11 * direction.X + transform.M21 * direction.Y + transform.M31 * direction.Z,
            transform.M12 * direction.X + transform.M22 * direction.Y + transform.M32 * direction.Z,
            transform.M13 * direction.X + transform.M23 * direction.Y + transform.M33 * direction.Z);
        return NormalizeOr(transformed, Vector3.UnitY);
    }

    private static Vector3 NormalizeOr(Vector3 vector, Vector3 fallback)
    {
        return vector.LengthSquared() < 0.000001f ? fallback : Vector3.Normalize(vector);
    }

    private static Matrix4x4 PoseToLocalMatrix(object? pose)
    {
        if (pose is not Matrix4x4 matrix)
            return Matrix4x4.Identity;

        var translation = new Vector3(matrix.M11, matrix.M12, matrix.M13);
        var rotation = new Quaternion(matrix.M21, matrix.M22, matrix.M23, matrix.M24);
        if (rotation.LengthSquared() < 0.000001f)
            rotation = Quaternion.Identity;
        else
            rotation = Quaternion.Normalize(rotation);

        var scale = new Vector3(matrix.M31, matrix.M32, matrix.M33);
        if (Math.Abs(scale.X) < 0.000001f)
            scale.X = 1.0f;
        if (Math.Abs(scale.Y) < 0.000001f)
            scale.Y = 1.0f;
        if (Math.Abs(scale.Z) < 0.000001f)
            scale.Z = 1.0f;

        return Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(translation);
    }

    private static string BuildLinkDetails(object link)
    {
        var parts = new List<string>();
        AddDetail(parts, "rest", GetValue(link, "restLength"));
        AddDetail(parts, "stiff", GetValue(link, "stiffness"));
        AddDetail(parts, "bendMin", GetValue(link, "bendMinLength"));
        AddDetail(parts, "stretchMax", GetValue(link, "stretchMaxLength"));
        AddDetail(parts, "bendStiff", GetValue(link, "bendStiffness"));
        return parts.Count == 0 ? "No exposed scalar fields." : string.Join("; ", parts);
    }

    private static string BuildLocalConstraintDetails(object local)
    {
        var parts = new List<string>();
        AddDetail(parts, "maxDist", GetValue(local, "maximumDistance"));
        AddDetail(parts, "maxDist", GetValue(local, "maxDistance"));
        return parts.Count == 0 ? "Local particle movement limit." : string.Join("; ", parts);
    }

    private static void AddDetail(List<string> parts, string name, object? value)
    {
        if (value == null)
            return;

        parts.Add($"{name}={Convert.ToString(value, CultureInfo.InvariantCulture)}");
    }
    private static void ApplyParticleRows(object cloth, IEnumerable<ParticleEditRow> rows)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simData == null)
            return;

        var particles = GetList(GetValue(simData, "particleDatas")) ?? Array.Empty<object>();
        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetValue(pose, "positions") as IList;
        var collisionMasks = GetValue(simData, "staticCollisionMasks") as IList;
        var fixedParticles = GetValue(simData, "fixedParticles") as IList;
        fixedParticles?.Clear();

        var totalMass = 0.0f;
        var maxRadius = 0.0f;

        foreach (var row in rows)
        {
            if (row.Index < 0 || row.Index >= particles.Count)
                continue;

            var particle = particles[row.Index];
            SetValue(particle, "mass", row.Mass);
            SetValue(particle, "invMass", row.InverseMass);
            SetValue(particle, "radius", row.Radius);
            SetValue(particle, "friction", row.Friction);

            if (positions != null)
                SetListItem(positions, row.Index, new Vector4(row.X, row.Y, row.Z, row.W));

            if (collisionMasks != null)
                SetListItem(collisionMasks, row.Index, row.CollisionMask);

            if (row.Fixed)
                AddListItem(fixedParticles, row.Index);

            totalMass += row.Mass;
            maxRadius = Math.Max(maxRadius, row.Radius);
        }

        SetValue(simData, "totalMass", totalMass);
        SetValue(simData, "maxParticleRadius", maxRadius);
    }
    private static void ApplyParticles(object cloth, JArray particleArray)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simData == null)
            return;

        var particles = GetList(GetValue(simData, "particleDatas")) ?? Array.Empty<object>();
        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetValue(pose, "positions") as IList;
        var collisionMasks = GetValue(simData, "staticCollisionMasks") as IList;
        var fixedParticles = GetValue(simData, "fixedParticles") as IList;
        fixedParticles?.Clear();

        var totalMass = 0.0f;
        var maxRadius = 0.0f;

        foreach (var particleToken in particleArray.OfType<JObject>())
        {
            var index = (int?)particleToken["index"] ?? -1;
            if (index < 0 || index >= particles.Count)
                continue;

            var particle = particles[index];
            var mass = ReadFloatToken(particleToken["mass"], GetValue(particle, "mass"));
            var inverseMass = particleToken["inverseMass"] != null
                ? ReadFloatToken(particleToken["inverseMass"], GetValue(particle, "invMass"))
                : mass == 0.0f ? 0.0f : 1.0f / mass;
            var radius = ReadFloatToken(particleToken["radius"], GetValue(particle, "radius"));

            SetValue(particle, "mass", mass);
            SetValue(particle, "invMass", inverseMass);
            SetValue(particle, "radius", radius);
            SetValue(particle, "friction", ReadFloatToken(particleToken["friction"], GetValue(particle, "friction")));

            if (positions != null && particleToken["position"] != null)
                SetListItem(positions, index, ReadVectorToken(particleToken["position"], positions[index]));

            if (collisionMasks != null && particleToken["collisionMask"] != null)
                SetListItem(collisionMasks, index, particleToken["collisionMask"]!.Value<int>());

            if ((bool?)particleToken["fixed"] == true)
                AddListItem(fixedParticles, index);

            totalMass += mass;
            maxRadius = Math.Max(maxRadius, radius);
        }

        SetValue(simData, "totalMass", totalMass);
        SetValue(simData, "maxParticleRadius", maxRadius);
    }

    private static void ApplyConstraints(object cloth, JArray constraintArray)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();

        foreach (var constraintToken in constraintArray.OfType<JObject>())
        {
            var constraintIndex = (int?)constraintToken["index"] ?? -1;
            if (constraintIndex < 0 || constraintIndex >= constraints.Count)
                continue;

            var constraint = constraints[constraintIndex];
            SetValue(constraint, "name", (string?)constraintToken["name"]);

            if (constraintToken["links"] is JArray linkArray)
                ApplyConstraintLinks(constraint, linkArray);

            if (constraintToken["localConstraints"] is JArray localArray)
                ApplyLocalConstraints(constraint, localArray);
        }
    }

    private static void ApplyConstraintLinks(object constraint, JArray linkArray)
    {
        var links = GetList(GetValue(constraint, "links")) ?? Array.Empty<object>();
        foreach (var linkToken in linkArray.OfType<JObject>())
        {
            var index = (int?)linkToken["index"] ?? -1;
            if (index < 0 || index >= links.Count)
                continue;

            var link = links[index];
            ApplyOptionalInt(link, "particleA", linkToken["particleA"]);
            ApplyOptionalInt(link, "particleB", linkToken["particleB"]);
            ApplyOptionalFloat(link, "restLength", linkToken["restLength"]);
            ApplyOptionalFloat(link, "stiffness", linkToken["stiffness"]);
            ApplyOptionalFloat(link, "bendMinLength", linkToken["bendMinLength"]);
            ApplyOptionalFloat(link, "stretchMaxLength", linkToken["stretchMaxLength"]);
            ApplyOptionalFloat(link, "bendStiffness", linkToken["bendStiffness"]);
        }
    }

    private static void ApplyLocalConstraints(object constraint, JArray localArray)
    {
        var localConstraints = GetList(GetValue(constraint, "localConstraints")) ?? Array.Empty<object>();
        foreach (var localToken in localArray.OfType<JObject>())
        {
            var index = (int?)localToken["index"] ?? -1;
            if (index < 0 || index >= localConstraints.Count)
                continue;

            var local = localConstraints[index];
            ApplyOptionalInt(local, "particleIndex", localToken["particleIndex"]);
            ApplyOptionalFloat(local, "maximumDistance", localToken["maximumDistance"]);
            ApplyOptionalFloat(local, "maxDistance", localToken["maxDistance"]);
        }
    }

    private static void ApplyOptionalInt(object target, string fieldName, JToken? token)
    {
        if (token != null && token.Type != JTokenType.Null)
            SetValue(target, fieldName, token.Value<int>());
    }

    private static void ApplyOptionalFloat(object target, string fieldName, JToken? token)
    {
        if (token != null && token.Type != JTokenType.Null)
            SetValue(target, fieldName, token.Value<float>());
    }
    private static void ApplyColliders(IReadOnlyList<object> collidables, JArray colliderArray)
    {
        foreach (var colliderToken in colliderArray.OfType<JObject>())
        {
            var index = (int?)colliderToken["index"] ?? -1;
            if (index < 0 || index >= collidables.Count)
                continue;

            var collidable = collidables[index];
            SetValue(collidable, "name", (string?)colliderToken["name"]);

            if (colliderToken["boneIndex"] != null)
                SetValue(collidable, "transformIndex", colliderToken["boneIndex"]!.Value<int>());

            if (colliderToken["transform"] != null)
                SetValue(collidable, "transform", ReadMatrixToken(colliderToken["transform"], GetValue(collidable, "transform")));

            if (colliderToken["boneOffset"] != null)
                SetValue(collidable, "boneOffset", ReadMatrixToken(colliderToken["boneOffset"], GetValue(collidable, "boneOffset")));

            var shape = GetValue(collidable, "shape");
            if (shape == null || colliderToken["shape"] is not JObject shapeToken)
                continue;

            var start = ReadVectorToken(shapeToken["start"], GetValue(shape, "start"));
            var end = ReadVectorToken(shapeToken["end"], GetValue(shape, "end"));
            SetValue(shape, "start", start);
            SetValue(shape, "end", end);
            SetValue(shape, "radius", ReadFloatToken(shapeToken["radius"], GetValue(shape, "radius")));

            if (shapeToken["dir"] != null)
                SetValue(shape, "dir", ReadVectorToken(shapeToken["dir"], GetValue(shape, "dir")));

            if (shapeToken["capLenSqrdInv"] != null)
            {
                SetValue(shape, "capLenSqrdInv", ReadFloatToken(shapeToken["capLenSqrdInv"], GetValue(shape, "capLenSqrdInv")));
            }
            else
            {
                var delta = end - start;
                var lengthSq = delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;
                if (lengthSq > 0.0f)
                {
                    var length = MathF.Sqrt(lengthSq);
                    SetValue(shape, "dir", new Vector4(delta.X / length, delta.Y / length, delta.Z / length, 0.0f));
                    SetValue(shape, "capLenSqrdInv", 1.0f / lengthSq);
                }
            }
        }
    }
    private static int GetParticleCount(object cloth)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        return GetList(GetValue(simData, "particleDatas"))?.Count ?? 0;
    }

    private static IReadOnlyList<object> GetClothDatas(object root)
    {
        var container = FindFirst(root, "hclClothContainer");
        return GetList(GetValue(container, "clothDatas")) ?? Array.Empty<object>();
    }

    private static IList GetMutableClothDatas(object root)
    {
        var container = FindFirst(root, "hclClothContainer") ?? throw new InvalidOperationException("No hclClothContainer found.");
        return GetMutableList(GetValue(container, "clothDatas"), "clothDatas");
    }

    private static IReadOnlyList<object> GetSkeletons(object root)
    {
        var animationContainer = FindFirst(root, "hkaAnimationContainer");
        return GetList(GetValue(animationContainer, "skeletons")) ?? Array.Empty<object>();
    }

    private static IList GetMutableSkeletons(object root)
    {
        var animationContainer = FindFirst(root, "hkaAnimationContainer") ?? throw new InvalidOperationException("No hkaAnimationContainer found.");
        return GetMutableList(GetValue(animationContainer, "skeletons"), "skeletons");
    }

    private static IReadOnlyList<object> GetCollidables(object root)
    {
        var container = FindFirst(root, "hclClothContainer");
        return GetList(GetValue(container, "collidables")) ?? Array.Empty<object>();
    }

    private static IList GetMutableCollidables(object root)
    {
        var container = FindFirst(root, "hclClothContainer") ?? throw new InvalidOperationException("No hclClothContainer found.");
        return GetMutableList(GetValue(container, "collidables"), "collidables");
    }

    private static IEnumerable<object> EnumerateReferencedCollidables(object cloth)
    {
        // Older files can keep the list directly on hclClothData, while BotW
        // stores it on each hclSimClothData. Support both layouts so every
        // editor surface follows the actual simulation dependencies.
        foreach (var collidable in GetList(GetValue(cloth, "perInstanceCollidables")) ?? Array.Empty<object>())
            yield return collidable;

        foreach (var simulation in GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>())
        {
            foreach (var collidable in GetList(GetValue(simulation, "perInstanceCollidables")) ?? Array.Empty<object>())
                yield return collidable;
        }
    }

    private static object CloneForCurrentGraph(object source)
    {
        var json = JsonConvert.SerializeObject(source, RawJsonSettings());
        return JsonConvert.DeserializeObject(json, source.GetType(), RawJsonSettings())
            ?? throw new InvalidOperationException($"Could not clone {source.GetType().Name}.");
    }

    private static object? FindFirst(object root, string typeName)
    {
        return EnumerateObjects(root).FirstOrDefault(x => x.GetType().Name == typeName);
    }

    private static IEnumerable<object> EnumerateObjects(object? root)
    {
        if (root == null)
            yield break;

        var seen = new HashSet<object>(ReferenceEquality.Instance);
        var stack = new Stack<object>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (!seen.Add(item))
                continue;

            yield return item;

            if (item is string)
                continue;

            if (item is IEnumerable enumerable && item is not IDictionary)
            {
                foreach (var child in enumerable)
                {
                    if (child != null && ShouldTraverse(child.GetType()))
                        stack.Push(child);
                }

                continue;
            }

            foreach (var child in GetChildValues(item))
            {
                if (child != null && ShouldTraverse(child.GetType()))
                    stack.Push(child);
            }
        }
    }

    private static IEnumerable<object?> GetChildValues(object item)
    {
        var type = item.GetType();

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            yield return field.GetValue(item);

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            if (property.Name is "Signature" or "IsIdentity")
                continue;

            object? value;
            try
            {
                value = property.GetValue(item);
            }
            catch
            {
                continue;
            }

            yield return value;
        }
    }

    private static bool ShouldTraverse(Type type)
    {
        if (type.IsPrimitive || type.IsEnum)
            return false;

        if (type == typeof(string) || type == typeof(decimal) || type == typeof(Vector4) || type == typeof(Matrix4x4))
            return false;

        return true;
    }

    private static bool SetValue(object? obj, string name, object? value)
    {
        if (obj == null || value == null)
            return false;

        var type = obj.GetType();
        var candidates = new[] { name, "m_" + name };

        foreach (var candidate in candidates)
        {
            var field = type.GetField(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (field != null)
            {
                field.SetValue(obj, ConvertForType(value, field.FieldType));
                return true;
            }

            var property = type.GetProperty(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is { CanWrite: true } && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(obj, ConvertForType(value, property.PropertyType));
                return true;
            }
        }

        return false;
    }

    private static object? ConvertForType(object? value, Type targetType)
    {
        if (value == null)
            return null;

        var actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (actualTarget.IsInstanceOfType(value))
            return value;

        if (actualTarget.IsEnum)
            return Enum.ToObject(actualTarget, value);

        return Convert.ChangeType(value, actualTarget, CultureInfo.InvariantCulture);
    }

    private static void SetListItem(IList list, int index, object? value)
    {
        if (index < 0 || index >= list.Count)
            return;

        var targetType = GetListElementType(list) ?? list[index]?.GetType() ?? typeof(object);
        list[index] = ConvertForType(value, targetType);
    }

    private static void AddListItem(IList? list, object? value)
    {
        if (list == null)
            return;

        var targetType = GetListElementType(list) ?? typeof(object);
        list.Add(ConvertForType(value, targetType));
    }

    private static Type? GetListElementType(IList list)
    {
        var type = list.GetType();
        if (type.IsArray)
            return type.GetElementType();

        return type.GetInterfaces()
            .Concat(new[] { type })
            .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>))
            ?.GetGenericArguments()[0];
    }

    private static float ReadFloatToken(JToken? token, object? fallback)
    {
        if (token == null || token.Type == JTokenType.Null)
            return fallback == null ? 0.0f : Convert.ToSingle(fallback, CultureInfo.InvariantCulture);

        return token.Value<float>();
    }

    private static Vector4 ReadVectorToken(JToken? token, object? fallback)
    {
        var fallbackVector = fallback is Vector4 vector ? vector : Vector4.Zero;
        return token switch
        {
            JObject obj => ReadVector(obj, fallbackVector),
            _ => fallbackVector
        };
    }

    private static Vector4 ReadVector(JObject? obj, Vector4 fallback)
    {
        if (obj == null)
            return fallback;

        return new Vector4(
            ReadFloatProperty(obj, "x", fallback.X),
            ReadFloatProperty(obj, "y", fallback.Y),
            ReadFloatProperty(obj, "z", fallback.Z),
            ReadFloatProperty(obj, "w", fallback.W));
    }

    private static float ReadFloatProperty(JObject obj, string name, float fallback)
    {
        return obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token)
            ? token.Value<float>()
            : fallback;
    }

    private static Matrix4x4 ReadMatrixToken(JToken? token, object? fallback)
    {
        var fallbackMatrix = fallback is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
        if (token is JObject obj && obj["rows"] is JArray rows)
            return ReadMatrixRows(rows);

        return fallbackMatrix;
    }

    private static Matrix4x4 ReadMatrixRows(JArray rows)
    {
        var r0 = ReadMatrixRow(rows.ElementAtOrDefault(0));
        var r1 = ReadMatrixRow(rows.ElementAtOrDefault(1));
        var r2 = ReadMatrixRow(rows.ElementAtOrDefault(2));
        var r3 = ReadMatrixRow(rows.ElementAtOrDefault(3));

        return new Matrix4x4(
            r0[0], r0[1], r0[2], r0[3],
            r1[0], r1[1], r1[2], r1[3],
            r2[0], r2[1], r2[2], r2[3],
            r3[0], r3[1], r3[2], r3[3]);
    }

    private static float[] ReadMatrixRow(JToken? row)
    {
        var values = row as JArray ?? new JArray();
        return new[]
        {
            values.ElementAtOrDefault(0)?.Value<float>() ?? 0.0f,
            values.ElementAtOrDefault(1)?.Value<float>() ?? 0.0f,
            values.ElementAtOrDefault(2)?.Value<float>() ?? 0.0f,
            values.ElementAtOrDefault(3)?.Value<float>() ?? 0.0f
        };
    }
    private static object? GetValue(object? obj, string name)
    {
        if (obj == null)
            return null;

        var type = obj.GetType();
        var candidates = new[] { name, "m_" + name };

        foreach (var candidate in candidates)
        {
            var field = type.GetField(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (field != null)
                return field.GetValue(obj);

            var property = type.GetProperty(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is { CanRead: true } && property.GetIndexParameters().Length == 0)
                return property.GetValue(obj);
        }

        return null;
    }

    private static string? GetString(object? obj, string name)
    {
        return GetValue(obj, name) as string;
    }

    private static IReadOnlyList<object>? GetList(object? value)
    {
        if (value == null || value is string)
            return null;

        if (value is IEnumerable enumerable)
            return enumerable.Cast<object>().ToList();

        return null;
    }

    private static IList GetMutableList(object? value, string name)
    {
        if (value is IList list)
            return list;

        throw new InvalidOperationException($"{name} is not a mutable list.");
    }

    private static object? GetFirst(object? value)
    {
        return GetList(value)?.FirstOrDefault();
    }

    private static int ToInt(object? value, int fallback)
    {
        if (value == null)
            return fallback;

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ToBool(object? value)
    {
        if (value is bool b)
            return b;

        return value != null && bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    private static string FormatSimpleValue(object? value)
    {
        return value switch
        {
            null => "(none)",
            Vector4 vector => $"[{vector.X}, {vector.Y}, {vector.Z}, {vector.W}]",
            Matrix4x4 => "(matrix)",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "(none)"
        };
    }

    private static JToken? ToToken(object? value)
    {
        return value switch
        {
            null => null,
            Vector4 vector => Vector(vector.X, vector.Y, vector.Z, vector.W),
            Matrix4x4 matrix => new JObject { ["rows"] = MatrixRows(matrix) },
            string text => text,
            bool boolean => boolean,
            byte or sbyte or short or ushort or int or uint or long or ulong => JToken.FromObject(value),
            float f => f,
            double d => d,
            decimal m => m,
            _ => JToken.FromObject(value, RawJsonSerializer())
        };
    }

    private static JObject Vector(float x, float y, float z, float w)
    {
        return new JObject
        {
            ["x"] = x,
            ["y"] = y,
            ["z"] = z,
            ["w"] = w
        };
    }

    private static JArray MatrixRows(Matrix4x4 matrix)
    {
        return new JArray
        {
            new JArray(matrix.M11, matrix.M12, matrix.M13, matrix.M14),
            new JArray(matrix.M21, matrix.M22, matrix.M23, matrix.M24),
            new JArray(matrix.M31, matrix.M32, matrix.M33, matrix.M34),
            new JArray(matrix.M41, matrix.M42, matrix.M43, matrix.M44)
        };
    }

    private static string CompressJson(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);

        return Convert.ToBase64String(output.ToArray());
    }

    private static string DecompressJson(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException("The technical HKX2 payload is empty.");

        var bytes = Convert.FromBase64String(encoded);
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
    private static string SerializeRaw(hkRootLevelContainer root)
    {
        return JsonConvert.SerializeObject(root, Formatting.Indented, RawJsonSettings());
    }

    private static hkRootLevelContainer DeserializeRoot(string json)
    {
        return JsonConvert.DeserializeObject<hkRootLevelContainer>(json, RawJsonSettings())
            ?? throw new InvalidOperationException("JSON did not contain an HKX root object.");
    }

    private static JsonSerializer RawJsonSerializer()
    {
        return JsonSerializer.Create(RawJsonSettings());
    }

    private static JsonSerializerSettings RawJsonSettings()
    {
        return new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            TypeNameHandling = TypeNameHandling.Auto,
            ContractResolver = new HavokContractResolver(),
            Converters = { new Vector4JsonConverter(), new Matrix4x4JsonConverter() }
        };
    }

    private sealed class HavokContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyName?.StartsWith("m_", StringComparison.Ordinal) == true)
                property.PropertyName = property.PropertyName[2..];

            if (property.PropertyName is "Signature" or "IsIdentity")
                property.Ignored = true;

            return property;
        }
    }

    private sealed class Vector4JsonConverter : JsonConverter<Vector4>
    {
        public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.X);
            writer.WritePropertyName("y");
            writer.WriteValue(value.Y);
            writer.WritePropertyName("z");
            writer.WriteValue(value.Z);
            writer.WritePropertyName("w");
            writer.WriteValue(value.W);
            writer.WriteEndObject();
        }

        public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            return new Vector4(
                ReadFloat(obj, "x", "X"),
                ReadFloat(obj, "y", "Y"),
                ReadFloat(obj, "z", "Z"),
                ReadFloat(obj, "w", "W"));
        }
    }

    private sealed class Matrix4x4JsonConverter : JsonConverter<Matrix4x4>
    {
        public override void WriteJson(JsonWriter writer, Matrix4x4 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("rows");
            MatrixRows(value).WriteTo(writer);
            writer.WriteEndObject();
        }

        public override Matrix4x4 ReadJson(JsonReader reader, Type objectType, Matrix4x4 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);

            if (obj["rows"] is JArray rows && rows.Count >= 4)
            {
                var r0 = ReadRow(rows[0]);
                var r1 = ReadRow(rows[1]);
                var r2 = ReadRow(rows[2]);
                var r3 = ReadRow(rows[3]);
                return new Matrix4x4(
                    r0[0], r0[1], r0[2], r0[3],
                    r1[0], r1[1], r1[2], r1[3],
                    r2[0], r2[1], r2[2], r2[3],
                    r3[0], r3[1], r3[2], r3[3]);
            }

            return new Matrix4x4(
                ReadFloat(obj, "M11"), ReadFloat(obj, "M12"), ReadFloat(obj, "M13"), ReadFloat(obj, "M14"),
                ReadFloat(obj, "M21"), ReadFloat(obj, "M22"), ReadFloat(obj, "M23"), ReadFloat(obj, "M24"),
                ReadFloat(obj, "M31"), ReadFloat(obj, "M32"), ReadFloat(obj, "M33"), ReadFloat(obj, "M34"),
                ReadFloat(obj, "M41"), ReadFloat(obj, "M42"), ReadFloat(obj, "M43"), ReadFloat(obj, "M44"));
        }

        private static float[] ReadRow(JToken row)
        {
            var values = row as JArray ?? new JArray();
            return new[]
            {
                values.ElementAtOrDefault(0)?.Value<float>() ?? 0,
                values.ElementAtOrDefault(1)?.Value<float>() ?? 0,
                values.ElementAtOrDefault(2)?.Value<float>() ?? 0,
                values.ElementAtOrDefault(3)?.Value<float>() ?? 0
            };
        }
    }

    private static float ReadFloat(JObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
                return token.Value<float>();
        }

        return 0;
    }

    private sealed class ReferenceEquality : IEqualityComparer<object>
    {
        public static readonly ReferenceEquality Instance = new();

        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}



