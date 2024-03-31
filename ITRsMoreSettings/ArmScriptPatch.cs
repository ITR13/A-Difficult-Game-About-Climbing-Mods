using HarmonyLib;
using UnityEngine;

namespace ITRsMoreSettings;

[HarmonyPatch(typeof(ArmScript_v2), "Update")]
public static class ArmScriptPatch
{
    internal static bool Active;

    static bool Prefix(
        ArmScript_v2 __instance,
        bool ___listenToInput,
        ClimbingSurface ___activeSurface,
        bool ___invertControls,
        ArmScript_v2 ___otherArm,
        Rigidbody2D ___body,
        bool ___isLeft,
        bool ___invertGrab,
        ref Vector2 ___mouseAxis,
        ref bool ___isGrabbing,
        ref float ___cursorDirection,
        ref bool ___controllerTriggerDown,
        ref bool ___toggledThisFrame
    )
    {
        if (!Active) return true;

        if (!___listenToInput || PauseMenu.GameIsPaused)
        {
            ___toggledThisFrame = false;
            return false;
        }

        if (___isGrabbing && ___activeSurface != null && !___invertControls && !___activeSurface.isPickup)
        {
            ___mouseAxis -= new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            if (!___otherArm.isGrabbing && ___otherArm.grabbedSurface == null)
            {
                ___cursorDirection = Mathf.Lerp(1f, -0.5f, Mathf.Abs(___body.velocity.x) / 3f);
            }

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space))
            {
                ___mouseAxis += Vector2.down * 1f;
            }
        }
        else
        {
            ___mouseAxis += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space))
            {
                ___mouseAxis += Vector2.up * 1f;
            }
        }

        ___cursorDirection = Mathf.Lerp(___cursorDirection, 1f, Time.deltaTime * 8f);
        var num = ___isLeft != ___invertGrab ? 0 : 1;
        var buttonName = ___isLeft ? "GrabL" : "GrabR";
        var axisName = ___isLeft ? "GrabLt" : "GrabRt";

        var triggerIsDown = Input.GetAxis(axisName) > 0.3f;
        var downThisFrame = Input.GetMouseButtonDown(num) ||
                            Input.GetButtonDown(buttonName) ||
                            (triggerIsDown && !___controllerTriggerDown);
        var upThisFrame = Input.GetMouseButtonUp(num) ||
                          Input.GetButtonUp(buttonName) ||
                          (!triggerIsDown && ___controllerTriggerDown);
        var anyIsDown = triggerIsDown ||
                        Input.GetMouseButton(num) ||
                        Input.GetButton(buttonName);
        ___controllerTriggerDown = triggerIsDown;

        if (Active)
        {
            anyIsDown = !anyIsDown;
            (downThisFrame, upThisFrame) = (upThisFrame, downThisFrame);
        }

        if (!___isGrabbing && downThisFrame)
        {
            ___isGrabbing = true;
            GrabActiveSurface(__instance, true);
        }
        else if (upThisFrame && !anyIsDown && !___toggledThisFrame)
        {
            ___isGrabbing = false;
            ReleaseSurface(__instance, true, false);
        }

        ___toggledThisFrame = false;

        return false;
    }

    private static void GrabActiveSurface(ArmScript_v2 self, bool playerInvoked)
    {
        new Traverse(self).Method("GrabActiveSurface", playerInvoked).GetValue();
    }

    private static void ReleaseSurface(ArmScript_v2 self, bool playerInvoked, bool newSurface)
    {
        new Traverse(self).Method("ReleaseSurface", playerInvoked, newSurface).GetValue();
    }
}