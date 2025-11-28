using UnityEngine;

public class EKGElectrodController : MonoBehaviour
{
    [SerializeField] private Transform respawnMarker;
    [SerializeField] private string respawnMarkerPath = "RefMarkers/DropLocationMarker";
    [SerializeField] private float respawnBelowY = -2f;
    [SerializeField] private bool checkEveryFrame = true;
    [SerializeField] private float checkInterval = 0.2f;
    [SerializeField] private Component xrGrabInteractable;
    [SerializeField] private bool requireNotGrabbed = true;
    [SerializeField] private EKGElectodManager manager;
    [SerializeField] private float attachRadius = 0.08f;
    [SerializeField] private bool snapRotation = true;
    [SerializeField] private Transform proximityPoint;
    [SerializeField] private Transform attachRoot;
    [SerializeField] private bool useGrabEvents = true;
    [SerializeField] private bool useReflectionGrabDetection = false;
    [SerializeField] private bool attachWhileGrabbed = true;
    [SerializeField] private bool detachOnGrab = false;
    [SerializeField] private bool respawnWhenNoMarkerOnRelease = true;
    [SerializeField] private bool useTriggerDetection = true;
    [SerializeField] private string markerTag = "EKGMarker";
    [SerializeField] private LayerMask markerLayers = ~0;
    [SerializeField] private bool disableGrabWhilePlaced = true; // like SL12: disable grab to drop from hand on snap

    float _nextCheck;
    bool _prevGrabbed;
    bool _isGrabbed;
    Transform _attachedMarker;
    Transform _triggerNearest;

    void OnEnable()
    {
        EnsureRespawnMarker();
        _nextCheck = 0f;
        _prevGrabbed = GetGrabState();
        if (attachRoot == null) attachRoot = transform;
        if (proximityPoint == null) proximityPoint = transform;
    }

    void Update()
    {
        if (!checkEveryFrame) return;
        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + Mathf.Max(0.02f, checkInterval);
        TickPlacement();
    }

    public void TryRespawn()
    {
        if (respawnMarker == null) EnsureRespawnMarker();
        if (respawnMarker == null) return;
        if (requireNotGrabbed && GetGrabState()) return;
        if (GetProbePosition().y >= respawnBelowY) return;

        ClearVelocities();
        DetachFromMarker();
        MoveRootTo(respawnMarker.position, respawnMarker.rotation);

        // Ensure grab is enabled after respawn so it can be picked up again
        if (xrGrabInteractable is Behaviour beh)
        {
            beh.enabled = true;
        }
    }

    bool IsGrabbedReflection()
    {
        if (xrGrabInteractable == null) return false;
        var t = xrGrabInteractable.GetType();
        var p = t.GetProperty("isSelected");
        if (p != null && p.PropertyType == typeof(bool))
        {
            try { return (bool)p.GetValue(xrGrabInteractable); } catch { }
        }
        p = t.GetProperty("selectingInteractor");
        if (p != null)
        {
            try { return p.GetValue(xrGrabInteractable) != null; } catch { }
        }
        p = t.GetProperty("isGrabbed");
        if (p != null && p.PropertyType == typeof(bool))
        {
            try { return (bool)p.GetValue(xrGrabInteractable); } catch { }
        }
        return false;
    }

    bool GetGrabState()
    {
        if (useGrabEvents) return _isGrabbed;
        if (useReflectionGrabDetection) return IsGrabbedReflection();
        return false;
    }

    void EnsureRespawnMarker()
    {
        if (respawnMarker != null) return;
        if (!string.IsNullOrEmpty(respawnMarkerPath))
        {
            var go = GameObject.Find(respawnMarkerPath);
            if (go != null) respawnMarker = go.transform;
        }
    }

    void TickPlacement()
    {
        if (manager == null) manager = Object.FindFirstObjectByType<EKGElectodManager>();
        bool grabbed = GetGrabState();
        if (grabbed && detachOnGrab && _attachedMarker != null)
        {
            DetachFromMarker();
        }
        if (!useGrabEvents && !grabbed && _prevGrabbed)
        {
            TryAttachOrRespawn();
        }
        var marker = ResolveNearestCandidate();
        if (marker != null)
        {
            if (attachWhileGrabbed || !grabbed)
            {
                if (manager == null || manager.TryReserve(this, marker))
                {
                    AttachToMarker(marker);
                }
            }
        }
        _prevGrabbed = grabbed;
    }

    public void OnGrabStarted()
    {
        _isGrabbed = true;
        if (detachOnGrab) DetachFromMarker();
    }

    public void OnGrabEnded()
    {
        _isGrabbed = false;
        TryAttachOrRespawn();
    }

    public void SetGrabbed(bool grabbed)
    {
        if (grabbed) OnGrabStarted(); else OnGrabEnded();
    }

