using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Shaders for materials made at runtime. A shader that no material asset uses is left out of a build, so
    /// Shader.Find returns null there (and the built-in "Unlit/Color" would draw magenta under URP): URP/Unlit is in
    /// Graphics Settings > Always Included Shaders for this. Callers make the material first and skip the look
    /// when it is missing, so a failed lookup never leaves an object behind.
    /// </summary>
    public static class RuntimeShaders
    {
        private static Shader unlit;
        private static readonly Dictionary<Color, Material> unlitByColour = new Dictionary<Color, Material>();

        public static Shader Unlit => unlit != null ? unlit : unlit = Shader.Find("Universal Render Pipeline/Unlit");

        /// <summary>One shared flat-colour material per colour (null if the shader is missing).</summary>
        public static Material UnlitMaterial(Color colour)
        {
            if (unlitByColour.TryGetValue(colour, out Material m) && m != null) return m;
            if (Unlit == null) return null;
            m = new Material(Unlit) { name = "MAT_RuntimeUnlit", color = colour };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            unlitByColour[colour] = m;
            return m;
        }
    }
}
