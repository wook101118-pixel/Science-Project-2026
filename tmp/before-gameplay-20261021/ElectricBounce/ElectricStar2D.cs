using UnityEngine;
using UnityEngine.Events;

namespace sung_wook
{
    /// <summary>기존 공에 별 수집을 전달하고 중복 트리거를 막는 수집물.</summary>
    [RequireComponent(typeof(CircleCollider2D))]
    [DisallowMultipleComponent]
    public sealed class ElectricStar2D : MonoBehaviour
    {
        [SerializeField] private UnityEvent onCollected = new UnityEvent();
        private bool collected;

        public bool IsCollected => collected;
        public UnityEvent OnCollected => onCollected;

        /// <summary>스테이지 재시작 시 수집 상태와 표시를 함께 복구한다.</summary>
        public void ResetProgress()
        {
            collected = false;
            gameObject.SetActive(true);
        }

        private void Awake() => GetComponent<Collider2D>().isTrigger = true;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!isActiveAndEnabled || collected) return;
            var ball = other.GetComponentInParent<ElectricBall2D>();
            if (ball == null || !ball.CanReceiveForce) return;
            collected = true;
            ball.CollectStar();
            ElectricSfxPlayer.Play(SfxCue.Star);
            onCollected.Invoke();
            gameObject.SetActive(false);
        }
    }
}
