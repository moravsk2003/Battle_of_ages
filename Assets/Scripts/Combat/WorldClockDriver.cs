using UnityEngine;

namespace BattleOfAges.Combat
{
    /// <summary>Moves the WorldClock with game time (paused game: Time.deltaTime is 0).</summary>
    public sealed class WorldClockDriver : MonoBehaviour
    {
        private void Update()
        {
            WorldClock.Advance(Time.deltaTime);
            PendingHits.Tick(); // swings land after their wind-up, also for strikers that fell meanwhile
            BattleFront.Tick(); // which way each side's battle pulls
        }
    }
}
