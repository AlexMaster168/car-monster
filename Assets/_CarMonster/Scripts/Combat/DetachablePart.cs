using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Отстреливаемая деталь кузова (бампер, капот, спойлер, двери, броня). Пока висит на машине — часть её
    /// составного коллайдера. При отрыве получает свой Rigidbody и улетает; при самосборке прилетает
    /// обратно по дуге с вращением и встаёт на своё место (объект детали не пересоздаётся).
    /// </summary>
    public class DetachablePart : MonoBehaviour
    {
        [SerializeField] private float partMass = 25f;
        [Tooltip("Через сколько секунд лежащая деталь прячется (если машину так и не собрали).")]
        [SerializeField] private float debrisLifetime = 12f;
        [Tooltip("Имя слоя для обломков (если такого слоя нет — слой не меняется).")]
        [SerializeField] private string debrisLayerName = "Debris";

        public bool IsDetached { get; private set; }
        public bool IsReturning { get; private set; }

        private Transform _originalParent;
        private Vector3 _localPosition;
        private Quaternion _localRotation;
        private int _originalLayer;
        private Rigidbody _partBody;
        private Collider[] _colliders;
        private float _hideAt;

        private TakedownSystem _owner;
        private float _returnStart;
        private float _returnDuration;
        private Vector3 _fromPos;
        private Quaternion _fromRot;
        private float _spinSign;

        private void Awake()
        {
            _originalParent = transform.parent;
            _localPosition = transform.localPosition;
            _localRotation = transform.localRotation;
            _originalLayer = gameObject.layer;
            _colliders = GetComponents<Collider>();
        }

        public void Detach(Vector3 velocity, Vector3 inheritedVelocity)
        {
            if (IsDetached) return;
            IsDetached = true;
            IsReturning = false;

            transform.SetParent(null, true);
            int debrisLayer = LayerMask.NameToLayer(debrisLayerName);
            if (debrisLayer >= 0) gameObject.layer = debrisLayer;
            SetColliders(true);

            if (_partBody == null) _partBody = gameObject.AddComponent<Rigidbody>();
            _partBody.isKinematic = false;
            _partBody.mass = partMass;
            _partBody.interpolation = RigidbodyInterpolation.Interpolate;
            _partBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _partBody.SetVelocity(inheritedVelocity + velocity);
            _partBody.angularVelocity = Random.insideUnitSphere * 12f;

            _hideAt = Time.time + debrisLifetime;
        }

        /// <summary>Начать полёт детали обратно к машине. По прибытии зовёт owner.OnPartReturned.</summary>
        public void BeginReturn(TakedownSystem owner, float delay, float duration)
        {
            if (!IsDetached || IsReturning) return;
            _owner = owner;
            IsReturning = true;

            if (!gameObject.activeSelf)
            {
                // Деталь уже спрятали — «прилетает» из случайной точки рядом.
                gameObject.SetActive(true);
                transform.position = owner.transform.position + Random.onUnitSphere * 8f + Vector3.up * 4f;
            }

            if (_partBody != null)
            {
                _partBody.isKinematic = true;
                _partBody.interpolation = RigidbodyInterpolation.None;
            }
            SetColliders(false);

            _fromPos = transform.position;
            _fromRot = transform.rotation;
            _returnStart = Time.time + delay;
            _returnDuration = Mathf.Max(0.05f, duration);
            _spinSign = Random.value > 0.5f ? 1f : -1f;
        }

        /// <summary>Мгновенно вернуть деталь на место.</summary>
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
            SetColliders(true);
            IsDetached = false;
            IsReturning = false;
        }

        private void Update()
        {
            if (!IsDetached) return;

            if (!IsReturning)
            {
                if (Time.time >= _hideAt) gameObject.SetActive(false);
                return;
            }

            if (Time.time < _returnStart) return;
            float e = Mathf.Clamp01((Time.time - _returnStart) / _returnDuration);
            float s = e * e * (3f - 2f * e);

            Vector3 target = _originalParent.TransformPoint(_localPosition);
            Quaternion targetRot = _originalParent.rotation * _localRotation;
            Vector3 control = (_fromPos + target) * 0.5f + Vector3.up * 3f;
            Vector3 a = Vector3.Lerp(_fromPos, control, s);
            Vector3 b = Vector3.Lerp(control, target, s);
            transform.position = Vector3.Lerp(a, b, s);
            transform.rotation = Quaternion.Slerp(_fromRot, targetRot, s) *
                                 Quaternion.AngleAxis((1f - s) * 540f * _spinSign, Vector3.forward);

            if (e >= 1f)
            {
                Restore();
                if (_owner != null) _owner.OnPartReturned(this);
            }
        }

        private void SetColliders(bool value)
        {
            if (_colliders == null) return;
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) _colliders[i].enabled = value;
        }
    }
}
