using System.Collections.Generic;
using UnityEngine;
using System;
using Object = UnityEngine.Object;

[Serializable]
public class AssetTable
{
    public Object this[string key]
    {
        get 
        {
            var item = list.Find(a => a.Key.Equals(key));
            if(item != null)
            {
                return item.Value;
            }
            else
            {
                return null;
            }
        }
    }

    public List<StringObjectPair> list;
}


[Serializable]
public class AssetTableValue
{
    public float this[string key]
    {
        get
        {
            var item = list.Find(a => a.Key.Equals(key));
            if (item != null)
            {
                return item.Value;
            }
            else
            {
                return -1;
            }
        }
    }
    public List<StringValuePair> list;
}


[Serializable]
public class StringObjectPair
{
    public string Key;
    public Object Value;
}

[Serializable]
public class StringValuePair
{
    public string Key;
    public float Value;
}

[Serializable]
public class TrsArray
{
    public string _path;
    public Transform transform;
    public bool _isValidScaleTransform;
    public Vector3 _position;
    public Vector3 _scale;
    public Vector3 _rotation;
    public bool IsOverrideTarget;

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }
}
[Serializable]
public class ConnectedBoneData
{
    public string Bone1;
    public string Bone2;
    public float Intensity;
    public bool IsFold;
}
