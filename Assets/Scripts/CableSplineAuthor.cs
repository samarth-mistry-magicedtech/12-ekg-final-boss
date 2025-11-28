using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer))]
public class CableSplineAuthor : MonoBehaviour
{
    public Transform startMarker;
    public Transform endMarker;
    public string startPath = "Environment1/EKG Mahcine/EKG_Cable_Splitter/Cable_Splitter_Marker";
    public string endPath   = "Environment1/EKG Mahcine/VGA_Plug/VGA_Plug_Marker";
    public Transform viaMarker;
    public string viaPath   = "VGA_Via_Marker";

    [Header("Curve (static)")]
    [Range(3, 11)] public int knotCount = 7;
    [Range(0f, 0.25f)] public float sagFraction = 0.06f; // fraction of chord length

    [Header("Extrude (tube)")]
    [Min(0.0001f)] public float thickness = 0.004f; // 4mm
    [Range(6, 64)] public int sides = 18;
    [Range(1, 64)] public int segmentsPerUnit = 12;
    [Header("Via Controls")]
    [Range(0.05f, 0.6f)] public float viaTangentScale = 0.25f; // fraction of segment length used for tangents at via
    [Range(1, 32)] public int subdivisionsPerSegment = 8; // used when via is present

    [Header("Physics/Collision")]
    public bool enableCollision = true;
    [Min(0.0001f)] public float slack = 1.08f;
    [Min(0f)] public float gravity = 0f;
    [Range(0f, 1f)] public float damping = 0.06f;
    [Range(1, 64)] public int physicsIterations = 16;
    [Min(0.0001f)] public float collisionRadius = 0.0f; // 0 = derive from thickness
    public LayerMask collisionMask; // leave 0 to auto-exclude Cables layer
    public bool anchorVia = true;

    SplineContainer container;
    Vector3 _lastStartW, _lastEndW, _lastViaW;
    int _lastKnotCount;
    float _lastSagFraction;
    bool _lastViaUsed;
    int _lastSubdiv;
    Vector3[] _pos;
    Vector3[] _prev;
    float[] _rest;
    bool _simInit;

    void OnEnable()
    {
        EnsureRefs();
        BuildSpline();
        ConfigureExtrude();
        SnapshotCurrent();
    }

