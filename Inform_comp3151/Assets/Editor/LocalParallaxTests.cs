#if UNITY_INCLUDE_TESTS
using System.Reflection;
using Inkform.Fx;
using Inkform.Life;
using Inkform.Tool;
using NUnit.Framework;
using UnityEngine;

namespace Inkform.Tests
{
    /// <summary>
    /// Local parallax: a zone's strata are always-visible child transforms that sit still until
    /// the player enters the trigger box — from that moment each tracks the camera at its own
    /// factor, and on leaving (or death, or respawn outside) they freeze in place. Re-entering
    /// re-anchors them to wherever they and the camera now sit.
    ///
    /// Edit mode: MonoBehaviour lifecycle callbacks do not run here, so Awake/LateUpdate/Enter
    /// are driven through reflection (same pattern as AbilityAndPromptTests).
    /// </summary>
    public class LocalParallaxTests
    {
        // ---- Harness ----

        // Edit mode creates no scene teardown — everything built here is destroyed per test.
        private static readonly System.Collections.Generic.List<Object> Created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in Created)
                if (created != null) Object.DestroyImmediate(created);
            Created.Clear();
        }

        private static GameObject Track(GameObject go)
        {
            Created.Add(go);
            return go;
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);

        private static object ReadField(object target, string field) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(target);

        private static void Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(target, args);

        /// <summary>A zone at worldPos whose strata list points at child transforms placed at
        /// worldPos + offsets[i], with factors[i]. The camera starts at camPos.</summary>
        private static LocalParallaxZone NewZone(Vector2 worldPos, Vector2 camPos,
            Vector2[] offsets, Vector2[] factors, out Transform[] strata)
        {
            var cameraGo = Track(new GameObject("camera"));
            cameraGo.transform.position = new Vector3(camPos.x, camPos.y, -10f);

            var zoneGo = Track(new GameObject("zone"));
            zoneGo.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            var zone = zoneGo.AddComponent<LocalParallaxZone>();

            strata = new Transform[offsets.Length];
            var list = new System.Collections.Generic.List<LocalParallaxZone.Stratum>();
            for (int i = 0; i < offsets.Length; i++)
            {
                var stratumGo = Track(new GameObject($"stratum{i}"));
                stratumGo.transform.SetParent(zoneGo.transform, false);
                stratumGo.transform.position = new Vector3(worldPos.x + offsets[i].x, worldPos.y + offsets[i].y, 0f);
                strata[i] = stratumGo.transform;

                list.Add(new LocalParallaxZone.Stratum
                {
                    target = stratumGo.transform,
                    factorX = factors[i].x,
                    factorY = factors[i].y,
                });
            }

            SetField(zone, "strata", list.ToArray());
            SetField(zone, "cameraTransform", cameraGo.transform);
            Invoke(zone, "Awake");
            return zone;
        }

        private static void Step(LocalParallaxZone zone) => Invoke(zone, "LateUpdate");

        private static Transform CameraOf(LocalParallaxZone zone) =>
            (Transform)ReadField(zone, "cameraTransform");

        // ---- Idle: outside the zone nothing moves ----

        [Test]
        public void OutsideZone_StrataNeverMove()
        {
            Transform[] strata;
            LocalParallaxZone zone = NewZone(Vector2.zero, Vector2.zero,
                new[] { new Vector2(5f, -2f) }, new[] { new Vector2(0.5f, 1f) }, out strata);

            CameraOf(zone).position = new Vector3(20f, 10f, -10f);
            Step(zone);
            Step(zone);

            Assert.AreEqual(5f, strata[0].position.x, 0.0001f, "no entry: the stratum sits where it was authored");
            Assert.AreEqual(-2f, strata[0].position.y, 0.0001f);
        }

        // ---- Inside: anchored tracking at each stratum's own factor ----

        [Test]
        public void Enter_CameraAtCentre_ShowsAuthoredComposition()
        {
            Transform[] strata;
            LocalParallaxZone zone = NewZone(Vector2.zero, Vector2.zero,
                new[] { new Vector2(5f, -2f) }, new[] { new Vector2(0.5f, 1f) }, out strata);

            Invoke(zone, "Enter");
            Step(zone);

            Assert.AreEqual(5f, strata[0].position.x, 0.0001f,
                "camera at the zone centre: the stratum sits exactly where it was authored");
            Assert.AreEqual(-2f, strata[0].position.y, 0.0001f);
        }

        [Test]
        public void OffCentreEntry_AlreadyShowsTheOffsetComposition()
        {
            // The distinguishing case against enter-anchoring: entering off-centre must NOT
            // treat the entry point as the reference. The reference is always the zone centre.
            Transform[] strata;
            LocalParallaxZone zone = NewZone(Vector2.zero, new Vector2(8f, 0f),
                new[] { new Vector2(5f, 0f) }, new[] { new Vector2(0.5f, 1f) }, out strata);

            Invoke(zone, "Enter");
            Step(zone);

            Assert.AreEqual(5f + 8f * 0.5f, strata[0].position.x, 0.0001f,
                "entering at cam x=8 shows authored + 8*factor — not the authored spot itself");
        }

        [Test]
        public void CameraBackAtCentre_RestoresAuthoredComposition()
        {
            Transform[] strata;
            LocalParallaxZone zone = NewZone(Vector2.zero, Vector2.zero,
                new[] { new Vector2(5f, 0f) }, new[] { new Vector2(0.5f, 1f) }, out strata);
            Transform camera = CameraOf(zone);

            Invoke(zone, "Enter");
            camera.position = new Vector3(10f, 0f, camera.position.z);
            Step(zone);
            Assert.AreEqual(5f + 10f * 0.5f, strata[0].position.x, 0.0001f);

            camera.position = new Vector3(0f, 0f, camera.position.z);
            Step(zone);
            Assert.AreEqual(5f, strata[0].position.x, 0.0001f,
                "walking back to the centre restores the authored layout — WYSIWYG");
        }

        [Test]
        public void CameraMove_DisplacesEachStratumByItsOwnFactor()
        {
            Transform[] strata;
            LocalParallaxZone zone = NewZone(Vector2.zero, Vector2.zero,
                new[] { new Vector2(5f, -2f), Vector2.zero },
                new[] { new Vector2(0.5f, 1f), new Vector2(0.25f, 1f) }, out strata);
            Transform camera = CameraOf(zone);

            Invoke(zone, "Enter");
            camera.position = new Vector3(10f, -4f, camera.position.z);
            Step(zone);

            Assert.AreEqual(5f + 10f * 0.5f, strata[0].position.x, 0.0001f, "stratum 0 follows at factor 0.5");
            Assert.AreEqual(10f * 0.25f, strata[1].position.x, 0.0001f, "stratum 1 follows at factor 0.25");
            Assert.AreEqual(-2f + -4f * 1f, strata[0].position.y, 0.0001f, "factorY 1 pins vertically");
        }

        // ---- Leaving: frozen in place ----

        [Test]
        public void Exit_FreezesStrataWhereTheyAre()
        {
            Transform[] strata;
            LocalParallaxZone zone = NewZone(Vector2.zero, Vector2.zero,
                new[] { new Vector2(5f, 0f) }, new[] { new Vector2(0.5f, 1f) }, out strata);
            Transform camera = CameraOf(zone);

            Invoke(zone, "Enter");
            Step(zone);
            camera.position = new Vector3(10f, 0f, camera.position.z);
            Step(zone);
            Assert.AreEqual(10f, strata[0].position.x, 0.0001f);

            Collider2D player = PlayerCollider();
            Invoke(zone, "OnTriggerExit2D", player);
            camera.position = new Vector3(50f, 0f, camera.position.z);
            Step(zone);

            Assert.AreEqual(10f, strata[0].position.x, 0.0001f,
                "outside the zone the camera can walk anywhere — the stratum stays frozen");
        }

        /// <summary>A collider tagged Player — the zone's callbacks filter on it.</summary>
        private static Collider2D PlayerCollider()
        {
            var playerGo = Track(new GameObject("player"));
            playerGo.tag = Tags.Player;
            return playerGo.AddComponent<BoxCollider2D>();
        }

        [Test]
        public void Reenter_IsStateless_PositionAlwaysMatchesTheFormula()
        {
            Transform[] strata;
            LocalParallaxZone zone = NewZone(Vector2.zero, Vector2.zero,
                new[] { new Vector2(5f, 0f) }, new[] { new Vector2(0.5f, 1f) }, out strata);
            Transform camera = CameraOf(zone);

            // Walk in, wander, leave, come back: the position is a pure function of the camera
            // (authored + (cam - centre) * factor) — no history is captured on any entry.
            Invoke(zone, "Enter");
            camera.position = new Vector3(10f, 0f, camera.position.z);
            Step(zone);
            Assert.AreEqual(5f + 10f * 0.5f, strata[0].position.x, 0.0001f);

            Collider2D player = PlayerCollider();
            Invoke(zone, "OnTriggerExit2D", player);
            Invoke(zone, "Enter");   // re-enter with the camera still at x=10: same composition
            camera.position = new Vector3(12f, 0f, camera.position.z);
            Step(zone);
            Assert.AreEqual(5f + 12f * 0.5f, strata[0].position.x, 0.0001f,
                "re-entry captures nothing — the formula alone decides the spot");
        }

        // ---- Death fallback & respawn re-probe ----

        [Test]
        public void DeathWhileInside_FreezesStrata()
        {
            Transform[] strata;
            LocalParallaxZone zone = NewZone(Vector2.zero, Vector2.zero,
                new[] { Vector2.zero }, new[] { new Vector2(0.5f, 1f) }, out strata);
            Transform camera = CameraOf(zone);

            Invoke(zone, "Enter");
            camera.position = new Vector3(10f, 0f, camera.position.z);
            Step(zone);
            Assert.AreEqual(5f, strata[0].position.x, 0.0001f);

            // The death teleport skips the physics exit callback; the zone must stop tracking.
            SetField(zone, "inside", true);
            Invoke(zone, "OnPlayerDied", new DeathContext(null, default, default));
            camera.position = new Vector3(100f, 0f, camera.position.z);
            Step(zone);

            Assert.AreEqual(5f, strata[0].position.x, 0.0001f,
                "a death teleport must not drag the strata along the run-back");
            Assert.IsFalse((bool)ReadField(zone, "inside"));
        }

        [Test]
        public void RespawnInsideBox_RestartsTracking()
        {
            Transform[] strata;
            LocalParallaxZone zone = NewZone(Vector2.zero, Vector2.zero,
                new[] { Vector2.zero }, new[] { new Vector2(0.5f, 1f) }, out strata);
            Transform camera = CameraOf(zone);

            // A checkpoint inside the cavern: the teleport overlaps statically, no enter callback.
            Invoke(zone, "OnPlayerRespawned", null, (object)Vector2.zero);
            camera.position = new Vector3(10f, 0f, camera.position.z);
            Step(zone);

            Assert.AreEqual(10f * 0.5f, strata[0].position.x, 0.0001f,
                "respawn inside the box re-enters and tracks from a fresh anchor");
        }

        [Test]
        public void RespawnOutsideBox_StrataStayIdle()
        {
            Transform[] strata;
            LocalParallaxZone zone = NewZone(Vector2.zero, Vector2.zero,
                new[] { new Vector2(3f, 0f) }, new[] { new Vector2(0.5f, 1f) }, out strata);
            Transform camera = CameraOf(zone);

            Invoke(zone, "OnPlayerRespawned", null, (object)new Vector2(100f, 100f));
            camera.position = new Vector3(10f, 0f, camera.position.z);
            Step(zone);

            Assert.IsFalse((bool)ReadField(zone, "inside"));
            Assert.AreEqual(3f, strata[0].position.x, 0.0001f, "respawn outside the box must not start tracking");
        }
    }
}
#endif
