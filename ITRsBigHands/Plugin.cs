using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ITRsBigHands;


[BepInPlugin("com.itr.adifficultgameaboutclimbing.bighands", "ITR's Big Hands", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<float> _handScale;
    private bool _tryUpdateScale;

    private void Awake()
    {
        _handScale = Config.Bind(
            "General.sliders",
            "Hand Scale",
            5.0f,
            "The scale of the player's hands"
        );

        _handScale.SettingChanged += (_, _) => _tryUpdateScale = true;
        SceneManager.activeSceneChanged += (_, _) => _tryUpdateScale = true;
        _tryUpdateScale = true;
        UpdateScale();
        
        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), "com.itr.adifficultgameaboutclimbing.bighands");
    }

    private void Update()
    {
        if (!_tryUpdateScale) return;
        _tryUpdateScale = !UpdateScale();
    }


    private bool UpdateScale()
    {
        var offset = 0.05f * (Mathf.Abs(_handScale.Value) - 1);
        IKControlPatch.Offset = new Vector3(0, offset, 0);
        ArmScriptPatch.Offset = offset;
        
        var parent = GameObject.Find("Climber5(Clone)");
        if (parent == null) return false;

        var rightHand = parent.transform.Find("ArmSetup2_CustomJoint")?.Find("Hand")?.GetComponent<CircleCollider2D>();
        if (rightHand == null) return false;

        var leftHand = parent.transform.Find("ArmSetup2_CustomJoint_L")?.Find("Hand")?.GetComponent<CircleCollider2D>();
        if (leftHand == null) return false;

        var spine03 = parent.transform.Find("Climber_Hero_Body_Prefab")
            ?.Find("HeroCharacter")
            ?.Find("Armature")
            ?.Find("pelvis")
            ?.Find("spine01")
            ?.Find("spine02")
            ?.Find("spine03");
        if (spine03 == null) return false;
        var handL = spine03.Find("shoulder_L").Find("arm_L").Find("forearm_L").Find("hand_L");
        var handR = spine03.Find("shoulder_R").Find("arm_R").Find("forearm_R").Find("hand_R");

        var handScale = Vector3.one * _handScale.Value;
        handL.localScale = handScale;
        handR.localScale = handScale;

        var colliderScale = 0.015f * Mathf.Abs(_handScale.Value);
        leftHand.radius = colliderScale;
        rightHand.radius = colliderScale;

        /*var colliderOffset = new Vector2(0, 0.04f * (_handScale.Value - 1));
        leftHand.offset = colliderOffset;
        rightHand.offset = colliderOffset;*/

        return true;
    }
}