using Inkform.Tool;
using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>
    /// 网格碎裂：把一个包围盒按 cellsX × cellsY 切成小块，每块沿「爆心 → 块中心」的 8 向弹开。
    /// 可破坏墙和炸弹共用同一套碎裂表现，自己不持有任何状态 ——
    /// 切几块、弹多快、长什么样全写在调用方给的 FragmentCue 里。
    /// </summary>
    public static class Shatter
    {
        /// <summary>
        /// 沿网格生成碎块。bounds 必须由调用方在关掉碰撞体之前取好 ——
        /// Collider2D 一 disabled，物理形状就被移除，bounds 会退化成原点上的零尺寸。
        /// </summary>
        public static void Burst(FragmentCue cue, Bounds bounds, Vector2 center, float force)
        {
            if (cue == null || cue.prefab == null) return;      // 槽位没配，静默跳过
            if (cue.cellsX < 1 || cue.cellsY < 1) return;

            Vector2 cell = new Vector2(bounds.size.x / cue.cellsX, bounds.size.y / cue.cellsY);

            for (int ix = 0; ix < cue.cellsX; ix++)
            {
                for (int iy = 0; iy < cue.cellsY; iy++)
                {
                    // 格中心 = 包围盒左下角 + (格号 + 0.5) × 格尺寸
                    Vector2 pos = new Vector2(
                        bounds.min.x + (ix + 0.5f) * cell.x,
                        bounds.min.y + (iy + 0.5f) * cell.y);

                    SpawnFragment(cue, pos, cell, center, force);
                }
            }
        }

        private static void SpawnFragment(FragmentCue cue, Vector2 pos, Vector2 cell,
                                          Vector2 center, float force)
        {
            GameObject frag = Object.Instantiate(cue.prefab, pos, Quaternion.identity);

            // 外观和缩放交给 Fragment 自己按 Cue 定：只有它知道最终随机取到的是哪张图、原始尺寸多大
            if (frag.TryGetComponent(out Fragment fragment)) fragment.Apply(cue, cell);
            else frag.transform.localScale = cell;      // 预制体上没挂 Fragment 时，退回「格尺寸即缩放」

            if (!frag.TryGetComponent(out Rigidbody2D body)) return;

            // 工程里 m_AutoSyncTransforms = 0，改完 transform 顺手同步刚体，和 Bomb.OnItemReleased 一个理由
            body.position = pos;
            body.linearVelocity = Dir8.Snap(pos - center) * (force * cue.forceMultiplier);
            body.angularVelocity = Random.Range(-cue.spinSpeed, cue.spinSpeed);
        }

        /// <summary>在 Scene 视图里画出切分网格，方便调块数。调用方负责先设好 Gizmos.color。</summary>
        public static void DrawGrid(Bounds bounds, int cellsX, int cellsY)
        {
            if (cellsX < 1 || cellsY < 1) return;

            for (int ix = 1; ix < cellsX; ix++)
            {
                float x = bounds.min.x + bounds.size.x * ix / cellsX;
                Gizmos.DrawLine(new Vector3(x, bounds.min.y), new Vector3(x, bounds.max.y));
            }
            for (int iy = 1; iy < cellsY; iy++)
            {
                float y = bounds.min.y + bounds.size.y * iy / cellsY;
                Gizmos.DrawLine(new Vector3(bounds.min.x, y), new Vector3(bounds.max.x, y));
            }
        }
    }
}
