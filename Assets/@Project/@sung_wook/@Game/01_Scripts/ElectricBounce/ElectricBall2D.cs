using UnityEngine;

namespace sung_wook
{
                                                                          
    public enum Polarity
    {
        Negative = -1,
        Neutral = 0,
        Positive = 1
    }

                 
                                 
                                                       
                  
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Player), typeof(PlayerMovement), typeof(Rigidbody2D))]
    public sealed class ElectricBall2D : MonoBehaviour
    {
        [SerializeField, Range(0f, 10f)]
        private float chargeMagnitude = 1f;
        private Player player;
        private PlayerMovement movement;

        public Rigidbody2D Body => Movement.Body;

        public Player Player
        {
            get
            {
                if (player == null)
                {
                    player = GetComponent<Player>();
                }
                return player;
            }
        }

        private PlayerMovement Movement
        {
            get
            {
                if (movement == null)
                {
                    movement = GetComponent<PlayerMovement>();
                }
                return movement;
            }
        }

        public Polarity CurrentPolarity => (Polarity)ChargeSign(Player.CurrentState);
        public float ChargeMagnitude => chargeMagnitude;
        public float SignedCharge => ChargeSign(Player.CurrentState) * chargeMagnitude;
        public int Stars { get; private set; }

                                                 
        public bool CanReceiveForce => isActiveAndEnabled && Movement != null
            && Movement.isActiveAndEnabled && Body != null && Body.simulated
            && Body.bodyType == RigidbodyType2D.Dynamic;

        private void Awake()
        {
            ValidateCharge();
            player = GetComponent<Player>();
            movement = GetComponent<PlayerMovement>();
            player.Setup();                                          
        }

        private void OnValidate() => ValidateCharge();

        private void ValidateCharge()
        {
            if (float.IsNaN(chargeMagnitude) || float.IsInfinity(chargeMagnitude))
            {
                chargeMagnitude = 1f;
            }
            chargeMagnitude = Mathf.Clamp(chargeMagnitude, 0f, 10f);
        }

                                                                      
        public void SetChargeMagnitude(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return;
            }
            chargeMagnitude = Mathf.Clamp(value, 0f, 10f);
        }

                                                                       
        public static int ChargeSign(PlayerStateType state)
        {
            switch (state)
            {
                case PlayerStateType.Positive:
                    return 1;
                case PlayerStateType.Negative:
                    return -1;
                default:
                    return 0;
            }
        }

                                                                       
        public void SetPolarity(Polarity value)
        {
            PlayerStateType state = value == Polarity.Positive
                ? PlayerStateType.Positive
                : value == Polarity.Negative ? PlayerStateType.Negative : PlayerStateType.Neutral;
            Player.ChangeState(state);
        }

        public void SetPositive()
        {
            SetPolarity(Polarity.Positive);
        }

        public void SetNegative()
        {
            SetPolarity(Polarity.Negative);
        }

        public void SetNeutral()
        {
            SetPolarity(Polarity.Neutral);
        }

                                                            
        public void CollectStar()
        {
            Stars++;
        }

        public void ResetStars()
        {
            Stars = 0;
        }

                                                                           
        public void AddElectricForce(Vector2 force)
        {
            if (CanReceiveForce)
            {
                Movement.ApplyElectricForce(force);
            }
        }

                                                                          
        public bool LaunchImpulse(Vector2 impulse)
        {
            return CanReceiveForce && Movement.LaunchImpulse(impulse);
        }

                                                                    
        public void ReturnToCheckpoint(Vector2 position)
        {
            enabled = true;
            Movement.enabled = true;
            Body.simulated = true;
            Body.position = position;
            Movement.ResetMotion();
        }

                                                                         
        public void ResetAt(Vector2 position, Polarity polarity)
        {
            enabled = true;
            Movement.enabled = true;
            Body.simulated = true;
            Body.position = position;
            Movement.ResetMotion();
            SetPolarity(polarity);
            ResetStars();
        }
    }
}
