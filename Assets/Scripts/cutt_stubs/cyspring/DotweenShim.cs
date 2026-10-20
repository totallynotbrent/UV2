using System;
using System.Collections.Generic;
using UnityEngine;

namespace DG.Tweening
{
    // minimal dotween shim for the vendored cyspring stack: only
    // DOTween.To(float setter, from, to, duration) with OnComplete is
    // used (CySpringDynamicsRatioTailSetter's tail ratio blend). a hidden
    // driver object advances the value in LateUpdate; no tween features.
    public static class DOTween
    {
        public class tween
        {
            public Action<float> setter;
            public float from, to, duration, elapsed;
            public Action on_complete;
        }

        static readonly List<tween> active = new List<tween>();
        static tween_driver driver;

        class tween_driver : MonoBehaviour
        {
            void LateUpdate()
            {
                float dt = Time.deltaTime;
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    var t = active[i];
                    t.elapsed += dt;
                    float k = t.duration <= 0f ? 1f : Mathf.Clamp01(t.elapsed / t.duration);
                    t.setter(Mathf.LerpUnclamped(t.from, t.to, k));
                    if (k >= 1f)
                    {
                        active.RemoveAt(i);
                        t.on_complete?.Invoke();
                    }
                }
            }
        }

        static void ensure_driver()
        {
            if (driver != null) return;
            var go = new GameObject("dotween_shim");
            go.hideFlags = HideFlags.HideAndDontSave;
            driver = go.AddComponent<tween_driver>();
        }

        // the To(...) call in the vendored code chains .OnComplete(...)
        // before any frame runs; the tween registers when OnComplete
        // lands, so To only builds it.
        public static tween To(Action<float> setter, float from, float to, float duration)
        {
            return new tween { setter = setter, from = from, to = to, duration = duration };
        }

        public static tween OnComplete(this tween t, Action cb)
        {
            t.on_complete = cb;
            ensure_driver();
            active.Add(t);
            return t;
        }
    }
}
