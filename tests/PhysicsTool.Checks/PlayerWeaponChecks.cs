using HKCLTool;
using BymlLibrary;
using System.Numerics;

internal static class PlayerWeaponChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static Dictionary<PlayerWeaponKind, PlayerWeaponSelection> Outfit(bool sword = false, bool bow = false) => new()
    {
        [PlayerWeaponKind.Sword] = new("Weapon_Sword_070", sword),
        [PlayerWeaponKind.Shield] = new("Weapon_Shield_001", sword),
        [PlayerWeaponKind.Bow] = new("Weapon_Bow_001", bow)
    };
    internal static void Synthetic()
    {
        Require(PlayerEquipmentNames.Name("Armor_001_Upper", false) == "Hylian Tunic", "BotW armor name missing");
        Require(PlayerEquipmentNames.Name("Weapon_Shield_030", true) == "Hylian Shield", "TotK shield name missing");
        Require(PlayerEquipmentNames.Name("Weapon_Sword_023", false) == "Ancient Short Sword", "BotW-specific weapon name missing");
        Require(PlayerEquipmentNames.Name("Armor_Default_Upper", false) == "Default upper", "Default armor label lost");
        Require(PlayerEquipmentNames.Name("Weapon_Sword_Mod", false) == "Weapon_Sword_Mod", "Unknown mod actor ID was lost");
        var labels = PlayerEquipmentNames.Labels(["Armor_001_Upper", "Armor_002_Upper"], false);
        Require(labels["Armor_001_Upper"] != labels["Armor_002_Upper"] && labels.Values.All(n => n.StartsWith("Hylian Tunic")), "Upgrade labels are ambiguous");
        foreach (var kind in Enum.GetValues<PlayerWeaponKind>())
        {
            Require(new PlayerWeaponEntry(kind, "Weapon_" + kind + "_001", "") { DisplayName = "Named item" }.ToString() == "Named item", "Weapon text search does not use its item name");
            Require(PlayerWeaponParameters.Bone(kind, false) == "Pod_A", "Stowed weapon binding changed");
            Require(PlayerWeaponParameters.Bone(kind, true) == (kind is PlayerWeaponKind.Shield or PlayerWeaponKind.Bow ? "Weapon_L" : "Weapon_R"), "Drawn hand is incorrect");
        }
        var p = PlayerWeaponParameters.Read(new(Byml.FromText("PlayerEquipScale: 2.0\nSheathRotateOffset: {X: 0.0, Y: 0.0, Z: 90.0}\nSheathTransOffset: {X: 1.0, Y: 2.0, Z: 3.0}\nSheathName: Work/Actor/Weapon_Sheath_070.engine__actor__ActorParam.gyml\n").GetMap()));
        Require(Vector3.Distance(Vector3.Transform(Vector3.UnitX, p.Offset(false)), new(1, 4, 3)) < 1e-5f, "Equipment degree/SRT conversion incorrect");
        Require(p.Sheath == "Weapon_Sheath_070" && p.Quiver == "", "Actor reference parsing incorrect");
        Require(Vector3.Distance(Vector3.Transform(Vector3.UnitX, p.Offset(true)), new(2, 0, 0)) < 1e-5f, "Missing draw offsets must be zero");
        Console.WriteLine("PASS: equipment names, default states, hand bindings, actor references and degree/SRT offsets.");
    }
    internal static void Verify(BotwActorScene scene)
    {
        var bones = scene.CurrentModel.Bones.GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First());
        var rest = scene.RestModel.Bones.GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First());
        foreach (var (index, attachment) in scene.EquipmentAttachments)
        {
            Require(bones.ContainsKey(attachment.Bone), "Equipment parent missing");
            Matrix4x4.Invert(attachment.Offset * BotwActorScene.Matrix(rest[attachment.Bone]), out var inverse);
            var delta = inverse * attachment.Offset * BotwActorScene.Matrix(bones[attachment.Bone]);
            var equipment = bones.Values.Where(b => b.Name.StartsWith($"Equipment_{index}_")).ToArray();
            Require(equipment.Length > 0, "Equipment skeleton missing");
            foreach (var bone in equipment)
            {
                var expected = BotwActorScene.Matrix(rest[bone.Name]) * delta;
                Require(Vector3.Distance(bone.Position, expected.Translation) < 1e-4f, "Equipment drifted from animated attachment: " + attachment.Actor);
                Matrix4x4.Decompose(expected, out _, out var rotation, out _);
                Require(Math.Abs(Quaternion.Dot(bone.Rotation, rotation)) > .9999f, "Equipment inherited character animation: " + attachment.Actor);
            }
        }
        Require(scene.CurrentModel.Meshes.SelectMany(m => m.Vertices).All(float.IsFinite), "Equipment produced nonfinite vertices");
    }
    internal static void Native(string path)
    {
        PhysicsTool.Desktop.Rendering.PortableModelImporter.ConfigureBfres();
        foreach (var state in new[] { Outfit(), Outfit(true), Outfit(bow: true),
            new Dictionary<PlayerWeaponKind, PlayerWeaponSelection> { [PlayerWeaponKind.LSword] = new("Weapon_LSword_001", true) },
            new Dictionary<PlayerWeaponKind, PlayerWeaponSelection> { [PlayerWeaponKind.Spear] = new("Weapon_Spear_001", true) } })
        {
            using var scene = BotwActorScene.Load(path, weapons: state);
            Require(scene.Weapons.Count == state.Count, "Equipment state missing");
            if (state.ContainsKey(PlayerWeaponKind.Sword))
            {
                Require(scene.EquipmentAttachments.Values.Any(a => a.Actor == "Weapon_Sheath_070" && a.Bone == "Pod_A"), "Master Sword sheath missing or moved to hand");
                Require(scene.EquipmentAttachments.Values.Any(a => a.Actor == "Weapon_Quiver_001" && a.Bone == "Pod_B"), "Bow quiver missing");
                Require(scene.EquipmentAttachments.Values.Any(a => a.Actor == "NormalArrow" && a.Bone == "Weapon_R") == state[PlayerWeaponKind.Bow].Equipped, "Drawn arrow state wrong");
            }
            var clip = scene.Animations.First(a => a.Name == "Assassin_DotonCharge");
            foreach (var simulate in new[] { false, true })
            for (var frame = 0; frame <= 24; frame += 6)
            { scene.Update(clip, frame, simulate, 1f / 60, frame == 0); Verify(scene); }
            Require(!scene.Resources.Warnings.Any(w => w.StartsWith("Missing equipment")), "Native equipment dependency not loaded");
            Console.WriteLine("PASS: native attachments " + string.Join(", ", state.Select(p => p.Value.Name + (p.Value.Equipped ? " drawn" : " stowed"))));
        }
    }
}
