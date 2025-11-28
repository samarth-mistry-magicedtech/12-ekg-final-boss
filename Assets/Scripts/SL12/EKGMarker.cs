using UnityEngine;


namespace SL12
{
    [RequireComponent(typeof(Collider))]
    public class EKGMarker : MonoBehaviour
    {
        [Tooltip("Optional visual to hide when pad placed.")]
        public GameObject markerVisual;

        [Tooltip("Optional snap point under this marker. If null, uses this transform.")]
        public Transform snapPoint;

        EKGPadPeelInteraction currentPad;
        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable currentPadGrab;
        Rigidbody currentPadRb;

        void Reset()
        {
            var c = GetComponent<Collider>();
            c.isTrigger = true;
        }

        void Awake()
        {
            var c = GetComponent<Collider>();
            c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            var pad = other.GetComponentInParent<EKGPadPeelInteraction>();
            if (pad == null) return;

            currentPad = pad;
            currentPadGrab = pad.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            currentPadRb = pad.GetComponent<Rigidbody>();
        }

        void OnTriggerExit(Collider other)
        {
            var pad = other.GetComponentInParent<EKGPadPeelInteraction>();
            if (pad != null && pad == currentPad)
            {
                currentPad = null;
                currentPadGrab = null;
                currentPadRb = null;
            }
        }

        void Update()
        {
            if (currentPad == null) return;
            if (!currentPad.IsPeeled) return;      // must peel first
            if (currentPad.IsPlaced) return;       // already placed

            // Allow snap even while still held, to satisfy: "Pad remains in controller until placed"
            SnapPad();
        }

        void SnapPad()
        {
            var target = snapPoint != null ? snapPoint : transform;

            currentPad.transform.SetParent(target, true);
            currentPad.transform.position = target.position;
            currentPad.transform.rotation = target.rotation;

            if (currentPadRb != null)
            {
                currentPadRb.isKinematic = true;
                currentPadRb.useGravity = false;
                currentPadRb.linearVelocity = Vector3.zero;
                currentPadRb.angularVelocity = Vector3.zero;
            }

            if (currentPadGrab != null)
            {
                currentPadGrab.enabled = false; // drops from hand
            }

            currentPad.IsPlaced = true;

            if (markerVisual != null) markerVisual.SetActive(false);

            PadManager.Instance?.OnPadPlaced();

            currentPad = null;
            currentPadGrab = null;
            currentPadRb = null;
        }
    }
}
