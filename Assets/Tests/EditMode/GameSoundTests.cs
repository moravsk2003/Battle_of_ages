using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.UI;

namespace BattleOfAges.Tests
{
    /// <summary>
    /// The sounds made in code: every one is finite, has a body, ends quietly and differs between its variants; the
    /// wind loops without a seam; loudness falls with distance. With SFX_DUMP set to a folder they are written there as
    /// WAV files to listen to.
    /// </summary>
    [TestFixture]
    public class GameSoundTests
    {
        [Test]
        public void EverySound_IsFinite_Audible_AndEndsQuietly()
        {
            foreach (GameSound.Kind kind in Enum.GetValues(typeof(GameSound.Kind)))
            {
                float[] a = GameSound.Synth(kind, 0), b = GameSound.Synth(kind, 2);
                Assert.That(a.Length, Is.InRange(GameSound.Rate / 20, GameSound.Rate * 2), kind + ": length");
                double sum = 0, diff = 0;
                float peak = 0f;
                for (int i = 0; i < a.Length; i++)
                {
                    Assert.IsFalse(float.IsNaN(a[i]) || float.IsInfinity(a[i]), kind + " at " + i);
                    peak = Mathf.Max(peak, Mathf.Abs(a[i]));
                    sum += a[i] * a[i];
                    if (i < b.Length) diff += Mathf.Abs(a[i] - b[i]);
                }
                Assert.AreEqual(0.9f, peak, 0.01f, kind + ": peak");
                Assert.Greater(Math.Sqrt(sum / a.Length), 0.03, kind + ": too faint for its length");
                Assert.Less(Mathf.Abs(a[a.Length - 1]), 0.01f, kind + ": ends with a click");
                Assert.Greater(diff / a.Length, 0.01, kind + ": the variants are the same");
            }
        }

        [Test]
        public void TheWind_LoopsWithoutASeam_AndBirdsCall()
        {
            float[] w = Ambience.Wind(10f, 11);
            Assert.AreEqual(10 * GameSound.Rate, w.Length);
            float step = Mathf.Abs(w[w.Length - 1] - w[0]), typical = 0f;
            for (int i = 1; i < w.Length; i++) typical = Mathf.Max(typical, Mathf.Abs(w[i] - w[i - 1]));
            Assert.LessOrEqual(step, typical, "the jump at the loop is no bigger than one inside it");
            for (int s = 40; s < 46; s++)
            {
                float[] bird = Ambience.Bird(s);
                Assert.Greater(bird.Length, GameSound.Rate / 10);
                foreach (float x in bird) Assert.IsFalse(float.IsNaN(x));
            }
        }

        [Test]
        public void Loudness_FallsWithDistance_ToSilence()
        {
            Assert.AreEqual(1f, GameSound.Loudness(0f), 1e-4f);
            Assert.AreEqual(1f, GameSound.Loudness(5f), 1e-4f);
            float last = 1f;
            for (float d = 10f; d <= GameSound.HearRange; d += 10f)
            {
                float g = GameSound.Loudness(d);
                Assert.Less(g, last);
                last = g;
            }
            Assert.AreEqual(0f, GameSound.Loudness(GameSound.HearRange + 1f), 1e-4f);
        }

        [Test]
        public void DumpToWav_WhenAsked()
        {
            string dir = Environment.GetEnvironmentVariable("SFX_DUMP");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set SFX_DUMP to a folder to write the sounds there");
            Directory.CreateDirectory(dir);
            foreach (GameSound.Kind kind in Enum.GetValues(typeof(GameSound.Kind)))
                for (int v = 0; v < GameSound.Variants; v++) Wav(Path.Combine(dir, $"{kind}_{v}.wav"), GameSound.Synth(kind, v));
            Wav(Path.Combine(dir, "Ambience_Wind.wav"), Ambience.Wind(10f, 11));
            for (int s = 0; s < 6; s++) Wav(Path.Combine(dir, $"Ambience_Bird_{s}.wav"), Ambience.Bird(40 + s));
        }

        /// <summary>16-bit mono PCM.</summary>
        private static void Wav(string path, float[] d)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = d.Length * 2;
                w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray());
                w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(GameSound.Rate); w.Write(GameSound.Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(bytes);
                foreach (float x in d) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(x, -1f, 1f) * 32767f));
            }
        }
    }
}
