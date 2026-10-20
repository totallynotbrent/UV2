using UnityEngine;
using System.Reflection;

namespace Gallop
{
    /// <summary>
    /// Forces ONLY hair to be loose. Does NOT affect skirt or clothing.
    /// </summary>
    public class CySpringForceLoose : MonoBehaviour
    {
        [Header("Hair ONLY Settings")]
        [Range(0.01f, 1.0f)]
        [SerializeField] private float _hairStiffness = 0.05f;  // Very low = very loose hair
        
        [Header("Collision")]
        [Range(0.5f, 3.0f)]
        [SerializeField] private float _hairCollisionScale = 1.0f;  // Hair collision size
        
        [Header("Settings")]
        [SerializeField] private bool _applyEveryFrame = true;
        
        private CySpringController _cySpringController;
        private CySpring[] _springArray;
        private FieldInfo _stiffnessField;
        private FieldInfo _springArrayField;
        
        private void Start()
        {
            _cySpringController = GetComponent<CySpringController>();
            if (_cySpringController == null)
            {
                Debug.LogWarning("[CySpringForceLoose] No CySpringController found");
                enabled = false;
                return;
            }
            
            // Get access to spring array
            _springArrayField = typeof(CySpringController).GetField("_springArray",
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            // Get access to stiffness field
            _stiffnessField = typeof(CySpring).GetField("_stiffnessForceRate",
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            Debug.Log($"[CySpringForceLoose] Ready. Hair stiffness={_hairStiffness}");
        }
        
        private void LateUpdate()
        {
            if (!_applyEveryFrame || _cySpringController == null)
                return;
            
            // ONLY modify Head (hair) - do NOT touch Body (skirt) or Tail
            _cySpringController.SetPartsSpringRate(CySpringController.Parts.Head, _hairStiffness);
            
            // Set collision for head only
            try
            {
                _cySpringController.SetScale(CySpringController.Parts.Head, _hairCollisionScale);
            }
            catch { }
            
            // Force the hair spring stiffness directly
            ForceHairStiffnessOnly();
        }
        
        private void ForceHairStiffnessOnly()
        {
            if (_springArrayField == null || _stiffnessField == null)
                return;
            
            var springArray = _springArrayField.GetValue(_cySpringController) as CySpring[];
            if (springArray == null || springArray.Length == 0)
                return;
            
            // Index 0 is Head (hair) - ONLY modify this one
            var headSpring = springArray[0];
            if (headSpring == null)
                return;
            
            // Force the stiffness value for hair ONLY
            _stiffnessField.SetValue(headSpring, _hairStiffness);
        }
        
        /// <summary>
        /// Set hair stiffness at runtime
        /// </summary>
        public void SetHairStiffness(float value)
        {
            _hairStiffness = Mathf.Clamp01(value);
            Debug.Log($"[CySpringForceLoose] Hair stiffness set to {_hairStiffness}");
        }
    }
}
