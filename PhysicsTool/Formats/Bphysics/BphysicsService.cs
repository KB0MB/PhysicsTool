using System.Numerics;

namespace HKCLTool;

/// <summary>Runtime attachment data used by BotW's .bphysics AAMP sidecar.</summary>
public sealed record BphysicsClothProfile(
    string Name,
    string BaseBone,
    float WindFrequency = 3.5f,
    float WindDrag = 8.0f,
    float WindMinSpeed = -2.0f,
    float WindMaxSpeed = 12.0f,
    float SubWindFactorMain = 1.0f,
    float SubWindFactorAdd = 0.0f,
    bool WindEnabled = true,
    bool WritebackToLocal = false);

/// <summary>
/// Deliberately small authoring model for a BPHYSICS file. Support-bone-only
/// files are valid: set UseCloth false and provide a SupportBonePath.
/// </summary>
public sealed class BphysicsDocument
{
    public string HkclPath { get; set; } = string.Empty;
    public bool UseCloth { get; set; } = true;
    public bool UseSupportBone { get; set; }
    public string? SupportBonePath { get; set; }
    public Vector3 SubWindDirection { get; set; } = Vector3.UnitY;
    public float SubWindFrequency { get; set; } = 5.0f;
    public float SubWindSpeed { get; set; } = 50.0f;
    public List<BphysicsClothProfile> Cloths { get; } = new();
}

/// <summary>
/// Writes the verified BotW bphysics hierarchy:
/// param_root -> ParamSet -> optional Cloth, with an optional SupportBone
/// object. No existing BPHYSICS file is used as a template.
/// </summary>
public static class BphysicsService
{
    private const uint ParamSetSettingsHash = 1258832850;

    /// <summary>
    /// Reads BotW's small runtime-sidecar format. Keeping this here lets the
    /// converter use verified wind and attachment values from matched files
    /// instead of relying only on family-name defaults.
    /// </summary>
    public static BphysicsDocument Load(string path)
    {
        var archive = BotwAampReader.Read(path);
        if (!string.Equals(archive.Type, "xml", StringComparison.Ordinal))
            throw new InvalidDataException("The AAMP archive is not a BotW XML BPHYSICS file.");

        var paramSet = archive.Root.FindChild("ParamSet")
            ?? throw new InvalidDataException("BPHYSICS has no ParamSet list.");
        var settings = paramSet.Objects.FirstOrDefault(node => node.Hash == ParamSetSettingsHash);
        var document = new BphysicsDocument
        {
            UseCloth = settings?.Find("use_cloth")?.AsBoolean() ?? false,
            UseSupportBone = settings?.Find("use_support_bone")?.AsBoolean() ?? false
        };

        var supportBone = paramSet.Objects.FirstOrDefault(node =>
            node.Find("support_bone_setup_file_path") != null);
        document.SupportBonePath = supportBone?.Find("support_bone_setup_file_path")?.AsString();

        var cloth = paramSet.FindChild("Cloth");
        if (cloth == null)
            return document;

        var header = cloth.Objects.FirstOrDefault(node => node.Hash == BotwAampWriter.Crc32("ClothHeader"));
        document.HkclPath = header?.Find("cloth_setup_file_path")?.AsString() ?? string.Empty;

        var subWind = cloth.Objects.FirstOrDefault(node => node.Hash == BotwAampWriter.Crc32("ClothSubWind"));
        if (subWind != null)
        {
            document.SubWindDirection = subWind.Find("sub_wind_direction")?.AsVector3() ?? Vector3.UnitY;
            document.SubWindFrequency = subWind.Find("sub_wind_frequency")?.AsSingle() ?? document.SubWindFrequency;
            document.SubWindSpeed = subWind.Find("sub_wind_speed")?.AsSingle() ?? document.SubWindSpeed;
        }

        foreach (var profile in cloth.Objects.Where(node => node.Find("name") != null))
        {
            document.Cloths.Add(new BphysicsClothProfile(
                profile.Find("name")!.AsString(),
                profile.Find("base_bone")?.AsString() ?? "Head",
                profile.Find("wind_frequency")?.AsSingle() ?? 3.5f,
                profile.Find("wind_drag")?.AsSingle() ?? 8.0f,
                profile.Find("wind_min_speed")?.AsSingle() ?? -2.0f,
                profile.Find("wind_max_speed")?.AsSingle() ?? 12.0f,
                profile.Find("sub_wind_factor_main")?.AsSingle() ?? 1.0f,
                profile.Find("sub_wind_factor_add")?.AsSingle() ?? 0.0f,
                profile.Find("wind_enable")?.AsBoolean() ?? true,
                profile.Find("writeback_to_local")?.AsBoolean() ?? false));
        }

        return document;
    }

