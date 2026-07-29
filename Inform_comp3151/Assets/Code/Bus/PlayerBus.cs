using System;
using UnityEngine;
using Inkform.player;

namespace Inkform.Bus
{
    /// <summary>
    /// 玩家状态总线：发布方只有 PlayerHandler，订阅方无需持有任何 PlayerHandler 引用。
    /// 总线负责去重（只在值真的变化时广播），并保存当前快照供订阅者做首次同步。
    /// </summary>
    public static class PlayerBus
    {
        public static event Action<PlayerState> StateChanged;
        public static event Action<FaceDirection> FaceChanged;

        // 当前快照：订阅者可在 OnEnable 里读取以完成首次同步
        public static PlayerState State { get; private set; }
        public static FaceDirection Face { get; private set; }

        public static void RaiseState(PlayerState state)
        {
            if (state == State) return;      // 去重：只在变化时广播
            State = state;
            StateChanged?.Invoke(state);
        }

        public static void RaiseFace(FaceDirection face)
        {
            if (face == Face) return;
            Face = face;
            FaceChanged?.Invoke(face);
        }

        // 静态字段不随场景重载清空；关闭 Domain Reload 时会残留上一次运行的死订阅者
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            StateChanged = null;
            FaceChanged = null;
            // 与 PlayerHandler 的字段默认值保持一致（Idle / R），避免启动时多广播一次
            State = PlayerState.Idle;
            Face = FaceDirection.R;
        }
    }
}
