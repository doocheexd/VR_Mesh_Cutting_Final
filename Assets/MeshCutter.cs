using UnityEngine;
using EzySlice;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class MeshCutter : MonoBehaviour
{
    [SerializeField] private Transform cuttingPlane;
    [SerializeField] private Material crossSectionMaterial;
    [SerializeField] private UndoManager undoManager;
    [SerializeField] private AITutorManager aiTutorManager;
    [SerializeField] private CutAnalyzer cutAnalyzer;

    // 玩家視角 AI 截圖
    [SerializeField] private AIVisionCapture aiVisionCapture;

    [Header("切開後的效果")]
    [Tooltip("兩塊各往外移動多少公尺（0.015 = 1.5 公分，兩塊之間約 3 公分的縫）")]
    [SerializeField] private float separationDistance = 0.015f;

    [Tooltip("分開的動作花多少秒")]
    [SerializeField] private float separationDuration = 0.3f;

    private GameObject currentTarget;

    // 給 CuttingPlaneGridSnap（切割板吸附到九宮格切面）用
    public GameObject CurrentTarget => currentTarget;
    public Transform CuttingPlaneTransform => cuttingPlane;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Cuttable") ||
            other.CompareTag("CutPiece"))
        {
            currentTarget = other.gameObject;

            Debug.Log(
                "進入切割區：" +
                currentTarget.name
            );
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (currentTarget != null &&
            currentTarget == other.gameObject)
        {
            Debug.Log("離開切割區");

            currentTarget = null;
        }
    }

    // =========================================================
    // 確認切割
    // =========================================================
    public void ConfirmCut()
    {
        if (currentTarget == null)
        {
            Debug.Log(
                "目前沒有可以切割的物件"
            );

            return;
        }

        SliceTarget();
    }

    // =========================================================
    // 執行切割
    // =========================================================
    private void SliceTarget()
    {
        if (cuttingPlane == null)
        {
            Debug.LogError(
                "MeshCutter 尚未指定 Cutting Plane"
            );

            return;
        }

        GameObject target =
            currentTarget;

        // 切割板已經吸附到九宮格選好的切面 → 用那個精準切面切
        CuttingPlaneGridSnap gridSnap =
            GetComponent<CuttingPlaneGridSnap>();

        if (gridSnap != null &&
            gridSnap.TryGetEngagedPlane(
                target,
                out Vector3 snapPoint,
                out Vector3 snapNormal))
        {
            PerformSlice(
                target,
                snapPoint,
                snapNormal,
                snapNormal
            );

            return;
        }

        // 使用校正後的切割角度
        Vector3 sliceNormal =
            GetSnappedPlaneNormal();

        PerformSlice(
            target,
            cuttingPlane.position,
            sliceNormal,
            cuttingPlane.up
        );
    }

    // =========================================================
    // 九宮格點點切割用：直接指定切割平面（世界座標）
    // =========================================================
    public bool CutWithPlane(
        GameObject target,
        Vector3 planePoint,
        Vector3 planeNormal
    )
    {
        if (target == null ||
            planeNormal.sqrMagnitude < 1e-12f)
        {
            return false;
        }

        Vector3 n = planeNormal.normalized;

        return PerformSlice(
            target,
            planePoint,
            n,
            n
        );
    }

    // =========================================================
    // 共用的切割流程（切割板 / 九宮格點點 都走這裡）
    // =========================================================
    private bool PerformSlice(
        GameObject target,
        Vector3 planePosition,
        Vector3 sliceNormal,
        Vector3 upperPushDirection
    )
    {
        SlicedHull hull =
            target.Slice(
                planePosition,
                sliceNormal
            );

        if (hull == null)
        {
            Debug.LogWarning(
                "切割失敗：切割平面可能沒有真正穿過物件"
            );

            return false;
        }

        // 建立上半部
        GameObject upperHull =
            hull.CreateUpperHull(
                target,
                crossSectionMaterial
            );

        // 建立下半部
        GameObject lowerHull =
            hull.CreateLowerHull(
                target,
                crossSectionMaterial
            );

        if (upperHull == null ||
            lowerHull == null)
        {
            Debug.LogWarning(
                "切割失敗：無法建立切割後物件"
            );

            if (upperHull != null) Destroy(upperHull);
            if (lowerHull != null) Destroy(lowerHull);

            return false;
        }

        upperHull.name =
            target.name + "_Upper";

        lowerHull.name =
            target.name + "_Lower";

        // 設定切割後物件
        SetupCutPiece(
            upperHull,
            upperPushDirection
        );

        SetupCutPiece(
            lowerHull,
            -upperPushDirection
        );

        // 兩塊沿著切面方向慢慢分開一點點，看得出切開了
        StartCoroutine(
            SeparatePieces(
                upperHull,
                lowerHull,
                upperPushDirection
            )
        );

        // =====================================================
        // Undo
        // =====================================================

        if (undoManager != null)
        {
            undoManager.SaveCut(
                target,
                upperHull,
                lowerHull
            );
        }

        if (currentTarget == target)
        {
            currentTarget = null;
        }

        // Undo 還需要原始物件
        // 所以這裡不 Destroy
        target.SetActive(false);

        // =====================================================
        // 告訴 AI Tutor：完成一刀
        // =====================================================

        if (aiTutorManager != null)
        {
            aiTutorManager.OnCutCompleted();
        }
        // =====================================================
        // 玩家視角 AI 截圖
        // =====================================================

        if (aiVisionCapture != null)
        {
            StartCoroutine(
                CaptureAfterCut()
            );
        }
        else
        {
            Debug.LogWarning(
                "MeshCutter 尚未指定 AIVisionCapture"
            );
        }

        Debug.Log("切割成功！");

        return true;
    }

    // =========================================================
    // 切開後兩塊輕輕分開（取代原本的彈飛 + 掉落）
    // =========================================================

    private System.Collections.IEnumerator SeparatePieces(
        GameObject upper,
        GameObject lower,
        Vector3 direction
    )
    {
        Vector3 dir =
            direction.sqrMagnitude > 1e-8f
                ? direction.normalized
                : Vector3.up;

        Vector3 upperStart =
            upper.transform.position;

        Vector3 lowerStart =
            lower.transform.position;

        float duration =
            Mathf.Max(separationDuration, 0.01f);

        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;

            float k =
                Mathf.SmoothStep(0f, 1f, t / duration);

            Vector3 offset =
                dir * (separationDistance * k);

            if (upper != null)
            {
                upper.transform.position =
                    upperStart + offset;
            }

            if (lower != null)
            {
                lower.transform.position =
                    lowerStart - offset;
            }

            if (upper == null && lower == null)
            {
                yield break;
            }

            yield return null;
        }
    }

    // =========================================================
    // 切割後等待一幀，再拍玩家視角
    // =========================================================

    private System.Collections.IEnumerator CaptureAfterCut()
    {
        // 等待一幀
        // 讓新的 CutPiece 先出現在畫面上
        yield return null;

        if (aiVisionCapture != null)
        {
            Debug.Log(
                "準備擷取切割後的玩家視角..."
            );

            aiVisionCapture
                .CaptureAndSendToAI();
        }
    }

    // =========================================================
    // 校正切割角度
    // =========================================================

    private Vector3 GetSnappedPlaneNormal()
    {
        Vector3 angles =
            cuttingPlane.eulerAngles;

        float snappedX =
            SnapAngle(
                angles.x
            );

        float snappedZ =
            SnapAngle(
                angles.z
            );

        Quaternion snappedRotation =
            Quaternion.Euler(
                snappedX,
                angles.y,
                snappedZ
            );

        return snappedRotation *
               Vector3.up;
    }

    private float SnapAngle(
        float angle
    )
    {
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return Mathf.Round(
            angle / 45f
        ) * 45f;
    }

    // =========================================================
    // 設定切割後的物件
    // =========================================================

    private void SetupCutPiece(
        GameObject piece,
        Vector3 pushDirection
    )
    {
        if (piece == null)
        {
            return;
        }

        // 切割後仍可以再次切割
        piece.tag = "CutPiece";

        MeshFilter meshFilter =
            piece.GetComponent<MeshFilter>();

        if (meshFilter == null ||
            meshFilter.sharedMesh == null)
        {
            Debug.LogWarning(
                "切割物件缺少 MeshFilter 或 Mesh"
            );

            return;
        }

        // =====================================================
        // Collider
        // =====================================================

        MeshCollider meshCollider =
            piece.AddComponent<MeshCollider>();

        meshCollider.sharedMesh =
            meshFilter.sharedMesh;

        meshCollider.convex = true;

        // =====================================================
        // Rigidbody
        // =====================================================

        Rigidbody rb =
            piece.AddComponent<Rigidbody>();

        // 切開後留在原地，不會掉下去或彈飛
        // （兩塊之間的小縫由 SeparatePieces 慢慢拉開）
        rb.mass = 0.5f;
        rb.useGravity = false;
        rb.isKinematic = true;

        rb.collisionDetectionMode =
            CollisionDetectionMode.ContinuousSpeculative;

        // =====================================================
        // VR Grab
        // =====================================================

        XRGrabInteractable grab =
            piece.AddComponent<
                XRGrabInteractable
            >();

        grab.colliders.Add(
            meshCollider
        );

        // 第一次抓起再放開後固定
        piece.AddComponent<
            CutPieceFreezeAfterGrab
        >();
    }
}