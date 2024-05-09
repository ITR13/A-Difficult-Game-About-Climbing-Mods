using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace SpeedrunningTools;

[HarmonyPatch(typeof(PauseMenu))]
public static class PauseMenuPatch
{
    public static event Action<bool> OnPause;
    public static event Action OnStartTimer;

    [HarmonyPatch("PauseGame")]
    [HarmonyPostfix]
    public static void PauseGamePostfix()
    {
        OnPause?.Invoke(true);
    }

    [HarmonyPatch("ResumeGame")]
    [HarmonyPostfix]
    public static void ResumeGamePostfix()
    {
        OnPause?.Invoke(false);
    }

    [HarmonyPatch("StartTimer")]
    [HarmonyPostfix]
    public static void StartTimerPostfix()
    {
        OnStartTimer?.Invoke();
    }


    [HarmonyPatch("FormatTime")]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> FormatTimeTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Is(OpCodes.Ldstr, "F2"))
            {
                yield return new CodeInstruction(OpCodes.Ldstr, "F3");
            }
            else
            {
                yield return instruction;
            }
        }
    }
}