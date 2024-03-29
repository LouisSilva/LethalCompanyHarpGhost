﻿using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using BepInEx.Logging;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// TODO: Add "Fat Boi" config which allows people to make the enforcer ghost unable to fly across gaps because they are too fat

namespace LethalCompanyHarpGhost.EnforcerGhost;

[SuppressMessage("ReSharper", "RedundantDefaultMemberInitializer")]
public class EnforcerGhostAIServer : EnemyAI
{
    private ManualLogSource _mls;
    public string ghostId;

    [Header("AI and Pathfinding")] 
    [Space(5f)]
    public AISearchRoutine searchForPlayers;
    
    [SerializeField] private float agentMaxAcceleration = 50f;
    [SerializeField] private float agentMaxSpeed = 0.8f;
    [SerializeField] private float maxSearchRadius = 60f;
    private float _agentCurrentSpeed = 0f;
    private float _shootTimer = 0f;
    private float _realtimeStunTimer = 0f;
    private float _timeSinceHittingLocalPlayer = 0f;

    private bool _hasBegunInvestigating = false;
    private bool _inStunAnimation = false;
    private bool _isCurrentlyShooting = false;
    private bool _isReloading = false;
    private bool _hasStartedReloadAnimation = false;
    // private bool _canHearPlayers = false;

    private Vector3 _targetPosition = default;
    private Vector3 _agentLastPosition = default;

    private ShotgunItem _heldShotgun;
    private NetworkObjectReference _shotgunObjectRef;
    
    private RoundManager _roundManager;
    
    [Header("Controllers")]
    [Space(5f)]
    #pragma warning disable 0649
    [SerializeField] private EnforcerGhostNetcodeController netcodeController;
    [HideInInspector] public IEscortee Escortee { private get; set; }
    #pragma warning restore 0649

    private enum States
    {
        Escorting = 0,
        SearchingForPlayers = 1,
        InvestigatingTargetPosition = 2,
        ShootingTargetPlayer = 3,
        Dead = 4
    }

