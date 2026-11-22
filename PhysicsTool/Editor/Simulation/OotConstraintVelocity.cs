using System.Numerics;

namespace HKCLTool;

internal enum OotConstraintVelocityMode { None, Preserve, Blend }

internal static class OotConstraintVelocity
{
    // hclSimClothStepInfo setup (0x7101b1aa48). Legacy stiffness model 1
    // never preserves velocity; the native default model is 2.
    internal static OotConstraintVelocityMode ForStep(int stiffnessModel, int totalSubsteps,
        float frequencyScale, float timeScale, float stiffnessScale, bool direct)
    {
        if (stiffnessModel == 1 || totalSubsteps == 1 && frequencyScale == 1 && timeScale == 1 && stiffnessScale == 1)
            return OotConstraintVelocityMode.None;
        return direct ? OotConstraintVelocityMode.Preserve : OotConstraintVelocityMode.Blend;
    }

    internal static Vector3 PreviousAfterCorrection(Vector3 before, Vector3 previous, Vector3 after,
        OotConstraintVelocityMode mode)
    {
        var oldMotion = before - previous;
        if (mode == OotConstraintVelocityMode.Preserve) return after - oldMotion;
        if (mode == OotConstraintVelocityMode.Blend)
        {
            var newMotion = after - previous;
            float oldLength = oldMotion.Length(), newLength = newMotion.Length();
            float total = oldLength + newLength;
            if (total > OotVolumeFrame.Epsilon)
            {
                float oldWeight = newLength / total;
                return after - (oldMotion * oldWeight + newMotion * (1 - oldWeight));
            }
        }
        return previous;
    }
}
