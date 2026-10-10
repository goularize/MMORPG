using UnityEngine;
using UnityEngine.EventSystems;
using Client.Network;
using Shared.Network;

namespace Client.World
{
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        public float speed = Shared.Constants.GameRules.BasePlayerMoveSpeed;

        private float _lastSendTime;
        private const float SendRate = 0.1f; // Send movement 10 times a second
        private bool _wasMoving = false;
        private Vector2 _moveInput;
        private Rigidbody2D _rb;

        [Header("Combat")]
        public float attackCooldown = Shared.Constants.GameRules.BasePlayerAttackInterval;
        private float _lastAttackTime;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            // The server ignores everything a dead character does, so do not let it walk or attack locally either
            // (it would drift away from its real position). The respawn flow itself is #171.
            if (Client.Network.Handlers.WorldHandler.LocalHealth <= 0)
            {
                _moveInput = Vector2.zero;
                _wasMoving = false;
                return;
            }

            HandleInput();
            HandleCombatInput();
        }

        private void FixedUpdate()
        {
            // Physical movement must happen in FixedUpdate for Unity Collisions to work smoothly
            if (_moveInput != Vector2.zero)
            {
                if (_rb != null)
                {
                    _rb.MovePosition(_rb.position + _moveInput * speed * Time.fixedDeltaTime);
                }
                else
                {
                    transform.position += (Vector3)_moveInput * speed * Time.fixedDeltaTime;
                }
            }
        }

        private void HandleInput()
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

            _moveInput = new Vector2(moveX, moveY).normalized;
            bool isMoving = (_moveInput != Vector2.zero);

            if (isMoving)
            {
                // Send the exact position to the server periodically
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

        private void HandleCombatInput()
        {
            if (UnityEngine.InputSystem.Mouse.current == null) return;

            var mouse = UnityEngine.InputSystem.Mouse.current;

            // Allow the player to hold the mouse down to auto-attack, or spam click
            bool attackHeld = mouse.leftButton.isPressed;
            bool interactClicked = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
            if (!attackHeld && !interactClicked) return;

            // Clicks on windows (dialogue, HUD) must not reach the world behind them
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            // Raycast to find an entity under the mouse cursor
            Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
            RaycastHit2D hit = Physics2D.Raycast(mouseWorldPos, Vector2.zero);
            if (hit.collider == null) return;

            // Check if it's a remote entity (monsters/other players will have this script)
            var targetEntity = hit.collider.GetComponentInParent<NetworkEntity>();
            if (targetEntity == null) return;

            // Friendly NPCs are talked to, never attacked
            if (targetEntity.EntityType == Shared.Enums.EntityType.Npc)
            {
                if (interactClicked) TryInteract(targetEntity);
                return;
            }

            if (attackHeld && Time.time - _lastAttackTime >= attackCooldown)
            {
                _lastAttackTime = Time.time;
                SendAttackRequest(targetEntity.EntityId);
            }
        }

        private void TryInteract(NetworkEntity npc)
        {
            // Only saves a round trip; the server validates the range again
            float distance = Vector2.Distance(transform.position, npc.transform.position);
            if (distance > Shared.Constants.GameRules.InteractRange)
            {
                GetComponent<EntityManager>()?.ShowMessage("Too far away", Color.white);
                return;
            }

            Client.Network.Handlers.DialogueHandler.RequestInteract(npc.EntityId);
        }

        private void SendAttackRequest(int targetId)
        {
            using (Packet packet = new Packet(OpCode.EntityAttackRequest))
            {
                packet.Write(targetId);
                NetworkManager.Instance.SendPacket(packet);
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
