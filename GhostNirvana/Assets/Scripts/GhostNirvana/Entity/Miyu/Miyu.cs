using UnityEngine;
using Optimization;
using NaughtyAttributes;
using CombatSystem;
using Danmaku;
using Base;
using Utils;
using ScriptableBehaviour;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

namespace GhostNirvana {

public partial class Miyu : PossessableAgent<Miyu.Input>, IHurtable, IHurtResponder, IHitResponder {
    public static Miyu Instance;

    public enum States {
        Grounded,
        Dash,
        Dead
    }


    #region Components
    public MiyuStateMachine StateMachine { get; private set; }
    #endregion

    #region Progression
    [HorizontalLine(color:EColor.Blue)]
    [BoxGroup("Progression"), SerializeField, Expandable] LinearLimiterFloat xp;
    #endregion

    #region Movement
    [HorizontalLine(color:EColor.Blue)]
    [BoxGroup("Movement"), SerializeField, Expandable] LinearFloat movementSpeed;
    [BoxGroup("Movement"), Range(0, 64), SerializeField] float accelerationAlpha = 24;
    [BoxGroup("Movement"), Range(0, 64), SerializeField] float deccelerationAlpha = 12;
    [BoxGroup("Movement"), Range(0, 720), SerializeField] public float TurnSpeed = 24;
    #endregion

    #region Combat
    [HorizontalLine(color:EColor.Blue)]
    [BoxGroup("Combat"), SerializeField, Required]
    MovableAgentRuntimeSet allEnemies;
    [BoxGroup("Combat"), SerializeField]
    List<Projectile> projectilePrefab;
    [field:SerializeField, BoxGroup("Combat")] public Transform BulletSource {get; private set; }
    [BoxGroup("Combat"), SerializeField, Expandable] LinearInt palette;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearLimiterInt health;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearFloat attackSpeed;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearInt bulletDamage;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearFloat bulletSpeed;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearFloat bulletKnockback;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearInt projectileCount;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearFloat projectileSpread;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearInt pierce;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearInt bounce;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearInt ricochet;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearFloat ricochetRange;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearLimiterInt magazine;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearFloat reloadRate;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearFloat pushbackStrengthOnDamage;
    [BoxGroup("Combat"), SerializeField, MinMaxSlider(0, 20)] Vector2 pushbackAttenuationDistance;
    [BoxGroup("Combat"), SerializeField] Ease pushbackAttenuationEase;
    [BoxGroup("Combat"), SerializeField] float pushbackStrengthOnDeath;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearLimiterFloat shield;
    [BoxGroup("Combat"), SerializeField, Expandable] LinearFloat shieldRegenerationRate;
    [BoxGroup("Combat"), SerializeField] float iframeSeconds;
    [BoxGroup("Combat"), SerializeField] UnityEvent<IHurtable, int, DamageType, Hit> _OnDamage;
    [BoxGroup("Combat")] public UnityEvent OnDeathEvent;
    [BoxGroup("Combat")] public UnityEvent OnShootEvent;
    [BoxGroup("Combat")] public UnityEvent<Hit> OnHitEvent = new UnityEvent<Hit>();
    [BoxGroup("Combat")] public UnityEvent OnLifeHit;
    [BoxGroup("Combat")] public UnityEvent OnShieldHit;
    [BoxGroup("Combat")] public UnityEvent<MovableAgent> OnKillEnemy;
    #endregion

    Timer iframeHappening;

    public Entity Owner => this;
    public bool IsDead => health.Value == 0;
    public bool HasBullet => magazine ? magazine.Value > 0 : false;
    public UnityEvent<IHurtable, int, DamageType, Hit> OnBeforeDamage => _OnDamage;

    Entity IHitResponder.Owner { get => this; set {} }

    protected override void Awake() {
		base.Awake();
        StateMachine = GetComponent<MiyuStateMachine>();
        Instance = this;

    }

    protected void Start() {
        health.Value = health.Limiter;
    }

    protected override void OnEnable() {
		base.OnEnable();
        IHurtResponder.ConnectChildrenHurtboxes(this);

        allEnemies.OnRemove += OnEnemyRemoveFromField;
    }

