using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Client.Network.Handlers;
using Shared.Enums;

namespace Client.UI
{
    /// <summary>
    /// The inventory window: the backpack grid, the paperdoll slots and the gold. It shows what InventoryHandler
    /// mirrors from the server and only asks the server to change things; the window updates when the answer arrives.
    ///
    /// Mouse: drag a cell onto another to move it (Shift+drag onto an empty cell splits a stack in half), drag an item
    /// onto its paperdoll slot to equip it, drag an equipped item onto the backpack to unequip it, drag an item
    /// outside the window to destroy it (after a confirmation). Right click or double click uses a consumable,
    /// equips equipment or unequips it. Hovering an item shows its tooltip.
    ///
    /// Put this on an always-active object (the Canvas): the panel itself starts hidden.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        public static InventoryUI Instance { get; private set; }

        [Header("Window")]
        [Tooltip("The window root. Hidden at start.")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text goldText;
        [Tooltip("Optional. \"used / total\" backpack slots.")]
        [SerializeField] private TMP_Text slotsText;
        [Tooltip("Optional. Short feedback line (\"Requires level 5\") that fades after a few seconds.")]
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private float messageSeconds = 3f;

        [Header("Backpack")]
        [Tooltip("Parent (with a Grid Layout Group) the backpack cells are created under.")]
        [SerializeField] private RectTransform gridContainer;
        [SerializeField] private InventorySlotUI slotPrefab;

        [Header("Paperdoll")]
        [Tooltip("One cell per equipment slot, in any order. Each cell is bound to its slot by the entries below.")]
        [SerializeField] private EquipmentCell[] equipmentCells;

        [Header("Destroy confirmation (optional: without it dragging outside the window does nothing)")]
        [SerializeField] private GameObject confirmPanel;
        [SerializeField] private TMP_Text confirmText;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;

        [Header("Input")]
        [SerializeField] private UnityEngine.InputSystem.Key toggleKey = UnityEngine.InputSystem.Key.I;

        [System.Serializable]
        private struct EquipmentCell
        {
            public EquipmentSlot slot;
            public InventorySlotUI cell;
        }

        private readonly System.Collections.Generic.List<InventorySlotUI> _backpackCells = new();
        private GameObject _ghost;
        private InventorySlotUI _dragSource;
        private int _pendingDestroySlot = -1;
        private float _messageUntil;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            closeButton.onClick.AddListener(Close);
            if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnConfirmDestroy);
            if (confirmNoButton != null) confirmNoButton.onClick.AddListener(CloseConfirm);

            foreach (var entry in equipmentCells) entry.cell.InitEquipment(this, entry.slot);

            panel.SetActive(false);
            if (confirmPanel != null) confirmPanel.SetActive(false);
            if (messageText != null) messageText.text = string.Empty;

