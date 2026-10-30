using System;
using System.Collections.Generic;
using UnityEngine;

// 원거리는 실제 엄폐 뒤로 이동 → 숨기 → 노출점 이동 → 탄 발사 → 복귀한다.
// 경로/엄폐가 없으면 LOS를 확보하며 접근한다. 근접 러셔는 우회 접근/근접 타격만 한다.
// 원거리 적은 웨이브 무기를 받아 공격 값과 손에 든 총 모델을 그 무기로 맞춘다. 근접 적은 무기를 받지 않는다.
public sealed class Enemy : MonoBehaviour, IDamageable
{
    public const float DEFAULT_HEADSHOT_MULTIPLIER = 1.5f;
    public const float DEFAULT_HELMET_REDUCTION = 0.4f;
    // 머리 영역 기본값(콜라이더 높이 대비 비율). 실제 값은 EnemyData.headHeightRatio를 쓴다.
    public const float DEFAULT_HEAD_HEIGHT_RATIO = 0.72f;

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
    // 근접 적만 드는 각목. 판정은 기존 근접 공격 로직을 사용한다.
    [SerializeField] private GameObject meleeWeapon;

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

    // 전투 확장: 헤드샷, 헬멧, 저지력 및 넉백
    private float headshotMultiplier = DEFAULT_HEADSHOT_MULTIPLIER;
    private bool hasHelmet;
    private float helmetDamageReduction = DEFAULT_HELMET_REDUCTION;
    private float stoppingResistance = 50f;
    private float stoppingDecayRate = 20f;
    private float staggerDuration = 0.4f;
    private float staggerImmunityDuration = 1.0f;
    private float knockbackResistance = 0.0f;
    private float headHeightRatio = DEFAULT_HEAD_HEIGHT_RATIO;

    private float accumulatedStopping;
    private float staggerUntil;
    private float staggerImmunityUntil;
    private float lastHitTime;
    private readonly Dictionary<int, PelletGroupRecord> pelletRecords = new Dictionary<int, PelletGroupRecord>();

    // 산탄 한 발(같은 그룹)에 대한 이 적의 누적. 저지력은 상한까지 합산하고, 넉백은 명중 산탄을 모아 발사당 한 번만 적용한다.
    private sealed class PelletGroupRecord
    {
        public float accumulatedStopping;
        public float timestamp;
        // 넉백 누적: 명중 산탄 수, 거리 계수 합, 방향 합, 발사 넉백 세기, 발사 산탄 수, 첫 명중 프레임.
        public int knockbackHits;
        public float distanceFactorSum;
        public Vector3 directionSum;
        public float knockback;
        public int pelletCount = 1;
        public int firstHitFrame = -1;
        public bool knockbackApplied;
    }

    public enum CombatState
    {
        SeekingCover,
        MovingToCover,
        Hiding,
        MovingToPeek,
        Peeking,
        Retreating,
        Advancing,
        Melee,
        Stopped,
        Staggered
    }

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
    public string PlayerWeaponId => weapon != null ? weapon.playerWeaponId : "";
    public string DropAmmoId => weapon != null ? weapon.dropAmmoId : "";

    // 전투 정보 조회 프로퍼티
    public float AccumulatedStopping => accumulatedStopping;
    public float StoppingResistance => stoppingResistance;
    public float StoppingDecayRate => stoppingDecayRate;
    public float StaggerDuration => staggerDuration;
    public float StaggerImmunityDuration => staggerImmunityDuration;
    public bool IsStaggered => State == CombatState.Staggered;
    public bool IsStaggerImmune => Time.time < staggerImmunityUntil;
    public bool HasHelmet => hasHelmet;
    public float HeadshotMultiplier => headshotMultiplier;
    public float HelmetDamageReduction => helmetDamageReduction;
    public float KnockbackResistance => knockbackResistance;

    public event Action<Enemy> Died;

    // _weapon은 웨이브 무기다. 근접 적이면 값이 들어와도 무시한다(근접 적에게 총을 주지 않는다).
    public void Configure(EnemyData.Entry _stats, EnemyWeaponData.Entry _weapon, float _hpMultiplier, Color _tint, PlayerHealth _target)
    {
        stats = _stats;
        target = _target;
        dead = false;
        stopped = false;
        ReleaseCover();
        ai = TableManager.Instance != null ? TableManager.Instance.EnemyAi : null;
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

        // 전투 스탯 초기화 (기본값 설정 및 EnemyData 폴백)
        ConfigureCombatStats(_stats);

        ApplyTint(_tint);
        ShowWeapon(armed ? weapon.id : "");
        if (meleeWeapon != null)
        {
            meleeWeapon.SetActive(stats != null && !stats.ranged);
        }
    }

