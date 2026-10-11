using System;
using System.Collections.Generic;
using UnityEngine;

// generated from the live skirt asset typetree (ast_bdy1002_00_skirt00):
// field names, types and order match the serialized format exactly, so the
// skirt parameter bundles deserialize with no extraction step.
namespace Gallop
{
    [Serializable]
    public class SkirtParamDataOverrideCollision
    {
        public string _collisionName = "";
        public float _radius;
    }

    [Serializable]
    public class SkirtParamDataElement
    {
        public string _rootBoneName = "";
        public string _childBoneName = "";
        public byte _isCheckRightLeg;
        public byte _isCheckLeftLeg;
        public byte IsIgnoreRaceRun;
    }

    public class SkirtParamDataAsset : ScriptableObject
    {
        public byte _isLongSkirt;
        public string _centerBoneName = "";
        public string _kneeLBoneName = "";
        public string _kneeRBoneName = "";
        public string _ankleLBoneName = "";
        public string _ankleRBoneName = "";
        public float _kneeColliderRadius;
        public float _ankleColliderRadius;
        public float _influenceAngle;
        public float _influenceMaxAngle;
        public byte IsOffsetAngleTargetEndNode;
        public byte IsScaleCollision;
        public List<SkirtParamDataOverrideCollision> OverrideCollisionList = new();
        public List<SkirtParamDataElement> _skirtBone = new();
    }
}
