namespace BattleOfAges.UI
{
    /// <summary>
    /// What a label shows, as a number: rebuild its text only when the number changes, so steady values make no garbage
    /// each frame (a string per label per frame adds up to GC hitches). Usage:
    /// <c>if (woodShown.Changed(wood)) woodText.text = wood.ToString();</c>
    /// </summary>
    public struct Shown
    {
        private long key;
        private bool set;

        public bool Changed(long value)
        {
            if (set && value == key) return false;
            set = true;
            key = value;
            return true;
        }

        /// <summary>Several values in one key (order matters).</summary>
        public static long Key(long a, long b) => a * 1000003L + b;
        public static long Key(long a, long b, long c) => Key(Key(a, b), c);
        public static long Key(long a, long b, long c, long d) => Key(Key(a, b, c), d);

        /// <summary>Forget the value: the next Changed is true.</summary>
        public void Reset() => set = false;
    }
}
