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

        public void set_timescale(float scale)
        {
            // zero is a no-op, never a rate; negatives play in reverse (the
            // control window's fine-speed control goes down to -3x, and a
            // negative master pitch plays the clip backward natively).
            if (scale == 0f) return;
            _rate = scale;
            // the speed modifier rides the master's pitch too: without this
            // the music position wins in advance() and no transport speed
            // applies at all once the song is live.
            if (_master != null) _master.pitch = _rate;
        }

        public void pause()
        {
            paused = true;
            if (_master != null && _master.isPlaying) _master.Pause();
        }

        public void resume()
        {
            paused = false;
            if (_master != null && !_master.isPlaying && _master.clip != null) _master.UnPause();
        }

        public void seek(float seconds)
        {
            _time = Mathf.Max(0f, seconds);
            // the bound master follows the scrub so the audio stays in sync.
            if (_master != null && _master.clip != null)
            {
                _time = Mathf.Min(_time, _master.clip.length);
                _master.time = _time;
            }
        }

        // the audio source the clock locks to; when set, the clock reads the
        // music position instead of accumulating delta.
        private AudioSource _master;

        public void bind_master(AudioSource src) => _master = src;

        // tracks whether the audio clock has ever reported sane progress;
        // device-less containers report the clip end from the first read.
        private bool _audio_trusted;

        // true once the master audio source is playing: the song clock is
        // authoritative only from this point, and consumers that must not
        // advance on the pre-music free-run time gate on it.
        public bool music_live => _master != null && _master.isPlaying;

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
