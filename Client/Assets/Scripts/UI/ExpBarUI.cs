using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Client.UI
{
    public class ExpBarUI : MonoBehaviour
    {
        public static ExpBarUI Instance;

        [Header("UI References")]
        public Slider expSlider;
        public TextMeshProUGUI expText;

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
            if (Network.Handlers.ProgressionHandler.ExpToNextLevel > 0)
            {
                UpdateExp(Network.Handlers.ProgressionHandler.CurrentExp, Network.Handlers.ProgressionHandler.ExpToNextLevel);
            }
            else
            {
                UpdateExp(0, 100);
            }
        }

        public void UpdateExp(long currentExp, long expToNextLevel)
        {
            if (expSlider != null)
            {
                expSlider.value = Mathf.Clamp01((float)currentExp / expToNextLevel);
            }

            if (expText != null)
            {
                float percentage = ((float)currentExp / expToNextLevel) * 100f;
                expText.text = $"{currentExp:N0} / {expToNextLevel:N0} ({percentage:F1}%)";
            }
        }
    }
}
