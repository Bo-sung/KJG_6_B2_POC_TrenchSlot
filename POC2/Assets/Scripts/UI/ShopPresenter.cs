using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 정비 상점 규칙(Presenter). 정비 시간에만 열리며, 열리는 동안 마우스 커서를 풀어 둔다.
// 화면 줄과 테이블 항목의 대응·문구는 여기서 정하고, 구매·장착·아킴보 판정은 GameManager.Try*가 한다.
// 파츠는 같은 종류를 여러 개 가질 수 있으므로 장착·분리는 파츠 개체(PartInstance) 단위로 넘긴다.
public sealed partial class ShopPresenter : MonoBehaviour
{
    [SerializeField] private ShopView view;
    [SerializeField] private GameManager gameManager;

    private ShopData shop;
    private WeaponData weapons;
    private AmmoData ammoTable;
    private string selectedWeaponId = "";
    private string selectedPartId = "";
    private readonly List<PartData.Entry> visibleParts = new List<PartData.Entry>();

    private void Start()
    {
        var tables = TableManager.Instance;
        shop = tables.Shop;
        weapons = tables.Weapon;
        ammoTable = tables.Ammo;
        CheckRowCounts();

        view.WeaponSelected += OnWeaponSelected;
        view.WeaponBuyClicked += OnWeaponBuyClicked;
        view.PartSelected += OnPartSelected;
        view.PartBuyClicked += OnPartBuyClicked;
        view.PartAttachClicked += OnPartAttachClicked;
        view.PartDetachClicked += OnPartDetachClicked;
        view.SlotCardClicked += OnSlotCardClicked;
        view.AkimboClicked += OnAkimboClicked;
        view.StatClicked += OnStatClicked;
        view.SupplyClicked += OnSupplyClicked;
        view.UpgradeClicked += OnUpgradeClicked;
        view.NextWaveClicked += OnNextWaveClicked;
        view.FocusChanged += OnFocusChanged;
        view.PointerEntered += OnPointerEntered;
        view.PointerExited += OnPointerExited;
        view.ScrollChanged += OnScrollChanged;
        gameManager.PhaseChanged += OnPhaseChanged;
        gameManager.IntermissionChanged += OnIntermissionChanged;
        gameManager.Wallet.BalanceChanged += OnWalletChanged;
        gameManager.Loadout.Changed += Refresh;
        gameManager.Parts.Changed += Refresh;
        gameManager.Perks.Changed += Refresh;
        gameManager.SlotOffers.Changed += Refresh;
        gameManager.Stats.Changed += OnStatsChanged;
        gameManager.Ammo.Changed += OnAmmoChanged;

        selectedWeaponId = gameManager.Loadout.ActiveWeaponId;
        view.SetWallet(gameManager.Wallet.Balance);
        OnPhaseChanged(gameManager.Phase);
    }

    private void OnDestroy()
    {
        if (view != null)
        {
            view.WeaponSelected -= OnWeaponSelected;
            view.WeaponBuyClicked -= OnWeaponBuyClicked;
            view.PartSelected -= OnPartSelected;
            view.PartBuyClicked -= OnPartBuyClicked;
            view.PartAttachClicked -= OnPartAttachClicked;
            view.PartDetachClicked -= OnPartDetachClicked;
            view.SlotCardClicked -= OnSlotCardClicked;
            view.AkimboClicked -= OnAkimboClicked;
            view.StatClicked -= OnStatClicked;
            view.SupplyClicked -= OnSupplyClicked;
            view.UpgradeClicked -= OnUpgradeClicked;
            view.NextWaveClicked -= OnNextWaveClicked;
            view.FocusChanged -= OnFocusChanged;
            view.PointerEntered -= OnPointerEntered;
            view.PointerExited -= OnPointerExited;
            view.ScrollChanged -= OnScrollChanged;
        }
        if (gameManager != null)
        {
            gameManager.PhaseChanged -= OnPhaseChanged;
            gameManager.IntermissionChanged -= OnIntermissionChanged;
            gameManager.Wallet.BalanceChanged -= OnWalletChanged;
            gameManager.Loadout.Changed -= Refresh;
            gameManager.Parts.Changed -= Refresh;
            gameManager.Perks.Changed -= Refresh;
            gameManager.SlotOffers.Changed -= Refresh;
            gameManager.Stats.Changed -= OnStatsChanged;
            gameManager.Ammo.Changed -= OnAmmoChanged;
        }
    }

