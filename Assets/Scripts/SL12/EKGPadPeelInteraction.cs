using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace SL12
{
    [RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
    public class EKGPadPeelInteraction : MonoBehaviour
    {
        [Header("References")]
        public GameObject backingChild;
        public GameObject peeledBackingPrefab;

        [Header("State (read-only)")]
        public bool IsPeeled { get; private set; } = false;
        public bool IsPlaced { get; set; } = false;

        [Header("Marker Snap")] 
        [Tooltip("Optional child to align to marker. If null, uses this transform.")]
        public Transform attachRoot;
        [Tooltip("Meters within which to consider a marker for snapping.")]
        public float attachRadius = 0.08f;
        [Tooltip("Match pad rotation to marker on snap.")]
        public bool snapRotation = true;
        [Tooltip("Tag used to identify patient markers.")]
        public string markerTag = "EKGMarker";
        [Tooltip("Layers used to find patient marker colliders.")]
        public LayerMask markerLayers = ~0;

        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable padGrab;
        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable backingGrab;
        UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor padHoldingInteractor;
        UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor peelingInteractor;
        Vector3 backingInitialLocalPos;
        Quaternion backingInitialLocalRot;
        Vector3 peelStartWorldPos;
        [Header("Peel Settings")]
        public float peelPullThreshold = 0.03f; // meters required to detach
        Collider[] padColliders;

        void Reset()
        {
            TryAutoWire();
        }

        void PerformPeelWithInteractor(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor interactor)
        {
            if (IsPeeled) return;

            // synthesize minimal args via current state
            IsPeeled = true;

            if (backingChild != null)
                backingChild.SetActive(false);

            var prefab = peeledBackingPrefab ?? PadManager.Instance?.peeledBackingPrefab;
            if (prefab != null)
            {
                var spawnPose = backingChild != null ? backingChild.transform : transform;
                var inst = Instantiate(prefab, spawnPose.position, spawnPose.rotation);

                var grabbable = inst.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>()
                                ?? inst.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                grabbable.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable.MovementType.Kinematic;

                var rb = inst.GetComponent<Rigidbody>() ?? inst.AddComponent<Rigidbody>();
                rb.useGravity = true; rb.isKinematic = false;

                var col = inst.GetComponent<Collider>() ?? inst.AddComponent<BoxCollider>();
                col.isTrigger = false;

                if (inst.GetComponent<PeeledBacking>() == null)
                    inst.AddComponent<PeeledBacking>();

                var selectable = grabbable as UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable;
                if (interactor != null)
                {
                    var attach = interactor.GetAttachTransform(selectable);
                    if (attach != null)
                    {
                        inst.transform.SetPositionAndRotation(attach.position, attach.rotation);
                        inst.transform.position += attach.forward * 0.03f;
                    }
                }

                var manager = UnityEngine.Object.FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
                if (manager != null && interactor != null && selectable != null)
                {
                    manager.SelectEnter(interactor, selectable);
                }
                else if (interactor != null)
                {
                    var attach = interactor.GetAttachTransform(selectable);
                    if (attach != null)
                        inst.transform.SetParent(attach, true);
                }
            }

            PadManager.Instance?.OnPadPeeled();

            // restore pad colliders
            SetPadCollidersEnabled(true);
        }

        void Awake()
        {
            padGrab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            TryAutoWire();

            if (peeledBackingPrefab == null && PadManager.Instance != null)
                peeledBackingPrefab = PadManager.Instance.peeledBackingPrefab;

            // Make pad static initially
            var rbInit = GetComponent<Rigidbody>();
            if (rbInit == null) rbInit = gameObject.AddComponent<Rigidbody>();
            rbInit.useGravity = false;
            rbInit.isKinematic = true;

            // cache pad colliders
            padColliders = GetComponentsInChildren<Collider>(includeInactive: true);
        }

        void OnEnable()
        {
            if (padGrab != null)
            {
                padGrab.selectEntered.AddListener(OnPadSelectEntered);
                padGrab.selectExited.AddListener(OnPadSelectExited);
            }
            if (backingGrab != null)
            {
                backingGrab.selectEntered.AddListener(OnBackingSelectEntered);
                backingGrab.selectExited.AddListener(OnBackingSelectExited);
            }
        }

        void OnDisable()
        {
            if (padGrab != null)
            {
                padGrab.selectEntered.RemoveListener(OnPadSelectEntered);
                padGrab.selectExited.RemoveListener(OnPadSelectExited);
            }
            if (backingGrab != null)
            {
                backingGrab.selectEntered.RemoveListener(OnBackingSelectEntered);
                backingGrab.selectExited.RemoveListener(OnBackingSelectExited);
            }
        }

        void TryAutoWire()
        {
            if (backingChild == null)
            {
                var t = transform.Find("EKG_Backing");
                if (t != null) backingChild = t.gameObject;
            }

            if (backingChild != null)
            {
                backingGrab = backingChild.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                if (backingGrab == null) backingGrab = backingChild.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

                var rb = backingChild.GetComponent<Rigidbody>();
                if (rb == null) rb = backingChild.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;

                var col = backingChild.GetComponent<Collider>();
                if (col == null) col = backingChild.AddComponent<BoxCollider>();
                col.isTrigger = false;

                // cache initial local pose
                backingInitialLocalPos = backingChild.transform.localPosition;
                backingInitialLocalRot = backingChild.transform.localRotation;
            }
        }

        void OnPadSelectEntered(SelectEnterEventArgs args)
        {
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false; // allow controller-driven motion
                rb.useGravity = false;   // keep off while held
            }
            padHoldingInteractor = args.interactorObject;
            // While holding the pad, disable its other colliders so the other hand can select the backing cleanly
            SetPadCollidersEnabled(false);

            // If previously placed, notify PadManager that this pad is no longer placed
            if (IsPlaced)
            {
                global::PadManager.Instance?.NotifyDetached(this);
                IsPlaced = false;
            }
        }

        void OnPadSelectExited(SelectExitEventArgs args)
        {
            if (padHoldingInteractor == args.interactorObject)
                padHoldingInteractor = null;

            // On release, if peeled, try to auto-snap to a nearby marker before deciding RB mode
            if (IsPeeled)
            {
                TryAutoSnapToMarker();
            }

            // When pad is released, either stay placed on marker or fall with gravity
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (IsPlaced)
                {
                    rb.useGravity = false;
                    rb.isKinematic = true;
                }
                else
                {
                    rb.useGravity = true;
                    rb.isKinematic = false;
                }
            }

            // Restore colliders when pad is fully released
            SetPadCollidersEnabled(true);
        }

        void OnBackingSelectEntered(SelectEnterEventArgs args)
        {
            if (IsPeeled) return;
            if (padHoldingInteractor == null) return; // need pad held first
            if (padHoldingInteractor == args.interactorObject) return; // must be the other hand

            // Second hand grabs the backing: peel immediately and hand the peeled prefab to that controller
            peelingInteractor = args.interactorObject as UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor;
            if (peelingInteractor != null)
            {
                PerformPeelWithInteractor(peelingInteractor);
                peelingInteractor = null;
            }
        }

        void OnBackingSelectExited(SelectExitEventArgs args)
        {
            // cancel peel if not completed
            peelingInteractor = null;
            if (!IsPeeled && backingChild != null)
            {
                backingChild.transform.localPosition = backingInitialLocalPos;
                backingChild.transform.localRotation = backingInitialLocalRot;
            }

            // restore pad colliders
            SetPadCollidersEnabled(true);
        }

        void Update()
        {
            if (!IsPeeled && peelingInteractor != null && backingChild != null)
            {
                // pull the backing toward the peeling hand attach
                var selectable = backingGrab as UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable;
                var attach = peelingInteractor.GetAttachTransform(selectable);
                if (attach != null)
                {
                    var targetPos = attach.position;
                    // smooth follow for sticky feel
                    backingChild.transform.position = Vector3.Lerp(backingChild.transform.position, targetPos, 0.35f);
                    backingChild.transform.rotation = Quaternion.Lerp(backingChild.transform.rotation, attach.rotation, 0.35f);

                    // check pull distance from start
                    if (Vector3.Distance(targetPos, peelStartWorldPos) >= peelPullThreshold)
                    {
                        PerformPeelWithInteractor(peelingInteractor);
                        peelingInteractor = null;
                    }
                }
            }
        }

        void PerformPeel(SelectEnterEventArgs args)
        {
            IsPeeled = true;

            if (backingChild != null)
                backingChild.SetActive(false);

            var prefab = peeledBackingPrefab ?? PadManager.Instance?.peeledBackingPrefab;
            if (prefab != null)
            {
                var spawnPose = backingChild != null ? backingChild.transform : transform;
                var inst = Instantiate(prefab, spawnPose.position, spawnPose.rotation);

                var grabbable = inst.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                if (grabbable == null) grabbable = inst.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                grabbable.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable.MovementType.Kinematic;

                var rb = inst.GetComponent<Rigidbody>();
                if (rb == null) rb = inst.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.isKinematic = false;

                var col = inst.GetComponent<Collider>();
                if (col == null) col = inst.AddComponent<BoxCollider>();
                col.isTrigger = false;

                if (inst.GetComponent<PeeledBacking>() == null)
                    inst.AddComponent<PeeledBacking>();

                var interactor = args.interactorObject as UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor;
                var selectable = grabbable as UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable;

                // Snap spawned peeled backing to the peeling hand's attach transform
                if (interactor != null)
                {
                    var attach = interactor.GetAttachTransform(selectable);
                    if (attach != null)
                    {
                        inst.transform.SetPositionAndRotation(attach.position, attach.rotation);
                        // small outward offset to feel like it's peeling off the pad
                        inst.transform.position += attach.forward * 0.03f;
                    }
                }

                var manager = UnityEngine.Object.FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
                if (manager != null && interactor != null && selectable != null)
                {
                    manager.SelectEnter(interactor, selectable);
                }
                else if (interactor != null)
                {
                    // Fallback: parent to the hand attach if manager not present
                    var attach = interactor.GetAttachTransform(selectable);
                    if (attach != null)
                        inst.transform.SetParent(attach, true);
                }
            }

            PadManager.Instance?.OnPadPeeled();
        }

        void SetPadCollidersEnabled(bool enabled)
        {
            // Refresh colliders each time to pick up any auto-wired components
            padColliders = GetComponentsInChildren<Collider>(includeInactive: true);
            if (padColliders == null) return;
            foreach (var c in padColliders)
            {
                if (c == null) continue;
                if (backingChild != null && c.transform == backingChild.transform) continue; // don't disable backing collider
                c.enabled = enabled;
            }
        }

        void TryAutoSnapToMarker()
        {
            Vector3 probe = (attachRoot != null ? attachRoot : transform).position;
            float bestSqr = attachRadius * attachRadius;
            Transform best = null;

            var cols = Physics.OverlapSphere(probe, attachRadius, markerLayers, QueryTriggerInteraction.Collide);
            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    var col = cols[i];
                    if (!col || !col.enabled) continue;
                    if (!string.IsNullOrEmpty(markerTag) && !col.CompareTag(markerTag)) continue;
                    float d2 = (col.transform.position - probe).sqrMagnitude;
                    if (d2 <= bestSqr)
                    {
                        bestSqr = d2;
                        best = col.transform;
                    }
                }
            }

            if (best == null) return;

            // Snap/attach
            var target = attachRoot != null ? attachRoot : transform;
            target.SetParent(best, true);
            target.position = best.position;
            if (snapRotation) target.rotation = best.rotation;

            // Lock physics while placed
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = false;
                rb.isKinematic = true;
            }

            IsPlaced = true;
            global::PadManager.Instance?.NotifyAttached(this);
        }
    }
}
