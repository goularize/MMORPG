using UnityEngine;
using Client.Network;
using Shared.Network;

namespace Client.World
{
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        public float speed = 5f;

        private float _lastSendTime;
        private const float SendRate = 0.1f; // Send movement 10 times a second

        private void Update()
        {
            HandleMovement();
        }

        private void HandleMovement()
        {
            float moveX = 0f;
            float moveY = 0f;

            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                if (UnityEngine.InputSystem.Keyboard.current.wKey.isPressed || UnityEngine.InputSystem.Keyboard.current.upArrowKey.isPressed) moveY += 1f;
                if (UnityEngine.InputSystem.Keyboard.current.sKey.isPressed || UnityEngine.InputSystem.Keyboard.current.downArrowKey.isPressed) moveY -= 1f;
                if (UnityEngine.InputSystem.Keyboard.current.dKey.isPressed || UnityEngine.InputSystem.Keyboard.current.rightArrowKey.isPressed) moveX += 1f;
                if (UnityEngine.InputSystem.Keyboard.current.aKey.isPressed || UnityEngine.InputSystem.Keyboard.current.leftArrowKey.isPressed) moveX -= 1f;
            }

            if (moveX != 0 || moveY != 0)
            {
                Vector3 moveDir = new Vector3(moveX, moveY, 0).normalized;
                transform.position += moveDir * speed * Time.deltaTime;

                SendMovementPacket(transform.position);
            }
        }

        private void SendMovementPacket(Vector3 position)
        {
            if (Time.time - _lastSendTime < SendRate) return;
            _lastSendTime = Time.time;

            using (Packet packet = new Packet(OpCode.PlayerMoveRequest))
            {
                packet.Write(position.x);
                packet.Write(position.y);
                packet.Write(position.z);
                NetworkManager.Instance.SendPacket(packet);
            }
        }
    }
}
