using System.Collections.Generic;
using UnityEngine;

// =============================================================
// 掛在要被切的方塊（CutTarget_Cube）上
// 執行時會自動在方塊表面畫上透明九宮格，
// 並在每一個格線交叉點放一個貼在表面上的點點（不是浮空的球）
//
// 切開之後，切出來的每一塊也會自動帶著自己那一部分的格線和點點
// （包含剛好落在切面上的點），所以可以一直切下去
// =============================================================
[DisallowMultipleComponent]
public class GridCutTarget : MonoBehaviour
{
    [Header("九宮格設定")]
    [Tooltip("每一面分成幾格（3 = 九宮格，交叉點 4x4 = 16 個）")]
    [Range(1, 6)]
    [SerializeField] private int divisions = 3;

    [Tooltip("格線粗細（相對於方塊邊長）")]
    [SerializeField] private float lineWidthRatio = 0.012f;

    [Tooltip("點點半徑（相對於方塊邊長）")]
    [SerializeField] private float dotRadiusRatio = 0.035f;

    [Tooltip("點點可以被選取的範圍（相對於方塊邊長，比點點大一點比較好點）")]
    [SerializeField] private float dotColliderRatio = 0.11f;

    [Tooltip("格線離表面的距離（非常小，只是避免和方塊表面閃爍）")]
    [SerializeField] private float surfaceOffsetRatio = 0.0015f;

    [Header("材質（留空 = 使用 GridCutController 上的設定）")]
    [SerializeField] private Material gridLineMaterial;
    [SerializeField] private Material dotMaterial;
    [SerializeField] private Material dotHoverMaterial;
    [SerializeField] private Material dotSelectedMaterial;

    public int Divisions => divisions;

    // 整個原始方塊的範圍（格子座標的基準）
    public Bounds LocalBounds { get; private set; }
    public float Size { get; private set; }
    public float PointEpsilon => Mathf.Max(Size, 1e-4f) * 1e-3f;

    public Material DotMaterial => dotMaterial;
    public Material DotHoverMaterial => dotHoverMaterial;
    public Material DotSelectedMaterial => dotSelectedMaterial;

    private readonly List<GridPoint> points = new List<GridPoint>();
    private bool built;

    // 從上一塊繼承來的資料（切出來的小塊才會有）
    private bool hasLattice;
    private Bounds latticeBounds;

    // 切過的面：xyz = 往外的法向量，w = d；留下來的部分滿足 dot(n, p) <= d
    private readonly List<Vector4> clips = new List<Vector4>();

    // 這一塊實際形狀的頂點（方塊自己的座標系）
    private Vector3[] hullVertices;
    private int[] hullTriangles;

    // 六個面：+X -X +Y -Y +Z -Z
    private static readonly Vector3[] FaceNormals =
    {
        Vector3.right, Vector3.left,
        Vector3.up, Vector3.down,
        Vector3.forward, Vector3.back
    };

    private void Start()
    {
        Build();
    }

    public void Build()
    {
        if (built)
        {
            return;
        }

        MeshFilter mf = GetComponent<MeshFilter>();

        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("GridCutTarget：找不到 MeshFilter，無法建立九宮格");
            return;
        }

        if (!hasLattice)
        {
            latticeBounds = mf.sharedMesh.bounds;
            hasLattice = true;
        }

        LocalBounds = latticeBounds;

        Vector3 s = LocalBounds.size;
        Size = Mathf.Min(s.x, Mathf.Min(s.y, s.z));

        if (Size <= 0f)
        {
            return;
        }

        hullVertices = mf.sharedMesh.vertices;
        hullTriangles = mf.sharedMesh.triangles;

        ResolveMaterials();

        GameObject root = new GameObject("GridOverlay");
        root.transform.SetParent(transform, false);

        BuildLines(root.transform);
        BuildDots(root.transform);

