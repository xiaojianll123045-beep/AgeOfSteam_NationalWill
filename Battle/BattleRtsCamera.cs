using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;

namespace FeudalInternalAffairs
{
    // 战场指挥官处理 + RTS 上帝视角相机
    //
    // 相机实现复用/参考自 RTS Camera(作者 lzh / lizhenhuan, MIT 许可)，核心三点:
    //   1) 每帧直接设置 MissionScreen.CombatCamera.Frame;
    //   2) 必须再调用 Mission.SetCameraFrame(ref frame, 1f) 告诉引擎, 否则渲染用的还是旧帧(会黑屏);
    //   3) 相机高度不能低于地形, 否则会渲染到地下(也会黑屏)。
    // 玩家角色: 隐身 + 无敌 + 停战(国家意志不亲自下场)。
    internal static class BattleRtsCamera
    {
        internal static bool Active;

        private static bool _inited;
        private static bool _applied;
        private static Vec3 _pos;
        private static float _yaw;
        private static float _pitch = -0.85f;
        private static float _dist = 45f;
        private static int _logCount;

        internal static void Reset()
        {
            Active = false;
            _inited = false;
            _applied = false;
        }

        // 进入"亲自指挥"的战斗时自动开启
        internal static void Activate()
        {
            Active = true;
            _inited = false;
            _applied = false;
        }

        [HarmonyPatch(typeof(MissionScreen), "CameraTick")]
        internal static class CameraTickPatch
        {
            private static void Postfix(MissionScreen __instance, float realDt)
            {
                try { Tick(__instance, realDt); } catch { }
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

        private static void Tick(MissionScreen screen, float dt)
        {
            if (screen == null) return;
            var mission = screen.Mission;
            if (mission == null) return;

            bool inBattle = false;
            try { inBattle = mission.Mode == MissionMode.Battle; } catch { }
            if (!inBattle) return;

            // F10 随时切换(战场内)
            try
            {
                if (Input.IsKeyPressed(InputKey.F10))
                {
                    Active = !Active;
                    DLog.Force(Active ? "RTS相机: 开" : "RTS相机: 关");
                }
            }
            catch { }

            if (!Active) return;
            if (dt <= 0f || dt > 1f) dt = 0.016f;

            // ---- 玩家角色: 隐身 + 无敌 + 停战 ----
            var agent = mission.MainAgent;
            if (agent != null)
            {
                try { if (agent.AgentVisuals != null) agent.AgentVisuals.SetVisible(false); } catch { }
                try { if (agent.HealthLimit < 100000f) agent.HealthLimit = 100000f; } catch { }
                try { if (agent.Health < 90000f) agent.Health = 100000f; } catch { }
                try { agent.SetIsAIPaused(true); } catch { }
            }

            if (!_inited)
            {
                try { _pos = agent != null ? agent.Position : Vec3.Zero; } catch { }
                try { _yaw = agent != null ? agent.LookDirectionAsAngle : 0f; } catch { }
                _inited = true;
                DLog.Force("RTS相机: 已接管(角色隐身/无敌/停战)");
            }

            // ---- 输入 ----
            float mx = 0f, my = 0f;
            try
            {
                if (Input.IsKeyDown(InputKey.W) || Input.IsKeyDown(InputKey.Up)) my += 1f;
                if (Input.IsKeyDown(InputKey.S) || Input.IsKeyDown(InputKey.Down)) my -= 1f;
                if (Input.IsKeyDown(InputKey.D) || Input.IsKeyDown(InputKey.Right)) mx += 1f;
                if (Input.IsKeyDown(InputKey.A) || Input.IsKeyDown(InputKey.Left)) mx -= 1f;

                // 右键拖动 = 旋转
                if (Input.IsKeyDown(InputKey.RightMouseButton))
                {
                    _yaw -= Input.MouseMoveX * 0.004f;
                    _pitch += Input.MouseMoveY * 0.004f;
                    if (_pitch < -1.45f) _pitch = -1.45f;
                    if (_pitch > -0.12f) _pitch = -0.12f;
                }

                // Q/E = 升降
                if (Input.IsKeyDown(InputKey.Q)) _dist = Math.Max(8f, _dist - 60f * dt);
                if (Input.IsKeyDown(InputKey.E)) _dist = Math.Min(300f, _dist + 60f * dt);
            }
            catch { }

            float speed = _dist * 1.2f + 8f;
            try { if (Input.IsKeyDown(InputKey.LeftShift)) speed *= 2.5f; } catch { }

            var look = new Vec3(
                (float)(Math.Cos(_pitch) * Math.Sin(_yaw)),
                (float)(Math.Cos(_pitch) * Math.Cos(_yaw)),
                (float)Math.Sin(_pitch));
            if (look.Length < 0.0001f) look = new Vec3(0f, 0f, -1f);

            var fwd = new Vec3((float)Math.Sin(_yaw), (float)Math.Cos(_yaw), 0f);
            var right = new Vec3(fwd.y, -fwd.x, 0f);
            _pos += (fwd * my + right * mx) * (speed * dt);

            var frame = MatrixFrame.Identity;
            frame.origin = _pos - look * _dist;
            frame.rotation.u = -look;
            var s = Vec3.CrossProduct(look, new Vec3(0f, 0f, 1f));
            if (s.Length < 0.0001f) s = new Vec3(1f, 0f, 0f);
            frame.rotation.s = s.NormalizedCopy();
            frame.rotation.f = Vec3.CrossProduct(frame.rotation.u, frame.rotation.s).NormalizedCopy();

            // ---- 不能低于地形(RTS 同款限制, 否则渲染到地下 -> 黑屏) ----
            try
            {
                float gh = mission.Scene.GetGroundHeightAtPosition(frame.origin + new Vec3(0f, 0f, 100f), (BodyFlags)0);
                if (gh < 9999f && frame.origin.z < gh + 0.5f) frame.origin.z = gh + 0.5f;
            }
            catch { }

            // ---- 应用: 相机 + Mission(关键) ----
            try { screen.UpdateFreeCamera(frame); } catch { }
            try { mission.SetCameraFrame(ref frame, 1f); } catch { }

            // 诊断(前几次 + 之后每 3 秒一条)
            _logCount++;
            if (_logCount <= 3 || _logCount % 180 == 0)
            {
                try
                {
                    var cam = screen.CombatCamera;
                    var f2 = cam != null ? cam.Frame : MatrixFrame.Identity;
                    DLog.Force("RTS诊断#" + _logCount
                        + " 期望=(" + frame.origin.x.ToString("F0") + "," + frame.origin.y.ToString("F0") + "," + frame.origin.z.ToString("F0") + ")"
                        + " 实际=(" + f2.origin.x.ToString("F0") + "," + f2.origin.y.ToString("F0") + "," + f2.origin.z.ToString("F0") + ")");
                }
                catch { }
            }
        }
    }
}
