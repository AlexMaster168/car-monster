using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CarMonster.EditorTools
{
    /// <summary>
    /// Генерирует физическую дорогу по сплайну: меш с MeshCollider (асфальт, поребрики, борта, разметка),
    /// виражи в поворотах, разрывы под пропасти, отбойники в поворотах и на эстакадах, опоры под эстакадами.
    /// </summary>
    public static class RoadBuilder
    {
        public struct Sample
        {
            public float d;
            public Vector3 c;
            public Vector3 fwd;
            public Vector3 right;
            public Vector3 up;
            public float curvature;
            public bool gap;
        }

        private const float CurbWidth = 1.2f;
        private const float SkirtDepth = 1.2f;

        public static List<Sample> SampleRoad(WaypointCircuit circuit, float bankAngle, Vector2[] gaps, float step = 3f)
        {
            var list = new List<Sample>();
            float length = circuit.Length;
            int count = Mathf.CeilToInt(length / step);
            for (int i = 0; i <= count; i++)
            {
                float d = Mathf.Min(i * step, length);
                Vector3 fwd = circuit.GetDirection(d);
                Vector3 flatFwd = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, flatFwd).normalized;
                float signed = Vector3.SignedAngle(
                    Vector3.ProjectOnPlane(circuit.GetDirection(d - 8f), Vector3.up),
                    Vector3.ProjectOnPlane(circuit.GetDirection(d + 8f), Vector3.up), Vector3.up);
                // Правый поворот → внешний (левый) край выше.
                float bank = Mathf.Clamp(signed / 20f, -1f, 1f) * bankAngle;
                Vector3 rolledRight = Quaternion.AngleAxis(-bank, fwd) * right;
                Vector3 up = Vector3.Cross(fwd, rolledRight).normalized;

                bool gap = false;
                for (int g = 0; g < gaps.Length; g++)
                    if (d > gaps[g].x && d < gaps[g].y) gap = true;

                list.Add(new Sample
                {
                    d = d,
                    c = circuit.GetPoint(d) + Vector3.up * 0.06f,
                    fwd = fwd,
                    right = rolledRight,
                    up = up,
                    curvature = signed,
                    gap = gap
                });
            }
            return list;
        }

        public static GameObject BuildRoadMesh(Transform parent, List<Sample> s, float halfWidth,
            Material asphalt, Material curbA, Material curbB, Material line, string assetPath)
        {
            var verts = new List<Vector3>(s.Count * 6 + 400);
            var uvs = new List<Vector2>(s.Count * 6 + 400);
            var tAsphalt = new List<int>();
            var tCurbA = new List<int>();
            var tCurbB = new List<int>();
            var tLine = new List<int>();

            for (int i = 0; i < s.Count; i++)
            {
                Sample p = s[i];
                Vector3 r = p.right;
                verts.Add(p.c - r * (halfWidth + CurbWidth));     // 0 left outer
                verts.Add(p.c - r * halfWidth);                    // 1 left edge
                verts.Add(p.c + r * halfWidth);                    // 2 right edge
                verts.Add(p.c + r * (halfWidth + CurbWidth));     // 3 right outer
                verts.Add(verts[verts.Count - 4] - p.up * SkirtDepth); // 4 left bottom
                verts.Add(verts[verts.Count - 2] - p.up * SkirtDepth); // 5 right bottom
                float v = p.d / 10f;
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(0.1f, v));
                uvs.Add(new Vector2(0.9f, v));
                uvs.Add(new Vector2(1f, v));
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
            }

            for (int i = 0; i < s.Count - 1; i++)
            {
                if (s[i].gap || s[i + 1].gap) continue;
                int a = i * 6;
                int b = (i + 1) * 6;
                Quad(tAsphalt, a + 1, a + 2, b + 2, b + 1);
                List<int> curb = (i / 2) % 2 == 0 ? tCurbA : tCurbB;
                Quad(curb, a + 0, a + 1, b + 1, b + 0);
                Quad(curb, a + 2, a + 3, b + 3, b + 2);
                // Борта: слева смотрят наружу (-right), справа (+right).
                tAsphalt.Add(b + 0); tAsphalt.Add(a + 0); tAsphalt.Add(a + 4);
                tAsphalt.Add(b + 0); tAsphalt.Add(a + 4); tAsphalt.Add(b + 4);
                tAsphalt.Add(a + 3); tAsphalt.Add(b + 3); tAsphalt.Add(b + 5);
                tAsphalt.Add(a + 3); tAsphalt.Add(b + 5); tAsphalt.Add(a + 5);
                // Низ (видно с эстакад снизу).
                Quad(tAsphalt, b + 4, b + 5, a + 5, a + 4);
            }

            // Пунктир по центру.
            for (int i = 0; i < s.Count - 2; i += 4)
            {
                if (s[i].gap || s[i + 2].gap) continue;
                Vector3 up = s[i].up * 0.015f;
                int k = verts.Count;
                verts.Add(s[i].c - s[i].right * 0.18f + up);
                verts.Add(s[i].c + s[i].right * 0.18f + up);
                verts.Add(s[i + 2].c + s[i + 2].right * 0.18f + up);
                verts.Add(s[i + 2].c - s[i + 2].right * 0.18f + up);
                for (int u = 0; u < 4; u++) uvs.Add(Vector2.zero);
                Quad(tLine, k, k + 1, k + 2, k + 3);
            }

            var mesh = new Mesh { name = "RoadMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 4;
            mesh.SetTriangles(tAsphalt, 0);
            mesh.SetTriangles(tCurbA, 1);
            mesh.SetTriangles(tCurbB, 2);
            mesh.SetTriangles(tLine, 3);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            CarRoster.EnsureFolder(System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(mesh, assetPath);

            var go = new GameObject("RoadMesh");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { asphalt, curbA, curbB, line };
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
            return go;
        }

        /// <summary>Отбойники: внешний край поворотов и оба края эстакад.</summary>
        public static int BuildBarriers(Transform parent, List<Sample> s, float halfWidth, Material a, Material b,
            float cornerThreshold = 7f, float elevatedThreshold = 2.5f)
        {
            var root = new GameObject("Barriers");
            root.transform.SetParent(parent, false);
            int count = 0;
            for (int i = 0; i < s.Count - 1; i++)
            {
                Sample p = s[i];
                if (p.gap || s[i + 1].gap) continue;
                bool corner = Mathf.Abs(p.curvature) > cornerThreshold;
                bool elevated = p.c.y > elevatedThreshold;
                if (!corner && !elevated) continue;

                bool left = elevated || p.curvature > 0f;   // правый поворот → стена слева
                bool right = elevated || p.curvature < 0f;
                Material m = (i / 2) % 2 == 0 ? a : b;
                Vector3 mid = (p.c + s[i + 1].c) * 0.5f;
                Vector3 dir = (s[i + 1].c - p.c).normalized;
                Quaternion rot = Quaternion.LookRotation(dir, p.up);
                float len = Vector3.Distance(p.c, s[i + 1].c) + 0.15f;
                float offset = halfWidth + CurbWidth + 0.35f;

                if (left) count += Wall(root.transform, mid - p.right * offset + p.up * 0.55f, rot, len, m);
                if (right) count += Wall(root.transform, mid + p.right * offset + p.up * 0.55f, rot, len, m);
            }
            return count;
        }

        private static int Wall(Transform parent, Vector3 pos, Quaternion rot, float length, Material m)
        {
            GameObject w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = "Barrier";
            w.transform.SetParent(parent, false);
            w.transform.SetPositionAndRotation(pos, rot);
            w.transform.localScale = new Vector3(0.6f, 1.1f, length);
            w.GetComponent<Renderer>().sharedMaterial = m;
            w.isStatic = true;
            return 1;
        }

        /// <summary>Опоры под эстакадами; groundAt(x,z) — высота земли под точкой.</summary>
        public static void BuildPillars(Transform parent, List<Sample> s, float halfWidth, Material m, Func<Vector3, float> groundAt)
        {
            var root = new GameObject("Pillars");
            root.transform.SetParent(parent, false);
            for (int i = 0; i < s.Count; i += 6)
            {
                Sample p = s[i];
                if (p.gap) continue;
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 top = p.c + p.right * (side * (halfWidth - 1f)) - p.up * SkirtDepth;
                    float ground = groundAt(top);
                    float h = top.y - ground;
                    if (h < 1.2f) continue;
                    GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pillar.name = "Pillar";
                    UnityEngine.Object.DestroyImmediate(pillar.GetComponent<Collider>());
                    pillar.transform.SetParent(root.transform, false);
                    pillar.transform.position = new Vector3(top.x, ground + h * 0.5f, top.z);
                    pillar.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(p.fwd, Vector3.up));
                    pillar.transform.localScale = new Vector3(1.4f, h, 1.4f);
                    pillar.GetComponent<Renderer>().sharedMaterial = m;
                    pillar.isStatic = true;
                }
            }
        }

        private static void Quad(List<int> t, int a, int b, int c, int d)
        {
            // a,b — ближний край (слева→направо), c,d — дальний (справа→налево). Лицом вверх.
            t.Add(a); t.Add(d); t.Add(c);
            t.Add(a); t.Add(c); t.Add(b);
        }
    }
}
