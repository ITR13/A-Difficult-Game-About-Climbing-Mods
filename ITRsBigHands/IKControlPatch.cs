using HarmonyLib;
using UnityEngine;

namespace ITRsBigHands;

[HarmonyPatch(typeof(IKControl), "SetTargets")]
public class IKControlPatch
{
    public static Vector3 Offset;
    
    static void Postfix(Transform ___leftHandObj, Transform ___rightHandObj)
    {
        if(___leftHandObj == null || ___rightHandObj == null) return;
        ___leftHandObj.transform.localPosition -= Offset;
        ___rightHandObj.transform.localPosition -= Offset;
    }
}