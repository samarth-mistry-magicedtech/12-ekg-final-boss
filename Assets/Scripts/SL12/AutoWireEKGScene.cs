#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace SL12
{
    [ExecuteAlways]
    public class AutoWireEKGScene : MonoBehaviour
    {
        [Tooltip("Optional explicit reference. If null, will try to locate 'EKG_Backing_Peeled' asset in Editor.")]
        public GameObject peeledBackingPrefab;

        [Tooltip("Run only once per scene load.")]
        public bool runOnce = true;

        bool _didRun = false;

        void OnEnable()
        {
            if (!Application.isPlaying && runOnce && _didRun) return;
            TryRun();
        }

        void Start()
        {
            if (runOnce && _didRun) return;
            TryRun();
        }

        void TryRun()
        {
            // Find peeled backing prefab if not provided
            if (peeledBackingPrefab == null)
            {
#if UNITY_EDITOR
                peeledBackingPrefab = FindPrefabByName("EKG_Backing_Peeled");
#endif
            }

            // Ensure PadManager exists and set reference
            var pm = FindObjectOfType<PadManager>();
            if (pm == null)
            {
                var go = GameObject.Find("PadManager") ?? new GameObject("PadManager");
                pm = go.GetComponent<PadManager>();
                if (pm == null) pm = go.AddComponent<PadManager>();
            }
            if (pm != null && pm.peeledBackingPrefab == null)
                pm.peeledBackingPrefab = peeledBackingPrefab;

            // Wire all pads
            WireAllPads(pm?.peeledBackingPrefab);

            // Wire tray (tag optional; snapping also works by name)
            var tray = GameObject.Find("KeyTray1");
            if (tray != null)
            {
                // no-op (name matching is enough)
            }

            // Wire markers by known names
            string[] markerNames = new[] { "RA","LA","RL","LL","V1","V2","V3","V4","V5","V6" };
            foreach (var name in markerNames)
                WireMarkerByName(name);

            _didRun = true;
        }

        void WireAllPads(GameObject peeledPrefab)
        {
            var allTransforms = FindObjectsOfType<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (t.name != "EKG_Pad_With_Back") continue;
                var go = t.gameObject;

                // Root pad components
                var grab = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                if (grab == null) grab = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                var rb = go.GetComponent<Rigidbody>();
                if (rb == null) rb = go.AddComponent<Rigidbody>();
                rb.useGravity = true; rb.isKinematic = false;
                if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();

                var peel = go.GetComponent<EKGPadPeelInteraction>();
                if (peel == null) peel = go.AddComponent<EKGPadPeelInteraction>();

                // Backing child
                var backing = t.Find("EKG_Backing");
                if (backing != null)
                {
                    var bgo = backing.gameObject;
                    var bgrab = bgo.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                    if (bgrab == null) bgrab = bgo.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                    var brb = bgo.GetComponent<Rigidbody>();
                    if (brb == null) brb = bgo.AddComponent<Rigidbody>();
                    brb.useGravity = false; brb.isKinematic = true;
                    if (bgo.GetComponent<Collider>() == null) bgo.AddComponent<BoxCollider>();
                }

                // Assign fields
                peel.backingChild = backing != null ? backing.gameObject : peel.backingChild;
                if (peel.peeledBackingPrefab == null)
                    peel.peeledBackingPrefab = peeledPrefab;

                // Ensure pad controller exists for respawn and prefab assignment
                var controller = go.GetComponent<EKGPadController>();
                if (controller == null) controller = go.AddComponent<EKGPadController>();
                if (controller.peelInteraction == null)
                    controller.peelInteraction = peel;
                if (controller.peeledBackingPrefab == null)
                    controller.peeledBackingPrefab = peeledPrefab;
            }
        }

        void WireMarkerByName(string name)
        {
            var all = FindObjectsOfType<Transform>(true);
            var tr = all.FirstOrDefault(x => x.name == name);
            if (tr == null) return;

            var go = tr.gameObject;
            var col = go.GetComponent<Collider>();
            if (col == null) col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;

            if (go.GetComponent<EKGMarker>() == null)
                go.AddComponent<EKGMarker>();
        }

#if UNITY_EDITOR
        GameObject FindPrefabByName(string assetName)
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var obj in all)
            {
                if (obj.name == assetName)
                {
                    // Ensure this is an Asset (not scene instance)
                    var path = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(path))
                        return obj;
                }
            }
            return null;
        }
#endif
    }
}
