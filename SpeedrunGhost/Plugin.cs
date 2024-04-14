using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpeedrunningTools;

[BepInPlugin(
    "com.itr.adifficultgameaboutclimbing.speedrunningtools",
    "Speedrunning Tools",
    MyPluginInfo.PLUGIN_VERSION
)]
public class Plugin : BaseUnityPlugin
{
    private const float Interval = 0.05f;

    private ConfigEntry<bool> EnableTeleport, EnableFly, EnableQuickSave, EnableRecording;
    private ConfigEntry<bool> SaveRestarts, SaveWins;
    private ConfigEntry<KeyCode> QuickSave, QuickLoad;
    private ConfigEntry<KeyCode>[] Fly;
    private ConfigEntry<KeyboardShortcut>[] Teleport;

    private ConfigEntry<float> HoverTime;

    private ConfigEntry<bool> AutoReplay;

    private float _recordingTimer;
    private float _nextKeyframe;
    private Recorder _recorder;
    private string[] _recorderPaths;
    private List<KeyframeData> _recordings = new();
    private List<Playback> _playbacks = new();

    private bool _initalized = false;
    private Rigidbody2D[] _playerBody;
    private float _hovering;

    private QuickSaveData _quickSaveData;


    private string _replaysFolder;
    private string _activeReplaysFolder;

    private void Awake()
    {
        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());

        EnableTeleport = Config.Bind(
            "_Toggles",
            "Enable Teleport",
            true,
            "Allow player to teleport to key points"
        );

        EnableFly = Config.Bind(
            "_Toggles",
            "Enable Fly",
            true,
            "Allow player to fly with IJKL"
        );

        EnableQuickSave = Config.Bind(
            "_Toggles",
            "Enable QuickSave",
            true,
            "Allow player to load and save any position"
        );

        EnableRecording = Config.Bind(
            "_Toggles",
            "Enable Recording",
            true,
            "If set to false, the player will no longer be recorded in the background"
        );
        EnableRecording.SettingChanged += (sender, args) =>
        {
            if (_recorder == null) StopRecording(true);
            if (EnableRecording.Value) StartRecording();
        };

        HoverTime = Config.Bind(
            "Tweaks",
            "Hover Time",
            0.25f,
            "How long the player will hover in the air after flying"
        );

        QuickSave = Config.Bind(
            "Keybinds",
            "QuickSave",
            KeyCode.G,
            "Saves your current position"
        );
        QuickLoad = Config.Bind(
            "Keybinds",
            "QuickLoad",
            KeyCode.F,
            "Loads the previously quicksaved position"
        );

        SaveWins = Config.Bind(
            "_Toggles",
            "Save Victories",
            true,
            "If true, the replay of your run is saved when you reach the finish line"
        );

        SaveRestarts = Config.Bind(
            "_Toggles",
            "Save Restarts",
            false,
            "If true, the replay of your run is saved when you press ctrl+R"
        );

        Fly = new ConfigEntry<KeyCode>[5];
        var flyDirs = new[] { "Up", "Left", "Down", "Right", "Still" };
        var flyKeys = new[] { KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.O };
        for (var i = 0; i < 5; i++)
        {
            Fly[i] = Config.Bind(
                "Keybinds",
                $"Fly {flyDirs[i]}",
                flyKeys[i],
                i != 4 ? $"Fly {flyDirs[i]}" : "Hover still in the air"
            );
        }

        Teleport = new ConfigEntry<KeyboardShortcut>[9];
        for (var i = 0; i < 9; i++)
        {
            Teleport[i] = Config.Bind(
                "Keybinds",
                $"Teleport #{i + 1}",
                new KeyboardShortcut(KeyCode.Alpha1 + i),
                $"Teleports to section #{i + 1}, whatever that might be"
            );
        }

        AutoReplay = Config.Bind(
            "_Toggles",
            "Replay immediately",
            false,
            "Plays all replays from the current session immediately. Mostly used for testing the mod."
        );

