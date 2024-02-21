using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;

namespace LethalCompanyHarpGhost.Instruments;

public class HarpBehaviour : PhysicsProp
{
    private ManualLogSource _mls;

    public AudioSource harpAudioSource;
    public List<AudioClip> harpAudioClips;
    
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

    [SerializeField] private ItemOffset playerHarpOffset;
    [SerializeField] private ItemOffset enemyHarpOffset;

    public void Awake()
    {
        _mls = BepInEx.Logging.Logger.CreateLogSource("Harp Behaviour");
        playerHarpOffset = new ItemOffset(positionOffset:new Vector3(-0.8f, 0.22f, 0.07f), rotationOffset:new Vector3(3, 12, -100));
        enemyHarpOffset = new ItemOffset(positionOffset:new Vector3(0, -0.6f, 0.6f));
        _isPlayingMusic = false;
    }
    
    public override void Start()
    {
        base.Start();
        
        _roundManager = FindObjectOfType<RoundManager>();
        UnityEngine.Random.InitState(FindObjectOfType<StartOfRound>().randomMapSeed - 10);
        
        if (harpAudioSource == null)
        {
            _mls.LogError("harpAudioSource is null!");
            return;
        }

        // ReSharper disable once InvertIf
        if (harpAudioClips == null || harpAudioClips.Count == 0)
        {
            _mls.LogError("harpAudioClips is null or empty!");
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
            UpdateItemOffsetsServerRpc(enemyHarpOffset);
        }
        else if (heldByPlayerOnServer)
        {
            LogDebug("heldByPlayerOnServer");
            UpdateItemOffsetsServerRpc(playerHarpOffset);
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
        HarpGhostPlugin.mls.LogInfo(logMessage);
        #endif
    }

    public override void ItemActivate(bool used, bool buttonDown = true)
    {
        base.ItemActivate(used, buttonDown);
        LogDebug("Harp ItemActivate() called");
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
        harpAudioSource.clip = harpAudioClips[UnityEngine.Random.Range(0, harpAudioClips.Count)];
        harpAudioSource.pitch = 1f;
        harpAudioSource.volume = Mathf.Clamp(HarpGhostConfig.Default.InstrumentMusicVolume.Value, 0f, 1f);
        harpAudioSource.Play();
        WalkieTalkie.TransmitOneShotAudio(harpAudioSource, harpAudioSource.clip, harpAudioSource.volume);
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
            harpAudioSource.pitch -= 0.033f;
            if (harpAudioSource.pitch <= 0.0) break;
        }
        harpAudioSource.Stop();
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
        LogDebug($"Owner: {this.OwnerClientId}");
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