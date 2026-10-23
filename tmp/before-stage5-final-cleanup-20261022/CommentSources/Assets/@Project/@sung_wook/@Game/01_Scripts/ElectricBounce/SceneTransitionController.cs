using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace sung_wook
{
    /// <summary>저장된 페이드 UI를 유지하며 비동기 씬 로딩과 화면 전환을 담당한다.</summary>
    [DisallowMultipleComponent]
    public sealed class SceneTransitionController : MonoBehaviour
    {
        [SerializeField] private CanvasGroup fadeOverlay;
        [SerializeField, Min(0f)] private float fadeDuration = 0.35f;

        private static SceneTransitionController instance;
        private AsyncOperation pendingLoad;
        private bool busy;
        private bool ownsTimeScale;
        private float previousTimeScale = 1f;

        public static bool IsBusy => instance != null && instance.busy;
        public static bool IsAvailable => instance != null && instance.isActiveAndEnabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            if (fadeOverlay == null)
            {
                Debug.LogError("Scene Transition의 Fade Overlay 참조가 비었습니다.", this);
                enabled = false;
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            BeginTransition();
            fadeOverlay.alpha = 1f;
            StartCoroutine(RevealInitialScene());
        }

        /// <summary>같은 프레임의 중복 요청을 차단하고 기존 화면을 가린 뒤 새 씬을 표시한다.</summary>
        public static bool LoadScene(string scenePath)
        {
            if (!IsAvailable)
            {
                Debug.LogError("비동기 씬 전환에 필요한 SceneTransitionController가 없습니다.");
                return false;
            }
            if (instance.busy) return false;
            if (string.IsNullOrWhiteSpace(scenePath) || !Application.CanStreamedLevelBeLoaded(scenePath))
            {
                Debug.LogError("Build Settings에 로드할 씬이 없습니다: " + scenePath, instance);
                return false;
            }

            instance.BeginTransition();
            instance.StartCoroutine(instance.LoadAndReveal(scenePath));
            return true;
        }

        private void BeginTransition()
        {
            busy = true;
            previousTimeScale = Time.timeScale;
            ownsTimeScale = true;
            Time.timeScale = 0f;
            ElectricSfxPlayer.StopGameplay();
            ElectricSfxPlayer.SetGameplayPaused(true);
            fadeOverlay.interactable = false;
            fadeOverlay.blocksRaycasts = true;
        }

        private IEnumerator RevealInitialScene()
        {
            // Start가 실행된 화면부터 페이드해 초기 배치가 보이는 순간을 방지한다.
            yield return null;
            yield return FadeTo(0f);
            FinishTransition(true);
        }

        private IEnumerator LoadAndReveal(string scenePath)
        {
            Exception loadError = null;
            try
            {
                pendingLoad = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
                if (pendingLoad != null) pendingLoad.allowSceneActivation = false;
            }
            catch (Exception error)
            {
                loadError = error;
            }

            if (pendingLoad == null || loadError != null)
            {
                Debug.LogError("씬 로딩을 시작하지 못했습니다: " + scenePath
                    + (loadError != null ? "\n" + loadError.Message : string.Empty), this);
                yield return FadeTo(0f);
                FinishTransition(false);
                yield break;
            }

            // 로딩은 화면을 가리는 동안 진행하며, 완전히 가려진 뒤에만 씬을 활성화한다.
            yield return FadeTo(1f);
            while (pendingLoad.progress < 0.9f) yield return null;
            pendingLoad.allowSceneActivation = true;
            while (!pendingLoad.isDone) yield return null;
            pendingLoad = null;
            yield return null;
            yield return FadeTo(0f);
            FinishTransition(true);
        }

        private IEnumerator FadeTo(float alpha)
        {
            float duration = float.IsNaN(fadeDuration) || float.IsInfinity(fadeDuration)
                ? 0.35f : Mathf.Clamp(fadeDuration, 0f, 10f);
            float initialAlpha = fadeOverlay.alpha;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                fadeOverlay.alpha = Mathf.Lerp(initialAlpha, alpha, Mathf.SmoothStep(0f, 1f, elapsed / duration));
                yield return null;
            }
            fadeOverlay.alpha = alpha;
        }

        private void FinishTransition(bool loaded)
        {
            fadeOverlay.alpha = 0f;
            fadeOverlay.blocksRaycasts = false;
            busy = false;
            if (ownsTimeScale) Time.timeScale = loaded ? 1f : previousTimeScale;
            ElectricSfxPlayer.SetGameplayPaused(Time.timeScale <= 0f);
            ownsTimeScale = false;
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            // 보류한 활성화가 남으면 다른 씬 로딩까지 대기하므로 종료 시 해제한다.
            if (pendingLoad != null && !pendingLoad.isDone) pendingLoad.allowSceneActivation = true;
            if (ownsTimeScale) Time.timeScale = previousTimeScale;
            instance = null;
        }
    }
}
