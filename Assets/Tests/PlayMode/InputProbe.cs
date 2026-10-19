using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UInput = UnityEngine.InputSystem.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Managers;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Performance review tool (not a regular test): what keyboard and mouse input costs. Feeds synthetic Input System
    /// events (a 1000 Hz mouse sends many per frame) in first person and on the map, and compares each phase with the
    /// same scene without input. Writes input_report.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Performance review tool, run on demand")]
    public class InputProbe
    {
        private ProfilerRecorder mainThread, gcAlloc, behaviourUpdate, lateBehaviour, inputUpdate;
        private readonly StringBuilder report = new StringBuilder();
        private Keyboard kb;
        private Mouse mouse;
        private RenderTexture rt;

        [UnityTest]
        public IEnumerator ProfileInput()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);

            LogAssert.ignoreFailingMessages = true;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);

            // No focused game view in batch mode: let device input reach the game anyway (restored at the end)
            var behaviour = UInput.settings.editorInputBehaviorInPlayMode; var background = UInput.settings.backgroundBehavior;
            UInput.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            UInput.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            kb = Keyboard.current ?? UInput.AddDevice<Keyboard>();
            mouse = Mouse.current ?? UInput.AddDevice<Mouse>();
            kb.MakeCurrent(); mouse.MakeCurrent();

            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 15);
            behaviourUpdate = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BehaviourUpdate", 15);
            lateBehaviour = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "LateBehaviourUpdate", 15);
            inputUpdate = ProfilerRecorder.StartNew(ProfilerCategory.Input, "PreUpdate.NewInputUpdate", 15);
            if (!inputUpdate.Valid) { inputUpdate.Dispose(); inputUpdate = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "PreUpdate.NewInputUpdate", 15); }
            if (!inputUpdate.Valid) { inputUpdate.Dispose(); inputUpdate = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PreUpdate.NewInputUpdate", 15); }

            Camera cam = Camera.main;
            rt = new RenderTexture(1920, 1080, 24);
            GameObject player = GameObject.Find("Player");
            player.GetComponent<BattleOfAges.Combat.HealthComponent>()?.InitializeHealth(1000000);
            CharacterController cc = player.GetComponent<CharacterController>();

            report.AppendLine("# Battle of Ages — input cost probe");
            report.AppendLine($"Unity {Application.unityVersion}, {SystemInfo.processorType}; input recorder valid {inputUpdate.Valid}; keyboard {kb.name}, mouse {mouse.name}.");
            report.AppendLine();
            report.AppendLine("| Phase | frames | avg ms | p95 ms | max ms | main ms | scripts ms | input update ms | GC B/frame (median) | moved m | turned ° |");
            report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

            watched = player.transform;
            yield return Wait(3f, cam);

            // First person
            yield return Phase("FP idle", 4f, cam, null);
            yield return Phase("FP mouse look, 1 event/frame", 4f, cam, () => MouseDelta(1));
            yield return Phase("FP mouse look, 16 events/frame (1000 Hz)", 4f, cam, () => MouseDelta(16));
            yield return Phase("FP W held", 4f, cam, () => Keys(Key.W));
            float speed = player.GetComponent<BattleOfAges.Combat.CombatUnit>().MoveSpeed;
            yield return Phase($"FP scripted walk {speed:0.#} m/s, no input", 4f, cam, () => cc.Move(player.transform.forward * speed * Time.deltaTime));
            yield return Phase("FP W + mouse 16/frame", 4f, cam, () => { Keys(Key.W); MouseDelta(16); });
            yield return Phase("FP W+Shift+Space spam", 4f, cam, () => Keys(Time.frameCount % 2 == 0 ? new[] { Key.W, Key.LeftShift, Key.Space } : new[] { Key.W }));
            yield return Phase("FP Space spam standing (press every 6th frame)", 6f, cam, () => Keys(Time.frameCount % 6 < 3 ? new[] { Key.Space } : new Key[0]));
            yield return Phase("FP idle after jumps", 4f, cam, null);
            Keys();

            // Map
            UnityEngine.Object.FindFirstObjectByType<GameModeManager>().SetGameMode(GameMode.RtsMap);
            yield return Wait(1f, null);
            Camera map = Camera.allCameras.FirstOrDefault(c => c.enabled && c.GetComponent<BattleOfAges.CameraSystem.RtsMapCameraController>() != null) ?? cam;
            watched = map.transform;
            yield return Phase("Map idle", 4f, map, null);
            yield return Phase("Map mouse move, 16 events/frame", 4f, map, () => MouseMove(16));
            yield return Phase("Map WASD pan", 4f, map, () => Keys(Key.W, Key.D));
            yield return Phase("Map right-drag pan", 4f, map, () => { mouse.MakeCurrent(); MouseMove(16, right: true); });
            Keys();
            UInput.QueueStateEvent(mouse, new MouseState());

            File.WriteAllText(Path.Combine(dir, "input_report.md"), report.ToString());
            Debug.Log("[InputProbe] report written\n" + report);
            rt.Release();
            UInput.settings.editorInputBehaviorInPlayMode = behaviour; UInput.settings.backgroundBehavior = background;
            foreach (ProfilerRecorder r in new[] { mainThread, gcAlloc, behaviourUpdate, lateBehaviour, inputUpdate }) r.Dispose();
        }

        private void Keys(params Key[] keys) => UInput.QueueStateEvent(kb, new KeyboardState(keys));

        private void MouseDelta(int events)
        {
            for (int i = 0; i < events; i++) UInput.QueueDeltaStateEvent(mouse.delta, new Vector2(0.6f, 0.1f * Mathf.Sin(Time.frameCount * 0.1f)));
        }

        private Transform watched;
        private float mouseT;

        private void MouseMove(int events, bool right = false)
        {
            for (int i = 0; i < events; i++)
            {
                mouseT += 0.002f;
                var pos = new Vector2(960f + 500f * Mathf.Sin(mouseT), 540f + 300f * Mathf.Cos(mouseT * 1.3f));
                UInput.QueueStateEvent(mouse, new MouseState { position = pos, delta = new Vector2(1f, 1f), buttons = (ushort)(right ? 2 : 0) });
            }
        }

        private void Render(Camera c)
        {
            if (c == null) return;
            RenderTexture prev = c.targetTexture;
            c.targetTexture = rt;
            c.Render();
            c.targetTexture = prev;
        }

        private IEnumerator Wait(float seconds, Camera c)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { Render(c); yield return null; }
        }

        private IEnumerator Phase(string name, float seconds, Camera c, Action step)
        {
            Vector3 p0 = watched.position; float yaw0 = watched.eulerAngles.y;
            var frame = new List<float>(); var main = new List<float>(); var scripts = new List<float>(); var input = new List<float>(); var gc = new List<long>();
            yield return null;
            float end = Time.realtimeSinceStartup + seconds, last = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < end)
            {
                step?.Invoke();
                Render(c);
                yield return null;
                float now = Time.realtimeSinceStartup;
                frame.Add((now - last) * 1000f); last = now;
                main.Add(mainThread.LastValue * 1e-6f);
                scripts.Add((behaviourUpdate.LastValue + lateBehaviour.LastValue) * 1e-6f);
                input.Add(inputUpdate.LastValue * 1e-6f);
                gc.Add(gcAlloc.LastValue);
            }
            frame.Sort(); gc.Sort();
            report.AppendLine($"| {name} | {frame.Count} | {frame.Average():0.00} | {frame[(int)(frame.Count * 0.95f)]:0.00} | {frame[frame.Count - 1]:0} | {main.Average():0.00} | {scripts.Average():0.000} | {input.Average():0.000} | {gc[gc.Count / 2]} | {Vector3.Distance(p0, watched.position):0.0} | {Mathf.Abs(Mathf.DeltaAngle(yaw0, watched.eulerAngles.y)):0} |");
        }
    }
}
