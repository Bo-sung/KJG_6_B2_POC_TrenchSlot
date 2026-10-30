using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static int CHECKS;
    private static LogData SETTINGS;
    private static AmmoData AMMO;
    private static ShopData SHOP;

    private sealed class Rig : IDisposable
    {
        public readonly MemoryLogStore store = new MemoryLogStore(10000);
        public readonly LogSession session;
        public readonly LogGameLifecycle life;
        public readonly LogShopCollector shop;
        public readonly LogAmmoCollector ammoLog;
        public readonly AmmoInventory ammo = new AmmoInventory(AMMO);
        public readonly PlayerWallet wallet = new PlayerWallet(100000);
        public readonly List<LogWeaponSnapshot> weapons = new List<LogWeaponSnapshot>();
        public readonly List<LogShopPart> parts = new List<LogShopPart>();
        public readonly Dictionary<string, int> stats = new Dictionary<string, int>();
        public readonly Dictionary<string, int> perks = new Dictionary<string, int>();
        public readonly string ammoId = AMMO.ammoTypes[0].id;
        public double health = 50;

        public Rig(LogData _settings = null, bool _fail = false)
        {
            session = new LogSession(_settings ?? SETTINGS, _id => _fail ? throw new IOException("synthetic failure") : store);
            life = new LogGameLifecycle(session);
            shop = new LogShopCollector(life);
            ammoLog = new LogAmmoCollector(life, () => new LogAmmoSnapshot { reserves = Snapshot().player.reserves });
            ammoLog.Observed += shop.ObserveAmmo;
            life.RegisterCollector(ammoLog);
            ammo.AmmoLog = ammoLog;
            life.Start("synthetic-build", null, null, "synthetic-map");
            life.BeginWave(new LogWavePlan { waveNumber = 1, durationSeconds = 10 }, null);
            life.CompleteWave(null, null, 0, 0);
            life.SetPhase("shop");
        }

        public LogShopSnapshot Snapshot()
        {
            return new LogShopSnapshot { player = new LogPlayerSnapshot { money = wallet.Balance, health = health,
                weapons = weapons.ToArray(), reserves = AMMO.ammoTypes.Select(_entry => new LogReserveSnapshot
                { ammoTypeId = _entry.id, reserve = ammo.GetReserve(_entry.id), maximum = ammo.GetMaximum(_entry.id) }).ToArray() },
                maxHealth = 100, parts = parts.ToArray(), stats = new Dictionary<string, int>(stats), perks = new Dictionary<string, int>(perks) };
        }

        public JObject[] Events(string _type)
        {
            session.Flush();
            return store.Lines.Select(JObject.Parse).Where(_row => (string)_row["eventType"] == _type).ToArray();
        }

        public void Dispose() => life.End("return_to_title", null, null);
    }

    private static void Check(bool _value, string _message)
    {
        CHECKS++;
        if (!_value)
        {
            throw new Exception(_message);
        }
    }

    private static LogShopProduct Product(string _type, string _id, int? _price, int? _slot = null, int? _index = null)
    {
        return new LogShopProduct { itemType = _type, itemId = _id, productId = LogShopCollector.ProductId(_type, _id),
            valid = _price.HasValue, price = _price, targetSlot = _slot, offerIndex = _index };
    }

    private static LogShopOffer Offer(LogShopProduct _product, string _placement = "catalog", string _state = "available")
    {
        return new LogShopOffer { product = _product, placement = _placement, row = 0, state = _state,
            purchasable = _state == "available", failureCode = _state == "available" ? null : _state, label = "synthetic label" };
    }

    private static void Main()
    {
        SETTINGS = JsonConvert.DeserializeObject<LogData>(File.ReadAllText("POC2/Assets/Resources/LogData.json"));
        AMMO = JsonConvert.DeserializeObject<AmmoData>(File.ReadAllText("POC2/Assets/Resources/AmmoData.json"));
        SHOP = JsonConvert.DeserializeObject<ShopData>(File.ReadAllText("POC2/Assets/Resources/ShopData.json"));
        Check(SHOP.weapons.Length > 0 && SHOP.stats.Length > 0 && SHOP.ammoPacks.Length > 0, "Read current dynamic shop and ammo JSON");
        Presentation();
        Visibility();
        Purchases();
        FailureCodes();
        IsolationAndFailure();
        StaticIntegration();
        Console.WriteLine("PASS: " + CHECKS + " phase-7 non-Unity checks; no Unity API executed.");
    }

    private static void Presentation()
    {
        using (var rig = new Rig())
        {
            var weapon = Product("Weapon", SHOP.weapons[0].weaponId, SHOP.weapons[0].price, 0);
            var part = Product("Part", "synthetic-part", 5);
            var offers = new[] { Offer(weapon, "weapon_buy"), Offer(part, "part_catalog", "locked") };
            rig.shop.Present(false, rig.wallet.Balance, weapon.itemId, null, offers);
            Check(rig.Events("shop_offers_presented").Length == 0, "Closed refresh is not exposure");
            rig.shop.Present(true, rig.wallet.Balance, weapon.itemId, null, offers);
            for (int i = 0; i < 100; i++)
            {
                rig.shop.Present(true, rig.wallet.Balance, weapon.itemId, null, offers);
            }
            Check(rig.Events("shop_offers_presented").Length == 1, "Identical refresh coalesced");
            var reference = rig.shop.FindOffer("Weapon", weapon.itemId, "weapon_buy", 0);
            Check(reference != null && reference.revision == 0, "Visible product reference points to initial revision");
            var direct = rig.shop.Begin(weapon, rig.Snapshot(), null);
            rig.shop.Complete(direct, true, "", null, rig.Snapshot());
            Check(rig.Events("shop_purchase_attempt")[0]["payload"]["offer"].Type == JTokenType.Null, "Direct API call does not infer offer linkage");
            var ui = rig.shop.Begin(weapon, rig.Snapshot(), reference);
            rig.shop.Complete(ui, true, "", null, rig.Snapshot());
            Check((string)rig.Events("shop_purchase_attempt")[1]["payload"]["offer"]["offerSetId"] == reference.offerSetId, "Explicit UI link preserved");
            weapon.price++;
            rig.shop.Present(true, rig.wallet.Balance, weapon.itemId, null, offers);
            Check((int)rig.Events("shop_offers_presented").Last()["payload"]["revision"] == 1, "Price change increments revision");
            var stale = rig.shop.Begin(weapon, rig.Snapshot(), reference);
            rig.shop.Complete(stale, true, "", null, rig.Snapshot());
            Check(rig.Events("shop_purchase_attempt").Last()["payload"]["offer"].Type == JTokenType.Null, "Stale revision not linked");
            rig.wallet.Add(1);
            rig.shop.Present(true, rig.wallet.Balance, weapon.itemId, null, offers);
            Check(rig.Events("shop_offers_presented").Length == 3, "Balance change is new revision");
            rig.shop.Present(true, rig.wallet.Balance, weapon.itemId, part.itemId, new[] { offers[1] });
            Check(rig.shop.FindOffer("Weapon", weapon.itemId, "weapon_buy", 0) == null, "Offscreen/hidden product absent from actual input");
            Check((int)rig.Events("shop_offers_presented").Last()["payload"]["presentation"]["offers"].Count() == 1, "No fabricated hidden offers");
            string firstSet = reference.offerSetId;
            rig.shop.Present(false, null, null, null, null);
            rig.shop.Present(true, rig.wallet.Balance, weapon.itemId, part.itemId, offers);
            Check((string)rig.Events("shop_offers_presented").Last()["payload"]["offerSetId"] != firstSet &&
                (int)rig.Events("shop_offers_presented").Last()["payload"]["revision"] == 0, "Reopen creates new set with reset revision");
            var upgrade = Offer(Product("SlotUpgrade", SHOP.slotUpgrades.pool[0].perkId, SHOP.slotUpgrades.pool[0].price, null, 0), "upgrade", "already_purchased");
            rig.shop.Present(true, rig.wallet.Balance, null, null, new[] { upgrade });
            Check(!(bool)rig.Events("shop_offers_presented").Last()["payload"]["presentation"]["offers"][0]["purchasable"], "Bought card remains visible but not purchasable");
            upgrade.state = "sold_out";
            upgrade.product = Product("SlotUpgrade", null, null, null, 0);
            rig.shop.Present(true, rig.wallet.Balance, null, null, new[] { upgrade });
            Check(rig.Events("shop_offers_presented").Last()["payload"]["presentation"]["offers"][0]["product"]["productId"].Type == JTokenType.Null, "Sold-out placeholder has no invented product");
            Check(LogShopCollector.ProductId("Part", "a:b") != LogShopCollector.ProductId("Part:a", "b"), "Stable product IDs escape delimiters");
            if (SHOP.slotUpgrades.pool.Length > 1)
            {
                var redrawn = Offer(Product("SlotUpgrade", SHOP.slotUpgrades.pool[1].perkId, SHOP.slotUpgrades.pool[1].price, null, 0), "upgrade");
                int revision = (int)rig.Events("shop_offers_presented").Last()["payload"]["revision"];
                rig.shop.Present(true, rig.wallet.Balance, null, null, new[] { redrawn });
                Check((int)rig.Events("shop_offers_presented").Last()["payload"]["revision"] == revision + 1 &&
                    rig.shop.FindOffer("SlotUpgrade", redrawn.product.itemId, "upgrade") != null, "Slot redraw exposes new item in next revision");
            }
        }
    }

    private static void Visibility()
    {
        Check(LogShopVisibility.Overlaps(0, 10, 0, 10, 0, 10, 0, 10), "Visible row intersects viewport");
        Check(!LogShopVisibility.Overlaps(11, 20, 0, 10, 0, 10, 0, 10), "Outside scroll row excluded");
        Check(LogShopVisibility.Overlaps(9, 20, 0, 10, 0, 10, 0, 10), "Partially clipped visible row included");
        Check(!LogShopVisibility.Overlaps(10, 20, 0, 10, 0, 10, 0, 10), "Boundary contact without area excluded");
        Check(!LogShopVisibility.Overlaps(0, 0, 0, 10, 0, 10, 0, 10), "Zero layout size excluded");
        Check(!LogShopVisibility.Overlaps(0, 10, 0, 10, 0, 0, 0, 10), "Collapsed viewport excluded");
    }

    private static void Purchases()
    {
        using (var rig = new Rig())
        {
            foreach (string type in new[] { "Weapon", "Part", "Stat", "Ammo", "SlotUpgrade" })
            {
                int price = type == "Weapon" ? SHOP.weapons[0].price : type == "Stat" ? SHOP.stats[0].basePrice :
                    type == "Ammo" ? SHOP.ammoPack.price : type == "SlotUpgrade" ? SHOP.slotUpgrades.pool[0].price : 5;
                string id = type == "Weapon" ? SHOP.weapons[0].weaponId : type == "Stat" ? SHOP.stats[0].id :
                    type == "Ammo" ? rig.ammoId : type == "SlotUpgrade" ? SHOP.slotUpgrades.pool[0].perkId : "synthetic-part";
                var product = Product(type, id, price, type == "Weapon" ? 0 : (int?)null, type == "SlotUpgrade" ? 0 : (int?)null);
                string placement = type == "Weapon" ? "weapon_buy" : type == "Part" ? "part_buy" :
                    type == "Stat" ? "stat" : type == "Ammo" ? "ammo" : "upgrade";
                rig.shop.Present(true, rig.wallet.Balance, null, null, new[] { Offer(product, placement) });
                var attempt = rig.shop.Begin(product, rig.Snapshot(), rig.shop.FindOffer(type, id, placement, product.targetSlot));
                Check(rig.wallet.TrySpend(price), "Controlled " + type + " payment");
                if (type == "Weapon")
                {
                    string weaponId = rig.session.GetWeaponId(new object());
                    rig.weapons.Add(new LogWeaponSnapshot { instanceId = weaponId, weaponTypeId = id, loadedAmmo = 6 });
                    rig.ammoLog.Change(new LogAmmoChange { source = "new_weapon", ammoTypeId = rig.ammoId, acquired = 6, loadedDelta = 6, causeId = attempt.AttemptId });
                }
                else if (type == "Part")
                {
                    rig.parts.Add(new LogShopPart { instanceId = rig.session.SessionId + ":part:1", partId = id });
                }
                else if (type == "Stat")
                {
                    rig.stats[id] = 1;
                    rig.health += 3;
                }
                else if (type == "Ammo")
                {
                    int room = rig.ammo.GetMaximum(rig.ammoId) - rig.ammo.GetReserve(rig.ammoId);
                    rig.ammo.Add(rig.ammoId, room + 100, "shop_pack", attempt.AttemptId);
                }
                else
                {
                    rig.perks[id] = 1;
                }
                rig.shop.Complete(attempt, true, "", null, rig.Snapshot());
                var result = rig.Events("shop_purchase_result").Last()["payload"];
                Check((bool)result["success"] && (int)result["actualSpent"] == price && (string)result["attemptId"] == attempt.AttemptId, "Actual " + type + " result linked");
                if (type == "Weapon")
                {
                    Check(result["createdWeaponIds"].Count() == 1 && (int)result["ammoChanges"][0]["acquired"] == 6, "New weapon ID and loaded grant captured");
                }
                if (type == "Part")
                {
                    Check(result["createdPartIds"].Count() == 1, "Actual new part instance captured");
                }
                if (type == "Stat")
                {
                    Check((int)result["after"]["stats"][id] == 1 && (double)result["actualHealthRestored"] == 3, "Applied stat and actual health delta");
                }
                if (type == "Ammo")
                {
                    Check((int)result["ammoChanges"][0]["acquired"] == AMMO.ammoTypes[0].maxReserve - AMMO.ammoTypes[0].startingReserve,
                        "Pack grant uses real cap not nominal quantity");
                }
                if (type == "SlotUpgrade")
                {
                    Check((int)result["after"]["perks"][id] == 1, "Applied upgrade stack captured");
                }
                rig.shop.Complete(attempt, true, "", null, rig.Snapshot());
            }
            Check(rig.Events("shop_purchase_result").Length == 5, "Repeated completion is idempotent");
            var repeat = rig.shop.Begin(Product("Part", "synthetic-part", 5), rig.Snapshot(), null);
            rig.wallet.TrySpend(5);
            rig.parts.Add(new LogShopPart { instanceId = rig.session.SessionId + ":part:2", partId = "synthetic-part" });
            rig.shop.Complete(repeat, true, "", null, rig.Snapshot());
            Check((string)rig.Events("shop_purchase_result").Last()["payload"]["createdPartIds"].Single() == rig.session.SessionId + ":part:2", "Repeated part purchase records new copy only");
            int attempts = rig.Events("shop_purchase_attempt").Length;
            rig.parts[0].attachedWeaponInstanceId = rig.weapons[0].instanceId;
            Check(rig.Events("shop_purchase_attempt").Length == attempts, "Free attach does not create purchase");
            rig.ammo.Add(rig.ammoId, 1, "slot_reserve", "synthetic-spin");
            Check(rig.Events("shop_purchase_attempt").Length == attempts, "Slot/inventory events do not create purchase");
            rig.life.End("return_to_title", null, null);
            File.WriteAllLines("Docs/로그-시스템-7차.synthetic.jsonl", rig.store.Lines);
        }
    }

    private static void FailureCodes()
    {
        using (var rig = new Rig())
        {
            foreach (var pair in new Dictionary<string, string>
            {
                ["정비 시간에만 구매할 수 있습니다."] = "shop_closed",
                ["소지금이 부족합니다."] = "insufficient_funds",
                ["아직 판매하지 않는 파츠입니다."] = "locked",
                ["교체할 슬롯을 선택하세요."] = "invalid_target_slot",
                ["이미 최대 예비탄입니다."] = "maximum_reserve",
                ["품절입니다."] = "sold_out",
                ["이미 구매했습니다."] = "already_purchased",
                ["무기 픽업 프리팹을 찾지 못했습니다."] = "missing_pickup_prefab",
                ["교체한 무기를 놓을 빈 바닥이 없습니다."] = "no_drop_space",
                ["탄약 구매 처리 중입니다."] = "purchase_in_progress",
                ["보충할 예비탄이 없습니다."] = "no_effect"
            })
            {
                var product = Product("Ammo", rig.ammoId, SHOP.ammoPack.price);
                if (pair.Value == "shop_closed")
                {
                    rig.life.SetPhase("wave");
                }
                var attempt = rig.shop.Begin(product, rig.Snapshot(), null);
                string code = LogShopCollector.ClassifyFailure(pair.Key, product);
                rig.shop.Complete(attempt, true, pair.Key, code, rig.Snapshot());
                var result = rig.Events("shop_purchase_result").Last()["payload"];
                Check(!(bool)result["success"] && (string)result["failureCode"] == pair.Value && (string)result["displayReason"] == pair.Key, "Failure code and existing display preserved");
                Check((int)result["actualSpent"] == 0 && result["product"]["price"].Type == JTokenType.Integer, "Failure keeps actual zero spending versus quoted price");
                if (pair.Value == "shop_closed")
                {
                    Check((string)rig.Events("shop_purchase_result").Last()["phase"] == "wave", "Outside-shop call retains real occurrence phase");
                    rig.life.SetPhase("shop");
                }
            }
            rig.wallet.TrySpend(rig.wallet.Balance);
            var poor = rig.shop.Begin(Product("Ammo", rig.ammoId, SHOP.ammoPack.price), rig.Snapshot(), null);
            Check(!rig.wallet.TrySpend(SHOP.ammoPack.price), "Actual wallet rejects unaffordable current JSON price");
            rig.shop.Complete(poor, true, "소지금이 부족합니다.", "insufficient_funds", rig.Snapshot());
            Check((int)rig.Events("shop_purchase_result").Last()["payload"]["actualSpent"] == 0, "Actual failed payment leaves zero spent");
            var invalid = Product("Ammo", "missing", null);
            Check(LogShopCollector.ClassifyFailure("아직 판매하지 않는 탄약입니다.", invalid) == "invalid_product", "Invalid ID distinct from unlock condition");
            var attemptInvalid = rig.shop.Begin(invalid, rig.Snapshot(), null);
            rig.shop.Complete(attemptInvalid, true, "아직 판매하지 않는 탄약입니다.", "invalid_product", rig.Snapshot());
            Check(rig.Events("shop_purchase_result").Last()["payload"]["product"]["price"].Type == JTokenType.Null, "Unknown price is null not zero");
            var upgrade = Product("SlotUpgrade", "", null, null, -1);
            Check(LogShopCollector.ClassifyFailure("품절입니다.", upgrade, SHOP.slotUpgrades.offerCount, false) == "invalid_offer_index", "Invalid index distinct from sold-out display");
            upgrade.offerIndex = 0;
            upgrade.valid = true;
            Check(LogShopCollector.ClassifyFailure("이미 구매했습니다.", upgrade, SHOP.slotUpgrades.offerCount, false) == "purchase_limit", "Stack limit distinct from purchased card");
        }
    }

    private static void IsolationAndFailure()
    {
        using (var rig = new Rig())
        using (var next = new Rig())
        {
            var context = rig.shop.Begin(Product("Ammo", rig.ammoId, SHOP.ammoPack.price), rig.Snapshot(), null);
            rig.life.End("death", null, null, true);
            rig.shop.Complete(context, true, "", null, rig.Snapshot());
            next.shop.Complete(context, true, "", null, next.Snapshot());
            Check(rig.Events("shop_purchase_incomplete").Length == 1 && next.Events("shop_purchase_result").Length == 0, "Pending purchase closes on end and never leaks into restart");
        }
        using (var rig = new Rig())
        {
            var unknown = rig.shop.Begin(Product("Stat", SHOP.stats[0].id, 0), null, null);
            rig.shop.Complete(unknown, true, "", null, null);
            Check(rig.Events("shop_purchase_result").Single()["payload"]["actualSpent"].Type == JTokenType.Null, "Unknown snapshots preserve null actual amount");
            var exception = rig.shop.Begin(Product("Weapon", SHOP.weapons[0].weaponId, 1), rig.Snapshot(), null);
            rig.shop.Complete(exception, false, null, null, rig.Snapshot());
            Check((string)rig.Events("shop_purchase_result").Last()["payload"]["failureCode"] == "exception", "Thrown purchase is not mislabeled success");
            var broken = rig.shop.Begin(Product("Part", "x", 1), rig.Snapshot(), null);
            var invalid = rig.Snapshot();
            invalid.maxHealth = double.NaN;
            rig.shop.Complete(broken, true, "", null, invalid);
            Check(rig.Events("shop_purchase_incomplete").Length == 1 && rig.session.Diagnostics.FailureCount > 0, "Invalid collection fails safely and closes attempt");
        }
        var disabled = LogInfo.Copy(SETTINGS);
        disabled.enabled = false;
        using (var rig = new Rig(disabled))
        {
            Check(rig.shop.Begin(Product("Ammo", "x", 1), rig.Snapshot(), null) == null && rig.wallet.TrySpend(1), "Disabled log does not block payment");
        }
        using (var rig = new Rig(_fail: true))
        {
            Check(rig.wallet.TrySpend(1) && rig.shop.Begin(Product("Ammo", "x", 1), null, null) == null, "Storage failure does not block game model");
        }
        using (var rig = new Rig())
        {
            var product = Product("Ammo", rig.ammoId, SHOP.ammoPack.price);
            var offer = Offer(product, "ammo");
            rig.shop.Present(true, rig.wallet.Balance, null, null, new[] { offer });
            offer.label = new string('x', SETTINGS.maxRecordBytes + 1);
            rig.shop.Present(true, rig.wallet.Balance, null, null, new[] { offer });
            Check(rig.shop.FindOffer("Ammo", rig.ammoId, "ammo") == null, "Dropped presentation cannot issue a dangling offer reference");
            var huge = rig.Snapshot();
            huge.parts = new[] { new LogShopPart { instanceId = new string('x', SETTINGS.maxRecordBytes + 1) } };
            Check(rig.shop.Begin(product, huge, null) == null, "Dropped attempt is not retained as successful collection");
        }
    }

    private static void StaticIntegration()
    {
        string game = File.ReadAllText("POC2/Assets/Scripts/Game/GameManager.cs");
        foreach (string type in new[] { "Weapon", "Part", "Stat", "Ammo", "SlotUpgrade" })
        {
            int offset = game.IndexOf("public string TryBuy" + type + "(");
            string body = game.Substring(offset, game.IndexOf("\n    }", offset) - offset);
            Check(body.Contains("RunLoggedPurchase(") && body.Contains("_logOffer = null"), "All current " + type + " API returns observed and direct source compatible");
        }
        Check(!game.Contains("TryBuyHeal(") && !File.ReadAllText("POC2/Assets/Resources/ShopData.json").Contains("\"heal\""), "No paid-heal product/API restored into slot-only recovery policy");
        string adapter = File.ReadAllText("POC2/Assets/Scripts/Game/GameManager.ShopLogging.cs");
        Check(adapter.Contains("finally") && adapter.Contains("CaptureLogShop()") && adapter.Contains("_purchase(context?.AttemptId)"), "All early returns and exceptions pass result boundary");
        Check(game.Contains("ammoShop.TryBuy(_ammoId, _causeId)") && game.Contains("ObserveNewLogWeapon(bought, \"new_weapon\", _causeId)"), "Ammo and weapon grants share attempt cause");
        string presenter = File.ReadAllText("POC2/Assets/Scripts/UI/ShopPresenter.Logging.cs");
        Check(presenter.Contains("view.CaptureLogRow") && presenter.Contains("visibleParts.Count") && presenter.Contains("view.UpgradeRowCount"), "Actual row counts and selected part scope observed");
        string view = File.ReadAllText("POC2/Assets/Scripts/UI/ShopView.cs");
        Check(view.Contains("activeInHierarchy") && view.Contains("group.alpha") && view.Contains("scroll.viewport") && view.Contains("logRectMasks"), "Inactive alpha and clipped scroll rows excluded");
        foreach (string type in new[] { "Weapon", "Part", "Stat", "Ammo", "SlotUpgrade" })
        {
            Check(File.ReadAllText("POC2/Assets/Scripts/UI/ShopPresenter.cs").Contains("LogOffer(\"" + type + "\""), "UI " + type + " explicitly links visible revision");
        }
        Check(!game.Contains("gameplayLog?.ShopPurchase"), "No duplicate legacy purchase writer");
        foreach (string method in new[] { "TryAttachPart", "TryDetachPart", "TryToggleAkimbo" })
        {
            int offset = game.IndexOf("public string " + method + "(");
            string body = game.Substring(offset, game.IndexOf("\n    }", offset) - offset);
            Check(!body.Contains("RunLoggedPurchase"), "Free operation excluded from purchase boundary: " + method);
        }
        foreach (string file in new[] { "Logging/LogShopCollector.cs", "Game/GameManager.ShopLogging.cs", "UI/ShopPresenter.Logging.cs" })
        {
            Check(File.Exists("POC2/Assets/Scripts/" + file + ".meta"), "New source meta: " + file);
        }
    }
}