    private void ConfigureCombatStats(EnemyData.Entry _stats)
    {
        string kind = _stats != null ? _stats.id : "";
        if (kind == EnemyInfo.HEAVY_ID)
        {
            headshotMultiplier = DEFAULT_HEADSHOT_MULTIPLIER;
            hasHelmet = true;
            helmetDamageReduction = 0.4f;
            stoppingResistance = 120f;
            stoppingDecayRate = 30f;
            staggerDuration = 0.3f;
            staggerImmunityDuration = 1.5f;
            knockbackResistance = 0.6f;
        }
        else if (kind == EnemyInfo.RIFLEMAN_ID)
        {
            headshotMultiplier = DEFAULT_HEADSHOT_MULTIPLIER;
            hasHelmet = false;
            helmetDamageReduction = 0f;
            stoppingResistance = 50f;
            stoppingDecayRate = 20f;
            staggerDuration = 0.4f;
            staggerImmunityDuration = 1.0f;
            knockbackResistance = 0.2f;
        }
        else
        {
            headshotMultiplier = DEFAULT_HEADSHOT_MULTIPLIER;
            hasHelmet = false;
            helmetDamageReduction = 0f;
            stoppingResistance = 40f;
            stoppingDecayRate = 15f;
            staggerDuration = 0.5f;
            staggerImmunityDuration = 1.0f;
            knockbackResistance = 0.0f;
        }

        // 전투 수치는 EnemyData 테이블에서 읽는다. 테이블이 없으면 필드 기본값을 유지한다.
        if (_stats != null)
        {
            headshotMultiplier = _stats.headshotMultiplier > 0f ? _stats.headshotMultiplier : DEFAULT_HEADSHOT_MULTIPLIER;
            hasHelmet = _stats.hasHelmet;
            helmetDamageReduction = Mathf.Clamp01(_stats.helmetDamageReduction);
            stoppingResistance = _stats.stoppingResistance;
            stoppingDecayRate = _stats.stoppingDecayRate;
            staggerDuration = _stats.staggerDuration;
            staggerImmunityDuration = _stats.staggerImmunityDuration;
            knockbackResistance = Mathf.Clamp01(_stats.knockbackResistance);
            headHeightRatio = _stats.headHeightRatio > 0f ? Mathf.Clamp01(_stats.headHeightRatio) : DEFAULT_HEAD_HEIGHT_RATIO;
        }

        accumulatedStopping = 0f;
        staggerUntil = 0f;
        staggerImmunityUntil = 0f;
        lastHitTime = 0f;
        pelletRecords.Clear();
    }

