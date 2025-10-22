using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using SpeedrunningTools;
using UnityEngine;

namespace ITRsSpeedrunTimer;

[BepInPlugin(
    "com.itr.adifficultgameaboutclimbing.speedruntimer",
    MyPluginInfo.PLUGIN_NAME,
    MyPluginInfo.PLUGIN_VERSION
)]
public class Plugin : BaseUnityPlugin
{
    public static Action<string> Log { get; private set; }
    public static Action<string> LogError { get; private set; }

    public static bool UseInGameTime { get; private set; }
    public static bool UseServer { get; private set; }

    public static int ThreadSleepTime { get; private set; }

    private List<string> _text = new List<string>();

    private Body _body;
    private ArmScript_v2 _leftArm, _rightArm;

    private ServerCommand _currentPhase;
    private GUIStyle _modlistStyle, _errorStyle;

    private string _errorString;
    private float _errorTimer;

    private ConfigEntry<bool> _useLiveSplit, _useInGameTime, _showModList, _useGrabSplits;
    private ConfigEntry<int> _threadSleepTime;
    private ConfigEntry<string>[] _splitNames;
    public static string[] SplitNames { get; private set; }

    private float _waitToUpdateTimer = 0;
    private int _skipMode = 2;
    private float _timeSinceSpawning = 0f;

    private bool wasSkippingLastFrame;
    private float _skipTime;

    private void Awake()
    {
        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        UseInGameTime = true;


        _useLiveSplit = Config.Bind(
            "_Toggles",
            "Connect To Livesplit",
            true,
            "Makes the game connect to the LiveSplit and automatically handle splits & start/end"
        );
        _useInGameTime = Config.Bind(
            "_Toggles",
            "Use In Game Time",
            true,
            "If true, the start and end points for in-game-time are used instead of the RTA ones."
        );
        _showModList = Config.Bind(
            "_Toggles",
            "Show Mod List",
            true,
            "Shows a list of installed mods in the top left courner of the screen"
        );
        _useGrabSplits = Config.Bind(
            "_Toggles",
            "Use Grab Splits",
            true,
            "If true, the timer splits a section when you grab a specific object. If false, it uses the original autosplitter positions."
        );

        _threadSleepTime = Config.Bind(
            "Other",
            "Thread Sleep Time",
            16,
            "How often the thread that communicates with livesplit updates (ms)"
        );

        var defaultSplits = new[] { "Intro", "Jungle", "Gears", "Pool", "Construction", "Cave", "Ice", "Ending" };
        _splitNames = defaultSplits
            .Select((value, index) => Config.Bind("Splits", value, value, $"The name of split #{index}"))
            .ToArray();
        SplitNames = defaultSplits;
        for (var i = 0; i < _splitNames.Length; i++)
        {
            var index = i;
            SplitNames[i] = _splitNames[i].Value;
            _splitNames[i].SettingChanged += (_, _) => { SplitNames[index] = _splitNames[index].Value; };
        }

        UseServer = _useLiveSplit.Value;
        _useLiveSplit.SettingChanged += (_, _) => UseServer = _useLiveSplit.Value;
        UseInGameTime = _useInGameTime.Value;
        _useInGameTime.SettingChanged += (_, _) => UseInGameTime = _useInGameTime.Value;

        ThreadSleepTime = Mathf.Max(1, _threadSleepTime.Value);
        _threadSleepTime.SettingChanged += (_, _) => ThreadSleepTime = Mathf.Max(1, _threadSleepTime.Value);

        _modlistStyle = new GUIStyle
        {
            padding = new RectOffset(0, 0, 0, 0),
            richText = false,
            normal =
            {
                textColor = Color.grey,
            },
        };
        _errorStyle = new GUIStyle
        {
            padding = new RectOffset(0, 0, 0, 0),
            richText = false,
            normal =
            {
                textColor = Color.red,
            },
        };

        Log = Logger.LogMessage;
        LogError = s =>
        {
            _errorTimer = 5;
            _errorString = s;
            Logger.LogError(s);
        };

        ClimberMainPatch.OnClimberSpawned += OnClimberSpawned;
        SaveSystemJTimePatch.OnGameWon += timeCompleted =>
        {
            if (!UseInGameTime || !_useLiveSplit.Value) return;
            SocketManager.Command(ServerCommand.SplitFinal, timeCompleted);
        };

        PauseMenuPatch.OnPause += (paused) =>
        {
            if (!UseInGameTime || !_useLiveSplit.Value) return;
            SocketManager.Command(
                paused ? ServerCommand.Pause : ServerCommand.Unpause,
                Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime
            );
        };

        PauseMenuPatch.OnStartTimer += () =>
        {
            if (!UseInGameTime || !_useLiveSplit.Value) return;
            SocketManager.Command(
                ServerCommand.StartTimer,
                Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime
            );
            if (_currentPhase == ServerCommand.StartTimer)
            {
                _currentPhase = ServerCommand.SplitIntro;
            }
        };

        SocketManager.Start();
    }

    private void OnDestroy()
    {
        SocketManager.Stop();
    }

    private void OnClimberSpawned(ClimberMain climber)
    {
        _body = climber.bodyScript;
        _leftArm = climber.arm_Left;
        _rightArm = climber.arm_Right;

        // If the current phase is reset, the player completed a run
        SocketManager.Command(
            _currentPhase != ServerCommand.Reset ? ServerCommand.Reset : ServerCommand.UpdateStatus,
            0
        );

        _currentPhase = ServerCommand.StartTimer;
        _skipMode = 2;
        _timeSinceSpawning = 0f;
        

        wasSkippingLastFrame = true;
        _skipTime = 0;
    }

