using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace Client.UI
{
    public class LoadingScreenUI : MonoBehaviour
    {
        public static LoadingScreenUI Instance;

        [Header("UI References")]
        public CanvasGroup canvasGroup;
        public Slider progressBar;
        public TextMeshProUGUI statusText;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                
                if (canvasGroup == null)
                    canvasGroup = gameObject.GetComponent<CanvasGroup>();
                    
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 0;
                    canvasGroup.interactable = false;
                    canvasGroup.blocksRaycasts = false;
                }
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void Show(string initialStatus = "Loading...")
        {
            if (statusText != null) statusText.text = initialStatus;
            if (progressBar != null) progressBar.value = 0f;
            
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }

        public void Hide()
        {
            if (gameObject.activeInHierarchy)
                StartCoroutine(FadeOutRoutine());
        }

        private IEnumerator FadeOutRoutine()
        {
            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;

                float t = 0;
                while (t < 0.5f)
                {
                    t += Time.deltaTime;
                    canvasGroup.alpha = Mathf.Lerp(1f, 0f, t / 0.5f);
                    yield return null;
                }
                canvasGroup.alpha = 0f;
            }
        }

        public void UpdateProgress(float progress, string text = null)
        {
            if (progressBar != null)
            {
                progressBar.value = progress;
            }
            if (statusText != null && text != null)
            {
                statusText.text = text;
            }
        }

        public static void ShowLoading(string status = "Loading...")
        {
            if (Instance == null)
            {
                GameObject prefab = Resources.Load<GameObject>("Prefabs/UI/LoadingScreenCanvas");
                if (prefab != null)
                {
                    Instantiate(prefab);
                }
                else
                {
                    // Fallback to creating a basic one dynamically
                    GameObject go = new GameObject("LoadingScreenCanvas_Dynamic");
                    Instance = go.AddComponent<LoadingScreenUI>();
                    Instance.canvasGroup = go.AddComponent<CanvasGroup>();
                    var canvas = go.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 999;
                    go.AddComponent<GraphicRaycaster>();
                    
                    GameObject bg = new GameObject("Background");
                    bg.transform.SetParent(go.transform, false);
                    var img = bg.AddComponent<Image>();
                    img.color = Color.black;
                    var rect = bg.GetComponent<RectTransform>();
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;

                    DontDestroyOnLoad(go);
                }
            }

            if (Instance != null) Instance.Show(status);
        }
    }
}
