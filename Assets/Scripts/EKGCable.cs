using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class EcgCableSpline : MonoBehaviour
{
    [Header("Endpoints")]
    [SerializeField] private Transform electrode;
    [SerializeField] private Transform ekgMachine;
    [SerializeField] private bool useElectrodeChildIfPresent = true;
    [SerializeField] private string electrodeChildName = "tail";

    [Header("Curve & Dip")]
    [SerializeField, Range(0f, 1f)] private float curveTension = 0.5f;
    [SerializeField, Range(0f, 1f)] private float dipAmount = 0.2f;
    [SerializeField] private AnimationCurve verticalDipProfile = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private Transform dipReference; // optional space for dip axis
    [SerializeField] private Vector3 dipAxisLocal = Vector3.down; // dip direction in dipReference local (or world if null)
    [SerializeField] private DipDirection dipDirection = DipDirection.Down; // pick a basis from reference transform

    [Header("Obstacle Avoidance")]
    [SerializeField] private bool enableObstacleAvoidance = true;
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [SerializeField] private float obstacleClearance = 0.03f;
    [SerializeField, Range(0f, 1f)] private float maxObstacleInfluence = 0.6f;
    [SerializeField] private bool ignoreCablesLayer = true;
    [SerializeField, Min(1)] private int avoidanceIterations = 10;

    [Header("Sampling")]
    [Min(2)] [SerializeField] private int segments = 64;

    [Header("Mesh")]
    [SerializeField] private float tubeRadius = 0.005f;
    [Range(3, 16)] [SerializeField] private int radialSegments = 8;
    [SerializeField] private Material tubeMaterial;

    [Header("Editor Updates")]
    [SerializeField] private bool autoUpdateInEditor = true;
    [SerializeField] private bool autoUpdateInPlay = true;

    private SplineContainer splineContainer;
    private Mesh cableMesh;
    private Vector3 _lastElectrodePos;
    private Vector3 _lastEkgPos;
    private Vector3 _lastDipPos;
    private bool _hasCache;

    void Awake()
    {
        EnsureSplineContainer();
        EnsureMesh();
        UpdateCable();
    }

    void Reset()
    {
        EnsureSplineContainer();
        EnsureMesh();
    }

    void OnEnable()
    {
        EnsureSplineContainer();
        EnsureMesh();
        _hasCache = false;
        UpdateCable();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        UpdateCable();
    }

    void Update()
    {
        if (!Application.isPlaying && autoUpdateInEditor)
        {
            UpdateCable();
        }
    }
#endif
    void LateUpdate()
    {
        if (!Application.isPlaying) return;
        if (ekgMachine == null) return;
        Transform e = ResolveElectrodeTransform();
        if (autoUpdateInPlay)
        {
            bool moved = !_hasCache
                || (_lastElectrodePos - e.position).sqrMagnitude > 1e-6f
                || (_lastEkgPos - ekgMachine.position).sqrMagnitude > 1e-6f
                || (dipReference && (_lastDipPos - dipReference.position).sqrMagnitude > 1e-6f);
            if (moved)
            {
                UpdateCable();
                _lastElectrodePos = e.position;
                _lastEkgPos = ekgMachine.position;
                _lastDipPos = dipReference ? dipReference.position : Vector3.zero;
                _hasCache = true;
            }
        }
    }

    void EnsureSplineContainer()
    {
        if (splineContainer == null)
        {
            splineContainer = GetComponent<SplineContainer>();
        }
    }

    void EnsureMesh()
    {
        var meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
            return;

        if (cableMesh == null)
        {
            cableMesh = meshFilter.sharedMesh;
            if (cableMesh == null)
            {
                cableMesh = new Mesh
                {
                    name = "EcgCableMesh"
                };
                meshFilter.sharedMesh = cableMesh;
            }
        }

        var meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null && tubeMaterial != null)
        {
            meshRenderer.sharedMaterial = tubeMaterial;
        }
    }
    Transform ResolveElectrodeTransform()
    {
        Transform baseT = electrode != null ? electrode : transform;
        if (!useElectrodeChildIfPresent || baseT == null) return baseT;
        if (!string.IsNullOrEmpty(electrodeChildName))
        {
            var child = baseT.Find(electrodeChildName);
            if (child != null) return child;
        }
        return baseT;
    }

    

    public void UpdateCable()
    {
        if (ekgMachine == null)
            return;

        EnsureSplineContainer();
        if (splineContainer == null)
            return;

        Transform e = ResolveElectrodeTransform();
        Vector3 p0 = e.position;
        Vector3 p3 = ekgMachine.position;

        Vector3 dir = p3 - p0;
        float distance = dir.magnitude;
        if (distance < 0.0001f)
            return;

        // Resolve dip direction from reference
        Vector3 dipNorm = Vector3.down;
        bool hasMidRef = dipReference != null;
        if (!hasMidRef)
        {
            Vector3 dipDir = ResolveDipDirection();
            dipNorm = dipDir.normalized;
        }

        // Base mid-point sag based on cable length and dipAmount in chosen dip direction
        Vector3 mid = hasMidRef ? dipReference.position : ( (p0 + p3) * 0.5f - dipNorm * (dipAmount * distance) );

        if (enableObstacleAvoidance && !hasMidRef)
        {
            ApplyObstacleAvoidance(p0, p3, distance, ref mid, dipNorm);
        }

        // Build 3-point spline in local space with autosmooth tangents
        Vector3 localP0 = splineContainer.transform.InverseTransformPoint(p0);
        Vector3 localMid = splineContainer.transform.InverseTransformPoint(mid);
        Vector3 localP3 = splineContainer.transform.InverseTransformPoint(p3);

        // Ensure we have at least one spline in the container
        if (splineContainer.Splines.Count == 0)
        {
            splineContainer.AddSpline();
        }

        // Get the spline from the container and modify its knots in-place
        Spline spline = splineContainer.Splines[0];
        spline.Clear();

        BezierKnot k0 = new BezierKnot((float3)localP0);
        BezierKnot k1 = new BezierKnot((float3)localMid);
        BezierKnot k2 = new BezierKnot((float3)localP3);

        spline.Add(k0);
        spline.Add(k1);
        spline.Add(k2);

        // Let Splines auto-compute smooth Bezier tangents
        spline.SetTangentMode(TangentMode.AutoSmooth);

        int count = Mathf.Max(2, segments + 1);
        Vector3[] positions = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            float tNorm = i / (float)(count - 1);           // 0..1 along full cable
            float3 splinePos = SplineUtility.EvaluatePosition(spline, tNorm);
            Vector3 pos = splineContainer.transform.TransformPoint((Vector3)splinePos);

            if (dipReference == null && verticalDipProfile != null && verticalDipProfile.keys != null && verticalDipProfile.keys.Length > 0)
            {
                float profile = verticalDipProfile.Evaluate(tNorm);
                pos -= dipNorm * (profile * dipAmount * 0.1f * distance);
            }

            positions[i] = pos;
        }

        if (enableObstacleAvoidance)
        {
            bool anchorMid = dipReference != null;
            AdjustSamplesForObstacles(positions, tubeRadius + obstacleClearance, avoidanceIterations, anchorMid, anchorMid ? dipReference.position : Vector3.zero, p0, p3);
        }

        BuildCableMesh(positions);
    }

    void ApplyObstacleAvoidance(
        Vector3 start,
        Vector3 end,
        float distance,
        ref Vector3 mid,
        Vector3 dipDir)
    {
        Vector3 limitedEnd = Vector3.Lerp(start, end, maxObstacleInfluence);

        int mask = FinalObstacleMask();
        if (Physics.Linecast(start, limitedEnd, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore))
        {
            Vector3 forward = (end - start).normalized;
            Vector3 side = Vector3.Cross(hit.normal, forward).normalized;
            Vector3 avoidanceOffset =
                hit.normal * obstacleClearance +
                side * (obstacleClearance * 0.5f);

            Vector3 obstaclePoint = hit.point + avoidanceOffset;

            float influence = 0.8f;
            Vector3 saggedMid = (start + end) * 0.5f - dipDir * (dipAmount * distance);
            mid = Vector3.Lerp(saggedMid, obstaclePoint, influence);
        }
    }

    int FinalObstacleMask()
    {
        if (!ignoreCablesLayer) return obstacleLayers;
        int cables = LayerMask.NameToLayer("Cables");
        if (cables < 0) return obstacleLayers;
        int exclude = 1 << cables;
        return obstacleLayers & ~exclude;
    }

    void AdjustSamplesForObstacles(Vector3[] positions, float clearanceRadius, int iters, bool anchorMid, Vector3 midAnchor, Vector3 startAnchor, Vector3 endAnchor)
    {
        if (positions == null || positions.Length < 2) return;
        int n = positions.Length;
        float rad = Mathf.Max(0.0005f, clearanceRadius);
        int mask = FinalObstacleMask();

        // Rest lengths to keep spacing
        float[] rest = new float[n - 1];
        for (int i = 0; i < n - 1; i++) rest[i] = Vector3.Distance(positions[i], positions[i + 1]);

        int midIndex = n / 2;
        for (int it = 0; it < Mathf.Max(1, iters); it++)
        {
            // Point collisions
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 p = positions[i];
                var cols = Physics.OverlapSphere(p, rad, mask, QueryTriggerInteraction.Ignore);
                if (cols != null)
                {
                    for (int c = 0; c < cols.Length; c++)
                    {
                        var col = cols[c];
                        if (!col || !col.enabled) continue;
                        bool useBounds = (col is TerrainCollider) || (col is MeshCollider mc && !mc.convex);
                        Vector3 closest = useBounds ? col.bounds.ClosestPoint(p) : col.ClosestPoint(p);
                        Vector3 delta = p - closest;
                        float d = delta.magnitude;
                        float pen = rad - d;
                        if (pen > 1e-5f)
                        {
                            Vector3 nrm = (d > 1e-5f) ? (delta / d) : (p - col.bounds.center).normalized;
                            if (nrm.sqrMagnitude < 1e-6f) nrm = Vector3.up;
                            p += nrm * pen;
                        }
                    }
                }
                positions[i] = p;
            }

            // Edge spherecasts
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p1 = positions[i];
                Vector3 p2 = positions[i + 1];
                Vector3 d = p2 - p1;
                float len = d.magnitude;
                if (len <= 1e-5f) continue;
                Vector3 dn = d / len;
                if (Physics.SphereCast(p1, rad, dn, out RaycastHit hit, len, mask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 push = hit.normal * Mathf.Max(0.00025f, rad * 0.25f);
                    if (i > 0) positions[i] += push;
                    if (i + 1 < n - 1) positions[i + 1] += push;
                }
            }

            // Re-anchor ends
            positions[0] = startAnchor;
            positions[n - 1] = endAnchor;
            if (anchorMid) positions[midIndex] = midAnchor;

            // Keep distances
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p1 = positions[i];
                Vector3 p2 = positions[i + 1];
                Vector3 d = p2 - p1;
                float L = d.magnitude;
                if (L <= 1e-6f) continue;
                float diff = (L - rest[i]) / L;
                bool a1 = (i == 0);
                bool a2 = (i + 1 == n - 1);
                if (!a1) positions[i] += d * diff * 0.5f;
                if (!a2) positions[i + 1] -= d * diff * 0.5f;
            }
            
            if (anchorMid) positions[midIndex] = midAnchor;
        }
    }

    void BuildCableMesh(Vector3[] positions)
    {
        if (positions == null || positions.Length < 2)
            return;

        EnsureMesh();
        if (cableMesh == null)
            return;

        // Work in the local space of this GameObject so the mesh aligns with the transform
        int ringCount = positions.Length;
        int ringVerts = radialSegments;

        int vertexCount = ringCount * ringVerts;
        int triangleCount = (ringCount - 1) * ringVerts * 2;

        Vector3[] verts = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        int[] tris = new int[triangleCount * 3];

        for (int i = 0; i < ringCount; i++)
        {
            // Convert world-space sampled position into local space for mesh vertices
            Vector3 center = transform.InverseTransformPoint(positions[i]);

            Vector3 tangent;
            if (i == 0)
            {
                tangent = (positions[i + 1] - positions[i]).normalized;
            }
            else if (i == ringCount - 1)
            {
                tangent = (positions[i] - positions[i - 1]).normalized;
            }
            else
            {
                tangent = (positions[i + 1] - positions[i - 1]).normalized;
            }

            Vector3 normal = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(normal, tangent)) > 0.9f)
            {
                normal = Vector3.forward;
            }

            Vector3 binormal = Vector3.Cross(tangent, normal).normalized;
            normal = Vector3.Cross(binormal, tangent).normalized;

            for (int j = 0; j < ringVerts; j++)
            {
                float angle = (j / (float)ringVerts) * Mathf.PI * 2f;
                Vector3 offset = Mathf.Cos(angle) * normal + Mathf.Sin(angle) * binormal;
                Vector3 vertex = center + offset * tubeRadius;

                int index = i * ringVerts + j;
                verts[index] = vertex;
                normals[index] = offset.normalized;
            }
        }

        int triIndex = 0;
        for (int i = 0; i < ringCount - 1; i++)
        {
            for (int j = 0; j < ringVerts; j++)
            {
                int current = i * ringVerts + j;
                int next = i * ringVerts + ((j + 1) % ringVerts);
                int currentUp = (i + 1) * ringVerts + j;
                int nextUp = (i + 1) * ringVerts + ((j + 1) % ringVerts);

                tris[triIndex++] = current;
                tris[triIndex++] = currentUp;
                tris[triIndex++] = nextUp;

                tris[triIndex++] = current;
                tris[triIndex++] = nextUp;
                tris[triIndex++] = next;
            }
        }

        cableMesh.Clear();
        cableMesh.SetVertices(verts);
        cableMesh.SetNormals(normals);
        cableMesh.SetTriangles(tris, 0);
        cableMesh.RecalculateBounds();
    }
    // Select dip direction using the reference transform's oriented axes
    private Vector3 ResolveDipDirection()
    {
        Vector3 v;
        if (dipReference)
        {
            switch (dipDirection)
            {
                case DipDirection.Up: v = dipReference.up; break;
                case DipDirection.Down: v = -dipReference.up; break;
                case DipDirection.Right: v = dipReference.right; break;
                case DipDirection.Left: v = -dipReference.right; break;
                case DipDirection.Forward: v = dipReference.forward; break;
                case DipDirection.Back: v = -dipReference.forward; break;
                case DipDirection.CustomLocalAxis: default: v = dipReference.TransformDirection(dipAxisLocal); break;
            }
        }
        else
        {
            switch (dipDirection)
            {
                case DipDirection.Up: v = Vector3.up; break;
                case DipDirection.Down: v = Vector3.down; break;
                case DipDirection.Right: v = Vector3.right; break;
                case DipDirection.Left: v = Vector3.left; break;
                case DipDirection.Forward: v = Vector3.forward; break;
                case DipDirection.Back: v = Vector3.back; break;
                case DipDirection.CustomLocalAxis: default: v = dipAxisLocal; break;
            }
        }
        if (v.sqrMagnitude < 1e-6f) v = Vector3.down;
        return v.normalized;
    }

    public enum DipDirection { Up, Down, Right, Left, Forward, Back, CustomLocalAxis }
}