    public static void Save(string path, BphysicsDocument document)
    {
        if (document.UseCloth && (document.Cloths.Count == 0 || string.IsNullOrWhiteSpace(document.HkclPath)))
            throw new InvalidOperationException("A cloth-enabled BPHYSICS export needs an HKCL path and at least one cloth profile.");
        if (document.UseSupportBone && string.IsNullOrWhiteSpace(document.SupportBonePath))
            throw new InvalidOperationException("A support-bone BPHYSICS export needs a .bphyssb path.");
        if (!document.UseCloth && !document.UseSupportBone)
            throw new InvalidOperationException("BPHYSICS must enable cloth, support bones, or both.");

        var root = new BotwAampWriter.AampListNode("param_root");
        var paramSet = new BotwAampWriter.AampListNode("ParamSet");
        root.Children.Add(paramSet);

        var settings = new BotwAampWriter.AampObjectNode("ParamSetSettings", ParamSetSettingsHash);
        settings.Parameters.Add(BotwAampWriter.AampParameterNode.Integer("use_rigid_body_set_num", 0));
        settings.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("use_ragdoll", false));
        settings.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("use_cloth", document.UseCloth));
        settings.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("use_support_bone", document.UseSupportBone));
        settings.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("use_character_controller", false));
        settings.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("use_contact_info", false));
        settings.Parameters.Add(BotwAampWriter.AampParameterNode.Integer("use_edge_rigid_body_num", 0));
        settings.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("use_system_group_handler", true));
        paramSet.Objects.Add(settings);

        if (document.UseSupportBone)
        {
            var supportBone = new BotwAampWriter.AampObjectNode("SupportBone");
            supportBone.Parameters.Add(BotwAampWriter.AampParameterNode.String256Value(
                "support_bone_setup_file_path", NormalizeGamePath(document.SupportBonePath!)));
            paramSet.Objects.Add(supportBone);
        }

        if (document.UseCloth)
        {
            var cloth = new BotwAampWriter.AampListNode("Cloth");
            paramSet.Children.Add(cloth);

            var header = new BotwAampWriter.AampObjectNode("ClothHeader");
            header.Parameters.Add(BotwAampWriter.AampParameterNode.String256Value(
                "cloth_setup_file_path", NormalizeGamePath(document.HkclPath)));
            header.Parameters.Add(BotwAampWriter.AampParameterNode.Integer("cloth_num", document.Cloths.Count));
            cloth.Objects.Add(header);

            var subWind = new BotwAampWriter.AampObjectNode("ClothSubWind");
            subWind.Parameters.Add(BotwAampWriter.AampParameterNode.Vector3("sub_wind_direction", document.SubWindDirection));
            subWind.Parameters.Add(BotwAampWriter.AampParameterNode.Single("sub_wind_frequency", document.SubWindFrequency));
            subWind.Parameters.Add(BotwAampWriter.AampParameterNode.Single("sub_wind_speed", document.SubWindSpeed));
            cloth.Objects.Add(subWind);

            foreach (var (profile, index) in document.Cloths.Select((profile, index) => (profile, index)))
            {
                var clothProfile = new BotwAampWriter.AampObjectNode($"Cloth_{index}");
                clothProfile.Parameters.Add(BotwAampWriter.AampParameterNode.String256Value("name", StripLinkPrefix(profile.Name)));
                clothProfile.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("base_bone", StripLinkPrefix(profile.BaseBone)));
                clothProfile.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("wind_enable", profile.WindEnabled));
                clothProfile.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("writeback_to_local", profile.WritebackToLocal));
                clothProfile.Parameters.Add(BotwAampWriter.AampParameterNode.Single("wind_frequency", profile.WindFrequency));
                clothProfile.Parameters.Add(BotwAampWriter.AampParameterNode.Single("wind_drag", profile.WindDrag));
                clothProfile.Parameters.Add(BotwAampWriter.AampParameterNode.Single("wind_min_speed", profile.WindMinSpeed));
                clothProfile.Parameters.Add(BotwAampWriter.AampParameterNode.Single("wind_max_speed", profile.WindMaxSpeed));
                clothProfile.Parameters.Add(BotwAampWriter.AampParameterNode.Single("sub_wind_factor_main", profile.SubWindFactorMain));
                clothProfile.Parameters.Add(BotwAampWriter.AampParameterNode.Single("sub_wind_factor_add", profile.SubWindFactorAdd));
                cloth.Objects.Add(clothProfile);
            }
        }

        var bytes = BotwAampWriter.WriteXml(root);
        Validate(bytes);
        File.WriteAllBytes(path, bytes);
    }

    private static string NormalizeGamePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string StripLinkPrefix(string value) => value.StartsWith("Link:", StringComparison.OrdinalIgnoreCase)
        ? value[5..]
        : value;

    private static void Validate(byte[] bytes)
    {
        if (bytes.Length < 0x40 || !bytes.AsSpan(0, 4).SequenceEqual("AAMP"u8))
            throw new InvalidDataException("The generated BPHYSICS file has no AAMP header.");

        var declaredSize = BitConverter.ToUInt32(bytes, 0x0c);
        var rootOffset = checked(0x30 + (int)BitConverter.ToUInt32(bytes, 0x14));
        if (declaredSize != bytes.Length || rootOffset < 0x34 || rootOffset > bytes.Length - 12)
            throw new InvalidDataException("The generated BPHYSICS AAMP header is internally inconsistent.");

        var typeEnd = Array.IndexOf(bytes, (byte)0, 0x30);
        var type = typeEnd > 0x30 ? System.Text.Encoding.UTF8.GetString(bytes, 0x30, typeEnd - 0x30) : string.Empty;
        if (!string.Equals(type, "xml", StringComparison.Ordinal))
            throw new InvalidDataException("The generated BPHYSICS file did not retain the expected xml AAMP type.");
    }
}
