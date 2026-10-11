using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // scene driver for the spring solver: one component per live stage,
    // registered by stage_loader. runs in LateUpdate AFTER the pose (motion
    // sampled in Update) — the decoded order the game uses.
    public class physics_director : MonoBehaviour
    {
        public static physics_director instance { get; private set; }

        // kill switch: UV2_PHYSICS=0 disables the whole pass (benching).
        public static bool enabled_by_env =>
            System.Environment.GetEnvironmentVariable("UV2_PHYSICS") != "0";

        readonly List<spring_sim> sims = new();
        float last_time;

        void Awake()
        {
            instance = this;
            enabled = enabled_by_env;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // registers a character after its containers are merged and bound.
        public spring_sim register(Transform character)
        {
            var sim = new spring_sim(character);
            sims.Add(sim);
            return sim;
        }

        public void clear() => sims.Clear();

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float song_dt = dt; // the song clock runs on wall time; pause = 0 handled below
            foreach (var sim in sims)
            {
                if (sim == null) continue;
                sim.begin(song_dt);
                sim.update(song_dt, collision_switch: true);
                sim.end();
            }
            last_time = Time.time;
        }
    }
}
