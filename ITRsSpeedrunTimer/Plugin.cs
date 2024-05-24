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

    private List<string> _text = new List<string>();

    private Body _body;
    private ArmScript_v2 _leftArm, _rightArm;

    private ServerCommand _currentPhase;
    private GUIStyle _modlistStyle, _errorStyle;

    private string _errorString;
    private float _errorTimer;

    private ConfigEntry<bool> _useServer, _useInGameTime, _showModList;
    private ConfigEntry<string>[] _splitNames;
    public static string[] SplitNames { get; private set; }
    
    private int _waitToUpdateTimer = 0;

    private void Awake()
    {
        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        UseInGameTime = true;


        _useServer = Config.Bind(
            "_Toggles",
            "Use Server",
            true,
            "Makes the game connect to the LiveSplit Server and automatically handle splits & start/end"
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

        var defaultSplits = new[] { "Intro", "Jungle", "Gears", "Pool", "Construction", "Cave", "Ice", "Ending" };
        _splitNames = defaultSplits
            .Select((value, index) => Config.Bind("Splits", value, value, $"The name of split #{index}"))
            .ToArray();
        SplitNames = defaultSplits;
        for (var i = 0; i < _splitNames.Length; i++)
        {
            var index = i;
            SplitNames[i] = _splitNames[i].Value;
            _splitNames[i].SettingChanged += (_, _) =>
            {
                SplitNames[index] = _splitNames[index].Value;
            };
        }

        UseServer = _useServer.Value;
        _useServer.SettingChanged += (_, _) => UseServer = _useServer.Value;
        UseInGameTime = _useInGameTime.Value;
        _useInGameTime.SettingChanged += (_, _) => UseInGameTime = _useInGameTime.Value;

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
            if (!UseInGameTime || !_useServer.Value) return;
            SocketManager.Command(ServerCommand.SplitFinal, timeCompleted);
        };

        PauseMenuPatch.OnPause += (paused) =>
        {
            if (!UseInGameTime || !_useServer.Value) return;
            SocketManager.Command(
                paused ? ServerCommand.Pause : ServerCommand.Unpause,
                Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime
            );
        };

        PauseMenuPatch.OnStartTimer += () =>
        {
            if (!UseInGameTime || !_useServer.Value) return;
            SocketManager.Command(
                ServerCommand.StartTimer,
                Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime
            );
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
    }

    private void FixedUpdate()
    {
        if (!_useServer.Value || _body == null) return;

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

        var time = Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime;
        switch (_currentPhase)
        {
            case ServerCommand.StartTimer:
                if (anyGrabbing)
                {
                    if (!UseInGameTime)
                    {
                        SocketManager.Command(ServerCommand.StartTimer, time);
                    }

                    _currentPhase = ServerCommand.SplitIntro;
                    break;
                }

                goto case ServerCommand.SplitIntro;
            case ServerCommand.SplitIntro:
                if (highestGrabbedY > 33)
                {
                    SocketManager.Command(ServerCommand.SplitIntro, time);
                    _currentPhase = ServerCommand.SplitJungle;
                    break;
                }

                goto case ServerCommand.SplitJungle;
            case ServerCommand.SplitJungle:
                if (highestGrabbedY > 60)
                {
                    SocketManager.Command(ServerCommand.SplitJungle, time);
                    _currentPhase = ServerCommand.SplitGears;
                    break;
                }

                goto case ServerCommand.SplitGears;
            case ServerCommand.SplitGears:
                if (inWater && y > 83)
                {
                    SocketManager.Command(ServerCommand.SplitGears, time);
                    _currentPhase = ServerCommand.SplitPool;
                    break;
                }

                goto case ServerCommand.SplitPool;
            case ServerCommand.SplitPool:
                if (highestGrabbedY > 112)
                {
                    SocketManager.Command(ServerCommand.SplitPool, time);
                    _currentPhase = ServerCommand.SplitConstruction;
                    break;
                }

                goto case ServerCommand.SplitConstruction;
            case ServerCommand.SplitConstruction:
                if (highestGrabbedY > 137)
                {
                    SocketManager.Command(ServerCommand.SplitConstruction, time);
                    _currentPhase = ServerCommand.SplitCave;
                    break;
                }

                goto case ServerCommand.SplitCave;
            case ServerCommand.SplitCave:
                if (highestGrabbedY > 154)
                {
                    SocketManager.Command(ServerCommand.SplitCave, time);
                    _currentPhase = ServerCommand.SplitIce;
                    break;
                }

                goto case ServerCommand.SplitIce;
            case ServerCommand.SplitIce:
                if (highestGrabbedY > 207)
                {
                    SocketManager.Command(ServerCommand.SplitIce, time);
                    _currentPhase = ServerCommand.SplitFinal;
                    break;
                }

                goto case ServerCommand.SplitFinal;
            case ServerCommand.SplitFinal:
                if (y > 240 && inWater && !anyGrabbing)
                {
                    if (!UseInGameTime)
                    {
                        SocketManager.Command(ServerCommand.SplitFinal, time);
                    }

                    _currentPhase = ServerCommand.Reset;
                }

                break;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.U))
        {
            SocketManager.Command(ServerCommand.StartTimer, 0);
        }

        _errorTimer -= Time.unscaledDeltaTime;


        if (_waitToUpdateTimer-- <= 0)
        {
            _waitToUpdateTimer += 10;
            // Reset phase means we've won
            if (_currentPhase != ServerCommand.Reset)
            {
                SocketManager.SyncTime =
                    Mathf.Round((Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime) * 1000) / 1000;
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