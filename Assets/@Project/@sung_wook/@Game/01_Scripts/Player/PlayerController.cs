using UnityEngine;

namespace sung_wook
{
    public class PlayerController : MonoBehaviour
    {
        public Player PlayerCompo {get; private set;}
        public PlayerMovement MoveCompo {get; private set;}
        public PlayerInputReader InputCompo {get; private set;}

        private void Awake()
        {
            PlayerCompo = GetComponent<Player>();
            MoveCompo = GetComponent<PlayerMovement>();
            InputCompo = GetComponent<PlayerInputReader>();
        }

        private void Update()
        {
            MoveCompo.SetMovex(InputCompo.MoveVector.x);
        }
    }   
}
