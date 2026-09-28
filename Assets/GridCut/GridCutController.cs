using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// =============================================================
// 九宮格點點切割的總控制（場景裡放一個就好）
//
// 操作方式：
//   1. 手把射線指到方塊上的點點，按握把(Grip) 選取；再按一次可取消
//   2. 選 2 個「同一面」上的點 → 沿著這條線、垂直那一面往下切
//      選 3 個點 → 用三個點決定的平面切（可以斜切）
//   3. 預覽：紅色虛線 = 選的點連線，綠色框 = 切開後的切面形狀
//   4. 右手 A 鍵 確認切割；左手 Y 鍵 清除重選
//   （Editor 測試：滑鼠左鍵點點點、Enter 確認、Backspace 清除）
// =============================================================
[DefaultExecutionOrder(-50)]
public class GridCutController : MonoBehaviour
{
    public static GridCutController Instance { get; private set; }

    public static readonly Color DefaultGridLineColor = new Color(0.15f, 0.22f, 0.32f);
    public static readonly Color DefaultDotColor = new Color(1f, 0.6f, 0.1f);
    public static readonly Color DefaultDotHoverColor = new Color(1f, 0.92f, 0.2f);
    public static readonly Color DefaultDotSelectedColor = new Color(0.9f, 0.12f, 0.12f);
    public static readonly Color DefaultDashColor = new Color(0.95f, 0.2f, 0.2f);
    public static readonly Color DefaultOutlineColor = new Color(0.1f, 0.8f, 0.3f);

    [Header("連結")]
    [SerializeField] private MeshCutter meshCutter;
    [SerializeField] private AITutorManager aiTutorManager;

    [Header("提示訊息")]
    [Tooltip("選點時把操作提示顯示在 AI 黑板上")]
    [SerializeField] private bool showMessagesOnBoard = true;

    [Header("按鍵（Input System 綁定路徑）")]
    [SerializeField]
    private string[] confirmBindings =
    {
        "<XRController>{RightHand}/primaryButton",   // 右手 A
        "<Keyboard>/enter"
    };

    [SerializeField]
    private string[] clearBindings =
    {
        "<XRController>{LeftHand}/secondaryButton",  // 左手 Y
        "<Keyboard>/backspace"
    };

    [Tooltip("在 Unity Editor 裡可以用滑鼠左鍵點點點（方便沒戴頭盔時測試）")]
    [SerializeField] private bool enableMouseClickInEditor = true;

    [Header("材質")]
    [SerializeField] private Material gridLineMaterial;
    [SerializeField] private Material dotMaterial;
    [SerializeField] private Material dotHoverMaterial;
    [SerializeField] private Material dotSelectedMaterial;
    [SerializeField] private Material previewDashMaterial;
    [SerializeField] private Material previewOutlineMaterial;

    [Header("預覽線粗細（相對於方塊邊長）")]
    [SerializeField] private float dashThicknessRatio = 0.018f;
    [SerializeField] private float dashLengthRatio = 0.06f;
    [SerializeField] private float dashGapRatio = 0.04f;
    [SerializeField] private float outlineThicknessRatio = 0.014f;

    public Material GridLineMaterial => gridLineMaterial;
    public Material DotMaterial => dotMaterial;
    public Material DotHoverMaterial => dotHoverMaterial;
    public Material DotSelectedMaterial => dotSelectedMaterial;

    private const int MaxPoints = 3;

    private GridCutTarget target;
    private readonly List<Vector3> selected = new List<Vector3>();

    private bool planeValid;
    private Vector3 planeA;
    private Vector3 planeB;
    private Vector3 planeC;

    private GameObject previewRoot;

    private InputAction confirmAction;
    private InputAction clearAction;

    // 避免同一次按壓被算兩次
    private GridCutTarget lastPressedTarget;
    private Vector3 lastPressedPosition;
    private float lastPressedTime = -10f;

