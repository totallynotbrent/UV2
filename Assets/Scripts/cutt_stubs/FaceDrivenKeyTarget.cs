using System;
using System.Collections.Generic;
using UnityEngine;

// generated from the game's face typetree dump: field names, types and order
// match the serialized FaceDrivenKeyTarget exactly (embedded in every
// pfb_chrXXXX_00 head prefab), so the morph target database deserializes
// with no extraction step. the trs deltas are the game's own facial morph
// data. all three category lists share one entry type (unity binds by field
// name, and each entry is the same _faceGroupInfo shape).
namespace Gallop
{
    [Serializable]
    public class TrsArray
    {
        public string _path;
        public bool _isValidScaleTransform;
        public Vector3 _position;
        public Vector3 _scale;
        public Vector3 _rotation;
        public bool IsOverrideTarget;
    }

    [Serializable]
    public class FaceGroupInfo
    {
        public List<TrsArray> _trsArray = new();
    }

    [Serializable]
    public class FacialPartsTarget
    {
        public List<FaceGroupInfo> _faceGroupInfo = new();
    }

    // the per-chara facial morph database: per-category target lists, each
    // entry a list of group variants (group 0 = right side, 1 = left per the
    // game's direction flag) holding trs deltas by rig path. the game
    // serializes it as a ScriptableObject asset named ast_chrXXXX_00_
    // facial_target inside the head bundle - exactly these three fields
    // (typetree-proven on pfb_chr0001_00_face000 and the chr1001 head).
    public class FaceDrivenKeyTarget : ScriptableObject
    {
        public List<FacialPartsTarget> _eyeTarget = new();
        public List<FacialPartsTarget> _eyebrowTarget = new();
        public List<FacialPartsTarget> _mouthTarget = new();
    }
}
