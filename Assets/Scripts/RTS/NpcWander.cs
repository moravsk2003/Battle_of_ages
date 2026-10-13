using UnityEngine;
using UnityEngine.AI;

namespace BattleOfAges.RTS
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RtsUnit))]
    public class NpcWander : MonoBehaviour
    {
        [Header("Wander Settings")]
        [SerializeField] private float wanderRadius = 12.0f;
        [SerializeField] private float wanderInterval = 6.0f;

        private RtsUnit rtsUnit;
        private NpcResourceGatherer gatherer;
        private Vector3 originPosition;
        private float timer;

        private void Start()
        {
            rtsUnit = GetComponent<RtsUnit>();
            gatherer = GetComponent<NpcResourceGatherer>();
            originPosition = transform.position;
            timer = Random.Range(1.0f, wanderInterval); // Stagger start time
        }

        private void Update()
        {
            // Only wander if unit is active and not currently controlled/selected by player
            if (rtsUnit != null && rtsUnit.IsSelected)
                return;

            // If unit is a resource gatherer, let NpcResourceGatherer handle AI (it may be added later; re-check rarely)
            if (gatherer == null && Time.frameCount % 30 == 0) gatherer = GetComponent<NpcResourceGatherer>();
            if (gatherer != null)
                return;

            timer += Time.deltaTime;
            if (timer >= wanderInterval)
            {
                timer = 0f;

                // Pick a random point around origin position
                Vector2 randCircle = Random.insideUnitCircle * wanderRadius;
                Vector3 newPos = originPosition + new Vector3(randCircle.x, 0f, randCircle.y);
                newPos.y = RtsUnit.GetTerrainHeight(newPos);

                if (rtsUnit != null)
                {
                    rtsUnit.IssueMoveCommand(newPos);
                }
            }
        }
    }
}