    // 테스트 및 외부 커스텀 튜닝 지원
    public void SetCombatTuning(float _headshotMult, bool _hasHelmet, float _helmetReduction,
        float _stoppingRes, float _decayRate, float _staggerDur, float _immunityDur, float _knockbackRes)
    {
        headshotMultiplier = _headshotMult > 0f ? _headshotMult : DEFAULT_HEADSHOT_MULTIPLIER;
        hasHelmet = _hasHelmet;
        helmetDamageReduction = Mathf.Clamp01(_helmetReduction);
        stoppingResistance = _stoppingRes > 0f ? _stoppingRes : 50f;
        stoppingDecayRate = _decayRate >= 0f ? _decayRate : 20f;
        staggerDuration = _staggerDur >= 0f ? _staggerDur : 0.4f;
        staggerImmunityDuration = _immunityDur >= 0f ? _immunityDur : 1.0f;
        knockbackResistance = Mathf.Clamp01(_knockbackRes);
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

        ApplyPendingKnockback();

        // 저지력 감쇠 (피격이 없는 동안 서서히 감소)
        if (accumulatedStopping > 0f && Time.time - lastHitTime > 0.3f)
        {
            accumulatedStopping = Mathf.Max(0f, accumulatedStopping - stoppingDecayRate * Time.deltaTime);
        }

        // 경직 상태 처리: 경직 중에는 이동 및 공격을 하지 않는다
        if (State == CombatState.Staggered)
        {
            if (Time.time >= staggerUntil)
            {
                // 경직 종료 및 면역 시작
                staggerImmunityUntil = Time.time + staggerImmunityDuration;
                if (stats.ranged)
                {
                    State = cover != null ? CombatState.Hiding : CombatState.SeekingCover;
                    nextCoverSearch = 0f;
                }
                else
                {
                    State = CombatState.Melee;
                }
            }
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
        if (ai == null)
        {
            return;
        }
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
        if (ai == null)
        {
            return true;
        }
        Vector3 delta = _position - transform.position;
        delta.y = 0f;
        return delta.magnitude <= ai.arrivalDistance + 0.05f;
    }

    private void FindCover()
    {
        if (navigation == null || !navigation.Ready || ai == null)
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
        nextCoverSearch = Time.time + (ai != null ? ai.coverRetrySeconds : 1f);
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

    // 기본 IDamageable 구현 (레거시 및 단순 피해 호출용)
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

    // 정밀 전투 판정: 헤드샷, 헬멧 경감, 저지력 누적/경직/공격중단, 넉백 처리
    public CombatHitResult TakeHit(Projectile.Shot _shot, RaycastHit _hit, Vector3 _shotOrigin)
    {
        if (dead)
        {
            return new CombatHitResult
            {
                damagedEnemy = false,
                killed = false
            };
        }

        HitZone zone = DetermineHitZone(_hit);
        bool isHead = zone == HitZone.Head || zone == HitZone.Helmet;
        bool isHelmet = isHead && (hasHelmet || zone == HitZone.Helmet);
        float baseDamage = _shot.damage;
        float finalDamage = baseDamage;

        if (isHead)
        {
            float mult = headshotMultiplier > 0f ? headshotMultiplier : DEFAULT_HEADSHOT_MULTIPLIER;
            float headDamage = baseDamage * mult;
            if (isHelmet && helmetDamageReduction > 0f)
            {
                float mitigated = headDamage * (1f - helmetDamageReduction);
                // 헬멧 피해 감소는 몸통 피해보다 낮아지지 않도록 제한
                finalDamage = Mathf.Max(baseDamage, mitigated);
            }
            else
            {
                finalDamage = headDamage;
            }
        }

        if (finalDamage > 0f)
        {
            health -= finalDamage;
        }

        if (health <= 0f)
        {
            Die();
            return new CombatHitResult
            {
                damagedEnemy = true,
                killed = true,
                isHeadshot = isHead,
                isHelmetHit = isHelmet,
                damageDealt = finalDamage,
                stoppingApplied = _shot.stoppingPower,
                staggered = false,
                knockedBack = false
            };
        }

        StartHitFlash();
        SoundPlayer.Play(SfxType.EnemyHit);

        // 저지력 처리 (헤드샷 보너스 없음, 기본 저지력만 적용)
        float stoppingPower = _shot.stoppingPower;
        float appliedStopping = 0f;
        bool triggeredStagger = false;

        if (stoppingPower > 0f)
        {
            if (_shot.pelletGroup > 0)
            {
                PelletGroupRecord record = PelletRecord(_shot.pelletGroup);

                float cap = _shot.pelletStopCap > 0f ? _shot.pelletStopCap : float.MaxValue;
                float allowed = Mathf.Max(0f, cap - record.accumulatedStopping);
                appliedStopping = Mathf.Min(stoppingPower, allowed);
                record.accumulatedStopping += appliedStopping;
            }
            else
            {
                appliedStopping = stoppingPower;
            }

            lastHitTime = Time.time;

            if (State != CombatState.Staggered && Time.time >= staggerImmunityUntil)
            {
                accumulatedStopping += appliedStopping;
                if (accumulatedStopping >= stoppingResistance)
                {
                    TriggerStagger();
                    triggeredStagger = true;
                }
            }
        }

        // 넉백: 단발은 즉시, 산탄은 같은 발사의 명중 산탄을 모아 다음 프레임에 한 번만 적용한다(명중 산탄 수·거리 반영).
        bool appliedKnockback = false;
        if (_shot.knockback > 0f && knockbackResistance < 1f)
        {
            Vector3 shotDir = _hit.point - _shotOrigin;
            shotDir.y = 0f;
            if (shotDir.sqrMagnitude < 0.0001f)
            {
                shotDir = transform.position - _shotOrigin;
                shotDir.y = 0f;
            }
            float dist = Vector3.Distance(_shotOrigin, _hit.point);
            float distFactor = Mathf.Clamp01(1f - (dist / Mathf.Max(1f, _shot.maxDistance)));

            if (_shot.pelletGroup > 0)
            {
                PelletGroupRecord record = PelletRecord(_shot.pelletGroup);
                if (!record.knockbackApplied && distFactor > 0f)
                {
                    record.knockbackHits++;
                    record.distanceFactorSum += distFactor;
                    record.directionSum += shotDir.normalized;
                    record.knockback = _shot.knockback;
                    record.pelletCount = Mathf.Max(1, _shot.pelletCount);
                    if (record.firstHitFrame < 0)
                    {
                        record.firstHitFrame = Time.frameCount;
                    }
                    appliedKnockback = true;
                }
            }
            else
            {
                float forceToApply = _shot.knockback * distFactor;
                if (forceToApply > 0.001f)
                {
                    ApplyKnockback(shotDir, forceToApply);
                    appliedKnockback = true;
                }
            }
        }

        return new CombatHitResult
        {
            damagedEnemy = true,
            killed = false,
            isHeadshot = isHead,
            isHelmetHit = isHelmet,
            damageDealt = finalDamage,
            stoppingApplied = appliedStopping,
            staggered = triggeredStagger,
            knockedBack = appliedKnockback
        };
    }

    private PelletGroupRecord PelletRecord(int _group)
    {
        PruneStalePelletRecords();
        if (!pelletRecords.TryGetValue(_group, out PelletGroupRecord record))
        {
            record = new PelletGroupRecord();
            pelletRecords[_group] = record;
        }
        record.timestamp = Time.time;
        return record;
    }

    // 산탄 넉백 적용. 첫 명중 다음 프레임에 그 발사로 맞은 산탄을 모아 한 번 민다.
    // 세기 = 발사 넉백 × (명중 산탄 수 / 발사 산탄 수) × 평균 거리 계수. 저항은 ApplyKnockback이 반영한다.
    private void ApplyPendingKnockback()
    {
        foreach (var record in pelletRecords.Values)
        {
            if (record.knockbackApplied || record.knockbackHits <= 0 || Time.frameCount <= record.firstHitFrame)
            {
                continue;
            }
            record.knockbackApplied = true;
            float hitRatio = Mathf.Clamp01((float)record.knockbackHits / record.pelletCount);
            float force = record.knockback * hitRatio * (record.distanceFactorSum / record.knockbackHits);
            ApplyKnockback(record.directionSum, force);
        }
    }

    public HitZone DetermineHitZone(RaycastHit _hit)
    {
        if (_hit.collider != null)
        {
            EnemyHitbox hitbox = _hit.collider.GetComponent<EnemyHitbox>();
            if (hitbox != null)
            {
                return hitbox.Zone;
            }
        }

        // 폴백 기하학 판정: 적 몸체 상단부 판정
        float enemyBottomY = transform.position.y;
        float height = EnemyNavigation.HEIGHT;
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            height = capsule.height;
            enemyBottomY = transform.position.y + capsule.center.y - capsule.height * 0.5f;
        }
        else
        {
            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                height = box.size.y;
                enemyBottomY = transform.position.y + box.center.y - box.size.y * 0.5f;
            }
        }

        float hitHeight = _hit.point.y - enemyBottomY;
        if (hitHeight >= height * headHeightRatio)
        {
            return hasHelmet ? HitZone.Helmet : HitZone.Head;
        }

        return HitZone.Body;
    }

