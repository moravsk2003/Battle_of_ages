using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

namespace BattleOfAges.Building
{
    /// <summary>
    /// The gate's two leaves: they swing open inward while the player or one of the player's units is near and close
    /// after (for enemies the gate stays shut: §10.5). The leaves' colliders turn with them, out of the opening.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallGateDoors : MonoBehaviour
    {
        public const float OpenAngle = 92f, SwingSpeed = 110f, CheckEvery = 0.2f;

        [SerializeField] private Transform left, right;
        private float angle, checkClock;
        private Transform player;

        public bool WantsOpen { get; private set; }
        public float Angle => angle;

        public void SetLeaves(Transform l, Transform r) { left = l; right = r; }

        private void Update()
        {
            checkClock -= Time.deltaTime;
            if (checkClock <= 0f)
            {
                checkClock = CheckEvery;
                WantsOpen = OwnNear(transform.position, GameBalance.GateOpenRadius);
            }
            float target = WantsOpen ? OpenAngle : 0f;
            if (Mathf.Approximately(angle, target)) return;
            angle = Mathf.MoveTowards(angle, target, SwingSpeed * Time.deltaTime);
            if (left != null) left.localRotation = Quaternion.Euler(0f, angle, 0f);
            if (right != null) right.localRotation = Quaternion.Euler(0f, -angle, 0f);
        }

        private bool OwnNear(Vector3 at, float radius)
        {
            float r2 = radius * radius;
            if (player == null) { GameObject p = Managers.GameContext.Player; if (p != null) player = p.transform; }
            if (player != null && (player.position - at).sqrMagnitude < r2) return true;
            var units = UnitRegistry.ActiveUnits;
            for (int i = 0; i < units.Count; i++)
                if (units[i] != null && (units[i].transform.position - at).sqrMagnitude < r2) return true;
            return false;
        }
    }
}
