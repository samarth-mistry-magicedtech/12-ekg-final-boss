using UnityEngine;
using System.Linq;
using UnityEngine.XR.Interaction.Toolkit;

namespace SL12
{
    public class PadManager : MonoBehaviour
    {
        public static PadManager Instance { get; private set; }

        [Header("References")]
        public GameObject peeledBackingPrefab;

        [Header("Pad Counters")]
        public int totalPadsAvailable = 10;
        public int peeledCount = 0;
        public int placedCount = 0;
        public int backingsOnTray = 0;

        [Header("Auto-wire")]
        public bool autoWireOnStart = true;
        bool didAutoWire = false;

        void Awake()
        {
            if (!Application.isPlaying) return; // avoid editor-time side effects
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (autoWireOnStart && !didAutoWire)
                TryAutoWire();
        }

        void Start()
        {
            if (!Application.isPlaying) return;
            if (autoWireOnStart && !didAutoWire)
                TryAutoWire();
        }

        void TryAutoWire()
        {
#if UNITY_EDITOR
            if (peeledBackingPrefab == null)
                peeledBackingPrefab = FindPrefabByName("EKG_Backing_Peeled");
#endif

            WireAllPads(peeledBackingPrefab);

            var tray = GameObject.Find("KeyTray1");
            if (tray != null)
            {
                try { tray.tag = "KeyTray1"; } catch { /* tag may not exist; name fallback works */ }
            }

            string[] names = { "RA","LA","RL","LL","V1","V2","V3","V4","V5","V6" };
            foreach (var n in names) WireMarkerByName(n);

            didAutoWire = true;
        }

        void WireAllPads(GameObject peeledPrefab)
        {
            var allTransforms = FindObjectsOfType<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (!t.name.StartsWith("EKG_Pad_With_Back")) continue;
                var go = t.gameObject;

                var grab = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                if (grab == null) grab = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                var rb = go.GetComponent<Rigidbody>();
                if (rb == null) rb = go.AddComponent<Rigidbody>();
                rb.useGravity = false; rb.isKinematic = true;
                if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();

                var peel = go.GetComponent<EKGPadPeelInteraction>();
                if (peel == null) peel = go.AddComponent<EKGPadPeelInteraction>();

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
                    var path = UnityEditor.AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(path))
                        return obj;
                }
            }
            return null;
        }
#endif

        public void OnPadPeeled() { peeledCount = Mathf.Max(peeledCount + 1, 0); }
        public void OnBackingPlaced() { backingsOnTray = Mathf.Max(backingsOnTray + 1, 0); }
        public void OnPadPlaced() { placedCount = Mathf.Max(placedCount + 1, 0); }
    }
}
