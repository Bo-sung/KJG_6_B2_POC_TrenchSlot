using System;

// 게임 로그 이벤트의 data 직렬화 자료(JsonUtility). 로직 없음. 기록은 GameTelemetry만 한다.
// 공통 필드(runId·wave·runSeconds)는 GameTelemetry가 채우고, 각 이벤트는 자기 필드만 더한다.
[Serializable]
public class TelemetryEvent
{
    public string runId;
    public int wave;
    // 런 시작 후 경과 게임 시간(초).
    public float runSeconds;
}

[Serializable]
public sealed class RunStartedEvent : TelemetryEvent
{
    public int startMoney;
    public float maxHealth;
    public string startWeaponId;
}

[Serializable]
public sealed class RunEndedEvent : TelemetryEvent
{
    // GameOver · SceneUnloaded · Quit
    public string reason;
    public int balance;
    public int totalKills;
    public int shots;
    public int shotsHit;
    public float damageTaken;
}

[Serializable]
public sealed class WaveStartedEvent : TelemetryEvent
{
    public float durationSeconds;
    public int rushers;
    public int riflemen;
    public int heavies;
    public string era;
    public string enemyWeapon;
    public float hpMultiplier;
    public int balance;
    public float health;
}

// 적 전멸(남은 시간 동안 슬롯 가능).
[Serializable]
public sealed class WaveClearedEvent : TelemetryEvent
{
    public float timeLeft;
    public int killsThisWave;
}

// 시간 만료 생존 → 회복·보너스 지급.
[Serializable]
public sealed class WaveCompletedEvent : TelemetryEvent
{
    public float healthRestored;
    public int bonusPaid;
    public int balance;
    public float health;
    public int killsThisWave;
}

[Serializable]
public sealed class IntermissionStartedEvent : TelemetryEvent
{
    public float intermissionSeconds;
    public int balance;
}

[Serializable]
public sealed class SlotSpinEvent : TelemetryEvent
{
    public int cost;
    public int balance;
    // SpinRejected 사유. 그 밖에는 빈 문자열.
    public string reason;
}

[Serializable]
public sealed class SlotSpinStartedEvent : TelemetryEvent
{
    public int cost;
    public int balance;
    public float spinSeconds;
    public int[] faces;
    // 회전 시작 시 실제 회복량(최대 체력에서 잘린 값)과 회복 후 체력.
    public float healthRestored;
    public float health;
}

[Serializable]
public sealed class SlotSpinResolvedEvent : TelemetryEvent
{
    public int multiplier;
    // 배수 당첨 현금(퍽·저금통 적용).
    public int payout;
    // 현물 보상(SlotRewardInfo.Apply로 실제 지급된 계획). 현금 대체·탄약·파츠.
    public int rewardCash;
    public string rewardAmmoId;
    public int rewardAmmoAmount;
    public bool ammoRefill;
    public string[] rewardPartIds;
    public bool nearMiss;
    public float piggyMultiplier;
    public int balance;
    public int magazineAmmo;
    public int reserveAmmo;
}

[Serializable]
public sealed class ShopPurchaseEvent : TelemetryEvent
{
    // Weapon · Part · Stat · Ammo · SlotUpgrade
    public string itemType;
    public string itemId;
    public bool success;
    // 실패 사유(성공이면 빈 문자열).
    public string reason;
    public int price;
    public int balanceBefore;
    public int balance;
}

[Serializable]
public sealed class WeaponChangeEvent : TelemetryEvent
{
    public string weaponId;
    public int slot;
    // 교체로 바닥에 놓인 무기(없으면 빈 문자열).
    public string replacedWeaponId;
    public int magazine;
    public string reason;
}

[Serializable]
public sealed class ShotEvent : TelemetryEvent
{
    public int shotId;
    public string weaponId;
    public int hand;
    public int pellets;
    public int magazineAmmo;
    public int reserveAmmo;
    public bool akimbo;
    public bool aiming;
}

[Serializable]
public sealed class HitEvent : TelemetryEvent
{
    public int shotId;
    public string weaponId;
    public float damage;
    public bool headshot;
    public bool helmet;
    public bool killed;
    public bool staggered;
}

[Serializable]
public sealed class KillEvent : TelemetryEvent
{
    public string enemyKind;
    public string enemyWeaponId;
    public int reward;
    public int killsThisWave;
    public int totalKills;
    public int balance;
}

[Serializable]
public sealed class PlayerDamagedEvent : TelemetryEvent
{
    public float damage;
    public float health;
    public float maxHealth;
    public bool lethal;
}
