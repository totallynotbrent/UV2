using System;
using System.Collections.Generic;
using UnityEngine;

// generated from the game's own typetree node dump: field names, types and
// order match the serialized format exactly, so bundle data binds with no
// extraction step.
namespace Gallop
{
    [Serializable]
    public class AssetTableValue
    {
        public List<UnityEngine.Object> list = new();
    }

    [Serializable]
    public class AssetTableEntry
    {
        public string Key = "";
        public GameObject Value;
    }

    [Serializable]
    public class AssetTable
    {
        public List<AssetTableEntry> list = new();
    }

    // the crowd controller's asset table: the prefabs the cyalume spawner
    // instantiates at runtime (default/random/mob keys on the pen-light rig).
    [Serializable]
    public class AssetHolder : MonoBehaviour
    {
        public AssetTable _assetTable = new();
        public AssetTableValue _assetTableValue = new();
    }
}
