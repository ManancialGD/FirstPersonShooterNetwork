using System;
using Unity.Netcode;
using UnityEngine;

public class HealthModule : NetworkBehaviour
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private Animator anim;

    public readonly NetworkVariable<int> currentHealth = new(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsDead { get; private set; } = false;


    public event Action Died;

    private RagDollLimb[] ragDollLimbs;

    private void Start()
    {
        if (IsServer)
            currentHealth.Value = maxHealth;

        ragDollLimbs = GetComponentsInChildren<RagDollLimb>();

        DeactivateRagdoll();

        if (IsServer)
            foreach (var limb in ragDollLimbs)
            {
                if (limb != null)
                {
                    limb.Hit += TakeDamage;
                }
            }
    }

    public void TakeDamage(RagDollLimb limb, Vector3 hitPos, Vector3 direction)
    {
        if (IsDead || !IsServer) return;
        int damage = 0;

        switch (limb.ThisLimbType)
        {
            case LimbType.Bottom:
                damage = 25;
                break;
            case LimbType.Upper:
                damage = 50;
                break;
            case LimbType.Head:
                damage = 100;
                break;
        }

        currentHealth.Value -= damage;

        if (currentHealth.Value <= 0)
        {
            Die(limb, direction);
        }
    }

    public void Die(RagDollLimb limb, Vector3 direction)
    {
        if (IsDead || !IsServer) return;
        IsDead = true;
        Died?.Invoke();
        ActivateRagdoll();

        DieClientRpc();
    }

    [ClientRpc]
    private void DieClientRpc()
    {
        IsDead = true;
        Died?.Invoke();
        ActivateRagdoll();
    }

    public void DeactivateRagdoll()
    {
        if (anim != null)
        {
            anim.enabled = true;
        }

        foreach (var limb in ragDollLimbs)
        {
            limb.GetComponent<Rigidbody>().isKinematic = true;
        }
    }

    [ClientRpc(RequireOwnership = false)]
    public void DeactivateRagdollClientRpc()
    {
        DeactivateRagdoll();
    }

    public void ActivateRagdoll()
    {
        if (anim != null)
        {
            anim.enabled = false;
        }

        foreach (var limb in ragDollLimbs)
        {
            limb.GetComponent<Rigidbody>().isKinematic = false;
        }
    }

    public void ResetIsDead()
    {
        if (!IsServer) return;
        IsDead = false;
        ResetIsDeadClientRpc();
    }

    [ClientRpc]
    private void ResetIsDeadClientRpc()
    {
        IsDead = false;
    }
}
