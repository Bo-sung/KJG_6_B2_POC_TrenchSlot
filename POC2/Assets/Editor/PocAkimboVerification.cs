using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// CLI: -batchmode -nographics -executeMethod PocAkimboVerification.RunChecks (no -quit). 메뉴 POC/Verify Akimbo And Parts (Play).
// 저장된 Game을 실제 PlayerLoop에서 돌려 GameManager·PlayerWeapon 경로로 무기 슬롯·아킴보·파츠 개체를 검사한다. 제품 씬·테이블은 저장하지 않는다.
// 다루는 흐름: 슬롯 전환 연사 간격 유지, 같은 파츠 중복 구매·자동 장착·보관·장착/분리·이동 금지, 손별 파츠 효과, 아킴보 토글·단계 제한,
// 아킴보 중 슬롯 키 무시, 양손 번갈아 사격·손별 연사 간격, 양손 재장전(배율·적은 손 우선), 슬롯 탄약 보상이 재장전을 끝냄,
// 아킴보 중 교체 시 해제·바닥 무기 파츠 유지, 아킴보 불가 무기의 아킴보 기록 정리.
[InitializeOnLoad]
public static class PocAkimboVerification
{
    private const string RUNNING = "PocAkimboVerification.running";
    private const string TAG = "[PocAkimboVerification]";
    private const string M1873 = "m1873";
    private const string HEAVY = "m1873_heavy_barrel";
    private static readonly Stack<IEnumerator> STEPS = new Stack<IEnumerator>();
    private static int passes;
    private static int failures;
    private static int lastFrame = -1;
    private static bool initialized;
    private static float deadline;
    private static GameManager game;
    private static PlayerWeapon weapon;
    private static PlayerHealth player;

    static PocAkimboVerification()
    {
        EditorApplication.update += Tick;
    }

