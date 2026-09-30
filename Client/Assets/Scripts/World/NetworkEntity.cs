using UnityEngine;

namespace Client.World
{
    public class NetworkEntity : MonoBehaviour
    {
        public int EntityId { get; private set; }
        public string EntityName { get; private set; }
        
        private Vector3 _targetPosition;
        public float moveSpeed = 10f; // Smooth interpolation speed

        public void Initialize(int id, string name, Vector3 startPos)
        {
            EntityId = id;
            EntityName = name;
            transform.position = startPos;
            _targetPosition = startPos;
        }

        public void UpdateTargetPosition(Vector3 newPos)
        {
            _targetPosition = newPos;
        }

        private void Update()
        {
            // Smoothly interpolate towards the server position
            if (Vector3.Distance(transform.position, _targetPosition) > 0.01f)
            {
                transform.position = Vector3.Lerp(transform.position, _targetPosition, Time.deltaTime * moveSpeed);
            }
        }
    }
}
