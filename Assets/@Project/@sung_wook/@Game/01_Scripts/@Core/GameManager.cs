using UnityEngine;

namespace sung_wook
{
    public class GameManager : Singleton<GameManager>
    {
        [field: SerializeField] public PlayerController        PlayerController { get; private set; }
        [field: SerializeField] public Player                  PlayerCompo { get; private set; }
        [field: SerializeField] public GearLeverController[]   GearLeverController { get; private set; }
        [field: SerializeField] public GearLever               LeverCompo { get; private set; }

        private void Awake()
        {
            if (PlayerCompo == null && PlayerController != null)
            {
                PlayerCompo = PlayerController.GetComponent<Player>();
            }
            
            if (PlayerCompo != null) PlayerCompo.Setup();
            if (LeverCompo != null) LeverCompo.Setup();
        }
    }
}
