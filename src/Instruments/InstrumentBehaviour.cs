using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;

namespace LethalCompanyHarpGhost.Instruments;

public class InstrumentBehaviour : PhysicsProp
{
    private ManualLogSource _mls;

    [Header("Audio")]
    [Space(3f)]
    public AudioSource instrumentAudioSource;
    public List<AudioClip> instrumentsAudioClips;

    [Header("Other")] [Space(3f)] [SerializeField]
    private string instrumentName;
    
    private RoundManager _roundManager;
    
    private int _timesPlayedWithoutTurningOff;
    
    private float _noiseInterval;
    
    private bool _isPlayingMusic;
    
    [Serializable]
    public struct ItemOffset : INetworkSerializable
    {
        public Vector3 positionOffset = default;
        public Vector3 rotationOffset = default;
        public Vector3 restingRotation = default;

        public ItemOffset(Vector3 positionOffset = default, Vector3 rotationOffset = default, Vector3 restingRotation = default)
        {
            this.positionOffset = positionOffset;
            this.rotationOffset = rotationOffset;
            this.restingRotation = restingRotation;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref positionOffset);
            serializer.SerializeValue(ref rotationOffset);
            serializer.SerializeValue(ref restingRotation);
        }
    }

    [SerializeField] private ItemOffset playerInstrumentOffset;
    [SerializeField] private ItemOffset enemyInstrumentOffset;

    public void Awake()
    {
        _mls = BepInEx.Logging.Logger.CreateLogSource($"{HarpGhostPlugin.ModGuid} | {instrumentName}");
        playerInstrumentOffset = new ItemOffset(positionOffset:new Vector3(-0.8f, 0.22f, 0.07f), rotationOffset:new Vector3(3, 12, -100));
        enemyInstrumentOffset = new ItemOffset(positionOffset:new Vector3(0, -0.6f, 0.6f));
        _isPlayingMusic = false;
    }
    
    public override void Start()
    {
        base.Start();
        
        _roundManager = FindObjectOfType<RoundManager>();
        UnityEngine.Random.InitState(FindObjectOfType<StartOfRound>().randomMapSeed - 10);
        
        if (instrumentAudioSource == null)
        {
            _mls.LogError($"{instrumentName} audio source is null!");
            return;
        }

        // ReSharper disable once InvertIf
        if (instrumentAudioSource == null || instrumentsAudioClips.Count == 0)
        {
            _mls.LogError($"{instrumentName} audio clip array is null or empty!");
            return;
        }
    }

    public override void Update()
    {
        base.Update();
        if (!IsOwner) return;

        if (isHeldByEnemy)
        {
            LogDebug("isHeldByEnemy");
            UpdateItemOffsetsServerRpc(enemyInstrumentOffset);
        }
        else if (heldByPlayerOnServer)
        {
            LogDebug("heldByPlayerOnServer");
            UpdateItemOffsetsServerRpc(playerInstrumentOffset);
        }
        
        if (!_isPlayingMusic) return;
        if (_noiseInterval <= 0.0)
        {
            _noiseInterval = 1f;
            ++_timesPlayedWithoutTurningOff;
            _roundManager.PlayAudibleNoise(transform.position, 16f, 3f, _timesPlayedWithoutTurningOff, noiseID: 540);
        }

        else _noiseInterval -= Time.deltaTime;
    }
    
    private void LogDebug(string logMessage)
    {
        #if DEBUG
        _mls.LogInfo(logMessage);
        #endif
    }

    public override void ItemActivate(bool used, bool buttonDown = true)
    {
        base.ItemActivate(used, buttonDown);
        LogDebug($"{instrumentName} ItemActivate() called");
        switch (_isPlayingMusic)
        {
            case false:
                StartMusicServerRpc();
                break;
            
            case true:
                StopMusicServerRpc();
                break;
        }

        isBeingUsed = used;
    }

    private void StartMusic()
    {
        instrumentAudioSource.clip = instrumentsAudioClips[UnityEngine.Random.Range(0, instrumentsAudioClips.Count)];
        instrumentAudioSource.pitch = 1f;
        instrumentAudioSource.volume = Mathf.Clamp(HarpGhostConfig.Default.InstrumentMusicVolume.Value, 0f, 1f);
        instrumentAudioSource.Play();
        WalkieTalkie.TransmitOneShotAudio(instrumentAudioSource, instrumentAudioSource.clip, instrumentAudioSource.volume);
        _isPlayingMusic = true;
    }

    private void StopMusic()
    {
        StartCoroutine(MusicPitchDown());
        _timesPlayedWithoutTurningOff = 0;
        _isPlayingMusic = false;
    }
    
    private IEnumerator MusicPitchDown()
    {
        for (int i = 0; i < 30; ++i)
        {
            yield return null;
            instrumentAudioSource.pitch -= 0.033f;
            if (instrumentAudioSource.pitch <= 0.0) break;
        }
        instrumentAudioSource.Stop();
    }

    public override void PocketItem()
    {
        base.PocketItem();
        StopMusicServerRpc();
    }

    public override void OnHitGround()
    {
        base.OnHitGround();
        StopMusicServerRpc();
    }

    public override void GrabItemFromEnemy(EnemyAI enemy)
    {
        base.GrabItemFromEnemy(enemy);
        isHeldByEnemy = true;
    }

    public override void DiscardItemFromEnemy()
    {
        base.DiscardItemFromEnemy();
        isHeldByEnemy = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void UpdateItemOffsetsServerRpc(ItemOffset itemOffset)
    {
        if (itemProperties.positionOffset == itemOffset.positionOffset &&
            itemProperties.rotationOffset == itemOffset.rotationOffset &&
            itemProperties.restingRotation == itemOffset.restingRotation) return;
        UpdateItemOffsetsClientRpc(itemOffset);
    }

    [ClientRpc]
    public void UpdateItemOffsetsClientRpc(ItemOffset itemOffset)
    {
        itemProperties.positionOffset = itemOffset.positionOffset;
        itemProperties.rotationOffset = itemOffset.rotationOffset;
        itemProperties.restingRotation = itemOffset.restingRotation;
    }

    [ServerRpc(RequireOwnership = false)]
    public void StopMusicServerRpc()
    {
        if (!_isPlayingMusic) return;
        StopMusicClientRpc();
    }
    
    [ClientRpc]
    public void StopMusicClientRpc()
    {
        StopMusic();
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void StartMusicServerRpc()
    {
        if (_isPlayingMusic) return;
        StartMusicClientRpc();
    }

    [ClientRpc]
    public void StartMusicClientRpc()
    {
        StartMusic();
    }
}