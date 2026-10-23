using UnityEngine;
using UnityEngine.Events;

namespace sung_wook
{
    /// <summary>별 개수와 학습 조건을 확인하고 한 번만 클리어 이벤트를 발행하는 출구.</summary>
    [RequireComponent(typeof(BoxCollider2D))]
    [DisallowMultipleComponent]
    public sealed class ElectricGoal2D : MonoBehaviour
    {
        [SerializeField, Min(0)] private int requiredStars = 1;
        [SerializeField] private UnityEvent onCleared = new UnityEvent();
        // Unity의 기존 직렬화 참조는 유지하고, 조건 판단은 작은 인터페이스로 위임한다.
        [SerializeField] private ElectricLearningController2D learningRequirement;
        private bool cleared;

        public bool IsCleared => cleared;
        public int RequiredStars => requiredStars;
        public UnityEvent OnCleared => onCleared;
        public void Configure(int starsRequired) => requiredStars = Mathf.Max(0, starsRequired);
        public void ConfigureLearningRequirement(ElectricLearningController2D lesson) => learningRequirement = lesson;
        public void ResetProgress() => cleared = false;

        private void Awake() => GetComponent<Collider2D>().isTrigger = true;
        private void OnTriggerEnter2D(Collider2D other) => TryClear(other);
        private void OnTriggerStay2D(Collider2D other) => TryClear(other);

        /// <summary>체류 중 전하/레버 조건이 충족되어도 클리어할 수 있도록 Enter와 Stay가 공유한다.</summary>
        private void TryClear(Collider2D other)
        {
            if (!isActiveAndEnabled || cleared) return;
            var ball = other.GetComponentInParent<ElectricBall2D>();
            if (ball == null || !ball.CanReceiveForce || ball.Stars < requiredStars) return;
            IElectricStageClearRule rule = learningRequirement;
            // A missing scene binding must not turn a learning exit into a star-only exit.
            if (learningRequirement == null || !learningRequirement.isActiveAndEnabled
                || learningRequirement.gameObject.scene != gameObject.scene || !rule.CanClearGoal(ball)) return;
            cleared = true;
            // 클리어 후 추가 가속/충돌을 막는다. 재시작은 ElectricBall2D.ResetAt에서 복구한다.
            ball.Body.linearVelocity = Vector2.zero;
            ball.Body.simulated = false;
            ball.enabled = false;
            ElectricSfxPlayer.Play(SfxCue.StageClear);
            onCleared.Invoke();
        }
    }
}
