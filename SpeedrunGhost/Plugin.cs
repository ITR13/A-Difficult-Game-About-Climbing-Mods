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

    private ConfigEntry<bool> _enableTeleport, _enableFly, _enableQuickSave, _enableRecording;
    private ConfigEntry<bool> _saveRestarts, _saveWins;
    private ConfigEntry<KeyCode> _quickSave, _quickLoad;
    private ConfigEntry<KeyCode>[] _fly;
    private ConfigEntry<KeyboardShortcut>[] _teleport;

    private ConfigEntry<float> _hoverTime;

    private ConfigEntry<bool> _autoReplay;

    private ConfigEntry<bool> _useNonTransparentGhostTexture;
    private ConfigEntry<float> _ghostTransparency;

    private float _recordingTimer;
    private float _nextKeyframe;
    private Recorder _recorder;
    private string[] _recorderPaths;
    private readonly List<KeyframeData> _recordings = new();
    private readonly List<Playback> _playbacks = new();

    private bool _initalized;
    private Rigidbody2D[] _playerBodies;
    private LegScript[] _playerLegScripts;
    private Transform[] _playerTransforms;

    private float _hovering;

    private QuickSaveData _quickSaveData;

    private string _replaysFolder;
    private string _activeReplaysFolder;

    private Material _ghostMaterial;
    private static readonly int SmoothAdd = Shader.PropertyToID("_SmoothAdd");

    private void Awake()
    {
        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());

        _enableTeleport = Config.Bind(
            "_Toggles",
            "Enable Teleport",
            true,
            "Allow player to teleport to key points"
        );

        _enableFly = Config.Bind(
            "_Toggles",
            "Enable Fly",
            true,
            "Allow player to fly with IJKL"
        );

        _enableQuickSave = Config.Bind(
            "_Toggles",
            "Enable QuickSave",
            true,
            "Allow player to load and save any position"
        );

        _enableRecording = Config.Bind(
            "_Toggles",
            "Enable Recording",
            true,
            "If set to false, the player will no longer be recorded in the background"
        );
        _enableRecording.SettingChanged += (_, _) =>
        {
            if (_recorder == null) StopRecording(true);
            if (_enableRecording.Value) StartRecording(FindObjectOfType<ClimberMain>());
        };

        _hoverTime = Config.Bind(
            "Tweaks",
            "Hover Time",
            0.25f,
            "How long the player will hover in the air after flying"
        );

        _quickSave = Config.Bind(
            "Keybinds",
            "QuickSave",
            KeyCode.G,
            "Saves your current position"
        );
        _quickLoad = Config.Bind(
            "Keybinds",
            "QuickLoad",
            KeyCode.F,
            "Loads the previously quicksaved position"
        );

        _saveWins = Config.Bind(
            "_Toggles",
            "Save Victories",
            true,
            "If true, the replay of your run is saved when you reach the finish line"
        );

        _saveRestarts = Config.Bind(
            "_Toggles",
            "Save Restarts",
            false,
            "If true, the replay of your run is saved when you press ctrl+R"
        );

        _useNonTransparentGhostTexture = Config.Bind(
            "Tweaks",
            "Opaque Ghost",
            false,
            "Uses an alternative texture for the ghosts"
        );
        _ghostTransparency = Config.Bind(
            "Tweaks",
            "Ghost Transparency",
            0.25f,
            "How transparent the texture will be, where 1 is least transparent"
        );

        _fly = new ConfigEntry<KeyCode>[5];
        var flyDirs = new[] { "Up", "Left", "Down", "Right", "Still" };
        var flyKeys = new[] { KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.O };
        for (var i = 0; i < 5; i++)
        {
            _fly[i] = Config.Bind(
                "Keybinds",
                $"Fly {flyDirs[i]}",
                flyKeys[i],
                i != 4 ? $"Fly {flyDirs[i]}" : "Hover still in the air"
            );
        }

        _teleport = new ConfigEntry<KeyboardShortcut>[9];
        for (var i = 0; i < 9; i++)
        {
            _teleport[i] = Config.Bind(
                "Keybinds",
                $"Teleport #{i + 1}",
                new KeyboardShortcut(KeyCode.Alpha1 + i),
                $"Teleports to section #{i + 1}, whatever that might be"
            );
        }

        _autoReplay = Config.Bind(
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
            _initalized = false;
            StopRecording(_saveWins.Value, Time.time + SaveSystemJ.timeFromSave - SaveSystemJ.startTime);
        };
        SceneManager.sceneUnloaded += _ =>
        {
            if (!_initalized) return;
            _initalized = false;
            StopRecording(_saveRestarts.Value);
        };
        ClimberMainPatch.OnClimberSpawned += Initialize;

        LoadGhostMaterial();
        _useNonTransparentGhostTexture.SettingChanged += (_, _) =>
        {
            Destroy(_ghostMaterial);
            _ghostMaterial = null;
            LoadGhostMaterial();
        };
        _ghostTransparency.SettingChanged += (_, _) => SetGhostTransparency();
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
        _playerBodies = climberMain.GetComponentsInChildren<Rigidbody2D>();
        _playerLegScripts = climberMain.GetComponentsInChildren<LegScript>();

        var transforms = new List<Transform>();
        IterateDown(climberMain.transform, transforms, new List<string>());
        _playerTransforms = transforms.ToArray();

        StartRecording(climberMain);
        _initalized = true;

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
            CreatePlayback(climberMain, rec);
        }
    }

    private void Update()
    {
        if (PauseMenu.GameIsPaused) return;
        if (!_initalized) return;

        RecordingStuff();
        QuickSaveStuff();
        TeleportStuff();
        RestartStuff();
    }

    private void FixedUpdate()
    {
        if (PauseMenu.GameIsPaused) return;
        if (!_initalized) return;
        FlyStuff();
    }

    private void TeleportStuff()
    {
        if (!_enableTeleport.Value) return;
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
            if (!_teleport[i].Value.IsDown()) continue;
            _initalized = false;
            StopRecording(false);
            FindObjectOfType<PlayerSpawn>().Respawn(endPoints[i]);
            _hovering = 1.5f;
            return;
        }
    }

    private void RestartStuff()
    {
        if (!Input.GetKeyDown(KeyCode.B)) return;
        StartCoroutine(RestartRoutine());
    }

    private IEnumerator RestartRoutine()
    {
        if (PauseMenu.GameIsPaused)
        {
            var pauseMenu = FindObjectOfType<PauseMenu>(true);
            pauseMenu.ResumeGame();
        }

        yield return new WaitForFixedUpdate();
        SaveSystemJ.NewGame(true);
        SceneManager.LoadScene(0);
    }

    private void FlyStuff()
    {
        if (!_enableFly.Value) return;
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
            if (!Input.GetKey(_fly[i].Value)) continue;
            _hovering = _hoverTime.Value;
            sumForces += forces[i];
        }

        if (!(_hovering > 0)) return;

        foreach (var rb in _playerBodies)
        {
            rb.velocity = sumForces;
            rb.AddForce(-Physics.gravity * rb.mass);
        }
    }

    private void QuickSaveStuff()
    {
        if (!_enableQuickSave.Value) return;
        if (Input.GetKeyDown(_quickSave.Value))
        {
            DoPhysicsSave();
        }

        if (Input.GetKeyDown(_quickLoad.Value))
        {
            DoPhysicsLoad();
        }
    }


    private void DoPhysicsSave()
    {
        Logger.LogInfo("QuickSave!");
        var positions = new Vector3[_playerBodies.Length];
        var rotations = new float[_playerBodies.Length];
        var velocities = new Vector2[_playerBodies.Length];
        var angularVelocities = new float[_playerBodies.Length];

        var legOffsets = new Vector2[_playerLegScripts.Length];

        var transformPositions = new Vector3[_playerTransforms.Length];
        var transformRotations = new Quaternion[_playerTransforms.Length];

        for (var i = 0; i < _playerTransforms.Length; i++)
        {
            var t = _playerTransforms[i];
            if (t == null) continue;
            transformPositions[i] = t.position;
            transformRotations[i] = t.rotation;
        }

        for (var i = 0; i < _playerBodies.Length; i++)
        {
            var rb = _playerBodies[i];
            if (rb == null) continue;
            positions[i] = rb.position;
            rotations[i] = rb.rotation;
            velocities[i] = rb.velocity;
            angularVelocities[i] = rb.angularVelocity;
        }

        for (var i = 0; i < _playerLegScripts.Length; i++)
        {
            if (_playerLegScripts[i] == null) continue;
            legOffsets[i] = Traverse.Create(_playerLegScripts[i]).Field("offset").GetValue<Vector2>();
        }

        _quickSaveData = new QuickSaveData
        {
            Valid = true,
            Time = _recordingTimer,
            Positions = positions,
            Rotations = rotations,
            Velocities = velocities,
            AngularVelocites = angularVelocities,
            LegOffsets = legOffsets,

            TransformPositions = transformPositions,
            TransformRotations = transformRotations,
        };
    }

    private void DoPhysicsLoad()
    {
        if (!_quickSaveData.Valid) return;
        Logger.LogInfo("QuickLoad!");

        for (var i = 0; i < _playerTransforms.Length; i++)
        {
            var t = _playerTransforms[i];
            if (t == null) continue;
            t.position = _quickSaveData.TransformPositions[i];
            t.rotation = _quickSaveData.TransformRotations[i];
        }

        for (var i = 0; i < _playerBodies.Length; i++)
        {
            var rb = _playerBodies[i];
            if (rb == null) return;
            rb.position = _quickSaveData.Positions[i];
            rb.rotation = _quickSaveData.Rotations[i];
            rb.velocity = _quickSaveData.Velocities[i];
            rb.angularVelocity = _quickSaveData.AngularVelocites[i];
        }

        for (var i = 0; i < _playerLegScripts.Length; i++)
        {
            if (_playerLegScripts[i] == null) continue;
            Traverse.Create(_playerLegScripts[i]).Field("offset").SetValue(_quickSaveData.LegOffsets[i]);
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

    private void StartRecording(ClimberMain climberMain)
    {
        if (!_enableRecording.Value)
        {
            Logger.LogInfo("Not Starting Recording");
            return;
        }

        _recordingTimer = 0f;
        _nextKeyframe = Interval;

        Logger.LogDebug("Finding HeroCharacter");
        var heroCharacter = climberMain.transform.Find("Climber_Hero_Body_Prefab").Find("HeroCharacter");
        Logger.LogDebug("Finding Armature");
        var armature = heroCharacter.Find("Armature");
        var children = new List<Transform>();
        var names = new List<string>();
        Logger.LogDebug("Gathering children");
        IterateDown(armature, children, names);

        Logger.LogDebug("Creating recorder");
        _recorder = new Recorder(heroCharacter, children.ToArray());
        _recorderPaths = names.ToArray();

        Logger.LogInfo("Started Recording");
    }

    private void StopRecording(bool save, float time = 0)
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

        if (_autoReplay.Value)
        {
            Logger.LogError($"{_autoReplay.Value} {_enableRecording.Value}");
            _recordings.Add(keyFrameData);
        }

        if (!save) return;

        Logger.LogInfo("Saving recording");

        var datetime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        if (time > 10)
        {
            time = Mathf.Round(time * 10000) / 10000;
            var seconds = time % 60;
            var minutes = Mathf.FloorToInt(time / 60);
            var hours = minutes / 60;

            datetime = $"{datetime}__{hours:00}-{minutes:00}-{seconds:00.0000}";
        }

        var path = Path.Combine(_replaysFolder, datetime + ".bin");

        var retries = 0;
        while (File.Exists(path))
        {
            path = Path.Combine(_replaysFolder, $"{datetime}.{++retries}.bin");
        }

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


    private void CreatePlayback(ClimberMain climberMain, KeyframeData data)
    {
        var prefab = climberMain.transform.Find("Climber_Hero_Body_Prefab").Find("HeroCharacter");
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
        var meshRenderer = root.Find("Body").GetComponent<SkinnedMeshRenderer>();
        meshRenderer.sharedMaterials = new[] { _ghostMaterial, _ghostMaterial };
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

    private void LoadGhostMaterial()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = _useNonTransparentGhostTexture.Value
            ? "SpeedrunningTools.ghost"
            : "SpeedrunningTools.ghost2";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Logger.LogError("(stream) Failed to load ghost material AssetBundle!");
            return;
        }

        var buffer = new byte[stream.Length];
        _ = stream.Read(buffer, 0, buffer.Length);
        var assetBundle = AssetBundle.LoadFromMemory(buffer);
        if (assetBundle == null)
        {
            Logger.LogError("(bundle) Failed to load ghost material AssetBundle!");
            return;
        }

        var matName = assetBundle.GetAllAssetNames().FirstOrDefault(path => path.EndsWith(".mat"));
        if (string.IsNullOrEmpty(matName))
        {
            Logger.LogError("(bundle) No materials in AssetBundle!");
            return;
        }

        _ghostMaterial = assetBundle.LoadAsset<Material>(matName);
        assetBundle.Unload(false);
        SetGhostTransparency();
    }

    private void SetGhostTransparency()
    {
        if (_useNonTransparentGhostTexture.Value) return;
        _ghostMaterial.SetFloat(SmoothAdd, _ghostTransparency.Value);
    }
}