    void ResolveEdgeCollisions(float radius, int viaIndex)
    {
        if (!enableCollision || _pos == null) return;
        int cables = LayerMask.NameToLayer("Cables");
        int exclude = (cables >= 0) ? (1 << cables) : 0;
        int mask = (collisionMask.value != 0) ? collisionMask.value : (~exclude);
        int n = _pos.Length;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p1 = _pos[i];
            Vector3 p2 = _pos[i + 1];
            Vector3 dir = p2 - p1;
            float len = dir.magnitude;
            if (len <= 1e-5f) continue;
            Vector3 dn = dir / len;
            if (Physics.SphereCast(p1, radius, dn, out var hit, len, mask, QueryTriggerInteraction.Ignore))
            {
                Vector3 nrm = hit.normal;
                float push = Mathf.Max(0.0005f, radius * 0.25f);
                bool a1 = (i == 0) || (i == viaIndex);
                bool a2 = (i + 1 == n - 1) || (i + 1 == viaIndex);
                if (!a1) _pos[i] += nrm * push;
                if (!a2) _pos[i + 1] += nrm * push;
            }
        }
    }

    void OnValidate()
    {
        EnsureRefs();
        ConfigureExtrude();
        RebuildIfNeeded();
    }

    void EnsureRefs()
    {
        if (container == null) container = GetComponent<SplineContainer>();
        if (startMarker == null) startMarker = FindByPath(startPath);
        if (endMarker == null) endMarker = FindByPath(endPath);
        if (viaMarker == null) viaMarker = FindByPath(viaPath);
    }

    Transform FindByPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var parts = path.Split('/');
        if (parts.Length == 0) return null;
        var root = GameObject.Find(parts[0]);
        if (!root) return null;
        Transform t = root.transform;
        if (parts.Length > 1)
        {
            var rel = string.Join("/", parts, 1, parts.Length - 1);
            t = t.Find(rel);
        }
        return t;
    }

    void BuildSpline()
    {
        if (!startMarker || !endMarker || container == null) return;
        var s = container.Spline; // default spline
        if (s == null) return;
        s.Clear();

        Vector3 aW = startMarker.position;
        Vector3 bW = endMarker.position;
        Vector3? vW = viaMarker ? (Vector3?)viaMarker.position : null;

        if (vW.HasValue)
        {
            // Subdivide both segments to create many knots for physics/collision
            Vector3 p0 = aW;
            Vector3 p1 = vW.Value;
            Vector3 p2 = bW;
            int sub = Mathf.Clamp(subdivisionsPerSegment, 1, 32);
            int total = sub + 1 + sub; // includes via once
            Vector3[] pts = new Vector3[total];
            int idx = 0;
            for (int i = 0; i <= sub; i++)
            {
                float t = (float)i / sub;
                pts[idx++] = Vector3.Lerp(p0, p1, t);
            }
            for (int i = 1; i <= sub; i++)
            {
                float t = (float)i / sub;
                pts[idx++] = Vector3.Lerp(p1, p2, t);
            }

            float scale = Mathf.Clamp(viaTangentScale, 0.05f, 0.6f);
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 cur = pts[i];
                Vector3 prev = (i > 0) ? pts[i - 1] : pts[i];
                Vector3 next = (i < pts.Length - 1) ? pts[i + 1] : pts[i];
                Vector3 inW = cur - prev;
                Vector3 outW = next - cur;
                float inL = inW.magnitude;
                float outL = outW.magnitude;
                Vector3 tinL = container.transform.InverseTransformVector((inL > 1e-6f ? (inW / inL) : Vector3.zero) * inL * scale);
                Vector3 toutL = container.transform.InverseTransformVector((outL > 1e-6f ? (outW / outL) : Vector3.zero) * outL * scale);
                Vector3 pL = container.transform.InverseTransformPoint(cur);
                s.Add(new BezierKnot((float3)pL, (float3)tinL, (float3)toutL, quaternion.identity));
            }
        }
        else
        {
            // Fallback: evenly spaced knots with gentle static sag
            float len = Vector3.Distance(aW, bW);
            if (len < 1e-5f) len = 1f;
            Vector3 dirW = (bW - aW).normalized;
            Vector3 sagW = Vector3.down * Mathf.Clamp(len * sagFraction, 0f, 0.08f);

            int count = Mathf.Clamp(knotCount, 3, 11);
            for (int i = 0; i < count; i++)
            {
                float t = (count == 1) ? 0f : i / (float)(count - 1);
                Vector3 pW = Vector3.Lerp(aW, bW, t);
                float w = Mathf.Sin(Mathf.PI * t);
                pW += sagW * (w * w);

                Vector3 pL = container.transform.InverseTransformPoint(pW);
                Vector3 tanL = container.transform.InverseTransformVector(dirW) * (len * 0.1f);
                var knot = new BezierKnot((float3)pL,
                                          (float3)(-tanL),
                                          (float3)( tanL),
                                          quaternion.identity);
                s.Add(knot);
            }
        }
    }

    void ConfigureExtrude()
    {
        var extrude = GetComponent<SplineExtrude>();
        if (!extrude) extrude = gameObject.AddComponent<SplineExtrude>();
        extrude.Sides = Mathf.Clamp(sides, 6, 64);
        extrude.Radius = Mathf.Max(0.0001f, thickness * 0.5f);
        extrude.SegmentsPerUnit = Mathf.Clamp(segmentsPerUnit, 1, 64);
        extrude.Capped = true;

        // Ensure MeshRenderer exists and is set
        var mr = GetComponent<MeshRenderer>();
        if (mr)
        {
            // Try to use an existing material named M_CableWhite
            var mats = Resources.FindObjectsOfTypeAll<Material>();
            Material white = null;
            for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name == "M_CableWhite") { white = mats[i]; break; }
            if (white == null)
            {
                white = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                white.name = "M_CableWhite";
                white.color = Color.white;
                white.SetFloat("_Smoothness", 0.6f);
            }
            mr.sharedMaterial = white;
        }

        // Ensure no collider so cables pass through other cables
        var mc = GetComponent<MeshCollider>();
        if (mc) DestroyImmediate(mc);
    }

