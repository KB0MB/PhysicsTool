using HKCLTool;

namespace PhysicsTool.Core;

internal static class IconBfska
{
    internal static ToolboxActorAnimation Load(byte[] bytes)
    {
        if (bytes.Length < 32 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x7f, 102, 114, 101, 115, 83, 85, 66 }) ||
            !bytes.AsSpan(12, 4).SequenceEqual("FSKA"u8))
            throw new InvalidDataException("Select a skeletal animation exported as .bfska.");
        uint platform = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24, 4));
        if (platform > 1) throw new InvalidDataException("Unknown BFSKA platform.");
        bool isSwitch = platform == 1;
        object animation;
        if (isSwitch)
        {
            var clip = new Syroot.NintenTools.NSW.Bfres.SkeletalAnim();
            using var stream = new MemoryStream(bytes, false);
            clip.Import(stream, false); animation = clip;
        }
        else
        {
            // The Wii U reader exposes a path-only standalone import API.
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bfska");
            try
            {
                File.WriteAllBytes(path, bytes);
                var clip = new Syroot.NintenTools.Bfres.SkeletalAnim();
                clip.Import(path, new Syroot.NintenTools.Bfres.ResFile()); animation = clip;
            }
            finally { File.Delete(path); }
        }
        return new ToolboxActorAnimation(animation, "Imported BFSKA");
    }
}
