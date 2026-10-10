using UnityEngine;

namespace Client.World
{
    /// <summary>The marker object over an NPC's head; it only bobs up and down. Created by QuestMarkerController.</summary>
    public class QuestMarkerView : MonoBehaviour
    {
        public const string ObjectName = "QuestMarker";

        private Vector3 _basePosition;
        private float _bobHeight;
        private float _bobSpeed;
        private float _phase;

        public void Setup(Vector3 localPosition, float bobHeight, float bobSpeed)
        {
            _basePosition = localPosition;
            _bobHeight = bobHeight;
            _bobSpeed = bobSpeed;
            _phase = Random.value * Mathf.PI * 2f; // markers of neighbouring NPCs do not move in lockstep
            transform.localPosition = localPosition;
        }

        private void Update()
        {
            float offset = Mathf.Sin(Time.time * _bobSpeed + _phase) * _bobHeight;
            transform.localPosition = _basePosition + new Vector3(0f, offset, 0f);
        }
    }
}
