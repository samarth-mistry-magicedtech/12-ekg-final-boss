using System;

public static class EKGMachinePowerState
{
    public static bool IsOn { get; private set; }
    public static event Action<bool> OnChanged;

    public static void SetOn(bool on)
    {
        if (IsOn == on) return;
        IsOn = on;
        OnChanged?.Invoke(IsOn);
    }
}
