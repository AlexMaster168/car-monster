using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Триггер-ворота. Индекс 0 — линия старт/финиш. Порядок задаёт TimeAttackManager.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        [Tooltip("Свой бонус времени. Меньше 0 — взять значение из GameBalanceSO.")]
        [SerializeField] private float timeBonusOverride = -1f;

        public int Index { get; set; }
        public float TimeBonusOverride => timeBonusOverride;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (body == null || !body.TryGetComponent(out ArcadeCarController car)) return;
            if (TimeAttackManager.Instance != null) TimeAttackManager.Instance.NotifyCheckpoint(this, car);
        }
    }
}
