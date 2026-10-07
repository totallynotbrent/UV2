using UnityEngine;
using UV2.App;

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

        // tracks whether the audio clock has ever reported sane progress;
        // device-less containers report the clip end from the first read.
        private bool _audio_trusted;

        // one frame step: the audio position once it has proven it advances
        // plausibly, else real-time accumulation. a paused clock holds the
        // seeked frame for the -uv2frame capture mode.
        public void advance(float delta)
        {
            if (paused) return;
            if (_master != null && _master.clip != null && _master.isPlaying)
            {
                float at = _master.time;
                if (!_audio_trusted)
                {
                    // trust only a read that starts near zero and moves forward
                    // by roughly the elapsed frames.
                    if (at < 1f)
                    {
                        _audio_trusted = true;
                        trace_log.write("clock: audio clock trusted");
                    }
                    else
                    {
                        if (!paused) _time += delta * _rate;
                        return;
                    }
                }
                _time = at;
                return;
            }
            if (!paused) _time += delta * _rate;
        }
    }
}
