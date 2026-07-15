using UnityEngine;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// Shows the currently selected melee weapon on the character's RightHand bone.
/// Synced via NetworkVariable so remote players see it too.
/// </summary>
public class HeldItemVisual : NetworkBehaviour
{
    [Header("Hand")]
    [SerializeField] private string handBoneName = "RightHand";
    [SerializeField] private Transform handOverride;

    [Header("Catalog (optional — also loaded from Resources/ItemDatabase)")]
    [SerializeField] private ItemDatabase itemDatabase;
    [SerializeField] private ItemData[] extraCatalog;

    private InventoryManager _inventory;
    private PlayerController _player;
    private GameObject _heldInstance;
    private ItemData _heldItemData;
    private string _appliedItemId = string.Empty;

    private readonly NetworkVariable<FixedString64Bytes> heldItemId = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public override void OnNetworkSpawn()
    {
        _player = GetComponent<PlayerController>();
        _inventory = GetComponentInChildren<InventoryManager>(true);

        if (itemDatabase == null)
            itemDatabase = Resources.Load<ItemDatabase>("ItemDatabase");

        heldItemId.OnValueChanged += OnHeldItemIdChanged;

        if (IsOwner && _inventory != null)
        {
            _inventory.OnItemSelected += OnLocalItemSelected;
            _inventory.OnItemDeselected += OnLocalItemDeselected;
        }

        if (_player != null)
            _player.characterIndex.OnValueChanged += OnCharacterIndexChanged;

        ApplyHeldVisual(heldItemId.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        heldItemId.OnValueChanged -= OnHeldItemIdChanged;

        if (_inventory != null)
        {
            _inventory.OnItemSelected -= OnLocalItemSelected;
            _inventory.OnItemDeselected -= OnLocalItemDeselected;
        }

        if (_player != null)
            _player.characterIndex.OnValueChanged -= OnCharacterIndexChanged;

        ClearHeldInstance();
    }

    private void OnCharacterIndexChanged(int previous, int current)
    {
        // Hand bone changes with avatar — rebuild visual.
        ApplyHeldVisual(heldItemId.Value.ToString());
    }

    private void OnLocalItemSelected(ItemData item, int slotIndex)
    {
        if (item == null || !item.CanHoldInHand)
        {
            heldItemId.Value = default;
            return;
        }

        if (itemDatabase != null)
            itemDatabase.Register(item);

        heldItemId.Value = new FixedString64Bytes(item.itemID);
    }

    private void OnLocalItemDeselected()
    {
        heldItemId.Value = default;
    }

    private void OnHeldItemIdChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        ApplyHeldVisual(current.ToString());
    }

    private void LateUpdate()
    {
        // Play Mode'da ItemData Held Local Position/Euler anında yansısın.
        if (_heldInstance == null || _heldItemData == null) return;
        ApplyHeldPose(_heldItemData);
    }

    private void ApplyHeldPose(ItemData item)
    {
        if (_heldInstance == null || item == null) return;
        int charIndex = _player != null ? _player.characterIndex.Value : 1;
        item.GetHeldPose(charIndex, out Vector3 pos, out Vector3 euler, out Vector3 scale);
        _heldInstance.transform.localPosition = pos;
        _heldInstance.transform.localRotation = Quaternion.Euler(euler);
        _heldInstance.transform.localScale = scale;
    }

    private void ApplyHeldVisual(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || itemId == "\0")
        {
            ClearHeldInstance();
            _appliedItemId = string.Empty;
            return;
        }

        // Null-terminated FixedString can include junk; trim.
        itemId = itemId.Trim('\0', ' ');

        if (itemId == _appliedItemId && _heldInstance != null)
        {
            // Keep ItemData ref so LateUpdate still live-tunes pose.
            if (_heldItemData == null)
                _heldItemData = ResolveItem(itemId);
            return;
        }

        ItemData item = ResolveItem(itemId);
        if (item == null || !item.CanHoldInHand)
        {
            ClearHeldInstance();
            _appliedItemId = string.Empty;
            return;
        }

        Transform hand = ResolveHand();
        if (hand == null)
        {
            ClearHeldInstance();
            _appliedItemId = string.Empty;
            Debug.LogWarning("[HeldItemVisual] RightHand bone not found yet (character selected?).");
            return;
        }

        ClearHeldInstance();

        GameObject prefab = item.GetHeldVisualPrefab();
        if (prefab == null) return;

        _heldInstance = Instantiate(prefab, hand);
        _heldInstance.name = $"Held_{item.itemID}";
        StripWorldOnlyComponents(_heldInstance);
        _heldItemData = item;
        ApplyHeldPose(item);

        _appliedItemId = itemId;
    }

    private ItemData ResolveItem(string itemId)
    {
        if (itemDatabase != null)
        {
            var fromDb = itemDatabase.Get(itemId);
            if (fromDb != null) return fromDb;
        }

        if (extraCatalog != null)
        {
            foreach (var item in extraCatalog)
            {
                if (item != null && item.itemID == itemId)
                    return item;
            }
        }

        // Local owner: inventory already has a live reference
        if (IsOwner && _inventory != null && _inventory.SelectedItem != null &&
            _inventory.SelectedItem.itemID == itemId)
            return _inventory.SelectedItem;

        return null;
    }

    private Transform ResolveHand()
    {
        if (handOverride != null)
            return handOverride;

        GameObject activeModel = null;
        if (_player != null)
        {
            int idx = _player.characterIndex.Value;
            if (idx == 0) activeModel = _player.modelA;
            else if (idx == 1) activeModel = _player.modelB;
        }

        if (activeModel != null && activeModel.activeInHierarchy)
        {
            var hand = FindDeepChild(activeModel.transform, handBoneName);
            if (hand != null) return hand;
        }

        return FindDeepChild(transform, handBoneName);
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindDeepChild(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private void ClearHeldInstance()
    {
        if (_heldInstance != null)
        {
            Destroy(_heldInstance);
            _heldInstance = null;
        }
        _heldItemData = null;
    }

    private static void StripWorldOnlyComponents(GameObject root)
    {
        // Destroy order: network first
        foreach (var no in root.GetComponentsInChildren<NetworkObject>(true))
            Destroy(no);

        foreach (var pickup in root.GetComponentsInChildren<ItemPickUp>(true))
            Destroy(pickup);

        // Optional package component — avoid hard compile dep if missing
        var netTransforms = root.GetComponentsInChildren<Component>(true);
        foreach (var c in netTransforms)
        {
            if (c != null && c.GetType().Name == "NetworkTransform")
                Destroy(c);
        }

        foreach (var col in root.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            Destroy(rb);
        }
    }
}
