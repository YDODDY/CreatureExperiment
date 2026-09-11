using UnityEngine;

namespace CreatureExperiment.Creature.Perception
{
    /// <summary>
    /// What kind of thing made a sound stimulus. Deliberately small - only what Hearing 0.1 actually
    /// produces (Player footsteps + jump). Voice / ObjectImpact / Door / CreatureSound are future
    /// extensions of this SAME enum - not built yet, but nothing about the shape below prevents them.
    /// </summary>
    public enum SoundStimulusType
    {
        Footstep,
        Jump,
    }

    /// <summary>
    /// One logical sound event, as data - NOT an AudioClip, NOT a physics/acoustics simulation. Any
    /// emitter (today: <see cref="PlayerMovementSoundEmitter"/>; nothing here assumes it is the only
    /// one) hands one of these to any listener (today: <see cref="CreatureHearing"/>) purely as a
    /// value. Deliberately tiny: a world position, a 0..1 loudness, a type tag, an optional source
    /// reference, and when it happened - no loudness falloff model, no occlusion, no propagation
    /// delay (see <see cref="CreatureHearing"/> for the simple distance*intensity model 0.1 uses).
    ///
    /// <see cref="Source"/> exists for debugging / future use only. A listener reacting to this
    /// stimulus must NOT read it to shortcut "this came from the Player, therefore I know it's the
    /// Player" - Hearing 0.1's whole point is that a heard stimulus is evidence of a sound at a
    /// position, not an identification. See <see cref="CreatureHearing"/>'s class summary.
    /// </summary>
    public readonly struct SoundStimulus
    {
        public readonly Vector3 Position;

        /// <summary>0..1. Louder Player movement tiers (Crouch &lt; Normal &lt; Dash &lt; Jump) produce a higher value - see <see cref="PlayerMovementSoundEmitter"/>.</summary>
        public readonly float Intensity;

        public readonly SoundStimulusType Type;

        /// <summary>Whatever object produced this stimulus (e.g. the Player root). Debug/future-use only - see class summary; a listener's REACTION must never read this to identify the source.</summary>
        public readonly Object Source;

        public readonly float Time;

        public SoundStimulus(Vector3 position, float intensity, SoundStimulusType type, Object source, float time)
        {
            Position = position;
            Intensity = Mathf.Clamp01(intensity);
            Type = type;
            Source = source;
            Time = time;
        }
    }
}
