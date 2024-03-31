using HarmonyLib;

namespace ITRsBigHands;

[HarmonyPatch(typeof(ArmScript_v2))]
public class ArmScriptPatch
{
    public static float Offset;

    [HarmonyPrefix]
    [HarmonyPatch("FixedUpdate")]
    static void FixedUpdatePrefix(ref float ___cursorDistance)
    {
        ___cursorDistance += Offset;
    }

    [HarmonyPostfix]
    [HarmonyPatch("FixedUpdate")]
    static void FixedUpdatePostfix(ref float ___cursorDistance)
    {
        ___cursorDistance -= Offset;
    }

    [HarmonyPrefix]
    [HarmonyPatch("Start")]
    static void StartPrefix(ref float ___armDistance)
    {
        var old = ___armDistance;
        ___armDistance += Offset;
    }
}