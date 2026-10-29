using UnityEngine;
using System.Reflection;

namespace Gallop
{
    /// <summary>
    /// Directly modifies hair physics by intercepting the stiffness value
    /// right before it's passed to the native plugin.
    /// </summary>
    public class CySpringHairDirect : MonoBehaviour
    {
        [Header("Hair Physics")]
        [Range(0.01f, 1.0f)]
        [SerializeField] private float _hairStiffness = 0.05f;
        
        [Header("Settings")]
        [SerializeField] private bool _enableOverride = true;
        
        private CySpringController _cySpringController;
        private CySpring _headSpring;
        private FieldInfo _stiffnessField;
        private FieldInfo _springArrayField;
        
        private void Start()
        {
            _cySpringController = GetComponent<CySpringController>();
            if (_cySpringController == null)
            {
                Debug.LogError("[CySpringHairDirect] No CySpringController!");
                enabled = false;
                return;
            }
            
            // Get access to spring array
            _springArrayField = typeof(CySpringController).GetField("_springArray",
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            // Get access to stiffness field
            _stiffnessField = typeof(CySpring).GetField("_stiffnessForceRate",
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (_springArrayField == null || _stiffnessField == null)
            {
                Debug.LogError("[CySpringHairDirect] Could not access required fields!");
                enabled = false;
                return;
            }
            
            // Get the head spring (index 0)
            UpdateHeadSpring();
            
            Debug.Log($"[CySpringHairDirect] Initialized. Hair stiffness={_hairStiffness}");
        }
        
        private void UpdateHeadSpring()
        {
            if (_cySpringController == null || _springArrayField == null)
                return;
            
            var springArray = _springArrayField.GetValue(_cySpringController) as CySpring[];
            if (springArray == null || springArray.Length == 0)
                return;
            
            // Index 0 is Head (hair)
            _headSpring = springArray[0];
        }
        
        private void LateUpdate()
        {
            if (!_enableOverride || _headSpring == null || _stiffnessField == null)
                return;
            
            // Force the stiffness value EVERY FRAME
            // This overrides whatever was set by SetPartsSpringRate
            _stiffnessField.SetValue(_headSpring, _hairStiffness);
        }
        
        /// <summary>
        /// Set hair stiffness at runtime
        /// </summary>
        public void SetHairStiffness(float value)
        {
            _hairStiffness = Mathf.Clamp01(value);
            Debug.Log($"[CySpringHairDirect] Hair stiffness set to {_hairStiffness}");
        }
        
        /// <summary>
        /// Get current hair stiffness
        /// </summary>
        public float GetHairStiffness()
        {
            return _hairStiffness;
        }
    }
}
