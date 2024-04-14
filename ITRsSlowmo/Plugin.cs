using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ITRsSlowmo;

[BepInPlugin(
    "com.itr.adifficultgameaboutclimbing.slowmo",
    "ITR's Slowmo",
    MyPluginInfo.PLUGIN_VERSION
)]
public class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        _enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            "If set to false, the timescale is 1"
        );
        _enabled.SettingChanged += (_, __) => Time.timeScale = _enabled.Value ? _timeScale.Value : 1f;

        _timeScale = Config.Bind(
            "General",
            "Timescale",
            0.2f,
            "Timescale"
        );

        _toggleTimescale = Config.Bind(
            "General",
            "Toggle",
            new KeyboardShortcut(KeyCode.T, KeyCode.LeftControl),
            "Toggles if slowdown is enabled or not"
        );

        _speedDown = Config.Bind(
            "General",
            "Speed Down",
            new KeyboardShortcut(KeyCode.N, KeyCode.LeftControl),
            "Slows the game down to 3/4th the current speed"
        );
        _speedUp = Config.Bind(
            "General",
            "Speed up",
            new KeyboardShortcut(KeyCode.M, KeyCode.LeftControl),
            "Speeds the game up to 4/3rds the current speed"
        );

        var scale = _timeScale.Value;
        if (scale >= 1)
        {
            _currentIndex = 10 + Mathf.RoundToInt((scale - 1) * 4);
        }
        else if (_timeScale.Value >= 0.1f)
        {
            _currentIndex = Mathf.RoundToInt(_timeScale.Value * 10);
        }
        else
        {
            _currentIndex = 1 + Mathf.RoundToInt(Mathf.Log(_timeScale.Value * 10) / Mathf.Log(2));
        }
    }

    private ConfigEntry<bool> _enabled;
    private ConfigEntry<float> _timeScale;
    private ConfigEntry<KeyboardShortcut> _speedUp, _speedDown, _toggleTimescale;

    private int _currentIndex = 0;

    private void Update()
    {
        if (PauseMenu.GameIsPaused) return;
        if (_toggleTimescale.Value.IsDown()) _enabled.Value = !_enabled.Value;
        if (!_enabled.Value) return;

        if (_speedUp.Value.IsDown())
        {
            _currentIndex++;
            SetSlowMoFromCurrentIndex();
        }
        else if (_speedDown.Value.IsDown())
        {
            _currentIndex--;
            SetSlowMoFromCurrentIndex();
        }

        Time.timeScale = _timeScale.Value;
    }

    private void SetSlowMoFromCurrentIndex()
    {
        if (_currentIndex >= 10)
        {
            _timeScale.Value = 1 + (_currentIndex - 10) * 0.25f;
        }
        else if (_currentIndex > 0)
        {
            _timeScale.Value = _currentIndex / 10f;
        }
        else
        {
            _timeScale.Value = Mathf.Pow(2, _currentIndex - 2) * 0.1f;
        }

        Logger.LogInfo($"New timescale is {_timeScale.Value}");
    }

    private void OnGUI()
    {
        GUILayout.Label($"TimeScale: {Time.timeScale}");
    }
}