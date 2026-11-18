using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Отстреливаемая деталь кузова (бампер, спойлер, дверь). Пока висит на машине — часть её
    /// составного коллайдера. При отрыве получает свой Rigidbody и улетает; при респавне машины
    /// возвращается на место (без Instantiate/Destroy объекта детали).
    /// </summary>
    public class DetachablePart : MonoBehaviour
    {
        [SerializeField] private float partMass = 25f;
        [Tooltip("Через сколько секунд оторванная деталь исчезает.")]
        [SerializeField] private float debrisLifetime = 7f;
        [Tooltip("Имя слоя для обломков (если такого слоя нет — слой не меняется).")]
        [SerializeField] private string debrisLayerName = "Debris";

        public bool IsDetached { get; private set; }

        private Transform _originalParent;
        private Vector3 _localPosition;
        private Quaternion _localRotation;
        private int _originalLayer;
        private Rigidbody _partBody;
        private float _hideAt;

        private void Awake()
        {
            _originalParent = transform.parent;
            _localPosition = transform.localPosition;
            _localRotation = transform.localRotation;
            _originalLayer = gameObject.layer;
        }

        public void Detach(Vector3 velocity, Vector3 inheritedVelocity)
        {
            if (IsDetached) return;
            IsDetached = true;

            transform.SetParent(null, true);
            int debrisLayer = LayerMask.NameToLayer(debrisLayerName);
            if (debrisLayer >= 0) gameObject.layer = debrisLayer;

            _partBody = gameObject.AddComponent<Rigidbody>();
            _partBody.mass = partMass;
            _partBody.interpolation = RigidbodyInterpolation.Interpolate;
            _partBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _partBody.SetVelocity(inheritedVelocity + velocity);
            _partBody.angularVelocity = Random.insideUnitSphere * 12f;

            _hideAt = Time.time + debrisLifetime;
        }

        public void Restore()
        {
            if (_partBody != null)
            {
                _partBody.isKinematic = true;
                Destroy(_partBody);
                _partBody = null;
            }

            transform.SetParent(_originalParent, false);
            transform.localPosition = _localPosition;
            transform.localRotation = _localRotation;
            gameObject.layer = _originalLayer;
            gameObject.SetActive(true);
            IsDetached = false;
        }

        private void Update()
        {
            if (IsDetached && Time.time >= _hideAt) gameObject.SetActive(false);
        }
    }
}
