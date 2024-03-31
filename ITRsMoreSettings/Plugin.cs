using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ITRsMoreSettings;

[BepInPlugin("com.itr.adifficultgameaboutclimbing.moresettings", "ITR's More Settings", "1.0.1")]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<bool> InvertClick, UseCheatScript, UseVsync;

    private void Awake()
    {
        InvertClick = Config.Bind(
            "General.Toggles",
            "Invert Mouse Click",
            false,
            "If true, you need to hold the button to let go"
        );

        UseCheatScript = Config.Bind(
            "General.Toggles",
            "Ctrl To Fly",
            false,
            "If true, you can hold ctrl and WASD to fly"
        );

        UseVsync = Config.Bind(
            "General.Toggles",
            "Vsync",
            QualitySettings.vSyncCount != 0,
            "Enables or disables vsync"
        );

        Logger.LogDebug($"Invert Mouse Click is {InvertClick.Value}!");

        ArmScriptPatch.Active = InvertClick.Value;
        InvertClick.SettingChanged += (_, _) =>
        {
            Logger.LogDebug($"Invert Mouse Click is now {InvertClick.Value}!");
            ArmScriptPatch.Active = InvertClick.Value;
        };

        UseCheatScript.SettingChanged += (_, _) =>
        {
            Logger.LogDebug($"Ctrl to fly is now {UseCheatScript.Value}");
            var playerBody = GameObject.Find("Climber_Hero_Body_Prefab");
            var cheatScript = playerBody.GetComponent<CheatScript>();
            cheatScript.enabled = UseCheatScript.Value;
        };

        QualitySettings.vSyncCount = UseVsync.Value ? 1 : 0;
        UseVsync.SettingChanged += (_, _) =>
        {
            Logger.LogDebug($"Vsync is now {UseVsync.Value}");
            QualitySettings.vSyncCount = UseVsync.Value ? 1 : 0;
        };

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), MyPluginInfo.PLUGIN_GUID);
    }

    private IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(2);
        SceneManager.sceneLoaded += (_, __) => StartCoroutine(ModifyCanvas());
        yield return ModifyCanvas();
    }

    private IEnumerator ModifyCanvas()
    {
        var pauseMenuCanvas = GameObject.Find("PauseMenuCanvas");
        while (pauseMenuCanvas == null)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            pauseMenuCanvas = GameObject.Find("PauseMenuCanvas");
        }

        var pauseMenu = pauseMenuCanvas.transform.Find("PauseMenu");
        if (pauseMenu == null)
        {
            Logger.LogError("Pause menu is null");
            yield break;
        }

        var panel = (RectTransform)pauseMenu.Find("Panel (1)");
        if (panel == null)
        {
            Logger.LogError("Panel is null");
            yield break;
        }

        panel.offsetMax = panel.offsetMax with { y = 700 };
        panel.offsetMin = panel.offsetMin with { y = -700 };

        var moveUp = new[]
        {
            "GraphicsQuality",
            "Resolution",
            "FullscreenMode",
            "AudioVolume",
            "MouseSensitivity",
            "Invert Grab",
        };
        Transform child = null;
        for (var i = 0; i < moveUp.Length; i++)
        {
            child = pauseMenu.Find(moveUp[i]);

            if (child == null)
            {
                Logger.LogError($"Child {moveUp[i]} is null!");
                continue;
            }

            var childPosition = child.localPosition;
            childPosition.y += 80;
            if (i < 3)
            {
                childPosition.y += 50;
            }

            child.localPosition = childPosition;
        }

        if (child == null)
        {
            Logger.LogDebug("Not creating toggles due to not finding template");
            yield break;
        }

        CreateToggle(pauseMenu, child, 1, InvertClick);

        /*var settingsManager = FindObjectOfType<SettingsManager>(true);
        var invertControls = PlayerPrefs.GetInt("InvertControls") == 1;
        CreateToggle(pauseMenu, child, 2, "Invert Controls", settingsManager.SetInvertControls, invertControls);
        settingsManager.SetInvertControls(invertControls);*/

        CreateToggle(pauseMenu, child, -3 - 1 / 7f, UseVsync);

        CreateToggle(pauseMenu, child, 2, UseCheatScript);
        var playerBody = GameObject.Find("Climber_Hero_Body_Prefab");
        var cheatScript = playerBody.GetComponent<CheatScript>();
        cheatScript.enabled = UseCheatScript.Value;
    }

    private void CreateToggle(Transform parent, Transform original, float offset, ConfigEntry<bool> entry)
    {
        var copy = Instantiate(original, parent);
        var copyPosition = original.localPosition;
        copyPosition.y -= 70 * offset;
        copy.localPosition = copyPosition;

        var text = copy.GetComponentInChildren<TextMeshProUGUI>();
        text.text = entry.Definition.Key;

        var toggle = copy.GetComponentInChildren<Toggle>();
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.m_PersistentCalls.Clear();
        toggle.SetIsOnWithoutNotify(entry.Value);
        entry.SettingChanged += (_, __) => toggle.SetIsOnWithoutNotify(entry.Value);
        toggle.onValueChanged.AddListener(newValue => entry.Value = newValue);
    }

    private void CreateToggle(
        Transform parent,
        Transform original,
        float offset,
        string toggleName,
        UnityAction<bool> onChanged,
        bool currentValue
    )
    {
        var copy = Instantiate(original, parent);
        var copyPosition = original.localPosition;
        copyPosition.y -= 70 * offset;
        copy.localPosition = copyPosition;

        var text = copy.GetComponentInChildren<TextMeshProUGUI>();
        text.text = toggleName;

        var toggle = copy.GetComponentInChildren<Toggle>();
        toggle.onValueChanged.RemoveAllListeners();
        toggle.SetIsOnWithoutNotify(currentValue);
        toggle.onValueChanged.AddListener(onChanged);
    }
}