using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Разлетающийся реквизит трассы (конусы, бочки, знаки). Сбит — летит по физике, потом сам
    /// возвращается на место, когда рядом нет машин.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DestructibleProp : MonoBehaviour
    {
        [SerializeField] private float resetDelay = 12f;
        [SerializeField] private float clearRadius = 25f;
        [SerializeField] private float knockSpeed = 4f;

        private Rigidbody _body;
        private Vector3 _homePos;
        private Quaternion _homeRot;
        private float _resetAt = -1f;
        private static readonly Collider[] Buffer = new Collider[8];

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _homePos = transform.position;
            _homeRot = transform.rotation;
            _body.Sleep();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.rigidbody == null || !collision.rigidbody.TryGetComponent(out ArcadeCarController _)) return;
            // Лёгкий реквизит улетает эффектно, даже если физика посчитала удар мягко.
            Vector3 push = collision.rigidbody.GetVelocity();
            if (push.magnitude > knockSpeed)
                _body.AddForce((push * 0.6f + Vector3.up * push.magnitude * 0.35f), ForceMode.VelocityChange);
            _resetAt = Time.time + resetDelay;
        }

        private void Update()
        {
            if (_resetAt < 0f || Time.time < _resetAt) return;
            int count = Physics.OverlapSphereNonAlloc(_homePos, clearRadius, Buffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody rb = Buffer[i].attachedRigidbody;
                if (rb != null && rb.TryGetComponent(out ArcadeCarController _))
                {
                    _resetAt = Time.time + 2f;
                    return;
                }
            }

            _body.SetVelocity(Vector3.zero);
            _body.angularVelocity = Vector3.zero;
            _body.position = _homePos;
            _body.rotation = _homeRot;
            transform.SetPositionAndRotation(_homePos, _homeRot);
            _body.Sleep();
            _resetAt = -1f;
        }
    }
}
