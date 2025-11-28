using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class EKGTutorialPanelSetup
{
    const string PanelPath = "Environment1/Doctors_Office_Exam_Room/Doctors_Office_Structure/DO_Structure/DO_walls3/EKG_Ui_Canvas/EKG_Background";

    [MenuItem("Tools/EKG/Setup Tutorial UI Panel (Auto)")]
    static void SetupPanelAuto()
    {
        var panel = FindByPathAcrossScenes(PanelPath);
        if (panel == null)
        {
            Debug.LogWarning($"EKGTutorialPanelSetup: Panel not found at '{PanelPath}'. Select the panel and run 'Setup Tutorial UI Panel (From Selection)'.");
            return;
        }
        SetupOn(panel.gameObject);
    }

    [MenuItem("Tools/EKG/Setup Tutorial UI Panel (From Selection)")]
    static void SetupPanelFromSelection()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogWarning("EKGTutorialPanelSetup: Select the tutorial panel GameObject first.");
            return;
        }
        SetupOn(go);
    }

    static void SetupOn(GameObject panel)
    {
        Undo.RegisterFullObjectHierarchyUndo(panel, "Setup Tutorial UI Panel");

        var controller = panel.GetComponent<EKGTutorialSlideController>();
        if (controller == null) controller = Undo.AddComponent<EKGTutorialSlideController>(panel);

        var body = EnsureText(panel.transform, new[] { "BodyText", "Body", "MainText" }, 28); // main copy
        var footer = EnsureText(panel.transform, new[] { "FooterText", "Footer", "SubText" }, 18); // footer/subtext
        var progress = EnsureText(panel.transform, new[] { "ProgressText", "Progress", "StepText" }, 16); // progress

        var continueBtn = EnsureButton(panel.transform, new[] { "Continue", "Next", "ContinueButton" }, "Continue");
        var backBtn = FindButton(panel.transform, new[] { "Back", "Previous", "BackButton" });

        controller.bodyText = body;
        controller.footerText = footer;
        controller.progressText = progress;
        controller.continueButton = continueBtn;
        controller.backButton = backBtn;

        if (continueBtn != null)
        {
            var txt = continueBtn.GetComponentInChildren<Text>();
            if (txt != null) txt.text = "Continue";

            // Ensure XR proxy so controller buttons (A/Trigger) can advance even if UI raycast is not set up
            var proxy = continueBtn.GetComponent<XRButtonClickProxy>();
            if (proxy == null)
            {
                proxy = Undo.AddComponent<XRButtonClickProxy>(continueBtn.gameObject);
            }
            proxy.targetButton = continueBtn;
        }
        if (backBtn != null)
        {
            var txt = backBtn.GetComponentInChildren<Text>();
            if (txt != null) txt.text = "Back";
        }

        EditorUtility.SetDirty(controller);
        Debug.Log("EKGTutorialPanelSetup: Tutorial panel wired. Assignments done for Body/Footer/Progress/Continue/Back.");
    }

    static Component EnsureText(Transform parent, string[] names, int fontSize)
    {
        var t = FindText(parent, names);
        if (t != null) return t;

        var go = new GameObject(names[0]);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.1f, 0.5f);
        rt.anchorMax = new Vector2(0.9f, 0.9f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var text = go.AddComponent<Text>();
        text.color = Color.black;
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = fontSize;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    static Component FindText(Transform parent, string[] names)
    {
        foreach (var n in names)
        {
            var t = parent.Find(n);
            if (t == null) continue;
            var ui = t.GetComponent<Text>();
            if (ui != null) return ui;
            var any = t.GetComponent<Component>();
            if (any != null && any.GetType().GetProperty("text") != null) return any;
        }
        foreach (var tr in parent.GetComponentsInChildren<Transform>(true))
        {
            if (names.Any(n => tr.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var ui = tr.GetComponent<Text>();
                if (ui != null) return ui;
                var any = tr.GetComponent<Component>();
                if (any != null && any.GetType().GetProperty("text") != null) return any;
            }
        }
        return null;
    }

    static Button EnsureButton(Transform parent, string[] names, string label)
    {
        var b = FindButton(parent, names);
        if (b != null) return b;

        var go = new GameObject(names[0]);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.7f, 0.05f);
        rt.anchorMax = new Vector2(0.9f, 0.15f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.0f, 0.42f, 0.88f, 1f);
        var btn = go.AddComponent<Button>();

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 0);
        trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        var txt = textGo.AddComponent<Text>();
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.fontSize = 14;
        txt.text = label;

        return btn;
    }

    static Button FindButton(Transform parent, string[] names)
    {
        foreach (var n in names)
        {
            var t = parent.Find(n);
            if (t == null) continue;
            var b = t.GetComponent<Button>();
            if (b != null) return b;
        }
        foreach (var tr in parent.GetComponentsInChildren<Transform>(true))
        {
            if (names.Any(n => tr.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var b = tr.GetComponent<Button>();
                if (b != null) return b;
            }
        }
        return null;
    }

    static Transform FindByPathAcrossScenes(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var parts = path.Split('/');
        for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
            Transform current = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (string.Equals(go.name, parts[0], StringComparison.OrdinalIgnoreCase)) { current = go.transform; break; }
            }
            if (current == null) continue;
            for (int i = 1; i < parts.Length; i++)
            {
                current = current.Find(parts[i]);
                if (current == null) break;
            }
            if (current != null) return current;
        }
        return null;
    }
}
