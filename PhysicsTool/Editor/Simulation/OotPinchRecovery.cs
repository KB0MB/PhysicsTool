using System.Numerics;

namespace HKCLTool;

// 0x7101c45600 / 0x7101c45340. State belongs to each authored set entry,
// not to the collider, and its clock advances only at the end of a substep.
internal sealed class OotPinchRecovery(float recoveryDuration, float releaseDuration, float releaseDistance)
{
    internal enum State : byte { Free, Held, Recovering, Releasing }
    private const float Epsilon = 1.1920928955078125e-7f;
    internal State Phase { get; private set; }
    internal float Elapsed { get; private set; }
    internal float InitialDistance { get; private set; } = BitConverter.Int32BitsToSingle(0x7f7fffee);

    internal void Reset()
    {
        Phase = State.Free;
        Elapsed = 0;
        InitialDistance = BitConverter.Int32BitsToSingle(0x7f7fffee);
    }

    internal Vector3 Apply(bool pinched, Vector3 position, Vector3 reference)
    {
        if (Phase is State.Held or State.Recovering)
        {
            if (!pinched) { Phase = State.Releasing; Elapsed = 0; }
        }
        else if (pinched)
        {
            Phase = State.Recovering;
            Elapsed = 0;
            InitialDistance = BitConverter.Int32BitsToSingle(0x7f7fffee);
        }
        if (Phase == State.Free) return position;
        if (Phase == State.Held) return reference;
        var offset = position - reference;
        float distance = offset.Length() + Epsilon;
        if (Phase == State.Recovering)
        {
            if (Elapsed <= 0) InitialDistance = distance;
            else if (Elapsed >= recoveryDuration) return reference;
            else
            {
                float limit = (1 - Elapsed / recoveryDuration) * InitialDistance;
                if (distance > limit) return position + offset * ((limit - distance) / distance);
            }
            return position;
        }
        if (Elapsed <= 0) return reference;
        if (Elapsed >= releaseDuration) return position;
        // Preserve the native multiply/rsqrt sequence, including zero/subnormal
        // handling, rather than interpreting the field name as a radial clamp.
        float limited = Math.Min(distance, releaseDistance);
        float squared = limited * limited;
        float inverse = (BitConverter.SingleToInt32Bits(squared) & 0x7f800000) == 0 ? 0 : 1 / MathF.Sqrt(squared);
        return reference + offset * (limited * (Elapsed / releaseDuration) * inverse);
    }

    internal void EndSubstep(float dt)
    {
        if (Phase is not (State.Recovering or State.Releasing)) return;
        Elapsed += dt;
        if (Phase == State.Recovering && Elapsed > recoveryDuration) Phase = State.Held;
        else if (Phase == State.Releasing && Elapsed > releaseDuration) Phase = State.Free;
    }
}

// Capsule scalar tail 0x7101b6bb3c: overlap is recorded before priority replacement.
internal struct OotPinchContactState
{
    internal int Priority;
    internal bool Pinched;
    internal bool Valid;
    internal void Reset() { Priority = -1; Pinched = false; Valid = false; }
    internal bool Add(int priority, float penetration)
    {
        Pinched = Priority >= 0;
        if (priority > Priority)
        {
            Priority = priority;
            Valid = penetration > 0;
            return Valid;
        }
        if (priority == Priority) Valid = false;
        return false;
    }
}
