using System;
using UnityEngine;

// 원거리는 실제 엄폐 뒤로 이동 → 숨기 → 노출점 이동 → 탄 발사 → 복귀한다.
// 경로/엄폐가 없으면 LOS를 확보하며 접근한다. 근접 러셔는 우회 접근/근접 타격만 한다.
// 원거리 적은 웨이브 무기를 받아 공격 값과 손에 든 총 모델을 그 무기로 맞춘다. 근접 적은 무기를 받지 않는다.
public sealed class Enemy : MonoBehaviour, IDamageable
{
    private const string BASE_COLOR_PROPERTY = "_BaseColor";
    // 피격 반응: 몸이 잠깐 흰색으로 번쩍인다. 처치 시에는 앞으로 쓰러진다.
    private const float HIT_FLASH_SECONDS = 0.08f;
    private const float FALL_ANGLE = 80f;
    // 총구가 없는 레거시 프리팹만 쓰는 기하학 폴백. 조준은 실제 CharacterController 중심을 쓴다.
    private const float FALLBACK_MUZZLE_HEIGHT = 1.4f;
    // 탄이 막히는 대상: 지형·엄폐물·플레이어. 같은 편 적은 Projectile이 통과시키고, 트리거는 스윕에서 제외된다.
    private static readonly int ENEMY_SHOT_MASK = Physics.DefaultRaycastLayers;

    [SerializeField] private Renderer bodyRenderer;
    // 무기 모델 부모. 자식 이름이 무기 id와 같은 모델만 켜진다. 모델은 프리팹에 미리 둔다.
    [SerializeField] private Transform weaponMount;

    private static readonly int BASE_COLOR_ID = Shader.PropertyToID(BASE_COLOR_PROPERTY);

    private EnemyData.Entry stats;
    private EnemyWeaponData.Entry weapon;
    private PlayerHealth target;
    private MaterialPropertyBlock colorBlock;
    private float health;
    private float nextAttackTime;
    private float attackDamage;
    private float attackRange;
    private float attackInterval;
    private float projectileSpeed;
    private float projectileLifetime;
    private float projectileRadius;
    private Color baseTint;
    private float flashEndTime;
    private bool flashing;
    private bool dead;
    private bool stopped;
    private EnemyNavigation navigation;
    private EnemyAiData ai;
    private CoverNode cover;
    private Vector3 peekPosition;
    private float stateUntil;
    private float nextCoverSearch;
    private float nextValidation;
    private bool firedThisPeek;
    private Transform activeMuzzle;

    public enum CombatState { SeekingCover, MovingToCover, Hiding, MovingToPeek, Peeking, Retreating, Advancing, Melee, Stopped }
    public CombatState State { get; private set; } = CombatState.SeekingCover;
    public CoverNode CurrentCover => cover;
    public EnemyNavigation Navigation => navigation;
    public float ShotRadius => projectileRadius;

    public bool IsDead => dead;
    // 현재 체력. 검증과 디버그 표시용 읽기 전용이다.
    public float Health => health;
    public bool IsFlashing => flashing;
    public string KindId => stats != null ? stats.id : "";
    public int Reward => stats != null ? stats.reward : 0;
    // 들고 있는 무기 id. 근접 적은 항상 빈 문자열이다.
    public string WeaponId => weapon != null ? weapon.id : "";

    public event Action<Enemy> Died;

    // _weapon은 웨이브 무기다. 근접 적이면 값이 들어와도 무시한다(근접 적에게 총을 주지 않는다).
    public void Configure(EnemyData.Entry _stats, EnemyWeaponData.Entry _weapon, float _hpMultiplier, Color _tint, PlayerHealth _target)
    {
        stats = _stats;
        target = _target;
        dead = false;
        stopped = false;
        ReleaseCover();
        ai = TableManager.Instance.EnemyAi;
        navigation = GetComponent<EnemyNavigation>();
        if (navigation != null)
        {
            navigation.Configure(stats != null ? stats.moveSpeed : 0f, ai);
        }
        State = stats != null && stats.ranged ? CombatState.SeekingCover : CombatState.Melee;
        nextCoverSearch = 0f;
        bool armed = stats != null && stats.ranged && _weapon != null;
        weapon = armed ? _weapon : null;
        attackDamage = armed ? weapon.attackDamage : (stats != null ? stats.attackDamage : 0f);
        attackRange = armed ? weapon.attackRange : (stats != null ? stats.attackRange : 0f);
        attackInterval = armed ? weapon.attackInterval : (stats != null ? stats.attackInterval : 1f);
        // 탄도는 무기 값이 우선이고, 무기가 없는 원거리 적만 EnemyData의 폴백 값을 쓴다.
        projectileSpeed = armed ? weapon.projectileSpeed : (stats != null ? stats.projectileSpeed : 0f);
        projectileLifetime = armed ? weapon.projectileLifetime : (stats != null ? stats.projectileLifetime : 0f);
        projectileRadius = armed ? weapon.projectileRadius : (stats != null ? stats.projectileRadius : 0f);
        health = stats != null ? stats.maxHealth * Mathf.Max(1f, _hpMultiplier) : 0f;
        nextAttackTime = Time.time + 0.5f;
        baseTint = _tint;
        flashing = false;
        ApplyTint(_tint);
        ShowWeapon(armed ? weapon.id : "");
    }

