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
    // 相机实现复用自 RTS Camera(作者 lzh / lizhenhuan, MIT 许可), 关键点全部照搬:
    //   1) 朝向: MatrixFrame.Identity -> RotateAboutSide(PI/2) -> RotateAboutForward(bearing) -> RotateAboutSide(elevation)
    //   2) 鼠标读法: MissionScreen.SceneLayer.Input.GetMouseMoveX/Y()(静态 Input 在任务里读不到)
    //   3) 移动: 沿 rotation.s(右) / rotation.u(前) / rotation.f(上) 三轴推相机
    //   4) 应用: CombatCamera.Frame = frame, 并且必须 Mission.SetCameraFrame(ref frame, 1f)
    //   5) 高度不低于地形(否则渲染到地下 -> 黑屏)
    // 玩家角色: 隐身 + 无敌 + 停战(国家意志不亲自下场)。
    internal static class BattleRtsCamera
    {
        internal static bool Active;

        private static bool _inited;
        private static Vec3 _camPos;
        private static float _bearing;
        private static float _elevation;
        private static int _logCount;
        private static System.Reflection.PropertyInfo _bearingProp;
        private static System.Reflection.PropertyInfo _elevProp;

        private const float MouseScale = 5.4E-05f;      // RTS 原值
        private const float ElevMin = -1.36591f;        // RTS 原值
        private const float ElevMax = 1.121997f;        // RTS 原值
        private const float MoveSpeed = 12f;

        internal static void Reset()
        {
            Active = false;
            _inited = false;
        }

        internal static void Activate()
        {
            Active = true;
            _inited = false;
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

        private static void Tick(MissionScreen screen, float realDt)
        {
            if (screen == null) return;
            var mission = screen.Mission;
            if (mission == null) return;

            bool inBattle = false;
            try { inBattle = mission.Mode == MissionMode.Battle; } catch { }
            if (!inBattle) return;

            // F10 切换
            try
            {
                var ic0 = screen.SceneLayer != null ? screen.SceneLayer.Input : null;
                bool pressed = ic0 != null ? ic0.IsKeyPressed(InputKey.F10) : Input.IsKeyPressed(InputKey.F10);
                if (pressed)
                {
                    Active = !Active;
                    _inited = false;
                    DLog.Force(Active ? "RTS相机: 开" : "RTS相机: 关");
                }
            }
            catch { }

            if (!Active) return;
            float dt = realDt;
            if (dt <= 0f || dt > 1f) dt = 0.016f;

            var ic = screen.SceneLayer != null ? screen.SceneLayer.Input : null;

            // ---- 玩家角色: 隐身 + 无敌 + 停战 ----
            var agent = mission.MainAgent;
            if (agent != null)
            {
                try { if (agent.AgentVisuals != null) agent.AgentVisuals.SetVisible(false); } catch { }
                try { if (agent.HealthLimit < 100000f) agent.HealthLimit = 100000f; } catch { }
                try { if (agent.Health < 90000f) agent.Health = 100000f; } catch { }
                try { agent.SetIsAIPaused(true); } catch { }
            }

            // ---- 初始化(照 RTS: 沿用当前相机位置/朝向) ----
            if (!_inited)
            {
                try { _camPos = screen.CombatCamera != null ? screen.CombatCamera.Frame.origin : Vec3.Zero; } catch { }
                try { _bearing = screen.CameraBearing; } catch { }
                try { _elevation = screen.CameraElevation; } catch { }
                try { _camPos = new Vec3(_camPos.x, _camPos.y, _camPos.z + 15f, 1f); } catch { }
                _inited = true;
                DLog.Force("RTS相机: 已接管(角色隐身/无敌/停战)");
            }

            // ---- 鼠标视角(照 RTS: 走任务 InputContext; 光标隐藏时才接管鼠标) ----
            try
            {
                if (ic != null && !screen.MouseVisible)
                {
                    float dx = ic.GetMouseMoveX();
                    float dy = ic.GetMouseMoveY();
                    float scale = MouseScale * 1f * screen.CameraViewAngle;
                    _bearing += -dx * scale;
                    _elevation += (NativeConfig.InvertMouse ? dy : -dy) * scale;
                    if (_elevation < ElevMin) _elevation = ElevMin;
                    if (_elevation > ElevMax) _elevation = ElevMax;
                }
            }
            catch { }

            // ---- 键位移动(照 RTS: 走任务 InputContext) ----
            float ix = 0f, iy = 0f, iz = 0f;
            try
            {
                if (ic != null)
                {
                    if (ic.IsKeyDown(InputKey.W)) iy += 1f;
                    if (ic.IsKeyDown(InputKey.S)) iy -= 1f;
                    if (ic.IsKeyDown(InputKey.D)) ix += 1f;
                    if (ic.IsKeyDown(InputKey.A)) ix -= 1f;
                    if (ic.IsKeyDown(InputKey.E)) iz += 1f;
                    if (ic.IsKeyDown(InputKey.Q)) iz -= 1f;
                }
            }
            catch { }

            float speed = MoveSpeed;
            try { if (ic != null && ic.IsKeyDown(InputKey.LeftShift)) speed *= 4f; } catch { }

            // ---- 构造相机帧(照 RTS 的旋转顺序) ----
            var frame = MatrixFrame.Identity;
            frame.rotation.RotateAboutSide(1.5707964f);
            frame.rotation.RotateAboutForward(_bearing);
            frame.rotation.RotateAboutSide(_elevation);

            // 沿 右(s) / 前(u) / 上(f) 三轴移动(照 RTS)
            if (ix != 0f || iy != 0f || iz != 0f)
            {
                _camPos += (frame.rotation.s * ix + frame.rotation.u * iy + frame.rotation.f * iz) * (speed * dt);
            }
            frame.origin = _camPos;

            // ---- 高度不低于地形(照 RTS) ----
            try
            {
                float gh = mission.Scene.GetGroundHeightAtPosition(frame.origin + new Vec3(0f, 0f, 100f), (BodyFlags)0);
                if (gh < 9999f && frame.origin.z < gh + 0.5f) frame.origin.z = gh + 0.5f;
                if (frame.origin.z > gh + 300f) frame.origin.z = gh + 300f;
            }
            catch { }

            // ---- 应用 ----
            try { if (screen.CombatCamera != null) screen.CombatCamera.Frame = frame; } catch { }
            try { mission.SetCameraFrame(ref frame, 1f); } catch { }
            // bearing/elevation 的 setter 不可访问, 走反射(RTS 同款做法)
            try
            {
                if (_bearingProp == null) _bearingProp = AccessTools.Property(typeof(MissionScreen), "CameraBearing");
                if (_bearingProp != null && _bearingProp.CanWrite) _bearingProp.SetValue(screen, _bearing);
                if (_elevProp == null) _elevProp = AccessTools.Property(typeof(MissionScreen), "CameraElevation");
                if (_elevProp != null && _elevProp.CanWrite) _elevProp.SetValue(screen, _elevation);
            }
            catch { }

            // 诊断(前几次 + 之后每 3 秒)
            _logCount++;
            if (_logCount <= 3 || _logCount % 180 == 0)
            {
                try
                {
                    DLog.Force("RTS诊断#" + _logCount
                        + " 相机=(" + frame.origin.x.ToString("F0") + "," + frame.origin.y.ToString("F0") + "," + frame.origin.z.ToString("F0") + ")"
                        + " 朝向=" + _bearing.ToString("F2") + " 俯仰=" + _elevation.ToString("F2")
                        + " 输入=(" + ix.ToString("F0") + "," + iy.ToString("F0") + "," + iz.ToString("F0") + ")");
                }
                catch { }
            }
        }
    }
}
