using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // plays the particle systems the game's stage prefabs ship with.
    //
    // the stage prefabs come out of the game's own bundles, so the smoke
    // plumes, the sparks and the dust that hang over the stage arrive as
    // authored ParticleSystem components: emitter rates, lifetime, size,
    // colour-over-life ramps, textures and curves are all in the prefab.
    // nothing drives them, because the game's stage controller is the
    // thing that starts them, so without this module the concert renders
    // with an empty air around the performers while the official is full
    // of drifting smoke and glow.
    //
    // visibility is not decided here: the object list's renderEnable flag
    // owns whether a prop shows at all and toggles its renderer, and the
    // bg colour groups own the tint. this module only starts the
    // simulation and keeps it stepping with the song clock.
    public class particle_playback : MonoBehaviour
    {
        private readonly List<ParticleSystem> systems = new();
        private bool started;
        private int logged;

        public void bind(List<Transform> roots)
        {
            systems.Clear();
            started = false;
            if (roots == null) return;
            for (int i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (root == null) continue;
                var found = root.GetComponentsInChildren<ParticleSystem>(true);
                for (int j = 0; j < found.Length; j++)
                {
                    if (found[j] == null) continue;
                    systems.Add(found[j]);
                }
            }
            trace_log.write($"particles: {systems.Count} systems in the stage");

            for (int i = 0; i < systems.Count; i++)
            {
                var ps = systems[i];
                // the prefab's own seed stays as authored: the game's
                // emitters randomise, so the official clip's smoke never
                // repeats either.
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
                if (logged < 8)
                {
                    var main = ps.main;
                    trace_log.write($"particles: '{ps.gameObject.name}' max {main.maxParticles} " +
                                     $"lifetime {main.startLifetime.constant:0.00} " +
                                     $"rate {ps.emission.rateOverTime.constant:0.00} " +
                                     $"playing {ps.isPlaying}");
                    logged++;
                }
            }
            started = systems.Count > 0;
        }

        // the song can pause and scrub, so the simulation follows the clock
        // instead of wall time: when the song time moves backwards the
        // emitters restart from their first frame.
        private float last_time = -1f;

        public void update(float song_time)
        {
            if (!started) return;
            if (last_time >= 0f && song_time < last_time - 0.05f)
            {
                for (int i = 0; i < systems.Count; i++)
                {
                    var ps = systems[i];
                    if (ps == null) continue;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }
            }
            last_time = song_time;
        }
    }
}
