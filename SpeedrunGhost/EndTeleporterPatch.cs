using System;
using HarmonyLib;
using UnityEngine;

namespace SpeedrunningTools;

[HarmonyPatch(typeof(EndTeleporterScript), "OnTriggerEnter2D")]
public static class EndTeleporterPatch
{
    public static event Action OnGameEnded;

    private static void Postfix()
    {
        try
        {
            OnGameEnded?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}