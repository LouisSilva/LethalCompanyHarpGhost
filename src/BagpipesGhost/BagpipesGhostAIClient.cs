﻿using BepInEx.Logging;
using LethalCompanyHarpGhost.Items;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.VFX;

namespace LethalCompanyHarpGhost.BagpipesGhost;

public class BagpipesGhostAIClient : MonoBehaviour
{
    private ManualLogSource _mls;
    private string _ghostId;
    
    private NetworkObjectReference _instrumentObjectRef;
    private int _instrumentScrapValue;
    private InstrumentBehaviour _heldInstrument;
    
    public enum AudioClipTypes
    {
        Death = 0,
        Damage = 1,
        Laugh = 2,
        Stun = 3,
        Upset = 4,
        LongLaugh = 5,
        Shocked = 6,
        Taunt = 7,
    }
    
    public AudioClip[] longLaughSfx;
    public AudioClip[] shockedSfx;
    public AudioClip[] tauntSfx;
    public AudioClip[] damageSfx;
    public AudioClip[] laughSfx;
    public AudioClip[] stunSfx;
    public AudioClip[] upsetSfx;
    public AudioClip dieSfx;
    public AudioClip tornadoTeleportSfx;
    
    public static readonly int IsRunning = Animator.StringToHash("isRunning");
    public static readonly int IsStunned = Animator.StringToHash("isStunned");
    public static readonly int IsDead = Animator.StringToHash("isDead");
    public static readonly int Death = Animator.StringToHash("death");
    public static readonly int Stunned = Animator.StringToHash("stunned");
    public static readonly int Recover = Animator.StringToHash("recover");
    public static readonly int Attack = Animator.StringToHash("attack");
    
    #pragma warning disable 0649
    [Header("Audio")] [Space(5f)]
    [SerializeField] private AudioSource creatureVoiceSource;
    [SerializeField] private AudioSource creatureSfxSource;
    
    [Header("Visual Effects")] [Space(5f)]
    [SerializeField] private VisualEffect teleportVfx;
    [SerializeField] private Animator animator;
    
    [Header("Controllers")] [Space(5f)]
    [SerializeField] private Transform grabTarget;
    [SerializeField] private SkinnedMeshRenderer renderer;
    [SerializeField] private BagpipesGhostNetcodeController netcodeController;
    #pragma warning restore 0649
    

    private void OnEnable()
    {
        netcodeController.OnDropBagpipes += HandleDropInstrument;
        netcodeController.OnSpawnBagpipes += HandleSpawnInstrument;
        netcodeController.OnGrabBagpipes += HandleGrabInstrument;
        netcodeController.OnPlayBagpipesMusic += HandleOnPlayInstrumentMusic;
        netcodeController.OnStopBagpipesMusic += HandleOnStopInstrumentMusic;
        netcodeController.OnUpdateGhostIdentifier += HandleUpdateGhostIdentifier;
        netcodeController.OnDestroyBagpipes += HandleDestroyBagpipes;
        netcodeController.OnSetMeshEnabled += HandleSetMeshEnabled;
        
        netcodeController.OnInitializeConfigValues += HandleOnInitializeConfigValues;
        netcodeController.OnPlayCreatureVoice += PlayVoice;
        netcodeController.OnEnterDeathState += HandleOnEnterDeathState;
        netcodeController.OnPlayTeleportVfx += HandlePlayTeleportVfx;
        
        netcodeController.OnDoAnimation += SetTrigger;
        netcodeController.OnChangeAnimationParameterBool += SetBool;
    }

    private void OnDestroy()
    {
        if (netcodeController == null) return;
        netcodeController.OnDropBagpipes -= HandleDropInstrument;
        netcodeController.OnSpawnBagpipes -= HandleSpawnInstrument;
        netcodeController.OnGrabBagpipes -= HandleGrabInstrument;
        netcodeController.OnPlayBagpipesMusic -= HandleOnPlayInstrumentMusic;
        netcodeController.OnStopBagpipesMusic -= HandleOnStopInstrumentMusic;
        netcodeController.OnUpdateGhostIdentifier -= HandleUpdateGhostIdentifier;
        netcodeController.OnDestroyBagpipes -= HandleDestroyBagpipes;
        netcodeController.OnSetMeshEnabled -= HandleSetMeshEnabled;
        
        netcodeController.OnInitializeConfigValues -= HandleOnInitializeConfigValues;
        netcodeController.OnPlayCreatureVoice -= PlayVoice;
        netcodeController.OnEnterDeathState -= HandleOnEnterDeathState;
        netcodeController.OnPlayTeleportVfx -= HandlePlayTeleportVfx;
        
        netcodeController.OnDoAnimation -= SetTrigger;
        netcodeController.OnChangeAnimationParameterBool -= SetBool;
    }

    private void Start()
    {
        _mls = BepInEx.Logging.Logger.CreateLogSource($"{HarpGhostPlugin.ModGuid} | Bagpipes Ghost AI {_ghostId} | Client");
        renderer.enabled = true;
    }
    
    private void HandlePlayTeleportVfx(string recievedGhostId)
    {
        if (_ghostId != recievedGhostId) return;
        teleportVfx.SendEvent("OnPlayTeleport");
        PlaySfx(tornadoTeleportSfx);
    }

    private void HandleDestroyBagpipes(string recievedGhostId)
    {
        if (_ghostId != recievedGhostId) return;
        if (_heldInstrument != null) Destroy(_heldInstrument.gameObject);
        _heldInstrument = null;
    }

    private void HandleOnPlayInstrumentMusic(string recievedGhostId)
    {
        if (_ghostId != recievedGhostId) return;
        if (_heldInstrument == null) return;
        _heldInstrument.StartMusicServerRpc();
    }
    
