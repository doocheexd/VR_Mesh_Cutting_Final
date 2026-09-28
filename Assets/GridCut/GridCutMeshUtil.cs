using System.Collections.Generic;
using UnityEngine;

// =============================================================
// 九宮格切割用的小工具：產生格線、點點、虛線、切面外框的 Mesh
// 全部都是在「方塊自己的座標系」裡計算
// =============================================================
public static class GridCutMeshUtil
{
    // 加一個長方形（center 為中心，halfA / halfB 為兩個方向的半長度）
    // normal 決定哪一面是正面（看得到的那一面）
    public static void AddQuad(
        List<Vector3> verts,
        List<int> tris,
        Vector3 center,
        Vector3 halfA,
        Vector3 halfB,
        Vector3 normal)
    {
        int i = verts.Count;

        verts.Add(center - halfA - halfB);
        verts.Add(center + halfA - halfB);
        verts.Add(center + halfA + halfB);
        verts.Add(center - halfA + halfB);

        bool facesNormal =
            Vector3.Dot(Vector3.Cross(halfA, halfB), normal) >= 0f;

        if (facesNormal)
        {
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }
        else
        {
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }
    }

    // 加一根細長方柱（從 a 到 b），從任何角度看都看得到
    public static void AddRod(
        List<Vector3> verts,
        List<int> tris,
        Vector3 a,
        Vector3 b,
        float thickness)
    {
        Vector3 d = b - a;
        float len = d.magnitude;

        if (len < 1e-6f)
        {
            return;
        }

        Vector3 dir = d / len;

        Vector3 reference =
            Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right;

        Vector3 p1 = Vector3.Cross(dir, reference).normalized;
        Vector3 p2 = Vector3.Cross(dir, p1).normalized;

        float h = thickness * 0.5f;
        float half = len * 0.5f;
        Vector3 mid = (a + b) * 0.5f;

        // 四個側面
        AddQuad(verts, tris, mid + p1 * h, dir * half, p2 * h, p1);
        AddQuad(verts, tris, mid - p1 * h, dir * half, p2 * h, -p1);
        AddQuad(verts, tris, mid + p2 * h, dir * half, p1 * h, p2);
        AddQuad(verts, tris, mid - p2 * h, dir * half, p1 * h, -p2);

        // 兩個端點
        AddQuad(verts, tris, mid + dir * half, p1 * h, p2 * h, dir);
        AddQuad(verts, tris, mid - dir * half, p1 * h, p2 * h, -dir);
    }

    // 虛線：一段一段的小方柱
    public static void AddDashedLine(
        List<Vector3> verts,
        List<int> tris,
        Vector3 a,
        Vector3 b,
        float thickness,
        float dashLength,
        float gapLength)
    {
        Vector3 d = b - a;
        float len = d.magnitude;

        if (len < 1e-6f || dashLength <= 0f)
        {
            return;
        }

        Vector3 dir = d / len;
        float step = dashLength + Mathf.Max(gapLength, 0f);

        for (float s = 0f; s < len; s += step)
        {
            float e = Mathf.Min(s + dashLength, len);
            AddRod(verts, tris, a + dir * s, a + dir * e, thickness);
        }
    }

    // 平面的圓形點點（正面朝 +Z）
    public static Mesh CreateDisc(float radius, int segments)
    {
        segments = Mathf.Max(segments, 6);

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        verts.Add(Vector3.zero);

        for (int i = 0; i < segments; i++)
        {
            float ang = (float)i / segments * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * radius);
        }

        for (int i = 0; i < segments; i++)
        {
            int cur = 1 + i;
            int next = 1 + (i + 1) % segments;

            // cross(cur, next) 指向 +Z → 正面朝 +Z
            tris.Add(0);
            tris.Add(cur);
            tris.Add(next);
        }

        Mesh mesh = new Mesh();
        mesh.name = "GridDot";
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    public static Mesh BuildMesh(
        string name,
        List<Vector3> verts,
        List<int> tris)
    {
        Mesh mesh = new Mesh();
        mesh.name = name;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    // 平面和方塊相交的切面形狀（依順序排好的多邊形頂點）
    public static List<Vector3> PlaneBoxPolygon(
        Bounds box,
        Vector3 planePoint,
        Vector3 planeNormal)
    {
        List<Vector3> result = new List<Vector3>();

        Vector3 n = planeNormal.normalized;
        Vector3 mn = box.min;
        Vector3 mx = box.max;

        Vector3[] c = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            c[i] = new Vector3(
                (i & 1) == 0 ? mn.x : mx.x,
                (i & 2) == 0 ? mn.y : mx.y,
                (i & 4) == 0 ? mn.z : mx.z);
        }

        float size = Mathf.Max(box.size.magnitude, 1e-6f);
        float eps = size * 1e-5f;
        float mergeEps = size * 1e-4f;

        // 12 條邊：兩個角只差一個軸
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit <= 4; bit <<= 1)
            {
                int j = i | bit;

                if (j == i)
                {
                    continue;
                }

                float da = Vector3.Dot(n, c[i] - planePoint);
                float db = Vector3.Dot(n, c[j] - planePoint);

                if (Mathf.Abs(da) <= eps)
                {
                    AddUnique(result, c[i], mergeEps);
                }

                if (Mathf.Abs(db) <= eps)
                {
                    AddUnique(result, c[j], mergeEps);
                }

                if ((da > eps && db < -eps) || (da < -eps && db > eps))
                {
                    float t = da / (da - db);
                    AddUnique(result, c[i] + (c[j] - c[i]) * t, mergeEps);
                }
            }
        }

        if (result.Count < 3)
        {
            return result;
        }

        Vector3 centroid = Vector3.zero;
        foreach (Vector3 p in result)
        {
            centroid += p;
        }
        centroid /= result.Count;

        Vector3 u = (result[0] - centroid).normalized;
        Vector3 w = Vector3.Cross(n, u);

        result.Sort((p, q) =>
        {
            float ap = Mathf.Atan2(Vector3.Dot(p - centroid, w), Vector3.Dot(p - centroid, u));
            float aq = Mathf.Atan2(Vector3.Dot(q - centroid, w), Vector3.Dot(q - centroid, u));
            return ap.CompareTo(aq);
        });

        return result;
    }

    private static void AddUnique(List<Vector3> list, Vector3 p, float eps)
    {
        foreach (Vector3 q in list)
        {
            if ((q - p).sqrMagnitude <= eps * eps)
            {
                return;
            }
        }

        list.Add(p);
    }
}
