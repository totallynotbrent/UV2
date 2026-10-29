using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // lays the song's pen light sprites across the crowd's meshes. the game
    // ships those meshes without any uv channel and its crowd controller
    // writes the sprite placement into them at run time, so without this
    // step the pattern sheet has nothing to land on and the audience stays
    // unlit.
    //
    // the sheets hold a lattice of ten light sprites across and thirty-two
    // down, measured from the song's own pattern textures.
    public static class cyalume_spread
    {
        public const int SHEET_COLUMNS = 10;
        public const int SHEET_ROWS = 32;

        public static int spread(Transform root)
        {
            if (root == null) return 0;
            var shader = Shader.Find("live/uv2_cyalume");
            if (shader == null)
            {
                trace_log.write("cyalume spread: live/uv2_cyalume shader is missing");
                return 0;
            }
            int spread_count = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string name = r.gameObject.name;
                if (!(name.StartsWith("cyalume_r") || name.StartsWith("cyalume_d"))) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                if (Apply(filter.sharedMesh, shader, r)) spread_count++;
            }
            trace_log.write($"cyalume spread: {spread_count} mesh(es) given sprite coordinates");
            return spread_count;
        }

        private static bool Apply(Mesh source, Shader shader, Renderer renderer)
        {
            Mesh mesh = Object.Instantiate(source);
            mesh.name = source.name + "_spread";
            var triangles = mesh.triangles;
            var vertices = mesh.vertices;
            if (triangles == null || vertices == null || triangles.Length < 6) return false;

            var local = new Vector2[vertices.Length];
            var cell = new Vector2[vertices.Length];
            var phase = new Vector2[vertices.Length];
            bool any = false;
            for (int t = 0; t + 5 < triangles.Length; t += 6)
            {
                int i0 = triangles[t];
                int i1 = triangles[t + 1];
                int i2 = triangles[t + 2];
                int i3 = triangles[t + 4];
                if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length || i3 >= vertices.Length) continue;

                Vector3 centre = (vertices[i0] + vertices[i1] + vertices[i2] + vertices[i3]) * 0.25f;
                Basis basis = Basis.Compute(vertices[i0], vertices[i1], vertices[i2], vertices[i3]);
                if (!basis.valid) continue;

                // every light in the crowd wears a sprite from the sheet and
                // a phase of its own, so neighbours do not march in step.
                int column;
                int row;
                float light_phase;
                PickCell(centre, out column, out row, out light_phase);

                local[i0] = Local(vertices[i0], basis, 0);
                local[i1] = Local(vertices[i1], basis, 1);
                local[i2] = Local(vertices[i2], basis, 2);
                local[i3] = Local(vertices[i3], basis, 3);
                cell[i0] = cell[i1] = cell[i2] = cell[i3] = new Vector2(column, row);
                phase[i0] = phase[i1] = phase[i2] = phase[i3] = new Vector2(light_phase, 0f);
                any = true;
            }
            if (!any) return false;

            mesh.uv = local;
            mesh.SetUVs(1, cell);
            mesh.SetUVs(2, phase);

            var filter = renderer.GetComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var material = renderer.material;
            material.shader = shader;
            return true;
        }

        // the sprite a light wears, chosen from where the light stands.
        private static void PickCell(Vector3 centre, out int column, out int row, out float light_phase)
        {
            unchecked
            {
                int h = (int)(centre.x * 7919f) ^ (int)(centre.z * 104729f) ^ (int)(centre.y * 1299709f);
                h ^= h >> 13;
                h *= 1274126177;
                h ^= h >> 16;
                if (h < 0) h = -h;
                column = h % SHEET_COLUMNS;
                row = (h / SHEET_COLUMNS) % SHEET_ROWS;
                light_phase = ((h / (SHEET_COLUMNS * SHEET_ROWS)) % 1000) / 1000f;
            }
        }

        // where a corner sits inside its own sprite.
        private static Vector2 Local(Vector3 vertex, Basis basis, int order_hint)
        {
            float fu;
            float fv;
            if (basis.max_u - basis.min_u > 1e-6f)
                fu = (vertex[basis.axis_u] - basis.min_u) / (basis.max_u - basis.min_u);
            else fu = 0.5f;
            if (basis.max_v - basis.min_v > 1e-6f)
                fv = (vertex[basis.axis_v] - basis.min_v) / (basis.max_v - basis.min_v);
            else fv = 0.5f;
            return new Vector2(Mathf.Clamp01(fu), Mathf.Clamp01(fv));
        }

        // the two axes the quad lies in, taken from the mesh's own shape.
        private struct Basis
        {
            public bool valid;
            public int axis_u;
            public int axis_v;
            public float min_u;
            public float max_u;
            public float min_v;
            public float max_v;

            public static Basis Compute(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var basis = new Basis();
                Vector3 va = b - a;
                Vector3 vb = c - a;
                Vector3 normal = Vector3.Cross(va, vb);
                if (normal.sqrMagnitude < 1e-12f) return basis;

                Vector3 extents = new Vector3(
                    Mathf.Max(Mathf.Abs(va.x), Mathf.Abs(vb.x)),
                    Mathf.Max(Mathf.Abs(va.y), Mathf.Abs(vb.y)),
                    Mathf.Max(Mathf.Abs(va.z), Mathf.Abs(vb.z)));
                extents.x = Mathf.Max(extents.x, Mathf.Abs(d.x - a.x));
                extents.y = Mathf.Max(extents.y, Mathf.Abs(d.y - a.y));
                extents.z = Mathf.Max(extents.z, Mathf.Abs(d.z - a.z));

                // the shortest axis is the quad's normal, the other two carry
                // the sprite's shape.
                int normal_axis = 0;
                float shortest = extents.x;
                if (extents.y < shortest) { shortest = extents.y; normal_axis = 1; }
                if (extents.z < shortest) { shortest = extents.z; normal_axis = 2; }

                int u_axis = (normal_axis + 1) % 3;
                int v_axis = (normal_axis + 2) % 3;

                float min_u = float.MaxValue, max_u = float.MinValue;
                float min_v = float.MaxValue, max_v = float.MinValue;
                foreach (var v in new[] { a, b, c, d })
                {
                    min_u = Mathf.Min(min_u, v[u_axis]); max_u = Mathf.Max(max_u, v[u_axis]);
                    min_v = Mathf.Min(min_v, v[v_axis]); max_v = Mathf.Max(max_v, v[v_axis]);
                }
                basis.valid = true;
                basis.axis_u = u_axis;
                basis.axis_v = v_axis;
                basis.min_u = min_u;
                basis.max_u = max_u;
                basis.min_v = min_v;
                basis.max_v = max_v;
                return basis;
            }
        }
    }
}
