using System;
using BepInEx.Logging;
using LethalCompanyHarpGhost.Instruments;
using Unity.Netcode;
using UnityEngine;

namespace LethalCompanyHarpGhost.HarpGhost;

public class HarpGhostNetcodeController : NetworkBehaviour
{
    #pragma warning disable 0649
    [SerializeField] private HarpGhostAIServer harpGhostAIServer;
    #pragma warning restore 0649

    private readonly ManualLogSource _mls = BepInEx.Logging.Logger.CreateLogSource("Harp Ghost Netcode Controller");
    
    public event Action<int> OnDoAnimation;
    public event Action<int, bool> OnChangeAnimationParameterBool;
    public static event Action<int, int, bool> OnPlayCreatureVoice;
    public static event Action OnEnterDeathState;
    public static event Action OnGrabHarp;
    public static event Action<NetworkObjectReference, int> OnSpawnHarp;
    public static event Action<Vector3> OnDropHarp;
    public static event Action OnPlayHarpMusic;
    public static event Action OnStopHarpMusic;
    public static event Action<int> OnChangeTargetPlayer;
    public static event Action<int, CauseOfDeath> OnDamageTargetPlayer;
    public static event Action<float, float> OnChangeAgentMaxSpeed;
    public static event Action OnFixAgentSpeedAfterAttack;
    public static event Action OnIncreaseTargetPlayerFearLevel;
    public static event Action OnInitializeConfigValues;

    private void Start()
    {
        harpGhostAIServer = GetComponent<HarpGhostAIServer>();
        if (harpGhostAIServer == null) _mls.LogError("harpGhostAI is null");
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls.LogInfo(msg);
        #endif
    }

    [ServerRpc(RequireOwnership = false)]
    public void FixAgentSpeedAfterAttackServerRpc()
    {
        OnFixAgentSpeedAfterAttack?.Invoke();
    }

    [ServerRpc(RequireOwnership = false)]
    public void ChangeAgentMaxSpeedServerRpc(float newMaxSpeed, float newMaxSpeed2)
    {
        OnChangeAgentMaxSpeed?.Invoke(newMaxSpeed, newMaxSpeed2);
    }

    [ServerRpc(RequireOwnership = false)]
    public void DamageTargetPlayerServerRpc(int damage, CauseOfDeath causeOfDeath = CauseOfDeath.Unknown)
    {
        LogDebug("DamageTargetPlayerServerRpc called");
        DamageTargetPlayerClientRpc(damage, causeOfDeath);
    }

    [ClientRpc]
    public void InitializeConfigValuesClientRpc()
    {
        OnInitializeConfigValues?.Invoke();
    }

    [ClientRpc]
    public void DamageTargetPlayerClientRpc(int damage, CauseOfDeath causeOfDeath = CauseOfDeath.Unknown)
    {
        LogDebug("DamageTargetPlayerClientRpc called");
        OnDamageTargetPlayer?.Invoke(damage, causeOfDeath);
    }

    [ClientRpc]
    public void ChangeTargetPlayerClientRpc(int playerClientId)
    {
        OnChangeTargetPlayer?.Invoke(playerClientId);
    }

    [ClientRpc]
    public void IncreaseTargetPlayerFearLevelClientRpc()
    {
        LogDebug("IncreaseTargetPlayerFearLevelClientRpc called");
        OnIncreaseTargetPlayerFearLevel?.Invoke();
    }

    [ClientRpc]
    public void EnterDeathStateClientRpc()
    {
        OnEnterDeathState?.Invoke();
    }

    [ClientRpc]
    public void ChangeAnimationParameterBoolClientRpc(int animationId, bool value)
    {
        OnChangeAnimationParameterBool?.Invoke(animationId, value);
    }

    [ClientRpc]
    public void DoAnimationClientRpc(int animationId)
    {
        OnDoAnimation?.Invoke(animationId);
    }

    [ClientRpc]
    public void GrabHarpClientRpc()
    {
        OnGrabHarp?.Invoke();
    }

    [ClientRpc]
    public void PlayHarpMusicClientRpc()
    {
        OnPlayHarpMusic?.Invoke();
    }
    
    [ClientRpc]
    public void StopHarpMusicClientRpc()
    {
        OnStopHarpMusic?.Invoke();
    }
    
    [ServerRpc]
    public void SpawnHarpServerRpc()
    {
        GameObject harpObject = Instantiate(
            HarpGhostPlugin.HarpItem.spawnPrefab,
            harpGhostAIServer.TransformPosition,
            Quaternion.identity,
            harpGhostAIServer.RoundManagerInstance.spawnedScrapContainer);
        
        AudioSource harpAudioSource = harpObject.GetComponent<AudioSource>();
        if (harpAudioSource == null) _mls.LogError("harpAudioSource is null");

        InstrumentBehaviour harpBehaviour = harpObject.GetComponent<InstrumentBehaviour>();
        if (harpBehaviour == null) _mls.LogError("harpBehaviour is null");

        int harpScrapValue = UnityEngine.Random.Range(HarpGhostConfig.Instance.HarpMinValue.Value, HarpGhostConfig.Instance.HarpMaxValue.Value + 1);
        harpObject.GetComponent<GrabbableObject>().fallTime = 0f;
        harpObject.GetComponent<GrabbableObject>().SetScrapValue(harpScrapValue);
        harpGhostAIServer.RoundManagerInstance.totalScrapValueInLevel += harpScrapValue;

        harpObject.GetComponent<NetworkObject>().Spawn();
        SpawnHarpClientRpc(harpObject, harpScrapValue);
    }

    [ClientRpc]
    private void SpawnHarpClientRpc(NetworkObjectReference harpObject, int harpScrapValue)
    {
        OnSpawnHarp?.Invoke(harpObject, harpScrapValue);
    }

    [ClientRpc]
    public void DropHarpClientRpc(Vector3 targetPosition)
    {
        OnDropHarp?.Invoke(targetPosition);
    }
    
    [ClientRpc]
    public void PlayCreatureVoiceClientRpc(int typeIndex, int clipArrayLength, bool interrupt = true)
    {
        int randomNum = UnityEngine.Random.Range(0, clipArrayLength);
        LogDebug($"Invoking OnPlayCreatureVoice | Audio clip index: {typeIndex}, audio clip random number: {randomNum}");
        OnPlayCreatureVoice?.Invoke(typeIndex, randomNum, interrupt);
    }
}