using UnityEngine;

public class LeverReader : MonoBehaviour
{
    [SerializeField] LeverMover lever;

    [SerializeField] float percent;
    public float Percent => (lever.Value + 1f) * 50f;

    void Update()
    {
        percent = Percent;
    }
}
