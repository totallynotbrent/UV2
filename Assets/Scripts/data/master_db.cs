using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UV2.Data
{
    // minimal sqlite3 reader over plain master.mdb via the bundled native sqlite.
    public static class master_db
    {
        private const string DLL = "sqlite3";

        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;
        private const int SQLITE_OPEN_READONLY = 0x00000001;

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_open_v2")]
        private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPStr)] string filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_prepare_v2")]
        private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPStr)] string sql, int byte_len, out IntPtr stmt, IntPtr tail);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_step")]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_count")]
        private static extern int sqlite3_column_count(IntPtr stmt);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_int64")]
        private static extern long sqlite3_column_int64(IntPtr stmt, int col);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_text")]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_type")]
        private static extern int sqlite3_column_type(IntPtr stmt, int col);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_finalize")]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_close")]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_errmsg")]
        private static extern IntPtr sqlite3_errmsg(IntPtr db);

        // row of raw sqlite cells; string cells keep their column index for utf8 reads.
        public sealed class row
        {
            private readonly long[] _ints;
            private readonly string[] _texts;

            public row(long[] ints, string[] texts)
            {
                _ints = ints;
                _texts = texts;
            }

            public long get_int(int col) => _ints[col];
            public string get_text(int col) => _texts[col];
        }

        public sealed class reader : IDisposable
        {
            private IntPtr _db;
            public string last_error { get; private set; }

            public static reader open(string path)
            {
                if (!File.Exists(path))
                    return null;

                int rc = sqlite3_open_v2(path, out IntPtr db, SQLITE_OPEN_READONLY, IntPtr.Zero);
                if (rc != SQLITE_OK)
                    return null;

                var r = new reader { _db = db };
                return r;
            }

            public List<row> query(string sql)
            {
                var rows = new List<row>();
                int rc = sqlite3_prepare_v2(_db, sql, -1, out IntPtr stmt, IntPtr.Zero);
                if (rc != SQLITE_OK)
                {
                    last_error = marshal_string(sqlite3_errmsg(_db));
                    Debug.LogError($"[master_db] prepare failed ({rc}): {last_error} -- {sql}");
                    return rows;
                }

                int ncols = sqlite3_column_count(stmt);
                while (true)
                {
                    rc = sqlite3_step(stmt);
                    if (rc == SQLITE_DONE) break;
                    if (rc != SQLITE_ROW)
                    {
                        last_error = marshal_string(sqlite3_errmsg(_db));
                        Debug.LogError($"[master_db] step failed ({rc}): {last_error} -- {sql}");
                        break;
                    }

                    var ints = new long[ncols];
                    var texts = new string[ncols];
                    for (int c = 0; c < ncols; c++)
                    {
                        int t = sqlite3_column_type(stmt, c);
                        if (t == 1 || t == 2) ints[c] = sqlite3_column_int64(stmt, c);
                        else if (t == 5) texts[c] = null;
                        else texts[c] = marshal_string(sqlite3_column_text(stmt, c));
                    }
                    rows.Add(new row(ints, texts));
                }

                sqlite3_finalize(stmt);
                return rows;
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

        private static string marshal_string(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            byte[] buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return System.Text.Encoding.UTF8.GetString(buf);
        }
    }
}
