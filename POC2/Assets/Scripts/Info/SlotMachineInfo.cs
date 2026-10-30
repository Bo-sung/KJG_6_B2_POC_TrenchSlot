using UnityEngine;

// 슬롯머신 당첨 표 조회·판정. 릴 3개는 각각 독립 추첨하며, 결과 한 줄을 3줄 일치·2줄 일치로 판정한다.
public static class SlotMachineInfo
{
    // 릴 모델의 면 개수. 면 회전 각도(면 번호 x 60도)가 이 값에 맞춰져 있다.
    public const int REEL_FACE_COUNT = 6;
    public const int REEL_COUNT = 3;
    // 탄약 심볼 id. 3줄이면 예비탄을 전량 충전한다.
    public const string AMMO_ID = "ammo";

    public static int SpinCost(SlotMachineData _table) => _table != null ? _table.spinCost : 0;
    public static float SpinHealAmount(SlotMachineData _table) => _table != null ? Mathf.Max(0f, _table.spinHealAmount) : 0f;

    public static float SpinSeconds(SlotMachineData _table) => _table != null ? _table.spinSeconds : 0f;

    // 릴 면 개수. 심볼 수와 같으며 REEL_FACE_COUNT와 맞아야 한다.
    public static int FaceCount(SlotMachineData _table) => _table?.symbols == null ? 0 : _table.symbols.Length;

    // 심볼 한 칸의 가중치. 탄약 심볼에만 _ammoWeightScale을 곱한다(탄약 심볼 퍽).
    public static int SymbolWeight(SlotMachineData.Symbol _symbol, int _ammoWeightScale)
    {
        if (_symbol == null)
        {
            return 0;
        }
        return _symbol.id == AMMO_ID ? _symbol.weight * Mathf.Max(1, _ammoWeightScale) : _symbol.weight;
    }

    // 릴 한 칸의 전체 가중치. 미스와 심볼 가중치의 합이며 고정 합(v4 2500 등)을 가정하지 않는다.
    public static int TotalWeight(SlotMachineData _table, int _ammoWeightScale)
    {
        if (_table == null || _table.symbols == null)
        {
            return 0;
        }
        int total = _table.missWeight;
        foreach (var symbol in _table.symbols)
        {
            total += SymbolWeight(symbol, _ammoWeightScale);
        }
        return total;
    }

    // 0 이상 TotalWeight 미만의 롤을 면 번호로 바꾼다. 미스(가중치 구간 밖)이면 -1.
    public static int PickFace(SlotMachineData _table, int _roll, int _ammoWeightScale)
    {
        if (_table == null || _table.symbols == null)
        {
            return -1;
        }
        if (_roll < _table.missWeight)
        {
            return -1;
        }
        int cursor = _table.missWeight;
        foreach (var symbol in _table.symbols)
        {
            if (symbol == null)
            {
                continue;
            }
            cursor += SymbolWeight(symbol, _ammoWeightScale);
            if (_roll < cursor)
            {
                return symbol.reelIndex;
            }
        }
        return -1;
    }

    public static SlotMachineData.Symbol FindByFace(SlotMachineData _table, int _face)
    {
        if (_table == null || _table.symbols == null)
        {
            return null;
        }
        foreach (var symbol in _table.symbols)
        {
            if (symbol != null && symbol.reelIndex == _face)
            {
                return symbol;
            }
        }
        return null;
    }

    public static string SymbolName(SlotMachineData _table, int _face)
    {
        var symbol = FindByFace(_table, _face);
        return symbol != null ? symbol.name : "없음";
    }

    // 결과 한 줄(3릴 면 번호)을 판정한다. 3줄 일치가 우선이며, 2줄 일치는 배수가 있을 때만 지급한다.
    public static SlotOutcome Evaluate(SlotMachineData _table, int[] _faces)
    {
        var outcome = new SlotOutcome { tripleFace = -1, pairFace = -1, multiplier = 0, hasAmmo = false, isNearMiss = false };
        if (_table == null || _faces == null || _faces.Length != REEL_COUNT)
        {
            return outcome;
        }

        if (_faces[0] == _faces[1] && _faces[1] == _faces[2])
        {
            outcome.tripleFace = _faces[0];
            var symbol = FindByFace(_table, _faces[0]);
            outcome.multiplier = symbol != null ? symbol.multiplier : 0;
            outcome.hasAmmo = symbol != null && symbol.fullAmmo;
            outcome.partCount = symbol != null ? Mathf.Max(0, symbol.parts) : 0;
            outcome.nextEraPartCount = symbol != null ? Mathf.Max(0, symbol.nextEraParts) : 0;
            outcome.isMiss = symbol == null || (outcome.multiplier == 0 && !outcome.hasAmmo && outcome.partCount == 0 && outcome.nextEraPartCount == 0);
            return outcome;
        }

        for (int i = 0; i < REEL_COUNT; i++)
        {
            int j = (i + 1) % REEL_COUNT;
            if (_faces[i] != _faces[j])
            {
                continue;
            }
            outcome.pairFace = _faces[i];
            var symbol = FindByFace(_table, _faces[i]);
            outcome.multiplier = symbol != null ? symbol.pairMultiplier : 0;
            break;
        }

        // 2줄이 맞았지만 지급이 없는 조합은 근접 미스로 본다(효과음 연출용).
        outcome.isNearMiss = outcome.pairFace >= 0 && outcome.multiplier == 0;
        outcome.isMiss = outcome.multiplier == 0;
        return outcome;
    }