    protected void OnDisable() {
        IHurtResponder.DisconnectChildrenHurtboxes(this);

        allEnemies.OnRemove -= OnEnemyRemoveFromField;
    }

    protected void Update() {
        PerformUpdate(StateMachine.RunUpdate);

        float deltaTimeMinutes = Time.deltaTime / 60.0f;
        shield.Value += shieldRegenerationRate.Value * deltaTimeMinutes;
        shield.CheckAndCorrectLimit();
    }

    public void ShootProjectile(Vector3 targetPosition) {
        Vector3 targetDirection = targetPosition - BulletSource.position;
        targetDirection.y = 0;
        targetDirection.Normalize();

        if (targetDirection.sqrMagnitude == 0) targetDirection = BulletSource.forward;

        float low = -projectileSpread.Value / 2;
        float high = projectileSpread.Value / 2;

        for (int i = 0; i < projectileCount.Value; i++) {
            float rotDegrees = 0;
            if (projectileCount.Value > 1) {
                float t =  ((float) i) / (projectileCount.Value - 1);
                rotDegrees = Mathf.Lerp(low, high, t);
            }

            Projectile bullet = ObjectPoolManager.Instance.Borrow(gameObject.scene,
                projectilePrefab[palette.Value], BulletSource.position, BulletSource.rotation);

            Vector3 projectileDirection = Quaternion.Euler(0, rotDegrees, 0) * targetDirection;

            bullet.Initialize(bulletDamage.Value, bulletKnockback.Value, 
                    pierce.Value, bounce.Value, ricochet.Value, ricochetRange.Value,
                    projectileDirection * bulletSpeed.Value);
            (bullet as IHitResponder).Owner = this;
        }

        OnShootEvent?.Invoke();
    }

    void IHurtable.OnTakeDamage(int damageAmount, DamageType damageType, Hit hit) {
        if (IsDead) return;

        if (shield.Value >= 1) {
            shield.Value -= 1;
            shield.CheckAndCorrectLimit();
            OnShieldHit?.Invoke();
        } else {
            bool killingHit = health.Value > 0 && health.Value <= damageAmount;
            health.Value -= damageAmount;
            health.CheckAndCorrectLimit();
            OnLifeHit?.Invoke();

            if (killingHit) OnDeath();
        }

        PushAllEnemiesAway(pushbackStrengthOnDamage.Value);

        iframeHappening = iframeSeconds;
    }

    void PushAllEnemiesAway(float strength, bool ignoreKnockbackImmunity = false) {
        foreach (MovableAgent enemy in allEnemies) {
            Vector3 knockbackDir = enemy.transform.position - transform.position;
            knockbackDir.y = 0;
            float distance = knockbackDir.magnitude;

            if (distance > 0.01) knockbackDir /= distance;
            else knockbackDir = Vector3.zero;

            float t = Mathf.InverseLerp(pushbackAttenuationDistance.x, pushbackAttenuationDistance.y, distance);
            t = Mathf.Clamp01(t);

            float attenuation = 1 - EaseEvaluator.Evaluate(pushbackAttenuationEase, t);

            (enemy as IKnockbackable).ApplyKnockback(strength * attenuation, knockbackDir, ignoreKnockbackImmunity);
        }
    }

    void OnDeath() {
        StateMachine.SetState(States.Dead);
        PushAllEnemiesAway(pushbackStrengthOnDeath, ignoreKnockbackImmunity: true);
        OnDeathEvent?.Invoke();
    }

    public bool ValidateHit(Hit hit) => !IsDead && !iframeHappening;
    public void RespondToHurt(Hit hit) { }

    protected override IEnumerator IDispose() {
        // never actually disposes of the player
        while (true) yield return null;
    }

    public void RespondToHit(Hit hit) {
        // will be invoked by child
        OnHitEvent?.Invoke(hit);
    }

    protected void OnEnemyRemoveFromField(MovableAgent enemy) {
        if (enemy.GetComponent<Status>()?.IsDead ?? false)
            OnKillEnemy?.Invoke(enemy);
    }
}

}
