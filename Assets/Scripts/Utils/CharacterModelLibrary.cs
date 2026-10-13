using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Rigged character models and their animator controllers, loaded from Resources so that characters spawned at
    /// runtime (raider camps, workers) can use them. Created and filled by the editor tool
    /// "Battle of Ages/Setup Rigged Character Models"; if it is missing, HumanoidModelBuilder falls back to the
    /// procedural primitive characters.
    /// </summary>
    public class CharacterModelLibrary : ScriptableObject
    {
        public const string ResourcePath = "CharacterModelLibrary";

        public GameObject KnightModel;
        public RuntimeAnimatorController KnightController;
        public Avatar KnightAvatar;
        public Mesh KnightMesh;       // processed: smoothed normals + rest positions in UV3
        public GameObject VillagerModel;
        public RuntimeAnimatorController VillagerController;
        public Avatar VillagerAvatar;
        public Mesh VillagerMesh;
        public Vector3 VillagerEyeLeft, VillagerEyeRight; // rest-pose eye centres (metres, character space), for brows / beards

        private static CharacterModelLibrary instance;
        private static bool searched;

        public static CharacterModelLibrary Instance
        {
            get
            {
                if (instance == null && !searched)
                {
                    instance = UnityEngine.Resources.Load<CharacterModelLibrary>(ResourcePath);
                    searched = instance != null;
                }
                return instance;
            }
        }
    }
}
