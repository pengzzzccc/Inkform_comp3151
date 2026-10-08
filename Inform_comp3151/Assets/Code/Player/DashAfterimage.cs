using Inkform.Bus;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// Dash afterimage: while the motor's attack-dash runs, snapshots of the player's current
    /// sprite are dropped along the path and fade out. The matching sound is AudioDirector's
    /// dashTrail slot, keyed off PlayerBus.DashAttempted (same frame the trail starts).
    ///
    /// Same independent-loop stance as DashBreaker: own Update, needs only PlayerMotor.IsDashing.
    /// Ghosts are pooled and parented to the scene root, so they stay where they were dropped
    /// instead of following the player.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class DashAfterimage : MonoBehaviour
    {
        [Tooltip("Seconds between two afterimages while dashing.")]
        [SerializeField] private float spawnInterval = 0.03f;
        [Tooltip("Seconds an afterimage takes to fade from tint alpha to zero.")]
        [SerializeField] private float fadeTime = 0.2f;
        [Tooltip("Afterimage colour; its alpha is the starting opacity.")]
        [SerializeField] private Color tint = new Color(0.6f, 0.85f, 1f, 0.6f);
        [SerializeField] private int poolSize = 12;

        private PlayerMotor motor;
        private SpriteRenderer body;
        private SpriteRenderer[] ghosts;
        private float[] spawnTimes;
        private Transform poolRoot;
        private bool wasDashing;
        private float nextSpawnTime;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            body = GetComponent<SpriteRenderer>();

            poolRoot = new GameObject("DashAfterimages").transform;
            ghosts = new SpriteRenderer[Mathf.Max(1, poolSize)];
            spawnTimes = new float[ghosts.Length];
            for (int i = 0; i < ghosts.Length; i++)
            {
                var go = new GameObject("Afterimage");
                go.transform.SetParent(poolRoot, false);
                go.SetActive(false);
                ghosts[i] = go.AddComponent<SpriteRenderer>();
            }
        }

        void OnDisable() => ClearAll();

        void OnDestroy()
        {
            if (poolRoot != null) Destroy(poolRoot.gameObject);
        }

        void Update()
        {
            if (LifeBus.IsDead)
            {
                ClearAll();
                wasDashing = false;
                return;
            }

            bool dashing = motor.IsDashing;
            if (dashing && (!wasDashing || Time.time >= nextSpawnTime))
            {
                Spawn();
                nextSpawnTime = Time.time + spawnInterval;
            }
            wasDashing = dashing;

            Fade();
        }

        private void Spawn()
        {
            if (body.sprite == null) return;

            // Free slot first; when every ghost is live, recycle the oldest
            int slot = 0;
            for (int i = 0; i < ghosts.Length; i++)
            {
                if (!ghosts[i].gameObject.activeSelf) { slot = i; break; }
                if (spawnTimes[i] < spawnTimes[slot]) slot = i;
            }

            SpriteRenderer ghost = ghosts[slot];
            Transform tf = ghost.transform;
            tf.SetPositionAndRotation(body.transform.position, body.transform.rotation);
            tf.localScale = body.transform.lossyScale;

            ghost.sprite = body.sprite;
            ghost.flipX = body.flipX;
            ghost.flipY = body.flipY;
            ghost.sharedMaterial = body.sharedMaterial;
            ghost.sortingLayerID = body.sortingLayerID;
            ghost.sortingOrder = body.sortingOrder - 1;   // behind the live player
            ghost.color = tint;

            spawnTimes[slot] = Time.time;
            ghost.gameObject.SetActive(true);
        }

        private void Fade()
        {
            for (int i = 0; i < ghosts.Length; i++)
            {
                SpriteRenderer ghost = ghosts[i];
                if (!ghost.gameObject.activeSelf) continue;

                float t = fadeTime > 0f ? (Time.time - spawnTimes[i]) / fadeTime : 1f;
                if (t >= 1f)
                {
                    ghost.gameObject.SetActive(false);
                    continue;
                }

                Color c = tint;
                c.a = tint.a * (1f - t);
                ghost.color = c;
            }
        }

        private void ClearAll()
        {
            if (ghosts == null) return;
            foreach (SpriteRenderer ghost in ghosts)
                if (ghost != null) ghost.gameObject.SetActive(false);
        }
    }
}
