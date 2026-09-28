using UnityEngine;

// =============================================================
// 切割板「悄悄吸附」到九宮格選好的切面
//
// 先用點點選好 2 或 3 個點（出現綠色切面框）
// → 拿切割板靠到方塊上，角度和位置差不多對的時候，
//   切割板會輕輕滑到剛好對準選好的切面
// → 這時按右手扳機切下去，就會精準照著點點的切面切
//
// 掛在有 MeshCutter 的物件上（BladeTrigger）
// 沒掛的話 GridCutController 執行時會自動補上
// =============================================================
[DefaultExecutionOrder(100)] // 在 CuttingAngleSnap 之後才調整，最後由這裡決定
public class CuttingPlaneGridSnap : MonoBehaviour
{
    [Tooltip("切割板和選好的切面，角度差在幾度以內才會吸附")]
    [SerializeField] private float snapAngle = 35f;

    [Tooltip("切割板中心離選好的切面多近（公尺）才會吸附")]
    [SerializeField] private float snapDistance = 0.08f;

    [Tooltip("吸附滑過去要花多少秒（越大越悄悄）")]
    [SerializeField] private float blendTime = 0.2f;

    [Header("目前狀態（只看不用改）")]
    [SerializeField] private string status = "";

    private MeshCutter meshCutter;

    private bool engaged;
    private float weight;

    private GridCutTarget engagedTarget;
    private Vector3 engagedPoint;
    private Vector3 engagedNormal;

    public bool IsEngaged => engaged;

    private void Awake()
    {
        meshCutter = GetComponent<MeshCutter>();
    }

    private void LateUpdate()
    {
        Transform board = meshCutter != null ? meshCutter.CuttingPlaneTransform : null;

        if (board == null)
        {
            engaged = false;
            weight = 0f;
            return;
        }

        bool hasPlane = false;
        Vector3 point = Vector3.zero;
        Vector3 normal = Vector3.up;
        GridCutTarget target = null;

        GridCutController ctrl = GridCutController.Instance;

        if (ctrl != null)
        {
            hasPlane = ctrl.TryGetPlaneWorld(out target, out point, out normal);
        }

        bool shouldEngage = false;

        if (!hasPlane)
        {
            status = "還沒選好切面（先選 2~3 個點）";
        }
        else if (meshCutter.CurrentTarget == null ||
                 meshCutter.CurrentTarget != target.gameObject)
        {
            status = "切割板還沒碰到方塊";
        }
        else
        {
            // 切割板正反面都可以，選比較接近的那一面
            if (Vector3.Dot(board.up, normal) < 0f)
            {
                normal = -normal;
            }

            float angle = Vector3.Angle(board.up, normal);
            float dist = Mathf.Abs(Vector3.Dot(normal, board.position - point));

            // 已經吸住的話放寬一點，避免手抖一直吸住又放開
            float angleLimit = engaged ? snapAngle + 10f : snapAngle;
            float distLimit = engaged ? snapDistance * 1.5f : snapDistance;

            shouldEngage = angle <= angleLimit && dist <= distLimit;

            status = (shouldEngage ? "已吸附" : "差一點") +
                     "（角度差 " + angle.ToString("0") + "°，距離 " +
                     (dist * 100f).ToString("0.0") + " 公分）";
        }

        if (shouldEngage && !engaged)
        {
            Debug.Log("[九宮格切割] 切割板已吸附到選好的切面");
        }

        engaged = shouldEngage;

        if (engaged)
        {
            engagedTarget = target;
            engagedPoint = point;
            engagedNormal = normal;
        }

        float step = blendTime > 0f ? Time.deltaTime / blendTime : 1f;
        weight = Mathf.MoveTowards(weight, engaged ? 1f : 0f, step);

        if (weight <= 0f || engagedTarget == null)
        {
            return;
        }

        // 目標姿勢：只做最小的旋轉 + 沿法線方向移動到切面上
        Vector3 pos = board.position;
        Quaternion rot = board.rotation;

        Vector3 n = engagedNormal;

        if (Vector3.Dot(board.up, n) < 0f)
        {
            n = -n;
        }

        Quaternion targetRot = Quaternion.FromToRotation(board.up, n) * rot;
        Vector3 targetPos = pos - n * Vector3.Dot(n, pos - engagedPoint);

        float k = Mathf.SmoothStep(0f, 1f, weight);

        board.SetPositionAndRotation(
            Vector3.Lerp(pos, targetPos, k),
            Quaternion.Slerp(rot, targetRot, k));
    }

    // MeshCutter 切割時會問：現在有沒有吸住？吸住就用選好的精準切面
    public bool TryGetEngagedPlane(GameObject target, out Vector3 point, out Vector3 normal)
    {
        point = engagedPoint;
        normal = engagedNormal;

        return engaged &&
               engagedTarget != null &&
               target != null &&
               engagedTarget.gameObject == target;
    }
}
