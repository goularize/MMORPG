using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Client.Network.Handlers;
using Shared.Enums;

namespace Client.UI
{
    /// <summary>
    /// One cell of the inventory window: a backpack slot or a paperdoll slot. It shows the item it is given and
    /// reports clicks, drags and hovers to the InventoryUI, which decides what they mean.
    /// </summary>
    public class InventorySlotUI : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("The cell background (tinted by the item's rarity). It must have Raycast Target on: it receives the mouse.")]
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [Tooltip("Shown on top of items with a stack; hidden for a single item.")]
        [SerializeField] private TMP_Text quantityText;
        [Tooltip("Initials of the item when there is no sprite for its icon, or the slot's name when a paperdoll slot is empty.")]
        [SerializeField] private TMP_Text labelText;
        [Tooltip("Optional. The refinement level (\"+3\").")]
        [SerializeField] private TMP_Text upgradeText;

        [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.12f);

        private InventoryUI _owner;

        public bool IsEquipment { get; private set; }

        /// <summary>The backpack slot index (backpack cells).</summary>
        public int Index { get; private set; }

        /// <summary>The paperdoll slot (equipment cells).</summary>
        public EquipmentSlot EquipSlot { get; private set; }

        public ItemView Item { get; private set; }

        public void InitBackpack(InventoryUI owner, int index)
        {
            _owner = owner;
            IsEquipment = false;
            Index = index;
        }

        public void InitEquipment(InventoryUI owner, EquipmentSlot slot)
        {
            _owner = owner;
            IsEquipment = true;
            EquipSlot = slot;
        }

        /// <param name="emptyLabel">Shown while the cell is empty (the paperdoll slot's name); null for a blank cell.</param>
        public void Show(ItemView item, string emptyLabel = null)
        {
            Item = item;

            if (item == null)
            {
                background.color = emptyColor;
                icon.enabled = false;
                quantityText.gameObject.SetActive(false);
                labelText.text = emptyLabel ?? string.Empty;
                labelText.color = new Color(1f, 1f, 1f, 0.5f);
                if (upgradeText != null) upgradeText.gameObject.SetActive(false);
                return;
            }

            var rarity = ItemFormat.RarityColor(item.Rarity);
            background.color = new Color(rarity.r, rarity.g, rarity.b, 0.45f);

            var sprite = ItemFormat.Icon(item.Icon);
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            labelText.text = sprite != null ? string.Empty : ItemFormat.Initials(item.Name);
            labelText.color = Color.white;

            quantityText.gameObject.SetActive(item.Quantity > 1);
            quantityText.text = item.Quantity.ToString();

            if (upgradeText != null)
            {
                upgradeText.gameObject.SetActive(item.UpgradeLevel > 0);
                upgradeText.text = "+" + item.UpgradeLevel;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_owner == null || Item == null || eventData.dragging) return;

            // Right click or double click: use / equip / unequip
            if (eventData.button == PointerEventData.InputButton.Right
                || (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount == 2))
            {
                _owner.Activate(this);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_owner == null || Item == null || eventData.button != PointerEventData.InputButton.Left)
            {
                eventData.pointerDrag = null; // nothing to carry
                return;
            }
            _owner.BeginDrag(this, eventData);
        }

        public void OnDrag(PointerEventData eventData) => _owner?.Drag(eventData);

        public void OnEndDrag(PointerEventData eventData) => _owner?.EndDrag(this, eventData);

        public void OnDrop(PointerEventData eventData)
        {
            var source = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<InventorySlotUI>() : null;
            if (_owner != null && source != null && source != this) _owner.Drop(source, this);
        }

        public void OnPointerEnter(PointerEventData eventData) => _owner?.Hover(this);

        public void OnPointerExit(PointerEventData eventData) => _owner?.Hover(null);
    }
}
