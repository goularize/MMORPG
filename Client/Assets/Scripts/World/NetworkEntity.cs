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

        /// <summary>
        /// Decides how this remote entity collides with the local player. The server owns its position (we only
        /// interpolate towards it), so it must never be pushed by physics; it is either solid or fully passable.
        /// Enemies, NPCs and resources are always solid, loot satchels are always passable, and other players are
        /// solid only on PvP maps. Passable entities keep their collider as a trigger, so mouse targeting still hits them.
        /// </summary>
        public void ConfigureCollision(Shared.Enums.EntityType type, bool pvpMap)
        {
            bool solid = type switch
            {
                Shared.Enums.EntityType.Player => pvpMap,
                Shared.Enums.EntityType.LootSatchel => false,
                _ => true
            };

            // A dynamic body gets velocity from every contact and, with no damping, keeps drifting while we also
            // write its transform: that was the "bumping" when two characters overlapped
            var body = GetComponent<Rigidbody2D>();
            if (body != null && body.bodyType == RigidbodyType2D.Dynamic)
            {
                body.bodyType = RigidbodyType2D.Kinematic;
            }

            foreach (var col in GetComponentsInChildren<Collider2D>())
            {
                col.isTrigger = !solid;
            }
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
