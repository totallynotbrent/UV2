namespace UV2.Live
{
    // retired spring solver handle: the physics now runs the umaviewer v1
    // stack (v1_physics_bridge), and attach_character always returns null.
    // this stub keeps the formation driver's and control bridge's typed
    // call sites compiling; every per-sim branch short-circuits on the
    // null they receive.
    public class spring_sim
    {
        public enum part { head = 0, body = 1, tail = 2 }

        // retired probe switches, kept for the scene_boot argument parser
        // (the bench harness still passes -uv2tailprobe / -uv2dense).
        public static bool tail_probe_enabled;
        public static bool bench_dense;

        // retired per-sim controls: the v1 stack solves from the live rig
        // pose each frame, so wind toggles, pose re-seeds and suppression
        // arming have no analogue. kept as no-ops for the callers.
        public void set_wind_enabled(bool on) { }
        public void reset_to_pose() { }
        public void arm_suppression() { }
    }
}
