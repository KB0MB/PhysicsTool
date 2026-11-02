using System.Numerics;
using System.Text;
using HKCLTool;
using static HKCLTool.BotwAampWriter;

namespace PhysicsTool.Core;

// Retains the original AAMP graph; only owned cloth/support fields are replaced.
public sealed class BphysicsEditingDocument
{
    private readonly byte[] _source;
    private readonly AampReadArchive _archive;
    private readonly List<AampReadObject?> _slotSources;
    public BphysicsDocument Settings { get; }
    public int PreservedSections => _archive.Root.FindChild("ParamSet")!.Children.Count(c => c.Hash != Crc32("Cloth")) +
        _archive.Root.FindChild("ParamSet")!.Objects.Count(o => o.Hash != 1258832850 && o.Hash != Crc32("SupportBone"));

    private BphysicsEditingDocument(byte[] bytes)
    {
        _source = bytes.ToArray();
        _archive = BotwAampReader.Read(_source);
        Settings = BphysicsService.Parse(_source);
        _slotSources = _archive.Root.FindChild("ParamSet")!.FindChild("Cloth")?.Objects
            .Where(o => o.Find("name") != null).Select(o => (AampReadObject?)o).ToList() ?? [];
    }

    public static BphysicsEditingDocument Load(string path) => Parse(File.ReadAllBytes(path));
    public static BphysicsEditingDocument Parse(byte[] bytes) => new(bytes);

    public int AddSlot(int duplicate = -1)
    {
        var original = duplicate >= 0 ? Settings.Cloths[duplicate] : new BphysicsClothProfile("Cloth", "Head");
        var stem = duplicate >= 0 ? original.Name + "_Copy" : "Cloth";
        var name = stem;
        for (var n = 2; Settings.Cloths.Any(p => p.Name == name); n++) name = stem + "_" + n;
        Settings.Cloths.Add(original with { Name = name });
        _slotSources.Add(duplicate >= 0 ? _slotSources[duplicate] : null);
        return Settings.Cloths.Count - 1;
    }

    public void RemoveSlot(int index) { Settings.Cloths.RemoveAt(index); _slotSources.RemoveAt(index); }
    public void MoveSlot(int index, int target)
    {
        if (index < 0 || target < 0 || index >= Settings.Cloths.Count || target >= Settings.Cloths.Count)
            throw new ArgumentOutOfRangeException(nameof(target));
        (Settings.Cloths[index], Settings.Cloths[target]) = (Settings.Cloths[target], Settings.Cloths[index]);
        (_slotSources[index], _slotSources[target]) = (_slotSources[target], _slotSources[index]);
    }

    public void Validate()
    {
        // An existing file may legitimately use only rigid bodies, or disable all physics.
        var d = Settings;
        if (d.UseCloth && (d.Cloths.Count == 0 || string.IsNullOrWhiteSpace(d.HkclPath)))
            throw new InvalidOperationException("Enabled cloth needs an HKCL path and at least one cloth slot.");
        if (d.UseSupportBone && string.IsNullOrWhiteSpace(d.SupportBonePath))
            throw new InvalidOperationException("Enabled support bones need a BPHYSSB path.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in d.Cloths)
        {
            var name = p.Name.StartsWith("Link:", StringComparison.OrdinalIgnoreCase) ? p.Name[5..] : p.Name;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(p.BaseBone) || !names.Add(name))
                throw new InvalidOperationException("Each cloth slot needs a unique name and a base bone.");
            if (new[] { p.WindFrequency, p.WindDrag, p.WindMinSpeed, p.WindMaxSpeed, p.SubWindFactorMain, p.SubWindFactorAdd }.Any(v => !float.IsFinite(v)) ||
                p.WindFrequency < 0 || p.WindDrag < 0 || p.WindMinSpeed > p.WindMaxSpeed)
                throw new InvalidOperationException($"{p.Name}: frequency and drag must be nonnegative, and minimum speed cannot exceed maximum speed.");
            CheckString(p.Name); CheckString(p.BaseBone);
        }
        CheckString(d.HkclPath); CheckString(d.SupportBonePath ?? "");
        if (new[] { d.SubWindDirection.X, d.SubWindDirection.Y, d.SubWindDirection.Z, d.SubWindSpeed, d.SubWindFrequency }.Any(v => !float.IsFinite(v)) ||
            d.SubWindSpeed < 0 || d.SubWindFrequency < 0)
            throw new InvalidOperationException("Shared wind values must be finite; speed and frequency cannot be negative.");
        static void CheckString(string value)
        {
            if (value.Contains('\0') || Encoding.UTF8.GetByteCount(value) > 255)
                throw new InvalidOperationException("Names and paths must fit within 255 UTF-8 bytes and cannot contain null characters.");
        }
    }