    // 프리팹 줄 수가 테이블보다 적으면 넘치는 항목은 보이지 않는다. 프리팹을 다시 구워야 한다(PocShopAuthoring).
    private void CheckRowCounts()
    {
        int slots = gameManager.Loadout.SlotCount;
        if (view.WeaponRowCount < shop.weapons.Length || view.StatRowCount < shop.stats.Length ||
            view.SupplyRowCount < shop.ammoPacks.Length || view.UpgradeRowCount < ShopInfo.SlotOfferCount(shop) ||
            view.WeaponBuyCount < slots || view.SlotCardCount < slots || view.PartSlotButtonCount < slots ||
            view.PartChipCount < MaxPartsPerWeapon())
        {
            Debug.LogError("상점 프리팹 줄 수가 상점 테이블보다 적습니다. POC/Build Shop UI로 프리팹을 다시 구우세요.");
        }
    }

    private int MaxPartsPerWeapon()
    {
        int max = 0;
        foreach (var weapon in weapons.weapons)
        {
            if (weapon != null)
            {
                max = Mathf.Max(max, PartInfo.ForWeapon(TableManager.Instance.Part, weapon.id).Count);
            }
        }
        return max;
    }

    private void OnWalletChanged(int _amount)
    {
        view.SetWallet(_amount);
        Refresh();
    }

    private void OnStatsChanged(string _id)
    {
        Refresh();
    }

    private void OnAmmoChanged(string _id, int _reserve, int _maximum)
    {
        Refresh();
    }

