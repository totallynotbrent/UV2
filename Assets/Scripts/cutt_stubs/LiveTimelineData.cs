using System;
using System.Collections.Generic;
using UnityEngine;

// generated from the game's own typetree node dumps: field names, types and
// order match the serialized format exactly so bundle data binds with no
// extraction step.
namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineCharacterSettings
    {
        public List<int> motionSequenceIndices = new();
        public List<byte> useHighPolygonModel = new();
        public List<byte> useHighPolygonModelForLightMode = new();
        public List<byte> useWetTexture = new();
        public List<byte> UseDirtTextureArray = new();
        public List<int> EyeReflectionTextureSizeRateArray = new();
    }

    [Serializable]
    public class PropsConditionData
    {
        public int Type;
        public int Value;
    }

    [Serializable]
    public class PropsConditionGroup
    {
        public List<PropsConditionData> propsConditionData = new();
        public int propsConditionCount;
        public byte satisfiesAllConditions;
        public byte IsInvalid;
    }

    [Serializable]
    public class PropsDataGroup
    {
        public string propsName = "";
        public byte isCharaProps;
        public byte IsToonProp;
        public byte IsRichProp;
        public byte IsDepthWriteAlphaMesh;
        public int charaPropsMajorId;
        public int charaPropsMinorId;
        public List<string> attachJointNames = new();
        public List<int> AttachPropIdArray = new();
        public List<string> AttachPropJointNameArray = new();
        public int attachJointNameCount;
        public byte hasShadow;
        public byte isInfluenceOfCharaHeight;
        public List<PropsConditionGroup> propsConditionGroup = new();
        public int propsConditionGroupCount;
        public byte IsFlareCollisionEnabled;
        public byte isUseGenderDiffPropsId;
        public int MaleCharaPropsMajorId;
        public int MaleCharaPropsMinorId;
        public int FemaleCharaPropsMajorId;
        public int FemaleCharaPropsMinorId;
    }

    [Serializable]
    public class LiveTimelinePropsSettings
    {
        public List<PropsDataGroup> propsDataGroup = new();
        public int propsDataGroupCount;
    }

    [Serializable]
    public class LiveTimelineSunShaftsSettings
    {
        public int resolution;
        public int screenBlendMode;
        public Color sunColor;
        public float sunPower;
        public float sunCenterBrightness;
        public float sunCenterMultiplex;
        public float intensity;
        public float fadeStart;
        public float fadeMix;
        public float blackLevel;
        public float komorebiRate;
        public float blurRadius;
        public int blurIterations;
        public byte isEnabledBorderClear;
    }

    [Serializable]
    public class LiveTimelineHdrBloomSettings
    {
        public int bloomBlurIterations;
    }

    [Serializable]
    public class LiveTimelineCharacterOptionSettings
    {
        public List<string> optionName = new();
    }

    [Serializable]
    public class LiveTimelineIndirectLightShaftsSettings
    {
        public int id;
        public int shaftType;
        public int shaftTextureNameLength;
        public List<string> shaftTextureNames = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraSettings
    {
        public int cameraNum;
    }

    [Serializable]
    public class LiveTimelineMonitorCameraSettings
    {
        public byte IsEnabledTextureWidthRate;
        public float TextureWidthRate;
    }

    [Serializable]
    public class LiveTimelineProjectorSettings
    {
        public byte IsEnabledProjectorObjectForLightMode;
    }

    [Serializable]
    public class FlareDataGroup
    {
        public int objectId;
        public byte isHqOnly;
    }

    [Serializable]
    public class LiveTimelineLensFlareSetting
    {
        public List<FlareDataGroup> flareDataGroup = new();
        public int flareDataGroupCount;
        public float lightStandard;
        public float underLimit;
    }

    [Serializable]
    public class Object
    {
        public int replaceQuality;
        public int objectId;
        public string objectName = "";
    }

    [Serializable]
    public class LiveTimelineStageObjectsSettings
    {
        public int objectCount;
        public List<Object> objectData = new();
    }

    [Serializable]
    public class LiveTimelineDebugSettings
    {
        public byte isCheckCharacterPosition;
    }

    [Serializable]
    public class LiveTimelineData : ScriptableObject
    {
        public string version = "";
        public int timeLength;
        public int characterPositionMode;
        public List<string> spotLightPrefabNames = new();
        public string spotLightParentName = "";
        public List<string> shadowPrefabNames = new();
        public LiveTimelineCharacterSettings characterSettings = new();
        public LiveTimelinePropsSettings propsSettings = new();
        public LiveTimelineSunShaftsSettings sunShaftsSettings = new();
        public LiveTimelineHdrBloomSettings hdrBloomSettings = new();
        public LiveTimelineCharacterOptionSettings characterOptionSettings = new();
        public LiveTimelineIndirectLightShaftsSettings indirectLightShaftsSettings = new();
        public LiveTimelineMultiCameraSettings multiCameraSettings = new();
        public LiveTimelineMonitorCameraSettings MonitorCameraSettings = new();
        public LiveTimelineProjectorSettings ProjectorSettings = new();
        public List<LiveTimelineWorkSheet> worksheetList = new();
        public int FacialLineType;
        public byte isUseHQParticle;
        public List<string> particlePrefabNames = new();
        public List<string> mirrorScanLightBodyPrefabNames = new();
        public LiveTimelineLensFlareSetting lensFlareSetting = new();
        public float maxForcalSize;
        public byte isUseMirrorScanMotionDictionary;
        public byte isUseGameSettingToParticle;
        public Vector3 StageArenaPosition;
        public byte IsChangedCharaLayerOnlyMirrorRendering;
        public byte IsRenderMirrorObjectOnlyOnBaseCamera;
        public byte UseDepthOnlyCube;
        public int DepthOnlyCubeCount;
        public LiveTimelineStageObjectsSettings stageObjectsSettings = new();
        public LiveTimelineDebugSettings debugSettings = new();
    }

    // the cutt prefab's timeline control component: holds the data asset.
    [Serializable]
    public class LiveTimelineControl : MonoBehaviour
    {
        public LiveTimelineData data;
    }
}
