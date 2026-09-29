using System;
using UnityEngine;

namespace UV2.Live
{
    // the timeline clock: free-running at real time now; the abstraction lets an
    // audio-clocked implementation slot in once the decode lands.
    public class timeline_clock
    {
        private float _time;
        private float _rate = 1f;

        public float time => _time;
        public float rate => _rate;
        public bool paused { get; private set; }

        public void set_timescale(float scale) => _rate = scale <= 0f ? 1f : scale;

        public void pause() => paused = true;

        public void resume() => paused = false;

        public void seek(float seconds) => _time = Mathf.Max(0f, seconds);

        // the audio source the clock locks to; when set, the clock reads the
        // music position instead of accumulating delta.
        private AudioSource _master;

        public void bind_master(AudioSource src) => _master = src;

        // one frame step: delta * current rate while running, or the live audio
        // position when a master source is bound.
        public void advance(float delta)
        {
            if (_master != null && _master.clip != null && _master.isPlaying)
            {
                _time = _master.time;
                return;
            }
            if (!paused) _time += delta * _rate;
        }
    }
}