            InventoryHandler.OnInventoryChanged += OnInventoryChanged;
        }

        private void OnDestroy()
        {
            InventoryHandler.OnInventoryChanged -= OnInventoryChanged;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[toggleKey].wasPressedThisFrame && !IsTyping())
                {
                    if (panel.activeSelf) Close(); else Open();
                }
                else if (panel.activeSelf && keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (confirmPanel != null && confirmPanel.activeSelf) CloseConfirm(); else Close();
                }
            }

            if (messageText != null && _messageUntil > 0f && Time.unscaledTime > _messageUntil)
            {
                messageText.text = string.Empty;
                _messageUntil = 0f;
            }
        }

        public void Open()
        {
            panel.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            CancelDrag();
            CloseConfirm();
            ItemTooltipUI.Instance?.Hide();
            panel.SetActive(false);
        }

        // Typing in the chat must not open windows
        private static bool IsTyping()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        private void OnInventoryChanged()
        {
            if (panel.activeSelf) Refresh();
        }

        private void Refresh()
        {
            EnsureBackpackCells();

            for (int i = 0; i < _backpackCells.Count; i++)
            {
                bool exists = i < InventoryHandler.SlotCount;
                _backpackCells[i].gameObject.SetActive(exists);
                if (!exists) continue;

                InventoryHandler.TryGetBackpackItem(i, out var item);
                _backpackCells[i].Show(item);
            }

            foreach (var entry in equipmentCells)
            {
                InventoryHandler.TryGetEquipped(entry.slot, out var item);
                entry.cell.Show(item, ItemFormat.SlotName(entry.slot));
            }

            goldText.text = InventoryHandler.Gold.ToString("N0");
            if (slotsText != null)
                slotsText.text = $"{InventoryHandler.SlotCount - InventoryHandler.FreeSlots} / {InventoryHandler.SlotCount}";

            // The item under the mouse may be gone or different now
            ItemTooltipUI.Instance?.Hide();
        }

        private void EnsureBackpackCells()
        {
            while (_backpackCells.Count < InventoryHandler.SlotCount)
            {
                var cell = Instantiate(slotPrefab, gridContainer);
                cell.InitBackpack(this, _backpackCells.Count);
                _backpackCells.Add(cell);
            }
        }

        // ---- Called by the cells ----

        /// <summary>Right click / double click: use a consumable, equip equipment, or unequip an equipped item.</summary>
        public void Activate(InventorySlotUI cell)
        {
            var item = cell.Item;
            if (item == null) return;

            if (cell.IsEquipment)
            {
                if (InventoryHandler.FreeSlots == 0) ShowMessage("Your backpack is full");
                else InventoryHandler.RequestUnequip(cell.EquipSlot);
                return;
            }

            switch (item.Type)
            {
                case ItemType.Consumable:
                    InventoryHandler.RequestUse(cell.Index);
                    break;

                case ItemType.Equipment:
                    TryEquip(cell.Index, item);
                    break;

                default:
                    ShowMessage($"{item.Name} cannot be used");
                    break;
            }
        }

        public void Hover(InventorySlotUI cell)
        {
            var tooltip = ItemTooltipUI.Instance;
            if (tooltip == null) return;

            if (cell != null && cell.Item != null && _dragSource == null) tooltip.Show(cell.Item);
            else tooltip.Hide();
        }

        public void BeginDrag(InventorySlotUI cell, PointerEventData eventData)
        {
            _dragSource = cell;
            ItemTooltipUI.Instance?.Hide();

            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            _ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            _ghost.transform.SetParent(canvas.transform, false);
            _ghost.GetComponent<CanvasGroup>().blocksRaycasts = false; // so the drop target below it is found

            var image = _ghost.GetComponent<Image>();
            var sprite = ItemFormat.Icon(cell.Item.Icon);
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : ItemFormat.RarityColor(cell.Item.Rarity);
            ((RectTransform)_ghost.transform).sizeDelta = ((RectTransform)cell.transform).rect.size;

            Drag(eventData);
        }

        public void Drag(PointerEventData eventData)
        {
            if (_ghost != null) _ghost.transform.position = eventData.position;
        }

        public void EndDrag(InventorySlotUI cell, PointerEventData eventData)
        {
            CancelDrag();

            // A drop on a cell was already handled by Drop. Anything else outside the window means destroy.
            if (eventData.pointerEnter == null || !IsInsidePanel(eventData.pointerEnter.transform))
            {
                if (!cell.IsEquipment) AskToDestroy(cell);
                else ShowMessage("Drag it onto the backpack to unequip it");
            }
        }

        public void Drop(InventorySlotUI source, InventorySlotUI target)
        {
            var item = source.Item;
            if (item == null) return;

            if (!source.IsEquipment && !target.IsEquipment)
            {
                bool split = (Keyboard()?.shiftKey.isPressed ?? false) && item.Quantity > 1 && target.Item == null;
                if (split) InventoryHandler.RequestSplit(source.Index, item.Quantity / 2, target.Index);
                else InventoryHandler.RequestMove(source.Index, target.Index);
            }
            else if (!source.IsEquipment && target.IsEquipment)
            {
                if (item.Type != ItemType.Equipment || item.Slot != target.EquipSlot)
                    ShowMessage($"{item.Name} does not go there");
                else
                    TryEquip(source.Index, item);
            }
            else if (source.IsEquipment && !target.IsEquipment)
            {
                if (InventoryHandler.FreeSlots == 0 && target.Item == null) ShowMessage("Your backpack is full");
                else InventoryHandler.RequestUnequip(source.EquipSlot);
            }
        }

        // ---- Internals ----

        private void TryEquip(int backpackSlot, ItemView item)
        {
            if (item.Slot == EquipmentSlot.None)
            {
                ShowMessage($"{item.Name} cannot be equipped");
            }
            else if (ProgressionHandler.Level < item.RequiredLevel)
            {
                ShowMessage($"Requires level {item.RequiredLevel}");
            }
            else
            {
                InventoryHandler.RequestEquip(backpackSlot, item.Slot);
            }
        }

        private void AskToDestroy(InventorySlotUI cell)
        {
            if (confirmPanel == null || cell.Item == null) return;

            _pendingDestroySlot = cell.Index;
            confirmText.text = cell.Item.Quantity > 1
                ? $"Destroy {cell.Item.Quantity}x {cell.Item.Name}?"
                : $"Destroy {cell.Item.Name}?";
            confirmPanel.SetActive(true);
        }

        private void OnConfirmDestroy()
        {
            // The item may have moved while the question was open: ask about what is there now
            if (_pendingDestroySlot >= 0 && InventoryHandler.TryGetBackpackItem(_pendingDestroySlot, out var item))
                InventoryHandler.RequestDrop(_pendingDestroySlot, item.Quantity);

            CloseConfirm();
        }

        private void CloseConfirm()
        {
            _pendingDestroySlot = -1;
            if (confirmPanel != null) confirmPanel.SetActive(false);
        }

        private void CancelDrag()
        {
            _dragSource = null;
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
        }

        private bool IsInsidePanel(Transform transform) => transform.IsChildOf(panel.transform);

        private void ShowMessage(string text)
        {
            if (messageText == null) return;

            messageText.text = text;
            _messageUntil = Time.unscaledTime + messageSeconds;
        }

        private static UnityEngine.InputSystem.Keyboard Keyboard() => UnityEngine.InputSystem.Keyboard.current;
    }
}
