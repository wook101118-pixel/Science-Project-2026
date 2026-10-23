using UnityEngine;
using UnityEngine.Events;

namespace sung_wook
{
                                                                   
    [RequireComponent(typeof(BoxCollider2D))]
    [DisallowMultipleComponent]
    public sealed class ElectricGoal2D : MonoBehaviour
    {
        [SerializeField, Min(0)] private int requiredStars = 1;
        [SerializeField] private UnityEvent onCleared = new UnityEvent();
                                                         
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

                                                                                 
        private void TryClear(Collider2D other)
        {
            if (!isActiveAndEnabled || cleared) return;
            var ball = other.GetComponentInParent<ElectricBall2D>();
            if (ball == null || !ball.CanReceiveForce || ball.Stars < requiredStars) return;
            IElectricStageClearRule rule = learningRequirement;
                                                                                           
            if (learningRequirement == null || !learningRequirement.isActiveAndEnabled
                || learningRequirement.gameObject.scene != gameObject.scene || !rule.CanClearGoal(ball)) return;
            cleared = true;
                                                                       
            ball.Body.linearVelocity = Vector2.zero;
            ball.Body.simulated = false;
            ball.enabled = false;
            ElectricSfxPlayer.Play(SfxCue.StageClear);
            onCleared.Invoke();
        }
    }
}