    // =========================================================
    // 生命週期
    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("場景裡有兩個 GridCutController，多的會被移除");
            Destroy(this);
            return;
        }

        Instance = this;

        if (gridLineMaterial == null) gridLineMaterial = CreateFallbackMaterial(DefaultGridLineColor);
        if (dotMaterial == null) dotMaterial = CreateFallbackMaterial(DefaultDotColor);
        if (dotHoverMaterial == null) dotHoverMaterial = CreateFallbackMaterial(DefaultDotHoverColor);
        if (dotSelectedMaterial == null) dotSelectedMaterial = CreateFallbackMaterial(DefaultDotSelectedColor);
        if (previewDashMaterial == null) previewDashMaterial = CreateFallbackMaterial(DefaultDashColor);
        if (previewOutlineMaterial == null) previewOutlineMaterial = CreateFallbackMaterial(DefaultOutlineColor);

        if (meshCutter == null)
        {
            meshCutter = FindFirstObjectByType<MeshCutter>();
        }

        if (aiTutorManager == null)
        {
            aiTutorManager = FindFirstObjectByType<AITutorManager>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnEnable()
    {
        confirmAction = BuildAction("GridCutConfirm", confirmBindings);
        confirmAction.performed += OnConfirmPerformed;
        confirmAction.Enable();

        clearAction = BuildAction("GridCutClear", clearBindings);
        clearAction.performed += OnClearPerformed;
        clearAction.Enable();
    }

    private void OnDisable()
    {
        if (confirmAction != null)
        {
            confirmAction.performed -= OnConfirmPerformed;
            confirmAction.Disable();
            confirmAction.Dispose();
            confirmAction = null;
        }

        if (clearAction != null)
        {
            clearAction.performed -= OnClearPerformed;
            clearAction.Disable();
            clearAction.Dispose();
            clearAction = null;
        }
    }

    private static InputAction BuildAction(string name, string[] bindings)
    {
        InputAction action = new InputAction(name, InputActionType.Button);

        if (bindings != null)
        {
            foreach (string b in bindings)
            {
                if (!string.IsNullOrEmpty(b))
                {
                    action.AddBinding(b);
                }
            }
        }

        return action;
    }

    private void OnConfirmPerformed(InputAction.CallbackContext context)
    {
        ConfirmCut();
    }

    private void OnClearPerformed(InputAction.CallbackContext context)
    {
        if (selected.Count > 0)
        {
            ClearSelection(true);
        }
    }

    private void Update()
    {
        // 方塊被 Reset 刪掉、或被舊的切割板切掉 → 清掉選取
        if (selected.Count > 0 &&
            (target == null || !target.gameObject.activeInHierarchy))
        {
            ClearSelection(false);
        }

#if UNITY_EDITOR
        if (enableMouseClickInEditor &&
            Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            Camera cam = Camera.main;

            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

                if (Physics.Raycast(ray, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore))
                {
                    GridPoint gp = hit.collider.GetComponent<GridPoint>();

                    if (gp != null)
                    {
                        gp.Press();
                    }
                }
            }
        }
#endif
    }

    // =========================================================
    // 選點
    // =========================================================
    public void OnPointPressed(GridPoint point)
    {
        if (point == null || point.Owner == null)
        {
            return;
        }

        GridCutTarget owner = point.Owner;

        if (owner == lastPressedTarget &&
            (point.LocalPosition - lastPressedPosition).sqrMagnitude <= owner.PointEpsilon * owner.PointEpsilon &&
            Time.unscaledTime - lastPressedTime < 0.25f)
        {
            return;
        }

        lastPressedTarget = owner;
        lastPressedPosition = point.LocalPosition;
        lastPressedTime = Time.unscaledTime;

        // 換了一個方塊 → 重新開始
        if (target != owner)
        {
            ClearSelection(false);
            target = owner;
        }

        int index = IndexOfSelected(point.LocalPosition);

        if (index >= 0)
        {
            selected.RemoveAt(index);
            target.SetPointSelected(point.LocalPosition, false);

            Log("GridPointDeselect", DescribePoint(point.LocalPosition));
        }
        else
        {
            if (selected.Count >= MaxPoints)
            {
                ShowMessage(
                    "最多只能選 3 個點。\n" +
                    "按右手 A 鍵確認切割，\n" +
                    "或按左手 Y 鍵清除重選。");
                return;
            }

            selected.Add(point.LocalPosition);
            target.SetPointSelected(point.LocalPosition, true);

            Log("GridPointSelect", DescribePoint(point.LocalPosition));
        }

        RefreshPreview(true);
    }

    private int IndexOfSelected(Vector3 p)
    {
        float eps = target != null ? target.PointEpsilon : 1e-4f;

        for (int i = 0; i < selected.Count; i++)
        {
            if ((selected[i] - p).sqrMagnitude <= eps * eps)
            {
                return i;
            }
        }

        return -1;
    }

    // =========================================================
    // 由選的點決定切割平面（方塊自己的座標系）
    // =========================================================
    private bool TryBuildPlane(out Vector3 a, out Vector3 b, out Vector3 c, out string message)
    {
        a = b = c = Vector3.zero;

        if (target == null || selected.Count == 0)
        {
            message = "請用手把選擇方塊上的點點。";
            return false;
        }

        if (selected.Count == 1)
        {
            message =
                "已選 1 個點。\n" +
                "再選一個點來畫出切割線。";
            return false;
        }

        a = selected[0];
        b = selected[1];

        if (selected.Count == 2)
        {
            List<Vector3> faces = target.GetCommonFaceNormals(a, b);

            if (faces.Count == 1)
            {
                // 沿著這條線、垂直這一面切下去
                c = a + faces[0] * target.Size;
            }
            else if (faces.Count == 0)
            {
                message =
                    "兩個點不在同一個面上。\n" +
                    "請再選第 3 個點，\n" +
                    "用三個點決定切面。";
                return false;
            }
            else
            {
                message =
                    "兩個點在方塊的同一條邊上，\n" +
                    "這樣切不開方塊。\n" +
                    "請再選第 3 個點。";
                return false;
            }
        }
        else
        {
            c = selected[2];
        }

        Vector3 n = Vector3.Cross(b - a, c - a);

        if (n.sqrMagnitude <= target.Size * target.Size * 1e-8f)
        {
            message =
                "三個點在同一條直線上，\n" +
                "沒辦法決定切面。\n" +
                "請換一個點。";
            return false;
        }

        if (!target.PlaneCutsThrough(a, n))
        {
            message =
                "這個切面只碰到方塊的表面，\n" +
                "切不開喔！請換一個點。";
            return false;
        }

        if (selected.Count == 2)
        {
            message =
                "紅色虛線是切割線，\n" +
                "綠色框是切開後的切面。\n" +
                "按右手 A 鍵確認切割，\n" +
                "或再選第 3 個點改成斜切。";
        }
        else
        {
            message =
                "三個點決定的切面預覽中（綠色框）。\n" +
                "按右手 A 鍵確認切割，\n" +
                "左手 Y 鍵清除重選。";
        }

        return true;
    }

    // =========================================================
    // 預覽：虛線 + 切面外框
    // =========================================================
    private void RefreshPreview(bool announce)
    {
        planeValid = TryBuildPlane(out planeA, out planeB, out planeC, out string message);

        if (previewRoot != null)
        {
            Destroy(previewRoot);
            previewRoot = null;
        }

        if (target != null && selected.Count >= 2)
        {
            float size = target.Size;

            previewRoot = new GameObject("GridCutPreview");
            previewRoot.transform.SetParent(target.transform, false);

            // 紅色虛線：依照選點順序連起來
            List<Vector3> dv = new List<Vector3>();
            List<int> dt = new List<int>();

            for (int i = 0; i < selected.Count - 1; i++)
            {
                GridCutMeshUtil.AddDashedLine(
                    dv, dt, selected[i], selected[i + 1],
                    dashThicknessRatio * size, dashLengthRatio * size, dashGapRatio * size);
            }

            if (selected.Count == 3)
            {
                GridCutMeshUtil.AddDashedLine(
                    dv, dt, selected[2], selected[0],
                    dashThicknessRatio * size, dashLengthRatio * size, dashGapRatio * size);
            }

            CreatePreviewPart("DashLine", dv, dt, previewDashMaterial);

            // 綠色框：切開後的切面形狀
            if (planeValid)
            {
                Vector3 n = Vector3.Cross(planeB - planeA, planeC - planeA);
                List<Vector3> poly = GridCutMeshUtil.PlaneBoxPolygon(target.LocalBounds, planeA, n);

                List<Vector3> ov = new List<Vector3>();
                List<int> ot = new List<int>();

                for (int i = 0; i < poly.Count; i++)
                {
                    GridCutMeshUtil.AddRod(
                        ov, ot, poly[i], poly[(i + 1) % poly.Count],
                        outlineThicknessRatio * size);
                }

                CreatePreviewPart("SectionOutline", ov, ot, previewOutlineMaterial);
            }
        }

        if (announce)
        {
            ShowMessage(message);
        }
    }

    private void CreatePreviewPart(string name, List<Vector3> verts, List<int> tris, Material mat)
    {
        if (previewRoot == null || verts.Count == 0)
        {
            return;
        }

        GameObject go = new GameObject(name);
        go.transform.SetParent(previewRoot.transform, false);

        go.AddComponent<MeshFilter>().sharedMesh =
            GridCutMeshUtil.BuildMesh(name, verts, tris);

        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // =========================================================
    // 確認切割
    // =========================================================
    public void ConfirmCut()
    {
        if (target == null || selected.Count == 0)
        {
            ShowMessage(
                "還沒有選點喔！\n" +
                "請先用手把選擇方塊上的點點。");
            return;
        }

        if (!planeValid)
        {
            TryBuildPlane(out _, out _, out _, out string reason);
            ShowMessage("還不能切割：\n" + reason);
            return;
        }

        if (meshCutter == null)
        {
            meshCutter = FindFirstObjectByType<MeshCutter>();

            if (meshCutter == null)
            {
                Debug.LogError("GridCutController：場景裡找不到 MeshCutter");
                return;
            }
        }

        Transform t = target.transform;

        Vector3 w1 = t.TransformPoint(planeA);
        Vector3 w2 = t.TransformPoint(planeB);
        Vector3 w3 = t.TransformPoint(planeC);
        Vector3 worldNormal = Vector3.Cross(w2 - w1, w3 - w1);

        GridCutTarget cutTarget = target;
        string detail = DescribeSelection();

        // 先清掉選點和預覽（切完後原方塊會被隱藏）
        ClearSelection(false);

        bool ok = meshCutter.CutWithPlane(cutTarget.gameObject, w1, worldNormal);

        Log(ok ? "GridCut" : "GridCutFailed", detail, cutTarget.name);

        if (!ok)
        {
            ShowMessage(
                "切割失敗了，\n" +
                "請換一組點再試一次。");
        }
    }

    public void ClearSelection(bool announce)
    {
        if (target != null)
        {
            target.ClearSelectionVisuals();
        }

        selected.Clear();
        planeValid = false;
        target = null;

        if (previewRoot != null)
        {
            Destroy(previewRoot);
            previewRoot = null;
        }

        if (announce)
        {
            ShowMessage("已清除選取的點。");
            Log("GridCutClear", "");
        }
    }

    // =========================================================
    // 小工具
    // =========================================================
    private string DescribePoint(Vector3 p)
    {
        if (target == null)
        {
            return p.ToString();
        }

        Vector3Int g = target.ToGridCoord(p);
        return "(" + g.x + "," + g.y + "," + g.z + ")";
    }

    private string DescribeSelection()
    {
        List<string> parts = new List<string>();

        foreach (Vector3 p in selected)
        {
            parts.Add(DescribePoint(p));
        }

        return "points=" + string.Join(" ", parts);
    }

    private void ShowMessage(string message)
    {
        Debug.Log("[九宮格切割] " + message.Replace("\n", " "));

        if (showMessagesOnBoard && aiTutorManager != null)
        {
            aiTutorManager.ShowAnalysisFeedback(message);
        }
    }

    private void Log(string action, string details, string objectName = null)
    {
        if (UserActionLogger.Instance != null)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                objectName = target != null ? target.name : "CutTarget";
            }

            UserActionLogger.Instance.LogAction(action, objectName, details);
        }
    }

    public static Material CreateFallbackMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material m = new Material(shader);

        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);

        return m;
    }
}
