using UnityEngine;

// 정비 상점 규칙(Presenter). 정비 시간에만 열리며, 열리는 동안 마우스 커서를 풀어 둔다.
// 상점 칸 내용은 ShopData 순서를 따르고, 구매 판정은 GameManager.TryBuy가 한다.
public sealed class ShopPresenter : MonoBehaviour
{
    [SerializeField] private ShopView view;
    [SerializeField] private GameManager gameManager;

    private ShopData.Item[] items;

    private void Start()
    {
        var shop = TableManager.Instance.Shop;
        items = shop != null && shop.items != null ? shop.items : new ShopData.Item[0];

        view.SlotClicked += OnSlotClicked;
        view.NextWaveClicked += OnNextWaveClicked;
        gameManager.PhaseChanged += OnPhaseChanged;
        gameManager.IntermissionChanged += view.SetIntermission;
        gameManager.Wallet.BalanceChanged += OnWalletChanged;
        gameManager.Loadout.Changed += RefreshSlots;
        gameManager.Perks.Changed += RefreshSlots;

        view.SetWallet(gameManager.Wallet.Balance);
        OnPhaseChanged(gameManager.Phase);
    }

    private void OnDestroy()
    {
        if (view != null)
        {
            view.SlotClicked -= OnSlotClicked;
            view.NextWaveClicked -= OnNextWaveClicked;
        }
        if (gameManager != null)
        {
            gameManager.PhaseChanged -= OnPhaseChanged;
            gameManager.IntermissionChanged -= view.SetIntermission;
            gameManager.Wallet.BalanceChanged -= OnWalletChanged;
            gameManager.Loadout.Changed -= RefreshSlots;
            gameManager.Perks.Changed -= RefreshSlots;
        }
    }

    // 잔액은 슬롯 지급 등 상점 밖에서도 바뀐다. 금액 표시와 칸 활성 상태를 함께 갱신해야 구매 가능 여부가 맞는다.
    private void OnWalletChanged(int _amount)
    {
        view.SetWallet(_amount);
        RefreshSlots();
    }

    private void OnPhaseChanged(GameManager.GamePhase _phase)
    {
        bool shopOpen = _phase == GameManager.GamePhase.Shop;
        view.SetVisible(shopOpen);
        InputGate.PlayerEnabled = !shopOpen && _phase != GameManager.GamePhase.GameOver;
        // 게임 오버 때 커서는 GameOverPresenter가 푼다. 두 Presenter가 같은 전이에서 커서를 다투지 않도록 여기서는 건드리지 않는다.
        if (_phase != GameManager.GamePhase.GameOver)
        {
            Cursor.lockState = shopOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = shopOpen;
        }
        if (shopOpen)
        {
            SoundPlayer.Play(SfxType.ShopOpen);
            ShowCompletionSummary();
            RefreshSlots();
        }
    }

    private void OnSlotClicked(int _index)
    {
        if (_index < 0 || _index >= items.Length)
        {
            return;
        }
        string failure = gameManager.TryBuy(items[_index]);
        if (failure == "")
        {
            ShowCompletionSummary();
        }
        else
        {
            view.SetMessage(failure);
        }
        if (failure == "")
        {
            SoundPlayer.Play(SfxType.Click);
        }
        RefreshSlots();
    }

    private void OnNextWaveClicked()
    {
        gameManager.StartNextWave();
    }

    private void ShowCompletionSummary()
    {
        view.SetMessage($"WAVE {gameManager.WaveNumber} 완료 · HP +{gameManager.WaveHealthRestored:0.##} · 보너스 +${gameManager.WaveBonusPaid:N0}");
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < view.SlotCount && i < items.Length; i++)
        {
            RenderSlot(i, items[i]);
        }
    }

    private void RenderSlot(int _index, ShopData.Item _item)
    {
        bool open = gameManager.Phase == GameManager.GamePhase.Shop;
        string label = _item.label;
        string price = $"${_item.price:N0}";
        bool affordable = gameManager.Wallet.CanAfford(_item.price);

        switch (_item.type)
        {
            case ShopInfo.TYPE_WEAPON:
                if (gameManager.Loadout.IsEquipped(_item.target))
                {
                    label += "\n장착 중";
                    price = "";
                }
                else if (gameManager.Loadout.IsOwned(_item.target))
                {
                    // 보유 무기는 바닥에 놓인 픽업으로만 되찾는다. 상점에서 무료 장착하지 않는다.
                    label += "\n보유 · 바닥 픽업 필요";
                    price = "";
                }
                break;
            case ShopInfo.TYPE_HEAL:
                label += $"\n체력 +{GameConfigInfo.HealAmount(TableManager.Instance.GameConfig)}";
                if (!gameManager.CanHeal)
                {
                    price = "최대";
                }
                break;
            case ShopInfo.TYPE_PERK:
                var perk = PerkInfo.Find(TableManager.Instance.Perk, _item.target);
                int stack = gameManager.Perks.StackOf(_item.target);
                int max = perk != null ? perk.maxStack : 0;
                label += perk != null ? $"\n{perk.description}\n스택 {stack} / {max}" : "";
                if (stack >= max)
                {
                    price = "최대";
                }
                break;
        }

        bool interactable = open && CanUse(_item, affordable);
        view.SetSlot(_index, label, price, interactable);
    }

    // 무기 칸은 아직 획득하지 않은 무기만 살 수 있다. 보유 무기(장착 중·바닥 픽업)는 상점에서 누를 수 없다.
    private bool CanUse(ShopData.Item _item, bool _affordable)
    {
        switch (_item.type)
        {
            case ShopInfo.TYPE_WEAPON:
                if (gameManager.Loadout.IsOwned(_item.target))
                {
                    return false;
                }
                return _affordable;
            case ShopInfo.TYPE_PERK:
                return gameManager.Perks.CanBuy(_item.target) && _affordable;
            case ShopInfo.TYPE_HEAL:
                return gameManager.CanHeal && _affordable;
            default:
                return _affordable;
        }
    }
}
