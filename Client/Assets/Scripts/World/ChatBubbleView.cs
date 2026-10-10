using UnityEngine;
using TMPro;

namespace Client.World
{
    /// <summary>A speech bubble over a character's head: stays for a while, fades out, then removes itself.</summary>
    public class ChatBubbleView : MonoBehaviour
    {
        public const string ObjectName = "ChatBubble";

        private TMP_Text _text;
        private float _lifetime;
        private float _fadeSeconds;
        private float _age;

        public void Setup(TMP_Text text, float lifetime, float fadeSeconds)
        {
            _text = text;
            _lifetime = lifetime;
            _fadeSeconds = Mathf.Max(0.01f, fadeSeconds);
        }

        private void Update()
        {
            _age += Time.deltaTime;

            float remaining = _lifetime - _age;
            if (remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (remaining < _fadeSeconds) _text.alpha = remaining / _fadeSeconds;
        }
    }
}
