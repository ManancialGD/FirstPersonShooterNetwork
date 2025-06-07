using System;
using UnityEngine;

public class RagDollLimb : MonoBehaviour
{
    [field: SerializeField] public LimbType ThisLimbType {get; private set;}
    public event Action<RagDollLimb, Vector3, Vector3> Hit;

    public void Damage(Vector3 hitPos, Vector3 direction)
    {
        Hit?.Invoke(this, hitPos, direction);
    }
}
