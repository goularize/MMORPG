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
        private bool _wasMoving = false;

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

            bool isMoving = (moveX != 0 || moveY != 0);

            if (isMoving)
            {
                Vector3 moveDir = new Vector3(moveX, moveY, 0).normalized;
                transform.position += moveDir * speed * Time.deltaTime;

                SendMovementPacket(transform.position);
                _wasMoving = true;
            }
            else if (_wasMoving)
            {
                // We just stopped moving! Force one final exact position packet to the server.
                ForceSendPacket(transform.position);
                _wasMoving = false;
            }
        }

        private void SendMovementPacket(Vector3 position)
        {
            if (Time.time - _lastSendTime < SendRate) return;
            ForceSendPacket(position);
        }

        private void ForceSendPacket(Vector3 position)
        {
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
