using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public class XRButtonClickProxy : MonoBehaviour
{
    public Button targetButton;
    public XRNode xrNode = XRNode.RightHand;
    public bool usePrimaryButton = true;
    public bool useTriggerButton = true;

    bool prevPressed;

    void Reset()
    {
        if (targetButton == null)
            targetButton = GetComponent<Button>();
    }

    void Update()
    {
        if (targetButton == null || !targetButton.interactable) { prevPressed = false; return; }

        bool pressed = false;
        var device = InputDevices.GetDeviceAtXRNode(xrNode);
        if (device.isValid)
        {
            bool primaryBtn = false, triggerBtn = false;
            if (usePrimaryButton)
                device.TryGetFeatureValue(CommonUsages.primaryButton, out primaryBtn);
            if (useTriggerButton)
                device.TryGetFeatureValue(CommonUsages.triggerButton, out triggerBtn);
            pressed = primaryBtn || triggerBtn;
        }

        if (pressed && !prevPressed)
        {
            targetButton.onClick?.Invoke();
        }
        prevPressed = pressed;
    }
}
