using UnityEngine;

public sealed class AmmoPickup : MonoBehaviour
{
    [SerializeField] private AmmoPickupView view;

    private GameManager owner;
    private bool claimed;

    public string AmmoId { get; private set; }
    public int Amount { get; private set; }
    public bool IsClaimed => claimed;
    public Vector3 CollectionPoint => view != null ? view.CollectionPoint : transform.position;

    public void Initialize(GameManager _owner, AmmoData.Entry _ammo, int _amount)
    {
        owner = _owner;
        AmmoId = _ammo.id;
        Amount = Mathf.Max(0, _amount);
        claimed = false;
        if (view != null)
        {
            ColorUtility.TryParseHtmlString(_ammo.color, out Color tint);
            view.SetColor(tint);
        }
    }

    private void Update()
    {
        if (owner != null && !claimed)
        {
            owner.TryCollectAmmo(this);
        }
    }

    public bool TryClaim()
    {
        if (claimed || !isActiveAndEnabled || Amount <= 0)
        {
            return false;
        }
        claimed = true;
        return true;
    }

    public void ReleaseClaim()
    {
        claimed = false;
    }

    public void Remove()
    {
        claimed = true;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