    private void OnPhaseChanged(GameManager.GamePhase _phase)
    {
        bool shopOpen = _phase == GameManager.GamePhase.Shop;
        view.SetVisible(shopOpen);
        if (!shopOpen)
        {
            gameManager.ObserveShopExposure(false, null, null, null, "phase_changed");
        }
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
            Refresh();
        }
    }

    private void ShowCompletionSummary()
    {
        view.SetMessage($"WAVE {gameManager.WaveNumber} 완료 · HP +{gameManager.WaveHealthRestored:0.##} · 보너스 +${gameManager.WaveBonusPaid:N0}");
    }

    // ---------- 입력 ----------

    // 입력마다 using var LogInput(...)으로 조작 한 번을 기록한다. 아무 일도 하지 않은 경로는 LogIgnored로 사유를 남긴다.
    private void OnWeaponSelected(int _index)
    {
        using var input = LogInput("weapon_select", "weapon_catalog", _index);
        if (_index >= 0 && _index < shop.weapons.Length && shop.weapons[_index] != null)
        {
            SelectWeapon(shop.weapons[_index].weaponId, "weapon_catalog");
        }
        else
        {
            LogIgnored("invalid_row");
        }
    }

    private void SelectWeapon(string _weaponId, string _logSource)
    {
        if (selectedWeaponId != _weaponId)
        {
            selectedWeaponId = _weaponId;
            selectedPartId = "";
        }
        Refresh();
        gameManager.ObserveShopSelection(selectedWeaponId, selectedPartId, _logSource);
    }

    private void OnWeaponBuyClicked(int _slot)
    {
        using var input = LogInput("weapon_buy", "weapon_buy", _slot, _slot);
        Report(gameManager.TryBuyWeapon(selectedWeaponId, _slot, LogOffer("Weapon", selectedWeaponId, "weapon_buy", _slot)), $"{WeaponInfo.DisplayName(weapons, selectedWeaponId)} 구매 완료");
    }

    private void OnPartSelected(int _index)
    {
        using var input = LogInput("part_select", "part_catalog", _index);
        if (_index >= 0 && _index < visibleParts.Count)
        {
            selectedPartId = visibleParts[_index].id;
            Refresh();
            gameManager.ObserveShopSelection(selectedWeaponId, selectedPartId, "part_catalog");
        }
        else
        {
            LogIgnored("invalid_row");
        }
    }

    private void OnPartBuyClicked()
    {
        using var input = LogInput("part_buy", "part_buy", 0);
        var part = SelectedPart();
        if (part == null)
        {
            LogIgnored("no_part_selected");
            return;
        }
        // 구매한 개체는 같은 종류 휴대 무기에 바로 붙거나 보관함으로 간다(GameManager가 정한다). 결과 위치를 알려 준다.
        string failure = gameManager.TryBuyPart(part.id, LogOffer("Part", part.id, "part_buy"));
        var all = gameManager.Parts.Instances;
        var bought = failure == "" && all.Count > 0 ? all[all.Count - 1] : null;
        Report(failure, bought != null ? $"{part.name} #{bought.Uid} 구매 · {PartPlace(bought)}" : $"{part.name} 구매");
    }

    // 보관함의 개체를 우선 붙이고, 보관함이 비었으면 다른 휴대 무기에 붙은 개체를 옮긴다.
    private void OnPartAttachClicked(int _slot)
    {
        using var input = LogInput("part_attach", "part_attach", _slot, _slot);
        var part = SelectedPart();
        var copy = part != null ? AttachSource(part, _slot) : null;
        if (copy != null)
        {
            Report(gameManager.TryAttachPart(copy, _slot), $"{part.name} #{copy.Uid} {_slot + 1}번 무기에 장착");
        }
        else
        {
            LogIgnored(part == null ? "no_part_selected" : "no_attach_source");
        }
    }

    private void OnPartDetachClicked(int _slot)
    {
        using var input = LogInput("part_detach", "part_detach", _slot, _slot);
        var part = SelectedPart();
        var copy = part != null ? CopyOn(gameManager.Loadout.Get(_slot), part) : null;
        if (copy != null)
        {
            Report(gameManager.TryDetachPart(copy), $"{part.name} #{copy.Uid} 분리 · 보관함으로");
        }
        else
        {
            LogIgnored(part == null ? "no_part_selected" : "part_not_on_weapon");
        }
    }

    // 휴대 무기 카드를 누르면 그 무기 종류를 선택해 파츠를 바로 본다.
    private void OnSlotCardClicked(int _slot)
    {
        using var input = LogInput("slot_card_select", "slot_card", _slot, _slot);
        var held = gameManager.Loadout.Get(_slot);
        if (held != null)
        {
            SelectWeapon(held.WeaponId, "slot_card");
        }
        else
        {
            LogIgnored("empty_slot");
        }
    }

    private void OnAkimboClicked()
    {
        using var input = LogInput("akimbo_toggle", "akimbo", 0);
        bool wasOn = gameManager.IsAkimbo;
        Report(gameManager.TryToggleAkimbo(), wasOn ? "아킴보 해제" : "아킴보 전환 · 양손 사용");
    }

    private void OnStatClicked(int _index)
    {
        using var input = LogInput("stat_buy", "stat", _index);
        if (_index >= 0 && _index < shop.stats.Length && shop.stats[_index] != null)
        {
            Report(gameManager.TryBuyStat(shop.stats[_index].id, LogOffer("Stat", shop.stats[_index].id, "stat")), $"{shop.stats[_index].name} 강화");
        }
        else
        {
            LogIgnored("invalid_row");
        }
    }

    // 보급은 탄약 팩만 판매한다. 체력은 슬롯 회전으로 회복한다.
    private void OnSupplyClicked(int _index)
    {
        using var input = LogInput("ammo_buy", "ammo", _index);
        if (_index >= 0 && _index < shop.ammoPacks.Length && shop.ammoPacks[_index] != null)
        {
            Report(gameManager.TryBuyAmmo(shop.ammoPacks[_index].ammoId, LogOffer("Ammo", shop.ammoPacks[_index].ammoId, "ammo")), "탄약 팩 구매");
        }
        else
        {
            LogIgnored("invalid_row");
        }
    }

    private void OnUpgradeClicked(int _index)
    {
        using var input = LogInput("slot_upgrade_buy", "upgrade", _index);
        Report(gameManager.TryBuySlotUpgrade(_index, LogOffer("SlotUpgrade", gameManager.SlotOffers.Get(_index), "upgrade")), "슬롯 업그레이드 구매");
    }

    private void OnNextWaveClicked()
    {
        using var input = LogInput("next_wave", "next_wave", 0);
        gameManager.StartNextWave();
    }

    private void OnIntermissionChanged(float _seconds)
    {
        if (view == null) return;
        float total = gameManager != null ? gameManager.ShopDuration : 0f;
        view.SetIntermission(_seconds, total);
    }

    private void Report(string _failure, string _success)
    {
        view.SetMessage(_failure == "" ? _success : _failure);
        if (_failure == "")
        {
            SoundPlayer.Play(SfxType.Click);
        }
        Refresh();
    }

    // ---------- 파츠 개체 ----------

    private PartData.Entry SelectedPart()
    {
        return visibleParts.Find(_p => _p.id == selectedPartId);
    }

    // 무기에 붙은 이 파츠 개체. 없으면 null이다.
    private static PartInstance CopyOn(WeaponInstance _weapon, PartData.Entry _part)
    {
        var copy = _weapon != null ? _weapon.PartInstanceIn(_part.slot) : null;
        return copy != null && copy.PartId == _part.id ? copy : null;
    }

    // _slot 무기에 붙일 개체: 보관함 우선, 없으면 다른 휴대 무기에서 옮긴다. 바닥 무기의 개체는 주워야 옮길 수 있어 쓰지 않는다.
    private PartInstance AttachSource(PartData.Entry _part, int _slot)
    {
        var stored = gameManager.Parts.FirstStored(_part.id);
        if (stored != null)
        {
            return stored;
        }
        var loadout = gameManager.Loadout;
        for (int i = 0; i < loadout.SlotCount; i++)
        {
            var copy = i != _slot ? CopyOn(loadout.Get(i), _part) : null;
            if (copy != null)
            {
                return copy;
            }
        }
        return null;
    }

    // ---------- 표시 ----------

    private void Refresh()
    {
        if (view == null || gameManager == null || shop == null)
        {
            return;
        }
        bool open = gameManager.Phase == GameManager.GamePhase.Shop;
        int wave = gameManager.ShopWave;
        var era = ShopInfo.EraForWave(shop, wave);
        view.SetTitle($"정비 상점 · {(era != null ? era.name : "")} · 다음 WAVE {wave}");
        RefreshWeapons(open, wave);
        RefreshParts(open, wave);
        RefreshStats(open);
        RefreshSupplies(open, wave);
        RefreshUpgrades(open);
        RefreshUserInfo(open);
        view.EnsureValidSelection();
    }

    private void RefreshWeapons(bool _open, int _wave)
    {
        for (int i = 0; i < view.WeaponRowCount; i++)
        {
            var offer = i < shop.weapons.Length ? shop.weapons[i] : null;
            var entry = offer != null ? WeaponInfo.Find(weapons, offer.weaponId) : null;
            if (entry == null)
            {
                view.SetWeaponRow(i, "", "", false, false);
                continue;
            }
            bool unlocked = ShopInfo.IsWeaponUnlocked(shop, entry, _wave);
            var era = ShopInfo.FindEra(shop, entry.era);
            int held = HeldCount(entry.id);
            string sub = unlocked ? $"{EraName(entry.era)} · {AmmoName(entry.ammoId)}{(held > 0 ? $" · 휴대 {held}자루" : "")}" : $"잠김 · WAVE {(era != null ? era.startWave : 0)}부터";
            // 잠긴 무기도 눌러서 상세를 볼 수 있다. 구매 버튼에서 막는다.
            view.SetWeaponRow(i, $"{entry.name}\n<size=90%><color=#AEBCCD>{sub}</color></size>", $"${offer.price:N0}", true, offer.weaponId == selectedWeaponId);
        }

        var selected = WeaponInfo.Find(weapons, selectedWeaponId);
        bool buyable = selected != null && ShopInfo.IsWeaponUnlocked(shop, selected, _wave);
        view.SetWeaponDetail(selected != null ? WeaponDetail(selected, buyable) : "무기를 선택하세요.");
        int price = ShopInfo.WeaponPrice(shop, selectedWeaponId);
        bool affordable = gameManager.Wallet.CanAfford(price);
        int empty = gameManager.Loadout.FirstEmptySlot();
        for (int slot = 0; slot < view.WeaponBuyCount; slot++)
        {
            var held = gameManager.Loadout.Get(slot);
            if (slot >= gameManager.Loadout.SlotCount || selected == null || (empty >= 0 && slot != empty))
            {
                view.SetWeaponBuy(slot, "", "", false);
                continue;
            }
            string label = held == null ? $"구매 → {slot + 1}번 슬롯" : $"구매 → {slot + 1}번 교체\n<size=90%>{WeaponInfo.DisplayName(weapons, held.WeaponId)}는 바닥에 놓임</size>";
            view.SetWeaponBuy(slot, label, $"${price:N0}", _open && buyable && affordable);
        }
    }

    private string WeaponDetail(WeaponData.Entry _entry, bool _unlocked)
    {
        var text = new StringBuilder();
        text.AppendLine($"<size=120%><b>{_entry.name}</b></size>  <color=#AEBCCD>{EraName(_entry.era)}{(_unlocked ? "" : " · 잠김")}</color>");
        text.AppendLine($"탄종 {AmmoName(_entry.ammoId)} · 탄창 {_entry.magazineSize}");
        string damage = _entry.pellets > 1 ? $"{_entry.damage:0.#} × {_entry.pellets}산탄" : $"{_entry.damage:0.#}";
        text.AppendLine($"피해 {damage} · 연사 {_entry.shotsPerSecond:0.#}발/초{(_entry.automatic ? " (자동)" : "")}");
        text.AppendLine($"재장전 {_entry.reloadSeconds:0.#}초 · 사거리 {_entry.range:0}m");
        string knockback = _entry.knockback > 0f ? $" · 넉백 {_entry.knockback:0.#}" : "";
        text.AppendLine($"저지력 {_entry.stoppingPower:0.#}{knockback} · 이동 {_entry.moveSpeedMultiplier * 100f:0}%");
        if (WeaponInfo.CanAkimbo(_entry))
        {
            text.AppendLine("<color=#9CE0C4>권총류 · 2자루 보유 시 아킴보 가능</color>");
        }
        if (gameManager.Loadout.FirstEmptySlot() < 0)
        {
            text.Append("<color=#B9C9D8><size=90%>두 슬롯이 차 있으면 교체할 슬롯을 선택합니다.</size></color>");
        }
        return text.ToString();
    }

    private int HeldCount(string _weaponId)
    {
        int held = 0;
        for (int i = 0; i < gameManager.Loadout.SlotCount; i++)
        {
            if (gameManager.Loadout.Get(i)?.WeaponId == _weaponId)
            {
                held++;
            }
        }
        return held;
    }

    private void RefreshParts(bool _open, int _wave)
    {
        visibleParts.Clear();
        var entry = WeaponInfo.Find(weapons, selectedWeaponId);
        if (entry != null)
        {
            visibleParts.AddRange(PartInfo.ForWeapon(TableManager.Instance.Part, entry.id));
        }
        if (SelectedPart() == null)
        {
            selectedPartId = visibleParts.Count > 0 ? visibleParts[0].id : "";
        }
        var inventory = gameManager.Parts;
        for (int i = 0; i < view.PartChipCount; i++)
        {
            if (i >= visibleParts.Count)
            {
                view.SetPartChip(i, "", "", false);
                continue;
            }
            var part = visibleParts[i];
            int owned = inventory.OwnedCount(part.id);
            string sub = owned > 0 ? $"보유 {owned} · 보관 {inventory.StoredCount(part.id)}" : PartInfo.SlotName(part.slot);
            view.SetPartChip(i, $"{part.name}\n<size=90%><color=#AEBCCD>{sub}</color></size>", $"${ShopInfo.PartPrice(shop, part):N0}", part.id == selectedPartId);
        }

        var selected = SelectedPart();
        var loadout = gameManager.Loadout;
        if (selected == null)
        {
            view.SetPartDetail(entry == null ? "무기를 선택하세요." : $"{entry.name}에는 판매 중인 파츠가 없습니다.");
            view.SetPartBuy("", "", false);
            for (int slot = 0; slot < view.PartSlotButtonCount; slot++)
            {
                view.SetPartAttach(slot, "", false);
                view.SetPartDetach(slot, "", false);
            }
            return;
        }

        bool unlocked = ShopInfo.IsWeaponUnlocked(shop, entry, _wave);
        int price = ShopInfo.PartPrice(shop, selected);
        view.SetPartDetail(PartDetail(selected));
        string buyLabel = !unlocked ? "잠김 · 무기 해금 후 판매" : inventory.OwnedCount(selected.id) > 0 ? "추가 구매" : "구매";
        view.SetPartBuy(buyLabel, $"${price:N0}", _open && unlocked && gameManager.Wallet.CanAfford(price));
        for (int slot = 0; slot < view.PartSlotButtonCount; slot++)
        {
            var held = loadout.Get(slot);
            if (slot >= loadout.SlotCount || held == null || held.WeaponId != selected.weaponId)
            {
                view.SetPartAttach(slot, held == null ? $"{slot + 1}번 · 빈 칸" : $"{slot + 1}번 · 다른 무기", false);
                view.SetPartDetach(slot, $"{slot + 1}번 분리", false);
                continue;
            }
            var onIt = CopyOn(held, selected);
            var source = AttachSource(selected, slot);
            string replace = held.PartIn(selected.slot) != null && onIt == null ? " (교체)" : "";
            string attachLabel = onIt != null ? $"{slot + 1}번 장착됨 #{onIt.Uid}" :
                source == null ? $"{slot + 1}번 장착 · 보관 없음" :
                source.IsStored ? $"{slot + 1}번 장착{replace}" : $"{slot + 1}번으로 옮김{replace}";
            view.SetPartAttach(slot, attachLabel, _open && onIt == null && source != null);
            view.SetPartDetach(slot, onIt != null ? $"{slot + 1}번 분리 #{onIt.Uid}" : $"{slot + 1}번 분리", _open && onIt != null);
        }
    }

    // 선택 파츠의 효과와 개체별 위치(보관·휴대 슬롯·바닥 무기).
    private string PartDetail(PartData.Entry _part)
    {
        var text = new StringBuilder();
        text.AppendLine($"<b>{_part.name}</b>  <color=#AEBCCD>[{PartInfo.SlotName(_part.slot)}]</color>");
        text.AppendLine($"<size=90%>{_part.description}</size>");
        var copies = gameManager.Parts.InstancesOf(_part.id);
        if (copies.Count == 0)
        {
            text.Append("<color=#B9C9D8>보유 0개 · 같은 파츠를 여러 개 사서 무기 개체마다 장착할 수 있습니다.</color>");
            return text.ToString();
        }
        var places = new List<string>();
        foreach (var copy in copies)
        {
            places.Add($"#{copy.Uid} {PartPlace(copy)}");
        }
        text.Append($"<color=#B9C9D8>보유 {copies.Count}개 · {string.Join(" · ", places)}</color>");
        return text.ToString();
    }

    private string PartPlace(PartInstance _copy)
    {
        if (_copy.IsStored)
        {
            return "보관";
        }
        int slot = gameManager.Loadout.IndexOf(_copy.AttachedTo);
        return slot >= 0 ? $"{slot + 1}번 장착" : "<color=#FFB080>바닥 무기(주워야 분리)</color>";
    }

    private void RefreshStats(bool _open)
    {
        for (int i = 0; i < view.StatRowCount; i++)
        {
            var stat = i < shop.stats.Length ? shop.stats[i] : null;
            if (stat == null)
            {
                view.SetStatRow(i, "", "", false);
                continue;
            }
            int count = gameManager.Stats.CountOf(stat.id);
            int price = ShopInfo.StatPrice(shop, stat, count);
            view.SetStatRow(i, $"<b>{stat.name}</b>\n<size=90%>{stat.description}</size>\n<size=90%><color=#AEBCCD>강화 {count}회</color></size>", $"${price:N0}", _open && gameManager.Wallet.CanAfford(price));
        }
    }

    private void RefreshSupplies(bool _open, int _wave)
    {
        var ammo = gameManager.Ammo;
        for (int i = 0; i < view.SupplyRowCount; i++)
        {
            if (i < shop.ammoPacks.Length)
            {
                var pack = shop.ammoPacks[i];
                if (pack == null || !ShopInfo.IsAmmoUnlocked(shop, weapons, pack.ammoId, _wave))
                {
                    view.SetSupplyRow(i, "", "", false);
                    continue;
                }
                int price = ShopInfo.AmmoPackPrice(shop, pack);
                bool canAdd = ShopInfo.CanAddAmmo(ammo, pack.ammoId);
                string label = $"{AmmoName(pack.ammoId)} 전량 충전\n<size=90%><color=#AEBCCD>예비 {ammo.GetReserve(pack.ammoId)} / {ammo.GetMaximum(pack.ammoId)}</color></size>";
                view.SetSupplyRow(i, label, canAdd ? $"${price:N0}" : "최대", _open && canAdd && gameManager.Wallet.CanAfford(price));
            }
            else
            {
                view.SetSupplyRow(i, "", "", false);
            }
        }
    }

    private void RefreshUpgrades(bool _open)
    {
        var offers = gameManager.SlotOffers;
        for (int i = 0; i < view.UpgradeRowCount; i++)
        {
            if (i >= offers.Count)
            {
                view.SetUpgradeRow(i, "", "", false);
                continue;
            }
            if (offers.IsSoldOut(i))
            {
                view.SetUpgradeRow(i, "품절", "", false);
                continue;
            }
            var perk = PerkInfo.Find(TableManager.Instance.Perk, offers.Get(i));
            var upgrade = ShopInfo.FindSlotUpgrade(shop, offers.Get(i));
            string name = perk != null ? perk.name : offers.Get(i);
            string description = perk != null ? perk.description : "";
            if (offers.IsBought(i))
            {
                // 산 칸은 다음 갱신까지 비워 둔다는 것을 보여 준다.
                view.SetUpgradeRow(i, $"<b>{name}</b>\n<size=90%><color=#AEBCCD>구매 완료 · 다음 갱신까지 빈 칸</color></size>", "", false);
                continue;
            }
            int price = upgrade != null ? upgrade.price : 0;
            view.SetUpgradeRow(i, UpgradeLabel(name, description), $"${price:N0}", _open && offers.CanBuy(i) && gameManager.Wallet.CanAfford(price));
        }
    }

    // 슬롯 업그레이드는 런당 1회라 보유 수 대신 설명에 자리를 준다. 가장 긴 설명도 카드 안에 들어가는지 PocShopUiVerification이 본다.
    private static string UpgradeLabel(string _name, string _description)
    {
        return $"<b>{_name}</b>\n<size=85%>{_description}</size>";
    }

    // 상품 선택과 무관하게 현재 상태(체력·스탯·휴대 무기 개체·아킴보·예비탄·보관 파츠·바닥 무기)를 항상 보여 준다.
    private void RefreshUserInfo(bool _open)
    {
        var stats = gameManager.Stats;
        float armor = (1f - stats.DamageTakenMultiplier) * 100f;
        view.SetUserInfo($"체력 {Mathf.CeilToInt(gameManager.CurrentHealth)} / {Mathf.CeilToInt(gameManager.MaxHealth)} · 방어력 {armor:0.#}% · 이동속도 {stats.MoveSpeedMultiplier * 100f:0}%\n" +
            $"재장전 시간 {stats.ReloadMultiplier * 100f:0}% · 예비탄 한도 +{stats.AmmoCapacityBonus}");

        var loadout = gameManager.Loadout;
        for (int slot = 0; slot < view.SlotCardCount; slot++)
        {
            var held = loadout.Get(slot);
            if (slot >= loadout.SlotCount)
            {
                view.SetSlotCard(slot, "", "", false, false);
                continue;
            }
            if (held == null)
            {
                view.SetSlotCard(slot, $"<b>{slot + 1}번 슬롯</b>\n<color=#AEBCCD>빈 칸</color>", "", false, false);
                continue;
            }
            var settings = gameManager.ResolveSettings(held);
            view.SetSlotCard(slot, $"<b>{slot + 1}번 슬롯</b> · {WeaponInfo.DisplayName(weapons, held.WeaponId)} · 장전 {held.Magazine} / {settings.magazineSize}\n" +
                $"<size=90%><color=#AEBCCD>파츠: {PartList(held)}</color></size>", SlotStatus(slot), true, slot == loadout.ActiveIndex || slot == loadout.OffHandIndex);
        }

        bool akimbo = gameManager.IsAkimbo;
        string note = akimbo ? "양손 사용 중 · 슬롯 2개 점유" :
            gameManager.CanAkimbo ? "같은 권총 2자루 보유 · 무료 전환 가능" : gameManager.AkimboUnavailableReason;
        view.SetAkimbo($"아킴보 {(akimbo ? "ON" : "OFF")} · 무료 전환", note, _open && (akimbo || gameManager.CanAkimbo), akimbo);
        view.SetInventory(InventoryText());
    }

    private string SlotStatus(int _slot)
    {
        var loadout = gameManager.Loadout;
        if (loadout.Akimbo)
        {
            return _slot == loadout.ActiveIndex ? "<color=#9CE0C4>● 아킴보 오른손</color>" : _slot == loadout.OffHandIndex ? "<color=#9CE0C4>● 아킴보 왼손</color>" : "";
        }
        return _slot == loadout.ActiveIndex ? "<color=#9CE0C4>● 사용 중</color>" : "";
    }

    private string PartList(WeaponInstance _weapon)
    {
        var names = new List<string>();
        foreach (var copy in _weapon.Parts)
        {
            var part = PartInfo.Find(TableManager.Instance.Part, copy.PartId);
            names.Add($"{(part != null ? part.name : copy.PartId)} #{copy.Uid}");
        }
        return names.Count > 0 ? string.Join(", ", names) : "없음";
    }

    // 예비탄 / 보관 파츠 / 파츠가 붙은 채 바닥에 놓인 무기.
    private string InventoryText()
    {
        var text = new StringBuilder();
        var loadout = gameManager.Loadout;
        var usedAmmo = new List<string>();
        for (int i = 0; i < loadout.SlotCount; i++)
        {
            var entry = loadout.Get(i) != null ? WeaponInfo.Find(weapons, loadout.Get(i).WeaponId) : null;
            if (entry != null && !usedAmmo.Contains(entry.ammoId))
            {
                usedAmmo.Add(entry.ammoId);
            }
        }
        var ammo = gameManager.Ammo;
        var reserves = new List<string>();
        foreach (var entry in ammoTable.ammoTypes)
        {
            if (entry != null && (usedAmmo.Contains(entry.id) || ammo.GetReserve(entry.id) > 0))
            {
                reserves.Add($"{entry.name} {ammo.GetReserve(entry.id)} / {ammo.GetMaximum(entry.id)}");
            }
        }
        text.AppendLine($"<b>공유 예비탄</b>  {(reserves.Count > 0 ? string.Join(" · ", reserves) : "없음")}");

        var stored = new List<string>();
        var dropped = new List<WeaponInstance>();
        foreach (var copy in gameManager.Parts.Instances)
        {
            var part = PartInfo.Find(TableManager.Instance.Part, copy.PartId);
            if (copy.IsStored)
            {
                stored.Add($"{(part != null ? part.name : copy.PartId)} #{copy.Uid} ({WeaponInfo.DisplayName(weapons, part != null ? part.weaponId : "")})");
            }
            else if (!loadout.Contains(copy.AttachedTo) && !dropped.Contains(copy.AttachedTo))
            {
                dropped.Add(copy.AttachedTo);
            }
        }
        text.AppendLine($"<b>보관 파츠</b>  {(stored.Count > 0 ? string.Join(", ", stored) : "없음")}");
        if (dropped.Count > 0)
        {
            text.AppendLine("<b>바닥 무기</b> <color=#AEBCCD>(파츠 유지 · 주우면 그대로)</color>");
            foreach (var weapon in dropped)
            {
                var settings = gameManager.ResolveSettings(weapon);
                text.AppendLine($"  {WeaponInfo.DisplayName(weapons, weapon.WeaponId)} · 장전 {weapon.Magazine} / {settings.magazineSize} · 파츠: {PartList(weapon)}");
            }
        }
        return text.ToString().TrimEnd();
    }

    private string EraName(string _eraId)
    {
        var era = ShopInfo.FindEra(shop, _eraId);
        return era != null ? era.name : _eraId;
    }

    private string AmmoName(string _ammoId)
    {
        var entry = AmmoInfo.Find(ammoTable, _ammoId);
        return entry != null ? entry.name : _ammoId;
    }
}
