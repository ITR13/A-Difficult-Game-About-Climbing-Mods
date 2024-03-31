using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Tomlet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpeedrunGhost;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private const float Interval = 0.05f;

    private ConfigEntry<bool> EnableTeleport, EnableFly, EnableQuickSave;
    private ConfigEntry<KeyCode> QuickSave, QuickLoad;
    private ConfigEntry<KeyCode>[] Fly;
    private ConfigEntry<KeyboardShortcut>[] Teleport;

    private float _recordingTimer;
    private float _nextKeyframe;
    private Recorder _recorder;
    private string[] _recorderPaths;
    private List<List<Keyframe>> _recordings = new();
    private List<Playback> _playbacks = new();

    private bool _initalized = false;
    private Rigidbody2D[] _playerBody;
    private float _hovering;

    private QuickSaveData _quickSaveData;

    private void Awake()
    {
        SceneManager.sceneUnloaded += _ => { StopRecording(true); };
        EnableTeleport = Config.Bind(
            "_Toggles",
            "Enable Teleport",
            true,
            "Allow player to teleport to key points"
        );

        EnableFly = Config.Bind(
            "Toggles",
            "Enable Fly",
            false,
            "Allow player to fly with IJKL"
        );

        EnableQuickSave = Config.Bind(
            "Toggles",
            "Enable EnableQuickSave",
            true,
            "Allow player to load and save any position"
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

        Fly = new ConfigEntry<KeyCode>[4];
        var flyDirs = new[] { "Up", "Left", "Down", "Right" };
        var flyKeys = new[] { KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.L };
        for (var i = 0; i < 4; i++)
        {
            Fly[i] = Config.Bind(
                "Keybinds",
                $"Fly {flyDirs[i]}",
                flyKeys[i],
                ""
            );
        }

        Teleport = new ConfigEntry<KeyboardShortcut>[9];
        for (var i = 0; i < 9; i++)
        {
            Teleport[i] = Config.Bind(
                "Keybinds",
                $"Teleport #{i}",
                new KeyboardShortcut(KeyCode.Alpha1 + i),
                $"Teleports to section #{i}, whatever that might be"
            );
        }

        // StartCoroutine(Initialize());
    }

    private IEnumerator Start()
    {
        for (var i = 0; i < 20; i++)
        {
            yield return null;
        }

        SceneManager.sceneLoaded += (_, _) => { StartCoroutine(Initialize()); };
    }

    private IEnumerator Initialize()
    {
        _initalized = false;
        yield return null;

        do
        {
            var heroCharacter = GameObject.Find("HeroCharacter");
            while (heroCharacter == null)
            {
                yield return null;
                heroCharacter = GameObject.Find("HeroCharacter");
            }

            _playerBody = heroCharacter.transform.parent.parent.GetComponentsInChildren<Rigidbody2D>();
        } while (_playerBody.Length == 0);

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
        if (!_initalized) return;

        RecordingStuff();
        QuickSaveStuff();
        TeleportStuff();
    }

    private void FixedUpdate()
    {
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
            FindObjectOfType<PlayerSpawn>().Respawn(endPoints[i]);
            StopRecording(false);
            StartCoroutine(Initialize());
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
        };

        _hovering -= Time.fixedDeltaTime;
        var sumForces = Vector3.zero;
        for (var i = 0; i < 4; i++)
        {
            if (!Input.GetKey(Fly[i].Value)) continue;
            _hovering = 3f;
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

        _recordingTimer = _quickSaveData.Time;
        _recorder.PurgeAfter(_recordingTimer);
        _nextKeyframe = _recorder.Keyframes[^1].Time + Interval;

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
            _recordings.Add(_recorder.Keyframes);
            _recorder = null;
            throw;
        }
    }

    private void StartRecording()
    {
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
        if (save && recorder != null)
        {
            Logger.LogInfo("Saving recording");
            _recordings.Add(recorder.Keyframes);
            var serialized = TomletMain.TomlStringFrom(
                new KeyframeData
                {
                    Paths = _recorderPaths,
                    Keyframes = recorder.Keyframes.ToArray(),
                }
            );
            var folder = Path.Combine(Application.dataPath, "..", "Replays");
            Directory.CreateDirectory(folder);
            var datetime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            File.WriteAllText(
                Path.Combine(folder, datetime + ".json"),
                serialized
            );
        }
    }

    private void IterateDown(Transform transform, List<Transform> list, List<String> names, string parentName = null)
    {
        if (transform.GetComponent<ParticleSystem>()) return;
        var name = (string.IsNullOrEmpty(parentName) ? "" : parentName + "\n") + transform.gameObject.name;

        list.Add(transform);
        names.Add(name);
        foreach (Transform child in transform)
        {
            IterateDown(child, list, names, name);
        }
    }


    private void CreatePlayback(List<Keyframe> keyframes)
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
        Logger.LogDebug("Gathering children");
        IterateDown(armature, children, new List<string>());

        Logger.LogDebug("Creating recorder");
        _playbacks.Add(
            new Playback(
                root,
                children.ToArray(),
                keyframes.ToArray()
            )
        );
    }
}