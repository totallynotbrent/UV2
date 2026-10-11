using System;
using System.Collections.Generic;
using UnityEngine;

// generated from the game's own typetree node dump (research/
// physics_container_typetree.txt): field names, types and order match the
// serialized format exactly, so the cloth bundles' components bind with no
// extraction step. the bundles ship one GameObject per container carrying
// this single component; the container is data-only (the game treats it the
// same way). connected bones are serialized as char arrays and read back as
// strings by Unity's binding.
namespace Gallop
{
    [Serializable]
    public class CySpringCollisionData
    {
        public string _collisionName = "";
        public string _targetObjectName = "";
        public byte _isOtherTarget;
        public Vector3 _offset;
        public Vector3 _offset2;
        public float _radius;
        public float _distance;
        public Vector3 _normal;
        public int _type;
        public byte _isInner;
    }

    [Serializable]
    public class CySpringParamDataChildElement
    {
        public string _boneName = "";
        public float _stiffnessForce;
        public float _dragForce;
        public float _gravity;
        public float _collisionRadius;
        public byte _needEnvCollision;
        public List<string> _collisionNameList = new();
        public float _verticalWindRateSlow;
        public float _horizontalWindRateSlow;
        public float _verticalWindRateFast;
        public float _horizontalWindRateFast;
        public byte _isLimit;
        public Vector3 _limitAngleMin;
        public Vector3 _limitAngleMax;
    }

    [Serializable]
    public class CySpringParamDataElement
    {
        public string _boneName = "";
        public float _stiffnessForce;
        public float _dragForce;
        public float _gravity;
        public float _collisionRadius;
        public byte _needEnvCollision;
        public List<string> _collisionNameList = new();
        public byte _isLimit;
        public Vector3 _limitAngleMin;
        public Vector3 _limitAngleMax;
        public float MoveSpringApplyRate = 1f;
        public byte _needSimulateEndBone = 1;
        public float _verticalWindRateSlow;
        public float _horizontalWindRateSlow;
        public float _verticalWindRateFast;
        public float _horizontalWindRateFast;
        public List<CySpringParamDataChildElement> _childElements = new();
    }

    [Serializable]
    public class ConnectedBoneData
    {
        public string Bone1 = "";
        public string Bone2 = "";
        public float Intensity;
        public byte IsFold;
    }

    // the container MonoBehaviour itself: one per cloth bundle
    // (pfb_chrN_clothNN / pfb_bdyN_bustN_clothNN / pfb_bdyN_clothNN /
    // pfb_tailN_clothNN). loaded in the order head, body, bust, tail.
    public class CySpringDataContainer : MonoBehaviour
    {
        public List<CySpringCollisionData> collisionParam = new();
        public List<CySpringParamDataElement> springParam = new();
        public List<ConnectedBoneData> ConnectedBoneList = new();

        public byte enableVerticalWind;
        public byte enableHorizontalWind;
        public float centerWindAngleSlow;
        public float centerWindAngleFast;
        public float verticalCycleSlow = 1f;
        public float horizontalCycleSlow = 1f;
        public float verticalAngleWidthSlow;
        public float horizontalAngleWidthSlow;
        public float verticalCycleFast = 1f;
        public float horizontalCycleFast = 1f;
        public float verticalAngleWidthFast;
        public float horizontalAngleWidthFast;
        public byte IsEnableHipMoveParam;
        public float HipMoveInfluenceDistance;
        public float HipMoveInfluenceMaxDistance;
        public byte UseCorrectScaleCalc;
    }
}
