using System;
using System.Text;
using UnityEngine;

// 슬롯머신 규칙(Presenter). 웨이브 중에만 회전할 수 있고, 비용 차감·릴 추첨·결과 판정·지급을 맡는다.
// 회전 중에는 카메라·이동·사격을 막지 않는다. 결과가 나오는 순간 정비 시간이어도 지급은 그대로 한다.
public sealed class SlotMachinePresenter : MonoBehaviour, IInteractable
{
    private const int PAYOUT_PARTICLE_COUNT = 48;

    [SerializeField] private SlotMachineReels reels;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PlayerWeapon weapon;

    private bool spinning;
    private bool payoutInProgress;
    private object spinOperation;
    private LogSpinContext activeLogSpin;
    private LogSlotCollector activeSlotLog;
    // 마지막으로 회전을 시작한 웨이브 번호. 이월 저금통 판정용(0이면 아직 회전 없음).
    private int lastSpinWave;

    private static SlotMachineData Table => TableManager.Instance.SlotMachine;
    private static ShopData Shop => TableManager.Instance.Shop;

    public bool IsSpinning => spinning;

    // 할인 퍽이 적용된 현재 회전 비용.
    public int CurrentCost
    {
        get
        {
            int baseCost = SlotMachineInfo.SpinCost(Table);
            return gameManager != null ? gameManager.Perks.ApplySpinCost(baseCost) : baseCost;
        }
    }

    public event Action<int> SpinStarted;
    public event Action<float> SpinHealed;
    // 결과, 지급액, 화면에 보일 문구.
    public event Action<SlotOutcome, int, string> SpinResolved;

    public bool CanInteract()
    {
        return !spinning
            && !payoutInProgress
            && reels != null
            && !reels.IsSpinning
            && gameManager != null
            && gameManager.IsWaveActive
            && gameManager.Wallet.CanAfford(CurrentCost);
    }

    public string GetPrompt()
    {
        if (spinning)
        {
            return "회전 중...";
        }
        if (gameManager == null || !gameManager.IsWaveActive)
        {
            return "정비 시간에는 슬롯이 잠깁니다";
        }
        if (gameManager.Wallet != null && !gameManager.Wallet.CanAfford(CurrentCost))
        {
            return $"소지금 부족 (필요 ${CurrentCost})";
        }
        return $"E / Y: 슬롯 회전 (-${CurrentCost} · 체력 +{SlotMachineInfo.SpinHealAmount(Table):0.#})";
    }

    public void Interact()
    {
        if (!CanInteract())
        {
            LogRejected();
            return;
        }

        int cost = CurrentCost;
        LogSlotState logBefore = gameManager.CaptureSlotLogState();
        gameManager?.GameplayLog?.SlotSpinRequested(cost, gameManager.Wallet.Balance);
        // 잔액/체력 변경 이벤트의 재진입으로 한 회전에 중복 결제·회복하지 않도록 먼저 잠근다.
        spinning = true;
        if (!gameManager.Wallet.TrySpend(cost))
        {
            spinning = false;
            gameManager?.GameplayLog?.SlotSpinRejected("InsufficientFunds", cost, gameManager.Wallet.Balance);
            return;
        }

        int[] faces = RollFaces();
        SlotOutcome outcome = SlotMachineInfo.Evaluate(Table, faces);
        float seconds = gameManager.Perks.ApplySpinSeconds(SlotMachineInfo.SpinSeconds(Table));
        // 이월 저금통: 이번 웨이브 첫 회전이고 직전 웨이브에 한 번도 돌리지 않았을 때만.
        int wave = gameManager.WaveNumber;
        bool piggyEligible = wave > 1 && lastSpinWave < wave - 1;
        float piggy = gameManager.Perks.PiggyBankMultiplier(piggyEligible);
        object operation = new object();
        spinOperation = operation;
        bool started = reels.BeginSpin(faces, seconds, () =>
        {
            if (!ReferenceEquals(spinOperation, operation))
            {
                return;
            }
            spinOperation = null;
            OnSpinStopped(outcome, piggy, wave);
        });
        if (!started)
        {
            spinOperation = null;
            gameManager.Wallet.Add(cost);
            spinning = false;
            gameManager?.GameplayLog?.SlotSpinRejected("ReelsBusy", cost, gameManager.Wallet.Balance);
            return;
        }

        lastSpinWave = wave;
        activeSlotLog = gameManager.SlotLog;
        activeLogSpin = activeSlotLog?.Start(this, cost, SlotMachineInfo.SpinCost(Table), seconds, faces, SymbolIds(faces), logBefore);
        float healed = gameManager.RecoverForSlotSpin(SlotMachineInfo.SpinHealAmount(Table));
        activeSlotLog?.Healed(activeLogSpin, healed, gameManager.CurrentHealth);
        if (healed > 0f) SpinHealed?.Invoke(healed);
        SpinStarted?.Invoke(cost);
    }

    // 상호작용 조건이 안 맞아 회전하지 못한 요청. 비용·소지금을 바꾸지 않는다.
    private void LogRejected()
    {
        string reason = spinning || payoutInProgress || (reels != null && reels.IsSpinning) ? "Spinning"
            : reels == null || gameManager == null ? "NotReady"
            : !gameManager.IsWaveActive ? "WaveInactive"
            : "InsufficientFunds";
        gameManager?.GameplayLog?.SlotSpinRejected(reason, CurrentCost, gameManager != null && gameManager.Wallet != null ? gameManager.Wallet.Balance : 0);
    }

