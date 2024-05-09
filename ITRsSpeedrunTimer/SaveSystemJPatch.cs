using System;
using HarmonyLib;

namespace SpeedrunningTools;

[HarmonyPatch(typeof(SaveSystemJ), "SetCompleteTime")]
public static class SaveSystemJTimePatch
{
    public static event Action<float> OnGameWon;

    public static void Postfix(float ___timeCompleted)
    {
        OnGameWon?.Invoke(___timeCompleted);
    }
}