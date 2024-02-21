using System.Collections;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;

namespace LethalCompanyHarpGhost.Ghost;

public class GhostAnimationController : MonoBehaviour
{
    private ManualLogSource _mls;
    
    #pragma warning disable 0649
    [SerializeField] private Animator animator;
    [SerializeField] private GhostNetcodeController netcodeController;
    [SerializeField] private GhostAudioManager audioManager;
    #pragma warning restore 0649
    
    public static readonly int IsRunning = Animator.StringToHash("isRunning");
    public static readonly int IsStunned = Animator.StringToHash("isStunned");
    public static readonly int IsDead = Animator.StringToHash("isDead");
    public static readonly int Death = Animator.StringToHash("death");
    public static readonly int Stunned = Animator.StringToHash("stunned");
    public static readonly int Recover = Animator.StringToHash("recover");
    public static readonly int Attack = Animator.StringToHash("attack");

    private int attackDamage = 35;

    private void Start()
    {
        _mls = BepInEx.Logging.Logger.CreateLogSource($"{HarpGhostPlugin.ModGuid} | Animation Controller");
        
        animator = GetComponent<Animator>();
        if (animator == null) _mls.LogError("Animator is null");

        netcodeController = GetComponent<GhostNetcodeController>();
        if (netcodeController == null) _mls.LogError("netcodeController is null");

        audioManager = GetComponent<GhostAudioManager>();
        if (audioManager == null) _mls.LogError("audioManager is null");
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls.LogInfo($"Animation Controller : {msg}");
        #endif
    }

    private void OnEnable()
    {
        netcodeController.OnDoAnimation += SetTrigger;
        netcodeController.OnChangeAnimationParameterBool += SetBool;
        GhostNetcodeController.OnEnterDeathState += HandleOnEnterDeathState;
        GhostNetcodeController.OnInitializeConfigValues += HandleInitializeConfigValues;
    }

    private void OnDestroy()
    {
        if (netcodeController == null) return;
        netcodeController.OnDoAnimation -= SetTrigger;
        netcodeController.OnChangeAnimationParameterBool -= SetBool;
        GhostNetcodeController.OnEnterDeathState -= HandleOnEnterDeathState;
        GhostNetcodeController.OnInitializeConfigValues -= HandleInitializeConfigValues;
    }

    private void HandleOnEnterDeathState()
    {
        SetTrigger(Death);
        SetBool(IsDead, true);
        SetBool(IsRunning, false);
        SetBool(IsStunned, false);
        Destroy(this);
    }

    private void HandleInitializeConfigValues()
    {
        attackDamage = GhostConfig.Instance.GhostAttackDamage.Value;
    }

    private void SetBool(int parameter, bool value)
    {
        LogDebug($"SetBoolCalled: {parameter}, {value}");
        animator.SetBool(parameter, value);
    }

    public bool GetBool(int parameter)
    {
        return animator.GetBool(parameter);
    }

    private void SetTrigger(int parameter)
    {
        LogDebug($"SetTriggerCalled: {parameter}");
        animator.SetTrigger(parameter);
    }
    
    public void OnAnimationEventFixAgentSpeedAfterAttack() // Is called by an animation event
    {
        if (NetworkManager.Singleton.IsClient && netcodeController.IsOwner)
        {
            netcodeController.FixAgentSpeedAfterAttackServerRpc();
        }
    }
    
    public void OnAnimationEventAttackShiftComplete() // Is called by an animation event
    {
        if (!NetworkManager.Singleton.IsClient || !netcodeController.IsOwner) return;
        
        netcodeController.ChangeAgentMaxSpeedServerRpc(0f, 0f); // Ghost is frozen while doing the second attack anim
        netcodeController.PlayCreatureVoiceClientRpc((int)GhostAudioManager.AudioClipTypes.Laugh, audioManager.laughSfx.Length);
        LogDebug("OnAnimationEventAttackShiftComplete() called");
        StartCoroutine(DamageTargetPlayerAfterDelay(0.05f, attackDamage, CauseOfDeath.Strangulation));
    }
    
    private IEnumerator DamageTargetPlayerAfterDelay(float delay, int damage, CauseOfDeath causeOfDeath = CauseOfDeath.Unknown) // Damages the player in time with the correct point in the animation
    {
        yield return new WaitForSeconds(delay);
        LogDebug("Damaging player in anim controller");
        netcodeController.DamageTargetPlayerServerRpc(damage, causeOfDeath);
    }
}