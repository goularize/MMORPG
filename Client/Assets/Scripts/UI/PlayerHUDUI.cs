using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Client.UI
{
    public class PlayerHUDUI : MonoBehaviour
    {
        public static PlayerHUDUI Instance;

        [Header("UI References")]
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI levelText;
        public Slider healthSlider;
        public TextMeshProUGUI healthText;
        public Slider manaSlider;
        public TextMeshProUGUI manaText;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (nameText != null)
            {
                nameText.text = CharacterSelectionUI.SelectedCharacterName;
            }
        }

        public void UpdateVitals(int currentHealth, int maxHealth, int currentMana, int maxMana)
        {
            if (healthSlider != null && maxHealth > 0)
            {
                healthSlider.value = Mathf.Clamp01((float)currentHealth / maxHealth);
            }

            if (healthText != null)
            {
                healthText.text = $"{currentHealth} / {maxHealth}";
            }

            if (manaSlider != null && maxMana > 0)
            {
                manaSlider.value = Mathf.Clamp01((float)currentMana / maxMana);
            }

            if (manaText != null)
            {
                manaText.text = $"{currentMana} / {maxMana}";
            }
        }

        public void UpdateLevel(int level)
        {
            if (levelText != null)
            {
                levelText.text = level.ToString();
            }
        }
    }
}
