// a lean mirror driver for live stages: renders the scene into a small
// render texture from the reflected base camera, then feeds it to any
// renderer whose material exposes _ReflectionTex (the game bakes the mirror
// floor into the stage prefab and relies on this hook to make it live).
// The fork's own MirrorReflection.cs uses URP hooks and SectionProfiler;
// UV2 runs the built-in pipeline, so this port keeps the math but drops the
// URP request plumbing and profiler markers.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public class mirror_reflection : MonoBehaviour
    {
        public enum Direction { Up, Forward, Right }

        private const string CAMERA_NAME = "UV2 Mirror Camera";
        private const string TEXTURE_NAME = "UV2 MirrorReflection";
        private const float FOV_MIN = 1f;
        private const float FOV_MAX = 179f;
        private const float TEXTURE_SIZE_RATE_MIN = 0.01f;
        private const int MIRROR_CAMERA_DEPTH_OFFSET = -100;

        private static readonly Vector4 DEFAULT_DIST_POWER = Vector4.zero;
        private static readonly Vector4 DEFAULT_DIST_MAP_TILE_OFFSET = new Vector4(1f, 1f, 0f, 0f);

        [SerializeField] private LayerMask _render_layers = ~0;
        [SerializeField] private int _mirror_texture_size = 384;
        [SerializeField] private float _mirror_clip_plane_offset = 0.07f;
        [SerializeField] private float _mirror_reflection_rate = 0f;
        [SerializeField] private Vector4 _mirror_distortion_tile_offset = DEFAULT_DIST_MAP_TILE_OFFSET;
        [SerializeField] private Vector4 _mirror_distortion_power = DEFAULT_DIST_POWER;
        [SerializeField] private Direction _direction = Direction.Up;
        [SerializeField] private Color _mirror_reflection_color = Color.white;
        [SerializeField] private Color _background_color = Color.black;
        [SerializeField] private bool _use_background_color;
        [SerializeField] private bool _use_mirror_texture_scale;
        [SerializeField] private float _mirror_texture_scale_for_base_camera = 1f;
        [SerializeField] private bool _use_base_camera_texture_size;
        [SerializeField] private bool _is_enabled_mirror_camera = true;
        [SerializeField] private bool _is_enabled = true;
        [SerializeField] private bool _log_debug;

        private Transform _transform;
        private Renderer _mirror_renderer;
        private Camera _base_camera;
        private Transform _base_camera_transform;
        private Camera _mirror_camera;
        private Transform _mirror_camera_transform;
        private RenderTexture _mirror_texture;
        private Material[] _materials;
        private LayerMask _final_render_layers = ~0;
        private int _object_layer;
        private bool _is_initialized;

        private Vector4 _init_mirror_distortion_power = DEFAULT_DIST_POWER;
        private Vector4 _init_mirror_distortion_tile_offset = DEFAULT_DIST_MAP_TILE_OFFSET;
        private int _old_mirror_texture_size = -1;
        private float _old_mirror_texture_scale = -1f;
        private bool _mirror_skip_frame;
        private bool _is_rendering_now;

        private static readonly int PID_Color = Shader.PropertyToID("_Color");
        private static readonly int PID_ReflectionRate = Shader.PropertyToID("_ReflectionRate");
        private static readonly int PID_ReflectionTex = Shader.PropertyToID("_ReflectionTex");
        private static readonly int PID_DistMapST = Shader.PropertyToID("_DistMap_ST");
        private static readonly int PID_DistPower = Shader.PropertyToID("_DistPower");

        // one frame budget across the stage so multiple mirrors don't stack a
        // full scene render in the same frame.
        private static int _frame_budget_frame = -1;
        private static int _budget_cursor;
        private static readonly List<mirror_reflection> _budget_queue = new List<mirror_reflection>();
        private int _budget_ticket = -1;

        // bound externally once the live driver knows the main camera.
        public void BindCamera(Camera cam)
        {
            _base_camera = cam;
            _base_camera_transform = cam != null ? cam.transform : null;
        }

        public void SetMirrorCameraEnabled(bool enabled_on)
        {
            _is_enabled_mirror_camera = enabled_on;
        }

        private void Awake()
        {
            _transform = transform;
            _mirror_renderer = GetComponent<Renderer>();
            _object_layer = gameObject.layer;
            _final_render_layers = _render_layers;
        }

        private void OnEnable()
        {
            _is_enabled = true;
        }

        private void OnDisable()
        {
            _is_enabled = false;
            ClearMaterialBinding();
        }

        private void OnDestroy()
        {
            ReleaseMirrorTexture();
            if (_mirror_camera != null)
            {
                if (Application.isPlaying) Destroy(_mirror_camera.gameObject);
                else DestroyImmediate(_mirror_camera.gameObject);
            }
        }

        private void LateUpdate()
        {
            if (!_is_enabled || !_is_enabled_mirror_camera) return;
            if (_mirror_renderer == null || !_mirror_renderer.isVisible) return;
            if (_base_camera == null || _mirror_camera == null) return;
            if (!IsBaseCameraInFrontOfMirror()) return;
            if (!_is_initialized) return;
            if (_is_rendering_now) return;

            // the fork skips every other frame; keep the same cadence so the
            // mirror cost stays bounded even on slower gpus.
            _mirror_skip_frame = !_mirror_skip_frame;
            if (_mirror_skip_frame) return;

            if (!AcquireFrameBudget()) return;

            ForceRenderOnce();
        }

        private bool AcquireFrameBudget()
        {
            if (Time.frameCount != _frame_budget_frame)
            {
                _frame_budget_frame = Time.frameCount;
                _budget_cursor = 0;
            }
            if (_budget_ticket < 0)
            {
                _budget_ticket = _budget_queue.Count;
                _budget_queue.Add(this);
            }
            if (_budget_queue.Count <= 0) return false;
            int slot = _budget_cursor % _budget_queue.Count;
            if (slot == _budget_ticket)
            {
                _budget_cursor++;
                return true;
            }
            return false;
        }

        // configures the mirror once both a base camera and a renderer exist.
        public void ConfigureFor(Camera base_camera)
        {
            BindCamera(base_camera);
            if (_mirror_renderer == null)
            {
                _mirror_renderer = GetComponent<Renderer>();
            }
            if (_mirror_renderer == null)
            {
                Debug.Log($"uv2 mirror '{name}' skipped: no renderer");
                return;
            }
            InitMaterials();
            CreateMirrorCamera();
            UpdateRenderTexture();
            UpdateMirrorTexture();
            UpdateMirrorParams();
            SetReflectionRate(1f);
            _is_initialized = true;
            Debug.Log($"uv2 mirror '{name}' initialized");
        }

        private void InitMaterials()
        {
            if (_mirror_renderer == null) return;
            _materials = _mirror_renderer.sharedMaterials;
            if (_materials == null || _materials.Length == 0) return;
            bool found_dist_power = false;
            bool found_dist_map_st = false;
            _init_mirror_distortion_power = DEFAULT_DIST_POWER;
            _init_mirror_distortion_tile_offset = DEFAULT_DIST_MAP_TILE_OFFSET;
            foreach (var mat in _materials)
            {
                if (mat == null) continue;
                if (!found_dist_power && mat.HasProperty(PID_DistPower))
                {
                    _init_mirror_distortion_power = mat.GetVector(PID_DistPower);
                    found_dist_power = true;
                }
                if (!found_dist_map_st && mat.HasProperty(PID_DistMapST))
                {
                    _init_mirror_distortion_tile_offset = mat.GetVector(PID_DistMapST);
                    found_dist_map_st = true;
                }
                if (found_dist_power && found_dist_map_st) break;
            }
            _mirror_distortion_power = _init_mirror_distortion_power;
            _mirror_distortion_tile_offset = _init_mirror_distortion_tile_offset;
        }

        private void CreateMirrorCamera()
        {
            if (_mirror_camera != null)
            {
                _mirror_camera_transform = _mirror_camera.transform;
                return;
            }
            var go = new GameObject(CAMERA_NAME, typeof(Camera));
            go.hideFlags = HideFlags.DontSave;
            _mirror_camera = go.GetComponent<Camera>();
            _mirror_camera_transform = go.transform;
            _mirror_camera.enabled = false;
            _mirror_camera.gameObject.tag = "Untagged";
            _mirror_camera_transform.SetParent(_transform, true);
            _mirror_camera_transform.position = _transform.position;
            _mirror_camera_transform.rotation = _transform.rotation;
            _mirror_camera_transform.localScale = Vector3.one;
            _mirror_camera.allowHDR = false;
            _mirror_camera.allowMSAA = false;
            _mirror_camera.depth = (_base_camera ? _base_camera.depth : 0f) + MIRROR_CAMERA_DEPTH_OFFSET;
            _mirror_camera.clearFlags = CameraClearFlags.Color;
            // drop the receiving mesh's own layer from the mirror pass so the
            // reflective floor doesn't see itself and recursively re-render.
            _final_render_layers = _render_layers & ~(1 << _object_layer);
            _mirror_camera.cullingMask = _final_render_layers;
            if (_log_debug) Debug.Log($"uv2 mirror '{name}': camera created");
        }

        private Vector2Int GetRenderTextureSize()
        {
            int ref_width = 1;
            int ref_height = 1;
            if (_base_camera != null && _base_camera.targetTexture != null)
            {
                ref_width = Mathf.Max(1, _base_camera.targetTexture.width);
                ref_height = Mathf.Max(1, _base_camera.targetTexture.height);
            }
            else if (_base_camera != null)
            {
                ref_width = Mathf.Max(1, _base_camera.pixelWidth);
                ref_height = Mathf.Max(1, _base_camera.pixelHeight);
            }
            else
            {
                ref_width = Mathf.Max(1, Screen.width);
                ref_height = Mathf.Max(1, Screen.height);
            }
            if (_use_base_camera_texture_size) return new Vector2Int(ref_width, ref_height);
            if (_use_mirror_texture_scale)
            {
                float scale = Mathf.Max(TEXTURE_SIZE_RATE_MIN, _mirror_texture_scale_for_base_camera);
                return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(ref_width * scale)),
                                      Mathf.Max(1, Mathf.RoundToInt(ref_height * scale)));
            }
            int long_side = Mathf.Max(1, _mirror_texture_size);
            if (ref_width >= ref_height)
            {
                return new Vector2Int(long_side, Mathf.Max(1, Mathf.RoundToInt(long_side * (float)ref_height / ref_width)));
            }
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(long_side * (float)ref_width / ref_height)), long_side);
        }

        private void UpdateRenderTexture()
        {
            bool need_rebuild = _mirror_texture == null
                || _old_mirror_texture_size != _mirror_texture_size
                || !Mathf.Approximately(_old_mirror_texture_scale, _mirror_texture_scale_for_base_camera);
            if (!need_rebuild) return;
            ReleaseMirrorTexture();
            var size = GetRenderTextureSize();
            _old_mirror_texture_scale = _mirror_texture_scale_for_base_camera;
            _mirror_texture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32)
            {
                name = TEXTURE_NAME,
                hideFlags = HideFlags.DontSave,
                useMipMap = false,
                autoGenerateMips = false
            };
            _mirror_texture.Create();
            if (_mirror_camera != null) _mirror_camera.targetTexture = _mirror_texture;
            UpdateMirrorTexture();
            _old_mirror_texture_size = _mirror_texture_size;
            if (_log_debug) Debug.Log($"uv2 mirror '{name}': texture {size.x}x{size.y}");
        }

        private void UpdateMirrorParams()
        {
            if (_base_camera == null || !_is_enabled_mirror_camera || _mirror_camera == null) return;
            _mirror_camera.clearFlags = CameraClearFlags.Color;
            _mirror_camera.backgroundColor = _use_background_color ? _background_color : _base_camera.backgroundColor;
            float fov = Mathf.Clamp(_base_camera.fieldOfView, FOV_MIN, FOV_MAX);
            _mirror_camera.fieldOfView = fov;
            _mirror_camera.nearClipPlane = _base_camera.nearClipPlane;
            _mirror_camera.farClipPlane = _base_camera.farClipPlane;
            _mirror_camera.orthographic = _base_camera.orthographic;
            _mirror_camera.orthographicSize = _base_camera.orthographicSize;
            _mirror_camera.targetTexture = _mirror_texture;
            _mirror_camera.cullingMask = _final_render_layers;
            if (_mirror_texture != null && _mirror_texture.height > 0)
                _mirror_camera.aspect = (float)_mirror_texture.width / _mirror_texture.height;
            else
                _mirror_camera.aspect = _base_camera.aspect;
            ApplyMaterialParams();
        }

        private void SetReflectionRate(float rate)
        {
            _mirror_reflection_rate = rate;
            ApplyMaterialParams();
        }

        private void ApplyMaterialParams()
        {
            if (_materials == null) return;
            foreach (var mat in _materials)
            {
                if (mat == null) continue;
                if (mat.HasProperty(PID_ReflectionRate)) mat.SetFloat(PID_ReflectionRate, _mirror_reflection_rate);
                if (mat.HasProperty(PID_Color)) mat.SetColor(PID_Color, _mirror_reflection_color);
                if (mat.HasProperty(PID_DistMapST)) mat.SetVector(PID_DistMapST, _mirror_distortion_tile_offset);
                if (mat.HasProperty(PID_DistPower)) mat.SetVector(PID_DistPower, _mirror_distortion_power);
            }
        }

        private void SetMirrorTextureOnMaterials(Texture tex)
        {
            if (_materials == null) return;
            foreach (var mat in _materials)
            {
                if (mat == null) continue;
                if (mat.HasProperty(PID_ReflectionTex)) mat.SetTexture(PID_ReflectionTex, tex);
            }
        }

        private void UpdateMirrorTexture()
        {
            SetMirrorTextureOnMaterials(_mirror_texture);
        }

        private void ClearMaterialBinding()
        {
            _mirror_reflection_rate = 0f;
            ApplyMaterialParams();
            SetMirrorTextureOnMaterials(null);
            if (_mirror_camera != null) _mirror_camera.targetTexture = null;
        }

        private void ReleaseMirrorTexture()
        {
            SetMirrorTextureOnMaterials(null);
            if (_mirror_texture == null) return;
            if (_mirror_camera != null && _mirror_camera.targetTexture == _mirror_texture)
                _mirror_camera.targetTexture = null;
            _mirror_texture.Release();
            if (Application.isPlaying) Destroy(_mirror_texture);
            else DestroyImmediate(_mirror_texture);
            _mirror_texture = null;
        }

        private Vector3 GetMirrorNormal()
        {
            switch (_direction)
            {
                case Direction.Forward: return _transform.forward;
                case Direction.Right: return _transform.right;
                case Direction.Up:
                default: return _transform.up;
            }
        }

        private bool IsPointAbovePlane(Vector3 point, Vector3 plane_pos, Vector3 plane_normal)
        {
            return Vector3.Dot(plane_normal, (point - plane_pos).normalized) > 0f;
        }

        private bool IsBaseCameraInFrontOfMirror()
        {
            if (_base_camera_transform == null || _transform == null) return false;
            return IsPointAbovePlane(_base_camera_transform.position, _transform.position, GetMirrorNormal());
        }

        private void UpdateProjectionMatrix()
        {
            if (_base_camera == null || _base_camera_transform == null
                || _mirror_camera == null || _mirror_camera_transform == null)
            {
                return;
            }
            Vector3 normal = GetMirrorNormal();
            Vector3 plane_pos = _transform.position;
            float d = -Vector3.Dot(normal, plane_pos) - _mirror_clip_plane_offset;
            Vector4 reflection_plane = new Vector4(normal.x, normal.y, normal.z, d);
            Matrix4x4 reflection_mat = Matrix4x4.zero;
            CalculateReflectionMatrix(ref reflection_mat, reflection_plane.x, reflection_plane.y, reflection_plane.z, reflection_plane.w);
            Vector3 reflected_pos = reflection_mat.MultiplyPoint(_base_camera_transform.position);
            _mirror_camera.worldToCameraMatrix = _base_camera.worldToCameraMatrix * reflection_mat;
            _mirror_camera.cullingMatrix = _base_camera.cullingMatrix * reflection_mat;
            Vector3 offset_plane_pos = plane_pos + normal * _mirror_clip_plane_offset;
            Matrix4x4 mirror_world_to_camera = _mirror_camera.worldToCameraMatrix;
            Vector3 cam_space_point = mirror_world_to_camera.MultiplyPoint(offset_plane_pos);
            Vector3 cam_space_normal = mirror_world_to_camera.MultiplyVector(normal).normalized;
            Vector4 clip_plane = new Vector4(cam_space_normal.x, cam_space_normal.y, cam_space_normal.z,
                -Vector3.Dot(cam_space_point, cam_space_normal));
            float old_fov = _base_camera.fieldOfView;
            try
            {
                _base_camera.fieldOfView = _mirror_camera.fieldOfView;
                _base_camera.aspect = _mirror_camera.aspect;
                Matrix4x4 projection = _base_camera.projectionMatrix;
                CalculateObliqueMatrix(ref projection, clip_plane);
                _mirror_camera.projectionMatrix = projection;
            }
            finally
            {
                _base_camera.fieldOfView = old_fov;
                _base_camera.ResetAspect();
            }
            _mirror_camera_transform.position = reflected_pos;
            Vector3 base_euler = _base_camera_transform.eulerAngles;
            _mirror_camera_transform.eulerAngles = new Vector3(0f, base_euler.y, base_euler.z);
        }

        private static float Sgn(float x)
        {
            if (x > 0f) return 1f;
            if (x < 0f) return -1f;
            return 0f;
        }

        private static void CalculateObliqueMatrix(ref Matrix4x4 projection, Vector4 clip_plane)
        {
            Vector4 q = projection.inverse * new Vector4(Sgn(clip_plane.x), Sgn(clip_plane.y), 1f, 1f);
            Vector4 c = clip_plane * (2f / Vector4.Dot(clip_plane, q));
            projection[2] = c.x - projection[3];
            projection[6] = c.y - projection[7];
            projection[10] = c.z - projection[11];
            projection[14] = c.w - projection[15];
        }

        private static void CalculateReflectionMatrix(ref Matrix4x4 reflection_mat, float x, float y, float z, float w)
        {
            reflection_mat.m00 = 1f - 2f * x * x;
            reflection_mat.m01 = -2f * x * y;
            reflection_mat.m02 = -2f * x * z;
            reflection_mat.m03 = -2f * x * w;
            reflection_mat.m10 = -2f * y * x;
            reflection_mat.m11 = 1f - 2f * y * y;
            reflection_mat.m12 = -2f * y * z;
            reflection_mat.m13 = -2f * y * w;
            reflection_mat.m20 = -2f * z * x;
            reflection_mat.m21 = -2f * z * y;
            reflection_mat.m22 = 1f - 2f * z * z;
            reflection_mat.m23 = -2f * z * w;
            reflection_mat.m30 = 0f;
            reflection_mat.m31 = 0f;
            reflection_mat.m32 = 0f;
            reflection_mat.m33 = 1f;
        }

        public void ForceRenderOnce()
        {
            if (_mirror_reflection_rate <= 0.001f) return;
            if (_mirror_renderer == null || !_mirror_renderer.isVisible) return;
            if (!_is_initialized || _base_camera == null || _mirror_camera == null) return;
            if (_is_rendering_now) return;
            if (!IsBaseCameraInFrontOfMirror()) return;

            _is_rendering_now = true;
            try
            {
                UpdateRenderTexture();
                UpdateMirrorParams();
                UpdateProjectionMatrix();
                bool old_invert = GL.invertCulling;
                try
                {
                    GL.invertCulling = !old_invert;
                    _mirror_camera.Render();
                }
                finally
                {
                    GL.invertCulling = old_invert;
                    if (_mirror_camera != null)
                    {
                        _mirror_camera.ResetWorldToCameraMatrix();
                        _mirror_camera.ResetProjectionMatrix();
                        _mirror_camera.ResetCullingMatrix();
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                _is_rendering_now = false;
            }
        }
    }
}
