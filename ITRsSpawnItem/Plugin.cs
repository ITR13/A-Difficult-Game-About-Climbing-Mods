using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using SpeedrunningTools;
using UnityEngine;

namespace ITRsSpawnItem;

[BepInPlugin("com.itr.adifficultgameaboutclimbing.spawnitem", MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<float> _automaticSpawnTime, _variance;
    private ConfigEntry<bool> _incrementalMode;
    private ConfigEntry<KeyCode> _manualSpawnKey;
    private ConfigEntry<KeyCode> _manualSpawnManyKey;

    private Transform _playerTransform;
    private List<GameObject> _prefabs = new();
    private List<float> _appearChance = new();

    private float _timer;

    private Queue<(float, GameObject)> _despawnQueue = new Queue<(float, GameObject)>();

    private void Awake()
    {
        _automaticSpawnTime = Config.Bind(
            "General",
            "Spawn Time",
            6f,
            "How long it is between each item spawning"
        );
        _variance = Config.Bind(
            "General",
            "Variance",
            0.5f,
            "How much longer or shorter the time between each spawn can randomly be. 0 = no variance, 1 = max variance."
        );
        _incrementalMode = Config.Bind(
            "General",
            "Incremental Mode",
            true,
            "The higher up you get, the higher the more frequent spawning will be"
        );
        _manualSpawnKey = Config.Bind(
            "General",
            "Spawn Item",
            KeyCode.None,
            "Manually spawn an item"
        );
        _manualSpawnManyKey = Config.Bind(
            "General",
            "Spawn 10 Items",
            KeyCode.None,
            "Manually spawn 5 items at the same time"
        );

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        var spawnItem =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("ITRsSpawnItem.spawnitem");
        ClimberMainPatch.OnClimberSpawned += OnClimberSpawned;

        var assetBundle = AssetBundle.LoadFromStream(spawnItem);

        _prefabs.Clear();
        _appearChance.Clear();
        var totalAppearChance = 0f;
        foreach (var assetName in assetBundle.GetAllAssetNames())
        {
            if (!assetName.EndsWith(".prefab")) continue;
            var prefab = assetBundle.LoadAsset<GameObject>(assetName);
            _prefabs.Add(prefab);
            var rb = prefab.GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                _prefabs.RemoveAt(_prefabs.Count - 1);
                Logger.LogError($"Prefab {assetName} doesn't have a rigidbody!");
                continue;
            }

            var mass = rb.mass;
            rb.mass = mass * 2;
            var chance = Mathf.Max(25 - mass, 5);
            totalAppearChance += chance;
            _appearChance.Add(chance);
        }

        if (totalAppearChance <= 0.2f) return;

        var cumsum = 0f;
        for (var i = 0; i < _appearChance.Count; i++)
        {
            cumsum += _appearChance[i];
            _appearChance[i] = cumsum / totalAppearChance;
        }
    }

    private void OnClimberSpawned(ClimberMain climberMain)
    {
        _playerTransform = climberMain.body.transform;
        _timer = 10f;
    }

    private void Update()
    {
        MaybeDespawn();

        if (_playerTransform == null) return;

        float degree = 1;
        if (_incrementalMode.Value)
        {
            degree = Mathf.Clamp((_playerTransform.position.y - 20) / 30, 0.5f, 100);
        }

        var spawnedThisFrame = DoAutomaticSpawn(degree);

        if (_manualSpawnKey.Value != KeyCode.None && Input.GetKeyDown(_manualSpawnKey.Value))
        {
            SpawnItem();
            spawnedThisFrame = true;
        }

        if (_manualSpawnManyKey.Value != KeyCode.None && Input.GetKeyDown(_manualSpawnManyKey.Value))
        {
            for (var i = 0; i < 5; i++)
            {
                SpawnItem();
            }

            spawnedThisFrame = true;
        }

        if (spawnedThisFrame && degree > 5)
        {
            var count = (int)Random.Range(0, degree / 5f);
            for (var i = 0; i < count; i++)
            {
                SpawnItem();
            }
        }
    }

    private bool DoAutomaticSpawn(float degree)
    {
        var spawnTime = _automaticSpawnTime.Value;
        if (!(spawnTime > 0.1)) return false;
        _timer -= Time.deltaTime;
        if (_timer > 0) return false;
        var currSpawnTime = spawnTime / degree;
        var variance = Mathf.Clamp01(_variance.Value);
        var minSpawnTime = Mathf.Max(0.1f, currSpawnTime * (1 - variance));
        var maxSpawnTime = currSpawnTime / (Mathf.Max(1 - variance, 0.1f));
        maxSpawnTime *= Mathf.Sqrt(degree);

        if (minSpawnTime >= maxSpawnTime)
        {
            _timer += currSpawnTime;
        }
        else
        {
            _timer += Random.Range(minSpawnTime, maxSpawnTime);
        }

        SpawnItem();
        return Random.Range(0, 5) == 0;
    }

    private void MaybeDespawn()
    {
        if (_despawnQueue.Count == 0) return;
        var (despawnTime, obj) = _despawnQueue.Peek();
        if (despawnTime > Time.time) return;
        _despawnQueue.Dequeue();
        GameObject.Destroy(obj);
    }

    private void SpawnItem()
    {
        var degree = Random.value;
        degree = Mathf.Pow((degree - 0.5f) * 2, 3);
        var offset = new Vector2(Mathf.Sin(degree * Mathf.PI), Mathf.Cos(degree * Mathf.PI)) * 6;
        var spawnPos = _playerTransform.position + (Vector3)offset;

        var randomRoll = Random.Range(0f, 1f);
        var prefabIndex = _appearChance.FindIndex(v => v >= randomRoll);
        if (prefabIndex < 0) prefabIndex = _prefabs.Count - 1;
        var prefab = _prefabs[prefabIndex];
        var velocity = CalculateVelocity(new Vector2(0, 1f) - offset * 1.2f, Physics2D.gravity.y, 0.4f);
        var torque = Random.Range(-40f, 40f) * Random.Range(0f, 1f);
        Logger.LogInfo($"Spawning {prefab.name} at {offset} ({degree}) with velocity {velocity} and torque {torque}");

        var spawned = Instantiate(prefab, spawnPos, Quaternion.identity);
        _despawnQueue.Enqueue((Time.time + 10f, spawned));

        var rigidbody = spawned.GetComponent<Rigidbody2D>();
        rigidbody.velocity = velocity;
        rigidbody.AddTorque(torque, ForceMode2D.Impulse);
    }


    public Vector2 CalculateVelocity(Vector2 displacement, float gravity, float time)
    {
        var velocityX = displacement.x / time;
        var velocityY = (displacement.y - 0.5f * gravity * Mathf.Pow(time, 2)) / time;
        return new Vector2(velocityX, velocityY);
    }
}