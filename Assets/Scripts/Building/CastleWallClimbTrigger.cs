using UnityEngine;
using BattleOfAges.Player;

namespace BattleOfAges.Building
{
    /// <summary>
    /// Trigger volume placed on Castle Wall stairs.
    /// Provides smooth upward climb assistance to CharacterControllers walking up the stairs.
    /// </summary>
    [DisallowMultipleComponent]
    public class CastleWallClimbTrigger : MonoBehaviour
    {
        [SerializeField] private float climbAssistanceSpeed = 3.5f;

        private void OnTriggerStay(Collider other)
        {
            FirstPersonController fpc = other.GetComponent<FirstPersonController>();
            if (fpc != null)
            {
                CharacterController cc = fpc.GetComponent<CharacterController>();
                if (cc != null)
                {
                    // Provide gentle upward assistance along the stairs incline
                    Vector3 assist = (Vector3.up * 1.2f + transform.forward * 0.8f).normalized * (climbAssistanceSpeed * Time.deltaTime);
                    cc.Move(assist);
                }
            }
        }
    }
}
