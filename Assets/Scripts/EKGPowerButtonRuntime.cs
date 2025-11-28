using UnityEngine;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit;

public class EKGPowerButtonRuntime : MonoBehaviour
{
    public EKGTutorialSlideController tutorial;
    public int requiredSlide = 9;
    [Header("Screen (Video)")]
    public VideoPlayer videoPlayer; // assign the VideoPlayer on EKG_Screen_Geo
    public bool loopVideo = true;
    public bool switchOnOnce = true;

    bool switched;
    UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable xrInteractable;

    void Awake()
    {
        // Prefer explicit assignment via inspector; fallback to child lookup
        if (videoPlayer == null)
            videoPlayer = GetComponentInChildren<VideoPlayer>(true);

        xrInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        if (xrInteractable != null)
        {
            xrInteractable.selectEntered.RemoveListener(OnSelectEntered);
            xrInteractable.selectEntered.AddListener(OnSelectEntered);
        }
    }

    void OnDestroy()
    {
        if (xrInteractable != null)
        {
            xrInteractable.selectEntered.RemoveListener(OnSelectEntered);
        }
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        Activate();
    }

    public void Activate()
    {
        if (switchOnOnce && switched) return;
        if (tutorial != null && tutorial.CurrentSlideNumber != requiredSlide) return;
        if (videoPlayer == null) return;
        videoPlayer.isLooping = loopVideo;
        // Ensure enabled and start playback
        if (!videoPlayer.enabled) videoPlayer.enabled = true;
        videoPlayer.Play();
        EKGMachinePowerState.SetOn(true);
        switched = true;
    }
}
