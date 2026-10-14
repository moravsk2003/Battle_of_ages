using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Procedural wildlife (Deer, Boar, Rabbit), each sculpted with SdfCreature on a small joint hierarchy that
    /// PeacefulAnimalAI animates: VisualModel / LegPivot_0..3 (hip / shoulder) / Knee_0..3, NeckPivot / Head
    /// (/ Ear_L, Ear_R), TailPivot. Leg order: 0 front-left, 1 front-right, 2 hind-left, 3 hind-right. Animals face +Z.
    /// </summary>
    public static class AnimalModelBuilder
    {
        public static GameObject CreateAnimalPrefab(AnimalType type, string name = "Animal")
        {
            GameObject root = new GameObject(name);

            CharacterController charCtrl = root.AddComponent<CharacterController>();
            charCtrl.height = (type == AnimalType.Rabbit) ? 0.8f : 1.6f;
            charCtrl.radius = (type == AnimalType.Rabbit) ? 0.35f : 0.6f;
            charCtrl.center = new Vector3(0f, charCtrl.height * 0.5f, 0f);
            charCtrl.stepOffset = 0.4f;

            HealthComponent hp = root.AddComponent<HealthComponent>();
            int maxHp = (type == AnimalType.Boar) ? 120 : (type == AnimalType.Deer ? 80 : 30);
            hp.InitializeHealth(maxHp);

            PeacefulAnimalAI ai = root.AddComponent<PeacefulAnimalAI>();

            GameObject visualModel = new GameObject("VisualModel");
            visualModel.transform.SetParent(root.transform, false);

            switch (type)
            {
                case AnimalType.Deer:
                    DeerModelBuilder.Build(visualModel.transform);
                    break;
                case AnimalType.Boar:
                    BoarModelBuilder.Build(visualModel.transform);
                    break;
                case AnimalType.Rabbit:
                    RabbitModelBuilder.Build(visualModel.transform);
                    break;
            }

            // Applies per-type stats and caches the VisualModel/Head nodes, which did not exist yet
            // when the AI's Awake ran (otherwise every animal behaved as a Deer and was never animated).
            ai.InitializeAnimal(type);

            return root;
        }
    }
}
