using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace sung_wook
{
                                                                    
    [DisallowMultipleComponent]
    public sealed class ElectricGameCompleteView : MonoBehaviour
    {
        public const string FirstStagePath = "Assets/@Project/@sung_wook/@Game/00_Scenes/@InGame/Stage/Stage_01.unity";
        public const string MainMenuPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/@InGame/MainMenu.unity";

        [SerializeField] private Button restartButton;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Button retryCurrentButton;
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private TMP_Text resultTitle;
        [SerializeField] private TMP_Text resultSummary;
        [SerializeField] private TMP_Text primaryLabel;
        [SerializeField] private TMP_Text resultHint;
        [SerializeField] private RectTransform resultFrame;

        private readonly List<GameObject> hiddenHud = new List<GameObject>();
        private bool ownsPause;
        private bool isDeath;
        private float previousTimeScale = 1f;
        private int completedStage = 5;
        private int layoutWidth, layoutHeight;
        private float layoutScale;
        public bool IsResultOpen => ownsPause && gameObject.activeInHierarchy;
        public int CompletedStage => completedStage;
        public bool IsDeathOpen => isDeath && IsResultOpen;

        private void Awake()
        {
            if (restartButton == null) restartButton = GetComponentInChildren<Button>(true);
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            if (restartButton != null) restartButton.onClick.AddListener(ContinueNextStage);
            if (retryCurrentButton != null) retryCurrentButton.onClick.AddListener(RetryCurrentStage);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ReturnToMainMenu);
        }

        private void OnDisable()
        {
            if (restartButton != null) restartButton.onClick.RemoveListener(ContinueNextStage);
            if (retryCurrentButton != null) retryCurrentButton.onClick.RemoveListener(RetryCurrentStage);
            if (mainMenuButton != null) mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
            if (ownsPause)
            {
                Time.timeScale = previousTimeScale;
                ownsPause = false;
            }
            foreach (GameObject child in hiddenHud)
                if (child != null) child.SetActive(true);
            hiddenHud.Clear();
        }

                                                              
        public void Show() => ShowStageResult(5, "모든 스테이지의 학습 목표와 별 수집을 완료했습니다.");

        public void ShowStageResult(int stage, string summary)
        {
            isDeath = false;
            layoutWidth = 0;
            completedStage = Mathf.Clamp(stage, 1, 5);
            gameObject.SetActive(true);
            if (resultTitle != null)
            {
                resultTitle.text = completedStage == 5 ? "" : "Stage " + completedStage + " 완료";
                resultTitle.gameObject.SetActive(completedStage != 5);
            }
            if (resultSummary != null) resultSummary.text = summary;
            if (primaryLabel != null) primaryLabel.text = completedStage == 5 ? "튜토리얼부터 새 시작" : "다음 단계";
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
            Canvas.ForceUpdateCanvases();
            RefreshLayout();
        }

        public void ShowDeath(int stage)
        {
            ShowStageResult(stage, "발판 아래로 추락했습니다.\n다시 도전하면 현재 스테이지를 처음부터 시작합니다.");
            isDeath = true;
            if (resultTitle != null)
            {
                resultTitle.text = "추락 · 사망";
                resultTitle.gameObject.SetActive(true);
            }
            if (primaryLabel != null) primaryLabel.text = "다시 도전";
            if (retryCurrentButton != null) retryCurrentButton.gameObject.SetActive(false);
            if (resultHint != null) resultHint.text = "R 다시 도전 · Esc 메인 메뉴";
            layoutWidth = 0;
            RefreshLayout();
        }

        private void Update()
        {
            if (IsResultOpen) RefreshLayout();
            if (IsResultOpen && !SceneTransitionController.IsBusy && Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame) ReturnToMainMenu();
            else if (IsDeathOpen && !SceneTransitionController.IsBusy && Keyboard.current != null
                && Keyboard.current.rKey.wasPressedThisFrame) RetryCurrentStage();
        }

        private void RefreshLayout()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null ? Mathf.Max(.01f, canvas.rootCanvas.scaleFactor) : 1f;
            if (layoutWidth == Screen.width && layoutHeight == Screen.height && Mathf.Abs(layoutScale - scale) < .001f) return;
            layoutWidth = Screen.width; layoutHeight = Screen.height; layoutScale = scale;
            bool narrow = Screen.width < 1100;
            if (resultFrame != null) SetRect(resultFrame, narrow ? new Vector2(.03f,.03f) : new Vector2(.13f,.13f), narrow ? new Vector2(.97f,.97f) : new Vector2(.87f,.87f));
            if (resultTitle != null)
            {
                SetRect(resultTitle.rectTransform,new Vector2(.06f,narrow?.85f:.80f),new Vector2(.94f,.96f));
                resultTitle.enableAutoSizing=true; resultTitle.fontSizeMin=Mathf.Max(36f,18f/scale);resultTitle.fontSizeMax=Mathf.Max(64f,resultTitle.fontSizeMin);
            }
            if (resultSummary != null)
            {
                SetRect(resultSummary.rectTransform,new Vector2(.07f,narrow?.35f:.24f),new Vector2(.93f,completedStage==5&&!isDeath?.90f:narrow?.83f:.78f));
                resultSummary.enableAutoSizing=true;resultSummary.fontSizeMin=Mathf.Max(22f,14f/scale);resultSummary.fontSizeMax=Mathf.Max(32f,resultSummary.fontSizeMin);
            }
            Button[] buttons={restartButton,retryCurrentButton,mainMenuButton};
            for(int index=0;index<buttons.Length;index++)
            {
                if(buttons[index]==null)continue;
                RectTransform rect=(RectTransform)buttons[index].transform;
                if(narrow)
                {
                    SetRect(rect,new Vector2(.12f,0),new Vector2(.88f,0));rect.pivot=new Vector2(.5f,0);
                    rect.anchoredPosition=new Vector2(0,(18f+(2-index)*40f)/scale);rect.sizeDelta=new Vector2(0,34f/scale);
                }
                else
                {
                    float x=isDeath?(index==0?.32f:.68f):.19f+index*.31f;SetRect(rect,new Vector2(x,.12f),new Vector2(x,.12f));rect.sizeDelta=new Vector2(290f,72f);
                }
                TMP_Text label=buttons[index].GetComponentInChildren<TMP_Text>(true);
                if(label!=null){label.enableAutoSizing=true;label.fontSizeMin=Mathf.Max(20f,14f/scale);label.fontSizeMax=Mathf.Max(36f,label.fontSizeMin);}
            }
            if(resultHint!=null)
            {
                SetRect(resultHint.rectTransform,new Vector2(.06f,.005f),new Vector2(.94f,narrow?.04f:.07f));
                resultHint.enableAutoSizing=true;resultHint.fontSizeMin=12f/scale;resultHint.fontSizeMax=Mathf.Max(23f,resultHint.fontSizeMin);
            }
        }

        private static void SetRect(RectTransform rect,Vector2 min,Vector2 max)
        {
            rect.anchorMin=min;rect.anchorMax=max;rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=Vector2.zero;
        }

        public void ContinueNextStage()
        {
            if (!IsResultOpen) return;
            if (isDeath) RetryCurrentStage();
            else if (completedStage == 5) RestartGame();
            else if (ElectricStageProgression.IsStageUnlocked(completedStage + 1))
                LoadResultScene(ElectricStageProgression.StageScenePath(completedStage + 1));
        }

        public void RetryCurrentStage()
        {
            if (IsResultOpen) LoadResultScene(ElectricStageProgression.StageScenePath(completedStage));
        }

        public void ReturnToMainMenu()
        {
            if (IsResultOpen) LoadResultScene(MainMenuPath);
        }

        public void RestartGame()
        {
            if (IsResultOpen) LoadResultScene(FirstStagePath);
        }

        private void LoadResultScene(string path)
        {
            if (SceneTransitionController.IsBusy) return;
                                                                                       
            bool previouslyOwnedPause = ownsPause;
            ownsPause = false;
            if (SceneTransitionController.LoadScene(path))
                ElectricSfxPlayer.Play(SfxCue.UiClick);
            else
                ownsPause = previouslyOwnedPause;
        }
    }
}
