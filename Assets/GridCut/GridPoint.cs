using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// =============================================================
// 九宮格上的一個點點（每個交叉點一個）
// 由 GridCutTarget 在執行時自動產生，不需要手動放到場景裡
// 手把射線指到點點 → 按握把(Grip) 選取 / 再按一次取消
// =============================================================
public class GridPoint : MonoBehaviour
{
    public GridCutTarget Owner { get; private set; }

    // 點在方塊表面上的位置（方塊自己的座標系）
    public Vector3 LocalPosition { get; private set; }

    private MeshRenderer rend;
    private XRSimpleInteractable interactable;

    private bool hovered;
    private bool selected;
    private Vector3 baseScale = Vector3.one;

    public void Init(GridCutTarget owner, Vector3 localPosition)
    {
        Owner = owner;
        LocalPosition = localPosition;

        rend = GetComponent<MeshRenderer>();
        baseScale = transform.localScale;

        // Collider 要先存在，XRSimpleInteractable 才抓得到
        interactable = gameObject.AddComponent<XRSimpleInteractable>();
        interactable.hoverEntered.AddListener(OnHoverEntered);
        interactable.hoverExited.AddListener(OnHoverExited);
        interactable.selectEntered.AddListener(OnSelectEntered);

        UpdateVisual();
    }

    private void OnDestroy()
    {
        if (interactable != null)
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            interactable.selectEntered.RemoveListener(OnSelectEntered);
        }
    }

    private void OnDisable()
    {
        hovered = false;
        UpdateVisual();
    }

    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        hovered = true;
        UpdateVisual();
    }

    private void OnHoverExited(HoverExitEventArgs args)
    {
        hovered = interactable != null && interactable.isHovered;
        UpdateVisual();
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        Press();
    }

    // 手把選取、或 Editor 裡滑鼠點擊都會走這裡
    public void Press()
    {
        GridCutController controller = GridCutController.Instance;

        if (controller == null)
        {
            Debug.LogWarning("GridPoint：場景裡找不到 GridCutController");
            return;
        }

        controller.OnPointPressed(this);
    }

    public void SetSelected(bool value)
    {
        selected = value;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (rend == null || Owner == null)
        {
            return;
        }

        Material m;

        if (selected)
        {
            m = Owner.DotSelectedMaterial;
        }
        else if (hovered)
        {
            m = Owner.DotHoverMaterial;
        }
        else
        {
            m = Owner.DotMaterial;
        }

        if (m != null)
        {
            rend.sharedMaterial = m;
        }

        transform.localScale =
            baseScale * (selected || hovered ? 1.4f : 1f);
    }
}
