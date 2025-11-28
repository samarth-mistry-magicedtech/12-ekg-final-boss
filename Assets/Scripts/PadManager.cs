using System;
using System.Collections.Generic;
using UnityEngine;

public class PadManager : MonoBehaviour
{
    public static PadManager Instance { get; private set; }

    public int placedCount => _attachedPads.Count;
    public int peeledCount => _peeledCount;
    public GameObject peeledBackingPrefab;

    readonly HashSet<UnityEngine.Object> _attachedPads = new HashSet<UnityEngine.Object>();
    int _peeledCount = 0;

    public static event Action CountsChanged;

    void Awake()
    {
        if (!Application.isPlaying) return; // don't touch singleton in edit mode
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Optional: uncomment if you want to persist across scenes
        // DontDestroyOnLoad(gameObject);
    }

    public void NotifyAttached(UnityEngine.Object padKey)
    {
        if (padKey == null) return;
        if (_attachedPads.Add(padKey)) CountsChanged?.Invoke();
    }

    public void NotifyDetached(UnityEngine.Object padKey)
    {
        if (padKey == null) return;
        if (_attachedPads.Remove(padKey)) CountsChanged?.Invoke();
    }

    public void OnPadPeeled()
    {
        _peeledCount++;
        CountsChanged?.Invoke();
    }
}
