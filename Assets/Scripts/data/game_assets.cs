using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UV2.Data
{
    // stream wrapper that xors bytes at position >= 256 with the per-file key, so
    // AssetBundle.LoadFromStream reads the game's encrypted bundles directly.
    public class decrypt_stream : FileStream
    {
        private readonly byte[] _fkey;

        // key = the meta db column e for this file; zero means the bundle is not encrypted.
        public decrypt_stream(string path, long key)
            : base(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        {
            if (key != 0)
            {
                byte[] ab_key = { 0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7, 0xB9, 0x47, 0x3E, 0x7C, 0xFB };
                byte[] kb = System.BitConverter.GetBytes(key);
                _fkey = new byte[88];
                for (int i = 0; i < 11; i++)
                    for (int j = 0; j < 8; j++)
                        _fkey[i * 8 + j] = (byte)(ab_key[i] ^ kb[j]);
            }
            else
            {
                _fkey = null;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);
            if (_fkey != null)
            {
                for (int i = 0; i < read; i++)
                {
                    long pos = Position - read + i;
                    if (pos >= 256)
                        buffer[offset + i] ^= _fkey[(int)(pos % 88)];
                }
            }
            return read;
        }
    }

    // loads named assets out of the game's own install: meta row -> decrypt stream -> AssetBundle.
    public static class game_assets
    {
        // already-loaded bundles by hash: unity refuses to load the same bundle file
        // twice, and shared prereqs (materials, ikcols) load repeatedly across a cast.
        private static readonly Dictionary<string, AssetBundle> loaded = new();

        // opens the bundle for one manifest row, reusing an already-loaded instance.
        public static AssetBundle open(meta_reader.asset_row row, string data_root)
        {
            if (loaded.TryGetValue(row.hash, out var existing) && existing != null)
                return existing;
            string path = Path.Combine(data_root, "dat", row.hash.Substring(0, 2), row.hash);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[game_assets] bundle file missing on disk: {path}");
                return null;
            }
            try
            {
                var stream = new decrypt_stream(path, row.key);
                var bundle = AssetBundle.LoadFromStream(stream);
                if (bundle == null)
                {
                    Debug.LogWarning($"[game_assets] LoadFromStream returned null for {row.name} (hash {row.hash}, key {row.key})");
                    return null;
                }
                loaded[row.hash] = bundle;
                return bundle;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[game_assets] open failed for {row.name}: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        // convenience: open, load one TextAsset, unload.
        public static string load_text(meta_reader.asset_row row, string data_root, string asset_name)
        {
            var bundle = open(row, data_root);
            if (bundle == null) return null;
            try
            {
                if (!bundle.Contains(asset_name)) return null;
                var text = bundle.LoadAsset<TextAsset>(asset_name);
                return text != null ? text.text : null;
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        // convenience: open, load one Texture2D; bundle stays loaded so the texture stays valid.
        public static Texture2D load_texture(meta_reader.asset_row row, string data_root, string asset_name)
        {
            var bundle = open(row, data_root);
            if (bundle == null) return null;
            if (!bundle.Contains(asset_name))
            {
                var names = bundle.GetAllAssetNames();
                Debug.LogWarning($"[game_assets] {asset_name} not in bundle {row.name}; contains: {string.Join(", ", names)}");
                bundle.Unload(true);
                return null;
            }
            var tex = bundle.LoadAsset<Texture2D>(asset_name);
            if (tex == null)
            {
                bundle.Unload(true);
                return null;
            }
            // kept loaded: unloading the bundle would destroy the texture handed to the ui.
            return tex;
        }
    }
}
