using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using sung_wook;

[DisallowMultipleComponent]
public class CollapsiblePanel : MonoBehaviour
{
    [SerializeField] private Button toggleButton;
    [SerializeField] private LayoutElement content;
    [SerializeField] private TMP_Text toggleLabel;
    [SerializeField] private bool initiallyOpen;
    [SerializeField, Min(0f)] private float duration = 0.25f;
    [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private float openHeight;
    private bool isOpen;
    private Coroutine routine;
    private PauseMenuController readingPause;
    private bool ownsReadingPause;

    public bool IsOpen => isOpen;
    public bool PausesForReading => ownsReadingPause && readingPause != null && readingPause.IsPaused;
    public Button ToggleButton => toggleButton;
    public TMP_Text ToggleLabel => toggleLabel;

    private void Awake()
    {
        if (toggleButton == null || content == null)
        {
            Debug.LogError("CollapsiblePanel requires a toggle button and content LayoutElement.", this);
            enabled = false;
            return;
        }

                                                                                
        openHeight = content.preferredHeight >= 0f
            ? content.preferredHeight
            : ((RectTransform)content.transform).rect.height;
        isOpen = initiallyOpen;
        content.preferredHeight = isOpen ? openHeight : 0f;
        RefreshLabel();
    }

    private void OnEnable()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);
    }

    private void OnDisable()
    {
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(Toggle);

        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
            content.preferredHeight = isOpen ? openHeight : 0f;
        }
        isOpen = false;
        if (content != null) content.preferredHeight = 0f;
        RefreshLabel();
        ReleaseReadingPause();
    }

    private void Update()
    {
        if (!isOpen || !ownsReadingPause) return;
                                                                 
        if (readingPause == null || !readingPause.IsPaused || readingPause.IsSettingsOpen
            || SceneTransitionController.IsBusy)
            SetOpen(false);
    }

    public void Toggle() => SetOpen(!isOpen);

    public void SetOpen(bool value)
    {
        if (!isActiveAndEnabled || isOpen == value) return;
        if (value && Application.isPlaying)
        {
            if (SceneTransitionController.IsBusy) return;
            ResolvePauseOwner();
            if (readingPause != null)
            {
                if (readingPause.IsPaused || readingPause.IsSettingsOpen) return;
                ownsReadingPause = readingPause.PauseForReading();
                if (!ownsReadingPause) return;
            }
            else return;
        }
        isOpen = value;
        if (!isOpen) ReleaseReadingPause();
        RefreshLabel();
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        float target = isOpen ? openHeight : 0f;
        if (duration <= 0f)
            content.preferredHeight = target;
        else
            routine = StartCoroutine(Animate(target));
    }

                                                                    
    public void ConfigureReadingSize(float width, float contentHeight, float tabHeight)
    {
        if (content == null || toggleButton == null) return;
        if (routine != null) { StopCoroutine(routine); routine = null; }
        openHeight = Mathf.Max(0f, contentHeight);
        content.preferredHeight = isOpen ? openHeight : 0f;
        RectTransform panel = (RectTransform)transform;
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        ((RectTransform)content.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        RectTransform tab = (RectTransform)toggleButton.transform;
        tab.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        LayoutElement tabLayout = toggleButton.GetComponent<LayoutElement>();
        if (tabLayout != null) tabLayout.minHeight = tabLayout.preferredHeight = tabHeight;
    }

    private void ResolvePauseOwner()
    {
        if (readingPause != null || !gameObject.scene.IsValid()) return;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            readingPause = root.GetComponentInChildren<PauseMenuController>(true);
            if (readingPause != null) break;
        }
    }

    private void ReleaseReadingPause()
    {
        if (!ownsReadingPause) return;
        ownsReadingPause = false;
        if (readingPause != null && readingPause.IsPaused && !readingPause.IsSettingsOpen
            && !SceneTransitionController.IsBusy)
            readingPause.Resume();
    }

    private void RefreshLabel()
    {
        if (toggleLabel != null)
            toggleLabel.text = isOpen ? "도움말 닫기 · Esc 계속" : "도움말 열기 · 안전 정지";
    }

    private IEnumerator Animate(float target)
    {
        float start = content.preferredHeight;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            content.preferredHeight = Mathf.Lerp(start, target, curve.Evaluate(Mathf.Clamp01(t)));
            yield return null;
        }

        content.preferredHeight = target;
        routine = null;
    }
}
