using HarmonyLib;
using UnityEngine;

namespace TinyLarge;

[HarmonyPatch(typeof(ArmScript_v2), "FixedUpdate")]
public static class ArmScriptPatch
{
    public static float Multiplier;
    
    static void Prefix(ref Vector2 ___mouseAxis)
    {
        ___mouseAxis *= Multiplier;
    }
}