    [MenuItem("POC/Verify Akimbo And Parts (Play)")]
    public static void RunChecks()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
        SessionState.SetBool(RUNNING, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(RUNNING, false) || !EditorApplication.isPlaying || lastFrame == Time.frameCount)
        {
            return;
        }
        lastFrame = Time.frameCount;
        try
        {
            if (!initialized)
            {
                initialized = true;
                passes = 0;
                failures = 0;
                Time.captureDeltaTime = 1f / 60f;
                deadline = Time.realtimeSinceStartup + 150f;
                Application.logMessageReceived += OnLog;
                STEPS.Push(Scenarios());
            }
            if (Time.realtimeSinceStartup > deadline)
            {
                throw new TimeoutException("Akimbo checks exceeded 150 seconds.");
            }
            while (STEPS.Count > 0)
            {
                IEnumerator step = STEPS.Peek();
                if (!step.MoveNext())
                {
                    STEPS.Pop();
                    continue;
                }
                if (step.Current is IEnumerator nested)
                {
                    STEPS.Push(nested);
                    continue;
                }
                return;
            }
            Finish();
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogException(exception);
            Finish();
        }
    }

    private static IEnumerator Scenarios()
    {
        var tables = TableManager.Instance;
        game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        weapon = UnityEngine.Object.FindFirstObjectByType<PlayerWeapon>();
        player = UnityEngine.Object.FindFirstObjectByType<PlayerHealth>();
        Check(game != null && weapon != null && player != null, "Game 씬 GameManager·PlayerWeapon·PlayerHealth");
        yield return Wait(() => game.WaveNumber == 1 && game.Phase == GameManager.GamePhase.Wave, 3f, "1웨이브 시작");
        player.Configure(1000000f);
        var loadout = game.Loadout;
        var heavy = PartInfo.Find(tables.Part, HEAVY);
        var m1873 = WeaponInfo.Find(tables.Weapon, M1873);

        // 1) 슬롯 전환으로 연사 간격을 건너뛰지 못한다(무기 개체별 연사 시각).
        var first = loadout.Get(0);
        Check(first != null && first.WeaponId == M1873 && loadout.Get(1) == null, "시작 무기 M1873 한 자루");
        var second = new WeaponInstance(M1873, m1873.magazineSize);
        loadout.Put(1, second);
        first.Magazine = m1873.magazineSize;
        yield return Frames(1);
        Fire();
        Check(first.Magazine == m1873.magazineSize - 1, "1번 무기 발사");
        float firstReady = first.NextShotTime;
        Check(firstReady > Time.time, "1번 무기 연사 간격 기록");
        Check(game.SelectSlot(1), "2번 슬롯 전환");
        Fire();
        Check(second.Magazine == m1873.magazineSize - 1, "2번 무기는 자기 연사 간격으로 발사");
        Check(game.SelectSlot(0), "1번 슬롯으로 복귀");
        Check(Mathf.Approximately(first.NextShotTime, firstReady), "재구성(Configure) 후에도 1번 연사 시각 유지");
        Fire();
        Check(first.Magazine == m1873.magazineSize - 1, "복귀 직후에는 1번 연사 간격이 남아 발사 불가(전환 우회 차단)");
        yield return WaitUntilTime(firstReady + 0.02f);
        Fire();
        Check(first.Magazine == m1873.magazineSize - 2, "연사 간격이 지나면 다시 발사");

        // 정비 시간으로.
        game.Wallet.Add(5000);
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 3f, "정비 시간 진입");

        // 2) 같은 파츠 중복 구매: 빈 칸 자동 장착(사용 중 → 나머지), 다 차면 보관.
        var parts = game.Parts;
        int before = game.Wallet.Balance;
        int price = ShopInfo.PartPrice(tables.Shop, heavy);
        Check(game.TryBuyPart(HEAVY) == "" && game.TryBuyPart(HEAVY) == "" && game.TryBuyPart(HEAVY) == "", "무거운 총열 3개 구매");
        Check(game.Wallet.Balance == before - price * 3 && parts.OwnedCount(HEAVY) == 3, "개당 가격 차감, 3개 보유");
        var copyA = first.PartInstanceIn(heavy.slot);
        var copyB = second.PartInstanceIn(heavy.slot);
        Check(copyA != null && copyB != null && copyA != copyB && parts.StoredCount(HEAVY) == 1, "1·2번 무기에 서로 다른 개체가 붙고 하나는 보관");
        var settings0 = game.ResolveSettings(first);
        Check(Mathf.Approximately(settings0.damage, m1873.damage * heavy.damageMultiplier), "장착 개체 효과가 그 무기 스탯에 반영");

        // 3) 장착·분리·이동 금지.
        Check(game.TryDetachPart(copyB) == "" && copyB.IsStored && !second.HasPart(HEAVY) && first.HasPart(HEAVY), "2번 무기 개체만 분리 → 보관");
        Check(Mathf.Approximately(game.ResolveSettings(second).damage, m1873.damage), "분리한 무기는 기본 피해로");
        Check(game.TryAttachPart(copyB, 1) == "" && second.PartInstanceIn(heavy.slot) == copyB, "보관 개체를 2번에 다시 장착");
        string blocked = game.TryAttachPart(copyA, 1);
        Check(blocked != "" && copyA.AttachedTo == first && second.PartInstanceIn(heavy.slot) == copyB && first.HasPart(HEAVY),
            "같은 파츠가 붙은 무기로 개체를 옮기면 실패하고 원래 무기 파츠도 그대로");
        Check(game.TryAttachPart(HEAVY, 1) != "" && parts.StoredCount(HEAVY) == 1, "id로 장착도 이미 붙은 무기면 실패(보관 개체 유지)");
        // 손별 효과를 보기 위해 2번은 기본 상태로 둔다.
        Check(game.TryDetachPart(copyB) == "", "2번 파츠 분리(손별 효과 준비)");

        // 4) 아킴보 토글(정비 시간 전용) · 손별 효과 · 연사 간격 유지.
        first.Magazine = m1873.magazineSize;
        second.Magazine = m1873.magazineSize;
        yield return WaitUntilTime(Mathf.Max(first.NextShotTime, second.NextShotTime) + 0.02f);
        Fire();
        float mainReady = first.NextShotTime;
        Check(first.Magazine == m1873.magazineSize - 1, "아킴보 전 1번 발사");
        Check(game.CanAkimbo && game.TrySetAkimbo(true) == "" && game.IsAkimbo && weapon.IsAkimbo && weapon.HandCount == 2, "아킴보 ON");
        Check(weapon.Hand(0) == first && weapon.Hand(1) == second, "손 0 = 사용 중 슬롯, 손 1 = 다른 슬롯 개체");
        Check(Mathf.Approximately(weapon.HandSettings(0).damage, m1873.damage * heavy.damageMultiplier) && Mathf.Approximately(weapon.HandSettings(1).damage, m1873.damage),
            "손별 파츠 효과(손 0 무거운 총열, 손 1 기본)");
        Check(Mathf.Approximately(weapon.HandNextShotTime(0), mainReady), "아킴보 전환으로 손 0 연사 시각이 초기화되지 않음");
        Fire();
        Check(first.Magazine == m1873.magazineSize - 1 && second.Magazine == m1873.magazineSize, "차례인 손 0이 간격 대기 중이면 쏘지 않는다");
        var held = UnityEngine.Object.FindFirstObjectByType<WeaponHeldModels>();
        Check(held != null && held.HasOffHand(m1873.modelId) && OffHandShown(held), "왼손 외형 표시");
        Check(game.TrySetAkimbo(false) == "" && !weapon.IsAkimbo && !OffHandShown(held), "아킴보 OFF · 왼손 외형 숨김");
        Check(game.TryToggleAkimbo() == "" && game.IsAkimbo, "토글로 다시 ON");

        // 5) 웨이브 중: 아킴보 유지, 슬롯 키 무시, 정비 시간 밖 전환 불가.
        game.StartNextWave();
        yield return Wait(() => game.Phase == GameManager.GamePhase.Wave, 3f, "다음 웨이브 시작");
        yield return Frames(2);
        Check(game.IsAkimbo && weapon.IsAkimbo, "웨이브 시작 후 아킴보 유지");
        Check(!game.SelectSlot(0) && !game.SelectSlot(1) && game.IsAkimbo && weapon.IsAkimbo, "아킴보 중 슬롯 키는 무시(아킴보 유지)");
        Check(game.TrySetAkimbo(false) != "" && game.IsAkimbo, "웨이브 중에는 아킴보 전환 불가");

        // 6) 양손 번갈아 사격·손별 연사 간격.
        first.Magazine = m1873.magazineSize;
        second.Magazine = m1873.magazineSize;
        yield return WaitUntilTime(Mathf.Max(first.NextShotTime, second.NextShotTime) + 0.02f);
        Fire();
        Check(weapon.LastFiredHand == 0 && first.Magazine == m1873.magazineSize - 1, "아킴보 첫 발은 손 0");
        float now = Time.time;
        Check(Mathf.Approximately(first.NextShotTime - now, 1f / weapon.HandSettings(0).shotsPerSecond), "손 0 연사 간격 = 손 0 무기 연사");
        Fire();
        Check(weapon.LastFiredHand == 1 && second.Magazine == m1873.magazineSize - 1, "다음 발은 손 1(번갈아)");
        Check(Mathf.Approximately(second.NextShotTime - now, 1f / weapon.HandSettings(1).shotsPerSecond), "손 1 연사 간격 = 손 1 무기 연사");
        Fire();
        Check(first.Magazine == m1873.magazineSize - 1 && second.Magazine == m1873.magazineSize - 1, "양손 모두 간격 대기 중이면 발사 없음");
        second.Magazine = 0;
        yield return WaitUntilTime(first.NextShotTime + 0.02f);
        Fire();
        Fire();
        Check(first.Magazine == m1873.magazineSize - 2 && second.Magazine == 0, "한쪽이 비면 남은 손만 쏜다(간격 지킴)");

        // 7) 양손 리볼버는 더 비어 있는 손부터 한 발씩 장전한다.
        var ammo = game.Ammo;
        ammo.Consume(m1873.ammoId, ammo.GetReserve(m1873.ammoId));
        ammo.Add(m1873.ammoId, 3);
        first.Magazine = 4;
        second.Magazine = 1;
        weapon.StartReload();
        float expected = Mathf.Max(weapon.HandSettings(0).reloadSeconds, weapon.HandSettings(1).reloadSeconds) * WeaponInfo.AkimboReloadMultiplier(tables.Weapon) / weapon.HandMagazineSize(0);
        Check(weapon.IsReloading, "아킴보 재장전 시작");
        float end = Time.time + expected;
        yield return WaitUntilTime(end - 0.1f);
        Check(weapon.IsReloading && second.Magazine == 1, $"첫 탄 장전 시간({expected:0.##}초) 전에는 탄약을 소비하지 않음");
        yield return WaitUntilTime(end + 0.1f);
        Check(weapon.IsReloading && second.Magazine == 2 && ammo.GetReserve(m1873.ammoId) == 2,
            "첫 탄 한 발만 더 비어 있는 손에 장전");
        yield return WaitUntilTime(end + expected * 2f + 0.3f);
        Check(!weapon.IsReloading && second.Magazine == 4 && first.Magazine == 4 && ammo.GetReserve(m1873.ammoId) == 0,
            "예비탄 3발은 장전탄이 적은 손 1부터 채움");

        // 8) 슬롯 탄약 보상은 진행 중 재장전을 끝낸다(예비탄 소비 없음).
        ammo.Add(m1873.ammoId, 50);
        int reserve = ammo.GetReserve(m1873.ammoId);
        first.Magazine = 1;
        weapon.StartReload();
        Check(weapon.IsReloading, "재장전 시작(보상 전)");
        weapon.GrantSlotAmmo();
        Check(!weapon.IsReloading && first.Magazine == weapon.HandMagazineSize(0) && second.Magazine == weapon.HandMagazineSize(1) &&
            ammo.GetReserve(m1873.ammoId) == reserve, "슬롯 탄약 보상이 양손을 채우고 재장전을 끝냄");

        // 9) 아킴보 중 교체 구매: 아킴보 해제, 교체된 무기는 파츠를 단 채 바닥.
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 3f, "다시 정비 시간");
        Check(game.TryAttachPart(copyB, 1) == "", "2번에 파츠 다시 장착(바닥 확인 준비)");
        Check(game.TryBuyWeapon("win1886", 1) == "", "아킴보 중 2번 슬롯을 Winchester로 교체");
        Check(!game.IsAkimbo && !weapon.IsAkimbo && weapon.HandCount == 1 && weapon.Current == first, "교체 후 아킴보 해제, 1번만 손에");
        Check(!loadout.Contains(second) && copyB.AttachedTo == second && parts.OwnedCount(HEAVY) == 3, "바닥 무기에 파츠 개체가 붙은 채 남음");
        Check(game.TryDetachPart(copyB) != "" && game.TryAttachPart(copyB, 0) != "", "바닥 무기 개체는 주워야 분리·이동 가능");
        Check(!OffHandShown(held), "왼손 외형 숨김");

        // 10) 아킴보 불가 무기에 아킴보 기록이 남으면 손 구성에 맞춰 푼다.
        var winA = loadout.Get(1);
        loadout.Put(0, new WeaponInstance("win1886", 8));
        Check(loadout.SetAkimbo(true) && loadout.Akimbo, "모델 단계에서는 같은 종류 짝이면 기록됨(사전 조건)");
        Call(game, "ApplyActiveWeapon");
        Check(!loadout.Akimbo && !weapon.IsAkimbo && winA != null, "아킴보 불가 무기면 기록도 해제");
    }

    private static void Fire()
    {
        Call(weapon, "TryFire");
    }

    private static bool OffHandShown(WeaponHeldModels _held)
    {
        foreach (Transform child in _held.transform)
        {
            if (child.name.EndsWith("_OffHand", StringComparison.Ordinal) && child.gameObject.activeSelf)
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerator WaitUntilTime(float _time)
    {
        float limit = Time.realtimeSinceStartup + 20f;
        while (Time.time < _time && Time.realtimeSinceStartup < limit)
        {
            yield return null;
        }
    }

    private static object Call(object _owner, string _name, params object[] _args) =>
        _owner.GetType().GetMethod(_name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_owner, _args);

    private static void Set(object _owner, string _name, object _value)
    {
        _owner.GetType().GetField(_name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_owner, _value);
    }

    private static IEnumerator Frames(int _count)
    {
        for (int i = 0; i < _count; i++)
        {
            yield return null;
        }
    }

    private static IEnumerator Wait(Func<bool> _condition, float _seconds, string _label)
    {
        float end = Time.realtimeSinceStartup + _seconds;
        while (!_condition() && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        Check(_condition(), _label);
    }

    private static void OnLog(string _message, string _stack, LogType _type)
    {
        if ((_type == LogType.Error && !_message.StartsWith(TAG, StringComparison.Ordinal)) || _type == LogType.Exception)
        {
            failures++;
        }
    }

    private static void Check(bool _condition, string _label)
    {
        if (_condition)
        {
            passes++;
            Debug.Log($"{TAG} PASS: {_label}");
        }
        else
        {
            failures++;
            Debug.LogError($"{TAG} FAIL: {_label}");
        }
    }

    private static void Finish()
    {
        SessionState.SetBool(RUNNING, false);
        initialized = false;
        Time.captureDeltaTime = 0f;
        Application.logMessageReceived -= OnLog;
        STEPS.Clear();
        Debug.Log($"{TAG} finished play-mode frames={Time.frameCount}, passes={passes}, failures={failures}");
        if (Application.isBatchMode)
        {
            PocEditorExit.Exit(failures == 0 ? 0 : 1);
            return;
        }
        EditorApplication.ExitPlaymode();
    }
}