        _replaysFolder = Path.Combine(Application.dataPath, "..", "Replays");
        Directory.CreateDirectory(_replaysFolder);

        _activeReplaysFolder = Path.Combine(Application.dataPath, "..", "ActiveReplays");
        Directory.CreateDirectory(_activeReplaysFolder);

        LoadActiveReplays();

        EndTeleporterPatch.OnGameEnded += () =>
        {
            StopRecording(SaveWins.Value);
            _initalized = false;
        };
        SceneManager.sceneUnloaded += _ => StopRecording(SaveRestarts.Value);
        ClimberMainPatch.OnClimberSpawned += Initialize;
    }


    private void LoadActiveReplays()
    {
        foreach (var path in Directory.EnumerateFiles(_activeReplaysFolder, "*.bin"))
        {
            try
            {
                using var binaryReader = new BinaryReader(File.OpenRead(path));
                var keyframeData = Serialization.Deserialize(binaryReader);
                if (keyframeData.Version != MyPluginInfo.PLUGIN_VERSION)
                {
                    Logger.LogWarning(
                        $"Warning! Replay was created with version {
                            keyframeData.Version
                        }, but current version is {
                            MyPluginInfo.PLUGIN_VERSION
                        }! You might experience some issues"
                    );
                }

                _recordings.Add(keyframeData);
                Logger.LogInfo($"Loaded replay {path}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Logger.LogError($"Encountered exception when reading file '{path}'\n{e.Message}");
            }
        }
    }

    private void Initialize(ClimberMain climberMain)
    {
        _initalized = false;
        _playerBody = climberMain.GetComponentsInChildren<Rigidbody2D>();

        StartRecording();
        _initalized = true;
        Logger.LogInfo("Started Recording");

        foreach (var playback in _playbacks)
        {
            if (playback.Root != null)
            {
                Destroy(playback.Root.gameObject);
            }
        }

        _playbacks.Clear();
        foreach (var rec in _recordings)
        {
            CreatePlayback(rec);
        }
    }

    private void Update()
    {
        if (PauseMenu.GameIsPaused) return;
        if (!_initalized) return;

        RecordingStuff();
        QuickSaveStuff();
        TeleportStuff();
    }

    private void FixedUpdate()
    {
        if (PauseMenu.GameIsPaused) return;
        if (!_initalized) return;
        FlyStuff();
    }

    private void TeleportStuff()
    {
        if (!EnableTeleport.Value) return;
        var endPoints = new[]
        {
            new Vector2(2f, -6f),
            new Vector2(4f, 31f),
            new Vector2(-9f, 55f),
            new Vector2(11f, 86f),
            new Vector2(3f, 110f),
            new Vector2(7f, 135f),
            new Vector2(44f, 154f),
            new Vector2(48f, 245f),
            new Vector2(1.7f, -33f),
        };
        for (var i = 0; i < endPoints.Length; i++)
        {
            if (!Teleport[i].Value.IsDown()) continue;
            _initalized = false;
            StopRecording(false);
            FindObjectOfType<PlayerSpawn>().Respawn(endPoints[i]);
            _hovering = 1.5f;
            return;
        }
    }

    private void FlyStuff()
    {
        if (!EnableFly.Value) return;
        var forces = new[]
        {
            Vector3.up * 10,
            Vector3.left * 10,
            Vector3.down * 10,
            Vector3.right * 10,
            Vector3.zero,
        };

        _hovering -= Time.fixedDeltaTime;
        var sumForces = Vector3.zero;
        for (var i = 0; i < 5; i++)
        {
            if (!Input.GetKey(Fly[i].Value)) continue;
            _hovering = HoverTime.Value;
            sumForces += forces[i];
        }

        if (!(_hovering > 0)) return;

        foreach (var rb in _playerBody)
        {
            rb.velocity = sumForces;
            rb.AddForce(-Physics.gravity * rb.mass);
        }
    }

    private void QuickSaveStuff()
    {
        if (!EnableQuickSave.Value) return;
        if (Input.GetKeyDown(QuickSave.Value))
        {
            DoPhysicsSave();
        }

        if (Input.GetKeyDown(QuickLoad.Value))
        {
            DoPhysicsLoad();
        }
    }


    private void DoPhysicsSave()
    {
        Logger.LogInfo("QuickSave!");
        var positions = new Vector3[_playerBody.Length];
        var rotations = new float[_playerBody.Length];
        var velocities = new Vector2[_playerBody.Length];
        var angularVelocities = new float[_playerBody.Length];

        for (var i = 0; i < _playerBody.Length; i++)
        {
            var rb = _playerBody[i];
            var t = rb.transform;
            positions[i] = t.position;
            rotations[i] = rb.rotation;
            velocities[i] = rb.velocity;
            angularVelocities[i] = rb.angularVelocity;
        }

        _quickSaveData = new QuickSaveData
        {
            Valid = true,
            Time = _recordingTimer,
            Positions = positions,
            Rotations = rotations,
            Velocities = velocities,
            AngularVelocites = angularVelocities,
        };
    }

    private void DoPhysicsLoad()
    {
        if (!_quickSaveData.Valid) return;
        Logger.LogInfo("QuickLoad!");

        for (var i = 0; i < _playerBody.Length; i++)
        {
            var rb = _playerBody[i];
            var t = rb.transform;
            t.position = _quickSaveData.Positions[i];
            rb.rotation = _quickSaveData.Rotations[i];
            rb.velocity = _quickSaveData.Velocities[i];
            rb.angularVelocity = _quickSaveData.AngularVelocites[i];
        }

        if (_recorder != null)
        {
            _recordingTimer = _quickSaveData.Time;
            _recorder.PurgeAfter(_recordingTimer);
            _nextKeyframe = _recorder.Keyframes[^1].Time + Interval;
        }

        foreach (var playback in _playbacks)
        {
            playback.JumpTo(_recordingTimer);
        }
    }

    private void RecordingStuff()
    {
        foreach (var playback in _playbacks)
        {
            playback.Update(Time.deltaTime);
        }

        _recordingTimer += Time.deltaTime;
        if (_recorder == null || _recordingTimer < _nextKeyframe) return;
        _nextKeyframe += Interval;
        if (_nextKeyframe > _recordingTimer)
        {
            _nextKeyframe = _recordingTimer + Interval;
        }

        try
        {
            _recorder.RecordKeyframe(_recordingTimer, _recorder.Keyframes.Count % 200 == 0);
        }
        catch
        {
            // Something went wrong, so we're saving no matter what
            StopRecording(true);
            throw;
        }
    }

    private void StartRecording()
    {
        if (!EnableRecording.Value)
        {
            return;
        }

        _recordingTimer = 0f;
        _nextKeyframe = Interval;

        Logger.LogDebug("Finding HeroCharacter");
        var heroCharacter = GameObject.Find("HeroCharacter").transform;
        Logger.LogDebug("Finding Armature");
        var armature = heroCharacter.Find("Armature");
        var children = new List<Transform>();
        var names = new List<string>();
        Logger.LogDebug("Gathering children");
        IterateDown(armature, children, names);

        Logger.LogDebug("Creating recorder");
        _recorder = new Recorder(heroCharacter, children.ToArray());
        _recorderPaths = names.ToArray();
    }

    private void StopRecording(bool save)
    {
        _quickSaveData.Valid = false;
        var recorder = _recorder;
        _recorder = null;
        if (recorder == null) return;

        var keyFrameData = new KeyframeData
        {
            Version = MyPluginInfo.PLUGIN_VERSION,
            Paths = _recorderPaths,
            Keyframes = recorder.Keyframes.ToArray(),
        };

        if (AutoReplay.Value)
        {
            Logger.LogError($"{AutoReplay.Value} {EnableRecording.Value}");
            _recordings.Add(keyFrameData);
        }

        if (!save) return;

        Logger.LogInfo("Saving recording");
        var datetime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var path = Path.Combine(_replaysFolder, datetime + ".bin");

        using var binaryWriter = new BinaryWriter(File.OpenWrite(path));
        Serialization.Serialize(
            binaryWriter,
            keyFrameData
        );
    }

    private void IterateDown(
        Transform currentTransform,
        List<Transform> list,
        List<string> names,
        string parentName = null
    )
    {
        if (currentTransform.GetComponent<ParticleSystem>()) return;
        var objectName = (string.IsNullOrEmpty(parentName) ? "" : parentName + "\n") + currentTransform.gameObject.name;

        list.Add(currentTransform);
        names.Add(objectName);
        foreach (Transform child in currentTransform)
        {
            IterateDown(child, list, names, objectName);
        }
    }


    private void CreatePlayback(KeyframeData data)
    {
        var prefab = GameObject.Find("HeroCharacter");
        Logger.LogDebug("Creating clone");
        var instance = Instantiate(prefab);

        Logger.LogDebug("Deleting components");
        foreach (var component in instance.GetComponentsInChildren<Component>(true).Reverse())
        {
            switch (component)
            {
                case CheatScript:
                case Body:
                case PlayerBodySoundManager:
                case Collider:
                case Rigidbody2D:
                case Joint2D:
                case AudioSource:
                case ParticleSystem:
                case ParticleSystemRenderer:
                    Destroy(component);
                    break;

                case IKControl:
                case AnimControl:
                    break;
            }
        }


        var root = instance.transform;
        Logger.LogDebug("Finding armature");
        var armature = root.Find("Armature");
        var children = new List<Transform>();
        var names = new List<string>();
        Logger.LogDebug("Gathering children");
        IterateDown(armature, children, names);
        var reorderdChildren = MatchTransformLists(children, names, data.Paths);

        Logger.LogDebug("Creating recorder");
        _playbacks.Add(
            new Playback(
                root,
                reorderdChildren,
                data.Keyframes.ToArray()
            )
        );
        var material = LoadGhostMaterial();
        var meshRenderer = root.Find("Body").GetComponent<SkinnedMeshRenderer>();
        meshRenderer.sharedMaterials = new[] { material, material };
    }

    private Transform[] MatchTransformLists(List<Transform> children, List<string> currentNames, string[] oldNames)
    {
        var result = new Transform[oldNames.Length];
        var prevCurrentIndex = -1;
        var prevMatchingIndex = -1;
        for (var currentIndex = 0; currentIndex < currentNames.Count; currentIndex++)
        {
            var nextMatching = Array.IndexOf(oldNames, currentNames[currentIndex], prevMatchingIndex + 1);
            if (nextMatching == -1) continue;

            var unassigned = Mathf.Min(currentIndex - prevCurrentIndex, nextMatching - prevMatchingIndex) - 1;

            for (var i = 0; i < unassigned; i++)
            {
                result[prevMatchingIndex + i + 1] = children[prevCurrentIndex + i + 1];
            }

            result[nextMatching] = children[currentIndex];

            if (nextMatching == result.Length - 1) break;
            prevCurrentIndex = currentIndex;
            prevMatchingIndex = nextMatching;
        }

        return result;
    }

    private Material LoadGhostMaterial()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "SpeedrunningTools.ghost";
        Logger.LogInfo(resourceName);
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Debug.LogError("(stream) Failed to load ghost material AssetBundle!");
            return null;
        }

        var buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        var assetBundle = AssetBundle.LoadFromMemory(buffer);
        if (assetBundle == null)
        {
            Debug.LogError("(bundle) Failed to load ghost material AssetBundle!");
            return null;
        }

        var matName = "assets/customshaders/ghost/ghost material.mat";
        var ghostMaterial = assetBundle.LoadAsset<Material>(matName);
        assetBundle.Unload(false);
        return ghostMaterial;
    }
}