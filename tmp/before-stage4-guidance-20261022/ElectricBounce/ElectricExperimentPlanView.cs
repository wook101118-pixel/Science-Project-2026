using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace sung_wook
{
    /// <summary>저장된 기존 버튼 두 개를 풀이 선택 기능에 연결한다.</summary>
    public sealed class ElectricExperimentPlanView : MonoBehaviour
    {
        [SerializeField] private Button firstPlanButton;
        [SerializeField] private Button secondPlanButton;
        [SerializeField] private TMP_Text firstPlanLabel;
        [SerializeField] private TMP_Text secondPlanLabel;
        private ElectricExperimentPlan2D plan;

        private void Awake()
        {
            if (firstPlanButton != null) firstPlanButton.onClick.AddListener(ChooseFirst);
            if (secondPlanButton != null) secondPlanButton.onClick.AddListener(ChooseSecond);
        }

        private void ChooseFirst() => plan?.SelectPlan(0);
        private void ChooseSecond() => plan?.SelectPlan(1);

        private void LateUpdate()
        {
            if (plan == null)
            {
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    plan = root.GetComponentInChildren<ElectricExperimentPlan2D>();
                    if (plan != null) break;
                }
            }
            bool available = plan != null && plan.ShowPlans;
            if (firstPlanButton != null) firstPlanButton.interactable = available && plan.CanSelect;
            if (secondPlanButton != null) secondPlanButton.interactable = available && plan.CanSelect;
            if (firstPlanLabel != null) firstPlanLabel.text = available
                ? (plan.SelectedPlan == 0 ? "[선택] " : "") + plan.PlanName(0) : "";
            if (secondPlanLabel != null) secondPlanLabel.text = available
                ? (plan.SelectedPlan == 1 ? "[선택] " : "") + plan.PlanName(1) : "";
        }

        private void OnDestroy()
        {
            if (firstPlanButton != null) firstPlanButton.onClick.RemoveListener(ChooseFirst);
            if (secondPlanButton != null) secondPlanButton.onClick.RemoveListener(ChooseSecond);
        }
    }
}
