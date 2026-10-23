using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace sung_wook
{
    /// <summary>씬 연결과 물리 공식을 검사한다. 별도 임시 씬의 검사 오브젝트는 종료 시 정리한다.</summary>
    public static class ElectricBounceValidation
    {
        public static int LastFailureCount { get; private set; }

        public static void RunBatch()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogError("RunBatch is only for an isolated Unity batch process. Use Validate Integration in the editor.");
                return;
            }
            try
            {
                if (SceneManager.GetActiveScene().path != ElectricBounceSceneInstaller.ScenePath)
                    EditorSceneManager.OpenScene(ElectricBounceSceneInstaller.ScenePath, OpenSceneMode.Single);
                Run();
                EditorApplication.Exit(LastFailureCount > 0 ? 1 : 0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        [Serializable]
        private sealed class Report
        {
            public string status;
            public string scenePath;
            public string mode = "Edit Mode; explicit component ticks, no input/Play Mode automation";
            public string physicsMode;
            public int passed;
            public List<string> checks = new List<string>();
            public List<string> failures = new List<string>();
            public List<string> skipped = new List<string>();
        }

        [MenuItem("Tools/sung_wook/Electric Bounce/Validate Integration")]
        public static void Run()
        {
            Scene originalScene = SceneManager.GetActiveScene();
            Scene probeScene = default;
            var report = new Report { scenePath = originalScene.path };
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                    "Run validation in Edit Mode; the user's running player is never simulated.");
                ValidateScene(report, originalScene);
                Check(report, "Physics model: charge signs, neutral force and inverse square distance", () =>
                {
                    Near(ElectricMath.CoulombForce(0, 1, 1, 16, 0.5f, 80), 0);
                    Near(ElectricMath.CoulombForce(1, 1, 2, 16, 0.5f, 80), 4);
                    Near(ElectricMath.CoulombForce(-1, 1, 2, 16, 0.5f, 80), -4);
                    Near(ElectricMath.CoulombForce(1, 1, 1, 16, 0.5f, 80), 16);
                    Near(ElectricMath.FieldForce(-1, 40, 2), -20);
                    Near(ElectricMath.SpeedFromVoltage(1, 100, 1)
                        / ElectricMath.SpeedFromVoltage(1, 25, 1), 2);
                });

                try
                {
                    probeScene = SceneManager.CreateScene("ElectricBounceValidation-" + Guid.NewGuid(),
                        new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                }
                catch (Exception exception)
                {
                    report.skipped.Add("Local scene creation unavailable: " + exception.Message);
                    probeScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                }
                Require(probeScene.IsValid() && probeScene != originalScene, "Could not create a disposable scene.");
                SceneManager.SetActiveScene(probeScene);
                PhysicsScene2D physics = probeScene.GetPhysicsScene2D();
                bool isolatedPhysics = physics.IsValid() && physics != Physics2D.defaultPhysicsScene;
                report.physicsMode = isolatedPhysics ? "Separate PhysicsScene2D, gravity disabled on probes"
                    : "Lifecycle-only fallback; default physics is never advanced";

                using (var probe = new Probe())
                {
                    ValidateLifecycle(report, probe);
                    if (isolatedPhysics)
                    {
                        Step(physics);
                        ValidateForces(report, probe, physics);
                        ValidateVoltage(report, probe, physics);
                        ValidateDistance(report, probe, physics);
                    }
                    else report.skipped.Add("Measured force, voltage-launch and distance-launch probes need local 2D physics.");
                }
            }
            catch (Exception exception)
            {
                report.failures.Add("Validation setup: " + UsefulException(exception).Message);
            }
            finally
            {
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                if (probeScene.IsValid() && probeScene != originalScene)
                {
                    // Only objects created in the disposable scene can be destroyed here.
                    foreach (GameObject root in probeScene.GetRootGameObjects()) Object.DestroyImmediate(root);
                    EditorSceneManager.CloseScene(probeScene, true);
                }
                report.status = report.failures.Count > 0 ? "failed" : report.skipped.Count > 0 ? "passed-with-skips" : "passed";
                LastFailureCount = report.failures.Count;
                string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                    "Logs", "ElectricBounceValidationReport.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                string summary = "Electric Bounce validation: " + report.status + ", " + report.passed
                    + " checks passed, " + report.failures.Count + " failures. Report: " + path;
                if (report.failures.Count == 0) Debug.Log(summary);
                else Debug.LogError(summary + "\n" + string.Join("\n", report.failures));
            }
        }

        private static void ValidateScene(Report report, Scene scene)
        {
            var selectors = new List<ElectricStageSelector>();
            foreach (GameObject root in scene.GetRootGameObjects())
                selectors.AddRange(root.GetComponentsInChildren<ElectricStageSelector>(true));
            Check(report, "Scene: exactly one configured stage selector", () =>
                Require(selectors.Count == 1, "Expected one ElectricStageSelector; run Connect or Rebind Current Scene first."));
            if (selectors.Count != 1) return;
            ElectricStageSelector selector = selectors[0];
            Check(report, "Scene: one player, movement, controller and electrical adapter on the linked player", () =>
            {
                Require(selector.Ball != null, "Selector has no player adapter.");
                GameObject player = selector.Ball.gameObject;
                Require(player.GetComponents<Player>().Length == 1, "Player component count is not one.");
                Require(player.GetComponents<PlayerMovement>().Length == 1, "PlayerMovement component count is not one.");
                Require(player.GetComponents<PlayerController>().Length == 1, "PlayerController component count is not one.");
                Require(player.GetComponents<ElectricBall2D>().Length == 1, "ElectricBall2D component count is not one.");
                Require(player.GetComponents<Rigidbody2D>().Length == 1, "Player rigidbody count is not one.");
            });
            Check(report, "Scene: four stage roots and exactly one active stage", () =>
            {
                Require(selector.StageRoots != null && selector.StageRoots.Length == 4, "Four stages are required.");
                int active = 0;
                var distinct = new HashSet<GameObject>();
                foreach (GameObject root in selector.StageRoots)
                {
                    Require(root != null && distinct.Add(root), "Stage root is missing or duplicated.");
                    if (root.activeInHierarchy) active++;
                }
                Require(active == 1, "Exactly one stage must be active.");
            });
            Check(report, "Scene: existing draggable reader and stage adapter are connected", () =>
            {
                Require(selector.Lever != null && selector.Lever.Reader != null, "Stage lever has no existing reader.");
                var reader = new SerializedObject(selector.Lever.Reader);
                var mover = reader.FindProperty("lever").objectReferenceValue as global::LeverMover;
                Require(mover != null && mover.MaxAngle > 0f, "Reader's LeverMover reference or angle is invalid.");
                var serializedMover = new SerializedObject(mover);
                Require(serializedMover.FindProperty("pivot").objectReferenceValue != null
                    && serializedMover.FindProperty("grabArea").objectReferenceValue != null,
                    "Existing lever requires both pivot and grab collider.");
            });
        }

        private static void ValidateLifecycle(Report report, Probe probe)
        {
            Check(report, "Lifecycle: Setup before Awake preserves state and reuses the authored neutral visual", () =>
            {
                probe.Player.Setup();
                Require(probe.Neutral.activeSelf, "Authored neutral visual was not selected.");
                int childCount = probe.Ball.transform.childCount;
                probe.Player.ChangeState(PlayerStateType.Positive);
                probe.Player.Setup();
                Require(probe.Player.CurrentState == PlayerStateType.Positive, "Repeated Setup reset the player's state.");
                Require(probe.Ball.transform.childCount == childCount, "Repeated Setup created duplicate visuals.");
                Require(!probe.Neutral.activeSelf && probe.Visual.CurrentVisualState == PlayerStateType.Positive,
                    "The original neutral visual overlaps the charged visual.");
                Require(probe.Ball.GetComponentsInChildren<SpriteRenderer>().Length == 1,
                    "More than one polarity sprite is active.");
            });
            Check(report, "Charge authority: the existing Player state and electric adapter agree for all signs", () =>
            {
                probe.Ball.SetNegative();
                Require(probe.Player.CurrentState == PlayerStateType.Negative && probe.Ball.SignedCharge < 0,
                    "Negative enum was treated as a positive numeric charge.");
                probe.Player.ChangeState(PlayerStateType.Positive);
                Near(probe.Ball.SignedCharge, 1);
                probe.Ball.SetNeutral();
                Near(probe.Ball.SignedCharge, 0);
                Require(probe.Neutral.activeSelf, "Neutral reset did not restore the original visual.");
            });
            Check(report, "Reset: a cleared/frozen ball restores simulation, polarity, position and stars", () =>
            {
                probe.Ball.CollectStar();
                probe.Ball.SetNegative();
                probe.Ball.Body.linearVelocity = new Vector2(3, 4);
                probe.Ball.Body.simulated = false;
                probe.Movement.enabled = false;
                probe.Ball.enabled = false;
                probe.Ball.ResetAt(new Vector2(2, 3), Polarity.Positive);
                Require(probe.Ball.CanReceiveForce && probe.Ball.Stars == 0, "Frozen ball was not restored.");
                Require(probe.Player.CurrentState == PlayerStateType.Positive, "Reset polarity is not authoritative.");
                Near((probe.Ball.Body.position - new Vector2(2, 3)).magnitude, 0);
                Near(probe.Ball.Body.linearVelocity.magnitude, 0);
            });
        }

        private static void ValidateForces(Report report, Probe probe, PhysicsScene2D physics)
        {
            Check(report, "Local physics: electric force reverses velocity with its sign", () =>
            {
                probe.Ball.ResetAt(Vector2.zero, Polarity.Positive);
                probe.Ball.AddElectricForce(Vector2.right * 10f);
                Step(physics);
                Require(probe.Ball.Body.linearVelocity.x > 0f, "Positive electric force did not accelerate right.");
                probe.Ball.ResetAt(Vector2.zero, Polarity.Positive);
                probe.Ball.AddElectricForce(Vector2.left * 10f);
                Step(physics);
                Require(probe.Ball.Body.linearVelocity.x < 0f, "Negative electric force did not accelerate left.");
            });
            Check(report, "Local physics: launch replaces prior velocity and scales impulse by mass", () =>
            {
                probe.Ball.ResetAt(Vector2.zero, Polarity.Positive);
                probe.Ball.Body.mass = 2;
                probe.Ball.Body.linearVelocity = new Vector2(9, -3);
                Require(probe.Ball.LaunchImpulse(new Vector2(3, 4)), "Valid launch was rejected.");
                Step(physics);
                Near(probe.Ball.Body.linearVelocity.x, 1.5f);
                Near(probe.Ball.Body.linearVelocity.y, 2f);
                probe.Ball.Body.mass = 1;
                Tick(probe.Movement);
                Near(probe.Ball.Body.linearVelocity.x, 1.5f);
            });
        }

        private static void ValidateVoltage(Report report, Probe probe, PhysicsScene2D physics)
        {
            Check(report, "Local physics: invalid voltage launches retain charge; valid launch converts qV to kinetic energy", () =>
            {
                probe.Ball.Body.mass = 1;
                probe.Ball.ResetAt(Vector2.zero, Polarity.Neutral);
                StageLever2D lever = probe.Create("Voltage lever").AddComponent<StageLever2D>();
                var zone = probe.Create("Voltage zone").AddComponent<BoxCollider2D>();
                zone.size = Vector2.one * 4f;
                Transform bottom = probe.Point("Bottom", Vector2.down);
                Transform top = probe.Point("Top", Vector2.up);
                var launcher = probe.Create("Voltage launcher").AddComponent<VoltageLauncher2D>();
                launcher.Configure(probe.Ball, lever, zone, bottom, top);
                lever.SetValue(1f);
                Require(Time.fixedDeltaTime > 0, "Fixed timestep must be positive.");
                int ticks = Mathf.CeilToInt(50f / (25f * Time.fixedDeltaTime)) + 1;
                for (int i = 0; i < ticks; i++) Tick(launcher);
                lever.SetValue(0f);
                float storedVoltage = launcher.Voltage;
                Require(storedVoltage >= 50f, "Charging did not reach its target.");
                Step(physics);
                launcher.RequestLaunch();
                Tick(launcher);
                Near(launcher.Voltage, storedVoltage);
                Near(probe.Ball.Body.linearVelocity.magnitude, 0);
                probe.Ball.ResetAt(Vector2.right * 10f, Polarity.Positive);
                launcher.RequestLaunch();
                Tick(launcher);
                Near(launcher.Voltage, storedVoltage);
                probe.Ball.ResetAt(Vector2.zero, Polarity.Positive);
                launcher.RequestLaunch();
                Tick(launcher);
                Step(physics);
                Near(launcher.Voltage, 0);
                Require(probe.Ball.Body.linearVelocity.y > 0, "Voltage launch did not point from lower to upper plate.");
                float kineticEnergy = 0.5f * probe.Ball.Body.mass * probe.Ball.Body.linearVelocity.sqrMagnitude;
                Near(kineticEnergy, probe.Ball.SignedCharge * storedVoltage, 0.02f);
                launcher.ResetCharge();
                Near(launcher.NormalizedVoltage, 0);
            });
        }

        private static void ValidateDistance(Report report, Probe probe, PhysicsScene2D physics)
        {
            Check(report, "Local physics: moving the same source closer strengthens a launch by inverse square distance", () =>
            {
                probe.Ball.Body.mass = 1;
                probe.Ball.ResetAt(Vector2.zero, Polarity.Positive);
                StageLever2D lever = probe.Create("Distance lever").AddComponent<StageLever2D>();
                Transform source = probe.Point("Source", Vector2.zero);
                Transform far = probe.Point("Far", new Vector2(-1.5f, -2));
                Transform near = probe.Point("Near", new Vector2(-0.45f, -0.6f));
                var launcher = probe.Create("Distance launcher").AddComponent<DistanceLauncher2D>();
                launcher.Configure(probe.Ball, lever, source, far, near);
                lever.SetValue(0f);
                launcher.RequestLaunch();
                Tick(launcher);
                Step(physics);
                Vector2 farVelocity = probe.Ball.Body.linearVelocity;
                Require(farVelocity.magnitude > 0f, "Far-distance launch was rejected.");
                probe.Ball.ResetAt(Vector2.zero, Polarity.Positive);
                launcher.ResetLaunch();
                lever.SetValue(1f);
                launcher.RequestLaunch();
                Tick(launcher);
                Step(physics);
                Vector2 nearVelocity = probe.Ball.Body.linearVelocity;
                Near(nearVelocity.magnitude / farVelocity.magnitude, 2.5f * 2.5f / (0.75f * 0.75f), 0.02f);
                Near(Vector2.Dot(nearVelocity.normalized, farVelocity.normalized), 1);
                probe.Ball.ResetAt(Vector2.zero, Polarity.Negative);
                launcher.ResetLaunch();
                launcher.RequestLaunch();
                Tick(launcher);
                Step(physics);
                Near(probe.Ball.Body.linearVelocity.magnitude, 0);
            });
        }

        private sealed class Probe : IDisposable
        {
            private readonly GameObject root = new GameObject("Electric Bounce disposable validation");
            public readonly ElectricBall2D Ball;
            public readonly Player Player;
            public readonly PlayerMovement Movement;
            public readonly ChangePlayerVisual Visual;
            public readonly GameObject Neutral;

            public Probe()
            {
                root.hideFlags = HideFlags.DontSave;
                GameObject ball = Create("Probe ball");
                Neutral = new GameObject("Visual", typeof(SpriteRenderer));
                Neutral.transform.SetParent(ball.transform, false);
                Visual = ball.AddComponent<ChangePlayerVisual>();
                Visual.root = ball;
                Visual._positiveVisual = Create("Positive template");
                Visual._positiveVisual.AddComponent<SpriteRenderer>();
                Visual._positiveVisual.SetActive(false);
                Visual._negativeVisual = Create("Negative template");
                Visual._negativeVisual.AddComponent<SpriteRenderer>();
                Visual._negativeVisual.SetActive(false);
                ball.AddComponent<CircleCollider2D>().radius = 0.25f;
                Rigidbody2D body = ball.AddComponent<Rigidbody2D>();
                body.gravityScale = 0;
                body.linearDamping = 0;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                Player = ball.AddComponent<Player>();
                Movement = ball.AddComponent<PlayerMovement>();
                Ball = ball.AddComponent<ElectricBall2D>();
            }

            public GameObject Create(string name)
            {
                var child = new GameObject(name);
                child.transform.SetParent(root.transform, false);
                return child;
            }

            public Transform Point(string name, Vector2 position)
            {
                Transform point = Create(name).transform;
                point.position = position;
                return point;
            }

            public void Dispose() => Object.DestroyImmediate(root);
        }

        private static void Step(PhysicsScene2D scene)
        {
            Require(scene.IsValid() && scene != Physics2D.defaultPhysicsScene, "Refusing to simulate the user's physics scene.");
            Require(scene.Simulate(0.02f), "Local PhysicsScene2D rejected the manual simulation step.");
        }

        private static void Tick(MonoBehaviour component)
        {
            MethodInfo method = component.GetType().GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null, "Component has no physics tick: " + component.GetType().Name);
            method.Invoke(component, null);
        }

        private static void Check(Report report, string label, Action action)
        {
            try { action(); report.passed++; report.checks.Add(label); }
            catch (Exception exception) { report.failures.Add(label + ": " + UsefulException(exception).Message); }
        }

        private static Exception UsefulException(Exception exception) =>
            exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;

        private static void Near(float actual, float expected, float tolerance = 0.001f) =>
            Require(!float.IsNaN(actual) && Mathf.Abs(actual - expected) <= tolerance,
                "Expected " + expected + ", observed " + actual + ".");

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
