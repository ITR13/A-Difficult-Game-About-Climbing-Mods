using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ITRsBigHands;


[BepInPlugin("com.itr.adifficultgameaboutclimbing.weightvest", "ITR's Weight Vest", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<float> _bodyWeight, _leftArmWeight, _rightArmWeight,  _leftHandWeight, _rightHandWeight;
    private bool _tryUpdateScale;

    private void Awake()
    {
        _bodyWeight = Config.Bind(
            "General.sliders",
            "Extra Body Weight",
            5.0f,
            "The player's weight is 8 + this"
        );
        _leftArmWeight = Config.Bind(
            "General.sliders",
            "Extra Left Arm Weight",
            0.0f,
            "The left arm weighs is 2 + this"
        );
        _rightArmWeight = Config.Bind(
            "General.sliders",
            "Extra Right Arm Weight",
            0.0f,
            "The right arm weighs 2 + this"
        );
        _leftHandWeight = Config.Bind(
            "General.sliders",
            "Extra Left Hand Weight",
            0.0f,
            "The left hand weighs is 1 + this"
        );
        _rightHandWeight = Config.Bind(
            "General.sliders",
            "Extra Right Hand Weight",
            0.0f,
            "The right hand weighs is 1 + this"
        );

        _bodyWeight.SettingChanged += (_, _) => _tryUpdateScale = true;
        _leftArmWeight.SettingChanged += (_, _) => _tryUpdateScale = true;
        _rightArmWeight.SettingChanged += (_, _) => _tryUpdateScale = true;
        _leftHandWeight.SettingChanged += (_, _) => _tryUpdateScale = true;
        _rightHandWeight.SettingChanged += (_, _) => _tryUpdateScale = true;
        SceneManager.activeSceneChanged += (_, _) => _tryUpdateScale = true;
        
        _tryUpdateScale = true;
        
        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), "com.itr.adifficultgameaboutclimbing.bighands");
    }

    private void Update()
    {
        if (!_tryUpdateScale) return;
        _tryUpdateScale = !UpdateWeight();
    }


    private bool UpdateWeight()
    {
        var toUpdate = new[]
        {
            ("Climber5(Clone)/Climber_Hero_Body_Prefab", 8f, _bodyWeight),
            ("Climber5(Clone)/ArmSetup2_CustomJoint", 2f, _rightArmWeight),
            ("Climber5(Clone)/ArmSetup2_CustomJoint_L", 2f, _leftArmWeight),
            ("Climber5(Clone)/ArmSetup2_CustomJoint/Hand", 1f, _rightHandWeight),
            ("Climber5(Clone)/ArmSetup2_CustomJoint_L/Hand", 1f, _leftHandWeight),
        };

        foreach (var (goName, baseValue, config) in toUpdate)
        {
            var go = GameObject.Find(goName);
            if (go == null) return false;
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                Logger.LogError($"GameObject {goName} had no rigidbody");
                continue;
            }

            rb.mass = baseValue + config.Value;
        }

        Logger.LogInfo("Updated player weight");
        return true;
    }
}