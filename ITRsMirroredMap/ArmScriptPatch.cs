using HarmonyLib;
using UnityEngine;

namespace ITRsMoreSettings;

[HarmonyPatch(typeof(ArmScript_v2), "FixedUpdate")]
public static class ArmScriptPatch
{
    static void Prefix(
        ref Vector2 ___mouseAxis
    )
    {
        ___mouseAxis.x = -___mouseAxis.x;
    }

    static void Postfix(
        ref Vector2 ___mouseAxis
    )
    {
        ___mouseAxis.x = -___mouseAxis.x;
    }
}