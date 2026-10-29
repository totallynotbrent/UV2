using System;
using System.Collections.Generic;
using UnityEngine;
using Gallop;
using UV2.App;

namespace UV2.Live
{
    // bridges umaviewer v1's physics stack into uv2: hair and tail ride
    // katboi's dynamicbone conversion (CySpringDataContainer.InitializePhysics),
    // body and bust ride the cyspring controller through the game's own
    // native solver dll. the flow mirrors umacontainercharacter.LoadPhysics
    // step for step: transform cache, hip lookup, container split, collider
    // init, controller load, per-frame begin/end with dt clamped to 1/60.
    public class v1_physics_bridge : MonoBehaviour
    {
        public static v1_physics_bridge instance { get; private set; }

        class chara_physics
        {
            public Transform root;
            public GameObject[] prefabs;         // loaded container instances
            public CySpringController controller; // body/bust (native dll)
            public List<DynamicBone> dyn_bones = new List<DynamicBone>();
            public Gallop.SkirtController skirt;  // knee correction via the native link
            public float body_scale = 1f;
        }

        readonly List<chara_physics> chars = new List<chara_physics>();
        bool held = true;

        void Awake()
        {
            instance = this;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            clear();
        }

        public void clear()
        {
            foreach (var c in chars)
            {
                if (c.controller != null) Destroy(c.controller);
                foreach (var db in c.dyn_bones)
                    if (db != null) Destroy(db);
                foreach (var p in c.prefabs)
                    if (p != null) Destroy(p);
            }
            chars.Clear();
        }

        // releases the start-pose hold: the director calls this when the
        // concert opens so the first solves happen against the live pose.
        public void release_hold()
        {
            held = true; // starts held; the director flips it after the cut
        }

        public void set_held(bool h) => held = h;

        // seek/teleport re-capture: the native controller re-derives its
        // targets from the rig pose on every begin, so a plain Reset per
        // controller clears any stale internal state the same way v1's
        // re-load did after a seek.
        public void reset()
        {
            foreach (var c in chars)
            {
                if (c.controller != null) c.controller.Reset();
                foreach (var db in c.dyn_bones)
                    if (db != null) db.ResetParticlesPosition();
            }
        }

        // binds one character. load_prefab returns an instantiated
        // container prefab (the same loader the old spring path used).
        public bool attach_character(int chara_id, string body_folder,
            int cloth_index, int bust_index, bool chara_specific_body,
            int tail_model_id, Transform character_root,
            Func<string, GameObject> load_prefab)
        {
            if (character_root == null || load_prefab == null) return false;

            // load the same container bundles the game ships per part.
            var loaded = new List<GameObject>();
            void add(string bundle)
            {
                var go = load_prefab(bundle);
                if (go != null) loaded.Add(go);
            }

            string body_stem = $"{body_folder}/clothes/pfb_{body_folder.Split('/')[3]}";
            // head cloth: the dress's hair slot down to 00
            for (int c = cloth_index; c >= 0; c--)
            {
                int before = loaded.Count;
                add($"3d/chara/head/chr{chara_id}_00/clothes/pfb_chr{chara_id}_00_cloth{c:00}");
                if (loaded.Count > before) break;
            }
            add($"{body_stem}_cloth{cloth_index:00}");
            if (chara_specific_body)
                add($"{body_stem}_bust_cloth{cloth_index:00}");
            else
                add($"{body_stem}_bust{bust_index}_cloth{cloth_index:00}");
            if (tail_model_id > 0)
                add($"3d/chara/tail/tail{tail_model_id:0000}_00/clothes/pfb_tail{tail_model_id:0000}_00_cloth{cloth_index:00}");

            // parent the container instances under the bridge so despawning
            // the character never destroys the shared data mid-solve.
            foreach (var go in loaded)
                go.transform.SetParent(transform, false);

            var containers = new List<CySpringDataContainer>();
            foreach (var go in loaded)
            {
                var dc = go.GetComponent<CySpringDataContainer>()
                         ?? go.GetComponentInChildren<CySpringDataContainer>(true);
                if (dc != null) containers.Add(dc);
            }
            if (containers.Count == 0)
            {
                foreach (var go in loaded) Destroy(go);
                trace_log.write($"v1 physics: chara {chara_id} has no spring containers");
                return false;
            }

            // v1's transform cache: every bone by name, first wins.
            var bones = new Dictionary<string, Transform>();
            var all = character_root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
                if (!string.IsNullOrEmpty(t.name) && !bones.ContainsKey(t.name))
                    bones[t.name] = t;

            Transform hip = find_transform(character_root, "Hip");
            if (hip == null) hip = character_root;

            var (head, body, bust, tail) = split_containers(containers);

            // hair + tail: katboi's dynamicbone path, colliders shared from
            // every container's authored rows (v1's exact flow).
            var body_colliders = new Dictionary<string, Transform>();
            foreach (var c in containers)
            {
                if (c == null) continue;
                var more = c.InitiallizeCollider(bones);
                foreach (var kv in more)
                    if (!body_colliders.ContainsKey(kv.Key)) body_colliders[kv.Key] = kv.Value;
            }
            if (head != null) head.InitializePhysics(bones, body_colliders);
            if (tail != null) tail.InitializePhysics(bones, body_colliders);

            var entry = new chara_physics
            {
                root = character_root,
                prefabs = loaded.ToArray(),
            };
            entry.body_scale = body_scale(character_root);
            foreach (var db in character_root.GetComponentsInChildren<DynamicBone>(true))
                entry.dyn_bones.Add(db);

            // body + bust: the native solver through the controller.
            if (body != null || bust != null)
            {
                var owner = new bridge_owner(body_scale(character_root));
                entry.controller = CySpringController.AddController(
                    character_root.gameObject, hip, owner);
                if (entry.controller != null)
                {
                    entry.controller.LoadFromDataContainers(
                        bones, head, body, bust, tail,
                        null, null, null,
                        owner.body_scale, owner.body_scale, owner.body_scale);
                    entry.controller.Reset();
                }
            }

            chars.Add(entry);
            trace_log.write($"v1 physics: chara {chara_id} bound " +
                $"(dyn {entry.dyn_bones.Count}, controller {(entry.controller != null ? "on" : "off")})");
            return true;
        }

