using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Debug = UnityEngine.Debug;

namespace UV2.Data
{
    // reads the encrypted meta db to resolve asset names to bundle rows.
    public static class meta_reader
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const string DLL = "sqlite3mc_x64";
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private const string DLL = "sqlite3mc_mac";
#else
        private const string DLL = "sqlite3mc";
#endif

        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;
        private const int SQLITE_OPEN_READONLY = 0x00000001;

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_open_v2")]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_key")]
        private static extern int sqlite3_key(IntPtr db, byte[] key, int key_len);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_prepare_v2")]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int byte_len, out IntPtr stmt, IntPtr tail);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_step")]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_count")]
        private static extern int sqlite3_column_count(IntPtr stmt);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_int64")]
        private static extern long sqlite3_column_int64(IntPtr stmt, int col);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_text")]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_finalize")]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_close")]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_errmsg")]
        private static extern IntPtr sqlite3_errmsg(IntPtr db);

        // one manifest row: where a named asset lives and how it is keyed.
        public sealed class asset_row
        {
            public string name;
            public string hash;
            public long length;
            public long key;
            public string prereq;
        }

        // db handle wrapper so callers can do many lookups without reopening the file.
        public sealed class reader : IDisposable
        {
            private IntPtr _db;

            public static reader open(string meta_path)
            {
                if (!System.IO.File.Exists(meta_path))
                {
                    Debug.LogError($"[meta_reader] meta db not found: {meta_path}");
                    return null;
                }
                int rc = sqlite3_open_v2(utf8_z(meta_path), out IntPtr db, SQLITE_OPEN_READONLY, IntPtr.Zero);
                if (rc != SQLITE_OK)
                {
                    Debug.LogError($"[meta_reader] open failed ({rc}): {meta_path}");
                    return null;
                }
                var key = final_meta_key();
                sqlite3_key(db, key, key.Length);
                return new reader { _db = db };
            }

            public Dictionary<string, asset_row> lookup(HashSet<string> names)
            {
                var result = new Dictionary<string, asset_row>();
                foreach (var name in names)
                {
                    string sql = $"SELECT n, h, l, e, d FROM a WHERE n = '{name}'";
                    byte[] sql_bytes = utf8_z(sql);
                    int rc = sqlite3_prepare_v2(_db, sql_bytes, sql_bytes.Length, out IntPtr stmt, IntPtr.Zero);
                    if (rc != SQLITE_OK)
                    {
                        Debug.LogError($"[meta_reader] prepare failed for {name}: {marshal_string(sqlite3_errmsg(_db))}");
                        continue;
                    }
                    if (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        var row = new asset_row
                        {
                            name = marshal_string(sqlite3_column_text(stmt, 0)),
                            hash = marshal_string(sqlite3_column_text(stmt, 1)),
                            length = sqlite3_column_int64(stmt, 2),
                            key = sqlite3_column_int64(stmt, 3),
                            prereq = marshal_string(sqlite3_column_text(stmt, 4))
                        };
                        if (!string.IsNullOrEmpty(row.hash))
                            result[name] = row;
                    }
                    sqlite3_finalize(stmt);
                }
                Debug.Log($"[meta_reader] resolved {result.Count} of {names.Count} requested asset names");
                return result;
            }

            public void Dispose()
            {
                if (_db != IntPtr.Zero)
                {
                    sqlite3_close(_db);
                    _db = IntPtr.Zero;
                }
            }
        }

        // key material for the meta db, same scheme the game uses (XOR of db key over the 13-byte base key).
        private static byte[] final_meta_key()
        {
            byte[] db_key =
            {
                0x6D, 0x5B, 0x65, 0x33, 0x63, 0x36, 0x63, 0x25, 0x54, 0x71, 0x2D, 0x73,
                0x50, 0x53, 0x63, 0x38, 0x6D, 0x34, 0x37, 0x7B, 0x35, 0x63, 0x70, 0x23,
                0x37, 0x34, 0x53, 0x29, 0x73, 0x43, 0x36, 0x33
            };
            byte[] base_key =
            {
                0xF1, 0x70, 0xCE, 0xA4, 0xDF, 0xCE, 0xA3, 0xE1, 0xA5, 0xD8, 0xC7, 0x0B, 0xD1
            };
            var final = new byte[db_key.Length];
            for (int i = 0; i < db_key.Length; i++)
                final[i] = (byte)(db_key[i] ^ base_key[i % 13]);
            return final;
        }

        // utf8 bytes with an explicit trailing nul so native reads never run past the array.
        private static byte[] utf8_z(string s)
        {
            byte[] body = Encoding.UTF8.GetBytes(s);
            var buf = new byte[body.Length + 1];
            Array.Copy(body, buf, body.Length);
            return buf;
        }

        private static string marshal_string(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            var buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }
    }
}