    void TryAttachOrRespawn()
    {
        var marker = manager != null ? manager.GetNearestMarker(GetProbePosition(), attachRadius) : null;
        if (marker != null)
        {
            if (manager == null || manager.TryReserve(this, marker))
            {
                AttachToMarker(marker);
            }
        }
        else
        {
            MoveRootTo(respawnMarker.position, respawnMarker.rotation);
        }
    }

    void AttachToMarker(Transform marker)
    {
        _attachedMarker = marker;
        ClearVelocities();
        var rb = attachRoot ? attachRoot.GetComponent<Rigidbody>() : GetComponent<Rigidbody>();
        if (rb == null) rb = attachRoot ? attachRoot.GetComponentInParent<Rigidbody>() : GetComponentInParent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        var ab = attachRoot ? attachRoot.GetComponent<ArticulationBody>() : GetComponent<ArticulationBody>();
        if (ab == null) ab = attachRoot ? attachRoot.GetComponentInParent<ArticulationBody>() : GetComponentInParent<ArticulationBody>();
        if (ab != null) ab.immovable = true;
        var target = attachRoot != null ? attachRoot : transform;
        target.SetParent(marker, true);
        target.position = marker.position;
        if (snapRotation) target.rotation = marker.rotation;

        // Drop from hand by disabling grab while placed (re-enabled on detach/respawn)
        if (disableGrabWhilePlaced && xrGrabInteractable is Behaviour beh)
        {
            beh.enabled = false;
        }
    }

    void DetachFromMarker()
    {
        if (_attachedMarker == null) return;
        var rb = attachRoot ? attachRoot.GetComponent<Rigidbody>() : GetComponent<Rigidbody>();
        if (rb == null) rb = attachRoot ? attachRoot.GetComponentInParent<Rigidbody>() : GetComponentInParent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;
        var ab = attachRoot ? attachRoot.GetComponent<ArticulationBody>() : GetComponent<ArticulationBody>();
        if (ab == null) ab = attachRoot ? attachRoot.GetComponentInParent<ArticulationBody>() : GetComponentInParent<ArticulationBody>();
        if (ab != null) ab.immovable = false;
        var target = attachRoot != null ? attachRoot : transform;
        target.SetParent(null, true);
        manager?.NotifyDetached(this, _attachedMarker);
        _attachedMarker = null;

        // Re-enable grabbing after detaching
        if (xrGrabInteractable is Behaviour beh)
        {
            beh.enabled = true;
        }
    }

    void ClearVelocities()
    {
        var ab = attachRoot ? attachRoot.GetComponent<ArticulationBody>() : GetComponent<ArticulationBody>();
        if (ab == null) ab = attachRoot ? attachRoot.GetComponentInParent<ArticulationBody>() : GetComponentInParent<ArticulationBody>();
        if (ab != null)
        {
            ab.linearVelocity = Vector3.zero;
            ab.angularVelocity = Vector3.zero;
        }
        else
        {
            var rb = attachRoot ? attachRoot.GetComponent<Rigidbody>() : GetComponent<Rigidbody>();
            if (rb == null) rb = attachRoot ? attachRoot.GetComponentInParent<Rigidbody>() : GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.Sleep();
            }
        }
    }

    Vector3 GetProbePosition()
    {
        var p = proximityPoint != null ? proximityPoint : transform;
        return p.position;
    }

    void MoveRootTo(Vector3 pos, Quaternion rot)
    {
        var t = attachRoot != null ? attachRoot : transform;
        t.position = pos;
        t.rotation = rot;
    }

    Transform ResolveNearestCandidate()
    {
        if (useTriggerDetection && _triggerNearest != null) return _triggerNearest;
        return manager != null ? manager.GetNearestMarker(GetProbePosition(), attachRadius) : null;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!useTriggerDetection) return;
        if (!string.IsNullOrEmpty(markerTag) && !other.CompareTag(markerTag)) return;
        if (((1 << other.gameObject.layer) & markerLayers) == 0) return;
        _triggerNearest = other.transform;
    }

    void OnTriggerStay(Collider other)
    {
        if (!useTriggerDetection) return;
        if (!string.IsNullOrEmpty(markerTag) && !other.CompareTag(markerTag)) return;
        if (((1 << other.gameObject.layer) & markerLayers) == 0) return;
        _triggerNearest = other.transform;
    }

    void OnTriggerExit(Collider other)
    {
        if (!useTriggerDetection) return;
        if (_triggerNearest == other.transform) _triggerNearest = null;
    }

    public void ForceRespawn()
    {
        if (respawnMarker == null) EnsureRespawnMarker();
        if (respawnMarker == null) return;
        ClearVelocities();
        DetachFromMarker();
        MoveRootTo(respawnMarker.position, respawnMarker.rotation);
    }
}
