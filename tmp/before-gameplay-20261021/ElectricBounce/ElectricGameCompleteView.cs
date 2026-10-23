using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace sung_wook
{
    /// <summary>전체 스테이지 완료 화면의 표시와 처음부터 다시 시작하는 버튼을 담당한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ElectricGameCompleteView : MonoBehaviour
    {
        public const string FirstStagePath = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_02.unity";

        [SerializeField] private Button restartButton;
        [SerializeField] private CanvasGroup canvasGroup;

        private readonly List<GameObject> hiddenHud = new List<GameObject>();
        private bool ownsPause;
        private float previousTimeScale = 1f;

        private void Awake()
        {
            if (restartButton == null) restartButton = GetComponentInChildren<Button>(true);
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            if (restartButton != null) restartButton.onClick.AddListener(RestartGame);
        }

        private void OnDisable()
        {
            if (restartButton != null) restartButton.onClick.RemoveListener(RestartGame);
            if (ownsPause)
            {
                Time.timeScale = previousTimeScale;
                ownsPause = false;
            }
            foreach (GameObject child in hiddenHud)
                if (child != null) child.SetActive(true);
            hiddenHud.Clear();
        }

        /// <summary>진행 관리자가 모든 스테이지 완료를 확정한 뒤 호출한다.</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            Canvas owner = GetComponentInParent<Canvas>();
            Transform hudRoot = owner != null ? owner.rootCanvas.transform : transform.parent;
            if (hudRoot != null && hudRoot != transform)
                foreach (Transform child in hudRoot)
                {
                    string childName = child.name.Trim();
                    if (child == transform || !child.gameObject.activeSelf
                        || (childName != "HUD" && childName != "HDD" && childName != "Learning_Objective_Panel"))
                        continue;
                    hiddenHud.Add(child.gameObject);
                    child.gameObject.SetActive(false);
                }

            if (!ownsPause)
            {
                previousTimeScale = Time.timeScale;
                ownsPause = true;
            }
            Time.timeScale = 0f;
            if (restartButton != null) restartButton.Select();
        }

        public void RestartGame()
        {
            if (SceneTransitionController.IsBusy) return;
            // Do not let OnDisable restore a previous pause while the new scene loads.
            bool previouslyOwnedPause = ownsPause;
            ownsPause = false;
            if (SceneTransitionController.LoadScene(FirstStagePath))
                ElectricSfxPlayer.Play(SfxCue.UiClick);
            else
                ownsPause = previouslyOwnedPause;
        }
    }
}