    public void TriggerStagger()
    {
        accumulatedStopping = 0f;
        State = CombatState.Staggered;
        staggerUntil = Time.time + staggerDuration;

        // 공격 중단 및 내비게이션 정지
        firedThisPeek = false;
        navigation?.Stop();
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + staggerDuration + 0.2f);
    }

    public void ApplyKnockback(Vector3 _direction, float _force)
    {
        if (dead || stopped || _force <= 0f)
        {
            return;
        }

        Vector3 moveDir = _direction;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.0001f)
        {
            return;
        }
        moveDir.Normalize();

        float effectiveDist = _force * Mathf.Clamp01(1f - knockbackResistance);
        if (effectiveDist <= 0.001f)
        {
            return;
        }

        // 지형 충돌 검사 (벽 관통 방지)
        float allowed = effectiveDist;
        if (Physics.CapsuleCast(
            transform.position + Vector3.up * EnemyNavigation.RADIUS,
            transform.position + Vector3.up * (EnemyNavigation.HEIGHT - EnemyNavigation.RADIUS),
            EnemyNavigation.RADIUS - 0.03f,
            moveDir,
            out RaycastHit hit,
            effectiveDist + 0.03f,
            ENEMY_SHOT_MASK,
            QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.GetComponentInParent<Enemy>() != this)
            {
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - 0.03f));
            }
        }

        Vector3 targetPos = transform.position + moveDir * allowed;
        if (UnityEngine.AI.NavMesh.SamplePosition(targetPos, out UnityEngine.AI.NavMeshHit navHit, 0.8f, UnityEngine.AI.NavMesh.AllAreas))
        {
            targetPos = navHit.position;
        }

        transform.position = targetPos;
        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.nextPosition = targetPos;
        }
    }

    private void PruneStalePelletRecords()
    {
        if (pelletRecords.Count == 0)
        {
            return;
        }
        float now = Time.time;
        List<int> toRemove = null;
        foreach (var pair in pelletRecords)
        {
            if (now - pair.Value.timestamp > 2.0f)
            {
                if (toRemove == null)
                {
                    toRemove = new List<int>();
                }
                toRemove.Add(pair.Key);
            }
        }
        if (toRemove != null)
        {
            for (int i = 0; i < toRemove.Count; i++)
            {
                pelletRecords.Remove(toRemove[i]);
            }
        }
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
        SoundPlayer.Play(stats != null && stats.id == EnemyInfo.HEAVY_ID ? SfxType.Explosion : SfxType.EnemyDown);
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
