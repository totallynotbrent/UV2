using System;
using System.Collections.Generic;
using UnityEngine;

// generated from the game's own typetree node dump: field names, types and
// order match the serialized format exactly, so bundle data binds with no
// extraction step.
namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyTimescaleData
    {
        public int frame;
        public int attribute;
        public float Timescale;
    }

    [Serializable]
    public class LiveTimelineKeyTimescaleDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyTimescaleData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyCameraPositionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int setType;
        public Vector3 position;
        public Vector3 charaPos;
        public Vector3 CharaPositionAtStartFrame;
        public byte IsUseCharaPositionAtPrevKeyStartFrame;
        public List<Vector3> bezierPoints = new();
        public int charaRelativeBase;
        public int charaRelativeParts;
        public float traceSpeed;
        public float nearClip;
        public float farClip;
        public int cullingLayer;
        public int BgColorType;
        public Color BgColor;
        public int BgColorTargetCharacterIndex;
        public byte IsAttachedToProps;
        public int PropsIndex;
        public int PropsAttachNodeIndex;
    }

    [Serializable]
    public class LiveTimelineKeyCameraPositionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCameraPositionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyCameraLookAtData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int lookAtType;
        public Vector3 position;
        public int lookAtCharaPos;
        public int lookAtCharaParts;
        public Vector3 charaPos;
        public Vector3 CharaPositionAtStartFrame;
        public byte IsUseCharaPositionAtPrevKeyStartFrame;
        public List<Vector3> bezierPoints = new();
        public float traceSpeed;
        public byte IsAttachedToProps;
        public int PropsIndex;
        public int PropsAttachNodeIndex;
    }

    [Serializable]
    public class LiveTimelineKeyCameraLookAtDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCameraLookAtData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyCameraFovData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int fovType;
        public float fov;
    }

    [Serializable]
    public class LiveTimelineKeyCameraFovDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCameraFovData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyCameraRollData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float degree;
    }

    [Serializable]
    public class LiveTimelineKeyCameraRollDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCameraRollData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyCameraMotionData
    {
        public int frame;
        public int attribute;
        public byte IsEnable;
        public int MotionType;
        public AnimationClip Clip;
        public float MotionHeadTime;
        public float PlaySpeed;
        public int CharaRelativeBase;
        public int CharaRelativeParts;
        public Vector3 Offset;
        public Vector3 CharaPos;
    }

    [Serializable]
    public class LiveTimelineKeyCameraMotionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCameraMotionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyHandShakeCameraData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float power;
        public float frequency;
        public float Rate;
    }

    [Serializable]
    public class LiveTimelineKeyHandShakeCameraDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyHandShakeCameraData> thisList = new();
        public byte _useFixedShakePattern;
    }

    [Serializable]
    public class EventData
    {
        public int eventId;
        public string serializedParamter = "";
    }

    [Serializable]
    public class LiveTimelineKeyEventData
    {
        public int frame;
        public int attribute;
        public List<EventData> eventList = new();
    }

    [Serializable]
    public class LiveTimelineKeyEventDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyEventData> thisList = new();
    }

    [Serializable]
    public class AndConditionData
    {
        public int TargetOrder;
        public byte ActiveCharaId;
        public int CharaId;
        public byte ActiveDressId;
        public int DressId;
        public int DressCondition;
    }

    [Serializable]
    public class SwapMotionData
    {
        public int TargetMotion;
        public int TargetOrder;
        public byte ActiveCharaId;
        public int CharaId;
        public byte ActiveDressId;
        public int DressId;
        public int DressCondition;
        public string SwapMotionName = "";
        public List<AndConditionData> AndConditionDataArray = new();
    }

    [Serializable]
    public class LiveTimelineKeyCharaMotionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public string motionName = "";
        public string motionName2 = "";
        public string motionName3 = "";
        public AnimationClip clip;
        public AnimationClip clip2;
        public AnimationClip clip3;
        public List<SwapMotionData> SwapMotionDataArray = new();
        public int overrideMotionSysTextId;
        public byte _useRandomMotionSystemText;
        public int _randomMotionGroupIndex;
        public byte useOverrideMotionFacial;
        public byte UseSecondMotion;
        public byte EyeTraceSmoothness;
        public int motionHeadFrame;
        public int playFrameLength;
        public float playSpeed;
        public byte loop;
        public byte _isTimescaleDisabled;
        public byte isMotionHeadFrameAll;
        public List<int> motionHeadFrameSeparetes = new();
        public byte _isUseRunMode;
        public float _runModeMotionSpeed;
        public float _runModeBlendRate;
        public byte _isUsePrevAnimationTime;
        public byte _isCancelX;
        public byte _isCancelY;
        public byte _isCancelZ;
        public float _cancelRate;
        public byte _isCancelScaleAveragePos;
        public int _cancelOffsetCheckTarget;
        public Vector3 _cancelFixPos;
    }

    [Serializable]
    public class LiveTimelineKeyCharaMotionSeqDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCharaMotionData> thisList = new();
    }

    [Serializable]
    public class RandomEntrySystemTextGroup
    {
        public List<int> _systemTextIdArray = new();
    }

    [Serializable]
    public class LiveTimelineCharaMotSeqData
    {
        public LiveTimelineKeyCharaMotionSeqDataList keys = new();
        public byte _existsOverrideSystemTextMotion;
        public byte _existsRunModeKey;
        public List<RandomEntrySystemTextGroup> _randomEntrySystemTextGroupArray = new();
    }

    [Serializable]
    public class LiveTimelineKeyBgColor1Data
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Color color;
        public float power;
        public float scale;
        public int flags;
        public int ColorType;
        public float Saturation;
        public Color toonDarkColor;
        public Color toonBrightColor;
        public float vertexColorToonPower;
        public float outlineWidthPower;
        public Color outlineColor;
        public int outlineColorBlend;
        public int LightBlendMode;
        public byte IsProjector;
        public string BlinkLightName = "";
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public byte IsAdjustedBlinkLightColor;
        public int loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public byte isPasteLoopUnit;
        public byte isChangeLoopInterpolate;
        public float f32;
        public int _loopType;
        public int _loopCount;
        public int _loopExecutedCount;
        public int _loopIntervalFrame;
        public byte _isChangeLoopInterpolate;
    }

    [Serializable]
    public class LiveTimelineKeyBgColor1DataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyBgColor1Data> thisList = new();
    }

    [Serializable]
    public class LiveTimelineBgColor1Data
    {
        public string name = "";
        public LiveTimelineKeyBgColor1DataList keys = new();
        public List<int> _targetCharaIdArray = new();
        public List<int> _targetDressIdArray = new();
    }

    [Serializable]
    public class LiveTimelineKeyBgColor2Data
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Color color1;
        public Color color2;
        public float power;
        public string BlinkLightName = "";
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public byte IsSyncBlinkLightToColor1;
        public byte IsSyncBlinkLightToColor2;
        public byte IsAdjustedBlinkLightColor;
        public int loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public byte isPasteLoopUnit;
        public byte isChangeLoopInterpolate;
        public float f32;
        public int _loopType;
        public int _loopCount;
        public int _loopExecutedCount;
        public int _loopIntervalFrame;
        public byte _isChangeLoopInterpolate;
    }

    [Serializable]
    public class LiveTimelineKeyBgColor2DataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyBgColor2Data> thisList = new();
    }

    [Serializable]
    public class LiveTimelineBgColor2Data
    {
        public string name = "";
        public LiveTimelineKeyBgColor2DataList keys = new();
    }

    [Serializable]
    public class ChangeUVSettingCondition
    {
        public byte IsCheck;
        public int CharaId;
        public int DressId;
    }

    [Serializable]
    public class ChangeUVSetting
    {
        public byte IsChangeUVSetting;
        public int DispID;
        public List<ChangeUVSettingCondition> ConditionArray = new();
    }

    [Serializable]
    public class LiveTimelineKeyMonitorControlData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector2 position;
        public Vector2 size;
        public int dispID;
        public float speed;
        public string outputTextureLabel = "";
        public int playStartOffsetFrame;
        public float blendFactor;
        public Color colorFade;
        public Color BaseColor;
        public int SrcBlendMode;
        public int DstBlendMode;
        public int RenderQueueNo;
        public byte IsRenderQueue;
        public int DispID2;
        public float CrossFadeRate;
        public int LightImageNo;
        public int LightImageNo2;
        public float FilterTexScale;
        public List<ChangeUVSetting> ChangeUVSettingArray = new();
        public int extraContent;
    }

    [Serializable]
    public class LiveTimelineKeyMonitorControlDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMonitorControlData> thisList = new();
    }

    [Serializable]
    public class int2_storage
    {
        public int x;
        public int y;
    }

    [Serializable]
    public class LiveTimelineMonitorControlData
    {
        public string name = "";
        public LiveTimelineKeyMonitorControlDataList keys = new();
        public int2_storage _aspectRatio = new();
    }

    [Serializable]
    public class LiveTimelineKeyAnimationData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int animationID;
        public int wrapMode;
        public float speed;
        public float offsetTime;
        public float AnimationTime;
        public byte UseAnimationTime;
    }

    [Serializable]
    public class LiveTimelineKeyAnimationDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyAnimationData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineAnimationData
    {
        public string name = "";
        public LiveTimelineKeyAnimationDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyTextureAnimationData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public string textureName = "";
        public byte textureNameEmpty;
        public Vector2 offset;
        public Vector2 tiling;
        public Vector2 scrollSpeed;
        public float scrollInterval;
        public int textureID;
    }

    [Serializable]
    public class LiveTimelineKeyTextureAnimationDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyTextureAnimationData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineTextureAnimationData
    {
        public string name = "";
        public LiveTimelineKeyTextureAnimationDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyTransformData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 position;
        public Vector3 rotate;
        public Vector3 scale;
    }

    [Serializable]
    public class LiveTimelineKeyTransformDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyTransformData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineTransformData
    {
        public string name = "";
        public LiveTimelineKeyTransformDataList keys = new();
        public byte enablePosition;
        public byte enableRotate;
        public byte enableScale;
    }

    [Serializable]
    public class LiveTimelineKeyRendererData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte renderEnable;
    }

    [Serializable]
    public class LiveTimelineKeyRendererDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyRendererData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineRendererData
    {
        public string name = "";
        public LiveTimelineKeyRendererDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyObjectData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 position;
        public Vector3 rotate;
        public Vector3 scale;
        public byte renderEnable;
        public int AttachTarget;
        public int CharacterPosition;
        public int MultiCameraIndex;
        public byte IsAttachProps;
        public int PropsIndex;
        public int OffsetValueType;
        public int LayerTypeValue;
        public byte IsLayerTypeRecursively;
        public byte UseScreenCapture;
        public int ScreenCaptureId;
        public byte Use2dBackground;
        public int BgId;
        public int BgSubId;
        public int UseCaptureAspectType;
    }

    [Serializable]
    public class LiveTimelineKeyObjectDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyObjectData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineObjectData
    {
        public string name = "";
        public LiveTimelineKeyObjectDataList keys = new();
        public byte enablePosition;
        public byte enableRotate;
        public byte enableScale;
    }

    [Serializable]
    public class LiveTimelineKeyWaveObjectData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte IsWorldDir;
        public Vector3 WaveDir;
        public float WaveFreq;
        public float WaveSpeed;
        public float WaveSize;
    }

    [Serializable]
    public class LiveTimelineKeyWaveObjectDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyWaveObjectData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineWaveObjectData
    {
        public string name = "";
        public LiveTimelineKeyWaveObjectDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyAudienceData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 position;
        public Vector3 rotate;
        public Vector3 scale;
        public Color cyalumeColor;
        public Color cyalumeGlowColor;
        public float cyalumeGlowColorPower;
        public float cyalumeMaskRadius;
        public int animationSetting;
        public int animationRootIndex;
        public int animationBodyRegion;
        public int animationCategory;
        public int animationIndex;
        public int animationWrapMode;
        public float animationSpeed;
        public float animationOffsetTime;
        public float AnimationTime;
        public byte UseAnimationTime;
        public Color AudienceColor;
        public float AudienceColorPower;
        public Color AudienceToonBrightColor;
        public Color AudienceToonDarkColor;
        public float AudienceVertexColorToonPower;
        public float AudienceOutlineWidthPower;
        public Color AudienceOutlineColor;
        public int AudienceOutlineColorBlend;
        public Color AudienceRimColor;
        public float AudienceRimStep;
        public float AudienceRimFeather;
        public float AudienceRimSpecRate;
        public float AudienceRimShadowRate;
        public float AudienceRimHorizonOffset;
        public float AudienceRimVerticalOffset;
        public Color AudienceRimColor2;
        public float AudienceRimStep2;
        public float AudienceRimFeather2;
        public float AudienceRimSpecRate2;
        public float AudienceRimShadowRate2;
        public float AudienceRimHorizonOffset2;
        public float AudienceRimVerticalOffset2;
        public Vector3 AudienceDirectionalLightAngle;
    }

    [Serializable]
    public class LiveTimelineKeyAudienceDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyAudienceData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineAudienceData
    {
        public string name = "";
        public LiveTimelineKeyAudienceDataList keys = new();
        public int _objectIndex;
    }

    [Serializable]
    public class LiveTimelineKeyPropsData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int settingFlags;
        public int propsID;
        public byte rendererEnable;
        public byte IsVisibleAttachedCharaLinked;
        public byte AutoSwitchLayerOnMirrorRendering;
        public Color color;
        public Color rootColor;
        public Color tipColor;
        public float colorPower;
        public byte IsApplyAnimation;
        public byte IsApplyReserveWarming;
        public AnimationClip AnimationClip;
        public float StartAnimationTime;
        public int AnimationHeadFrame;
        public Color ToonDarkColor;
        public Color ToonBrightColor;
        public byte IsCastShadow;
        public byte IsCastShadowForced;
        public Vector3 _directionalLightAngle;
        public byte IsUpdateOutline;
        public float OutlineWidth;
        public Color OutlineColor;
        public byte IsEmissive;
        public Color EmissiveColor;
        public float EmissiveScrollTimeScale;
        public float EmissiveScrollEnergyScale;
    }

    [Serializable]
    public class LiveTimelineKeyPropsDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyPropsData> thisList = new();
    }

    [Serializable]
    public class LiveTimelinePropsData
    {
        public string name = "";
        public LiveTimelineKeyPropsDataList keys = new();
        public byte _applyVariation;
        public int _variationId;
    }

    [Serializable]
    public class LiveTimelineKeyPropsAttachData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public string _attachJointName = "";
        public int _attachJointHash;
        public string _copyPositionJointName = "";
        public int _copyPositionJointHash;
        public int _settingFlags;
        public int _propsId;
        public Vector3 _offsetPosition;
        public Vector3 OffsetRotate;
        public Vector3 OffsetScale;
        public byte IsLinkAttachBone;
        public int _attachType;
        public int _attachPropId;
        public string _attachTargetPropNodeName = "";
    }

    [Serializable]
    public class LiveTimelineKeyPropsAttachDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyPropsAttachData> thisList = new();
    }

    [Serializable]
    public class LiveTimelinePropsAttachData
    {
        public string name = "";
        public LiveTimelineKeyPropsAttachDataList keys = new();
        public byte _applyVariation;
        public int _variationId;
    }

    [Serializable]
    public class SourceData
    {
        public int _sourceType;
        public int _targetIndex;
        public string _nodeName = "";
        public float _weight;
        public int _propId;
    }

    [Serializable]
    public class LiveTimelineKeyParentConstraintData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte _enable;
        public float _weight;
        public List<SourceData> _sourceDataArray = new();
        public byte _influenceOfSourceScale;
    }

    [Serializable]
    public class LiveTimelineKeyParentConstraintDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyParentConstraintData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineParentConstraintData
    {
        public LiveTimelineKeyParentConstraintDataList keys = new();
        public int _objectType;
        public int _charaIndex;
        public int _dressIndex;
        public int _propsIndex;
        public string _stageObjectName = "";
        public int _stageObjectNameHash;
    }

    [Serializable]
    public class LiveTimelineKeyCameraSwitcherData
    {
        public int frame;
        public int attribute;
        public int cameraIndex;
    }

    [Serializable]
    public class LiveTimelineKeyCameraSwitcherDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCameraSwitcherData> thisList = new();
    }

    [Serializable]
    public class FacialPartsData
    {
        public int FacialPartsId;
        public int WeightPer;
    }

    [Serializable]
    public class SongPartVariationLipSyncTarget
    {
        public int SongPartVariationId;
        public int PositionFlag;
    }

    [Serializable]
    public class LiveTimelineKeyLipSyncData
    {
        public int frame;
        public int attribute;
        public int facialId;
        public int weight;
        public int speed;
        public int time;
        public List<FacialPartsData> facialPartsDataArray = new();
        public int interpolateType;
        public int character;
        public List<SongPartVariationLipSyncTarget> SongPartVariationTargetFlagList = new();
    }

    [Serializable]
    public class LiveTimelineKeyLipSyncDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyLipSyncData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyLipSyncPatternRangeData
    {
        public int frame;
        public int attribute;
        public int RangeActionType;
        public int RangeIndex;
        public int CharacterFlag;
    }

    [Serializable]
    public class LiveTimelineKeyLipSyncPatternRangeDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyLipSyncPatternRangeData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyLipSyncPatternData
    {
        public int frame;
        public int attribute;
        public int facialId;
        public int weight;
        public int speed;
        public int time;
        public List<FacialPartsData> facialPartsDataArray = new();
        public int interpolateType;
    }

    [Serializable]
    public class LiveTimelineKeyLipSyncPatternDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyLipSyncPatternData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineLipSyncPatternData
    {
        public LiveTimelineKeyLipSyncPatternDataList keys = new();
        public int _patternId;
        public byte _applyVariation;
        public int _variationId;
    }

    [Serializable]
    public class LiveTimelineKeyPostEffectDOFData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float forcalSize;
        public float blurSpread;
        public int charactor;
        public int dofBlurType;
        public int dofQuality;
        public float dofForegroundSize;
        public float dofFocalPoint;
        public float dofSmoothness;
        public float BallBlurPowerFactor;
        public float BallBlurBrightnessThreshhold;
        public float BallBlurBrightnessIntensity;
        public float BallBlurSpread;
    }

    [Serializable]
    public class LiveTimelineKeyPostEffectDOFDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyPostEffectDOFData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyPostEffectBloomDiffusionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float bloomDofWeight;
        public float threshold;
        public float intensity;
        public float BloomBlurSize;
        public int BloomBlendMode;
        public float diffusionBlurSize;
        public float diffusionBright;
        public float diffusionThreshold;
        public float diffusionSaturation;
        public float diffusionContrast;
    }

    [Serializable]
    public class LiveTimelineKeyPostEffectBloomDiffusionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyPostEffectBloomDiffusionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyRadialBlurData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int moveBlurType;
        public Vector2 radialBlurOffset;
        public int radialBlurDownsample;
        public float radialBlurStartArea;
        public float radialBlurEndArea;
        public float radialBlurPower;
        public int radialBlurIteration;
        public Vector2 radialBlurEllipseDir;
        public float radialBlurRollEulerAngles;
        public float depthPowerFront;
        public float depthPowerBack;
        public Vector4 depthCancelRect;
        public float depthCancelBlendLength;
    }

    [Serializable]
    public class LiveTimelineKeyRadialBlurDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyRadialBlurData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyPostFilmData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int filmMode;
        public int colorType;
        public float filmPower;
        public Vector2 filmOffsetParam;
        public Vector4 filmOptionParam;
        public Color color0;
        public Color color1;
        public Color color2;
        public Color color3;
        public float depthPower;
        public float DepthClip;
        public float RollAngle;
        public Vector2 FilmScale;
        public int layerMode;
        public int movieResId;
        public int movieFrameOffset;
        public float movieSpeed;
        public int colorBlend;
        public float colorBlendFactor;
        public string BlinkLightName = "";
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public byte IsAdjustedBlinkLightColor;
        public int loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public byte isPasteLoopUnit;
        public byte isChangeLoopInterpolate;
    }

    [Serializable]
    public class LiveTimelineKeyPostFilmDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyPostFilmData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyFluctuationData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte IsEnable;
        public Vector2 MoveDirection;
        public float MovePower;
        public float Power;
        public float DepthClip;
    }

    [Serializable]
    public class LiveTimelineKeyFluctuationDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFluctuationData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyVortexData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte IsEnable;
        public Vector4 Area;
        public float RotVolume;
        public float DepthClip;
    }

    [Serializable]
    public class LiveTimelineKeyVortexDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyVortexData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyFadeData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Color fadeColor;
    }

    [Serializable]
    public class LiveTimelineKeyFadeDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFadeData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyCameraLayerData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 offsetMaxPosition;
        public Vector3 offsetMinPosition;
    }

    [Serializable]
    public class LiveTimelineKeyCameraLayerDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCameraLayerData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyProjectorData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int motionID;
        public int materialID;
        public float speed;
        public Color color1;
        public float power;
        public Vector3 position;
        public float rotate;
        public Vector2 size;
        public int LightBlendMode;
        public int loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public byte isPasteLoopUnit;
        public byte isChangeLoopInterpolate;
    }

    [Serializable]
    public class LiveTimelineKeyProjectorDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyProjectorData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineProjectorData
    {
        public string name = "";
        public LiveTimelineKeyProjectorDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyFacialFaceData
    {
        public int frame;
        public int attribute;
        public int facialId;
        public int weight;
        public int speed;
        public int time;
        public int interpolateType;
    }

    [Serializable]
    public class LiveTimelineKeyFacialFaceDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFacialFaceData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyFacialMouthData
    {
        public int frame;
        public int attribute;
        public int facialId;
        public int weight;
        public int speed;
        public int time;
        public List<FacialPartsData> facialPartsDataArray = new();
        public int interpolateType;
        public int type;
    }

    [Serializable]
    public class LiveTimelineKeyFacialMouthDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFacialMouthData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyFacialEyeData
    {
        public int frame;
        public int attribute;
        public int facialId;
        public int weight;
        public int speed;
        public int time;
        public List<FacialPartsData> facialPartsDataArrayL = new();
        public List<FacialPartsData> facialPartsDataArrayR = new();
        public int interpolateType;
    }

    [Serializable]
    public class LiveTimelineKeyFacialEyeDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFacialEyeData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyFacialEyebrowData
    {
        public int frame;
        public int attribute;
        public int facialId;
        public int weight;
        public int speed;
        public int time;
        public List<FacialPartsData> facialPartsDataArrayL = new();
        public List<FacialPartsData> facialPartsDataArrayR = new();
        public int interpolateType;
        public byte usePartsScale;
    }

    [Serializable]
    public class LiveTimelineKeyFacialEyebrowDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFacialEyebrowData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyFacialEyeTrackData
    {
        public int frame;
        public int attribute;
        public int targetType;
        public int verticalRatePer;
        public int horizontalRatePer;
        public int speedRatePer;
        public int speed;
        public int time;
        public int interpolateType;
        public Vector3 DirectPosition;
    }

    [Serializable]
    public class LiveTimelineKeyFacialEyeTrackDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFacialEyeTrackData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyFacialEarData
    {
        public int frame;
        public int attribute;
        public int facialId;
        public int weight;
        public int speed;
        public int time;
        public int facialEarIdL;
        public int facialEarIdR;
        public byte useEarRandomMotion;
        public int randomMotionEarType;
        public int randomMotionIndex;
        public int interpolateType;
    }

    [Serializable]
    public class LiveTimelineKeyFacialEarDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFacialEarData> thisList = new();
    }

    [Serializable]
    public class TeardropConfig
    {
        public int teardropIndex;
        public int teardropSlot;
        public byte isReversed;
        public float Alpha;
        public float Speed;
        public Color Color;
    }

    [Serializable]
    public class LiveTimelineKeyFacialEffectData
    {
        public int frame;
        public int attribute;
        public int cheekType;
        public int tearyType;
        public int tearfulType;
        public List<TeardropConfig> teardropConfigs = new();
        public int mangameIndex;
        public int TwinkleTimeComparisonType;
    }

    [Serializable]
    public class LiveTimelineKeyFacialEffectDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFacialEffectData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyTailMotionData
    {
        public int frame;
        public int attribute;
        public byte UseTailRandomMotion;
        public int MotionIndex;
        public float PlaySpeed;
        public byte IsTimescaleDisabled;
    }

    [Serializable]
    public class LiveTimelineKeyTailMotionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyTailMotionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineFacialData
    {
        public LiveTimelineKeyFacialFaceDataList faceKeys = new();
        public LiveTimelineKeyFacialMouthDataList mouthKeys = new();
        public LiveTimelineKeyFacialEyeDataList eyeKeys = new();
        public LiveTimelineKeyFacialEyebrowDataList eyebrowKeys = new();
        public LiveTimelineKeyFacialEyeTrackDataList eyeTrackKeys = new();
        public LiveTimelineKeyFacialEarDataList earKeys = new();
        public LiveTimelineKeyFacialEffectDataList effectKeys = new();
        public LiveTimelineKeyTailMotionDataList tailMotionKeys = new();
    }

    [Serializable]
    public class LiveTimelineKeyToneCurveData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte IsEnable;
        public float DepthMask;
        public AnimationCurve ToneAnimationCurve = new();
        public Color MinCorrectionLevel;
        public Color MaxCorrectionLevel;
        public AnimationCurve MaskToneCurve = new();
        public Color MaskMinCorrectionLevel;
        public Color MaskMaxCorrectionLevel;
    }

    [Serializable]
    public class LiveTimelineKeyToneCurveDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyToneCurveData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyExposureData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte IsEnable;
        public float DepthMask;
        public float Gain;
        public float Lift;
        public float MaskGain;
        public float MaskLift;
    }

    [Serializable]
    public class LiveTimelineKeyExposureDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyExposureData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyTransmittedLightData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int Iterations;
        public float Intensity;
        public float Threshold;
        public float BlurSpread;
        public int BlendMode;
    }

    [Serializable]
    public class LiveTimelineKeyTransmittedLightDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyTransmittedLightData> thisList = new();
    }

    [Serializable]
    public class GameObjectIdentifierInHierarchy
    {
        public string _hierarchyPath = "";
    }

    [Serializable]
    public class LiveTransmittedLightMaskScaleKeyValue
    {
        public GameObjectIdentifierInHierarchy _gameObjectIdentifier = new();
        public float _maskScale;
    }

    [Serializable]
    public class LiveTimelineKeyTransmittedLightMaskData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int _currentVersion;
        public int _lightContainerCount;
        public List<float> MaskScaleArray = new();
        public List<LiveTransmittedLightMaskScaleKeyValue> _maskScaleKeyValueList = new();
        public byte _isBlinkLightColorPowerLinked;
    }

    [Serializable]
    public class LiveTimelineKeyTransmittedLightMaskDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyTransmittedLightMaskData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineTransmittedLightMaskData
    {
        public string name = "";
        public LiveTimelineKeyTransmittedLightMaskDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyLensDistortionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float Intensity;
        public float IntensityX;
        public float IntensityY;
        public float CenterX;
        public float CenterY;
        public float Scale;
    }

    [Serializable]
    public class LiveTimelineKeyLensDistortionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyLensDistortionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyScreenCaptureData
    {
        public int frame;
        public int attribute;
        public int CaptureId;
        public float CaptureScale;
    }

    [Serializable]
    public class LiveTimelineKeyScreenCaptureDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyScreenCaptureData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyFacialNoiseData
    {
        public int frame;
        public int attribute;
        public int EnableCharacterBitFlag;
    }

    [Serializable]
    public class LiveTimelineKeyFacialNoiseDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFacialNoiseData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyCharaMotionNoiseData
    {
        public int frame;
        public int attribute;
        public float sideChrMotNoiseBaseBias;
        public float sideChrMotNoiseRange;
        public float sideChrMotNoiseFrequency;
        public float backChrMotNoiseBaseBias;
        public float backChrMotNoiseRange;
        public float backChrMotNoiseFrequency;
        public byte isNegativeCheck;
        public List<int> cySpringGravityScaleType = new();
        public List<float> cySpringGravityScale = new();
        public List<string> cySpringGravityScaleBoneName = new();
        public List<float> cySpringExpressionKneeCollisionRadius = new();
        public List<float> cySpringExpressionAnkleCollisionRadius = new();
        public List<float> cySpringExpressionInfluenceAngle = new();
        public List<float> cySpringExpressionInfluenceMaxAngle = new();
    }

    [Serializable]
    public class LiveTimelineKeyCharaMotionNoiseDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCharaMotionNoiseData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyFormationOffsetData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 Position;
        public float RotationY;
        public float LocalRotationY;
        public float ScaleFactor;
        public byte visible;
        public int DressIndex;
        public byte warmUpCySpring;
        public float CySpringWarmUpTime;
        public float PropCySpringWarmUpTime;
        public byte isEnabledOffset;
        public Vector3 offsetMaxPosition;
        public Vector3 offsetMinPosition;
        public byte isEnabledOffsetCameraTargetChara;
        public byte IsWorldSpace;
        public Vector3 WorldSpaceOrigin;
        public float WorldRotationY;
        public byte IsLookAtWorldOrigin;
        public byte IsPositionAddParentNode;
        public string ParentObjectName = "";
        public byte IsCastShadow;
        public float CySpringRate;
        public byte IsLayerIndex;
        public int LayerIndex;
        public byte IsEmissiveColor;
        public Color EmissiveColor;
        public float EmissiveScrollTimeScale;
        public float EmissiveScrollEnergyScale;
        public byte IsEmissiveIntensity;
        public float EmissiveIntensity;
        public byte IsEmissiveSoftEdge;
        public float EmissiveRimPower;
        public float EmissiveRimIntensity;
        public byte IsEmissiveCenter;
        public float DirtEnergyR;
        public float DirtEnergyG;
        public float DirtEnergyB;
        public Color DirtColor;
        public Color DirtRimSpecularColor;
        public Color DirtToonColor;
        public int IKSystem;
        public int IKSystemParam1;
        public int IKSystemParam2;
        public int IKSystemParam3;
        public int IKSystemParam4;
        public int PositionPriority;
        public byte IsEnabledIKMicStandLOffset;
        public byte IsEnabledIKMicStandROffset;
        public Vector3 IKMicStandLOffsetHigh;
        public Vector3 IKMicStandLOffsetLow;
        public Vector3 IKMicStandROffsetHigh;
        public Vector3 IKMicStandROffsetLow;
        public int ver;
        public Vector2 posXZ;
        public float posY;
        public Vector3 position;
        public float rotY;
    }

    [Serializable]
    public class LiveTimelineKeyFormationOffsetDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFormationOffsetData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineFormationOffsetData
    {
        public LiveTimelineKeyFormationOffsetDataList centerKeys = new();
        public LiveTimelineKeyFormationOffsetDataList left1Keys = new();
        public LiveTimelineKeyFormationOffsetDataList right1Keys = new();
        public LiveTimelineKeyFormationOffsetDataList left2Keys = new();
        public LiveTimelineKeyFormationOffsetDataList right2Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place06Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place07Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place08Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place09Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place10Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place11Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place12Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place13Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place14Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place15Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place16Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place17Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place18Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place19Keys = new();
        public LiveTimelineKeyFormationOffsetDataList place20Keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyVolumeLightData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 sunPosition;
        public Color color1;
        public float power;
        public float komorebi;
        public float blurRadius;
        public float ColorRate;
        public float ScreenColorPower;
        public float EffectColorPower;
        public byte enable;
        public byte isEnabledBorderClear;
        public string BlinkLightName = "";
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public byte IsAdjustedBlinkLightColor;
    }

    [Serializable]
    public class LiveTimelineKeyVolumeLightDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyVolumeLightData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineVolumeLightData
    {
        public string name = "";
        public LiveTimelineKeyVolumeLightDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyHdrBloomData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float intensity;
        public float blurSpread;
        public byte enable;
    }

    [Serializable]
    public class LiveTimelineKeyHdrBloomDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyHdrBloomData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineHdrBloomData
    {
        public string name = "";
        public LiveTimelineKeyHdrBloomDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyParticleData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float emissionRate;
    }

    [Serializable]
    public class LiveTimelineKeyParticleDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyParticleData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineParticleData
    {
        public string name = "";
        public LiveTimelineKeyParticleDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyParticleGroupData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float FlickerLightRate;
        public float FlickerDarkRate;
    }

    [Serializable]
    public class LiveTimelineKeyParticleGroupDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyParticleGroupData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineParticleGroupData
    {
        public string name = "";
        public LiveTimelineKeyParticleGroupDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyWashLightData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float RaycastDistance;
        public float CameraProjectionSide;
        public float CameraProjectionColorPower;
    }

    [Serializable]
    public class LiveTimelineKeyWashLightDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyWashLightData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineWashLightData
    {
        public string name = "";
        public LiveTimelineKeyWashLightDataList keys = new();
        public byte _isAllSettings;
    }

    [Serializable]
    public class LiveTimelineKeyLaserData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 objectPosition;
        public Vector3 objectRotate;
        public Vector3 objectScale;
        public int formation;
        public Vector3 rotate;
        public float degRootYaw;
        public float degLaserPitch;
        public float posInterval;
        public int blink;
        public float blinkPeriod;
        public float RaycastDistance;
    }

    [Serializable]
    public class LiveTimelineKeyLaserDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyLaserData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineLaserData
    {
        public string name = "";
        public LiveTimelineKeyLaserDataList keys = new();
        public int _objectIndex;
        public int _materialIndex;
    }

    [Serializable]
    public class LiveTimelineKeyBlinkLightData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int LightBlendMode;
        public List<Color> color0Array = new();
        public List<Color> color1Array = new();
        public List<float> powerArray = new();
        public List<byte> isReverseHueArray = new();
        public List<int> CmnColorType0Array = new();
        public List<int> CmnColorType1Array = new();
        public List<int> CharacterIndex0Array = new();
        public List<int> CharacterIndex1Array = new();
        public List<byte> IsColorBlend0Array = new();
        public List<byte> IsColorBlend1Array = new();
        public List<float> ColorBlendRate0Array = new();
        public List<float> ColorBlendRate1Array = new();
        public List<Color> AltCharaColor0Array = new();
        public List<Color> AltCharaColor1Array = new();
        public int pattern;
        public int colorType;
        public float powerMin;
        public float powerMax;
        public int loopCount;
        public float waitTime;
        public float turnOnTime;
        public float turnOffTime;
        public float keepTime;
        public float intervalTime;
    }

    [Serializable]
    public class LiveTimelineKeyBlinkLightDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyBlinkLightData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineBlinkLightData
    {
        public string name = "";
        public LiveTimelineKeyBlinkLightDataList keys = new();
        public byte _useWashLightBlendMode;
    }

    [Serializable]
    public class LiveTimelineKeyUVScrollLightData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Color mulColor0;
        public Color mulColor1;
        public float colorPower;
        public float scrollOffsetX;
        public float scrollOffsetY;
        public float scrollSpeedX;
        public float scrollSpeedY;
        public Texture2D texture;
        public int ColorType0;
        public int ColorType1;
        public int CharacterIndex0;
        public int CharacterIndex1;
        public byte IsColorBlend0;
        public byte IsColorBlend1;
        public float ColorBlendRate0;
        public float ColorBlendRate1;
        public Color AltCharaColor0;
        public Color AltCharaColor1;
        public int loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public byte isPasteLoopUnit;
        public byte isChangeLoopInterpolate;
    }

    [Serializable]
    public class LiveTimelineKeyUVScrollLightDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyUVScrollLightData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineUVScrollLightData
    {
        public string name = "";
        public LiveTimelineKeyUVScrollLightDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyFacialToonData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float CheekPretenseThreshold;
        public float NosePretenseThreshold;
        public float CylinderBlend;
        public float HairNormalBlend;
        public int UseOriginalDirectionalLight;
        public Vector3 OriginalDirectionalLightDir;
        public float EyeToonStep;
        public float EyeToonFeather;
        public float EyeSaturation;
    }

    [Serializable]
    public class LiveTimelineKeyFacialToonDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFacialToonData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineFacialToonData
    {
        public LiveTimelineKeyFacialToonDataList centerKeys = new();
        public LiveTimelineKeyFacialToonDataList left1Keys = new();
        public LiveTimelineKeyFacialToonDataList right1Keys = new();
        public LiveTimelineKeyFacialToonDataList left2Keys = new();
        public LiveTimelineKeyFacialToonDataList right2Keys = new();
        public LiveTimelineKeyFacialToonDataList motion5Keys = new();
        public LiveTimelineKeyFacialToonDataList motion6Keys = new();
        public LiveTimelineKeyFacialToonDataList motion7Keys = new();
        public LiveTimelineKeyFacialToonDataList motion8Keys = new();
        public LiveTimelineKeyFacialToonDataList motion9Keys = new();
        public LiveTimelineKeyFacialToonDataList motion10Keys = new();
        public LiveTimelineKeyFacialToonDataList motion11Keys = new();
        public LiveTimelineKeyFacialToonDataList motion12Keys = new();
        public LiveTimelineKeyFacialToonDataList motion13Keys = new();
        public LiveTimelineKeyFacialToonDataList motion14Keys = new();
        public LiveTimelineKeyFacialToonDataList motion15Keys = new();
        public LiveTimelineKeyFacialToonDataList motion16Keys = new();
        public LiveTimelineKeyFacialToonDataList motion17Keys = new();
        public LiveTimelineKeyFacialToonDataList motion18Keys = new();
        public LiveTimelineKeyFacialToonDataList motion19Keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyGlobalLightData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 lightDir;
        public Color rimColor;
        public float rimStep;
        public float rimFeather;
        public float rimSpecRate;
        public float globalRimShadowRate;
        public byte cameraFollow;
        public float RimHorizonOffset;
        public float RimVerticalOffset;
        public float RimHorizonOffset2;
        public float RimVerticalOffset2;
        public Color rimColor2;
        public float rimStep2;
        public float rimFeather2;
        public float rimSpecRate2;
        public float globalRimShadowRate2;
        public int flags;
        public string BlinkLightName = "";
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public int loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public byte isPasteLoopUnit;
        public byte isChangeLoopInterpolate;
    }

    [Serializable]
    public class LiveTimelineKeyGlobalLightDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyGlobalLightData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineGlobalLightData
    {
        public string name = "";
        public LiveTimelineKeyGlobalLightDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyGlobalFogData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte isDistance;
        public float startDistance;
        public byte isHeight;
        public float height;
        public float heightDensity;
        public Color color;
        public int fogMode;
        public float expDensity;
        public float start;
        public float end;
        public byte useRadialDistance;
        public string BlinkLightName = "";
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public byte IsAdjustedBlinkLightColor;
    }

    [Serializable]
    public class LiveTimelineKeyGlobalFogDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyGlobalFogData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineGlobalFogData
    {
        public string name = "";
        public LiveTimelineKeyGlobalFogDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyColorCorrectionData
    {
        public int frame;
        public int attribute;
        public byte enable;
        public float saturation;
        public int mode;
        public AnimationCurve redCurve = new();
        public AnimationCurve greenCurve = new();
        public AnimationCurve blueCurve = new();
        public AnimationCurve depthRedCurve = new();
        public AnimationCurve depthGreenCurve = new();
        public AnimationCurve depthBlueCurve = new();
        public AnimationCurve blendCurve = new();
        public byte selective;
        public Color keyColor;
        public Color targetColor;
    }

    [Serializable]
    public class LiveTimelineKeyColorCorrectionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyColorCorrectionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineColorCorrectionData
    {
        public string name = "";
        public LiveTimelineKeyColorCorrectionDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyPreColorCorrectionData
    {
        public int frame;
        public int attribute;
        public byte enable;
        public float saturation;
        public int mode;
        public AnimationCurve redCurve = new();
        public AnimationCurve greenCurve = new();
        public AnimationCurve blueCurve = new();
        public AnimationCurve depthRedCurve = new();
        public AnimationCurve depthGreenCurve = new();
        public AnimationCurve depthBlueCurve = new();
        public AnimationCurve blendCurve = new();
        public byte selective;
        public Color keyColor;
        public Color targetColor;
        public int excludeColorCorrectionCharacterFlags;
        public int excludeColorCorrectionShadowFlags;
    }

    [Serializable]
    public class LiveTimelineKeyPreColorCorrectionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyPreColorCorrectionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelinePreColorCorrectionData
    {
        public string name = "";
        public LiveTimelineKeyPreColorCorrectionDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyLightShaftsData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte enabled;
        public Vector4 speed;
        public Vector4 angle;
        public Vector4 offset;
        public Vector4 alpha;
        public Vector4 alpha2;
        public Vector4 maskAlpha;
        public float maskAnimeTime;
        public Vector2 maskAlphaRange;
        public float scale;
        public byte _isAdjustScale;
    }

    [Serializable]
    public class LiveTimelineKeyLightShaftsDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyLightShaftsData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineLightShaftsData
    {
        public string name = "";
        public LiveTimelineKeyLightShaftsDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMonitorCameraLayerData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 offsetMaxPosition;
        public Vector3 offsetMinPosition;
    }

    [Serializable]
    public class LiveTimelineKeyMonitorCameraLayerDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMonitorCameraLayerData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMonitorCameraLayerData
    {
        public string name = "";
        public LiveTimelineKeyMonitorCameraLayerDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMonitorCameraPositionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int setType;
        public Vector3 position;
        public Vector3 charaPos;
        public Vector3 CharaPositionAtStartFrame;
        public byte IsUseCharaPositionAtPrevKeyStartFrame;
        public List<Vector3> bezierPoints = new();
        public int charaRelativeBase;
        public int charaRelativeParts;
        public float traceSpeed;
        public float nearClip;
        public float farClip;
        public int cullingLayer;
        public int BgColorType;
        public Color BgColor;
        public int BgColorTargetCharacterIndex;
        public byte IsAttachedToProps;
        public int PropsIndex;
        public int PropsAttachNodeIndex;
        public byte enable;
        public float fov;
        public float roll;
        public int cullingMask;
    }

    [Serializable]
    public class LiveTimelineKeyMonitorCameraPositionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMonitorCameraPositionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMonitorCameraPositionData
    {
        public string name = "";
        public LiveTimelineKeyMonitorCameraPositionDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMonitorCameraLookAtData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int lookAtType;
        public Vector3 position;
        public int lookAtCharaPos;
        public int lookAtCharaParts;
        public Vector3 charaPos;
        public Vector3 CharaPositionAtStartFrame;
        public byte IsUseCharaPositionAtPrevKeyStartFrame;
        public List<Vector3> bezierPoints = new();
        public float traceSpeed;
        public byte IsAttachedToProps;
        public int PropsIndex;
        public int PropsAttachNodeIndex;
    }

    [Serializable]
    public class LiveTimelineKeyMonitorCameraLookAtDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMonitorCameraLookAtData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMonitorCameraLookAtData
    {
        public string name = "";
        public LiveTimelineKeyMonitorCameraLookAtDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraLayerData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 offsetMaxPosition;
        public Vector3 offsetMinPosition;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraLayerDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMultiCameraLayerData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraLayerData
    {
        public string name = "";
        public LiveTimelineKeyMultiCameraLayerDataList keys = new();
        public int MultiCameraNo;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPositionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int setType;
        public Vector3 position;
        public Vector3 charaPos;
        public Vector3 CharaPositionAtStartFrame;
        public byte IsUseCharaPositionAtPrevKeyStartFrame;
        public List<Vector3> bezierPoints = new();
        public int charaRelativeBase;
        public int charaRelativeParts;
        public float traceSpeed;
        public float nearClip;
        public float farClip;
        public int cullingLayer;
        public int BgColorType;
        public Color BgColor;
        public int BgColorTargetCharacterIndex;
        public byte IsAttachedToProps;
        public int PropsIndex;
        public int PropsAttachNodeIndex;
        public byte enableMultiCamera;
        public float fadeTime;
        public float lineThickness;
        public int LineType;
        public Color LineColor;
        public float LineAntialiasing;
        public int maskType;
        public byte updateMainCamera;
        public float roll;
        public float fov;
        public float maskRoll;
        public Vector2 maskOffset;
        public float MaskCentralAngle;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPositionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMultiCameraPositionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraPositionData
    {
        public string name = "";
        public LiveTimelineKeyMultiCameraPositionDataList keys = new();
        public int MultiCameraNo;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraLookAtData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int lookAtType;
        public Vector3 position;
        public int lookAtCharaPos;
        public int lookAtCharaParts;
        public Vector3 charaPos;
        public Vector3 CharaPositionAtStartFrame;
        public byte IsUseCharaPositionAtPrevKeyStartFrame;
        public List<Vector3> bezierPoints = new();
        public float traceSpeed;
        public byte IsAttachedToProps;
        public int PropsIndex;
        public int PropsAttachNodeIndex;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraLookAtDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMultiCameraLookAtData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraLookAtData
    {
        public string name = "";
        public LiveTimelineKeyMultiCameraLookAtDataList keys = new();
        public int MultiCameraNo;
    }

    [Serializable]
    public class LiveTimelineKeyLensFlareData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 offset;
        public Color color;
        public float brightness;
        public float fadeSpeed;
        public byte enableParameter;
        public byte enableFlare;
        public byte IsAutoBrightness;
        public byte IsOverridePosition;
    }

    [Serializable]
    public class LiveTimelineKeyLensFlareDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyLensFlareData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineLensFlareData
    {
        public string name = "";
        public LiveTimelineKeyLensFlareDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyStageEnvironmentData
    {
        public int frame;
        public int attribute;
        public byte isValidMirror;
        public byte isMirror;
        public byte isBgMirror;
        public byte IsMirrorBg3d;
        public byte EnableCharacterMirrorExpandFaceBounds;
        public int characterMirror;
        public int CharacterMirrorHead;
        public int CharacterMirrorExpandFaceBounds;
        public float mirrorReflectionRate;
        public byte isValidShadow;
        public int characterShadow;
        public byte isSoftShadow;
        public byte IsToonMirror;
        public byte isValidWaterReflection;
        public float waterReflection;
        public float waveScale;
        public float waterDistortion;
        public float waterUCross;
        public float waterVCross;
        public float waterUSpeed;
        public float waterVSpeed;
        public float waterNormalPower;
        public float waveDistortionPower;
        public float waveClearly;
        public float waveDiffusion;
        public float waveDecline;
        public Color waterColor;
        public float waterVOffset;
        public byte IsValidStageFovShift;
        public float BaseFov;
        public float ShiftPower;
        public byte IsShiftY;
        public byte _isValidMirror;
        public byte _mirror;
        public byte _bgMirror;
        public int _characterMirror;
        public float _mirrorReflectionRate;
    }

    [Serializable]
    public class LiveTimelineKeyStageEnvironmentDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyStageEnvironmentData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineEnvironmentData
    {
        public string name = "";
        public LiveTimelineKeyStageEnvironmentDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMirrorReflectionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte EnableKey;
        public byte EnableMirror;
        public byte EnableBgLayer;
        public byte Enable3dLayer;
        public byte IsToonMirror;
        public float MirrorReflectionRate;
        public int TargetChara;
        public byte EnableCharaHead;
        public int TargetCharaHead;
    }

    [Serializable]
    public class LiveTimelineKeyMirrorReflectionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMirrorReflectionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMirrorReflectionData
    {
        public string name = "";
        public LiveTimelineKeyMirrorReflectionDataList keys = new();
        public int _baseCameraType;
        public int _baseCameraIndex;
    }

    [Serializable]
    public class LiveTimelineKeySweatLocatorData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int owner;
        public float alpha;
        public int randomVisibleCount;
        public byte locator0_isVisible;
        public Vector3 locator0_offset;
        public Vector3 locator0_offsetAngle;
        public int locator0_offsetType;
        public byte locator1_isVisible;
        public Vector3 locator1_offset;
        public Vector3 locator1_offsetAngle;
        public int locator1_offsetType;
        public byte locator2_isVisible;
        public Vector3 locator2_offset;
        public Vector3 locator2_offsetAngle;
        public int locator2_offsetType;
        public byte locator3_isVisible;
        public Vector3 locator3_offset;
        public Vector3 locator3_offsetAngle;
        public int locator3_offsetType;
        public byte locator4_isVisible;
        public Vector3 locator4_offset;
        public Vector3 locator4_offsetAngle;
        public int locator4_offsetType;
    }

    [Serializable]
    public class LiveTimelineKeySweatLocatorDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeySweatLocatorData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineSweatLocatorData
    {
        public string name = "";
        public LiveTimelineKeySweatLocatorDataList keys = new();
    }

    [Serializable]
    public class ParticleParam
    {
        public string ObjectName = "";
        public int ObjectNameHash;
        public byte IsEnabled;
        public float MainDuration;
        public byte MainLooping;
        public byte IsUpdateMainSimulationSpace;
        public byte IsSetAllMainSimulationSpace;
        public int MainSimulationSpace;
        public Color MainStartColorMin;
        public Color MainStartColorMax;
        public float EmissionRateOverTimeMultiplier;
        public Vector3 ShapeScale;
        public byte IsUpdateRandomSeed;
        public byte IsSetAllRandomSeed;
        public int RandomSeed;
    }

    [Serializable]
    public class LiveTimelineKeyEffectData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Color color;
        public float colorPower;
        public int ColorProperty;
        public int owner;
        public int occurrenceSpot;
        public string ParentStageObjectName = "";
        public byte IsAttachProps;
        public int PropsIndex;
        public Vector3 offset;
        public Vector3 offsetAngle;
        public Vector3 offsetScale;
        public List<ParticleParam> ParticleParamArray = new();
        public string BlinkLightName = "";
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public byte IsAdjustedBlinkLightColor;
        public byte IsSyncBlinkLight;
        public byte IsLinkOwnerPositionX;
        public byte IsLinkOwnerPositionY;
        public byte IsLinkOwnerPositionZ;
        public byte IsLinkOwnerRotate;
    }

    [Serializable]
    public class LiveTimelineKeyEffectDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyEffectData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineEffectData
    {
        public string name = "";
        public LiveTimelineKeyEffectDataList keys = new();
        public byte _applyVariation;
        public int _variationId;
        public string _folder = "";
    }

    [Serializable]
    public class LiveTimelineKeyContactEffectData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte ResetEffectAll;
        public byte IsEnabled;
        public int CharacterFlag;
        public int EffectMaxCount;
        public Color EffectColor;
        public float EffectColorPower;
        public byte ResetEffectColor;
        public byte IsEffectColorPowerRGBOnly;
        public Vector3 EffectOffset;
        public Vector3 EffectRotate;
        public float ContactPlaneHeight;
    }

    [Serializable]
    public class LiveTimelineKeyContactEffectDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyContactEffectData> thisList = new();
    }

    [Serializable]
    public class FootInfo
    {
        public Vector3 _offset;
        public Vector3 _direction;
        public float _releaseDistance;
    }

    [Serializable]
    public class TargetInfo
    {
        public string _rootBoneName = "";
        public FootInfo _footInfo = new();
    }

    [Serializable]
    public class CharaFootGroundData
    {
        public List<TargetInfo> _targetInfoArray = new();
        public float _effectDuration;
        public float _effectScaleMax;
        public AnimationCurve _effectScaleCurveByVelocity = new();
    }

    [Serializable]
    public class LiveTimelineContactEffectData
    {
        public string name = "";
        public LiveTimelineKeyContactEffectDataList keys = new();
        public string _folder = "";
        public CharaFootGroundData _charaFootGroundData = new();
    }

    [Serializable]
    public class LiveTimelineKeyRayHitEffectData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte ResetEffectAll;
        public byte IsEnabled;
        public byte IsRaycastMaskLayerCharaModel;
        public byte IsRaycastMaskLayerBG;
        public byte IsRaycastMaskLayer3D;
        public int Character;
        public float RayOriginAngleX;
        public float RayOriginAngleZ;
        public float RayOriginRadius;
        public int RayTargetNode;
        public Vector3 RayTargetPositionOffset;
        public float RayMaxDistance;
        public float RaycastInterval;
        public int RayHitEffectMaxCount;
        public float RayHitEffectDirectionBlendRate;
        public byte ResetRayHitEffectColor;
        public Color RayHitEffectColor;
        public float RayHitEffectColorPower;
        public byte IsRayHitEffectColorPowerRGBOnly;
        public byte IsRayHitEffectLayerTransparentFX;
    }

    [Serializable]
    public class LiveTimelineKeyRayHitEffectDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyRayHitEffectData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineRayHitEffectData
    {
        public string name = "";
        public LiveTimelineKeyRayHitEffectDataList keys = new();
        public string _folder = "";
        public float _effectDuration;
    }

    [Serializable]
    public class LiveTimelineKeyTiltShiftData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int mode;
        public int quality;
        public float blurArea;
        public float maxBlurSize;
        public int downsample;
        public Vector2 offset;
        public float roll;
    }

    [Serializable]
    public class LiveTimelineKeyTiltShiftDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyTiltShiftData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyHatchingData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int BlendMode;
        public float BlendAlpha;
        public Texture2D NoiseTexture;
        public float StrokeTiling;
        public float StrokeHeight;
        public float StrokeAngle;
        public float StrokeLength;
        public Vector2 CircleMaskOffset;
        public float CircleMaskSize;
        public Vector2 CircleMaskStretchRatio;
        public float CircleMaskAngle;
        public float CircleMaskInnerRatio;
        public float CircleMaskInnerEffect;
    }

    [Serializable]
    public class LiveTimelineKeyHatchingDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyHatchingData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyEdgeStyleData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte IsEnable;
        public Vector2 OffsetDirectionBase;
        public float OffsetScaleBase;
        public Vector2 OffsetDirectionExpand;
        public float OffsetScaleExpand;
        public byte IsCharacterListMode;
        public List<int> VisibleIndexList = new();
        public int DownscaleFactor;
        public float MinDepthValue;
        public float MaxDepthValue;
        public Texture2D MistNoiseTexture;
        public Color MistColor;
        public float MistDensity;
        public float MistIntensity;
        public float MistPower;
        public Vector2 MistScrollSpeed;
        public float MistBlurSize;
        public Texture2D SplashNoiseTexture;
        public Vector2 SplashScrollSpeed;
        public Color SplashColor;
        public float SplashThreshold;
        public float SplashBaseRatio;
        public float SplashDensity;
        public float SplashIntensity;
        public float SplashPower;
        public byte IsSplashOuter;
        public byte IsSplashInner;
    }

    [Serializable]
    public class LiveTimelineKeyEdgeStyleDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyEdgeStyleData> thisList = new();
        public int EdgeStyle;
    }

    [Serializable]
    public class LiveTimelineKeyFlashPlayerData
    {
        public int frame;
        public int attribute;
        public int _actionType;
        public byte UseActionLabel;
        public string ActionLabel = "";
        public string CueSheetName = "";
        public string CueName = "";
    }

    [Serializable]
    public class LiveTimelineKeyFlashPlayerDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyFlashPlayerData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyTitleData
    {
        public int frame;
        public int attribute;
        public int _actionType;
        public int _actionFrame;
    }

    [Serializable]
    public class LiveTimelineKeyTitleDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyTitleData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeySpotlight3dData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte isActive;
        public Color color;
        public float colorPower;
        public float localHeight;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public Vector3 characterPosition;
        public int targetCameraType;
        public int targetCameraIndex;
        public string assetName = "";
        public int characterIndex;
    }

    [Serializable]
    public class LiveTimelineKeySpotlight3dDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeySpotlight3dData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineSpotlight3dData
    {
        public string name = "";
        public LiveTimelineKeySpotlight3dDataList keys = new();
        public int _characterIndex;
        public int _assetId;
    }

    [Serializable]
    public class LiveTimelineKeyCharaNodeData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int PositionFlag;
        public byte EnableHeadCySpring;
        public byte EnableEarCySpring;
        public byte EnableBodyCySpring;
        public byte EnableSkirtCySpring;
        public byte EnableTailCySpring;
        public List<string> TargetCySpringBornNameList = new();
        public List<byte> TargetCySpringBornEnableList = new();
    }

    [Serializable]
    public class LiveTimelineKeyCharaNodeDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCharaNodeData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineCharaNodeData
    {
        public string name = "";
        public LiveTimelineKeyCharaNodeDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyNodeScaleData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int characterFlag;
        public int targetFlag;
        public int sizeType;
        public int scaleRatePer;
    }

    [Serializable]
    public class LiveTimelineKeyNodeScaleDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyNodeScaleData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineNodeScaleData
    {
        public string name = "";
        public LiveTimelineKeyNodeScaleDataList keys = new();
    }

    [Serializable]
    public class KeyVector3
    {
        public byte _isValid;
        public Vector3 _value;
    }

    [Serializable]
    public class NameNodeParam
    {
        public string Name = "";
        public int NameHash;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public byte AddTransformPosition;
        public byte AddTransformRotation;
        public byte AddTransformScale;
    }

    [Serializable]
    public class CharacterNodeData
    {
        public KeyVector3 HeadRotateOffset = new();
        public KeyVector3 NeckRotateOffset = new();
        public KeyVector3 ChestRotateOffset = new();
        public KeyVector3 SpineRotateOffset = new();
        public KeyVector3 WaistRotateOffset = new();
        public List<NameNodeParam> NameNodeParamArray = new();
    }

    [Serializable]
    public class LiveTimelineKeyCharaNodeOffsetData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public CharacterNodeData _commonNodeData = new();
        public byte _isUsePersonalityParam;
        public List<CharacterNodeData> _personalityNodeDataArray = new();
    }

    [Serializable]
    public class LiveTimelineKeyCharaNodeOffsetDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCharaNodeOffsetData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineCharaNodeOffsetData
    {
        public string name = "";
        public LiveTimelineKeyCharaNodeOffsetDataList keys = new();
        public int _characterIndex;
    }

    [Serializable]
    public class LiveTimelineKeyCharaFootLightData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int positionFlag;
        public List<float> hightMax = new();
        public List<Color> lightColor = new();
        public List<int> LightBlendModeArray = new();
        public List<int> EasingArray = new();
    }

    [Serializable]
    public class LiveTimelineKeyCharaFootLightDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCharaFootLightData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyChromaticAberrationData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte isEnable;
        public Vector2 redOffset;
        public Vector2 greenOffset;
        public Vector2 blueOffset;
        public float power;
        public float clip;
        public int effectType;
    }

    [Serializable]
    public class LiveTimelineKeyChromaticAberrationDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyChromaticAberrationData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineChromaticAberrationData
    {
        public string name = "";
        public LiveTimelineKeyChromaticAberrationDataList keys = new();
    }

    [Serializable]
    public class BitField
    {
        public int m_Bits;
    }

    [Serializable]
    public class AnimationData
    {
        public int TextureId;
        public int DivisionNumberX;
        public int DivisionNumberY;
        public int MaxCut;
        public float AnimationTime;
        public Vector2 ScaleUV;
        public Vector2 OffsetUV;
    }

    [Serializable]
    public class LiveTimelineKeyLightProjectionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte IsEnable;
        public byte OverrideIgnoreLayer;
        public BitField OverrideLayerMask = new();
        public byte BacksideOff;
        public float MirrorBallBacksideFeather;
        public byte ProjectionToCharacterModelOnly;
        public int TextureId;
        public Color Color;
        public Vector3 Position;
        public Vector3 Angle;
        public Vector3 Scale;
        public byte Orthographic;
        public float OrthographicSize;
        public float NearClipPlane;
        public float FarClipPlane;
        public float FieldOfView;
        public float ColorPower;
        public int LightBlendMode;
        public byte CharacterAttach;
        public int CharacterAttachPosition;
        public string BlinkLightName = "";
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public byte IsAdjustedBlinkLightColor;
        public Vector3 MirrorBallRotateAxis;
        public float MirrorBallRotateValue;
        public float MirrorBallProjectionRadius;
        public float MirrorBallFallOffPower;
        public byte MirrorBallIsLoopRotation;
        public float MirrorBallLoopRotationSpeed;
        public Vector2 MirrorBallUVOffset;
        public Vector2 MirrorBallUVScale;
        public byte MirrorBallUseCubeMap;
        public AnimationData AnimationParam = new();
    }

    [Serializable]
    public class LiveTimelineKeyLightProjectionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyLightProjectionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineLightProjectionData
    {
        public string name = "";
        public LiveTimelineKeyLightProjectionDataList keys = new();
        public int ContentType;
    }

    [Serializable]
    public class LiveTimelineKeyBillboardData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte manualAngle;
        public Vector3 angle;
    }

    [Serializable]
    public class LiveTimelineKeyBillboardDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyBillboardData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineBillboardData
    {
        public string name = "";
        public LiveTimelineKeyBillboardDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostFilmData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int filmMode;
        public int colorType;
        public float filmPower;
        public Vector2 filmOffsetParam;
        public Vector4 filmOptionParam;
        public Color color0;
        public Color color1;
        public Color color2;
        public Color color3;
        public float depthPower;
        public float DepthClip;
        public float RollAngle;
        public Vector2 FilmScale;
        public int layerMode;
        public int movieResId;
        public int movieFrameOffset;
        public float movieSpeed;
        public int colorBlend;
        public float colorBlendFactor;
        public string BlinkLightName = "";
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public byte IsAdjustedBlinkLightColor;
        public int loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public byte isPasteLoopUnit;
        public byte isChangeLoopInterpolate;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostFilmDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMultiCameraPostFilmData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraPostFilmData
    {
        public string name = "";
        public int MultiCameraNo;
        public LiveTimelineKeyMultiCameraPostFilmDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostEffectBloomDiffusionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float bloomDofWeight;
        public float threshold;
        public float intensity;
        public float BloomBlurSize;
        public int BloomBlendMode;
        public float diffusionBlurSize;
        public float diffusionBright;
        public float diffusionThreshold;
        public float diffusionSaturation;
        public float diffusionContrast;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostEffectBloomDiffusionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMultiCameraPostEffectBloomDiffusionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraPostEffectBloomDiffusionData
    {
        public string name = "";
        public int MultiCameraNo;
        public LiveTimelineKeyMultiCameraPostEffectBloomDiffusionDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraColorCorrectionData
    {
        public int frame;
        public int attribute;
        public byte enable;
        public float saturation;
        public int mode;
        public AnimationCurve redCurve = new();
        public AnimationCurve greenCurve = new();
        public AnimationCurve blueCurve = new();
        public AnimationCurve depthRedCurve = new();
        public AnimationCurve depthGreenCurve = new();
        public AnimationCurve depthBlueCurve = new();
        public AnimationCurve blendCurve = new();
        public byte selective;
        public Color keyColor;
        public Color targetColor;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraColorCorrectionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMultiCameraColorCorrectionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraColorCorrectionData
    {
        public string name = "";
        public int MultiCameraNo;
        public LiveTimelineKeyMultiCameraColorCorrectionDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraTiltShiftData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int mode;
        public int quality;
        public float blurArea;
        public float maxBlurSize;
        public int downsample;
        public Vector2 offset;
        public float roll;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraTiltShiftDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMultiCameraTiltShiftData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraTiltShiftData
    {
        public string name = "";
        public int MultiCameraNo;
        public LiveTimelineKeyMultiCameraTiltShiftDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraRadialBlurData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int moveBlurType;
        public Vector2 radialBlurOffset;
        public int radialBlurDownsample;
        public float radialBlurStartArea;
        public float radialBlurEndArea;
        public float radialBlurPower;
        public int radialBlurIteration;
        public Vector2 radialBlurEllipseDir;
        public float radialBlurRollEulerAngles;
        public float depthPowerFront;
        public float depthPowerBack;
        public Vector4 depthCancelRect;
        public float depthCancelBlendLength;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraRadialBlurDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMultiCameraRadialBlurData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraRadialBlurData
    {
        public string name = "";
        public int MultiCameraNo;
        public LiveTimelineKeyMultiCameraRadialBlurDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostEffectDOFData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public float forcalSize;
        public float blurSpread;
        public int charactor;
        public int dofBlurType;
        public int dofQuality;
        public float dofForegroundSize;
        public float dofFocalPoint;
        public float dofSmoothness;
        public float BallBlurPowerFactor;
        public float BallBlurBrightnessThreshhold;
        public float BallBlurBrightnessIntensity;
        public float BallBlurSpread;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostEffectDOFDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMultiCameraPostEffectDOFData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraPostEffectDOFData
    {
        public string name = "";
        public int MultiCameraNo;
        public LiveTimelineKeyMultiCameraPostEffectDOFDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraTransmittedLightData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int Iterations;
        public float Intensity;
        public float Threshold;
        public float BlurSpread;
        public int BlendMode;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraTransmittedLightDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMultiCameraTransmittedLightData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMultiCameraTransmittedLightData
    {
        public string name = "";
        public int MultiCameraNo;
        public LiveTimelineKeyMultiCameraTransmittedLightDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyData_AdditionalLight
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 Position;
        public Vector3 Rotate;
        public byte IsEnable;
        public int Type;
        public float Range;
        public int SpotAngle;
        public int IndirectMultiplier;
        public int ShadowType;
        public float Strength;
        public float Bias;
        public float NormalBias;
        public float NearPlane;
    }

    [Serializable]
    public class LiveTimelineKeyAdditionalLightList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyData_AdditionalLight> thisList = new();
    }

    [Serializable]
    public class LiveTimelineAdditionalLight
    {
        public string name = "";
        public LiveTimelineKeyAdditionalLightList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyData_MultiLightShadow
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte IsUseParam;
        public Vector3 ShadowCenterPosition;
        public Vector3 ShadowForward;
        public float ShadowFadeStart;
        public float ShadowFadeLength;
        public byte IsUseShadowFront;
        public byte IsShadowNearStart;
        public Color ShadowStartColor;
        public Color ShadowEndColor;
        public float ShadowDistance;
        public byte IsDarkestShadowOnly;
    }

    [Serializable]
    public class LiveTimelineKeyMultiLightShadowDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyData_MultiLightShadow> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyMobControlData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 Position;
        public Vector3 Angle;
        public Vector3 Scale;
    }

    [Serializable]
    public class LiveTimelineKeyMobControlDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMobControlData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMobControlData
    {
        public string name = "";
        public int GroupIndex;
        public LiveTimelineKeyMobControlDataList Keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyCyalumeControlData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public Vector3 Position;
        public Vector3 Angle;
        public Vector3 Scale;
    }

    [Serializable]
    public class LiveTimelineKeyCyalumeControlDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCyalumeControlData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineCyalumeControlData
    {
        public string name = "";
        public int GroupIndex;
        public LiveTimelineKeyCyalumeControlDataList Keys = new();
    }

    [Serializable]
    public class CySpringWindParam
    {
        public byte _isEnableVertical;
        public byte _isEnableHorizontal;
        public float _verticalCycle;
        public float _horizontalCycle;
        public float _verticalAngleWidth;
        public float _horizontalAngleWidth;
        public int _partsMask;
        public List<float> _powerScaleArray = new();
        public Vector3 _direction;
        public Vector3 _right;
        public byte _isLocalDirection;
    }

    [Serializable]
    public class LiveTimelineKeyCharaWind
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte IsEnableWind;
        public CySpringWindParam WindParam = new();
    }

    [Serializable]
    public class LiveTimelineKeyCharaWindDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCharaWind> thisList = new();
    }

    [Serializable]
    public class LiveTimelineCharaWindData
    {
        public LiveTimelineKeyCharaWindDataList centerKeys = new();
        public LiveTimelineKeyCharaWindDataList left1Keys = new();
        public LiveTimelineKeyCharaWindDataList right1Keys = new();
        public LiveTimelineKeyCharaWindDataList left2Keys = new();
        public LiveTimelineKeyCharaWindDataList right2Keys = new();
        public LiveTimelineKeyCharaWindDataList place06Keys = new();
        public LiveTimelineKeyCharaWindDataList place07Keys = new();
        public LiveTimelineKeyCharaWindDataList place08Keys = new();
        public LiveTimelineKeyCharaWindDataList place09Keys = new();
        public LiveTimelineKeyCharaWindDataList place10Keys = new();
        public LiveTimelineKeyCharaWindDataList place11Keys = new();
        public LiveTimelineKeyCharaWindDataList place12Keys = new();
        public LiveTimelineKeyCharaWindDataList place13Keys = new();
        public LiveTimelineKeyCharaWindDataList place14Keys = new();
        public LiveTimelineKeyCharaWindDataList place15Keys = new();
        public LiveTimelineKeyCharaWindDataList place16Keys = new();
        public LiveTimelineKeyCharaWindDataList place17Keys = new();
        public LiveTimelineKeyCharaWindDataList place18Keys = new();
        public LiveTimelineKeyCharaWindDataList place19Keys = new();
        public LiveTimelineKeyCharaWindDataList place20Keys = new();
    }

    [Serializable]
    public class TimelineCharaPartsData
    {
        public string _rendererName = "";
        public int _rendererHash;
        public byte IsVisible;
    }

    [Serializable]
    public class LiveTimelineKeyCharaParts
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public List<TimelineCharaPartsData> CharaPartsDataArray = new();
    }

    [Serializable]
    public class LiveTimelineKeyCharaPartsDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCharaParts> thisList = new();
    }

    [Serializable]
    public class LiveTimelineCharaPartsData
    {
        public LiveTimelineKeyCharaPartsDataList centerKeys = new();
        public LiveTimelineKeyCharaPartsDataList left1Keys = new();
        public LiveTimelineKeyCharaPartsDataList right1Keys = new();
        public LiveTimelineKeyCharaPartsDataList left2Keys = new();
        public LiveTimelineKeyCharaPartsDataList right2Keys = new();
        public LiveTimelineKeyCharaPartsDataList place06Keys = new();
        public LiveTimelineKeyCharaPartsDataList place07Keys = new();
        public LiveTimelineKeyCharaPartsDataList place08Keys = new();
        public LiveTimelineKeyCharaPartsDataList place09Keys = new();
        public LiveTimelineKeyCharaPartsDataList place10Keys = new();
        public LiveTimelineKeyCharaPartsDataList place11Keys = new();
        public LiveTimelineKeyCharaPartsDataList place12Keys = new();
        public LiveTimelineKeyCharaPartsDataList place13Keys = new();
        public LiveTimelineKeyCharaPartsDataList place14Keys = new();
        public LiveTimelineKeyCharaPartsDataList place15Keys = new();
        public LiveTimelineKeyCharaPartsDataList place16Keys = new();
        public LiveTimelineKeyCharaPartsDataList place17Keys = new();
        public LiveTimelineKeyCharaPartsDataList place18Keys = new();
        public LiveTimelineKeyCharaPartsDataList place19Keys = new();
        public LiveTimelineKeyCharaPartsDataList place20Keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyCharaCollisionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte IsEnable;
        public Vector3 Position;
        public Vector3 Angle;
        public int CollisionType;
        public float Radius;
        public Vector3 Offset;
        public Vector3 Offset2;
        public byte IsInner;
        public byte IsApplyHead;
        public byte IsApplyTail;
        public int CharacterFlag;
    }

    [Serializable]
    public class LiveTimelineKeyCharaCollisionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyCharaCollisionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineCharaCollisionData
    {
        public string name = "";
        public LiveTimelineKeyCharaCollisionDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineKeyEyeCameraPositionData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int setType;
        public Vector3 position;
        public Vector3 charaPos;
        public Vector3 CharaPositionAtStartFrame;
        public byte IsUseCharaPositionAtPrevKeyStartFrame;
        public List<Vector3> bezierPoints = new();
        public int charaRelativeBase;
        public int charaRelativeParts;
        public float traceSpeed;
        public float nearClip;
        public float farClip;
        public int cullingLayer;
        public int BgColorType;
        public Color BgColor;
        public int BgColorTargetCharacterIndex;
        public byte IsAttachedToProps;
        public int PropsIndex;
        public int PropsAttachNodeIndex;
        public byte IsEnabled;
        public float Power;
        public float Roll;
        public float Fov;
        public int CullingMask;
        public Texture2D MaskTexture;
    }

    [Serializable]
    public class LiveTimelineKeyEyeCameraPositionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyEyeCameraPositionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineEyeCameraPositionData
    {
        public string name = "";
        public LiveTimelineKeyEyeCameraPositionDataList keys = new();
        public int _characterIndex;
    }

    [Serializable]
    public class LiveTimelineKeyEyeCameraLookAtData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int lookAtType;
        public Vector3 position;
        public int lookAtCharaPos;
        public int lookAtCharaParts;
        public Vector3 charaPos;
        public Vector3 CharaPositionAtStartFrame;
        public byte IsUseCharaPositionAtPrevKeyStartFrame;
        public List<Vector3> bezierPoints = new();
        public float traceSpeed;
        public byte IsAttachedToProps;
        public int PropsIndex;
        public int PropsAttachNodeIndex;
    }

    [Serializable]
    public class LiveTimelineKeyEyeCameraLookAtDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyEyeCameraLookAtData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineEyeCameraLookAtData
    {
        public string name = "";
        public LiveTimelineKeyEyeCameraLookAtDataList keys = new();
    }

    [Serializable]
    public class OverrideCueIdData
    {
        public int Type;
        public int TargetId;
        public int OverrideCueIdId;
    }

    [Serializable]
    public class LiveTimelineKeyVoiceData
    {
        public int frame;
        public int attribute;
        public int CueId;
        public List<OverrideCueIdData> OverrideCueIdDataList = new();
    }

    [Serializable]
    public class LiveTimelineKeyVoiceDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyVoiceData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyTransparentCameraData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte Enable;
        public byte IsOrthographic;
        public byte IsDefaultTransform;
        public float OrthographicSize;
    }

    [Serializable]
    public class LiveTimelineKeyTransparentCameraDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyTransparentCameraData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaCameraData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte Enable;
        public float OrthographicSize;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaCameraDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMiniCharaCameraData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaTransformData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public byte Visible;
        public Vector3 Position;
        public Vector3 Rotate;
        public float BodyScale;
        public int LayerIndex;
        public byte IsLayout2d;
        public byte IsAttachChara;
        public int AttachIndex;
        public string AttachNodeName = "";
        public byte ShouldInheritParentScale;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaTransformDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMiniCharaTransformData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMiniCharaTransformData
    {
        public LiveTimelineKeyMiniCharaTransformDataList keys = new();
        public int _index;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaMotionData
    {
        public int frame;
        public int attribute;
        public AnimationClip BodyAnimationClip;
        public int BodyHeadFrame;
        public float BodyPlaySpeed;
        public UnityEngine.Object FacialDrivenKeyAnimation;
        public int FacialHeadFrame;
        public float FacialPlaySpeed;
        public UnityEngine.Object EarDrivenKeyAnimation;
        public int EarHeadFrame;
        public float EarPlaySpeed;
        public AnimationClip PositionAnimationClip;
        public int PositionHeadFrame;
        public float PositionPlaySpeed;
        public byte Loop;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaMotionDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMiniCharaMotionData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMiniCharaMotionData
    {
        public LiveTimelineKeyMiniCharaMotionDataList keys = new();
        public int _index;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaColorData
    {
        public int frame;
        public int attribute;
        public int interpolateType;
        public AnimationCurve curve = new();
        public int easingType;
        public int TargetFlags;
        public int ColorType;
        public Color Color;
        public float ColorPower;
        public float OutlineWidthPower;
        public Color OutlineColor;
        public int OutlineColorBlend;
        public byte IsSyncBlinkLight;
        public string BlinkLightName = "";
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public byte IsAdjustedBlinkLightColor;
        public int _loopType;
        public int _loopCount;
        public int _loopExecutedCount;
        public int _loopIntervalFrame;
        public byte _isChangeLoopInterpolate;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaColorDataList
    {
        public int _attribute;
        public int _playMode;
        public List<LiveTimelineKeyMiniCharaColorData> thisList = new();
    }

    [Serializable]
    public class LiveTimelineMiniCharaColorData
    {
        public string name = "";
        public LiveTimelineKeyMiniCharaColorDataList keys = new();
    }

    [Serializable]
    public class LiveTimelineWorkSheet : ScriptableObject
    {
        public string version = "";
        public int targetCameraIndex;
        public byte enableAtRuntime;
        public byte enableAtEdit;
        public float TotalTimeLength;
        public byte Lyrics;
        public int SheetType;
        public int SheetVariationId;
        public byte IsVariationSheet;
        public LiveTimelineKeyTimescaleDataList timescaleKeys = new();
        public LiveTimelineKeyCameraPositionDataList cameraPosKeys = new();
        public List<LiveTimelineMultiCameraPositionData> multiCameraPosKeys = new();
        public List<LiveTimelineMultiCameraLookAtData> multiCameraLookAtKeys = new();
        public LiveTimelineKeyCameraLookAtDataList cameraLookAtKeys = new();
        public LiveTimelineKeyCameraFovDataList cameraFovKeys = new();
        public LiveTimelineKeyCameraRollDataList cameraRollKeys = new();
        public LiveTimelineKeyPostEffectBloomDiffusionDataList postEffectBloomDiffusionKeys = new();
        public LiveTimelineKeyPostFilmDataList postFilmKeys = new();
        public List<LiveTimelineColorCorrectionData> colorCorrectionDataLists = new();
        public LiveTimelineKeyExposureDataList ExposureKeys = new();
        public List<LiveTimelineGlobalFogData> globalFogDataLists = new();
        public LiveTimelineKeyToneCurveDataList ToneCurveKeys = new();
        public List<LiveTimelineVolumeLightData> volumeLightKeys = new();
        public List<LiveTimelineChromaticAberrationData> chromaticAberrationList = new();
        public List<LiveTimelineAudienceData> audienceList = new();
        public List<LiveTimelineHdrBloomData> hdrBloomKeys = new();
        public List<LiveTimelineCharaMotSeqData> charaMotSeqList = new();
        public List<LiveTimelineAnimationData> animationList = new();
        public LiveTimelineKeyCameraSwitcherDataList cameraSwitcherKeys = new();
        public LiveTimelineKeyLipSyncDataList ripSyncKeys = new();
        public LiveTimelineKeyLipSyncDataList ripSync2Keys = new();
        public LiveTimelineFacialData facial1Set = new();
        public List<LiveTimelineFacialData> other4FacialArray = new();
        public LiveTimelineFormationOffsetData formationOffsetSet = new();
        public List<LiveTimelineGlobalLightData> globalLightDataLists = new();
        public List<LiveTimelineEnvironmentData> environmentDataLists = new();
        public List<LiveTimelineLensFlareData> lensFlareList = new();
        public List<LiveTimelineMirrorReflectionData> MirrorReflectionDataList = new();
        public List<LiveTimelineMonitorCameraPositionData> monitorCameraPosKeys = new();
        public LiveTimelineKeyFadeDataList fadeKeys = new();
        public List<LiveTimelineSweatLocatorData> sweatLocatorList = new();
        public LiveTimelineKeyCameraMotionDataList cameraMotionKeys = new();
        public LiveTimelineKeyHandShakeCameraDataList handShakeCameraKeys = new();
        public LiveTimelineKeyEventDataList eventKeys = new();
        public List<LiveTimelineBgColor1Data> bgColor1List = new();
        public List<LiveTimelineBgColor2Data> bgColor2List = new();
        public List<LiveTimelineMonitorControlData> monitorControlList = new();
        public List<LiveTimelineTextureAnimationData> textureAnimationList = new();
        public List<LiveTimelineTransformData> transformList = new();
        public List<LiveTimelineRendererData> rendererList = new();
        public List<LiveTimelineObjectData> objectList = new();
        public List<LiveTimelineWaveObjectData> waveObjectList = new();
        public List<LiveTimelinePropsData> propsList = new();
        public List<LiveTimelinePropsAttachData> propsAttachList = new();
        public List<LiveTimelineParentConstraintData> ParentConstraintDataList = new();
        public LiveTimelineKeyLipSyncPatternRangeDataList LipSyncPatternRangeKeys = new();
        public List<LiveTimelineLipSyncPatternData> LipSyncPatternDataList = new();
        public LiveTimelineKeyPostEffectDOFDataList postEffectDOFKeys = new();
        public LiveTimelineKeyRadialBlurDataList radialBlurKeys = new();
        public LiveTimelineKeyPostFilmDataList postFilm2Keys = new();
        public LiveTimelineKeyPostFilmDataList postFilm3Keys = new();
        public LiveTimelineKeyFluctuationDataList FluctuationKeys = new();
        public LiveTimelineKeyVortexDataList VortexKeys = new();
        public LiveTimelineKeyCameraLayerDataList cameraLayerKeys = new();
        public List<LiveTimelineProjectorData> projecterList = new();
        public LiveTimelineKeyFacialEyeTrackDataList other4EyeTrackKeys = new();
        public LiveTimelineKeyTransmittedLightDataList TransmittedLightKeys = new();
        public List<LiveTimelineTransmittedLightMaskData> transmittedLightMaskDataList = new();
        public LiveTimelineKeyLensDistortionDataList LensDistortionKeys = new();
        public LiveTimelineKeyScreenCaptureDataList ScreenCaptureDataList = new();
        public LiveTimelineKeyTailMotionDataList tailMotionDataList = new();
        public LiveTimelineKeyFacialNoiseDataList facialNoiseKeys = new();
        public LiveTimelineKeyCharaMotionNoiseDataList charaMotionNoiseKeys = new();
        public List<LiveTimelineParticleData> particleList = new();
        public List<LiveTimelineParticleGroupData> particleGroupList = new();
        public List<LiveTimelineWashLightData> WashLightList = new();
        public List<LiveTimelineLaserData> laserList = new();
        public List<LiveTimelineBlinkLightData> blinkLightList = new();
        public List<LiveTimelineUVScrollLightData> uvScrollLightList = new();
        public LiveTimelineFacialToonData facialToonSet = new();
        public List<LiveTimelinePreColorCorrectionData> preColorCorrectionDataLists = new();
        public List<LiveTimelineLightShaftsData> lightShaftsKeysLine = new();
        public List<LiveTimelineMonitorCameraLayerData> monitorCameraLayerKeys = new();
        public List<LiveTimelineMonitorCameraLookAtData> monitorCameraLookAtKeys = new();
        public List<LiveTimelineMultiCameraLayerData> multiCameraLayerKeys = new();
        public List<LiveTimelineEffectData> effectList = new();
        public List<LiveTimelineContactEffectData> ContactEffectDataList = new();
        public List<LiveTimelineRayHitEffectData> RayHitEffectDataList = new();
        public LiveTimelineKeyTiltShiftDataList tiltShiftKeys = new();
        public LiveTimelineKeyHatchingDataList hatchingKeys = new();
        public LiveTimelineKeyEdgeStyleDataList edgeStyleKeys = new();
        public LiveTimelineKeyFlashPlayerDataList flashPlayerKeys = new();
        public LiveTimelineKeyTitleDataList titleKeys = new();
        public List<LiveTimelineSpotlight3dData> spotlight3dList = new();
        public List<LiveTimelineCharaNodeData> CharaNodeDataList = new();
        public List<LiveTimelineNodeScaleData> nodeScaleList = new();
        public List<LiveTimelineCharaNodeOffsetData> CharaNodeOffsetDataList = new();
        public LiveTimelineKeyCharaFootLightDataList charaFootLightKeys = new();
        public List<LiveTimelineLightProjectionData> lightProjectionList = new();
        public List<LiveTimelineBillboardData> billboardList = new();
        public List<LiveTimelineMultiCameraPostFilmData> postFilm1MultiCameraKeys = new();
        public List<LiveTimelineMultiCameraPostFilmData> postFilm2MultiCameraKeys = new();
        public List<LiveTimelineMultiCameraPostFilmData> postFilm3MultiCameraKeys = new();
        public List<LiveTimelineMultiCameraPostEffectBloomDiffusionData> postEffectBloomDiffusionMultiCameraKeys = new();
        public List<LiveTimelineMultiCameraColorCorrectionData> multiCameraColorCorrectionDataLists = new();
        public List<LiveTimelineMultiCameraTiltShiftData> multiCameraTiltShiftDataLists = new();
        public List<LiveTimelineMultiCameraRadialBlurData> multiCameraRadialBlurDataLists = new();
        public List<LiveTimelineMultiCameraPostEffectDOFData> postEffectDOFMultiCameraKeys = new();
        public List<LiveTimelineMultiCameraTransmittedLightData> MultiCameraTransmittedLightDataList = new();
        public List<LiveTimelineAdditionalLight> AdditionalLightList = new();
        public LiveTimelineKeyMultiLightShadowDataList MultiLightShadowKeys = new();
        public List<LiveTimelineMobControlData> MobControlKeys = new();
        public List<LiveTimelineCyalumeControlData> CyalumeControlKeys = new();
        public LiveTimelineCharaWindData charaWind = new();
        public LiveTimelineCharaPartsData CharaPartsKeys = new();
        public List<LiveTimelineCharaCollisionData> CharaCollisionDataList = new();
        public List<LiveTimelineEyeCameraPositionData> EyeCameraPosList = new();
        public List<LiveTimelineEyeCameraLookAtData> EyeCameraLookAtList = new();
        public LiveTimelineKeyVoiceDataList VoiceKeys = new();
        public LiveTimelineKeyTransparentCameraDataList TransparentCameraKeys = new();
        public LiveTimelineKeyMiniCharaCameraDataList MiniCharaCameraKeys = new();
        public List<LiveTimelineMiniCharaTransformData> MiniCharaTransformDataList = new();
        public List<LiveTimelineMiniCharaMotionData> MiniCharaMotionDataList = new();
        public List<LiveTimelineMiniCharaColorData> MiniCharaColorDataList = new();
    }
}
