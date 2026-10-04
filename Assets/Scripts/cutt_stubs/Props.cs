using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // serialized game component on live prop prefabs; drives the stand-mic pole telescope
    // (uv2_props_adjustment_decoded.md). field order matches the bundle typetree exactly.
    [Serializable]
    public class Props : MonoBehaviour
    {
        [Serializable]
        public class AdjustmentData
        {
            public Transform Transform;
            public Vector3 OffsetRange;
            public Vector3 TransformRate;
            public int TargetNode;
            public Vector3 TargetOffset;
        }

        public AdjustmentData[] _adjustmentDataArray;
        public byte _isInfluenceOfCharaHeight;
        public List<GameObject> _blinkLightRootObjectArray = new();
        public List<Transform> _stageObjectTransformArray = new();
    }
}