    private void Update()
    {
        if (flashing && Time.time >= flashEndTime)
        {
            flashing = false;
            ApplyTint(baseTint);
        }

        if (dead || stopped || stats == null)
        {
            return;
        }
        if (target == null || !target.IsAlive)
        {
            StopCombat();
            return;
        }
        navigation?.TickMotion();
        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(toTarget);
        }

        if (!stats.ranged)
        {
            State = CombatState.Melee;
            bool clear = EnemySight.Clear(transform.position + Vector3.up, target, attackRange, transform, 0.02f);
            if (distance > stats.stopDistance || !clear)
            {
                navigation?.MoveTo(target.transform.position);
            }
            else
            {
                navigation?.Stop();
            }
            if (clear && distance <= attackRange && Time.time >= nextAttackTime)
            {
                nextAttackTime = Time.time + attackInterval;
                Attack();
            }
            return;
        }
        UpdateRanged(distance);
    }

    private void UpdateRanged(float _distance)
    {
        if (cover != null && Time.time >= nextValidation)
        {
            nextValidation = Time.time + ai.revalidateSeconds;
            if (cover.Owner != this || !cover.IsHiddenFrom(target) || navigation == null ||
                !navigation.CanReach(cover.HiddenPosition) || !cover.TryPeek(this, target, attackRange, out _))
            {
                AbandonCover();
            }
        }
        if (cover != null && navigation != null && navigation.IsStuck)
        {
            AbandonCover();
        }
        if (cover == null)
        {
            if (Time.time >= nextCoverSearch)
            {
                State = CombatState.SeekingCover;
                FindCover();
                nextCoverSearch = Time.time + ai.coverRetrySeconds;
            }
            if (cover == null)
            {
                State = CombatState.Advancing;
                bool clear = HasShot();
                if (clear && _distance <= Mathf.Min(stats.stopDistance, attackRange))
                {
                    navigation?.Stop();
                }
                else
                {
                    navigation?.MoveTo(target.transform.position);
                }
                TryRangedAttack();
                return;
            }
        }

        switch (State)
        {
            case CombatState.MovingToCover:
            case CombatState.Retreating:
                if (Arrived(cover.HiddenPosition))
                {
                    navigation.Stop();
                    State = CombatState.Hiding;
                    stateUntil = Time.time + ai.hideSeconds;
                }
                else if (!navigation.MoveTo(cover.HiddenPosition))
                {
                    AbandonCover();
                }
                break;
            case CombatState.Hiding:
                if (Time.time >= stateUntil)
                {
                    if (cover.TryPeek(this, target, attackRange, out peekPosition))
                    {
                        State = CombatState.MovingToPeek;
                        navigation.MoveTo(peekPosition);
                    }
                    else
                    {
                        AbandonCover();
                    }
                }
                break;
            case CombatState.MovingToPeek:
                if (!EnemySight.Clear(peekPosition + Vector3.up * FALLBACK_MUZZLE_HEIGHT, target, attackRange, transform, projectileRadius))
                {
                    State = CombatState.Retreating;
                }
                else if (Arrived(peekPosition))
                {
                    navigation.Stop();
                    State = CombatState.Peeking;
                    firedThisPeek = false;
                    stateUntil = Time.time + ai.peekSeconds;
                }
                else if (!navigation.MoveTo(peekPosition))
                {
                    AbandonCover();
                }
                break;
            case CombatState.Peeking:
                if (!HasShot() || Time.time >= stateUntil || firedThisPeek)
                {
                    State = CombatState.Retreating;
                    navigation.MoveTo(cover.HiddenPosition);
                }
                else
                {
                    firedThisPeek = TryRangedAttack();
                }
                break;
        }
    }

    private bool Arrived(Vector3 _position)
    {
        Vector3 delta = _position - transform.position;
        delta.y = 0f;
        return delta.magnitude <= ai.arrivalDistance + 0.05f;
    }

    private void FindCover()
    {
        if (navigation == null || !navigation.Ready)
        {
            return;
        }
        CoverNode best = null;
        float score = ai.searchRadius * ai.searchRadius;
        Vector3 bestPeek = default;
        foreach (var node in CoverNode.Active)
        {
            if (node == null || (node.Owner != null && node.Owner != this) || !node.IsHiddenFrom(target) ||
                !navigation.CanReach(node.HiddenPosition) || !node.TryPeek(this, target, attackRange, out Vector3 peek))
            {
                continue;
            }
            float candidate = (node.HiddenPosition - transform.position).sqrMagnitude;
            if (candidate < score)
            {
                score = candidate;
                best = node;
                bestPeek = peek;
            }
        }
        if (best != null && best.TryReserve(this))
        {
            cover = best;
            peekPosition = bestPeek;
            State = CombatState.MovingToCover;
            nextValidation = Time.time + ai.revalidateSeconds;
            navigation.MoveTo(cover.HiddenPosition);
        }
    }

    private bool HasShot()
    {
        Vector3 origin = ShotOrigin();
        Vector3 shoulder = transform.position;
        shoulder.y = origin.y;
        // A long visible barrel must not poke through a wall and fire from its far side.
        return EnemySight.ClearSegment(shoulder, origin, target, transform, projectileRadius) &&
            EnemySight.Clear(origin, target, attackRange, transform, projectileRadius);
    }

    private Vector3 ShotOrigin()
    {
        return activeMuzzle != null ? activeMuzzle.position : transform.position + Vector3.up * FALLBACK_MUZZLE_HEIGHT;
    }

    private bool TryRangedAttack()
    {
        if (Time.time < nextAttackTime || !HasShot())
        {
            return false;
        }
        nextAttackTime = Time.time + attackInterval;
        Attack();
        return true;
    }

    private void AbandonCover()
    {
        ReleaseCover();
        navigation?.Stop();
        State = CombatState.SeekingCover;
        nextCoverSearch = Time.time + ai.coverRetrySeconds;
    }

    private void ReleaseCover()
    {
        if (cover != null)
        {
            cover.Release(this);
            cover = null;
        }
    }

    public void StopCombat()
    {
        stopped = true;
        ReleaseCover();
        navigation?.Stop();
        State = CombatState.Stopped;
    }

    private void OnDisable()
    {
        ReleaseCover();
        navigation?.Stop();
    }

    public void TakeDamage(float _amount)
    {
        if (dead || _amount <= 0f)
        {
            return;
        }

        health -= _amount;
        if (health <= 0f)
        {
            Die();
            return;
        }
        StartHitFlash();
        SoundPlayer.Play(SfxType.EnemyHit);
    }

    private void StartHitFlash()
    {
        flashing = true;
        flashEndTime = Time.time + HIT_FLASH_SECONDS;
        ApplyTint(Color.white);
    }

    private void Attack()
    {
        if (stats.ranged)
        {
            SoundPlayer.Play(SfxType.EnemyShot);
            FireProjectile();
            return;
        }
        // 근접 러셔는 총이 없어 접촉 거리에서 직접 때린다. 원거리 적은 위에서 발사체만 보낸다.
        target.ApplyDamage(attackDamage, transform.position);
    }

    // 원거리 적의 발사. 피해는 여기서 주지 않는다. 탄이 플레이어나 지형에 닿을 때 Projectile이 준다.
    private void FireProjectile()
    {
        if (stats == null || !stats.ranged || !HasShot())
        {
            return;
        }
        if (projectileSpeed <= 0f || projectileLifetime <= 0f)
        {
            Debug.LogWarning($"적 {KindId}: 발사체 탄도 값이 없어 발사하지 않습니다.");
            return;
        }

        Vector3 origin = ShotOrigin();
        Vector3 aim = EnemySight.TargetCenter(target);
        Vector3 direction = aim - origin;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }
        direction.Normalize();

        EffectSpawner.Spawn(EffectSpawner.MUZZLE_FLASH, origin, Quaternion.LookRotation(direction));
        var shot = new Projectile.Shot
        {
            team = Projectile.Team.Enemy,
            damage = attackDamage,
            speed = projectileSpeed,
            maxDistance = attackRange,
            lifetime = projectileLifetime,
            radius = projectileRadius
        };
        ProjectileSpawner.Launch(ProjectileSpawner.ENEMY_BULLET, shot, origin, direction, ENEMY_SHOT_MASK, transform);
    }

    // 무기 모델은 프리팹에 미리 만들어 두고, 이름이 무기 id와 맞는 것만 켠다. 런타임에 메시를 만들지 않는다.
    private void ShowWeapon(string _weaponId)
    {
        activeMuzzle = null;
        if (weaponMount == null)
        {
            return;
        }
        foreach (Transform model in weaponMount)
        {
            model.gameObject.SetActive(model.name == _weaponId);
            if (model.gameObject.activeSelf)
            {
                activeMuzzle = model.Find("Muzzle");
            }
        }
    }

    private void Die()
    {
        dead = true;
        StopCombat();
        foreach (var part in GetComponentsInChildren<Collider>())
        {
            part.enabled = false;
        }
        StartHitFlash();
        transform.rotation *= Quaternion.Euler(FALL_ANGLE, 0f, 0f);
        SoundPlayer.Play(stats.id == EnemyInfo.HEAVY_ID ? SfxType.Explosion : SfxType.EnemyDown);
        Died?.Invoke(this);
        Destroy(gameObject, 0.4f);
    }

    private void ApplyTint(Color _tint)
    {
        if (bodyRenderer == null)
        {
            return;
        }
        if (colorBlock == null)
        {
            colorBlock = new MaterialPropertyBlock();
        }
        colorBlock.SetColor(BASE_COLOR_ID, _tint);
        bodyRenderer.SetPropertyBlock(colorBlock);
    }
}
