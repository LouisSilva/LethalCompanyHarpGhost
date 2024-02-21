using System.Diagnostics.CodeAnalysis;
using BepInEx.Logging;
using GameNetcodeStuff;
using LethalCompanyHarpGhost.Instruments;
using Unity.Netcode;
using UnityEngine;

namespace LethalCompanyHarpGhost.Ghost;

[SuppressMessage("ReSharper", "RedundantDefaultMemberInitializer")]
public class GhostAIClient : MonoBehaviour
{
    private ManualLogSource _mls;
    
    private NetworkObjectReference _harpObjectRef;

    private int _harpScrapValue;

    private InstrumentBehaviour _heldHarp;

    private PlayerControllerB _targetPlayer;
    
    public Transform grabTarget;

    public Transform eye;
    

    private void OnEnable()
    {
        GhostNetcodeController.OnDropHarp += HandleDropHarp;
        GhostNetcodeController.OnSpawnHarp += HandleSpawnHarp;
        GhostNetcodeController.OnGrabHarp += HandleGrabHarp;
        GhostNetcodeController.OnPlayHarpMusic += HandleOnPlayHarpMusic;
        GhostNetcodeController.OnStopHarpMusic += HandleOnStopHarpMusic;
        GhostNetcodeController.OnChangeTargetPlayer += HandleChangeTargetPlayer;
        GhostNetcodeController.OnDamageTargetPlayer += HandleDamageTargetPlayer;
        GhostNetcodeController.OnIncreaseTargetPlayerFearLevel += HandleIncreaseTargetPlayerFearLevel;
    }

    private void OnDestroy()
    {
        GhostNetcodeController.OnDropHarp -= HandleDropHarp;
        GhostNetcodeController.OnSpawnHarp -= HandleSpawnHarp;
        GhostNetcodeController.OnGrabHarp -= HandleGrabHarp;
        GhostNetcodeController.OnPlayHarpMusic -= HandleOnPlayHarpMusic;
        GhostNetcodeController.OnStopHarpMusic -= HandleOnStopHarpMusic;
        GhostNetcodeController.OnChangeTargetPlayer -= HandleChangeTargetPlayer;
        GhostNetcodeController.OnDamageTargetPlayer -= HandleDamageTargetPlayer;
        GhostNetcodeController.OnIncreaseTargetPlayerFearLevel -= HandleIncreaseTargetPlayerFearLevel;
    }

    private void Start()
    {
        _mls = BepInEx.Logging.Logger.CreateLogSource($"{HarpGhostPlugin.ModGuid} | Ghost AI | Client");
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls.LogInfo(msg);
        #endif
    }

    private void HandleIncreaseTargetPlayerFearLevel()
    {
        LogDebug("HandleIncreaseTargetPlayerFearLevel called");
        if (GameNetworkManager.Instance.localPlayerController != _targetPlayer) return; LogDebug("localplayercontroller is not target player");
        
        if (_targetPlayer == null)
        {
            _mls.LogError("Target player is null");
            return;
        }
        
        LogDebug($"Increasing fear level for {_targetPlayer.name}");
        if (_targetPlayer.HasLineOfSightToPosition(eye.position, 115f, 50, 3f))
        {
            LogDebug("Player to add fear to is looking at the ghost");
            _targetPlayer.JumpToFearLevel(1);
            _targetPlayer.IncreaseFearLevelOverTime(0.8f);;
        }
        
        else if (Vector3.Distance(eye.transform.position, _targetPlayer.transform.position) < 3)
        {
            LogDebug("Player to add fear to is not looking at the ghost, but is near");
            _targetPlayer.JumpToFearLevel(0.6f);
            _targetPlayer.IncreaseFearLevelOverTime(0.4f);;
        }
    }

    private void HandleOnPlayHarpMusic()
    {
        if (_heldHarp == null) return;
        _heldHarp.StartMusicServerRpc();
    }
    
    private void HandleOnStopHarpMusic()
    {
        if (_heldHarp == null) return;
        _heldHarp.StopMusicServerRpc();
    }

    private void HandleDropHarp(Vector3 dropPosition)
    {
        if (_heldHarp == null) return;
        _heldHarp.parentObject = null;
        _heldHarp.transform.SetParent(StartOfRound.Instance.propsContainer, true);
        _heldHarp.EnablePhysics(true);
        _heldHarp.fallTime = 0f;
        
        Transform parent;
        _heldHarp.startFallingPosition =
            (parent = _heldHarp.transform.parent).InverseTransformPoint(_heldHarp.transform.position);
        _heldHarp.targetFloorPosition = parent.InverseTransformPoint(dropPosition);
        _heldHarp.floorYRot = -1;
        _heldHarp.grabbable = true;
        _heldHarp.grabbableToEnemies = true;
        _heldHarp.isHeld = false;
        _heldHarp.isHeldByEnemy = false;
        _heldHarp.StopMusicServerRpc();
        _heldHarp = null;
    }

    private void HandleGrabHarp()
    {
        if (_heldHarp != null) return;
        if (!_harpObjectRef.TryGet(out NetworkObject networkObject)) return;
        _heldHarp = networkObject.gameObject.GetComponent<InstrumentBehaviour>();

        _heldHarp.SetScrapValue(_harpScrapValue);
        _heldHarp.parentObject = grabTarget;
        _heldHarp.isHeldByEnemy = true;
        _heldHarp.grabbableToEnemies = false;
        _heldHarp.grabbable = false;
    }
    
    private void HandleSpawnHarp(NetworkObjectReference harpObject, int harpScrapValue)
    {
        _harpObjectRef = harpObject;
        _harpScrapValue = harpScrapValue;
    }

    private void HandleChangeTargetPlayer(int targetPlayerObjectId)
    {
        if (targetPlayerObjectId == -69420)
        {
            _targetPlayer = null;
            return;
        }
        
        PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[targetPlayerObjectId];
        _targetPlayer = player;
        LogDebug($"Target player is now: {player.name}");
    }

    private void HandleDamageTargetPlayer(int damage, CauseOfDeath causeOfDeath = CauseOfDeath.Unknown)
    {
        _targetPlayer.DamagePlayer(damage, causeOfDeath: causeOfDeath);
    }
}