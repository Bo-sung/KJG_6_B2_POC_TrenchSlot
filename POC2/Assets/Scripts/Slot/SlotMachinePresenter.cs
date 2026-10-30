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
    // 마지막으로 회전을 시작한 웨이브 번호. 이월 저금통 판정용(0이면 아직 회전 없음).
    private int lastSpinWave;

    private static SlotMachineData Table => TableManager.Instance.SlotMachine;

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
    // 결과, 지급액, 화면에 보일 문구.
    public event Action<SlotOutcome, int, string> SpinResolved;

    public bool CanInteract()
    {
        return !spinning
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
        return $"E / Y: 슬롯 회전 (-${CurrentCost})";
    }

    public void Interact()
    {
        if (!CanInteract())
        {
            return;
        }

        int cost = CurrentCost;
        if (!gameManager.Wallet.TrySpend(cost))
        {
            return;
        }

        int[] faces = RollFaces();
        SlotOutcome outcome = SlotMachineInfo.Evaluate(Table, faces);
        float seconds = gameManager.Perks.ApplySpinSeconds(SlotMachineInfo.SpinSeconds(Table));
        // 이월 저금통: 이번 웨이브 첫 회전이고 직전 웨이브에 한 번도 돌리지 않았을 때만.
        int wave = gameManager.WaveNumber;
        bool piggyEligible = wave > 1 && lastSpinWave < wave - 1;
        float piggy = gameManager.Perks.PiggyBankMultiplier(piggyEligible);
        bool started = reels.BeginSpin(faces, seconds, () => OnSpinStopped(outcome, piggy));
        if (!started)
        {
            gameManager.Wallet.Add(cost);
            return;
        }

        lastSpinWave = wave;
        spinning = true;
        SpinStarted?.Invoke(cost);
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

    private void OnSpinStopped(SlotOutcome _outcome, float _piggyMultiplier)
    {
        if (!spinning)
        {
            return;
        }
        spinning = false;
        var text = new StringBuilder();
        int payout = 0;

        if (_outcome.hasAmmo)
        {
            if (weapon != null)
            {
                weapon.GrantSlotAmmo();
            }
            SoundPlayer.Play(SfxType.AmmoRefill);
            text.Append("탄약 보급! ");
        }

        if (_outcome.multiplier > 0)
        {
            float bonus = (1f + gameManager.Perks.KillBonusPerKill * gameManager.KillsThisWave) * gameManager.Perks.PayoutMultiplier * _piggyMultiplier;
            payout = Mathf.FloorToInt(SlotMachineInfo.SpinCost(Table) * _outcome.multiplier * bonus);
            gameManager.Wallet.Add(payout);
            reels.PlayPayout(PAYOUT_PARTICLE_COUNT);
            SoundPlayer.Play(SfxType.Payout);
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
            SoundPlayer.Play(SfxType.NearMiss);
            text.Append("아깝다!");
        }
        else if (!_outcome.hasAmmo)
        {
            SoundPlayer.Play(SfxType.Fail);
            text.Append("꽝");
        }

        SpinResolved?.Invoke(_outcome, payout, text.ToString());
    }
}
