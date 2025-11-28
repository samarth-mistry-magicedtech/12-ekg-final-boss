using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public class EKGTutorialSlideController : MonoBehaviour
{
    [Serializable]
    public class Slide
    {
        [TextArea(2, 5)] public string body;
        [TextArea(1, 3)] public string footer;
        public bool requirePads10;
        public bool requireLeads10;
        public bool requireMachineOn;
    }

    public Component bodyText;
    public Component footerText;
    public Component progressText;
    public Button continueButton;
    public Button backButton;

    public AudioSource narrationSource;
    public List<AudioClip> narrationClips = new List<AudioClip>();
    public bool fallbackToFirstClip = false;
    public bool autoPlayNarrationOnAdvance = true;
    public bool autoPlayOnStart = true;
    [Range(0f, 5f)] public float startNarrationDelay = 1.0f;

    public List<Slide> slides = new List<Slide>();

    public bool allowKeyboardAdvance = false;
    public KeyCode advanceKey = KeyCode.Space;
    public KeyCode backKey = KeyCode.Backspace;

    [Header("XR Input")]
    public bool useXRInput = true;
    public XRNode continueXRNode = XRNode.RightHand;
    bool prevXRPressed = false;
    [Header("Input Debounce")]
    [Range(0f, 1f)] public float minAdvanceInterval = 0.2f;
    [Range(0f, 1f)] public float xrIgnoreDuration = 0.2f;
    float lastAdvanceTime = -1f;
    float xrIgnoreUntil = -1f;

    [Header("Arrow Indicator")]
    public GameObject arrowPrefab;
    [Range(0f, 1f)] public float arrowYOffset = 0.2f;
    [Range(0f, 1f)] public float secondArrowExtraYOffset = 0.1f;
    public Color arrowColor = Color.red;
    public string firstArrowMarkerName = "FirstArrowMarker";
    public string secondArrowMarkerName = "SecondArrowMarker";
    public string powerButtonPath = "Environment1/ECG_Machine/EKG_Machine_Console/EKG_Button_GRP/EKG_Power_Button";
    public enum ArrowAxis { Up, Forward, Right }
    public ArrowAxis prefabPointAxis = ArrowAxis.Right;
    public bool invertArrowAxis = true;
    [Range(0.1f, 5f)] public float arrowScaleMultiplier = 5.0f;
    public bool rotateArrow = true;
    public float arrowRotateSpeed = 90f;
    GameObject arrowInstance;
    Vector3 arrowOriginalScale;

    int index;
    public int CurrentSlideNumber { get { return index + 1; } }

    void Awake()
    {
        if (slides == null || slides.Count == 0)
            SeedDefaultSlides();

        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(Next);
            continueButton.onClick.AddListener(Next);
        }
        if (backButton != null)
        {
            backButton.onClick.RemoveListener(Prev);
            backButton.onClick.AddListener(Prev);
        }

        // EKGMachinePowerState.SetOn(false);
        index = Mathf.Clamp(index, 0, slides.Count - 1);
        Show(index);
    }

    void Start()
    {
        if (autoPlayOnStart)
            StartCoroutine(PlayStartNarration());
    }

    void Update()
    {
        // XR controller fallback: advance with primary button or trigger
        if (useXRInput)
        {
            bool pressed = false;
            try
            {
                var device = InputDevices.GetDeviceAtXRNode(continueXRNode);
                if (device.isValid)
                {
                    bool primaryBtn = false, triggerBtn = false;
                    device.TryGetFeatureValue(CommonUsages.primaryButton, out primaryBtn);
                    device.TryGetFeatureValue(CommonUsages.triggerButton, out triggerBtn);
                    pressed = primaryBtn || triggerBtn;
                }
            }
            catch { }

            // Ignore XR input during debounce window to avoid double-advance
            if (Time.unscaledTime >= xrIgnoreUntil)
            {
                if (pressed && !prevXRPressed)
                    Next();
            }
            prevXRPressed = pressed;
        }

        // Optional keyboard for desktop testing (legacy input only)
        #if ENABLE_LEGACY_INPUT_MANAGER
        if (allowKeyboardAdvance)
        {
            if (Input.GetKeyDown(advanceKey)) Next();
            if (Input.GetKeyDown(backKey)) Prev();
        }
        #endif

        // Refresh gating for current slide
        UpdateContinueVisibility();
        RotateArrowIfActive();
    }

    void Next()
    {
        if (slides == null || slides.Count == 0) return;
        if (!CanAdvanceNow()) return;
        if (!IsCurrentSlideAllowed()) return;
        index = Mathf.Min(index + 1, slides.Count - 1);
        Show(index);
        if (autoPlayNarrationOnAdvance) TryPlayNarration(index + 1);
        ArmDebounce();
    }

    void Prev()
    {
        if (slides == null || slides.Count == 0) return;
        if (!CanAdvanceNow()) return;
        index = Mathf.Max(index - 1, 0);
        Show(index);
        ArmDebounce();
    }

    bool CanAdvanceNow()
    {
        return Time.unscaledTime - lastAdvanceTime >= minAdvanceInterval;
    }

    void ArmDebounce()
    {
        lastAdvanceTime = Time.unscaledTime;
        xrIgnoreUntil = Time.unscaledTime + xrIgnoreDuration;
    }

    void Show(int i)
    {
        if (slides == null || slides.Count == 0) return;
        var s = slides[Mathf.Clamp(i, 0, slides.Count - 1)];
        SetText(bodyText, s.body);
        SetText(footerText, s.footer);
        SetText(progressText, $"Slide {i + 1}/{slides.Count}");

        if (footerText != null)
            footerText.gameObject.SetActive(!string.IsNullOrWhiteSpace(s.footer));
        if (backButton != null)
            backButton.gameObject.SetActive(i > 0);

        UpdateArrowForSlide(i + 1);
        UpdateContinueVisibility();
    }

    void SetText(Component c, string value)
    {
        if (c == null) return;
        var t = c.GetType();
        var p = t.GetProperty("text");
        if (p != null && p.CanWrite) p.SetValue(c, value);
    }

    void UpdateArrowForSlide(int slideNumber)
    {
        if (!Application.isPlaying)
        {
            if (arrowInstance != null) arrowInstance.SetActive(false);
            return;
        }
        Transform target = null;
        float y = arrowYOffset;

        if (slideNumber == 2)
        {
            target = FindTargetByNameOrPath(firstArrowMarkerName);
        }
        else if (slideNumber == 5)
        {
            target = FindTargetByNameOrPath(secondArrowMarkerName);
            y += secondArrowExtraYOffset;
        }
        else if (slideNumber == 9)
        {
            target = FindTargetByNameOrPath(powerButtonPath);
            if (target == null) target = FindTargetByNameOrPath("EKG_Power_Button");
        }

        if (target != null)
        {
            EnsureArrowInstance();
            if (arrowInstance != null)
            {
                arrowInstance.SetActive(true);
                arrowInstance.transform.position = target.position + Vector3.up * y;
                OrientArrowDown();
                ApplyArrowScale();
                TryApplyArrowColor(arrowColor);
            }
        }
        else
        {
            if (arrowInstance != null) arrowInstance.SetActive(false);
        }
    }

    Transform FindTargetByNameOrPath(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return null;
        var go = GameObject.Find(nameOrPath);
        if (go != null) return go.transform;
        try
        {
            var all = FindObjectsOfType<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (string.Equals(t.name, nameOrPath, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
        }
        catch { }
        return null;
    }

    void EnsureArrowInstance()
    {
        if (arrowInstance != null) return;
        if (arrowPrefab != null)
        {
            arrowInstance = Instantiate(arrowPrefab);
        }
        else
        {
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "TutorialArrow_Fallback";
            cyl.transform.localScale = new Vector3(0.05f, 0.15f, 0.05f);
            arrowInstance = cyl;
        }
        arrowInstance.hideFlags = HideFlags.HideAndDontSave;
        var cols = arrowInstance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
        arrowInstance.SetActive(false);
        arrowOriginalScale = arrowInstance.transform.localScale;
        ApplyArrowScale();
        TryApplyArrowColor(arrowColor);
    }

    void OrientArrowDown()
    {
        if (arrowInstance == null) return;
        Vector3 localAxis;
        switch (prefabPointAxis)
        {
            case ArrowAxis.Forward: localAxis = Vector3.forward; break;
            case ArrowAxis.Right: localAxis = Vector3.right; break;
            default: localAxis = Vector3.up; break;
        }
        if (invertArrowAxis) localAxis = -localAxis;
        var rot = Quaternion.FromToRotation(localAxis, Vector3.down);
        arrowInstance.transform.rotation = rot;
    }

    void ApplyArrowScale()
    {
        if (arrowInstance == null) return;
        if (arrowOriginalScale == Vector3.zero) arrowOriginalScale = arrowInstance.transform.localScale;
        arrowInstance.transform.localScale = arrowOriginalScale * arrowScaleMultiplier;
    }

    void RotateArrowIfActive()
    {
        if (!Application.isPlaying) return;
        if (!rotateArrow) return;
        if (arrowInstance != null && arrowInstance.activeInHierarchy)
        {
            arrowInstance.transform.Rotate(Vector3.up, arrowRotateSpeed * Time.deltaTime, Space.World);
        }
    }

    void OnDisable()
    {
        if (arrowInstance != null)
        {
            if (Application.isPlaying) Destroy(arrowInstance); else DestroyImmediate(arrowInstance);
            arrowInstance = null;
        }
    }

    void OnDestroy()
    {
        if (arrowInstance != null)
        {
            if (Application.isPlaying) Destroy(arrowInstance); else DestroyImmediate(arrowInstance);
            arrowInstance = null;
        }
    }

    void TryApplyArrowColor(Color c)
    {
        if (arrowInstance == null) return;
        var rens = arrowInstance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rens.Length; i++)
        {
            var r = rens[i];
            var mats = r.materials;
            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null) continue;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.color = c;
                if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", c);
            }
        }
    }

    void SeedDefaultSlides()
    {
        slides = new List<Slide>
        {
            new Slide{ body = "Your task is to correctly place electrodes for a 10‑lead EKG on your patient. When you are ready, press 'Continue' to proceed." },
            new Slide{ body = "Before starting, ensure the patient's skin is clean and dry. Here, the skin has already been prepared." },
            new Slide{ body = "Electrodes adhere better on a prepared surface." },
            new Slide{ body = "When you press Continue, the EKG machine and electrodes will appear on the bedside and you may begin." },
            new Slide{ body = "We have marked these spots on the patient's body with labeled, color‑coded markers.", footer = "Peel the back off the pads and apply them to the patient.", requirePads10 = true },
            new Slide{ body = "Attach the lead wire that matches the spot's color and label." },
            new Slide{ body = "Begin with the limb leads before moving to the chest leads.", footer = "Attach the correct lead to each electrode pad.", requireLeads10 = true },
            new Slide{ body = "You're almost done! Now that all electrodes are placed, let's verify the connections." },
            new Slide{ body = "Switch on the EKG machine to check the readings.", requireMachineOn = true },
            new Slide{ body = "EKG setup complete!" },
            new Slide{ body = "You have successfully placed the electrodes for a 10‑lead EKG." },
            new Slide{ body = "Proper placement ensures accurate readings, which are crucial for patient diagnosis." },
            new Slide{ body = "Exiting Simulation." }
        };
    }

    bool IsCurrentSlideAllowed()
    {
        if (slides == null || slides.Count == 0) return true;
        var s = slides[Mathf.Clamp(index, 0, slides.Count - 1)];
        return AreConditionsMet(s);
    }

    void UpdateContinueVisibility()
    {
        if (continueButton == null) return;
        bool allowed = IsCurrentSlideAllowed();
        var go = continueButton.gameObject;
        if (go.activeSelf != allowed) go.SetActive(allowed);
    }

    bool AreConditionsMet(Slide s)
    {
        if (s == null) return true;
        if (s.requirePads10)
        {
            // Accept either global PadManager or SL12.PadManager
            bool ok = false;
            var padsGlobal = PadManager.Instance;
            if (padsGlobal != null) ok |= padsGlobal.placedCount >= 10;
            var padsSL12 = SL12.PadManager.Instance;
            if (padsSL12 != null) ok |= padsSL12.placedCount >= 10;
            return ok;
        }
        if (s.requireLeads10)
        {
            var mgr = UnityEngine.Object.FindFirstObjectByType<EKGElectodManager>();
            if (mgr != null) return mgr.GetCorrectCount() >= 10; // all 10 correctly placed
            return false;
        }
        if (s.requireMachineOn)
        {
            return EKGMachinePowerState.IsOn;
        }
        return true;
    }

    void TryPlayNarration(int slideNumber)
    {
        if (narrationSource == null) return;
        AudioClip clip = null;
        // Prefer explicitly assigned clips by index (slideNumber is 1-based)
        int idx = slideNumber - 1;
        if (narrationClips != null && idx >= 0 && idx < narrationClips.Count)
            clip = narrationClips[idx];
        if (clip == null && fallbackToFirstClip && narrationClips != null && narrationClips.Count > 0)
            clip = narrationClips[0];
        // Fallback to Resources/Narration/<n> or <nn>
        if (clip == null) clip = Resources.Load<AudioClip>("Narration/" + slideNumber);
        if (clip == null) clip = Resources.Load<AudioClip>("Narration/" + slideNumber.ToString("00"));
        if (clip != null)
        {
            narrationSource.Stop();
            narrationSource.clip = clip;
            narrationSource.Play();
        }
    }

    IEnumerator PlayStartNarration()
    {
        if (startNarrationDelay > 0f)
            yield return new WaitForSeconds(startNarrationDelay);
        TryPlayNarration(index + 1);
    }
}