#if UNITY_EDITOR
    void Update()
    {
        if (!Application.isPlaying)
        {
            EnsureRefs();
            RebuildIfNeeded();
            SimulateAndUpdate();
        }
    }
#endif

    void RebuildIfNeeded()
    {
        if (!startMarker || !endMarker || container == null) return;
        bool viaUsed = viaMarker != null;
        Vector3 aW = startMarker.position;
        Vector3 bW = endMarker.position;
        Vector3 vW = viaUsed ? viaMarker.position : Vector3.zero;

        bool moved = (_lastStartW - aW).sqrMagnitude > 1e-6f || (_lastEndW - bW).sqrMagnitude > 1e-6f;
        bool viaMoved = viaUsed && (_lastViaW - vW).sqrMagnitude > 1e-6f;
        bool shapeChanged = _lastKnotCount != knotCount || Mathf.Abs(_lastSagFraction - sagFraction) > 1e-6f || _lastViaUsed != viaUsed || _lastSubdiv != subdivisionsPerSegment;

        if (moved || viaMoved || shapeChanged)
        {
            BuildSpline();
            SnapshotCurrent();
        }
    }

    void SnapshotCurrent()
    {
        _lastStartW = startMarker ? startMarker.position : Vector3.zero;
        _lastEndW   = endMarker ? endMarker.position : Vector3.zero;
        _lastViaW   = viaMarker ? viaMarker.position : Vector3.zero;
        _lastKnotCount = knotCount;
        _lastSagFraction = sagFraction;
        _lastViaUsed = viaMarker != null;
        _lastSubdiv = subdivisionsPerSegment;
    }

    void EnsureSimArraysFromSpline()
    {
        var s = container ? container.Spline : null;
        if (s == null) return;
        int n = s.Count;
        if (_pos == null || _pos.Length != n)
        {
            _pos = new Vector3[n];
            _prev = new Vector3[n];
            _rest = new float[Mathf.Max(0, n - 1)];
            _simInit = false;
        }

        if (!_simInit)
        {
            for (int i = 0; i < n; i++)
            {
                var k = s[i];
                _pos[i] = container.transform.TransformPoint((Vector3)k.Position);
                _prev[i] = _pos[i];
            }
            for (int i = 0; i < n - 1; i++)
            {
                _rest[i] = Vector3.Distance(_pos[i], _pos[i + 1]) * Mathf.Max(1.0f, slack);
            }
            _simInit = true;
        }
    }

    void SimulateAndUpdate()
    {
        if (!container) return;
        var s = container.Spline;
        if (s == null || s.Count < 2) return;
        EnsureSimArraysFromSpline();
        int n = s.Count;

        // Anchors
        int viaIndex = -1;
        if (viaMarker && anchorVia)
        {
            float best = float.MaxValue;
            Vector3 v = viaMarker.position;
            for (int i = 0; i < n; i++)
            {
                float d = (_pos[i] - v).sqrMagnitude;
                if (d < best) { best = d; viaIndex = i; }
            }
        }

        float dt = 1f / 60f;
        Vector3 g = Vector3.down * gravity * dt * dt;
        float damp = Mathf.Clamp01(damping);

        // Verlet integration for interior (non-anchored) points
        for (int i = 0; i < n; i++)
        {
            bool anchored = (i == 0) || (i == n - 1) || (i == viaIndex);
            if (anchored) continue;
            Vector3 cur = _pos[i];
            Vector3 vel = (_pos[i] - _prev[i]) * (1.0f - damp);
            Vector3 next = cur + vel + g;
            _prev[i] = cur;
            _pos[i] = next;
        }

        // Project constraints and collisions
        int it = Mathf.Max(1, physicsIterations);
        float rad = (collisionRadius > 0f) ? collisionRadius : Mathf.Max(0.0001f, thickness * 0.6f);
        for (int k = 0; k < it; k++)
        {
            // Distance constraints
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p1 = _pos[i];
                Vector3 p2 = _pos[i + 1];
                Vector3 d = p2 - p1;
                float L = d.magnitude;
                if (L <= 1e-6f) continue;
                float target = _rest[i];
                float diff = (L - target) / L;
                bool a1 = (i == 0) || (i == viaIndex);
                bool a2 = (i + 1 == n - 1) || (i + 1 == viaIndex);
                if (!a1) _pos[i] += d * diff * 0.5f;
                if (!a2) _pos[i + 1] -= d * diff * 0.5f;
            }

            // Re-anchor
            _pos[0] = startMarker ? startMarker.position : _pos[0];
            _pos[n - 1] = endMarker ? endMarker.position : _pos[n - 1];
            if (viaIndex >= 0 && viaMarker) _pos[viaIndex] = viaMarker.position;

            ResolveCollisions(rad, viaIndex);
            ResolveEdgeCollisions(rad, viaIndex);
        }

        // Push back to spline with auto-smooth-like tangents
        s.Clear();
        for (int i = 0; i < n; i++)
        {
            Vector3 p = _pos[i];
            Vector3 pl = container.transform.InverseTransformPoint(p);
            Vector3 prev = (i > 0) ? _pos[i - 1] : _pos[i];
            Vector3 next = (i < n - 1) ? _pos[i + 1] : _pos[i];
            Vector3 tinW = (p - prev);
            Vector3 toutW = (next - p);
            float tinLen = tinW.magnitude;
            float toutLen = toutW.magnitude;
            float scale = Mathf.Clamp(viaTangentScale, 0.05f, 0.6f);
            Vector3 tinL = container.transform.InverseTransformVector((tinLen > 1e-6f ? (tinW / tinLen) : Vector3.zero) * tinLen * scale);
            Vector3 toutL = container.transform.InverseTransformVector((toutLen > 1e-6f ? (toutW / toutLen) : Vector3.zero) * toutLen * scale);
            var knot = new BezierKnot((float3)pl, (float3)tinL, (float3)toutL, quaternion.identity);
            s.Add(knot);
        }
    }

    void ResolveCollisions(float radius, int viaIndex)
    {
        if (!enableCollision || _pos == null) return;
        int cables = LayerMask.NameToLayer("Cables");
        int exclude = (cables >= 0) ? (1 << cables) : 0;
        int mask = (collisionMask.value != 0) ? collisionMask.value : (~exclude);
        for (int i = 0; i < _pos.Length; i++)
        {
            if (i == 0 || i == _pos.Length - 1 || (i == viaIndex)) continue;
            Vector3 p = _pos[i];
            var cols = Physics.OverlapSphere(p, radius, mask, QueryTriggerInteraction.Ignore);
            if (cols == null || cols.Length == 0) continue;
            for (int c = 0; c < cols.Length; c++)
            {
                var col = cols[c];
                if (!col || !col.enabled) continue;
                if (cables >= 0 && col.gameObject.layer == cables) continue;
                bool useBounds = (col is TerrainCollider) || (col is MeshCollider mc && !mc.convex);
                Vector3 closest = useBounds ? col.bounds.ClosestPoint(p) : col.ClosestPoint(p);
                Vector3 delta = p - closest;
                float d = delta.magnitude;
                float pen = radius - d;
                if (pen > 1e-5f)
                {
                    Vector3 n = (d > 1e-5f) ? (delta / d) : (p - col.bounds.center).normalized;
                    if (n.sqrMagnitude < 1e-6f) n = Vector3.up;
                    _pos[i] = p + n * pen;
                }
            }
        }
    }
}