        // per-frame driver: dynamicbone solves its own lateupdate; the
        // controller steps here with dt clamped to the game's 60hz like
        // v1's AlterLateUpdateRuntime.
        void LateUpdate()
        {
            if (held) return;
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 1f / 60f);
            for (int i = 0; i < chars.Count; i++)
            {
                var c = chars[i];
                if (c.controller == null) continue;
                try
                {
                    c.controller.BeginSimulation(dt, true);
                    c.controller.EndSimulation();
                }
                catch (DllNotFoundException)
                {
                    // no native solver on this platform (linux bench): drop
                    // the native path for the session and keep the
                    // dynamicbone side solving.
                    Gallop.CySpringNative.UseNativePlugin = false;
                }
            }
        }

        // v1's skirt flow: the param asset feeds v1's SkirtController,
        // linked into the cyspring controller natively. v1's
        // LoadSkirtController + LinkSkirtControllerToCySpring pair.
        public void attach_skirt(int chara_id, Transform character_root,
            Gallop.SkirtParamDataAsset asset)
        {
            var entry = chars.Find(c => c.root == character_root);
            if (entry == null || asset == null) return;
            if (entry.skirt == null)
                entry.skirt = character_root.gameObject.AddComponent<Gallop.SkirtController>();
            entry.skirt.CharaId = chara_id;
            entry.skirt.ModelScale = entry.body_scale > 0f ? entry.body_scale : 1f;
            entry.skirt.Create(asset);
            if (entry.controller != null)
                entry.controller.CreateLinkSkirtController(entry.skirt);
            trace_log.write($"skirt: chara {chara_id} bound via v1 SkirtController");
        }

        static Transform find_transform(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == name) return all[i];
            return null;
        }

        // v1's container classifier: name first, path second, bone names
        // as the fallback. 0 head, 1 body, 2 bust, 3 tail.
        static (CySpringDataContainer, CySpringDataContainer, CySpringDataContainer, CySpringDataContainer)
        split_containers(List<CySpringDataContainer> containers)
        {
            CySpringDataContainer head = null, body = null, bust = null, tail = null;
            foreach (var c in containers)
            {
                if (c == null) continue;
                switch (guess_kind(c))
                {
                    case 0: if (head == null) head = c; break;
                    case 1: if (body == null) body = c; break;
                    case 2: if (bust == null) bust = c; break;
                    case 3: if (tail == null) tail = c; break;
                }
            }
            if (body == null && containers.Count == 1) body = containers[0];
            return (head, body, bust, tail);
        }

        static int guess_kind(CySpringDataContainer c)
        {
            string name = c.name.ToLowerInvariant();
            if (name.StartsWith("pfb_bdy") || name.Contains("_bdy")) return 1;
            if (name.Contains("bust") || name.Contains("breast") || name.Contains("mune")) return 2;
            if (name.StartsWith("pfb_tail") || name.Contains("_tail")) return 3;
            if (name.StartsWith("pfb_chr") && !name.Contains("bdy")) return 0;
            if (name.Contains("tail")) return 3;
            if (name.Contains("bust") || name.Contains("breast") || name.Contains("mune")) return 2;
            if (name.Contains("head") || name.Contains("hair") || name.Contains("ear")) return 0;
            if (name.Contains("body") || name.Contains("cloth") || name.Contains("skirt")) return 1;
            if (c.springParam != null)
            {
                foreach (var el in c.springParam)
                {
                    if (el == null) continue;
                    int k = guess_bone_kind(el._boneName);
                    if (k >= 0) return k;
                    if (el._childElements == null) continue;
                    foreach (var ch in el._childElements)
                    {
                        if (ch == null) continue;
                        k = guess_bone_kind(ch._boneName);
                        if (k >= 0) return k;
                    }
                }
            }
            return 1;
        }

        static int guess_bone_kind(string bone_name)
        {
            if (string.IsNullOrEmpty(bone_name)) return -1;
            string n = bone_name.ToLowerInvariant();
            if (n.Contains("tail")) return 3;
            if (n.Contains("bust") || n.Contains("breast") || n.Contains("mune")) return 2;
            if (n.Contains("head") || n.Contains("hair") || n.Contains("ear")) return 0;
            if (n.Contains("skirt") || n.Contains("cloth") || n.Contains("body")) return 1;
            return -1;
        }

        // the rig's body scale: the Position bone's local scale y (the
        // game's bodyScale lives on that node).
        static float body_scale(Transform character)
        {
            var pos = find_transform(character, "Position");
            float s = pos != null ? pos.localScale.y : 1f;
            return s > 1e-5f ? s : 1f;
        }

        // v1's owner: every scale getter returns the body scale.
        class bridge_owner : CySpringOwner
        {
            public readonly float body_scale;
            public bridge_owner(float scale) : base(null) { body_scale = scale; }
            public override bool IsCharaModel() => true;
            public override int GetCharaID() => 0;
            public override int GetDressID() => 0;
            public override int GetHeadID() => 0;
            public override float GetCySpringCorrectScale(bool isHead) => body_scale;
            public override float GetBodyScale() => body_scale;
            public override float GetTotalScale() => body_scale;
        }
    }
}