    // 릴마다 독립 추첨한다. 탄약 심볼 가중치 배수는 퍽 보유에 따라 달라진다.
    private int[] RollFaces()
    {
        int scale = gameManager.Perks.AmmoWeightScale;
        int total = SlotMachineInfo.TotalWeight(Table, scale);
        int faceCount = Mathf.Max(1, SlotMachineInfo.FaceCount(Table));
        int[] faces = new int[SlotMachineInfo.REEL_COUNT];
        for (int i = 0; i < faces.Length; i++)
        {
            int face = SlotMachineInfo.PickFace(Table, UnityEngine.Random.Range(0, Mathf.Max(1, total)), scale);
            faces[i] = face >= 0 ? face : UnityEngine.Random.Range(0, faceCount);
        }
        return faces;
    }

    private static string[] SymbolIds(int[] _faces)
    {
        var ids = new string[_faces.Length];
        for (int i = 0; i < _faces.Length; i++)
        {
            if (Table?.symbols == null)
            {
                continue;
            }
            foreach (var symbol in Table.symbols)
            {
                if (symbol != null && symbol.reelIndex == _faces[i])
                {
                    ids[i] = symbol.id;
                    break;
                }
            }
        }
        return ids;
    }

    // _spinWave는 회전을 시작한 웨이브다. 정비 중에 결과가 나와도 시대·파츠 계획은 이 값을 쓴다.
    private void OnSpinStopped(SlotOutcome _outcome, float _piggyMultiplier, int _spinWave)
    {
        if (!spinning || payoutInProgress)
        {
            return;
        }
        payoutInProgress = true;
        try
        {
            var text = new StringBuilder();
            int payout = 0;
            double? cashBonus = null;
            LogSlotState beforePayout = gameManager.CaptureSlotLogState();

            SlotRewardInfo.Reward reward = gameManager.PlanSlotReward(_spinWave, _outcome);
            SlotRewardInfo.Apply(reward, gameManager.Ammo, gameManager.Parts, gameManager.Wallet, activeLogSpin?.SpinId);
            string rewardText = SlotRewardInfo.Describe(reward, Shop, TableManager.Instance.Part, TableManager.Instance.Ammo);

            if (_outcome.multiplier > 0)
            {
                float bonus = (1f + gameManager.Perks.KillBonusPerKill * gameManager.KillsThisWave) * gameManager.Perks.PayoutMultiplier * _piggyMultiplier;
                cashBonus = bonus;
                payout = Mathf.FloorToInt(SlotMachineInfo.SpinCost(Table) * _outcome.multiplier * bonus);
                gameManager.Wallet.Add(payout);
                bool triple = _outcome.tripleFace >= 0;
                string name = SlotMachineInfo.SymbolName(Table, triple ? _outcome.tripleFace : _outcome.pairFace);
                text.Append($"{name} {(triple ? "3줄" : "2줄")} x{_outcome.multiplier}  +${payout:N0}");
                if (_piggyMultiplier > 1f)
                {
                    text.Append($"  (저금통 x{_piggyMultiplier:0.#})");
                }
            }
            else if (_outcome.isNearMiss)
            {
                text.Append("아깝다!");
            }
            LogSlotState afterPayout = gameManager.CaptureSlotLogState();
            activeSlotLog?.Resolve(activeLogSpin, beforePayout, afterPayout, new LogSlotOutcome
            {
                tripleFace = _outcome.tripleFace, pairFace = _outcome.pairFace, multiplier = _outcome.multiplier,
                hasAmmo = _outcome.hasAmmo, partCount = _outcome.partCount, nextEraPartCount = _outcome.nextEraPartCount,
                isMiss = _outcome.isMiss, isNearMiss = _outcome.isNearMiss
            }, _piggyMultiplier, cashBonus, payout, reward.partIds.ToArray());

            if (rewardText.Length > 0)
            {
                if (text.Length > 0)
                {
                    text.Append("  ");
                }
                text.Append(rewardText);
            }
            if (text.Length == 0)
            {
                text.Append("꽝");
            }

            if (payout > 0 || reward.partIds.Count > 0)
            {
                reels.PlayPayout(PAYOUT_PARTICLE_COUNT);
            }
            if (payout > 0)
            {
                SoundPlayer.Play(SfxType.Payout);
            }
            else if (reward.ammoId != null)
            {
                SoundPlayer.Play(SfxType.AmmoRefill);
            }
            else if (_outcome.isNearMiss)
            {
                SoundPlayer.Play(SfxType.NearMiss);
            }
            else if (reward.IsEmpty)
            {
                SoundPlayer.Play(SfxType.Fail);
            }

            spinning = false;
            SpinResolved?.Invoke(_outcome, payout, text.ToString());
        }
        catch
        {
            activeSlotLog?.Incomplete(activeLogSpin, "payout_interrupted");
            throw;
        }
        finally
        {
            spinning = false;
            payoutInProgress = false;
            activeLogSpin = null;
            activeSlotLog = null;
        }
    }
}
