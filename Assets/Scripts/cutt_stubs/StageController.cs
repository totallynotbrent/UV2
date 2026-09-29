using System;
using System.Collections.Generic;
using UnityEngine;

// generated from the game's own typetree node dump: the stage controller's
// serialized fields in the game's wire order.
namespace Gallop.Live
{
    [Serializable]
    public class FlareDataGroup
    {
        public int objectId;
        public byte isHqOnly;
    }

    [Serializable]
    public class Object
    {
        public int replaceQuality;
        public int objectId;
        public string objectName = "";
    }

    [Serializable]
    public class StageObjectUnit
    {
        public string UnitName = "";
        public List<GameObject> ChildObjects = new();
        public List<string> _childObjectNames = new();
    }

    [Serializable]
    public class StageObjectSelect
    {
        public string _prefabName = "";
        public int _condition;
        public int _selectIndex;
        public List<GameObject> _stageObjectArray = new();
    }

    [Serializable]
    public class NeonMaterialInfo
    {
        public Material _mainMaterial;
        public Material _backMaterial;
    }

    [Serializable]
    public class MovieMaterialInfo
    {
        public Material _movieMaterial;
        public byte _movieType;
        public int _renderOrder;
        public byte HasPropertyFilterTexScale;
        public Vector2 FilterTexScaleBase;
    }

    [Serializable]
    public class MipmapBias
    {
        public float _stage;
    }

    [Serializable]
    public class StageController : MonoBehaviour
    {
        public List<GameObject> _stageObjects = new();
        public List<StageObjectUnit> _stageObjectUnits = new();
        public List<StageObjectSelect> _stageObjectSelects = new();
        public List<GameObject> _audienceObjects = new();
        public Vector3 _cyalumeOffsetPosition;
        public List<Material> _washLightMaterials = new();
        public List<Material> _uvScrollLightMaterials = new();
        public List<GameObject> _laserObjects = new();
        public List<Material> _laserMaterials = new();
        public List<Material> _appTimeMaterialArray = new();
        public List<UnityEngine.Object> _mirrorScanLightTextures = new();
        public List<Material> _footLightMaterials = new();
        public List<NeonMaterialInfo> _neonMaterialInfos = new();
        public List<MovieMaterialInfo> _movieMaterialInfos = new();
        public List<GameObject> _animationObjects = new();
        public string _borderLightObjectName = "";
        public string _sunObjectName = "";
        public List<GameObject> _preLiveSkitPrefabs = new();
        public List<GameObject> _afterLiveSkitPrefabs = new();
        public List<AnimationClip> _audienceAnimations = new();
        public MipmapBias _mipmapBias = new();
        public List<GameObject> _blackBoardNamePrefabs = new();
        public GameObject _blackBoardNameAtlas;
    }
}
