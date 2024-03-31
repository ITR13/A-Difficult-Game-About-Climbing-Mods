using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ITRsMirroredMap;

[BepInPlugin("com.itr.adifficultgameaboutclimbing.mirroredmap", "ITR's Mirrored Map", MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private Camera _mainCamera;
    private float _timer;

    private void Awake()
    {
        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), MyPluginInfo.PLUGIN_GUID);
    }

    private void Flip(Camera camera, bool flipCulling)
    {
        if (flipCulling)
        {
            if(camera.GetComponent<MirrorFlipCamera>()) return;
            camera.gameObject.AddComponent<MirrorFlipCamera>();
            return;
        }
        var scale = new Vector3(-1, 1, 1);
        camera.projectionMatrix = camera.projectionMatrix * Matrix4x4.Scale(scale);
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer <= 1) return; 
        _timer -= 1;

        var mainCamera = Camera.main;
        if (mainCamera == null || _mainCamera == mainCamera) return;
        _mainCamera = mainCamera;

        Flip(mainCamera, true);
        FlipChildCamera(mainCamera, "BackgroundCamera", false);
        FlipChildCamera(mainCamera, "ForegroundCamera", true);
    }

    private void FlipChildCamera(Camera mainCamera, string name, bool flipCulling)
    {
        var transform = mainCamera.transform.Find(name);
        if (transform == null) return;
        var camera = transform.GetComponent<Camera>();
        if (camera == null) return;
        Flip(camera, flipCulling);
    }
}