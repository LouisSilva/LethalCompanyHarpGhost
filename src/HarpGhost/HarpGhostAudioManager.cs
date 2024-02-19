﻿using BepInEx.Logging;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LethalCompanyHarpGhost.HarpGhost;

public class HarpGhostAudioManager : MonoBehaviour
{
    private readonly ManualLogSource _mls = BepInEx.Logging.Logger.CreateLogSource("Harp Ghost Audio Manager");
    
    #pragma warning disable 0649
    [SerializeField] private AudioSource creatureVoiceSource;
    [SerializeField] private AudioSource creatureSfxSource;
    #pragma warning restore 0649
    
    [Header("Audio")]
    [Space(5f)]
    public AudioClip[] damageSfx;
    public AudioClip[] laughSfx;
    public AudioClip[] stunSfx;
    public AudioClip[] upsetSfx;
    public AudioClip dieSfx;
    
    public enum AudioClipTypes
    {
        Death = 0,
        Damage = 1,
        Laugh = 2,
        Stun = 3,
        Upset = 4
    }
    
    public enum NoiseIds
    {
        PlayerFootstepsLocal = 6,
        PlayerFootstepsServer = 7,
        Lightning = 11,
        PlayersTalking = 75,
        Harp = 540,
        BaboonHawkCaw = 1105,
        ShipHorn = 14155,
        WhoopieCushionFart = 101158,
        DropSoundEffect = 941,
        Boombox = 5,
        ItemDropship = 94,
        DoubleWing = 911,
        RadarBoosterPing = 1015,
        Jetpack = 41,
        DocileLocustBees = 14152,
    }
    
    public enum NoiseIDToIgnore
    {
        Harp = 540,
        DoubleWing = 911,
        Lightning = 11,
        DocileLocustBees = 14152,
        BaboonHawkCaw = 1105
    }

    private void Start()
    {
        if (creatureSfxSource == null) _mls.LogError("creatureSfxSource is null");
        if (creatureVoiceSource == null) _mls.LogError("creatureVoiceSource is null");
        
        if (damageSfx == null || damageSfx.Length == 0) _mls.LogError("DamageSfx is null or empty");
        if (laughSfx == null || laughSfx.Length == 0) _mls.LogError("LaughSfx is null or empty");
        if (stunSfx == null || stunSfx.Length == 0) _mls.LogError("StunSfx is null or empty");
        if (upsetSfx == null || upsetSfx.Length == 0) _mls.LogError("UpsetSfx is null or empty");
        if (dieSfx == null) _mls.LogError("DieSfx is null");
        
        SubscribeToEvents();
    }
    
    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls.LogInfo(msg);
        #endif
    }

    private void SubscribeToEvents()
    {
        HarpGhostNetcodeController.OnPlayCreatureVoice += PlayVoice;
        HarpGhostNetcodeController.OnEnterDeathState += HandleOnEnterDeathState;
    }

    private void OnDestroy()
    {
        HarpGhostNetcodeController.OnPlayCreatureVoice -= PlayVoice;
        HarpGhostNetcodeController.OnEnterDeathState -= HandleOnEnterDeathState;
    }

    private void HandleOnEnterDeathState()
    {
        creatureVoiceSource.Stop(true);
        creatureSfxSource.Stop(true);
        PlayVoice((int)AudioClipTypes.Death, 1);
        Destroy(this);
    }

    private void PlayVoice(int typeIndex, int randomNum, float volume = 1f, bool interrupt = true)
    {
        creatureVoiceSource.pitch = Random.Range(0.8f, 1.1f);
        LogDebug($"Audio clip index: {typeIndex}, audio clip random number: {randomNum}");
        
        AudioClip audioClip = typeIndex switch
        {
            (int)AudioClipTypes.Death => dieSfx,
            (int)AudioClipTypes.Damage => damageSfx[randomNum],
            (int)AudioClipTypes.Laugh => laughSfx[randomNum],
            (int)AudioClipTypes.Stun => stunSfx[randomNum],
            (int)AudioClipTypes.Upset => upsetSfx[randomNum],
            _ => null
        };

        if (audioClip == null)
        {
            _mls.LogError($"Harp ghost voice audio clip index '{typeIndex}' and randomNum: '{randomNum}' is null");
            return;
        }
        
        LogDebug($"Playing audio clip: {audioClip.name}");
        if (interrupt) creatureVoiceSource.Stop(true);
        creatureVoiceSource.volume = 1f;
        creatureVoiceSource.PlayOneShot(audioClip);
        WalkieTalkie.TransmitOneShotAudio(creatureVoiceSource, audioClip, volume);
    }
    
    private void PlaySfx(AudioClip clip, float volume = 1f)
    {
        creatureSfxSource.volume = volume;
        creatureSfxSource.PlayOneShot(clip);
        WalkieTalkie.TransmitOneShotAudio(creatureSfxSource, clip, volume);
    }
}