using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 슬롯 현물 보상(v4) 결정적 검사. 난수는 고정 롤 큐로 넣어 항상 같은 결과가 나온다.
// 검사 범위: 6^3=216 릴 조합 전부, 탄약 3줄·꽝 탄약 계획, 파츠 풀(휴대 70%/시대 30%), 다음 시대 대체, 중복 파츠, 회전 시작 웨이브 스냅샷, 탄약 한도,
// 다음 시대 부재/마지막 시대 안내 문구 및 파츠/현금 대체, 이전 시대 무기 휴대 시 현 시대 대체 파츠 한정 추첨, 결제/결과 지급 재진입 가드(Interact 및 OnSpinStopped).
// 메뉴 POC/Verify Slot Reward. PocVerification.RunUnitChecks도 RunModelChecks를 부른다.
[InitializeOnLoad]
public static class PocSlotRewardVerification
{
    private const string TAG = "[PocSlotRewardVerification]";

    static PocSlotRewardVerification()
    {
        EditorApplication.delayCall += () => Run();
    }

    [MenuItem("POC/Verify Slot Reward")]
    public static void Run()
    {
        int failures = RunModelChecks();
        string logMsg = $"{TAG} finished: failures={failures}";
        Debug.Log(logMsg);
        try
        {
            string logDir = Path.GetFullPath(Path.Combine(Application.dataPath, "Logs"));
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            File.WriteAllText(Path.Combine(logDir, "PocSlotRewardVerification.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {logMsg}\n");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{TAG} could not write log file: {ex.Message}");
        }
    }

    public static int RunModelChecks()
    {
        int failures = 0;
        var table = TableManager.Initialize();
        var slot = table.SlotMachine;

        CheckFaceSweep(slot, ref failures);
        CheckTripleAmmo(table, ref failures);
        CheckMissAmmo(table, ref failures);
        CheckParts(table, ref failures);
        CheckNextEraFallback(table, ref failures);
        CheckSnapshotWave(table, ref failures);
        CheckApplyCapAndDuplicates(table, ref failures);
        CheckDescribe(table, ref failures);
        CheckReentrantDuplication(table, ref failures);

        Debug.Log($"{TAG} model checks failures={failures}");
        return failures;
    }

    // 6^3 = 216 조합 전부. 현금 배수는 3줄 소총·철모·인식표·7(4조합)과 인식표 2줄(15조합)뿐이고, 꽝(isMiss)은 195조합이다.
    private static void CheckFaceSweep(SlotMachineData _slot, ref int _failures)
    {
        int payingCombos = 0;
        int ammoRefills = 0;
        int nearMisses = 0;
        int misses = 0;
        int partSymbols = 0;
        int nextEraCount = 0;
        bool rulesHold = true;
        for (int a = 0; a < 6; a++)
        for (int b = 0; b < 6; b++)
        for (int c = 0; c < 6; c++)
        {
            var o = SlotMachineInfo.Evaluate(_slot, new[] { a, b, c });
            bool triple = a == b && b == c;
            bool dogtagPair = !triple && o.pairFace == 3 && o.multiplier == 1;
            if (o.multiplier > 0) payingCombos++;
            if (o.hasAmmo) ammoRefills++;
            if (o.isNearMiss) nearMisses++;
            if (o.isMiss) misses++;
            if (o.partCount > 0) partSymbols++;
            if (o.nextEraPartCount > 0) nextEraCount++;

            // 탄약은 3줄에서만 나온다. 2줄 배수는 인식표뿐이다. 파츠는 3줄 소총·철모·파츠·7에서만 나온다(인식표는 없다).
            bool expectAmmo = triple && a == 4;
            bool expectPay = (triple && (a == 0 || a == 1 || a == 3 || a == 5)) || dogtagPair;
            bool expectPartCount = triple && (a == 0 || a == 1 || a == 2 || a == 5);
            bool expectNext = triple && a == 5;
            bool expectMiss = !(triple || dogtagPair);
            if (o.hasAmmo != expectAmmo || (o.multiplier > 0) != expectPay || o.isMiss != expectMiss ||
                (o.partCount > 0) != expectPartCount || (o.nextEraPartCount > 0) != expectNext || (dogtagPair && o.partCount != 0))
            {
                rulesHold = false;
                Debug.LogError($"{TAG} face combo {a}{b}{c} breaks v4 rules");
            }
        }
        Check(rulesHold, "216 face combinations follow v4 rules (per-combo)", ref _failures);
        Check(payingCombos == 19, $"paying combos = 4 cash triples + 15 dogtag pairs (got {payingCombos})", ref _failures);
        Check(ammoRefills == 1, $"ammo refill combos = triple ammo only (got {ammoRefills})", ref _failures);
        Check(misses == 195, $"miss combos = 216 - 21 (got {misses})", ref _failures);
        Check(nearMisses == 75, $"non-paying pairs = 90 - 15 dogtag pairs (got {nearMisses})", ref _failures);
        Check(partSymbols == 4 && nextEraCount == 1, "part grants only on triple rifle/helmet/part/seven; next-era part only on seven", ref _failures);
    }

    private static void CheckTripleAmmo(TableManager _table, ref int _failures)
    {
        var slot = _table.SlotMachine;
        var ammo = new AmmoInventory(_table.Ammo);
        var ctx = Context(_table, 1, ammo, "m1873", "win1886");
        // 휴대 탄종 colt45(36/500), wcf4065(0/500)은 둘 다 덜 찼다. 롤 0.99 -> 두 번째(wcf4065).
        var reward = SlotRewardInfo.Plan(slot, Outcome(4, 4, 4), ctx, Rolls(0.99f));
        Check(reward.ammoId == "wcf4065" && reward.ammoRefill && reward.ammoAmount == 500 && reward.cash == 0,
            "triple ammo refills a carried ammo type to full reserve (wcf4065 +500), no cash", ref _failures);

        ammo.Add("colt45", 10000);
        ammo.Add("wcf4065", 10000);
        // 휴대 탄종이 모두 가득: 현재 시대(1887)의 덜 찬 탄종(ga12)을 채운다.
        var refillEra = SlotRewardInfo.Plan(slot, Outcome(4, 4, 4), Context(_table, 1, ammo, "m1873", "win1886"), Rolls(0f));
        Check(refillEra.ammoId == "ga12" && refillEra.ammoRefill && refillEra.ammoAmount == 500 && refillEra.cash == 0,
            "triple ammo with full carried ammo refills non-full current-era stock (ga12)", ref _failures);

        // 현재 시대 탄종(colt45, wcf4065, ga12)이 모두 가득: 현금 $5 대체.
        ammo.Add("ga12", 10000);
        var full = SlotRewardInfo.Plan(slot, Outcome(4, 4, 4), Context(_table, 1, ammo, "m1873", "win1886"), Rolls(0f));
        Check(full.ammoId == null && full.cash == slot.fullAmmoCash && full.cash == 5,
            "when no current-era ammo can refill, pay $5 instead", ref _failures);

        // 두 휴대 무기가 같은 탄종이면 중복 없이 한 번만 고려한다(m1873 colt45 + 같은 colt45 무기 없음 -> 하나).
        var single = new AmmoInventory(_table.Ammo);
        var one = SlotRewardInfo.Plan(slot, Outcome(4, 4, 4), Context(_table, 1, single, "m1873", "m1873"), Rolls(0.9f));
        Check(one.ammoId == "colt45", "duplicate carried weapons count their ammo once", ref _failures);

        // 탄약 3줄은 한 탄종의 예비탄만 채운다(그 외 탄종 불변).
        var other = new AmmoInventory(_table.Ammo);
        int before = other.GetReserve("ga12");
        SlotRewardInfo.Apply(SlotRewardInfo.Plan(slot, Outcome(4, 4, 4), Context(_table, 1, other, "m1873"), Rolls(0f)), other, null, null);
        Check(other.GetReserve("colt45") == 500 && other.GetReserve("ga12") == before, "triple ammo changes only the chosen ammo type", ref _failures);

        // 현재 시대 풀 선택: 휴대 무기가 없으면 현재 시대 탄종 풀(웨이브 1: 3종)에서 고른다. 롤 0.9 -> index 2 -> ga12.
        var none = new AmmoInventory(_table.Ammo);
        var pool = SlotRewardInfo.Plan(slot, Outcome(4, 4, 4), Context(_table, 1, none), Rolls(0.9f));
        Check(pool.ammoId == "ga12", "triple ammo without carried weapons picks from current-era pool (wave 1 => ga12)", ref _failures);
    }

    private static void CheckMissAmmo(TableManager _table, ref int _failures)
    {
        var slot = _table.SlotMachine;
        // 기준 탄창 1개(referenceMagazineSize) 지급: colt45(6)->6, wcf4065(8)->8, b303(10)->10, ga12(5)->5, acp45(50)->50.
        var ammo = new AmmoInventory(_table.Ammo);
        var carried = SlotRewardInfo.Plan(slot, Outcome(1, 2, 5), Context(_table, 1, ammo, "m1873", "win1886"), Rolls(0.1f, 0.99f));
        Check(carried.ammoId == "wcf4065" && carried.ammoAmount == 8 && carried.cash == 0, "miss picks carried ammo (70%) and gives referenceMagazineSize 8", ref _failures);

        var era = SlotRewardInfo.Plan(slot, Outcome(1, 2, 5), Context(_table, 6, ammo, "m1873"), Rolls(0.9f, 0.5f));
        Check(era.ammoId == "b303" && era.ammoAmount == 10, "miss uses current-era pool 30% (wave 6, b303 => referenceMagazineSize 10)", ref _failures);

        var big = SlotRewardInfo.Plan(slot, Outcome(1, 2, 5), Context(_table, 11, ammo, "mosin"), Rolls(0.95f, 0f));
        Check(big.ammoId == "acp45" && big.ammoAmount == 50, "miss at wave 11 uses WW2 pool (acp45 => referenceMagazineSize 50)", ref _failures);

        var min = SlotRewardInfo.Plan(slot, Outcome(1, 2, 5), Context(_table, 1, ammo, "m1873"), Rolls(0.1f, 0f));
        Check(min.ammoAmount >= 6, "miss amount is at least referenceMagazineSize 6", ref _failures);

        // 가득 찬 탄종이 선택되면 $5. 탄약 이벤트 없음.
        var full = new AmmoInventory(_table.Ammo);
        full.Add("colt45", 10000);
        var fullMiss = SlotRewardInfo.Plan(slot, Outcome(1, 2, 5), Context(_table, 1, full, "m1873"), Rolls(0.1f, 0f));
        Check(fullMiss.ammoId == null && fullMiss.cash == 5, "miss with full selected ammo pays $5 instead", ref _failures);

        // 2줄 탄약(같은 탄약 두 개)은 꽝과 같다: 보급은 있지만 현금 보너스는 없다.
        var pairAmmo = SlotRewardInfo.Plan(slot, Outcome(4, 4, 0), Context(_table, 1, ammo, "m1873"), Rolls(0.1f, 0f));
        Check(pairAmmo.cash == 0 && pairAmmo.ammoId != null && pairAmmo.ammoAmount == 6 && !pairAmmo.ammoRefill, "ammo pair is a plain miss with no cash bonus", ref _failures);

        // 가득 찬 탄종 + 실제 지급: 예비탄 한도를 넘기지 않는다.
        var near = new AmmoInventory(_table.Ammo);
        near.Add("acp45", 497);
        var capped = SlotRewardInfo.Plan(slot, Outcome(1, 2, 5), Context(_table, 11, near, "thompson"), Rolls(0.1f, 0f));
        Check(capped.ammoId == "acp45" && capped.ammoAmount == 3, "miss amount (50) is clipped to the remaining room (3) in the plan", ref _failures);
    }

    private static void CheckParts(TableManager _table, ref int _failures)
    {
        var slot = _table.SlotMachine;
        var ammo = new AmmoInventory(_table.Ammo);
        // 휴대 풀(m1873·win1886 파츠 6개): 롤 0.1 -> 휴대 풀, 롤 0 -> 첫 파츠 m1873_nickel.
        var carriedRifle = SlotRewardInfo.Plan(slot, Outcome(0, 0, 0), Context(_table, 1, ammo, "m1873", "win1886"), Rolls(0.1f, 0f));
        Check(carriedRifle.partIds.Count == 1 && carriedRifle.partIds[0] == "m1873_nickel", "rifle triple grants a part from held weapons on the 70% roll", ref _failures);

        // 시대 풀(1887 파츠 9개, 테이블 순서): 롤 0.95 -> 시대 풀, 롤 0.99 -> 마지막 colt1878_ivory_grip.
        var eraRifle = SlotRewardInfo.Plan(slot, Outcome(0, 0, 0), Context(_table, 1, ammo, "m1873", "win1886"), Rolls(0.95f, 0.99f));
        Check(eraRifle.partIds.Count == 1 && eraRifle.partIds[0] == "colt1878_ivory_grip", "rifle triple 30% draws from the current-era pool (roll 0.99 -> last part)", ref _failures);

        // 휴대 무기가 없으면 시대 풀로 바로 간다(롤 0 -> 첫 파츠 m1873_nickel).
        var noHeld = SlotRewardInfo.Plan(slot, Outcome(1, 1, 1), Context(_table, 1, ammo), Rolls(0.1f, 0f));
        Check(noHeld.partIds.Count == 1 && noHeld.partIds[0] == "m1873_nickel", "no held weapon falls back to the era pool", ref _failures);

        // 인식표 3줄·2줄 어디에도 파츠는 없다.
        var dogtag = SlotRewardInfo.Plan(slot, Outcome(3, 3, 3), Context(_table, 1, ammo, "m1873"), Rolls(0.1f, 0f));
        var dogtagPair = SlotRewardInfo.Plan(slot, Outcome(3, 3, 5), Context(_table, 1, ammo, "m1873"), Rolls(0.1f, 0f));
        Check(dogtag.partIds.Count == 0 && dogtag.cash == 0 && dogtagPair.partIds.Count == 0, "dogtag never grants a part", ref _failures);

        // 파츠 3줄 현 시대 1개, 7은 현 시대 + 다음 시대 1개.
        var part = SlotRewardInfo.Plan(slot, Outcome(2, 2, 2), Context(_table, 1, ammo, "m1873"), Rolls(0.1f, 0f));
        Check(part.partIds.Count == 1 && part.cash == 0 && part.ammoId == null, "part triple grants exactly one part, no cash, no ammo", ref _failures);
    }

    private static void CheckNextEraFallback(TableManager _table, ref int _failures)
    {
        var slot = _table.SlotMachine;
        var ammo = new AmmoInventory(_table.Ammo);

        // 1. 기본 테이블: 웨이브 1(1887)의 다음 시대 WW1에는 파츠가 없다: 7은 현 시대 파츠 2개(1887 풀에서 직접)로 채우고 대체를 표시한다.
        // 롤 3개: 1887 파츠(AddPart: 풀 0.1f -> carried, pick 0f -> m1873_nickel), 다음 시대 대체(AddNextEraPart: 1887 풀에서 직접 pick 0.99f -> colt1878_ivory_grip).
        var seven = SlotRewardInfo.Plan(slot, Outcome(5, 5, 5), Context(_table, 1, ammo, "m1873", "win1886"), Rolls(0.1f, 0f, 0.99f));
        Check(seven.partIds.Count == 2 && seven.nextEraFallback && !seven.lastEraFallback && !seven.nextEraFallbackCash && seven.cash == 0,
            "seven with missing WW1 parts substitutes a current-era part (2 parts, nextEraFallback true, lastEraFallback false)", ref _failures);
        Check(seven.partIds.TrueForAll(id => IsPartOf(_table, id, "m1873", "win1886", "colt1878")),
            "seven substitute parts all come from the 1887 pool", ref _failures);
        string sevenText = SlotRewardInfo.Describe(seven, _table.Shop, _table.Part, _table.Ammo);
        Check(sevenText.Contains("(다음 시대 파츠 없음: 현 시대 파츠로 대체)"),
            $"absent next era notice shows nextEraFallback text ({sevenText})", ref _failures);

        // 2. [결함 회귀 검사 1 - 이전 시대 파츠 휴대, 다음 시대 풀 비어 있음, 현 시대 풀 존재]:
        //    현 시대 대체 파츠가 휴대 무기(이전 시대) 풀로 새지 않고 현 시대 풀(smle)에서만 직추첨되어야 한다.
        var syntheticParts = new PartData
        {
            parts = new[]
            {
                new PartData.Entry { id = "m1873_nickel", weaponId = "m1873", name = "니켈 도금" },         // 1887 (이전 시대)
                new PartData.Entry { id = "smle_ww1_scope", weaponId = "smle", name = "WW1 조준경" },        // WW1 (현 시대)
            }
        };
        // 웨이브 6은 WW1 시대. 다음 시대는 WW2(startWave 11). WW2에는 파츠가 없으므로 다음 시대 풀은 비어 있다.
        // 플레이어는 이전 시대 무기 m1873을 휴대 중.
        var carriedOlderCtx = new SlotRewardInfo.Context
        {
            wave = 6,
            shop = _table.Shop,
            weapons = _table.Weapon,
            parts = syntheticParts,
            ammoTable = _table.Ammo,
            ammo = ammo,
            carriedWeaponIds = new List<string> { "m1873" }
        };
        // 롤 3개:
        // AddPart: 롤 0.9f (>0.7 -> 시대 풀 선택), 롤 0f -> smle_ww1_scope.
        // AddNextEraPart: 다음 시대(WW2) 비어 있음 -> 현 시대 풀(smle_ww1_scope)에서 직추첨.
        // 만약 구버전 결함(AddPart 호출)이 발생하면 롤 0.01f (<0.7)에서 휴대 무기(1887 m1873_nickel)를 뽑아 규칙을 위반하게 됨.
        var regressionRolls = Rolls(0.9f, 0f, 0.01f);
        var regressionReward = SlotRewardInfo.Plan(slot, Outcome(5, 5, 5), carriedOlderCtx, regressionRolls);
        Check(regressionReward.partIds.Count == 2 &&
              regressionReward.partIds[0] == "smle_ww1_scope" &&
              regressionReward.partIds[1] == "smle_ww1_scope" &&
              regressionReward.nextEraFallback &&
              !regressionReward.lastEraFallback,
              "next-era fallback draws directly from current-era pool and NEVER chooses carried older-era parts", ref _failures);

        // 3. [마지막 시대 검사 (Populated Current Era)]:
        //    마지막 시대(WW2, wave 11)에서는 다음 시대가 존재하지 않는다(ShopInfo.NextEra == null).
        //    휴대 무기가 이전 시대 무기(m1873)이더라도 현 시대 풀(thompson)에서만 직접 뽑고 lastEraFallback을 표시해야 한다.
        var lastEraParts = new PartData
        {
            parts = new[]
            {
                new PartData.Entry { id = "m1873_nickel", weaponId = "m1873", name = "니켈 도금" },         // 1887 (이전 시대)
                new PartData.Entry { id = "thompson_drum", weaponId = "thompson", name = "드럼 탄창" },     // WW2 (마지막 시대)
            }
        };
        var lastEraCtx = new SlotRewardInfo.Context
        {
            wave = 11,
            shop = _table.Shop,
            weapons = _table.Weapon,
            parts = lastEraParts,
            ammoTable = _table.Ammo,
            ammo = ammo,
            carriedWeaponIds = new List<string> { "m1873" }
        };
        // AddPart: 롤 0.9f -> 시대 풀, 롤 0f -> thompson_drum
        // AddNextEraPart: 마지막 시대 -> 현 시대 풀(thompson_drum)에서 직추첨, 롤 0.01f -> thompson_drum
        var lastEraReward = SlotRewardInfo.Plan(slot, Outcome(5, 5, 5), lastEraCtx, Rolls(0.9f, 0f, 0.01f));
        Check(lastEraReward.partIds.Count == 2 &&
              lastEraReward.partIds[0] == "thompson_drum" &&
              lastEraReward.partIds[1] == "thompson_drum" &&
              lastEraReward.lastEraFallback &&
              !lastEraReward.nextEraFallback &&
              !lastEraReward.nextEraFallbackCash,
              "last era seven draws substitute from current era only, setting lastEraFallback and not nextEraFallback", ref _failures);
        string lastEraText = SlotRewardInfo.Describe(lastEraReward, _table.Shop, lastEraParts, _table.Ammo);
        Check(lastEraText.Contains("(마지막 시대: 현 시대 파츠로 대체)"),
            $"last era notice shows last era text ({lastEraText})", ref _failures);

        // 4. [빈 현 시대 검사 A - 다음 시대 부재 + 현 시대 파츠도 없음]:
        //    웨이브 6(WW1)인데 WW1 파츠도 없고 다음 시대(WW2) 파츠도 없음 -> 부품 자리마다 $5 현금 대체.
        var emptyParts = new PartData { parts = Array.Empty<PartData.Entry>() };
        var emptyCurrentCtx = new SlotRewardInfo.Context
        {
            wave = 6,
            shop = _table.Shop,
            weapons = _table.Weapon,
            parts = emptyParts,
            ammoTable = _table.Ammo,
            ammo = ammo,
            carriedWeaponIds = new List<string> { "m1873" }
        };
        var emptyCurrentReward = SlotRewardInfo.Plan(slot, Outcome(5, 5, 5), emptyCurrentCtx, Rolls(0.1f, 0f));
        Check(emptyCurrentReward.partIds.Count == 0 &&
              emptyCurrentReward.cash == 2 * slot.fullAmmoCash &&
              emptyCurrentReward.nextEraFallback &&
              emptyCurrentReward.nextEraFallbackCash &&
              !emptyCurrentReward.lastEraFallback,
              "empty next-era and empty current-era substitutes cash for both parts with nextEraFallbackCash", ref _failures);
        string emptyCurrentText = SlotRewardInfo.Describe(emptyCurrentReward, _table.Shop, emptyParts, _table.Ammo);
        Check(emptyCurrentText.Contains("(다음 시대 파츠 없음: 현금으로 대체)") && emptyCurrentText.Contains("+$10"),
            $"absent next era cash substitute notice ({emptyCurrentText})", ref _failures);

        // 5. [빈 현 시대 검사 B - 마지막 시대 + 현 시대 파츠도 없음]:
        //    웨이브 11(WW2)인데 WW2 파츠 없음 -> 현금 $5 대체 및 lastEraFallback + nextEraFallbackCash.
        var emptyLastCtx = new SlotRewardInfo.Context
        {
            wave = 11,
            shop = _table.Shop,
            weapons = _table.Weapon,
            parts = emptyParts,
            ammoTable = _table.Ammo,
            ammo = ammo,
            carriedWeaponIds = new List<string> { "mosin" }
        };
        var emptyLastReward = SlotRewardInfo.Plan(slot, Outcome(5, 5, 5), emptyLastCtx, Rolls(0.1f, 0f));
        Check(emptyLastReward.partIds.Count == 0 &&
              emptyLastReward.cash == 2 * slot.fullAmmoCash &&
              emptyLastReward.lastEraFallback &&
              emptyLastReward.nextEraFallbackCash &&
              !emptyLastReward.nextEraFallback,
              "last era with empty current era substitutes cash with lastEraFallback and nextEraFallbackCash", ref _failures);
        string emptyLastText = SlotRewardInfo.Describe(emptyLastReward, _table.Shop, emptyParts, _table.Ammo);
        Check(emptyLastText.Contains("(마지막 시대: 현금으로 대체)") && emptyLastText.Contains("+$10"),
            $"last era cash substitute notice ({emptyLastText})", ref _failures);

        // 6. 정상 다음 시대 추첨: 다음 시대 파츠가 있으면 휴대 무기(1887)가 있어도 다음 시대 풀에서만 뽑는다.
        var normalParts = new PartData
        {
            parts = new[]
            {
                new PartData.Entry { id = "m1873_nickel", weaponId = "m1873", name = "니켈 도금" },
                new PartData.Entry { id = "smle_synthetic_scope", weaponId = "smle", name = "합성 조준경" },
            }
        };
        var normalCtx = new SlotRewardInfo.Context
        {
            wave = 1,
            shop = _table.Shop,
            weapons = _table.Weapon,
            parts = normalParts,
            ammoTable = _table.Ammo,
            ammo = ammo,
            carriedWeaponIds = new List<string> { "m1873" }
        };
        var normalReward = SlotRewardInfo.Plan(slot, Outcome(5, 5, 5), normalCtx, Rolls(0.1f, 0f, 0.1f));
        Check(normalReward.partIds.Count == 2 &&
              normalReward.partIds[0] == "m1873_nickel" &&
              normalReward.partIds[1] == "smle_synthetic_scope" &&
              !normalReward.nextEraFallback &&
              !normalReward.lastEraFallback,
              "next-era award draws exclusively from next-era pool when populated", ref _failures);
    }

    // 회전 시작 웨이브를 넘긴다. 정비 중 지급이 다음 웨이브 시대를 쓰면 안 된다.
    private static void CheckSnapshotWave(TableManager _table, ref int _failures)
    {
        var slot = _table.SlotMachine;
        var ammo = new AmmoInventory(_table.Ammo);
        // 웨이브 5(1887): 현재 시대(1887) 탄종 3종(colt45, wcf4065, ga12) -> 롤 0.5 -> wcf4065.
        var at5 = SlotRewardInfo.Plan(slot, Outcome(4, 4, 4), Context(_table, 5, ammo), Rolls(0.5f));
        // 같은 결과를 웨이브 6(WW1, 다음 시대) 기준으로 계획하면 WW1 탄종(p9x19, b303, ga12) -> 롤 0.5 -> b303.
        var at6 = SlotRewardInfo.Plan(slot, Outcome(4, 4, 4), Context(_table, 6, ammo), Rolls(0.5f));
        Check(at5.ammoId == "wcf4065" && at6.ammoId == "b303", "triple ammo uses the spin wave's current-era pool (5 -> wcf4065, 6 -> b303)", ref _failures);
        var partAt5 = SlotRewardInfo.Plan(slot, Outcome(0, 0, 0), Context(_table, 5, ammo), Rolls(0.95f, 0.99f));
        Check(partAt5.partIds.Count == 1 && partAt5.partIds[0] == "colt1878_ivory_grip", "part era pool is the spin wave's era (1887 at wave 5)", ref _failures);
    }

    private static void CheckApplyCapAndDuplicates(TableManager _table, ref int _failures)
    {
        var slot = _table.SlotMachine;
        var ammo = new AmmoInventory(_table.Ammo);
        var parts = new PartInventory(_table.Part);
        var wallet = new PlayerWallet(0);

        // 같은 파츠 두 개: 중복 보유, 둘 다 보관함(장착 안 함).
        var dup = SlotRewardInfo.Plan(slot, Outcome(0, 0, 0), Context(_table, 1, ammo, "m1873"), Rolls(0.1f, 0f));
        string dupId = dup.partIds[0];
        SlotRewardInfo.Apply(dup, ammo, parts, wallet);
        SlotRewardInfo.Apply(dup, ammo, parts, wallet);
        Check(parts.OwnedCount(dupId) == 2 && parts.StoredCount(dupId) == 2 && parts.AttachedCount(dupId) == 0,
            "duplicate part grants stack in storage and are not auto-attached", ref _failures);

        // 탄약 한도: 남은 자리보다 많이 줘도 최대치를 넘지 않는다.
        ammo.Add("colt45", 10000);
        ammo.Consume("colt45", 3);
        var missCapped = SlotRewardInfo.Plan(slot, Outcome(1, 2, 5), Context(_table, 1, ammo, "m1873"), Rolls(0.1f, 0f));
        SlotRewardInfo.Apply(missCapped, ammo, parts, wallet);
        Check(ammo.GetReserve("colt45") == ammo.GetMaximum("colt45"), "giving 1 magazine (6) when 3 are missing clamps to maximum", ref _failures);

        ammo.Consume("colt45", 10);
        var miss = SlotRewardInfo.Plan(slot, Outcome(1, 2, 5), Context(_table, 1, ammo, "m1873"), Rolls(0.1f, 0f));
        SlotRewardInfo.Apply(miss, ammo, parts, wallet);
        Check(ammo.GetReserve("colt45") == ammo.GetMaximum("colt45") - 4, "apply grants the planned miss ammo (6) and stays under the cap", ref _failures);
        ammo.Add("colt45", 10000);
        Check(ammo.GetReserve("colt45") == ammo.GetMaximum("colt45"), "adding beyond the cap clamps to maximum", ref _failures);

        // 현금 $5 지급은 지갑에 들어간다(현재 시대 탄종이 모두 가득 찬 경우).
        ammo.Add("wcf4065", 10000);
        ammo.Add("ga12", 10000);
        var cash = SlotRewardInfo.Plan(slot, Outcome(4, 4, 4), Context(_table, 1, ammo, "m1873"), Rolls(0.1f, 0f));
        int balance = wallet.Balance;
        SlotRewardInfo.Apply(cash, ammo, parts, wallet);
        Check(wallet.Balance == balance + cash.cash && cash.cash == slot.fullAmmoCash, "cash substitute reaches the wallet", ref _failures);
    }

    private static void CheckDescribe(TableManager _table, ref int _failures)
    {
        var slot = _table.SlotMachine;
        var ammo = new AmmoInventory(_table.Ammo);
        var reward = SlotRewardInfo.Plan(slot, Outcome(0, 0, 0), Context(_table, 1, ammo, "win1886"), Rolls(0.1f, 0f));
        string text = SlotRewardInfo.Describe(reward, _table.Shop, _table.Part, _table.Ammo);
        var part = PartInfo.Find(_table.Part, reward.partIds[0]);
        int price = ShopInfo.PartPrice(_table.Shop, part);
        Check(text.Contains(part.name) && text.Contains($"${price:N0} 상당"), $"part text shows display name and shop price ({text})", ref _failures);
        Check(SlotRewardInfo.Describe(new SlotRewardInfo.Reward(), _table.Shop, _table.Part, _table.Ammo) == "", "empty reward has no text", ref _failures);

        // 대체 안내 문구 4종 검증
        var rNextPart = new SlotRewardInfo.Reward { nextEraFallback = true };
        rNextPart.partIds.Add("m1873_nickel");
        Check(SlotRewardInfo.Describe(rNextPart, _table.Shop, _table.Part, _table.Ammo).Contains("(다음 시대 파츠 없음: 현 시대 파츠로 대체)"),
            "describe notice for absent next era with part substitute", ref _failures);

        var rNextCash = new SlotRewardInfo.Reward { nextEraFallback = true, nextEraFallbackCash = true, cash = 5 };
        Check(SlotRewardInfo.Describe(rNextCash, _table.Shop, _table.Part, _table.Ammo).Contains("(다음 시대 파츠 없음: 현금으로 대체)"),
            "describe notice for absent next era with cash substitute", ref _failures);

        var rLastPart = new SlotRewardInfo.Reward { lastEraFallback = true };
        rLastPart.partIds.Add("m1873_nickel");
        Check(SlotRewardInfo.Describe(rLastPart, _table.Shop, _table.Part, _table.Ammo).Contains("(마지막 시대: 현 시대 파츠로 대체)"),
            "describe notice for last era with part substitute", ref _failures);

        var rLastCash = new SlotRewardInfo.Reward { lastEraFallback = true, nextEraFallbackCash = true, cash = 5 };
        Check(SlotRewardInfo.Describe(rLastCash, _table.Shop, _table.Part, _table.Ammo).Contains("(마지막 시대: 현금으로 대체)"),
            "describe notice for last era with cash substitute", ref _failures);
    }

    private static void CheckReentrantDuplication(TableManager _table, ref int _failures)
    {
        var root = new GameObject("ReentrantTestRoot");
        root.SetActive(false);
        try
        {
            var gm = root.AddComponent<GameManager>();
            var reels = root.AddComponent<SlotMachineReels>();
            var presenter = root.AddComponent<SlotMachinePresenter>();

            typeof(GameManager).GetField("wallet", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(gm, new PlayerWallet(100));
            typeof(GameManager).GetField("perks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(gm, new PerkInventory(_table.Perk));
            typeof(GameManager).GetField("loadout", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(gm, new WeaponLoadout(2));
            typeof(GameManager).GetField("parts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(gm, new PartInventory(_table.Part));
            typeof(GameManager).GetField("ammo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(gm, new AmmoInventory(_table.Ammo));
            typeof(GameManager).GetField("phase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(gm, GameManager.GamePhase.Wave);

            var so = new SerializedObject(presenter);
            so.FindProperty("gameManager").objectReferenceValue = gm;
            so.FindProperty("reels").objectReferenceValue = reels;
            so.ApplyModifiedPropertiesWithoutUndo();

            var spinningField = typeof(SlotMachinePresenter).GetField("spinning", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var onSpinStoppedMethod = typeof(SlotMachinePresenter).GetMethod("OnSpinStopped", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            spinningField.SetValue(presenter, true);

            bool wasSpinningDuringPayout = false;
            bool canInteractDuringPayout = true;
            int reentrantInteractCalls = 0;
            int reentrantOnSpinStoppedCalls = 0;
            int spinResolvedCount = 0;

            var outcome = SlotMachineInfo.Evaluate(_table.SlotMachine, new[] { 0, 0, 0 });

            // Path 1 (Interact reentrancy) & Path 2 (OnSpinStopped reentrancy):
            // When Wallet.BalanceChanged fires during payout, attempt BOTH reentrant Interact and reentrant OnSpinStopped.
            gm.Wallet.BalanceChanged += (newBalance) =>
            {
                wasSpinningDuringPayout = presenter.IsSpinning;
                canInteractDuringPayout = presenter.CanInteract();

                // Path 1: Attempt reentrant Interact
                if (presenter.CanInteract())
                {
                    reentrantInteractCalls++;
                    presenter.Interact();
                }

                // Path 2: Attempt reentrant OnSpinStopped
                reentrantOnSpinStoppedCalls++;
                onSpinStoppedMethod.Invoke(presenter, new object[] { outcome, 1f, 1 });
            };

            bool wasSpinningBeforeResolved = true;
            presenter.SpinResolved += (resolvedOutcome, payout, text) =>
            {
                spinResolvedCount++;
                wasSpinningBeforeResolved = presenter.IsSpinning;
            };

            int initialBalance = gm.Wallet.Balance;
            onSpinStoppedMethod.Invoke(presenter, new object[] { outcome, 1f, 1 });

            Check(wasSpinningDuringPayout, "presenter remains spinning during payout and grant effects", ref _failures);
            Check(!canInteractDuringPayout, "presenter cannot interact during payout and grant effects", ref _failures);
            Check(reentrantInteractCalls == 0, "Path 1 regression: reentrant Interact call was blocked during payout", ref _failures);
            Check(reentrantOnSpinStoppedCalls > 0, "Path 2 setup: reentrant OnSpinStopped was attempted during wallet payout", ref _failures);
            Check(spinResolvedCount == 1, "Path 2 regression: reentrant OnSpinStopped was blocked by payoutInProgress (SpinResolved fired exactly once)", ref _failures);
            Check(!wasSpinningBeforeResolved, "spinning is set to false before SpinResolved fires", ref _failures);
            Check(!presenter.IsSpinning, "presenter is not spinning after completion", ref _failures);

            int expectedPayout = SlotMachineInfo.SpinCost(_table.SlotMachine) * outcome.multiplier;
            Check(gm.Wallet.Balance == initialBalance + expectedPayout,
                $"wallet only received a single payout without duplicate from reentrancy (got {gm.Wallet.Balance - initialBalance}, expected {expectedPayout})", ref _failures);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // ---------- helpers ----------

    private static SlotOutcome Outcome(int _a, int _b, int _c)
    {
        return SlotMachineInfo.Evaluate(TableManager.Instance.SlotMachine, new[] { _a, _b, _c });
    }

    private static SlotRewardInfo.Context Context(TableManager _table, int _wave, AmmoInventory _ammo, params string[] _carried)
    {
        return new SlotRewardInfo.Context
        {
            wave = _wave,
            shop = _table.Shop,
            weapons = _table.Weapon,
            parts = _table.Part,
            ammoTable = _table.Ammo,
            ammo = _ammo,
            carriedWeaponIds = new List<string>(_carried),
        };
    }

    // 주어진 롤을 차례로 돌려준다. 모자라면 0을 준다.
    private static Func<float> Rolls(params float[] _values)
    {
        var queue = new Queue<float>(_values);
        return () => queue.Count > 0 ? queue.Dequeue() : 0f;
    }

    private static bool IsPartOf(TableManager _table, string _partId, params string[] _weaponIds)
    {
        var part = PartInfo.Find(_table.Part, _partId);
        return part != null && Array.IndexOf(_weaponIds, part.weaponId) >= 0;
    }

    private static void Check(bool _value, string _message, ref int _failures)
    {
        if (_value)
        {
            return;
        }
        _failures++;
        Debug.LogError($"{TAG} FAIL {_message}");
    }
}
