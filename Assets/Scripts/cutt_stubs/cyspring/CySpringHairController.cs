using UnityEngine;
using System.Reflection;

namespace Gallop
{
    /// <summary>
    /// Direct control for hair physics - override stiffness more aggressively.
    /// </summary>
    public class CySpringHairController : MonoBehaviour
    {
        [Header("Hair Physics Override")]
        [Range(0.01f, 1.0f)]
        [SerializeField] private float _hairStiffnessOverride = 0.1f;  // Very low = very loose
        
        [Range(0.5f, 10.0f)]
        [SerializeField] private float _hairGravityMultiplier = 5.0f;  // HIGH gravity to prevent floating up
        
        [Range(0.0f, 1.0f)]
        [SerializeField] private float _hairDragOverride = 0.5f;  // Less drag = more movement
        
        [Header("Collision")]
        [Range(0.5f, 3.0f)]
        [SerializeField] private float _hairCollisionScale = 1.0f;  // Collision size for hair
        
        [Header("Settings")]
        [SerializeField] private bool _autoApply = true;
        [SerializeField] private float _applyDelay = 1.0f;
        
        private CySpringController _cySpringController;
        private float _applyTimer;
        private bool _hasApplied;
        
        private void Start()
        {
            _cySpringController = GetComponent<CySpringController>();
            if (_cySpringController == null)
            {
                Debug.LogWarning("[CySpringHairController] No CySpringController found");
                enabled = false;
                return;
            }
            
            _applyTimer = _applyDelay;
            _hasApplied = false;
        }
        
        private void Update()
        {
            if (_hasApplied || !_autoApply || _cySpringController == null)
                return;
            
            _applyTimer -= Time.deltaTime;
            if (_applyTimer <= 0)
            {
                ApplyHairOverrides();
                _hasApplied = true;
            }
        }
        
        /// <summary>
        /// Apply hair-specific overrides
        /// </summary>
        public void ApplyHairOverrides()
        {
            if (_cySpringController == null)
                return;
            
            Debug.Log("[CySpringHairController] Applying hair overrides...");
            
            // Override Head (hair) spring rate - this is the key!
            _cySpringController.SetPartsSpringRate(CySpringController.Parts.Head, _hairStiffnessOverride);
            
            // Set collision scale for head
            try
            {
                _cySpringController.SetScale(CySpringController.Parts.Head, _hairCollisionScale);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CySpringHairController] Failed to set head scale: {e.Message}");
            }
            
            // APPLY GRAVITY - this prevents hair from floating up!
            CySpringController.GravityRate = _hairGravityMultiplier;
            
            // Try to access and modify the actual spring stiffness
            TryModifySpringStiffness();
            
            Debug.Log($"[CySpringHairController] Applied: stiffness={_hairStiffnessOverride}, " +
                     $"gravity={_hairGravityMultiplier}, drag={_hairDragOverride}, " +
                     $"collision={_hairCollisionScale}");
        }
        
        private void TryModifySpringStiffness()
        {
            // Try to access the spring array directly
            var springArrayField = typeof(CySpringController).GetField("_springArray",
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (springArrayField == null)
                return;
            
            var springArray = springArrayField.GetValue(_cySpringController) as CySpring[];
            if (springArray == null || springArray.Length == 0)
                return;
            
            // Index 0 is Head (hair)
            var headSpring = springArray[0];
            if (headSpring == null)
                return;
            
            // Try to set stiffness force rate
            var stiffnessField = typeof(CySpring).GetField("_stiffnessForceRate",
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (stiffnessField != null)
            {
                stiffnessField.SetValue(headSpring, _hairStiffnessOverride);
                Debug.Log($"[CySpringHairController] Set head spring stiffness to {_hairStiffnessOverride}");
            }
        }
        
        /// <summary>
        /// Set hair stiffness at runtime
        /// </summary>
        public void SetHairStiffness(float stiffness)
        {
            _hairStiffnessOverride = Mathf.Clamp01(stiffness);
            ApplyHairOverrides();
        }
        
        /// <summary>
        /// Set hair collision at runtime
        /// </summary>
        public void SetHairCollision(float scale)
        {
            _hairCollisionScale = Mathf.Clamp(scale, 0.5f, 3.0f);
            ApplyHairOverrides();
        }
    }
}
