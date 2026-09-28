using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class CutPieceFreezeAfterGrab : MonoBehaviour
{
    private Rigidbody rb;
    private XRGrabInteractable grab;

    private bool hasBeenGrabbed = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();

        // 切割後留在原地（不掉落、不彈飛），抓起來才會動
        rb.useGravity = false;
        rb.isKinematic = true;

        grab.selectEntered.AddListener(OnGrab);
        grab.selectExited.AddListener(OnRelease);
    }

    private void OnDestroy()
    {
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnGrab);
            grab.selectExited.RemoveListener(OnRelease);
        }
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        hasBeenGrabbed = true;

        rb.isKinematic = false;
        rb.useGravity = false;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        if (!hasBeenGrabbed)
            return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.useGravity = false;
        rb.isKinematic = true;
    }
}