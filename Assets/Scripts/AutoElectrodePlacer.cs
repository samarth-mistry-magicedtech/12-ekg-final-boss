using UnityEngine;

public class AutoElectrodePlacer : MonoBehaviour
{
    [Tooltip("Manager that knows electrodes, markers, and correctness.")]
    public EKGElectodManager manager;

    [Tooltip("When true, enables keyboard/start triggers for auto placement (editor/dev only).")]
    public bool isDebugMode = true;

    [Tooltip("Optional: If set, runs on Start() once.")]
    public bool placeOnStart = false;

    [Tooltip("Keyboard key to trigger placement (editor/desktop testing).")]
    public KeyCode triggerKey = KeyCode.C;

    void Awake()
    {
        if (manager == null)
            manager = Object.FindFirstObjectByType<EKGElectodManager>();
    }

    void Start()
    {
        if (isDebugMode && placeOnStart)
            PlaceAllCorrect();
    }

    void Update()
    {
        if (!isDebugMode) return;
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(triggerKey))
        {
            Debug.Log($"[AutoElectrodePlacer] Trigger key '{triggerKey}' pressed. Placing all electrodes on correct markers.");
            PlaceAllCorrect();
        }
#endif
    }

    public void PlaceAllCorrect()
    {
        if (manager == null) return;

        // For each electrode entry, snap its controller to the correct marker transform
        foreach (var e in manager.electrodes)
        {
            if (e == null || e.controller == null) continue;
            var marker = manager.GetMarkerTransformById(e.correctMarkerId);
            if (marker == null) continue;

            // Detach any current attachment and reserve the correct marker
            manager.Release(e.controller);
            if (!manager.TryReserve(e.controller, marker))
                continue;

            // Attach like in controller logic
            var ctrl = e.controller;
            var t = ctrl.transform;
            var attachRootField = ctrl.GetType().GetField("attachRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var snapRotationField = ctrl.GetType().GetField("snapRotation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var attachRoot = attachRootField != null ? attachRootField.GetValue(ctrl) as Transform : null;
            bool snapRot = snapRotationField != null ? (bool)snapRotationField.GetValue(ctrl) : true;

            var target = attachRoot != null ? attachRoot : t;
            // Clear velocities if possible
            var rb = target.GetComponent<Rigidbody>() ?? target.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            var ab = target.GetComponent<ArticulationBody>() ?? target.GetComponentInParent<ArticulationBody>();
            if (ab != null)
            {
                ab.immovable = true;
                ab.linearVelocity = Vector3.zero;
                ab.angularVelocity = Vector3.zero;
            }

            target.SetParent(marker, true);
            target.position = marker.position;
            if (snapRot) target.rotation = marker.rotation;
        }
    }
}
