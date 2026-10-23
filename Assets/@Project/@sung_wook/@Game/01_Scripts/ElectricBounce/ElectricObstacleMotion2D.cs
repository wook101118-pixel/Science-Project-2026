using UnityEngine;

namespace sung_wook
{
                 
                                         
                                                               
                  
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

                                                                   
            ResetMotion();
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                ResetMotion();
            }
        }

                                                                  
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
                                                              
            if (obstacleBody == null || obstacleBody.bodyType != RigidbodyType2D.Kinematic)
            {
                return;
            }

            elapsedTime += Time.fixedDeltaTime;
            Vector2 localPosition = EvaluateLocalPosition(elapsedTime);

                                               
            obstacleBody.MovePosition(ToWorldPosition(localPosition));
        }

                                                                 
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

                                                                     
        private Vector2 ToWorldPosition(Vector2 localPosition)
        {
            Vector3 point = new Vector3(localPosition.x, localPosition.y, localDepth);
            return transform.parent == null ? point : transform.parent.TransformPoint(point);
        }

        private void RestoreStartPosition()
        {
            if (obstacleBody != null)
            {
                                                                       
                obstacleBody.position = ToWorldPosition(startLocalPosition);
            }
        }
    }
}
