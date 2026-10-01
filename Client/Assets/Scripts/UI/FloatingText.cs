using UnityEngine;
using TMPro;

namespace Client.UI
{
    public class FloatingText : MonoBehaviour
    {
        [Header("Components")]
        public TextMeshPro textMesh;

        [Header("Settings")]
        public float moveSpeed = 1.5f;
        public float duration = 1.5f;
        
        private Color _textColor;
        private float _timeAlive = 0f;

        public void Setup(string text, Color color, bool isCrit)
        {
            if (textMesh == null) textMesh = GetComponent<TextMeshPro>();
            
            textMesh.text = text;
            textMesh.color = color;
            _textColor = color;

            // Make Crits significantly larger
            if (isCrit)
            {
                textMesh.fontSize *= 1.5f;
                // Add a slight pop/scale effect if desired
                transform.localScale = Vector3.one * 1.2f;
            }
        }

        private void Update()
        {
            // Float upward continuously
            transform.position += Vector3.up * moveSpeed * Time.deltaTime;

            _timeAlive += Time.deltaTime;

            // Fade out smoothly during the second half of its life
            if (_timeAlive > duration / 2f)
            {
                float fadePercent = (_timeAlive - (duration / 2f)) / (duration / 2f);
                _textColor.a = Mathf.Lerp(1f, 0f, fadePercent);
                textMesh.color = _textColor;
            }

            // Destroy when duration is completely up
            if (_timeAlive >= duration)
            {
                Destroy(gameObject);
            }
        }
    }
}