    public override void Start()
    {
        base.Start();
        if (!IsServer) return;
        
        ghostId = Guid.NewGuid().ToString();
        _mls = BepInEx.Logging.Logger.CreateLogSource($"{HarpGhostPlugin.ModGuid} | Enforcer Ghost AI {ghostId} | Server");
        
        netcodeController = GetComponent<EnforcerGhostNetcodeController>();
        if (netcodeController == null) _mls.LogError("Netcode Controller is null");
        
        agent = GetComponent<NavMeshAgent>();
        if (agent == null) _mls.LogError("NavMeshAgent component not found on " + name);
        agent.enabled = true;
        
        _roundManager = FindObjectOfType<RoundManager>();
        
        netcodeController.SyncGhostIdentifierClientRpc(ghostId);
        netcodeController.OnGrabShotgunPhaseTwo += HandleGrabShotgunPhaseTwo;
        netcodeController.OnSpawnShotgun += HandleSpawnShotgun;
        
        UnityEngine.Random.InitState(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
        InitializeConfigValues();
        netcodeController.ChangeAnimationParameterBoolClientRpc(ghostId, EnforcerGhostAIClient.IsDead, false);
        netcodeController.ChangeAnimationParameterBoolClientRpc(ghostId, EnforcerGhostAIClient.IsStunned, false);
        netcodeController.ChangeAnimationParameterBoolClientRpc(ghostId, EnforcerGhostAIClient.IsRunning, false);
        netcodeController.ChangeAnimationParameterBoolClientRpc(ghostId, EnforcerGhostAIClient.IsHoldingShotgun, false);
        
        netcodeController.SpawnShotgunServerRpc(ghostId);
        netcodeController.GrabShotgunClientRpc(ghostId);
        
        _mls.LogInfo("Enforcer Ghost Spawned");
    }
    
    private void FixedUpdate()
    {
        if (!IsServer) return;
        Vector3 position = transform.position;
        _agentCurrentSpeed = Mathf.Lerp(_agentCurrentSpeed, (position - _agentLastPosition).magnitude / Time.deltaTime, 0.75f);
        _agentLastPosition = position;
    }

    public override void Update()
    {
        base.Update();
        if (!IsServer) return;
        
        CalculateAgentSpeed();
        
        _realtimeStunTimer -= Time.deltaTime;
        _shootTimer += Time.deltaTime;
        _timeSinceHittingLocalPlayer += Time.deltaTime;
        
        if (stunNormalizedTimer <= 0.0 && _inStunAnimation && !isEnemyDead)
        {
            netcodeController.DoAnimationClientRpc(ghostId, EnforcerGhostAIClient.Recover);
            _inStunAnimation = false;
            netcodeController.ChangeAnimationParameterBoolClientRpc(ghostId, EnforcerGhostAIClient.IsStunned, false);
        }
        
        if (StartOfRound.Instance.allPlayersDead)
        {
            netcodeController.ChangeAnimationParameterBoolClientRpc(ghostId, EnforcerGhostAIClient.IsRunning, false);
            return;
        }
    }

    public override void DoAIInterval()
    {
        base.DoAIInterval();
        if (!IsServer) return;

        // Reload shotgun if its empty
        if (currentBehaviourStateIndex != (int)States.Dead && _isReloading && !_hasStartedReloadAnimation)
        {
            _hasStartedReloadAnimation = true;
            StartCoroutine(ReloadShotgun());
        }

        switch (currentBehaviourStateIndex)
        {
            case (int)States.Escorting:
            {
                if (Escortee == null) SwitchBehaviourStateLocally((int)States.SearchingForPlayers);
                break;
            }
            
            case (int)States.SearchingForPlayers: // searching for player state
            {
                PlayerControllerB tempTargetPlayer = CheckLineOfSightForClosestPlayer(90f, 40, 2);
                if (tempTargetPlayer != null)
                {
                    SwitchBehaviourStateLocally((int)States.ShootingTargetPlayer);
                    break;
                }
                
                if (!searchForPlayers.inProgress)
                {
                    if (_targetPosition != default)
                    {
                        if (CheckForPath(_targetPosition))
                        {
                            searchForPlayers.searchWidth = 30f;
                            StartSearch(_targetPosition, searchForPlayers);
                            break;
                        }
                    }
                    
                    // If there is no target player last seen position, just search from where the ghost is currently at
                    searchForPlayers.searchWidth = 100f;
                    StartSearch(transform.position, searchForPlayers);
                    LogDebug("Started search");
                    break;
                }
                
                break;
            }
            
            case (int)States.InvestigatingTargetPosition: // investigating last seen player position state
            {
                if (searchForPlayers.inProgress) StopSearch(searchForPlayers);

                // Check for player in LOS
                PlayerControllerB tempTargetPlayer = CheckLineOfSightForClosestPlayer(90f, 40, 2);
                if (tempTargetPlayer != null)
                {
                    SwitchBehaviourStateLocally((int)States.ShootingTargetPlayer);
                    break;
                }
                
                // begin investigating if not already
                if (!_hasBegunInvestigating) 
                {
                    if (_targetPosition == default) SwitchBehaviourStateLocally((int)States.SearchingForPlayers);
                    else
                    {
                        if (!SetDestinationToPosition(_targetPosition, true))
                        {
                            SwitchBehaviourStateLocally((int)States.SearchingForPlayers);
                            break;
                        }
                        _hasBegunInvestigating = true;
                    }
                }

                // If player isnt in LOS and ghost has reached the player's last known position, then switch to state 1
                if (Vector3.Distance(transform.position, _targetPosition) <= 1)
                {
                    SwitchBehaviourStateLocally((int)States.SearchingForPlayers);
                    break;
                }
                
                break;
            }

            case (int)States.ShootingTargetPlayer:
            {
                if (searchForPlayers.inProgress) StopSearch(searchForPlayers);
                
                PlayerControllerB playerControllerB = CheckLineOfSightForClosestPlayer(90f, 40, 2);
                if (playerControllerB == null)
                {
                    SwitchBehaviourStateLocally((int)States.InvestigatingTargetPosition);
                    break;
                }
                
                BeginChasingPlayer((int)playerControllerB.playerClientId);
                
                // _targetPosition is the last seen position of a player before they went out of view
                _targetPosition = targetPlayer.transform.position;
                netcodeController.IncreaseTargetPlayerFearLevelClientRpc(ghostId);
                
                AimAtPosition(targetPlayer.transform.position);
                
                // Check the distance between the enforcer ghost and the target player, if they are close, then stop moving
                if (Vector3.Distance(transform.position, targetPlayer.transform.position) <= 5f)
                {
                    movingTowardsTargetPlayer = false;
                    agentMaxSpeed = 0f;
                }
                else
                {
                    agentMaxSpeed = 1f;
                    movingTowardsTargetPlayer = true;
                }

                if (!_inStunAnimation && !_isCurrentlyShooting && !_isReloading && _shootTimer > 2f)
                {
                    StartCoroutine(ShootTargetPlayer());
                }
                    
                break;
            }

            case (int)States.Dead:
            {
                if (searchForPlayers.inProgress) StopSearch(searchForPlayers);

                break;
            }
        }
    }

    private IEnumerator ReloadShotgun()
    {
        if (!IsServer) yield break;
        _isReloading = true;
        _hasStartedReloadAnimation = true;
        _isCurrentlyShooting = false;
        float previousSpeed = agentMaxSpeed;
        agentMaxSpeed = 0f;
        
        LogDebug("In reload coroutine");
        _heldShotgun.shellsLoaded = 2;
        netcodeController.UpdateShotgunShellsLoadedClientRpc(ghostId, 2);
        netcodeController.DoAnimationClientRpc(ghostId, EnforcerGhostAIClient.ReloadShotgun);
        
         yield return new WaitForSeconds(0.3f);
        agentMaxSpeed = previousSpeed;
        yield return new WaitForSeconds(0.5f);
        _isReloading = false;
        _hasStartedReloadAnimation = false;
    }

    private IEnumerator ShootTargetPlayer()
    {
        if (!IsServer) yield break;
        
        _isCurrentlyShooting = true;
        while (_isCurrentlyShooting && currentBehaviourStateIndex == (int)States.ShootingTargetPlayer)
        {
            if (_realtimeStunTimer > 0)
            {
                yield return new WaitForSeconds(_realtimeStunTimer);
                continue;
            }

            if (_heldShotgun.shellsLoaded <= 0)
            {
                LogDebug("The shotgun has no more bullets! Reloading!");
                _isReloading = true;
                _hasStartedReloadAnimation = false;
                _isCurrentlyShooting = false;
                yield break;
            }
            
            PlayerControllerB playerInLos = CheckLineOfSightForPlayer(90, 40);
            if (playerInLos != null)
            {
                if (playerInLos != targetPlayer)
                {
                    netcodeController.ChangeTargetPlayerClientRpc(ghostId, (int)playerInLos.playerClientId);
                    targetPlayer = playerInLos;
                }
            } else yield return new WaitForSeconds(0.2f);
            
            // Check if the enforcer ghost is aiming at the player
            Vector3 directionToPlayer = targetPlayer.transform.position - _heldShotgun.transform.position;
            directionToPlayer.Normalize();
            float dotProduct = Vector3.Dot(_heldShotgun.transform.forward, directionToPlayer);
            float distanceToPlayer = Vector3.Distance(_heldShotgun.transform.position, targetPlayer.transform.position);
            
            float accuracyThreshold = 0.875f;
            if (distanceToPlayer < 3f)
                accuracyThreshold = 0.7f;

            if (dotProduct > accuracyThreshold)
            {
                StartCoroutine(ShootShotgun());
                yield break;
            }
            
            yield return new WaitForSeconds(0.2f);
        }
    }

    private IEnumerator ShootShotgun()
    {
        if (!IsServer) yield break;
        LogDebug("In the shoot shotgun method");
        netcodeController.ShootGunClientRpc(ghostId);
        _shootTimer = 0;
        
        yield return new WaitForSeconds(0.3f);
        _isCurrentlyShooting = false;
    }

    private void AimAtPosition(Vector3 position)
    {
        Vector3 direction = (position - transform.position).normalized;
        Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
    }

    // Called by the escortee ghost to tell the enforcer to switch to behaviour state 1 and stop escorting
    public void EnterRambo()
    {
        if (!IsServer) return;
        if (currentBehaviourStateIndex != (int)States.Dead && currentBehaviourStateIndex != (int)States.SearchingForPlayers)
        {
            SwitchBehaviourStateLocally((int)States.SearchingForPlayers);
        }
    }

    private void BeginChasingPlayer(int targetPlayerObjectId)
    {
        if (!IsServer) return;
        netcodeController.ChangeTargetPlayerClientRpc(ghostId, targetPlayerObjectId);
        PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[targetPlayerObjectId];
        SetMovingTowardsTargetPlayer(player);
    }

    private void SwitchBehaviourStateLocally(int state)
    {
        if (!IsServer) return;
        switch (state)
        {
            case (int)States.Escorting:
            {
                LogDebug($"Switched to behaviour state {(int)States.Escorting}!");
                
                agentMaxSpeed = 0.5f;
                agentMaxAcceleration = 50f;
                movingTowardsTargetPlayer = false;
                openDoorSpeedMultiplier = 4f;
                _isCurrentlyShooting = false;
                _shootTimer = 0f;
                _hasBegunInvestigating = false;
                _targetPosition = default;

                break;
            }

            case (int)States.SearchingForPlayers:
            {
                LogDebug($"Switched to behaviour state {(int)States.SearchingForPlayers}!");

                agentMaxSpeed = 2f;
                agentMaxAcceleration = 15f;
                openDoorSpeedMultiplier = 1f;
                _isCurrentlyShooting = false;
                movingTowardsTargetPlayer = false;
                _hasBegunInvestigating = false;
                _targetPosition = default;
                
                Escortee?.EscorteeBreakoff();

                break;
            }
            
            case (int)States.InvestigatingTargetPosition:
            {
                LogDebug($"Switched to behaviour state {(int)States.InvestigatingTargetPosition}!");

                agentMaxSpeed = 2f;
                agentMaxAcceleration = 15f;
                openDoorSpeedMultiplier = 1f;
                _isCurrentlyShooting = false;
                moveTowardsDestination = true;
                movingTowardsTargetPlayer = false;
                _hasBegunInvestigating = false;

                Escortee?.EscorteeBreakoff();
                
                break;
            }
            
            case (int)States.ShootingTargetPlayer:
            {
                LogDebug($"Switched to behaviour state {(int)States.ShootingTargetPlayer}!");

                agentMaxSpeed = 1f;
                agentMaxAcceleration = 15f;
                openDoorSpeedMultiplier = 1f;
                _isCurrentlyShooting = false;
                movingTowardsTargetPlayer = true;
                _hasBegunInvestigating = false;
                _targetPosition = default;

                Escortee?.EscorteeBreakoff();
                
                break;
            }

            case (int)States.Dead:
            {
                LogDebug($"Switched to behaviour state {(int)States.Dead}!");

                agentMaxSpeed = 0f;
                agentMaxAcceleration = 0f;
                movingTowardsTargetPlayer = false;
                agent.speed = 0f;
                agent.enabled = false;
                isEnemyDead = true;
                _isCurrentlyShooting = false;
                _isReloading = false;
                _hasStartedReloadAnimation = false;
                _targetPosition = default;
                _hasBegunInvestigating = false;
                moveTowardsDestination = false;
                
                Escortee?.EscorteeBreakoff();
                netcodeController.DropShotgunClientRpc(ghostId, transform.position);
                netcodeController.ChangeAnimationParameterBoolClientRpc(ghostId, EnforcerGhostAIClient.IsDead, true);
                break;
            }
        }
        
        if (currentBehaviourStateIndex == state) return;
        previousBehaviourStateIndex = currentBehaviourStateIndex;
        currentBehaviourStateIndex = state;
    }

    public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false)
    {
        base.HitEnemy(force, playerWhoHit, playHitSFX);
        if (!IsServer) return;
        if (isEnemyDead) return;
        if (playerWhoHit == null) return;

        enemyHP -= force;
        Escortee?.EscorteeBreakoff(playerWhoHit);
        if (enemyHP > 0)
        {
            return;
        }
        
        // Ghost is dead
        netcodeController.EnterDeathStateClientRpc(ghostId);
        KillEnemyClientRpc(false);
        SwitchBehaviourStateLocally((int)States.Dead);
    }