    public byte[] Snapshot()
    {
        if (_slotSources.Count != Settings.Cloths.Count) throw new InvalidOperationException("Cloth slot metadata is out of sync.");
        var root = Copy(_archive.Root);
        var set = root.Children.Single(c => c.NameHash == Crc32("ParamSet"));
        var generated = BphysicsService.BuildRoot(Settings, keepInactive: true).Children.Single();
        var settings = set.Objects.FirstOrDefault(o => o.NameHash == 1258832850);
        if (settings == null) { settings = new("", 1258832850); set.Objects.Add(settings); }
        Put(settings, AampParameterNode.Boolean("use_cloth", Settings.UseCloth));
        Put(settings, AampParameterNode.Boolean("use_support_bone", Settings.UseSupportBone));
        var support = generated.Objects.FirstOrDefault(o => o.Name == "SupportBone");
        if (support != null) MergeObject(set.Objects, support);
        var cloth = generated.Children.FirstOrDefault();
        if (cloth != null)
        {
            var target = set.Children.FirstOrDefault(c => c.NameHash == Crc32("Cloth"));
            if (target == null) { target = new("Cloth"); set.Children.Add(target); }
            target.Objects.RemoveAll(o => o.Parameters.Any(p => Hash(p) == Crc32("name")));
            MergeObject(target.Objects, cloth.Objects[0]);
            MergeObject(target.Objects, cloth.Objects[1]);
            for (var i = 0; i < Settings.Cloths.Count; i++)
            {
                var profile = new AampObjectNode("Cloth_" + i);
                if (_slotSources[i] is { } old) profile.Parameters.AddRange(old.Parameters.Select(p => AampParameterNode.Raw(p.Hash, p.Type, p.Bytes)));
                foreach (var parameter in cloth.Objects[i + 2].Parameters) Put(profile, parameter);
                // Existing runtime names are literal bindings, including any Link: prefix.
                Put(profile, AampParameterNode.String256Value("name", Settings.Cloths[i].Name));
                Put(profile, AampParameterNode.StringReferenceValue("base_bone", Settings.Cloths[i].BaseBone));
                target.Objects.Add(profile);
            }
        }
        else if (set.Children.FirstOrDefault(c => c.NameHash == Crc32("Cloth")) is { } empty)
        {
            empty.Objects.RemoveAll(o => o.Parameters.Any(p => Hash(p) == Crc32("name")));
            var header = empty.Objects.FirstOrDefault(o => o.NameHash == Crc32("ClothHeader"));
            if (header != null) Put(header, AampParameterNode.Integer("cloth_num", 0));
        }
        return WriteArchive(_archive.Type, root, BitConverter.ToUInt32(_source, 4), BitConverter.ToUInt32(_source, 8), BitConverter.ToUInt32(_source, 16));
    }

    public void Save(string path)
    {
        Validate();
        var bytes = Snapshot();
        Parse(bytes).Validate();
        AtomicFile.WriteAllBytes(path, bytes);
    }

    private static uint Hash(AampParameterNode p) => p.NameHash ?? Crc32(p.Name);
    private static void Put(AampObjectNode node, AampParameterNode parameter)
    {
        var i = node.Parameters.FindIndex(p => Hash(p) == Hash(parameter));
        if (i < 0) node.Parameters.Add(parameter); else node.Parameters[i] = parameter;
    }
    private static void MergeObject(List<AampObjectNode> objects, AampObjectNode source)
    {
        var hash = source.NameHash ?? Crc32(source.Name);
        var target = objects.FirstOrDefault(o => (o.NameHash ?? Crc32(o.Name)) == hash);
        if (target == null) { objects.Add(source); return; }
        foreach (var p in source.Parameters) Put(target, p);
    }
    private static AampListNode Copy(AampReadList source)
    {
        var result = new AampListNode("", source.Hash);
        result.Children.AddRange(source.Children.Select(Copy));
        foreach (var o in source.Objects)
        {
            var node = new AampObjectNode("", o.Hash);
            node.Parameters.AddRange(o.Parameters.Select(p => AampParameterNode.Raw(p.Hash, p.Type, p.Bytes)));
            result.Objects.Add(node);
        }
        return result;
    }
}
