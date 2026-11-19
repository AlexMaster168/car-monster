using UnityEngine;

namespace CarMonster
{
    /// <summary>Ускоряющий пад на дороге: рывок вперёд + немного нитро. Работает для игрока и ботов.</summary>
    [RequireComponent(typeof(Collider))]
    public class BoostPad : MonoBehaviour
    {
        [SerializeField] private float impulse = 11f;
        [SerializeField] private float nitroCharge = 25f;
        [SerializeField] private float cooldown = 1f;

        public int TriggerCount { get; private set; }

        private readonly System.Collections.Generic.Dictionary<int, float> _lastTrigger =
            new System.Collections.Generic.Dictionary<int, float>();

        private void Reset() => GetComponent<Collider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (body == null || !body.TryGetComponent(out ArcadeCarController car) || car.IsFrozen) return;

            int id = body.GetInstanceID();
            if (_lastTrigger.TryGetValue(id, out float last) && Time.time - last < cooldown) return;
            _lastTrigger[id] = Time.time;

            car.AddForwardImpulse(impulse);
            if (car.TryGetComponent(out NitroSystem nitro)) nitro.AddCharge(nitroCharge, NitroGainSource.BoostPad);
            TriggerCount++;
        }
    }
}
