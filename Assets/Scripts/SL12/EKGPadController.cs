using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace SL12
{
    [RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class EKGPadController : MonoBehaviour
    {
        [Header("References")]
        public EKGPadPeelInteraction peelInteraction;
        [Tooltip("Prefab for the peeled backing sticker.")]
        public GameObject peeledBackingPrefab;

        [Header("Respawn Settings")]
        [Tooltip("Optional explicit respawn point for this pad.")]
        public Transform respawnPoint;
        [Tooltip("Scene path used to auto-find respawn point if none is assigned.")]
        public string respawnMarkerPath = "RefMarkers/DropLocationMarker";
        [Tooltip("If pad Y goes below this value while not held, it will be teleported back to respawnPoint.")]
        public float respawnBelowY = 0.1f;
        [Tooltip("How often to check for respawn (seconds).")]
        public float checkInterval = 0.2f;
        [Tooltip("Optional: when respawning, raycast down from respawnPoint to land on a surface (e.g., table/floor). Leave empty to skip.")]
        public LayerMask respawnSurfaceMask = 0;

        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;
        Rigidbody rb;
        float nextCheckTime;
        bool isHeld;

        void Awake()
        {
            grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            rb = GetComponent<Rigidbody>();

            if (peelInteraction == null)
                peelInteraction = GetComponent<EKGPadPeelInteraction>();

            // Allow configuring the peeled backing prefab on this controller
            if (peelInteraction != null && peeledBackingPrefab != null && peelInteraction.peeledBackingPrefab == null)
                peelInteraction.peeledBackingPrefab = peeledBackingPrefab;

            EnsureRespawnPoint();
        }

        void OnEnable()
        {
            if (grab != null)
            {
                grab.selectEntered.AddListener(OnSelectEntered);
                grab.selectExited.AddListener(OnSelectExited);
            }
        }

        void OnDisable()
        {
            if (grab != null)
            {
                grab.selectEntered.RemoveListener(OnSelectEntered);
                grab.selectExited.RemoveListener(OnSelectExited);
            }
            isHeld = false;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            isHeld = true;
            // While held, explicitly clear placed state so no external logic tries to attach
            if (peelInteraction != null)
                peelInteraction.IsPlaced = false;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            isHeld = false;
        }

        void Update()
        {
            if (respawnPoint == null)
            {
                // Retry lookup in case respawn point spawned late
                EnsureRespawnPoint();
                if (respawnPoint == null) return;
            }
            if (grab != null && grab.isSelected) return; // don't respawn while held
            if (isHeld) return; // extra guard for any attach/respawn while held

            if (Time.unscaledTime < nextCheckTime) return;
            nextCheckTime = Time.unscaledTime + Mathf.Max(0.02f, checkInterval);

            // If pad has fallen below the allowed height, teleport back to respawn
            if (transform.position.y < respawnBelowY)
            {
                RespawnAtPoint();
            }
        }

        void EnsureRespawnPoint()
        {
            if (respawnPoint != null) return;
            if (string.IsNullOrEmpty(respawnMarkerPath)) return;

            var go = GameObject.Find(respawnMarkerPath);
            if (go != null)
                respawnPoint = go.transform;
        }

        void RespawnAtPoint()
        {
            if (respawnPoint == null) return;
            if (isHeld) return; // never modify parent/pose while held

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = false;
                rb.isKinematic = true;
            }

            // Ensure pad is grabbable again after respawn
            if (grab != null)
                grab.enabled = true;

            // Clear placed state so markers can snap this pad again later
            if (peelInteraction != null)
                peelInteraction.IsPlaced = false;

            transform.SetParent(respawnPoint, true);

            Vector3 targetPos = respawnPoint.position;
            Quaternion targetRot = respawnPoint.rotation;

            if (respawnSurfaceMask.value != 0)
            {
                var origin = respawnPoint.position + Vector3.up * 0.5f;
                if (Physics.Raycast(origin, Vector3.down, out var hit, 2f, respawnSurfaceMask, QueryTriggerInteraction.Ignore))
                {
                    targetPos = hit.point;
                }
            }

            transform.position = targetPos;
            transform.rotation = targetRot;
        }
    }
}
