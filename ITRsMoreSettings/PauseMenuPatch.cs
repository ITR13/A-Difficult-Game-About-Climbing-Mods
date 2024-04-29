using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ITRsMoreSettings;

[HarmonyPatch(typeof(PauseMenu), "LoadStats")]
public static class PauseMenuPatch
{
    public static void Prefix()
    {
        Application.targetFrameRate /= OnDemandRendering.renderFrameInterval;
    }

    public static void Postfix()
    {
        Application.targetFrameRate *= OnDemandRendering.renderFrameInterval;
    }
}