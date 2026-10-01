using UnityEditor;
using UnityEngine;
using UV2.App;

namespace UV2Build
{
    // creates the two empty scenes and wires the bootstrappers into them.
    public static class scene_baker
    {
        [MenuItem("UV2/Bake Scenes")]
        public static void bake()
        {
            make_scene("Assets/Scenes/Concert.unity", "UV2.App.scene_bootstrap", "build_concert_scene");
            Debug.Log("[scene_baker] scenes baked");
        }

        private static void make_scene(string path, string type_name, string method_name)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var go = new GameObject("boot");
            var boot = go.AddComponent<scene_boot>();
            boot.type_name = type_name;
            boot.method_name = method_name;
            if (!System.IO.Directory.Exists("Assets/Scenes")) System.IO.Directory.CreateDirectory("Assets/Scenes");
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
        }
    }
}