        built = true;
    }

    // =========================================================
    // 切開後：把格子交給切出來的兩塊
    // =========================================================
    public static void InheritToPieces(
        GameObject original,
        GameObject pieceA,
        GameObject pieceB,
        Vector3 worldPlanePoint,
        Vector3 worldPlaneNormal)
    {
        if (original == null)
        {
            return;
        }

        GridCutTarget src = original.GetComponent<GridCutTarget>();

        if (src == null)
        {
            return;
        }

        src.EnsureLattice();

        Transform t = original.transform;

        // 平面轉到方塊自己的座標系（切出來的小塊一開始和原本的座標系一樣）
        Vector3 pLocal = t.InverseTransformPoint(worldPlanePoint);
        Vector3 nLocal = t.localToWorldMatrix.transpose.MultiplyVector(worldPlaneNormal).normalized;
        float d = Vector3.Dot(nLocal, pLocal);

        CreateChild(src, pieceA, nLocal, d);
        CreateChild(src, pieceB, nLocal, d);
    }

    private static void CreateChild(GridCutTarget src, GameObject piece, Vector3 n, float d)
    {
        if (piece == null)
        {
            return;
        }

        MeshFilter mf = piece.GetComponent<MeshFilter>();

        if (mf == null || mf.sharedMesh == null)
        {
            return;
        }

        // 用這一塊的中心判斷它在切面的哪一邊
        Vector3 centroid = Vector3.zero;
        Vector3[] verts = mf.sharedMesh.vertices;

        foreach (Vector3 v in verts)
        {
            centroid += v;
        }

        if (verts.Length > 0)
        {
            centroid /= verts.Length;
        }

        bool onPositiveSide = Vector3.Dot(n, centroid) - d > 0f;

        // 往外的法向量要指向「被切掉的那一邊」
        Vector4 clip = onPositiveSide
            ? new Vector4(-n.x, -n.y, -n.z, -d)
            : new Vector4(n.x, n.y, n.z, d);

        GridCutTarget child = piece.GetComponent<GridCutTarget>();

        if (child == null)
        {
            child = piece.AddComponent<GridCutTarget>();
        }

        child.divisions = src.divisions;
        child.lineWidthRatio = src.lineWidthRatio;
        child.dotRadiusRatio = src.dotRadiusRatio;
        child.dotColliderRatio = src.dotColliderRatio;
        child.surfaceOffsetRatio = src.surfaceOffsetRatio;

        child.gridLineMaterial = src.gridLineMaterial;
        child.dotMaterial = src.dotMaterial;
        child.dotHoverMaterial = src.dotHoverMaterial;
        child.dotSelectedMaterial = src.dotSelectedMaterial;

        child.hasLattice = true;
        child.latticeBounds = src.latticeBounds;

        child.clips.Clear();
        child.clips.AddRange(src.clips);
        child.clips.Add(clip);
    }

    private void EnsureLattice()
    {
        if (hasLattice)
        {
            return;
        }

        MeshFilter mf = GetComponent<MeshFilter>();

        if (mf != null && mf.sharedMesh != null)
        {
            latticeBounds = mf.sharedMesh.bounds;
            hasLattice = true;
        }
    }

    // =========================================================
    // 透明九宮格：只畫格線（切掉的部分不畫）
    // =========================================================
    private void BuildLines(Transform parent)
    {
        List<Vector3> v = new List<Vector3>();
        List<int> t = new List<int>();

        Vector3 c = LocalBounds.center;
        Vector3 e = LocalBounds.extents;

        float lw = lineWidthRatio * Size;
        float off = surfaceOffsetRatio * Size;

        for (int f = 0; f < 6; f++)
        {
            GetFaceAxes(f, out Vector3 n, out Vector3 ea, out Vector3 eb, out int k, out int a, out int b);

            Vector3 faceCenter = c + n * e[k];

            for (int i = 0; i <= divisions; i++)
            {
                float tt = -1f + 2f * i / divisions;

                // 沿著 b 方向的線
                Vector3 p0 = faceCenter + ea * (tt * e[a]) - eb * e[b];
                Vector3 p1 = faceCenter + ea * (tt * e[a]) + eb * e[b];
                AddClippedLine(v, t, p0, p1, n, ea, lw, off);

                // 沿著 a 方向的線
                Vector3 q0 = faceCenter + eb * (tt * e[b]) - ea * e[a];
                Vector3 q1 = faceCenter + eb * (tt * e[b]) + ea * e[a];
                AddClippedLine(v, t, q0, q1, n, eb, lw, off);
            }
        }

        if (v.Count == 0)
        {
            return;
        }

        GameObject go = new GameObject("GridLines");
        go.transform.SetParent(parent, false);

        go.AddComponent<MeshFilter>().sharedMesh =
            GridCutMeshUtil.BuildMesh("GridLines", v, t);

        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = gridLineMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // 一條格線（p0→p1）先裁掉被切走的部分，再畫成貼在面上的細長條
    private void AddClippedLine(
        List<Vector3> v, List<int> t,
        Vector3 p0, Vector3 p1,
        Vector3 normal, Vector3 widthAxis,
        float lineWidth, float offset)
    {
        if (!ClipSegment(ref p0, ref p1))
        {
            return;
        }

        Vector3 d = p1 - p0;
        float len = d.magnitude;

        if (len < Size * 1e-4f)
        {
            return;
        }

        Vector3 dir = d / len;

        GridCutMeshUtil.AddQuad(
            v, t,
            (p0 + p1) * 0.5f + normal * offset,
            dir * (len * 0.5f + lineWidth * 0.5f),
            widthAxis * (lineWidth * 0.5f),
            normal);
    }

    private bool ClipSegment(ref Vector3 p0, ref Vector3 p1)
    {
        float eps = Size * 1e-4f;

        foreach (Vector4 c in clips)
        {
            Vector3 n = new Vector3(c.x, c.y, c.z);

            float f0 = Vector3.Dot(n, p0) - c.w;
            float f1 = Vector3.Dot(n, p1) - c.w;

            if (f0 > eps && f1 > eps)
            {
                return false;
            }

            if (f0 > eps || f1 > eps)
            {
                float tt = f0 / (f0 - f1);
                Vector3 cut = p0 + (p1 - p0) * tt;

                if (f0 > eps)
                {
                    p0 = cut;
                }
                else
                {
                    p1 = cut;
                }
            }
        }

        return true;
    }

    // =========================================================
    // 點點：每一個格子交叉點，只要在這一塊的表面上就放一個
    // =========================================================
    private void BuildDots(Transform parent)
    {
        Vector3 mn = LocalBounds.min;
        Vector3 sz = LocalBounds.size;

        float off = surfaceOffsetRatio * Size;
        float colliderSize = dotColliderRatio * Size;
        float colliderThickness = Mathf.Max(off * 6f, Size * 0.02f);
        float eps = PointEpsilon;

        Mesh disc = GridCutMeshUtil.CreateDisc(dotRadiusRatio * Size, 20);

        List<Vector3> normals = new List<Vector3>();

        for (int i = 0; i <= divisions; i++)
        {
            for (int j = 0; j <= divisions; j++)
            {
                for (int k = 0; k <= divisions; k++)
                {
                    Vector3 p = new Vector3(
                        mn.x + sz.x * i / divisions,
                        mn.y + sz.y * j / divisions,
                        mn.z + sz.z * k / divisions);

                    if (!IsInsideClips(p))
                    {
                        continue;
                    }

                    GetSurfaceNormals(p, eps, normals);

                    // 在裡面、不在表面上的點不用放
                    foreach (Vector3 n in normals)
                    {
                        CreateDot(parent, disc, p, n, off, colliderSize, colliderThickness);
                    }
                }
            }
        }
    }

    private void CreateDot(
        Transform parent, Mesh disc,
        Vector3 p, Vector3 n,
        float off, float colliderSize, float colliderThickness)
    {
        Vector3 up = Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up;

        GameObject dot = new GameObject("Dot");
        dot.transform.SetParent(parent, false);
        dot.transform.localPosition = p + n * (off * 2f);
        dot.transform.localRotation = Quaternion.LookRotation(n, up);

        dot.AddComponent<MeshFilter>().sharedMesh = disc;

        MeshRenderer r = dot.AddComponent<MeshRenderer>();
        r.sharedMaterial = dotMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        BoxCollider bc = dot.AddComponent<BoxCollider>();
        bc.size = new Vector3(colliderSize, colliderSize, colliderThickness);
        bc.center = Vector3.zero;

        GridPoint gp = dot.AddComponent<GridPoint>();
        gp.Init(this, p);

        points.Add(gp);
    }

    // 這個點在哪些面上（原本方塊的面 + 切出來的面），回傳往外的法向量
    private void GetSurfaceNormals(Vector3 p, float eps, List<Vector3> result)
    {
        result.Clear();

        Vector3 c = LocalBounds.center;
        Vector3 e = LocalBounds.extents;

        for (int f = 0; f < 6; f++)
        {
            GetFaceAxes(f, out Vector3 n, out _, out _, out int k, out _, out _);

            float plane = c[k] + (n[k] > 0f ? e[k] : -e[k]);

            if (Mathf.Abs(p[k] - plane) <= eps)
            {
                result.Add(n);
            }
        }

        foreach (Vector4 clip in clips)
        {
            Vector3 n = new Vector3(clip.x, clip.y, clip.z);

            if (Mathf.Abs(Vector3.Dot(n, p) - clip.w) <= eps)
            {
                result.Add(n);
            }
        }
    }

    private bool IsInsideClips(Vector3 p)
    {
        float eps = PointEpsilon;

        foreach (Vector4 c in clips)
        {
            if (Vector3.Dot(new Vector3(c.x, c.y, c.z), p) - c.w > eps)
            {
                return false;
            }
        }

        return true;
    }

    private void ResolveMaterials()
    {
        GridCutController ctrl = GridCutController.Instance;

        if (gridLineMaterial == null && ctrl != null) gridLineMaterial = ctrl.GridLineMaterial;
        if (dotMaterial == null && ctrl != null) dotMaterial = ctrl.DotMaterial;
        if (dotHoverMaterial == null && ctrl != null) dotHoverMaterial = ctrl.DotHoverMaterial;
        if (dotSelectedMaterial == null && ctrl != null) dotSelectedMaterial = ctrl.DotSelectedMaterial;

        if (gridLineMaterial == null) gridLineMaterial = GridCutController.CreateFallbackMaterial(GridCutController.DefaultGridLineColor);
        if (dotMaterial == null) dotMaterial = GridCutController.CreateFallbackMaterial(GridCutController.DefaultDotColor);
        if (dotHoverMaterial == null) dotHoverMaterial = GridCutController.CreateFallbackMaterial(GridCutController.DefaultDotHoverColor);
        if (dotSelectedMaterial == null) dotSelectedMaterial = GridCutController.CreateFallbackMaterial(GridCutController.DefaultDotSelectedColor);
    }

    // =========================================================
    // 給 GridCutController 用的查詢
    // =========================================================

    public void SetPointSelected(Vector3 localPosition, bool value)
    {
        float eps = PointEpsilon;

        foreach (GridPoint gp in points)
        {
            if (gp != null &&
                (gp.LocalPosition - localPosition).sqrMagnitude <= eps * eps)
            {
                gp.SetSelected(value);
            }
        }
    }

    public void ClearSelectionVisuals()
    {
        foreach (GridPoint gp in points)
        {
            if (gp != null)
            {
                gp.SetSelected(false);
            }
        }
    }

    // 兩個點同時所在的面（回傳往外的法向量）
    public List<Vector3> GetCommonFaceNormals(Vector3 p, Vector3 q)
    {
        List<Vector3> a = new List<Vector3>();
        List<Vector3> b = new List<Vector3>();
        List<Vector3> result = new List<Vector3>();

        float eps = PointEpsilon;

        GetSurfaceNormals(p, eps, a);
        GetSurfaceNormals(q, eps, b);

        foreach (Vector3 na in a)
        {
            foreach (Vector3 nb in b)
            {
                if ((na - nb).sqrMagnitude < 1e-6f)
                {
                    result.Add(na);
                    break;
                }
            }
        }

        return result;
    }

    // 這個平面有沒有真的把這一塊切成兩塊（不是只碰到表面）
    public bool PlaneCutsThrough(Vector3 point, Vector3 normal)
    {
        if (hullVertices == null || hullVertices.Length == 0)
        {
            return false;
        }

        Vector3 n = normal.normalized;

        float min = float.MaxValue;
        float max = float.MinValue;

        foreach (Vector3 v in hullVertices)
        {
            float d = Vector3.Dot(n, v - point);
            min = Mathf.Min(min, d);
            max = Mathf.Max(max, d);
        }

        float tol = Size * 1e-3f;

        return max > tol && min < -tol;
    }

    // 平面和這一塊相交的切面形狀（依順序排好的多邊形頂點）
    public List<Vector3> GetSectionPolygon(Vector3 point, Vector3 normal)
    {
        List<Vector3> result = new List<Vector3>();

        if (hullVertices == null || hullTriangles == null)
        {
            return result;
        }

        Vector3 n = normal.normalized;
        float eps = Size * 1e-5f;
        float mergeEps = Size * 1e-3f;

        for (int i = 0; i + 2 < hullTriangles.Length; i += 3)
        {
            for (int e = 0; e < 3; e++)
            {
                Vector3 a = hullVertices[hullTriangles[i + e]];
                Vector3 b = hullVertices[hullTriangles[i + (e + 1) % 3]];

                float da = Vector3.Dot(n, a - point);
                float db = Vector3.Dot(n, b - point);

                if (Mathf.Abs(da) <= eps)
                {
                    AddUnique(result, a, mergeEps);
                }

                if ((da > eps && db < -eps) || (da < -eps && db > eps))
                {
                    AddUnique(result, a + (b - a) * (da / (da - db)), mergeEps);
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

    // 把點轉成格子座標，例如 (0,3,1)，方便記錄和給 AI 看
    public Vector3Int ToGridCoord(Vector3 localPosition)
    {
        Vector3 mn = LocalBounds.min;
        Vector3 sz = LocalBounds.size;

        return new Vector3Int(
            Mathf.RoundToInt((localPosition.x - mn.x) / sz.x * divisions),
            Mathf.RoundToInt((localPosition.y - mn.y) / sz.y * divisions),
            Mathf.RoundToInt((localPosition.z - mn.z) / sz.z * divisions));
    }

    private static void GetFaceAxes(
        int face,
        out Vector3 normal,
        out Vector3 axisA,
        out Vector3 axisB,
        out int k,
        out int a,
        out int b)
    {
        k = face / 2;
        a = (k + 1) % 3;
        b = (k + 2) % 3;

        normal = FaceNormals[face];
        axisA = Axis(a);
        axisB = Axis(b);
    }

    private static Vector3 Axis(int i)
    {
        if (i == 0) return Vector3.right;
        if (i == 1) return Vector3.up;
        return Vector3.forward;
    }
}
