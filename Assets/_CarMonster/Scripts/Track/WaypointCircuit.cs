using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Замкнутый маршрут по путевым точкам со сглаживанием Catmull-Rom.
    /// Даёт точку/направление по дистанции и «прогресс» машины вдоль трассы (для ИИ и респавна).
    /// Точки = дочерние объекты по порядку, если массив не задан вручную.
    /// </summary>
    public class WaypointCircuit : MonoBehaviour
    {
        [SerializeField] private Transform[] waypoints;
        [SerializeField] private bool loop = true;
        [SerializeField] private Color gizmoColor = new Color(1f, 0.8f, 0f);
        [Tooltip("Половина ширины дороги, м.")]
        [SerializeField] private float halfWidth = 11f;
        [Tooltip("Участки без дороги (пропасти) по дистанции: x = начало, y = конец. Там нельзя собираться.")]
        [SerializeField] private Vector2[] gaps = new Vector2[0];

        public float HalfWidth => halfWidth;

        /// <summary>Если дистанция попадает в пропасть или разгон перед ней — сдвинуть за пропасть.</summary>
        public float SkipGaps(float distance, float approach = 45f)
        {
            float d = Wrap(distance);
            for (int i = 0; i < gaps.Length; i++)
            {
                if (d >= gaps[i].x - approach && d <= gaps[i].y + 4f) return gaps[i].y + 12f;
            }
            return d;
        }

        /// <summary>Расстояние от точки до дороги (по горизонтали от осевой и по высоте).</summary>
        public void DistanceToTrack(Vector3 position, out float lateral, out float below)
        {
            float along = GetClosestDistance(position);
            Vector3 p = GetPoint(along);
            Vector3 flat = position - p;
            below = p.y - position.y;
            flat.y = 0f;
            lateral = Mathf.Max(0f, flat.magnitude - halfWidth);
        }

        private Vector3[] _points;
        private float[] _distances;

        public float Length { get; private set; }
        public int PointCount => _points != null ? _points.Length : 0;
        public bool Loop => loop;

        private void Awake() => Rebuild();

        public void Rebuild()
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                waypoints = new Transform[transform.childCount];
                for (int i = 0; i < waypoints.Length; i++) waypoints[i] = transform.GetChild(i);
            }

            int n = waypoints.Length;
            _points = new Vector3[n];
            _distances = new float[n + 1];
            for (int i = 0; i < n; i++) _points[i] = waypoints[i].position;

            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                _distances[i] = total;
                if (i < n - 1 || loop) total += Vector3.Distance(_points[i], _points[(i + 1) % n]);
            }
            _distances[n] = total;
            Length = total;
        }

        private float Wrap(float distance)
        {
            if (Length <= 0f) return 0f;
            if (!loop) return Mathf.Clamp(distance, 0f, Length);
            distance %= Length;
            return distance < 0f ? distance + Length : distance;
        }

        private int SegmentAt(float distance)
        {
            int lo = 0;
            int hi = _points.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_distances[mid] <= distance) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        private Vector3 PointAtIndex(int i)
        {
            int n = _points.Length;
            if (loop) return _points[((i % n) + n) % n];
            return _points[Mathf.Clamp(i, 0, n - 1)];
        }

        public Vector3 GetPoint(float distance)
        {
            if (_points == null || _points.Length == 0) return transform.position;
            if (_points.Length == 1) return _points[0];

            distance = Wrap(distance);
            int seg = SegmentAt(distance);
            float segLength = _distances[seg + 1] - _distances[seg];
            float t = segLength > 0.0001f ? (distance - _distances[seg]) / segLength : 0f;

            Vector3 p0 = PointAtIndex(seg - 1);
            Vector3 p1 = PointAtIndex(seg);
            Vector3 p2 = PointAtIndex(seg + 1);
            Vector3 p3 = PointAtIndex(seg + 2);

            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                           (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        public Vector3 GetDirection(float distance)
        {
            Vector3 d = GetPoint(distance + 1.5f) - GetPoint(distance - 1.5f);
            return d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;
        }

        /// <summary>
        /// Дистанция вдоль трассы до ближайшей точки. hintSegment &lt; 0 — полный поиск,
        /// иначе ищем в окне ±window сегментов (дёшево каждый кадр).
        /// </summary>
        public float GetClosestDistance(Vector3 position, int hintSegment, int window, out int segment)
        {
            segment = 0;
            if (_points == null || _points.Length < 2) return 0f;

            int n = _points.Length;
            int segCount = loop ? n : n - 1;
            int start = 0;
            int count = segCount;
            if (hintSegment >= 0 && window * 2 + 1 < segCount)
            {
                start = hintSegment - window;
                count = window * 2 + 1;
            }

            float best = float.MaxValue;
            float bestDistance = 0f;
            for (int k = 0; k < count; k++)
            {
                int i = ((start + k) % segCount + segCount) % segCount;
                Vector3 a = _points[i];
                Vector3 b = _points[(i + 1) % n];
                Vector3 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 0.0001f ? Mathf.Clamp01(Vector3.Dot(position - a, ab) / len2) : 0f;
                float d2 = (a + ab * t - position).sqrMagnitude;
                if (d2 < best)
                {
                    best = d2;
                    segment = i;
                    bestDistance = _distances[i] + (_distances[i + 1] - _distances[i]) * t;
                }
            }
            return bestDistance;
        }

        public float GetClosestDistance(Vector3 position) => GetClosestDistance(position, -1, 0, out _);

#if UNITY_EDITOR
        public void EditorSetWaypoints(Transform[] points, bool isLoop)
        {
            waypoints = points;
            loop = isLoop;
            Rebuild();
        }

        public void EditorSetRoad(float roadHalfWidth, Vector2[] gapRanges)
        {
            halfWidth = roadHalfWidth;
            gaps = gapRanges ?? new Vector2[0];
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) Rebuild();
            if (_points == null || _points.Length < 2) return;
            Gizmos.color = gizmoColor;
            const float step = 3f;
            Vector3 prev = GetPoint(0f);
            for (float d = step; d <= Length; d += step)
            {
                Vector3 p = GetPoint(d);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
            for (int i = 0; i < _points.Length; i++) Gizmos.DrawWireSphere(_points[i], 0.6f);
        }
#endif
    }
}
