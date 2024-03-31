using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TinyLarge;

[BepInPlugin("com.itr.adifficultgameaboutclimbing.tinylarge", "ITR's TinyLarge", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<float> _scale;
    private ConfigEntry<KeyboardShortcut> _decreaseSize, _increaseSize, _resetSize;
    // private float _jumpCooldown;

    private void Awake()
    {
        _scale = Config.Bind(
            "Sizemod",
            "Player Scale",
            1.0f,
            "The scale of the player"
        );

        _decreaseSize = Config.Bind(
            "Sizemod",
            "Decrease Size",
            new KeyboardShortcut(KeyCode.O, KeyCode.LeftControl),
            "Make the player smaller"
        );
        _increaseSize = Config.Bind(
            "Sizemod",
            "Increase Size",
            new KeyboardShortcut(KeyCode.P, KeyCode.LeftControl),
            "Make the player bigger"
        );
        _resetSize = Config.Bind(
            "Sizemod",
            "Reset Size",
            new KeyboardShortcut(KeyCode.P, KeyCode.O),
            "Reset the player scale"
        );

        _scale.SettingChanged += (_, _) =>
        {
            StopAllCoroutines();
            StartCoroutine(UpdateScale(false));
        };

        StartCoroutine(UpdateScale(true));
        SceneManager.activeSceneChanged += (_, _) => StartCoroutine(UpdateScale(true));

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), "com.itr.adifficultgameaboutclimbing.tinylarge");
    }


    private void Update()
    {
        var scale = _scale.Value;

        if (_resetSize.Value.IsDown())
        {
            scale = 1;
        }
        else if (_decreaseSize.Value.IsPressed())
        {
            scale -= Time.unscaledDeltaTime;
        }
        else if (_increaseSize.Value.IsPressed())
        {
            scale += Time.unscaledDeltaTime;
        }

        _scale.Value = scale;

/*
        _jumpCooldown -= Time.deltaTime;
        if (Input.GetKey(KeyCode.W) && scale < 0.75f && _jumpCooldown <= 0)
        {
            _jumpCooldown += 0.5f;

            var playerBody = GameObject.Find("Climber_Hero_Body_Prefab");
            var body = playerBody.GetComponent<Rigidbody2D>();
            var rightHand = playerBody
                .transform
                .parent
                .Find("ArmSetup2_CustomJoint")
                .GetComponent<ArmScript_v2>();
            var leftHand = playerBody
                .transform
                .parent
                .Find("ArmSetup2_CustomJoint_L")
                .GetComponent<ArmScript_v2>();

            var multiplier = 0f;
            if (rightHand.grabbedSurface != null && !rightHand.grabbedSurface.isDynamic)
            {
                multiplier += 1;
                Traverse.Create(rightHand).Method("ReleaseSurface", true, false);
            }

            if (leftHand.grabbedSurface != null && !leftHand.grabbedSurface.isDynamic)
            {
                multiplier += 1;
                Traverse.Create(leftHand).Method("ReleaseSurface", true, false);
            }

            //Debug.LogError($"{rightHand.grabbedSurface.dynamicRB.sharedMaterial.friction}, {leftHand.grabbedSurface.dynamicRB.sharedMaterial.friction}");

            body.AddForce(new Vector2(0, multiplier * 50 * (1 - scale)), ForceMode2D.Impulse);
        }*/
    }

    private IEnumerator UpdateScale(bool waitASec)
    {
        if (waitASec)
        {
            yield return new WaitForSecondsRealtime(1);
        }

        var playerBody = GameObject.Find("Climber_Hero_Body_Prefab");
        while (playerBody == null)
        {
            yield return null;
            playerBody = GameObject.Find("Climber_Hero_Body_Prefab");
        }

        var parent = playerBody.transform.parent;

        var body = playerBody.GetComponent<Rigidbody2D>();
        body.centerOfMass = new Vector2(0, 0.69f + (_scale.Value - 1) * 0.2955f);

        var absScale = Mathf.Abs(_scale.Value);

        /*var massMultiplier = Mathf.Clamp(absScale, 0.125f, 1);
        body.mass = 8 * massMultiplier;
        var armRight = parent.Find("ArmSetup2_CustomJoint").GetComponent<Rigidbody2D>();
        var armLeft = parent.Find("ArmSetup2_CustomJoint_L").GetComponent<Rigidbody2D>();
        armRight.mass = 2 * massMultiplier;
        armLeft.mass = 2 * massMultiplier;*/


        // playerBody.transform.localScale = Vector3.one * Scale.Value;
        ArmScriptPatch.Multiplier = _scale.Value;

        var armDistance = absScale * 0.64f;
        foreach (var distancejoint in parent.GetComponentsInChildren<DistanceJoint2D>())
        {
            distancejoint.distance = armDistance;
        }

        foreach (var armscript in parent.GetComponentsInChildren<ArmScript_v2>())
        {
            armscript.armDistance = armDistance;
            Traverse.Create(armscript).Field<float>("cursorDistance").Value = Mathf.Max(absScale, 0.01f);
            // Traverse.Create(armscript).Field<float>("maxTargetVelocity").Value = Mathf.Min(10, 1000 - absScale * 1000);
        }

        var heroCharacter = playerBody.transform.Find("HeroCharacter");
        heroCharacter.localScale = _scale.Value * Vector3.one;
        heroCharacter.localPosition = new Vector3(0, 1.0f - _scale.Value, 0);

        var mainCamera = Camera.main!;
        var backgroundCamera = mainCamera.transform.Find("BackgroundCamera");
        var foregroundCamera = mainCamera.transform.Find("ForegroundCamera");

        var cameraScale = Mathf.Max(Math.Abs(_scale.Value), 0.5f);

        mainCamera.orthographicSize = 2.2f * cameraScale;
        backgroundCamera.localPosition = Vector3.back * (10 * cameraScale - 10);
        foregroundCamera.localPosition = Vector3.back * (10 * cameraScale - 10);
    }
}