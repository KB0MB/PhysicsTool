using HKCLTool;
using PhysicsTool.Core;

internal static class IconRaulVisibilityChecks
{
    internal static void Inspect(string root, string[] fallbacks)
    {
        root=IconCaptureAsset.Root(root);
        foreach(string name in new[]{"Player","PlayerRaulHand","Armor_001_Upper","Armor_005_Upper","Armor_006_Upper","Armor_009_Upper","Armor_015_Upper","Armor_001_Head"})
        {
            string? path=new[]{root}.Concat(fallbacks).Select(r=>Path.Combine(IconCaptureAsset.Root(r),"Pack/Actor",name+".pack.zs")).FirstOrDefault(File.Exists);
            if(path==null)continue;
            var actor=new BotwActorResources(path,new[]{root}.Concat(fallbacks));
            var rules=actor.ReadEquipmentVisibility();
            Console.WriteLine(name+" HIDE="+string.Join(",",rules.HideGroups));
            foreach(var group in rules.Groups) Console.WriteLine("  "+group.Key+"="+string.Join(",",group.Value));
            if (name.EndsWith("_Upper") || name.EndsWith("_Head"))
            {
                var hidden=IconArmorProps.HiddenRaulMaterials(actor);
                string[] expected=rules.HideGroups.Contains("G_Upper_RaulHand_All_Hide") ? [] :
                    rules.HideGroups.Contains("G_Upper_RaulHand_A") ? ["A"] : rules.HideGroups.Contains("G_Upper_RaulHand_B") ? ["A","B"] : ["A","B","C"];
                var visible=new[]{"A","B","C"}.Where(s=>!IconArmorProps.HiddenByDefault(name,true,IconArmorProps.Prefix+"Upper_Skin_RaulSkin_"+s,hidden)).ToArray();
                if(!visible.SequenceEqual(expected))throw new Exception("Native RaulSkin mismatch: "+name);
                Console.WriteLine("PASS native RaulSkin "+name+" visible="+string.Join(",",visible));
            }
        }
    }
}
