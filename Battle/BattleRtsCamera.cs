using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;

namespace FeudalInternalAffairs
{
    // 战场上的"指挥官处理"(阶段2, 精简版)
    //
    // 相机交给 RTS Camera mod(MIT, 作者 lzh/lizhenhuan): 进战场后按 F10 就是上帝视角,
    // 或在其菜单里勾选 "Use Free Camera By Default" 自动开启。
    // 我们自己做的那套自由相机已被证明会黑屏(每帧与原版抢相机), 这里不再接管相机,
    // 只负责"国家意志不亲自下场": 角色隐身 + 无敌 + 停战。
    internal static class BattleRtsCamera
    {
        internal static bool Active;

        private static bool _applied;

        internal static void Reset()
        {
            Active = false;
            _applied = false;
        }

        internal static void Activate()
        {
            Active = true;
            _applied = false;
        }

        [HarmonyPatch(typeof(MissionScreen), "CameraTick")]
        internal static class CameraTickPatch
        {
            private static void Postfix(MissionScreen __instance)
            {
                try { Tick(__instance); } catch { }
            }
        }

        [HarmonyPatch(typeof(MissionScreen), "OnFinalize")]
        internal static class FinalizePatch
        {
            private static void Postfix()
            {
                try { Reset(); } catch { }
            }
        }

        private static void Tick(MissionScreen screen)
        {
            if (!Active || screen == null) return;
            var mission = screen.Mission;
            if (mission == null) return;
            try { if (mission.Mode != MissionMode.Battle) return; } catch { }

            var agent = mission.MainAgent;
            if (agent == null) return;

            // 玩家角色: 隐身 + 无敌 + 停住不参战(国家意志不亲自下场)
            try { if (agent.AgentVisuals != null) agent.AgentVisuals.SetVisible(false); } catch { }
            try { if (agent.HealthLimit < 100000f) agent.HealthLimit = 100000f; } catch { }
            try { if (agent.Health < 90000f) agent.Health = 100000f; } catch { }
            try { agent.SetIsAIPaused(true); } catch { }

            if (!_applied)
            {
                _applied = true;
                DLog.Force("战场: 玩家角色已隐身/无敌/停战(相机交给 RTS Camera mod, 按 F10)");
            }
        }
    }
}