    public override void SetEnemyStunned(
        bool setToStunned,
        float setToStunTime = 1f,
        PlayerControllerB setStunnedByPlayer = null)
    {
        base.SetEnemyStunned(setToStunned, setToStunTime, setStunnedByPlayer);
        if (!IsServer) return;
        
        _realtimeStunTimer = setToStunTime;
        Escortee?.EscorteeBreakoff(setStunnedByPlayer);
    }

    private void HandleGrabShotgunPhaseTwo(string recievedGhostId)
    {
        if (!IsServer) return;
        if (ghostId != recievedGhostId) return;
        if (_heldShotgun != null) return;
        if (!_shotgunObjectRef.TryGet(out NetworkObject networkObject)) return;
        _heldShotgun = networkObject.gameObject.GetComponent<ShotgunItem>();
    }
    
    private void HandleSpawnShotgun(string recievedGhostId, NetworkObjectReference shotgunObject, int shotgunScrapValue)
    {
        if (ghostId != recievedGhostId) return;
        _shotgunObjectRef = shotgunObject;
    }

    private bool CheckForPath(Vector3 position)
    {
        position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 1.75f);
        path1 = new NavMeshPath();
        
        // ReSharper disable once UseIndexFromEndExpression
        return agent.CalculatePath(position, path1) && !(Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 2.7f)) > 1.5499999523162842);
    }
    
    public override void FinishedCurrentSearchRoutine()
    {
        base.FinishedCurrentSearchRoutine();
        if (!IsServer) return;
        if (searchForPlayers.inProgress)
            searchForPlayers.searchWidth = Mathf.Clamp(searchForPlayers.searchWidth + 10f, 1f, maxSearchRadius);
    }
    
    private void InitializeConfigValues()
    {
        if (!IsServer) return;
        netcodeController.InitializeConfigValuesClientRpc(ghostId);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (!IsServer) return;
        
        netcodeController.OnGrabShotgunPhaseTwo -= HandleGrabShotgunPhaseTwo;
        netcodeController.OnSpawnShotgun -= HandleSpawnShotgun;
    }
    
    private void CalculateAgentSpeed()
    {
        if (!IsServer) return;
        if (stunNormalizedTimer > 0)
        {
            agent.speed = 0;
            agent.acceleration = agentMaxAcceleration;
            return;
        }

        if (currentBehaviourStateIndex != (int)States.Dead)
        {
            MoveWithAcceleration();
        }
    }
    
    private void MoveWithAcceleration() {
        if (!IsServer) return;
        
        float speedAdjustment = Time.deltaTime / 2f;
        agent.speed = Mathf.Lerp(agent.speed, agentMaxSpeed, speedAdjustment);
        
        float accelerationAdjustment = Time.deltaTime;
        agent.acceleration = Mathf.Lerp(agent.acceleration, agentMaxAcceleration, accelerationAdjustment);
    }
    
    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls?.LogInfo(msg);
        #endif
    }
    
    public Vector3 TransformPosition => transform.position;
    public RoundManager RoundManagerInstance => RoundManager.Instance;
}