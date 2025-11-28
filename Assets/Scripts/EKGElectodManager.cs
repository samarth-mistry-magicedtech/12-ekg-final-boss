using System;
using System.Collections.Generic;
using UnityEngine;

public class EKGElectodManager : MonoBehaviour
{
    [Serializable]
    public class MarkerEntry
    {
        public string id;
        public Transform marker;
    }

    [Serializable]
    public class ElectrodeEntry
    {
        public EKGElectrodController controller;
        public string correctMarkerId;
    }

    public List<MarkerEntry> markers = new List<MarkerEntry>();
    public List<ElectrodeEntry> electrodes = new List<ElectrodeEntry>();

    public AudioSource sfxSource;
    public AudioClip wrongAttachClip;
    public AudioClip correctAttachClip;

    readonly Dictionary<Transform, EKGElectrodController> occupancy = new Dictionary<Transform, EKGElectrodController>();
    readonly Dictionary<EKGElectrodController, Transform> attached = new Dictionary<EKGElectrodController, Transform>();

    public Transform GetNearestMarker(Vector3 position, float radius)
    {
        Transform best = null;
        float bestSqr = radius * radius;
        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i].marker;
            if (m == null) continue;
            float d2 = (m.position - position).sqrMagnitude;
            if (d2 <= bestSqr)
            {
                bestSqr = d2;
                best = m;
            }
        }
        return best;
    }

    public void NotifyAttached(EKGElectrodController ctrl, Transform marker)
    {
        TryReserve(ctrl, marker);
    }

    public void NotifyDetached(EKGElectrodController ctrl, Transform marker)
    {
        Release(ctrl);
    }

    public bool TryReserve(EKGElectrodController ctrl, Transform marker)
    {
        if (ctrl == null || marker == null) return false;
        if (occupancy.TryGetValue(marker, out var who) && who != ctrl) return false;

        Transform previous;
        bool hadPrev = attached.TryGetValue(ctrl, out previous);
        if (hadPrev && previous == marker)
        {
            return true;
        }

        if (hadPrev && previous != null)
        {
            if (occupancy.TryGetValue(previous, out var prevWho) && prevWho == ctrl)
                occupancy.Remove(previous);
        }

        attached[ctrl] = marker;
        occupancy[marker] = ctrl;

        bool correct = IsCorrect(ctrl, marker);
        if (sfxSource != null)
        {
            if (!correct && wrongAttachClip != null) sfxSource.PlayOneShot(wrongAttachClip);
            else if (correct && correctAttachClip != null) sfxSource.PlayOneShot(correctAttachClip);
        }
        return true;
    }

    public void Release(EKGElectrodController ctrl)
    {
        if (ctrl == null) return;
        if (attached.TryGetValue(ctrl, out var m))
        {
            attached.Remove(ctrl);
            if (m != null && occupancy.TryGetValue(m, out var who) && who == ctrl)
                occupancy.Remove(m);
        }
    }

    public int GetConnectedCount()
    {
        return attached.Count;
    }

    public int GetCorrectCount()
    {
        int c = 0;
        foreach (var kv in attached)
        {
            if (IsCorrect(kv.Key, kv.Value)) c++;
        }
        return c;
    }

    public Transform GetMarkerTransformById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < markers.Count; i++)
        {
            if (string.Equals(markers[i].id, id, StringComparison.OrdinalIgnoreCase))
                return markers[i].marker;
        }
        return null;
    }

    public bool IsCorrect(EKGElectrodController ctrl, Transform marker)
    {
        if (ctrl == null || marker == null) return false;
        string markerId = GetMarkerId(marker);
        if (string.IsNullOrEmpty(markerId)) return false;
        var e = FindElectrode(ctrl);
        if (e == null || string.IsNullOrEmpty(e.correctMarkerId)) return false;
        return string.Equals(e.correctMarkerId, markerId, StringComparison.OrdinalIgnoreCase);
    }

    string GetMarkerId(Transform marker)
    {
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i].marker == marker) return markers[i].id;
        }
        return null;
    }

    ElectrodeEntry FindElectrode(EKGElectrodController ctrl)
    {
        for (int i = 0; i < electrodes.Count; i++)
        {
            if (electrodes[i].controller == ctrl) return electrodes[i];
        }
        return null;
    }
}
