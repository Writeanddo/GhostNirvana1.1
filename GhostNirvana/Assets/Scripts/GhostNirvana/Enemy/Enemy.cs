using System.Collections.Generic;
using UnityEngine;
using CombatSystem;
using Base;
using NaughtyAttributes;

namespace GhostNirvana {

public abstract class Enemy<Input>
    : PossessableAgent<Input>, IHurtable, IHurtResponder, IHitResponder {

    public Entity Owner => this;
    public Status Status { get; private set; }

#region Inherited members
    public void RespondToHurt(Hit hit) {
    }

    public bool ValidateHit(Hit hit) => true;

    void IHurtable.OnTakeDamage(float damageAmount, DamageType damageType, Hit hit)
        => Status.TakeDamage(damageAmount);
#endregion

    List<Hitbox> hitboxes = new List<Hitbox>();
    [SerializeField, Required] MovableAgentRuntimeSet allEnemies;

    protected override void Awake() {
        base.Awake();
        Status = GetComponent<Status>();
        Status.Owner = this;
        hitboxes.AddRange(GetComponentsInChildren<Hitbox>());
    }

    protected virtual void OnEnable() => allEnemies.Add(this);
    protected virtual void OnDisable() => allEnemies.Remove(this);

    protected void CheckForHits() {
        foreach (Hitbox hitbox in hitboxes)
            hitbox.CheckForHits();
    }

    public void RespondToHit(Hit hit) {
        Entity targetOwner = hit.Hurtbox.HurtResponder.Owner;
        (targetOwner as IHurtable)?.TakeDamage(Status.BaseStats.Damage, null, hit);
        (targetOwner as IKnockbackable)?.ApplyKnockback(Status.BaseStats.Knockback, hit.Normal);
    }
}


}
