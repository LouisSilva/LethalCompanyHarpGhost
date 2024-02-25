﻿using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using LethalLib.Modules;
using Unity.Collections;
using Unity.Netcode;
using static LethalLib.Modules.Levels;
using static LethalLib.Modules.Enemies;
using static LethalLib.Modules.Items;
using NetworkPrefabs = LethalLib.Modules.NetworkPrefabs;

namespace LethalCompanyHarpGhost
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class HarpGhostPlugin : BaseUnityPlugin
    {
        public const string ModGuid = $"LCM_HarpGhost|{ModVersion}";
        private const string ModName = "Lethal Company Harp Ghost Mod";
        private const string ModVersion = "1.2.3";

        private readonly Harmony _harmony = new Harmony(ModGuid);
        
        private static readonly ManualLogSource _mls = BepInEx.Logging.Logger.CreateLogSource(ModGuid);

        private static HarpGhostPlugin _instance;
        
        public static HarpGhostConfig HarpGhostConfig { get; internal set; }

        private static EnemyType _harpGhostEnemyType;

        public static Item HarpItem;
        private static Item BagpipesItem;
        public static Item TubaItem;

        private void Awake()
        {
            if (_instance == null) _instance = this;

            Assets.PopulateAssets();
            if (Assets.MainAssetBundle == null)
            {
                _mls.LogError("MainAssetBundle is null");
                return;
            }
            
            _harmony.PatchAll();
            HarpGhostConfig = new HarpGhostConfig(Config);
            
            SetupHarpGhost();
            
            SetupHarp();
            SetupTuba();
            
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (Type type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (MethodInfo method in methods)
                {
                    object[] attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
            
            _mls.LogInfo($"Plugin {ModName} is loaded!");
        }

        private static void SetupHarpGhost()
        {
            _harpGhostEnemyType = Assets.MainAssetBundle.LoadAsset<EnemyType>("HarpGhost");
            _harpGhostEnemyType.canDie = HarpGhostConfig.Instance.GhostIsKillable.Value;
            _harpGhostEnemyType.PowerLevel = HarpGhostConfig.Instance.GhostPowerLevel.Value;
            _harpGhostEnemyType.canBeStunned = HarpGhostConfig.Instance.GhostIsStunnable.Value;
            _harpGhostEnemyType.MaxCount = HarpGhostConfig.Instance.MaxAmountOfGhosts.Value;
            _harpGhostEnemyType.stunTimeMultiplier = HarpGhostConfig.Instance.GhostStunTimeMultiplier.Value;
            _harpGhostEnemyType.stunGameDifficultyMultiplier = HarpGhostConfig.Instance.GhostStunGameDifficultyMultiplier.Value;
            _harpGhostEnemyType.canSeeThroughFog = HarpGhostConfig.Instance.GhostCanSeeThroughFog.Value;
            
            TerminalNode harpGhostTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("HarpGhostTN");
            TerminalKeyword harpGhostTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("HarpGhostTK");
            
            NetworkPrefabs.RegisterNetworkPrefab(_harpGhostEnemyType.enemyPrefab);
            Utilities.FixMixerGroups(_harpGhostEnemyType.enemyPrefab);
            RegisterEnemy(_harpGhostEnemyType, Mathf.Clamp(HarpGhostConfig.Instance.GhostSpawnRate.Value, 0, 999), HarpGhostConfig.Instance.GhostSpawnLevel.Value, SpawnType.Default, harpGhostTerminalNode, harpGhostTerminalKeyword);
            
            // RegisterEnemy(HarpGhostEnemyType, SpawnType.Default, new Dictionary<LevelTypes, int>{
            //     [LevelTypes.DineLevel] = Mathf.Clamp(HarpGhostConfig.Instance.GhostSpawnRate.Value, 0, 100), 
            //     [LevelTypes.RendLevel] = Mathf.Clamp(HarpGhostConfig.Instance.GhostSpawnRate.Value, 0, 100)}, 
            //     infoNode: harpGhostTerminalNode, infoKeyword:harpGhostTerminalKeyword);
        }

        private static void SetupHarp()
        {
            // string[] assetNames = Assets.MainAssetBundle.GetAllAssetNames();
            // foreach (string assetName in assetNames)
            // {
            //     mls.LogInfo("Asset in bundle: " + assetName);
            // }
            
            HarpItem = Assets.MainAssetBundle.LoadAsset<Item>("HarpItemData");
            if (HarpItem == null)
            {
                _mls.LogError("Failed to load HarpItemData from AssetBundle.");
                return;
            }
            
            NetworkPrefabs.RegisterNetworkPrefab(HarpItem.spawnPrefab);
            Utilities.FixMixerGroups(HarpItem.spawnPrefab);
            RegisterScrap(HarpItem, 0, LevelTypes.All);
        }

        private static void SetupBagpipes()
        {
            BagpipesItem = Assets.MainAssetBundle.LoadAsset<Item>("BagpipesItemData");
            if (BagpipesItem == null)
            {
                _mls.LogError("Failed to load BagpipesItemData from AssetBundle");
            }
            
            NetworkPrefabs.RegisterNetworkPrefab(BagpipesItem.spawnPrefab);
            Utilities.FixMixerGroups(BagpipesItem.spawnPrefab);
            RegisterScrap(BagpipesItem, 0, LevelTypes.All);
        }
        
        private static void SetupTuba()
        {
            TubaItem = Assets.MainAssetBundle.LoadAsset<Item>("TubaItemData");
            if (TubaItem == null)
            {
                _mls.LogError("Failed to load TubaItemData from AssetBundle");
            }
            
            NetworkPrefabs.RegisterNetworkPrefab(TubaItem.spawnPrefab);
            Utilities.FixMixerGroups(TubaItem.spawnPrefab);
            RegisterScrap(TubaItem, 0, LevelTypes.All);
        }
    }

    public class HarpGhostConfig : SyncedInstance<HarpGhostConfig>
    {
        public readonly ConfigEntry<int> GhostInitialHealth;
        public readonly ConfigEntry<int> GhostAttackDamage;
        public readonly ConfigEntry<float> GhostStunTimeMultiplier;
        public readonly ConfigEntry<float> GhostMaxDoorSpeedMultiplier;
        public readonly ConfigEntry<float> GhostStunGameDifficultyMultiplier;
        public readonly ConfigEntry<bool> GhostIsStunnable;
        public readonly ConfigEntry<bool> GhostIsKillable;
        public readonly ConfigEntry<bool> GhostCanSeeThroughFog;
        
        public readonly ConfigEntry<float> GhostAnnoyanceLevelDecayRate;
        public readonly ConfigEntry<float> GhostAnnoyanceThreshold;
        public readonly ConfigEntry<float> GhostMaxSearchRadius;

        public readonly ConfigEntry<float> GhostVoiceSfxVolume;
        public readonly ConfigEntry<float> InstrumentVolume;

        public readonly ConfigEntry<int> GhostSpawnRate;
        public readonly ConfigEntry<int> MaxAmountOfGhosts;
        public readonly ConfigEntry<int> GhostPowerLevel;
        public readonly ConfigEntry<LevelTypes> GhostSpawnLevel;
        
        public readonly ConfigEntry<int> HarpMinValue;
        public readonly ConfigEntry<int> HarpMaxValue;

        public HarpGhostConfig(ConfigFile cfg)
        {
            InitInstance(this);
            
            GhostInitialHealth = cfg.Bind(
                "General",
                "Health",
                3,
                "The health when spawned"
                );
            
            GhostIsKillable = cfg.Bind(
                "General",
                "Ghost Is Killable",
                true,
                "Whether a Harp Ghost can be killed or not"
            );
            
            GhostAttackDamage = cfg.Bind(
                "General",
                "Attack Damage",
                35,
                "The attack damage of the ghost"
            );
            
            GhostStunTimeMultiplier = cfg.Bind(
                "General",
                "Stun Time Multiplier",
                1f,
                "The multiplier for how long a Harp Ghost can be stunned"
            );
            
            GhostMaxDoorSpeedMultiplier = cfg.Bind(
                "General",
                "Max Door Speed Multiplier",
                6f,
                "The MAXIMUM multiplier for how long it takes a Harp Ghost to open a door (maximum because the value changes depending on how angry the ghost is, there is no global value)"
            );
            
            GhostIsStunnable= cfg.Bind(
                "General",
                "Ghost Is Stunnable",
                true,
                "Whether a Harp Ghost can be stunned or not"
            );
            
            GhostStunTimeMultiplier = cfg.Bind(
                "General",
                "Stun Time Multiplier",
                1f,
                "The multiplier for how long a Harp Ghost can be stunned"
            );
            
            GhostStunGameDifficultyMultiplier = cfg.Bind(
                "General",
                "Stun Game Difficulty Multiplier",
                0f,
                "Not sure what this does"
            );
            
            GhostCanSeeThroughFog = cfg.Bind(
                "General",
                "Ghost Can See Through Fog",
                false,
                "Whether a Harp Ghost can see through fog"
            );
            
            GhostAnnoyanceLevelDecayRate = cfg.Bind(
                "General",
                "Annoyance Level Decay Rate",
                0.3f,
                "The decay rate of the ghost's annoyance level (due to noises) over time"
            );
            
            GhostAnnoyanceThreshold = cfg.Bind(
                "General",
                "Annoyance Level Threshold",
                8f,
                "The threshold of how annoyed the ghost has to be (from noise) to get angry"
            );
            
            GhostMaxSearchRadius = cfg.Bind(
                "General",
                "Max Search Radius",
                100f,
                "The maximum distance the ghost will go to search for a player"
            );
            
            GhostVoiceSfxVolume = cfg.Bind(
                "Audio",
                "Voice Sound Effects Volume",
                1f,
                "The volume of the ghost's voice. Values are from 0-1"
            );

            InstrumentVolume = cfg.Bind(
                "Audio",
                "Instrument Volume",
                1f,
                "The volume of the music played from any instrument. Values are from 0-1"
            );
            
            GhostSpawnRate = cfg.Bind(
                "Spawn Values",
                "Harp Ghost Spawn Value",
                40,
                "The weighted spawn rarity of the harp ghost"
            );
            
            MaxAmountOfGhosts = cfg.Bind(
                "Spawn Values",
                "Max Amount of Harp Ghosts",
                2,
                "The maximum amount of harp ghosts that can spawn in a game"
            );
            
            GhostPowerLevel = cfg.Bind(
                "Spawn Values",
                "Ghost Power Level",
                1,
                "The power level of a Harp Ghost"
            );
            
            GhostSpawnLevel = cfg.Bind(
                "Spawn Values",
                "Ghost Spawn Level",
                LevelTypes.DineLevel,
                "The LevelTypes that the ghost spawns in. Only single values are accepted."
            );
            
            HarpMinValue = cfg.Bind(
                "Spawn Values",
                "Harp Minimum value",
                150,
                "The minimum value that the harp can be set to"
            );
            
            HarpMaxValue = cfg.Bind(
                "Spawn Values",
                "Harp Maximum value",
                300,
                "The maximum value that the harp can be set to"
            );
        }

        private static void RequestSync() {
            if (!IsClient) return;

            using FastBufferWriter stream = new(IntSize, Allocator.Temp);
            MessageManager.SendNamedMessage("ModName_OnRequestConfigSync", 0uL, stream);
        }

        private static void OnRequestSync(ulong clientId, FastBufferReader _) {
            if (!IsHost) return;

            Debug.Log($"Config sync request received from client: {clientId}");

            byte[] array = SerializeToBytes(Instance);
            int value = array.Length;

            using FastBufferWriter stream = new(value + IntSize, Allocator.Temp);

            try {
                stream.WriteValueSafe(in value, default);
                stream.WriteBytesSafe(array);

                MessageManager.SendNamedMessage("ModName_OnReceiveConfigSync", clientId, stream);
            } catch(Exception e) {
                Debug.Log($"Error occurred syncing config with client: {clientId}\n{e}");
            }
        }

        private static void OnReceiveSync(ulong _, FastBufferReader reader) {
            if (!reader.TryBeginRead(IntSize)) {
                Debug.LogError("Config sync error: Could not begin reading buffer.");
                return;
            }

            reader.ReadValueSafe(out int val, default);
            if (!reader.TryBeginRead(val)) {
                Debug.LogError("Config sync error: Host could not sync.");
                return;
            }

            byte[] data = new byte[val];
            reader.ReadBytesSafe(ref data, val);

            SyncInstance(data);

            Debug.Log("Successfully synced config with host.");
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
        public static void InitializeLocalPlayer() {
            if (IsHost) {
                MessageManager.RegisterNamedMessageHandler("HarpGhost_OnRequestConfigSync", OnRequestSync);
                Synced = true;

                return;
            }

            Synced = false;
            MessageManager.RegisterNamedMessageHandler("HarpGhost_OnReceiveConfigSync", OnReceiveSync);
            RequestSync();
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameNetworkManager), "StartDisconnect")]
        public static void PlayerLeave() {
            RevertSync();
        }
    }

    // Got from https://lethal.wiki/dev/intermediate/custom-config-syncing
    [Serializable]
    public class SyncedInstance<T>
    {
        internal static CustomMessagingManager MessageManager => NetworkManager.Singleton.CustomMessagingManager;
        internal static bool IsClient => NetworkManager.Singleton.IsClient;
        internal static bool IsHost => NetworkManager.Singleton.IsHost;
        
        [NonSerialized]
        protected static int IntSize = 4;

        public static T Default { get; private set; }
        public static T Instance { get; private set; }

        public static bool Synced { get; internal set; }

        protected void InitInstance(T instance) {
            Default = instance;
            Instance = instance;
            
            IntSize = sizeof(int);
        }

        internal static void SyncInstance(byte[] data) {
            Instance = DeserializeFromBytes(data);
            Synced = true;
        }

        internal static void RevertSync() {
            Instance = Default;
            Synced = false;
        }

        public static byte[] SerializeToBytes(T val) {
            BinaryFormatter bf = new();
            using MemoryStream stream = new();

            try {
                bf.Serialize(stream, val);
                return stream.ToArray();
            }
            catch (Exception e) {
                Debug.LogError($"Error serializing instance: {e}");
                return null;
            }
        }

        public static T DeserializeFromBytes(byte[] data) {
            BinaryFormatter bf = new();
            using MemoryStream stream = new(data);

            try {
                return (T) bf.Deserialize(stream);
            } catch (Exception e) {
                Debug.LogError($"Error deserializing instance: {e}");
                return default;
            }
        }
    }
    
    public static class Assets
    {
        private const string MainAssetBundleName = "Assets.harpghostbundle";
        public static AssetBundle MainAssetBundle = null;

        private static string GetAssemblyName() => Assembly.GetExecutingAssembly().FullName.Split(',')[0];
        public static void PopulateAssets()
        {
            if (MainAssetBundle != null) return;
            using Stream assetStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(GetAssemblyName() + "." + MainAssetBundleName);
            MainAssetBundle = AssetBundle.LoadFromStream(assetStream);
        }
    }
}