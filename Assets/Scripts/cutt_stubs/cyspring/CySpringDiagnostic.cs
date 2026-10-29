using UnityEngine;
using System.Collections.Generic;

namespace Gallop
{
    /// <summary>
    /// Diagnostic tool to check CySpring bone movement amounts.
    /// </summary>
    public class CySpringDiagnostic : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool _enableDiagnostic = true;
        [SerializeField] private float _logInterval = 2.0f;
        
        private CySpringController _cySpringController;
        private float _logTimer;
        private Dictionary<Transform, Vector3> _lastPositions = new Dictionary<Transform, Vector3>();
        private Dictionary<Transform, float> _totalMovement = new Dictionary<Transform, float>();
        
        private void Start()
        {
            _cySpringController = GetComponent<CySpringController>();
            if (_cySpringController == null)
            {
                Debug.LogWarning("[CySpringDiagnostic] No CySpringController found");
                enabled = false;
                return;
            }
            
            Debug.Log("[CySpringDiagnostic] Ready. Monitoring movement amounts...");
        }
        
        private void Update()
        {
            if (!_enableDiagnostic || _cySpringController == null)
                return;
            
            _logTimer += Time.deltaTime;
            if (_logTimer >= _logInterval)
            {
                CheckBoneMovement();
                _logTimer = 0f;
            }
        }
        
        private void CheckBoneMovement()
        {
            Transform[] allTransforms = GetComponentsInChildren<Transform>();
            
            float totalHairMovement = 0f;
            float totalSkirtMovement = 0f;
            int hairCount = 0;
            int skirtCount = 0;
            
            float maxHairMovement = 0f;
            float maxSkirtMovement = 0f;
            string maxHairBone = "";
            string maxSkirtBone = "";
            
            foreach (Transform t in allTransforms)
            {
                if (t == null) continue;
                
                string name = t.name.ToLower();
                bool isHair = name.Contains("hair") || name.Contains("ponytail") || name.Contains("braid");
                bool isSkirt = name.Contains("skirt") || name.Contains("cloth");
                
                if (isHair || isSkirt)
                {
                    Vector3 currentPos = t.position;
                    
                    if (_lastPositions.TryGetValue(t, out Vector3 lastPos))
                    {
                        float movement = Vector3.Distance(currentPos, lastPos);
                        
                        if (isHair)
                        {
                            hairCount++;
                            totalHairMovement += movement;
                            if (movement > maxHairMovement)
                            {
                                maxHairMovement = movement;
                                maxHairBone = t.name;
                            }
                        }
                        
                        if (isSkirt)
                        {
                            skirtCount++;
                            totalSkirtMovement += movement;
                            if (movement > maxSkirtMovement)
                            {
                                maxSkirtMovement = movement;
                                maxSkirtBone = t.name;
                            }
                        }
                    }
                    
                    _lastPositions[t] = currentPos;
                }
            }
            
            float avgHair = hairCount > 0 ? totalHairMovement / hairCount : 0;
            float avgSkirt = skirtCount > 0 ? totalSkirtMovement / skirtCount : 0;
            
            Debug.Log($"[CySpringDiagnostic] === MOVEMENT REPORT ===");
            Debug.Log($"Hair: {hairCount} bones, avg={avgHair:F6}, max={maxHairMovement:F6} ({maxHairBone})");
            Debug.Log($"Skirt: {skirtCount} bones, avg={avgSkirt:F6}, max={maxSkirtMovement:F6} ({maxSkirtBone})");
            Debug.Log($"Gravity: {CySpringController.GravityRate}");
            Debug.Log($"If avg < 0.001 = very stiff, avg > 0.01 = flowing");
        }
    }
}
