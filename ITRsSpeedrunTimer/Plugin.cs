using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
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
    private float _syncTimer;

    private ServerCommand _currentPhase;
    private CancellationTokenSource _cancellationTokenSource = new();
    private GUIStyle _modlistStyle, _errorStyle;

    private string _errorString;
    private float _errorTimer;

    private ConfigEntry<bool> _useServer, _useInGameTime, _showModList;

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
            SocketManager.TimeToSync = timeCompleted;
            SocketManager.Commands.Enqueue(ServerCommand.SplitFinal);
        };

        PauseMenuPatch.OnPause += (paused) =>
        {
            if (!UseInGameTime || !_useServer.Value) return;
            SocketManager.Commands.Enqueue(paused ? ServerCommand.Pause : ServerCommand.Unpause);
            SocketManager.TimeToSync = (Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime);
        };

        PauseMenuPatch.OnStartTimer += () =>
        {
            if (!UseInGameTime || !_useServer.Value) return;
            SocketManager.Commands.Enqueue(ServerCommand.StartTimer);
            SocketManager.TimeToSync = (Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime);
        };

        var thread = new Thread(() => { SocketManager.ThreadedLoop(_cancellationTokenSource.Token); });
        thread.Start();
    }

    private void OnDestroy()
    {
        _cancellationTokenSource.Cancel();
    }

    private void OnClimberSpawned(ClimberMain climber)
    {
        _body = climber.bodyScript;
        _leftArm = climber.arm_Left;
        _rightArm = climber.arm_Right;

        if (_currentPhase != ServerCommand.Reset)
        {
            // If the current phase is reset, the player completed a run
            SocketManager.Commands.Clear();
            SocketManager.Commands.Enqueue(ServerCommand.Reset);
        }
        else
        {
            SocketManager.Commands.Enqueue(ServerCommand.UpdateStatus);
        }

        _currentPhase = ServerCommand.StartTimer;
    }

    private void FixedUpdate()
    {
        if (!_useServer.Value || _body == null) return;
        // Reset phase means we've won
        if (_currentPhase != ServerCommand.Reset)
        {
            _syncTimer -= Time.fixedDeltaTime;
            if (_syncTimer < 0)
            {
                _syncTimer += 5;
                SocketManager.TimeToSync =
                    Mathf.Round((Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime) * 1000) / 1000;
            }
        }

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

        switch (_currentPhase)
        {
            case ServerCommand.StartTimer:
                if (anyGrabbing)
                {
                    if (!UseInGameTime)
                    {
                        SocketManager.Commands.Enqueue(ServerCommand.StartTimer);
                    }

                    _currentPhase = ServerCommand.SplitIntro;
                    break;
                }

                goto case ServerCommand.SplitIntro;
            case ServerCommand.SplitIntro:
                if (highestGrabbedY > 33)
                {
                    SocketManager.Commands.Enqueue(ServerCommand.SplitIntro);
                    _currentPhase = ServerCommand.SplitJungle;
                    break;
                }

                goto case ServerCommand.SplitJungle;
            case ServerCommand.SplitJungle:
                if (highestGrabbedY > 60)
                {
                    SocketManager.Commands.Enqueue(ServerCommand.SplitJungle);
                    _currentPhase = ServerCommand.SplitGears;
                    break;
                }

                goto case ServerCommand.SplitGears;
            case ServerCommand.SplitGears:
                if (inWater && y > 83)
                {
                    SocketManager.Commands.Enqueue(ServerCommand.SplitGears);
                    _currentPhase = ServerCommand.SplitPool;
                    break;
                }

                goto case ServerCommand.SplitPool;
            case ServerCommand.SplitPool:
                if (highestGrabbedY > 112)
                {
                    SocketManager.Commands.Enqueue(ServerCommand.SplitPool);
                    _currentPhase = ServerCommand.SplitConstruction;
                    break;
                }

                goto case ServerCommand.SplitConstruction;
            case ServerCommand.SplitConstruction:
                if (highestGrabbedY > 137)
                {
                    SocketManager.Commands.Enqueue(ServerCommand.SplitConstruction);
                    _currentPhase = ServerCommand.SplitCave;
                    break;
                }

                goto case ServerCommand.SplitCave;
            case ServerCommand.SplitCave:
                if (highestGrabbedY > 154)
                {
                    SocketManager.Commands.Enqueue(ServerCommand.SplitCave);
                    _currentPhase = ServerCommand.SplitIce;
                    break;
                }

                goto case ServerCommand.SplitIce;
            case ServerCommand.SplitIce:
                if (highestGrabbedY > 207)
                {
                    SocketManager.Commands.Enqueue(ServerCommand.SplitIce);
                    _currentPhase = ServerCommand.SplitFinal;
                    break;
                }

                goto case ServerCommand.SplitFinal;
            case ServerCommand.SplitFinal:
                if (y > 240 && inWater && !anyGrabbing)
                {
                    if (!UseInGameTime)
                    {
                        SocketManager.Commands.Enqueue(ServerCommand.SplitFinal);
                    }

                    _currentPhase = ServerCommand.Reset;
                }

                break;
        }
    }

    private void Update()
    {
        _errorTimer -= Time.unscaledDeltaTime;

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