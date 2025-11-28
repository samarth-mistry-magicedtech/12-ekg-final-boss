#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SL12.Editor
{
    public static class SL12AutoWireEditor
    {
        private const string PadPrefabNameA = "EKG_Pad_With_Back";
        private const string PadPrefabNameB = "EKG_Pad_With_Backing";
        private const string BackingChildName = "EKG_Backing";
        private const string PeeledBackingPrefabName = "EKG_Backing_Peeled";
        private const string TrayName = "KeyTray1";
        private static readonly string[] MarkerNames = { "RA","LA","RL","LL","V1","V2","V3","V4","V5","V6" };

        [MenuItem("Tools/SL12/Wire Prefabs (Pad + Peeled Backing)")]
        public static void WirePrefabs()
        {
            var peeled = FindPrefabByName(PeeledBackingPrefabName);
            if (peeled == null)
            {
                Debug.LogWarning($"Could not find prefab '{PeeledBackingPrefabName}'. Place it under Assets/Prefab.");
            }

            // Try both pad prefab name variants
            var padA = FindPrefabByName(PadPrefabNameA);
            var padB = FindPrefabByName(PadPrefabNameB);

            if (padA == null && padB == null)
            {
                Debug.LogWarning($"No pad prefab found. Expected '{PadPrefabNameA}.prefab' or '{PadPrefabNameB}.prefab' under Assets/Prefab.");
            }

            if (padA != null) WirePadPrefab(padA, peeled);
            if (padB != null) WirePadPrefab(padB, peeled);

            var peeledPrefab = FindPrefabByName(PeeledBackingPrefabName);
            if (peeledPrefab != null) WirePeeledBackingPrefab(peeledPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("SL12: Prefab wiring complete.");
        }

        [MenuItem("Tools/SL12/Wire Scene (Pads, Tray, Markers)")]
        public static void WireScene()
        {
            var peeled = FindPrefabByName(PeeledBackingPrefabName);

            EnsurePadManager(peeled);
            WireAllPadInstancesInScene(peeled);
            WireTrayInScene();
            WireMarkersInScene();

            MarkActiveSceneDirty();
            Debug.Log("SL12: Scene wiring complete.");
        }

        [MenuItem("Tools/SL12/Full Auto-Wire (Prefabs + Scene)")]
        public static void FullAutoWire()
        {
            WirePrefabs();
            WireScene();
        }

        // ----- Prefab wiring -----
        private static void WirePadPrefab(GameObject prefabAsset, GameObject peeledBackingPrefab)
        {
            var path = AssetDatabase.GetAssetPath(prefabAsset);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                WirePadGameObject(root, peeledBackingPrefab);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void WirePeeledBackingPrefab(GameObject prefabAsset)
        {
            var path = AssetDatabase.GetAssetPath(prefabAsset);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                EnsurePeeledBackingComponents(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ----- Scene wiring -----
        private static void EnsurePadManager(GameObject peeledBackingPrefab)
        {
            var pm = Object.FindObjectOfType<PadManager>();
            if (pm == null)
            {
                var go = GameObject.Find("PadManager") ?? new GameObject("PadManager");
                pm = go.GetComponent<PadManager>();
                if (pm == null) pm = go.AddComponent<PadManager>();
            }
            if (pm.peeledBackingPrefab == null && peeledBackingPrefab != null)
                pm.peeledBackingPrefab = peeledBackingPrefab;
        }

        private static void WireAllPadInstancesInScene(GameObject peeledBackingPrefab)
        {
            var all = Object.FindObjectsOfType<Transform>(true);
            foreach (var t in all)
            {
                if (!t.name.StartsWith(PadPrefabNameA) && !t.name.StartsWith(PadPrefabNameB))
                    continue;
                WirePadGameObject(t.gameObject, peeledBackingPrefab);
            }
        }

        private static void WireTrayInScene()
        {
            var tray = GameObject.Find(TrayName);
            if (tray == null)
            {
                Debug.LogWarning($"Tray '{TrayName}' not found in scene. Create it with a child 'BackingSnap'.");
                return;
            }
            // Optional: tag assignment if tag exists
            try { tray.tag = TrayName; } catch { /* ignore if tag not present */ }
        }

        private static void WireMarkersInScene()
        {
            var all = Object.FindObjectsOfType<Transform>(true);
            foreach (var name in MarkerNames)
            {
                var tr = all.FirstOrDefault(x => x.name == name);
                if (tr == null) continue;
                EnsureMarkerComponents(tr.gameObject);
            }
        }

        // ----- Core wiring helpers -----
        private static void WirePadGameObject(GameObject go, GameObject peeledBackingPrefab)
        {
            // Root pad
            EnsureGrab(go);
            EnsureRigidbody(go, useGravity: false, isKinematic: true);
            EnsureCollider(go);

            var peel = go.GetComponent<EKGPadPeelInteraction>();
            if (peel == null) peel = Undo.AddComponent<EKGPadPeelInteraction>(go);

            // Backing child
            var backingTr = go.transform.Find(BackingChildName);
            if (backingTr != null)
            {
                var backing = backingTr.gameObject;
                EnsureGrab(backing);
                EnsureRigidbody(backing, useGravity: false, isKinematic: true);
                EnsureCollider(backing);
                peel.backingChild = backing;
            }
            else
            {
                Debug.LogWarning($"'{BackingChildName}' not found under {go.name}. Make sure your hierarchy is correct.");
            }

            if (peel.peeledBackingPrefab == null && peeledBackingPrefab != null)
                peel.peeledBackingPrefab = peeledBackingPrefab;

            // Ensure pad controller for respawn and prefab assignment
            var controller = go.GetComponent<EKGPadController>();
            if (controller == null) controller = Undo.AddComponent<EKGPadController>(go);
            if (controller.peelInteraction == null)
                controller.peelInteraction = peel;
            if (controller.peeledBackingPrefab == null && peeledBackingPrefab != null)
                controller.peeledBackingPrefab = peeledBackingPrefab;
        }

        private static void EnsureMarkerComponents(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null)
            {
                col = Undo.AddComponent<SphereCollider>(go);
                (col as SphereCollider).radius = 0.035f;
            }
            col.isTrigger = true;

            if (go.GetComponent<EKGMarker>() == null)
                Undo.AddComponent<EKGMarker>(go);
        }

        private static void EnsurePeeledBackingComponents(GameObject go)
        {
            EnsureGrab(go);
            EnsureRigidbody(go, useGravity: true, isKinematic: false);
            EnsureCollider(go);
            if (go.GetComponent<PeeledBacking>() == null)
                go.AddComponent<PeeledBacking>();
        }

        private static void EnsureGrab(GameObject go)
        {
            if (go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>() == null)
                Undo.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(go);
        }

        private static void EnsureRigidbody(GameObject go, bool useGravity, bool isKinematic)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = Undo.AddComponent<Rigidbody>(go);
            }
            rb.useGravity = useGravity;
            rb.isKinematic = isKinematic;
        }

        private static void EnsureCollider(GameObject go)
        {
            if (go.GetComponent<Collider>() == null)
                Undo.AddComponent<BoxCollider>(go);
        }

        // ----- Utilities -----
        private static GameObject FindPrefabByName(string name)
        {
            var guids = AssetDatabase.FindAssets($"t:Prefab {name}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.name == name)
                    return go;
            }
            return null;
        }

        private static void MarkActiveSceneDirty()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
#endif
