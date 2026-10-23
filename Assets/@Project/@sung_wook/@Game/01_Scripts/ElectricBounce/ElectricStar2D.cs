using UnityEngine;
using UnityEngine.Events;

namespace sung_wook
{
                                                           
    [RequireComponent(typeof(CircleCollider2D))]
    [DisallowMultipleComponent]
    public sealed class ElectricStar2D : MonoBehaviour
    {
        [SerializeField] private UnityEvent onCollected = new UnityEvent();
        private bool collected;

        public bool IsCollected => collected;
        public UnityEvent OnCollected => onCollected;

                                                             
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