    public static SlotMachineData.Symbol FindById(SlotMachineData _table, string _id)
    {
        if (_table == null || _table.symbols == null)
        {
            return null;
        }
        foreach (var symbol in _table.symbols)
        {
            if (symbol != null && symbol.id == _id)
            {
                return symbol;
            }
        }
        return null;
    }

    // 안내 문구. 3줄 배수와 2줄 배수를 심볼마다 한 줄씩 보여준다.
    public static string PayoutText(SlotMachineData _table)
    {
        if (_table == null || _table.symbols == null)
        {
            return "";
        }
        string text = "";
        foreach (var symbol in _table.symbols)
        {
            if (symbol == null)
            {
                continue;
            }
            if (text.Length > 0)
            {
                text += "\n";
            }
            text += $"{symbol.name} x{symbol.multiplier}";
            if (symbol.pairMultiplier > 0)
            {
                text += $" (2줄 x{symbol.pairMultiplier})";
            }
        }
        return text;
    }

    // 표 형식 검사. 문제가 있으면 원인을 로그로 남기고 중단하지 않는다.
    public static void Validate(SlotMachineData _table)
    {
        if (_table == null || _table.symbols == null || _table.symbols.Length == 0)
        {
            Debug.LogError("슬롯머신 테이블에 심볼이 없습니다.");
            return;
        }
        if (_table.symbols.Length != REEL_FACE_COUNT)
        {
            Debug.LogError($"슬롯머신 심볼은 릴 면 {REEL_FACE_COUNT}개와 같아야 합니다(현재 {_table.symbols.Length}개).");
        }
        if (_table.spinCost < 0)
        {
            Debug.LogError("슬롯머신 spinCost가 음수입니다.");
        }
        if (_table.spinHealAmount < 0f || float.IsNaN(_table.spinHealAmount) || float.IsInfinity(_table.spinHealAmount))
        {
            Debug.LogError("슬롯머신 spinHealAmount가 올바르지 않습니다.");
        }
        if (_table.heldRewardChance < 0f || _table.heldRewardChance > 1f || _table.missAmmoFraction <= 0f || _table.missAmmoFraction > 1f || _table.fullAmmoCash < 0)
        {
            Debug.LogError("슬롯 현물 보상 설정이 올바르지 않습니다.");
        }
        if (_table.spinSeconds <= 0f)
        {
            Debug.LogError("슬롯머신 spinSeconds는 0보다 커야 합니다.");
        }

        bool[] seen = new bool[REEL_FACE_COUNT];
        foreach (var symbol in _table.symbols)
        {
            if (symbol == null)
            {
                Debug.LogError("슬롯머신 테이블에 빈 심볼이 있습니다.");
                continue;
            }
            if (symbol.reelIndex < 0 || symbol.reelIndex >= seen.Length)
            {
                Debug.LogError($"슬롯머신 심볼 {symbol.id}의 reelIndex({symbol.reelIndex})가 범위를 벗어났습니다.");
            }
            else if (seen[symbol.reelIndex])
            {
                Debug.LogError($"슬롯머신 심볼 {symbol.id}의 reelIndex({symbol.reelIndex})가 중복됩니다.");
            }
            else
            {
                seen[symbol.reelIndex] = true;
            }
            if (symbol.weight < 0 || symbol.multiplier < 0 || symbol.pairMultiplier < 0 || symbol.parts < 0 || symbol.nextEraParts < 0)
            {
                Debug.LogError($"슬롯머신 심볼 {symbol.id}의 weight 또는 배수가 음수입니다.");
            }
        }
        if (_table.missWeight < 0 || TotalWeight(_table, 1) <= 0)
        {
            Debug.LogError("슬롯머신 테이블의 전체 가중치가 올바르지 않습니다.");
        }
    }
}