    private void HandleOnStopInstrumentMusic(string recievedGhostId)
    {
        if (_ghostId != recievedGhostId) return;
        if (_heldInstrument == null) return;
        _heldInstrument.StopMusicServerRpc();
    }

    private void HandleDropInstrument(string recievedGhostId, Vector3 dropPosition)
    {
        if (_ghostId != recievedGhostId) return;
        if (_heldInstrument == null) return;
        _heldInstrument.parentObject = null;
        _heldInstrument.transform.SetParent(StartOfRound.Instance.propsContainer, true);
        _heldInstrument.EnablePhysics(true);
        _heldInstrument.fallTime = 0f;
        
        Transform parent;
        _heldInstrument.startFallingPosition =
            (parent = _heldInstrument.transform.parent).InverseTransformPoint(_heldInstrument.transform.position);
        _heldInstrument.targetFloorPosition = parent.InverseTransformPoint(dropPosition);
        _heldInstrument.floorYRot = -1;
        _heldInstrument.grabbable = true;
        _heldInstrument.grabbableToEnemies = true;
        _heldInstrument.isHeld = false;
        _heldInstrument.isHeldByEnemy = false;
        _heldInstrument.StopMusicServerRpc();
        _heldInstrument = null;
    }

    private void HandleGrabInstrument(string recievedGhostId)
    {
        if (_ghostId != recievedGhostId) return;
        if (_heldInstrument != null) return;
        if (!_instrumentObjectRef.TryGet(out NetworkObject networkObject)) return;
        _heldInstrument = networkObject.gameObject.GetComponent<InstrumentBehaviour>();

        _heldInstrument.SetScrapValue(_instrumentScrapValue);
        _heldInstrument.parentObject = grabTarget;
        _heldInstrument.isHeldByEnemy = true;
        _heldInstrument.grabbableToEnemies = false;
        _heldInstrument.grabbable = false;
    }
    
    private void HandleSpawnInstrument(string recievedGhostId, NetworkObjectReference instrumentObject, int instrumentScrapValue)
    {
        if (_ghostId != recievedGhostId) return;
        _instrumentObjectRef = instrumentObject;
        _instrumentScrapValue = instrumentScrapValue;
    }

    private void HandleSetMeshEnabled(string recievedGhostId, bool meshEnabled)
    {
        if (_ghostId != recievedGhostId) return;
        renderer.enabled = meshEnabled;
    }
    
    private void PlayVoice(string recievedGhostId, int typeIndex, int randomNum, bool interrupt = true)
    {
        if (_ghostId != recievedGhostId) return;
        creatureVoiceSource.pitch = Random.Range(0.8f, 1.1f);
        
        AudioClip audioClip = typeIndex switch
        {
            (int)AudioClipTypes.Death => dieSfx,
            (int)AudioClipTypes.Damage => damageSfx[randomNum],
            (int)AudioClipTypes.Laugh => laughSfx[randomNum],
            (int)AudioClipTypes.Stun => stunSfx[randomNum],
            (int)AudioClipTypes.Upset => upsetSfx[randomNum],
            (int)AudioClipTypes.LongLaugh => longLaughSfx[randomNum],
            (int)AudioClipTypes.Shocked => shockedSfx[randomNum],
            (int)AudioClipTypes.Taunt => tauntSfx[randomNum],
            _ => null
        };

        if (audioClip == null)
        {
            _mls.LogError($"Bagpipes ghost voice audio clip index '{typeIndex}' and randomNum: '{randomNum}' is null");
            return;
        }
        
        LogDebug($"Playing audio clip: {audioClip.name}");
        if (interrupt) creatureVoiceSource.Stop(true);
        creatureVoiceSource.PlayOneShot(audioClip);
        WalkieTalkie.TransmitOneShotAudio(creatureVoiceSource, audioClip, creatureVoiceSource.volume);
    }
    
    private void PlaySfx(AudioClip clip, float volume = 1f)
    {
        creatureSfxSource.volume = volume;
        creatureSfxSource.PlayOneShot(clip);
        WalkieTalkie.TransmitOneShotAudio(creatureSfxSource, clip, volume);
    }
    
    private void SetBool(string recievedGhostId, int parameter, bool value)
    {
        if (_ghostId != recievedGhostId) return;
        animator.SetBool(parameter, value);
    }

    public bool GetBool(int parameter)
    {
        return animator.GetBool(parameter);
    }

    private void SetTrigger(string recievedGhostId, int parameter)
    {
        if (_ghostId != recievedGhostId) return;
        animator.SetTrigger(parameter);
    }
    
    private void HandleOnEnterDeathState(string recievedGhostId)
    {
        if (_ghostId != recievedGhostId) return;
        SetTrigger(_ghostId, Death);
        SetBool(_ghostId, IsDead, true);
        SetBool(_ghostId, IsRunning, false);
        SetBool(_ghostId, IsStunned, false);
        creatureVoiceSource.Stop(true);
        creatureSfxSource.Stop(true);
        PlayVoice(_ghostId, (int)AudioClipTypes.Death, 1);
        Destroy(this);
    }
    
    private void HandleOnInitializeConfigValues(string recievedGhostId)
    {
        if (_ghostId != recievedGhostId) return;
        creatureVoiceSource.volume = BagpipeGhostConfig.Default.BagpipeGhostVoiceSfxVolume.Value;
    }
    
    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls?.LogInfo(msg);
        #endif
    }

    private void HandleUpdateGhostIdentifier(string recievedGhostId)
    {
        _ghostId = recievedGhostId;
    }
}