    private void FixedUpdate()
    {
        _timeSinceSpawning += Time.fixedDeltaTime;
        if (!_useLiveSplit.Value || _body == null) return;

        var y = _body.transform.position.y;
        var inWater = _body.isInWater;

        var anyGrabbing = false;
        var highestGrabbedY = -1f;

        if (_leftArm.grabbedSurface != null)
        {
            anyGrabbing = true;
            highestGrabbedY = _leftArm.grabbedSurface.transform.position.y;
        }

        if (_rightArm.grabbedSurface != null)
        {
            anyGrabbing = true;
            highestGrabbedY = Mathf.Max(_rightArm.grabbedSurface.transform.position.y, highestGrabbedY);
        }

        var splitPos = _body.transform.position;

        var time = Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime;
        if (y < 15)
        {
            _skipMode = 0;
            wasSkippingLastFrame = false;
            _skipTime = 0;
        }
        else if (_skipMode > 0)
        {
            wasSkippingLastFrame = true;
            time = 0;
            _skipTime = 0;
        }
        else if (wasSkippingLastFrame)
        {
            wasSkippingLastFrame = false;
            _skipTime = time;
        }

        time -= _skipTime;

        var grabSplit = _useGrabSplits.Value && _skipMode <= 0;

        if (y > 240 && inWater && !anyGrabbing && _currentPhase < ServerCommand.Reset)
        {
            if (!UseInGameTime)
            {
                SocketManager.Command(ServerCommand.SplitFinal, time, _skipMode-- > 0);
            }

            _currentPhase = ServerCommand.Reset;
        }
        else if ((grabSplit
                     ? highestGrabbedY > 207
                     : splitPos is { y: > 204, x: < 47 } or { y: > 208 }) &&
                 _currentPhase < ServerCommand.SplitFinal)
        {
            SocketManager.Command(ServerCommand.SplitIce, time, _skipMode-- > 0);
            _currentPhase = ServerCommand.SplitFinal;
        }
        else if ((grabSplit ? highestGrabbedY > 154 : splitPos is { y: > 152 }) &&
                 _currentPhase < ServerCommand.SplitIce)
        {
            SocketManager.Command(ServerCommand.SplitCave, time, _skipMode-- > 0);
            _currentPhase = ServerCommand.SplitIce;
        }
        else if ((grabSplit
                     ? highestGrabbedY > 137
                     : splitPos is { y: > 135 }) &&
                 _currentPhase < ServerCommand.SplitCave)
        {
            SocketManager.Command(ServerCommand.SplitConstruction, time, _skipMode-- > 0);
            _currentPhase = ServerCommand.SplitCave;
        }
        else if ((grabSplit
                     ? highestGrabbedY > 112
                     : splitPos is { y: > 109, x: < 20 } or { y: > 120f }) &&
                 _currentPhase < ServerCommand.SplitConstruction)
        {
            SocketManager.Command(ServerCommand.SplitPool, time, _skipMode-- > 0);
            _currentPhase = ServerCommand.SplitConstruction;
        }
        else if ((grabSplit
                     ? inWater && y > 83
                     : splitPos is { y: > 80f and < 87, x: > 8f } or { y: >= 87, x: >= 20f }) &&
                 _currentPhase < ServerCommand.SplitPool)
        {
            SocketManager.Command(ServerCommand.SplitGears, time, _skipMode-- > 0);
            _currentPhase = ServerCommand.SplitPool;
        }
        else if ((grabSplit
                     ? highestGrabbedY > 60
                     : splitPos is { y: > 55, x: < 0 } or { y: > 60, x: > 0 }) &&
                 _currentPhase < ServerCommand.SplitGears)
        {
            SocketManager.Command(ServerCommand.SplitJungle, time, _skipMode-- > 0);
            _currentPhase = ServerCommand.SplitGears;
        }
        else if ((grabSplit
                     ? highestGrabbedY > 33
                     : splitPos is { y: > 31 }) &&
                 _currentPhase < ServerCommand.SplitJungle &&
                 (_skipMode > 0 || splitPos.x > 0))
        {
            SocketManager.Command(ServerCommand.SplitIntro, time, _skipMode-- > 0);
            _currentPhase = ServerCommand.SplitJungle;
        }
        else if (anyGrabbing && _currentPhase < ServerCommand.SplitIntro && !UseInGameTime)
        {
            SocketManager.Command(ServerCommand.StartTimer, time, _skipMode-- > 0);
            _currentPhase = ServerCommand.SplitIntro;
        }
        else if (_currentPhase < ServerCommand.SplitIntro && _timeSinceSpawning >= 0.1f && splitPos.y >= 15)
        {
            SocketManager.Command(ServerCommand.StartTimer, time, _skipMode-- > 0);
            _currentPhase = ServerCommand.SplitIntro;
        }

        return;
    }

    private void Update()
    {
        _errorTimer -= Time.unscaledDeltaTime;
        _waitToUpdateTimer -= Time.unscaledDeltaTime;

        if (_waitToUpdateTimer <= 0)
        {
            _waitToUpdateTimer += 10;
            // Reset phase means we've won
            if (_currentPhase != ServerCommand.Reset)
            {
                SocketManager.SyncTime =
                    Mathf.Round((Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime) * 1000) / 1000 - _skipTime;
            }
        }

        if (_body == null) return;

        var plugins = Chainloader.PluginInfos;
        if (_text.Count == plugins.Count) return;
        _text.Clear();
        foreach (var guid in plugins.Keys)
        {
            _text.Add(guid);
        }
    }

    private void OnGUI()
    {
        if (_showModList.Value)
        {
            foreach (var t in _text)
            {
                GUILayout.Label(t, _modlistStyle);
            }
        }

        if (!(_errorTimer > 0)) return;
        GUILayout.Label(_errorString, _errorStyle);
    }
}