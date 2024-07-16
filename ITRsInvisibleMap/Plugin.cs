using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ITRsInvisibleMap;

[BepInPlugin(
    "com.itr.adifficultgameaboutclimbing.invisiblemap",
    "ITR's Invisible Map",
    MyPluginInfo.PLUGIN_VERSION
)]
public class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        SceneManager.sceneLoaded += (_, _) => GoBlind();
        GoBlind();
    }

    private void GoBlind()
    {
        var camera = Camera.main;
        if(camera==null) return;

        var backgroundCamera = camera.transform.Find("BackgroundCamera");
        var foregroundCamera = camera.transform.Find("ForegroundCamera");

        camera.cullingMask = 80;
        backgroundCamera.GetComponent<Camera>().cullingMask = 0;
        foregroundCamera.GetComponent<Camera>().cullingMask = 0;
    }
}
