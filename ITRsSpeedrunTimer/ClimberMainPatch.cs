using System;
using HarmonyLib;

namespace SpeedrunningTools;

[HarmonyPatch(typeof(ClimberMain), "Start")]
public static class ClimberMainPatch
{
    public static event Action<ClimberMain> OnClimberSpawned;
    
    public static void Postfix(ClimberMain __instance)
    {
        OnClimberSpawned?.Invoke(__instance);
    }
}