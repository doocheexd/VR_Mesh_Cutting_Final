using System.Collections.Generic;
using UnityEngine;

// =============================================================
// 掛在要被切的方塊（CutTarget_Cube）上
// 執行時會自動在方塊的 6 個面畫上透明九宮格，
// 並在每一個格線交叉點放一個貼在表面上的點點（不是浮空的球）
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
    public Bounds LocalBounds { get; private set; }
    public float Size { get; private set; }
    public float PointEpsilon => Mathf.Max(Size, 1e-4f) * 1e-3f;

    public Material DotMaterial => dotMaterial;
    public Material DotHoverMaterial => dotHoverMaterial;
    public Material DotSelectedMaterial => dotSelectedMaterial;

    private readonly List<GridPoint> points = new List<GridPoint>();
    private bool built;

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

        LocalBounds = mf.sharedMesh.bounds;

        Vector3 s = LocalBounds.size;
        Size = Mathf.Min(s.x, Mathf.Min(s.y, s.z));

        if (Size <= 0f)
        {
            return;
        }

        ResolveMaterials();

        GameObject root = new GameObject("GridOverlay");
        root.transform.SetParent(transform, false);

        BuildLines(root.transform);
        BuildDots(root.transform);

        built = true;
    }

    // =========================================================
    // 透明九宮格：只畫格線，其餘部分是透明的
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
                GridCutMeshUtil.AddQuad(
                    v, t,
                    faceCenter + ea * (tt * e[a]) + n * off,
                    ea * (lw * 0.5f),
                    eb * (e[b] + lw * 0.5f),
                    n);

                // 沿著 a 方向的線
                GridCutMeshUtil.AddQuad(
                    v, t,
                    faceCenter + eb * (tt * e[b]) + n * off,
                    ea * (e[a] + lw * 0.5f),
                    eb * (lw * 0.5f),
                    n);
            }
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

    // =========================================================
    // 點點：每一個格線交叉點一個，平貼在表面上
    // =========================================================
    private void BuildDots(Transform parent)
    {
        Vector3 c = LocalBounds.center;
        Vector3 e = LocalBounds.extents;

        float off = surfaceOffsetRatio * Size;
        float colliderSize = dotColliderRatio * Size;
        float colliderThickness = Mathf.Max(off * 6f, Size * 0.02f);

        Mesh disc = GridCutMeshUtil.CreateDisc(dotRadiusRatio * Size, 20);

        for (int f = 0; f < 6; f++)
        {
            GetFaceAxes(f, out Vector3 n, out Vector3 ea, out Vector3 eb, out int k, out int a, out int b);

            Vector3 faceCenter = c + n * e[k];
            Vector3 up = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;

            for (int i = 0; i <= divisions; i++)
            {
                for (int j = 0; j <= divisions; j++)
                {
                    float ti = -1f + 2f * i / divisions;
                    float tj = -1f + 2f * j / divisions;

                    // 點在表面上的精確位置
                    Vector3 p =
                        faceCenter +
                        ea * (ti * e[a]) +
                        eb * (tj * e[b]);

                    GameObject dot = new GameObject("Dot_F" + f + "_" + i + "_" + j);
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
            }
        }
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

    // 兩個點同時所在的面（回傳面的法向量）
    public List<Vector3> GetCommonFaceNormals(Vector3 p, Vector3 q)
    {
        List<Vector3> result = new List<Vector3>();

        Vector3 c = LocalBounds.center;
        Vector3 e = LocalBounds.extents;
        float eps = PointEpsilon;

        for (int f = 0; f < 6; f++)
        {
            GetFaceAxes(f, out Vector3 n, out _, out _, out int k, out _, out _);

            float plane = c[k] + (n[k] > 0f ? e[k] : -e[k]);

            if (Mathf.Abs(p[k] - plane) <= eps &&
                Mathf.Abs(q[k] - plane) <= eps)
            {
                result.Add(n);
            }
        }

        return result;
    }

    // 這個平面有沒有真的把方塊切成兩塊（不是只碰到表面）
    public bool PlaneCutsThrough(Vector3 point, Vector3 normal)
    {
        Vector3 n = normal.normalized;
        Vector3 mn = LocalBounds.min;
        Vector3 mx = LocalBounds.max;

        float min = float.MaxValue;
        float max = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? mn.x : mx.x,
                (i & 2) == 0 ? mn.y : mx.y,
                (i & 4) == 0 ? mn.z : mx.z);

            float d = Vector3.Dot(n, corner - point);
            min = Mathf.Min(min, d);
            max = Mathf.Max(max, d);
        }

        float tol = Size * 1e-4f;

        return max > tol && min < -tol;
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
