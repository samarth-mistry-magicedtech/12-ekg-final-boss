using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace SL12
{
    [RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public class PeeledBacking : MonoBehaviour
    {
        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;
        Rigidbody rb;
        Collider col;

        bool inTrayZone = false;
        Transform trayTransform;

        [Header("Tray Settings")]
        [SerializeField] Transform trayOverride;
        [SerializeField] string trayTag = "KeyTray1";
        [SerializeField] string trayName = "KeyTray1";
        [SerializeField] string traySnapChildName = "BackingSnap";
        [SerializeField] float proximityRadius = 0.05f;

        void Awake()
        {
            grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();

            rb.useGravity = true;
            rb.isKinematic = false;
            col.isTrigger = false;

            var proximity = gameObject.AddComponent<SphereCollider>();
            proximity.isTrigger = true;
            proximity.radius = proximityRadius;
        }

        void OnEnable()
        {
            if (grab != null)
                grab.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            if (grab != null)
                grab.selectExited.RemoveListener(OnSelectExited);
        }

        void OnTriggerEnter(Collider other)
        {
            if (IsTray(other.gameObject))
            {
                inTrayZone = true;
                trayTransform = GetTraySnap(other.gameObject.transform);
            }
        }

        void OnTriggerExit(Collider other)
        {
            if (other.transform == trayTransform || IsTray(other.gameObject))
            {
                inTrayZone = false;
                trayTransform = null;
            }
        }

        bool IsTray(GameObject go)
        {
            if (trayOverride != null && go.transform == trayOverride) return true;

            if (!string.IsNullOrEmpty(trayTag) && go.CompareTag(trayTag)) return true;
            if (!string.IsNullOrEmpty(trayName) && go.name == trayName) return true;
            return false;
        }

        Transform GetTraySnap(Transform tray)
        {
            if (trayOverride != null)
                tray = trayOverride;

            if (!string.IsNullOrEmpty(traySnapChildName))
            {
                var t = tray.Find(traySnapChildName);
                if (t != null)
                    return t;
            }

            return tray;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (inTrayZone && trayTransform != null)
                SnapToTray();
        }

        void SnapToTray()
        {
            transform.SetParent(trayTransform, true);
            transform.position = trayTransform.position;
            transform.rotation = trayTransform.rotation;

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            grab.enabled = false;

            PadManager.Instance?.OnBackingPlaced();
        }
    }
}
