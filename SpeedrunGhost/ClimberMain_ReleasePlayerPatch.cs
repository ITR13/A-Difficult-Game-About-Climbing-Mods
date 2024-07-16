using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SpeedrunningTools;

[HarmonyPatch("ClimberMain+<ReleasePlayer>d__18, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", "MoveNext")]
public static class ClimberMainReleasePlayerPatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Is(OpCodes.Ldc_R4, 1.5f))
            {
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0.1f);
            }
            else
            {
                yield return instruction;
            }
        }
    }
}