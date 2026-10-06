using UnityEngine;
using TMPro;

namespace Client.World
{
    public class EntityManager : MonoBehaviour
    {
        [Header("Visuals")]
        public TextMeshPro nameText;
        public Animator animator;
        public GameObject floatingTextPrefab; // Optional prefab for later

        private Vector3 _lastPosition;

        // --- Animation Parameter Hashes (Best Practice Optimization) ---
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int HitHash = Animator.StringToHash("Hit");

        private void Start()
        {
            _lastPosition = transform.position;
        }

        private void Update()
        {
            if (animator == null) return;

            // Calculate actual movement velocity based on position changes
            Vector3 velocity = (transform.position - _lastPosition) / Time.deltaTime;
            _lastPosition = transform.position;

            float speed = velocity.magnitude;

            // If we are moving, update the facing direction for Blend Trees
            if (speed > 0.05f)
            {
                Vector3 dir = velocity.normalized;
                animator.SetFloat(MoveXHash, dir.x);
                animator.SetFloat(MoveYHash, dir.y);
            }

            // Update the animation state (0 = Idle, >0 = Moving)
            animator.SetFloat(SpeedHash, speed);
        }

        // --- Replicated health (server-authoritative, fed by EntityVitals / EntityDeath) ---
        public int Health { get; private set; } = 1;
        public int MaxHealth { get; private set; } = 1;
        public bool IsDead { get; private set; }
        public float HealthPercent => MaxHealth > 0 ? Mathf.Clamp01((float)Health / MaxHealth) : 0f;

        /// <summary>Raised whenever health or max health changes (e.g. to drive an overhead health bar).</summary>
        public event System.Action<EntityManager> VitalsChanged;
        /// <summary>Raised once when the entity dies.</summary>
        public event System.Action<EntityManager> Died;

        public void SetVitals(int health, int maxHealth)
        {
            Health = health;
            MaxHealth = maxHealth;

            // A health value above zero means the entity is alive again (respawn / revive)
            if (health > 0) IsDead = false;

            VitalsChanged?.Invoke(this);
        }

        public void OnDeath()
        {
            if (IsDead) return;

            IsDead = true;
            Health = 0;
            VitalsChanged?.Invoke(this);
            Died?.Invoke(this);
        }

        public void SetName(string characterName)
        {
            if (nameText != null)
            {
                nameText.text = characterName;
            }
            else
            {
                Debug.LogWarning($"[{gameObject.name}] EntityManager is missing the NameText reference! Please assign it in the Inspector.");
            }
        }

        public void TriggerAttack()
        {
            if (animator != null)
            {
                animator.SetTrigger(AttackHash);
            }
        }

        public void TriggerHit()
        {
            if (animator != null)
            {
                animator.SetTrigger(HitHash);
            }
        }

        public void ShowFloatingText(int damage, bool isCrit, bool isDodge)
        {
            string textToShow;
            Color textColor = Color.white;

            if (isDodge)
            {
                textToShow = "Dodge";
                textColor = Color.gray;
            }
            else if (isCrit)
            {
                textToShow = $"{damage}!";
                textColor = Color.red;
            }
            else
            {
                textToShow = damage.ToString();
                textColor = Color.yellow;
            }

            if (floatingTextPrefab != null)
            {
                // Spawn slightly above the character's origin
                Vector3 spawnPos = transform.position + new Vector3(0, 1.5f, 0);
                
                // Add slight randomness to X and Y so rapid attacks don't overlap perfectly
                spawnPos.x += UnityEngine.Random.Range(-0.3f, 0.3f);
                spawnPos.y += UnityEngine.Random.Range(-0.2f, 0.2f);

                GameObject go = Instantiate(floatingTextPrefab, spawnPos, Quaternion.identity);
                var ft = go.GetComponent<Client.UI.FloatingText>();
                if (ft != null)
                {
                    ft.Setup(textToShow, textColor, isCrit);
                }
            }
            else
            {
                // Fallback if the prefab hasn't been assigned in the Unity Editor yet
                Debug.Log($"[Combat] {gameObject.name} takes {textToShow}");
            }
        }
    }
}
