using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 씬에 배치한 장애물 하나를 두 지점 사이에서 반복 이동한다.
    /// 충돌 판정은 기존 Collider2D에 맡기고, 입력·학습 판정·플레이어 상태에는 관여하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class ElectricObstacleMotion2D : MonoBehaviour
    {
        [Tooltip("부모 Transform 기준 출발점의 절대 로컬 좌표. 현재 위치에 더하는 오프셋이 아닙니다.")]
        [SerializeField] private Vector2 startLocalPosition;

        [Tooltip("부모 Transform 기준 도착점의 절대 로컬 좌표. 두 지점을 Scene의 배치에 맞춰 지정합니다.")]
        [SerializeField] private Vector2 endLocalPosition = new Vector2(0f, -2f);

        [Tooltip("출발점에서 도착점까지 한 번 이동하는 시간(초). 복귀에도 같은 시간이 걸립니다.")]
        [SerializeField, Min(0.02f)] private float oneWayDuration = 1.5f;

        [Tooltip("스테이지가 활성화된 뒤 첫 주기를 시작하기 전 기다리는 시간(초).")]
        [SerializeField, Min(0f)] private float startDelay;

        [Tooltip("매 주기 출발점에서 기다리는 시간(초). 위쪽을 출발점으로 지정하면 상단 대기 시간입니다.")]
        [SerializeField, Min(0f)] private float startPause = 0.5f;

        [Tooltip("매 주기 도착점에서 기다리는 시간(초). 아래쪽을 도착점으로 지정하면 하단 대기 시간입니다.")]
        [SerializeField, Min(0f)] private float endPause = 0.5f;

        private Rigidbody2D obstacleBody;
        private float elapsedTime;
        private float localDepth;

        private void Awake()
        {
            obstacleBody = GetComponent<Rigidbody2D>();
            localDepth = transform.localPosition.z;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            // 재선택 때 현재 위치를 원점으로 삼지 않는다. 따라서 반복 활성화해도 좌표가 누적되지 않는다.
            ResetMotion();
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                ResetMotion();
            }
        }

        /// <summary>씬 재시작과 재활성화가 동일한 시작 위치·왕복 주기를 사용한다.</summary>
        public void ResetMotion()
        {
            if (obstacleBody == null)
            {
                obstacleBody = GetComponent<Rigidbody2D>();
                localDepth = transform.localPosition.z;
            }
            elapsedTime = 0f;
            RestoreStartPosition();
        }

        private void FixedUpdate()
        {
            // Body Type, 중력, 회전 제한은 씬의 Rigidbody2D 설정으로 관리한다.
            if (obstacleBody == null || obstacleBody.bodyType != RigidbodyType2D.Kinematic)
            {
                return;
            }

            elapsedTime += Time.fixedDeltaTime;
            Vector2 localPosition = EvaluateLocalPosition(elapsedTime);

            // 이동 중에는 물리 엔진을 통하므로 공과의 충돌이 유지된다.
            obstacleBody.MovePosition(ToWorldPosition(localPosition));
        }

        /// <summary>대기 → 전진 → 대기 → 복귀 주기의 현재 위치만 계산한다.</summary>
        private Vector2 EvaluateLocalPosition(float time)
        {
            float delay = Mathf.Max(0f, startDelay);
            if (time <= delay)
            {
                return startLocalPosition;
            }

            float duration = Mathf.Max(0.02f, oneWayDuration);
            float waitAtStart = Mathf.Max(0f, startPause);
            float waitAtEnd = Mathf.Max(0f, endPause);
            float cycleLength = duration * 2f + waitAtStart + waitAtEnd;
            float phase = Mathf.Repeat(time - delay, cycleLength);

            if (phase < waitAtStart)
            {
                return startLocalPosition;
            }

            phase -= waitAtStart;
            if (phase < duration)
            {
                return Vector2.Lerp(startLocalPosition, endLocalPosition, phase / duration);
            }

            phase -= duration;
            if (phase < waitAtEnd)
            {
                return endLocalPosition;
            }

            return Vector2.Lerp(endLocalPosition, startLocalPosition, (phase - waitAtEnd) / duration);
        }

        /// <summary>부모의 이동·회전·확대를 반영하되, 저장된 Z 배치 깊이는 유지한다.</summary>
        private Vector2 ToWorldPosition(Vector2 localPosition)
        {
            Vector3 point = new Vector3(localPosition.x, localPosition.y, localDepth);
            return transform.parent == null ? point : transform.parent.TransformPoint(point);
        }

        private void RestoreStartPosition()
        {
            if (obstacleBody != null)
            {
                // 활성 상태 전환 때만 시작점으로 복원한다. 실제 왕복 운동은 FixedUpdate가 담당한다.
                obstacleBody.position = ToWorldPosition(startLocalPosition);
            }
        }
    }
}
