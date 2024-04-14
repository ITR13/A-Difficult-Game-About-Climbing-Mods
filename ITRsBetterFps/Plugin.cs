using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using VolumetricFogAndMist;

namespace ITRsBetterFps;

[BepInPlugin(
    "com.itr.adifficultgameaboutclimbing.betterfps",
    "ITR's Better Fps",
    MyPluginInfo.PLUGIN_VERSION
)]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<bool> _disableFog;
    private ConfigEntry<bool> _disableWaterfallEdge, _disableWaterfallSteam;
    private ConfigEntry<bool> _disableForegroundFoliage, _disableBackgroundFoliage, _disableBackgroundRocks;

    private void Awake()
    {
        _disableFog = Config.Bind(
            "_Toggles",
            "Disable Fog",
            true,
            "Disables all volumetric fog"
        );

        _disableWaterfallEdge = Config.Bind(
            "_Toggles",
            "Disable Waterfall edge",
            true,
            "Disables the effect on the edge of waterfalls"
        );

        _disableWaterfallSteam = Config.Bind(
            "_Toggles",
            "Disable Waterfall steam",
            true,
            "Disables the steam in waterfalls"
        );

        _disableBackgroundFoliage = Config.Bind(
            "_Toggles",
            "Disable Background Foliage",
            true,
            "Disables the bushes in the background"
        );

        _disableForegroundFoliage = Config.Bind(
            "_Toggles",
            "Disable Foreground Foliage",
            false,
            "Disables the bushes in the foreground"
        );

        _disableBackgroundRocks = Config.Bind(
            "_Toggles",
            "Disable Background Rocks",
            false,
            "Disables rocks in the background"
        );

        SceneManager.sceneLoaded += (_, _) => OptimizeScene();
        OptimizeScene();
    }


    private void OptimizeScene()
    {
        Logger.LogInfo("Optimizing scene...");
        DisableVolumetricFog();
        DisableWatefallStuff();
        DisableRenderers();
    }

    private void DisableWatefallStuff()
    {
        if (_disableWaterfallSteam.Value)
        {
            FindAndDisable("WaterSteam_ParticleSystem");
            FindAndDisable("WaterSteam_ParticleSystem (1)");
        }

        if (_disableWaterfallEdge.Value) FindAndDisable("WaterfallEdge");
    }

    private void FindAndDisable(string goName)
    {
        var go = GameObject.Find(goName);
        if (go == null)
        {
            Logger.LogError($"Failed to find gameobject '{goName}'");
            return;
        }

        go.SetActive(false);
    }

    private void DisableVolumetricFog()
    {
        if (!_disableFog.Value) return;
        var volumetricFog = FindObjectsOfType<VolumetricFog>(true);
        foreach (var fog in volumetricFog)
        {
            fog.enabled = false;
        }
    }

    private void DisableRenderers()
    {
        var disableBgFoliage = _disableBackgroundFoliage.Value;
        var disableBgRocks = _disableBackgroundRocks.Value;
        var checkBg = disableBgFoliage || disableBgRocks;
        var checkFg = _disableForegroundFoliage.Value;
        if (!(checkBg || checkFg)) return;
        var fgLayer = LayerMask.NameToLayer("FG_ONLY");
        var bgLayer = LayerMask.NameToLayer("Background");


        var renderers = FindObjectsOfType<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (checkFg && renderer.gameObject.layer == fgLayer)
            {
                if (renderer.sharedMaterial.name.ToLower().Contains("bush"))
                {
                    renderer.enabled = false;
                }
            }
            else if (checkBg && renderer.gameObject.layer == bgLayer)
            {
                var matName = renderer.sharedMaterial.name.ToLower();
                if ((disableBgRocks && matName.Contains("rock")) || (disableBgFoliage && matName.Contains("bush")))
                {
                    renderer.enabled = false;
                }
            }
        }
    }
}