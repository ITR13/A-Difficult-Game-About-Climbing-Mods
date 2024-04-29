using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ITRsMoreSettings;

[BepInPlugin("com.itr.adifficultgameaboutclimbing.moresettings", "ITR's More Settings", "1.0.1")]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<bool> InvertClick, UseCheatScript, UseVsync;
    private ConfigEntry<int> OnDemandRenderingCount;
    private TMP_Dropdown _refreshRateDropdown, _inputRateDropdown;

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
            "Enable Cheats",
            false,
            "If true, you can hold ctrl and WASD to fly, press P to change the time, and more"
        );

        UseVsync = Config.Bind(
            "General.Toggles",
            "Vsync",
            QualitySettings.vSyncCount != 0,
            "Enables or disables vsync"
        );

        OnDemandRenderingCount = Config.Bind(
            "General",
            "Input Framerate Multiplier",
            1,
            "Sets OnDemandRendering.renderFrameInterval"
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
            var playerBody = GameObject.Find("Climber_Hero_Body_Prefab");
            var cheatScript = playerBody.GetComponent<CheatScript>();
            cheatScript.enabled = UseCheatScript.Value;
        };

        QualitySettings.vSyncCount = UseVsync.Value ? 1 : 0;
        UseVsync.SettingChanged += (_, _) =>
        {
            Logger.LogDebug($"Vsync is now {UseVsync.Value}");
            QualitySettings.vSyncCount = UseVsync.Value ? 1 : 0;
            UpdateDropdownText();
            if (_refreshRateDropdown != null) _refreshRateDropdown.enabled = !UseVsync.Value;
        };

        OnDemandRendering.renderFrameInterval = Mathf.Max(1, OnDemandRenderingCount.Value);
        OnDemandRenderingCount.SettingChanged += (_, _) => { SetFPS(_refreshRateDropdown.value); };

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

        yield return null;

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

        panel.offsetMax = panel.offsetMax with { y = 750 };
        panel.offsetMin = panel.offsetMin with { y = -750 };

        var moveUp = new[]
        {
            "GraphicsQuality",
            "Resolution",
            "FullscreenMode",
            "RefreshRate",
            "AudioVolume",
            "MouseSensitivity",
            "ControllerSensitivity",
            "InvertMouse",
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
            childPosition.y += 10;
            if (i < 4)
            {
                childPosition.y += 120;
            }

            child.localPosition = childPosition;

            // Refresh Rate
            if (i == 3)
            {
                _refreshRateDropdown = child.GetComponentInChildren<TMP_Dropdown>();
            }
        }

        if (_refreshRateDropdown == null)
        {
            Logger.LogDebug("Not adding dropdown options due to not finding refreshRateDropdown");
        }
        else
        {
            CreateInputDropdown(pauseMenu, _refreshRateDropdown.transform.parent, 1);

            // Clearing it twice here, but I just wanna show intent so meh
            _refreshRateDropdown.enabled = !UseVsync.Value;
            // _refreshRateDropdown.onValueChanged.RemoveAllListeners();
            _refreshRateDropdown.onValueChanged.m_PersistentCalls.Clear();
            _refreshRateDropdown.onValueChanged.AddListener(SetFPS);
        }

        if (child == null)
        {
            Logger.LogDebug("Not creating toggles due to not InvertMouse");
            yield break;
        }

        CreateToggle(pauseMenu, child, -3 - 1 / 7f, UseVsync);

        CreateToggle(pauseMenu, child, 3, InvertClick);

        /*var settingsManager = FindObjectOfType<SettingsManager>(true);
        var invertControls = PlayerPrefs.GetInt("InvertControls") == 1;
        CreateToggle(pauseMenu, child, 2, "Invert Controls", settingsManager.SetInvertControls, invertControls);
        settingsManager.SetInvertControls(invertControls);*/

        CreateToggle(pauseMenu, child, 4, UseCheatScript);
        var playerBody = GameObject.Find("Climber_Hero_Body_Prefab");
        var cheatScript = playerBody.GetComponent<CheatScript>();
        cheatScript.enabled = UseCheatScript.Value;
    }

    private void CreateInputDropdown(Transform parent, Transform original, float offset)
    {
        var copy = Instantiate(original, parent);
        var copyPosition = original.localPosition;
        copyPosition.y -= 70 * offset;
        copy.localPosition = copyPosition;

        var text = copy.GetComponentInChildren<TextMeshProUGUI>();
        text.text = "Input Rate";

        _inputRateDropdown = copy.GetComponentInChildren<TMP_Dropdown>();
        _inputRateDropdown.onValueChanged.RemoveAllListeners();
        _inputRateDropdown.onValueChanged.m_PersistentCalls.Clear();

        _inputRateDropdown.ClearOptions();
        _inputRateDropdown.AddOptions(new List<string> { "", "", "", "" });

        _inputRateDropdown.onValueChanged.AddListener(newValue => OnDemandRenderingCount.Value = newValue + 1);
        _inputRateDropdown.SetValueWithoutNotify(OnDemandRenderingCount.Value - 1);
        _inputRateDropdown.RefreshShownValue();
    }

    private void SetFPS(int selection)
    {
        OnDemandRendering.renderFrameInterval = OnDemandRenderingCount.Value;
        Application.targetFrameRate = selection switch
        {
            0 => 60 * OnDemandRenderingCount.Value,
            1 => 100 * OnDemandRenderingCount.Value,
            2 => 120 * OnDemandRenderingCount.Value,
            3 => 144 * OnDemandRenderingCount.Value,
            4 => 240 * OnDemandRenderingCount.Value,
            5 => 0,
            _ => Application.targetFrameRate
        };
        PlayerPrefs.SetInt("fps", Application.targetFrameRate);
        PlayerPrefs.Save();

        UpdateDropdownText();
    }

    private void UpdateDropdownText()
    {
        var baseFramerate = UseVsync.Value
            ? Screen.currentResolution.refreshRate
            : Application.targetFrameRate / OnDemandRenderingCount.Value;
        for (var i = 1; i <= 4; i++)
        {
            _inputRateDropdown.options[i - 1].text = baseFramerate == 0 ? $"Unlimited x {i}" : $"{baseFramerate * i}";
        }

        _inputRateDropdown.RefreshShownValue();
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

        Destroy(copy.GetChild(6).gameObject);
        Destroy(copy.GetChild(5).gameObject);
        Destroy(copy.GetChild(3).gameObject);
        Destroy(copy.GetChild(2).gameObject);
        Destroy(copy.GetChild(1).gameObject);
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