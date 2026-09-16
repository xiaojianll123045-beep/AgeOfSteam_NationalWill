using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
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
        private static bool _agentPrepared;
        private static Vec3 _camPos;
        private static float _bearing;
        private static float _elevation;
        private static int _logCount;
        private static System.Reflection.PropertyInfo _bearingProp;
        private static System.Reflection.PropertyInfo _elevProp;
        private static System.Reflection.PropertyInfo _isPlayerAgentAddedProp;

        private const float MouseScale = 5.4E-05f;      // RTS 原值
        private const float ElevMin = -1.36591f;        // RTS 原值
        private const float ElevMax = 1.121997f;        // RTS 原值
        private const float MoveSpeed = 12f;

        private static bool? _rtsModPresent;

        // RTS Camera 是否在场: 现在他们的源码已并入我们的程序集(ThirdParty/RTSCamera),
        // 所以这里主要靠"类型是否存在"判断; 另外也兼容外部单独安装 RTS Camera 的情况。
        internal static bool RtsModPresent
        {
            get
            {
                if (_rtsModPresent == null)
                {
                    bool found = false;
                    // 1) 源码合并进来的(编译在我们的程序集里)
                    try { found = AccessTools.TypeByName("RTSCamera.Logic.RTSCameraLogic") != null; } catch { }
                    // 2) 外部单独安装的 RTS Camera
                    if (!found)
                    {
                        try
                        {
                            var basePath = TaleWorlds.Library.BasePath.Name;
                            if (!string.IsNullOrEmpty(basePath))
                            {
                                found = System.IO.File.Exists(System.IO.Path.Combine(basePath, "Modules", "RTSCamera", "SubModule.xml"))
                                     || System.IO.File.Exists(System.IO.Path.Combine(basePath, "Modules", "RTSCamera.CommandSystem", "SubModule.xml"));
                            }
                        }
                        catch { }
                    }
                    if (!found)
                    {
                        try
                        {
                            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                            {
                                var n = a.GetName().Name;
                                if (n == "RTSCamera" || n == "RTSCamera.CommandSystem") { found = true; break; }
                            }
                        }
                        catch { }
                    }
                    _rtsModPresent = found;
                    if (found) DLog.Force("RTS Camera 在场: 我们的战场相机/角色处理让位(不接管)");
                }
                return _rtsModPresent.Value;
            }
        }

        internal static void Reset()
        {
            Active = false;
            _inited = false;
            _agentPrepared = false;
            _parkSpot = Vec3.Zero;
            _parkLogged = false;
        }

        internal static void Activate()
        {
            Active = true;
            _inited = false;
            _agentPrepared = false;
            _askedRtsMod = false;
            _rtsWait = 0f;
            _askedCameraGoTo = false;
            _goToWait = 0f;
            _parkDone = false;
        }

        // 请求 RTS Camera 切到自由相机(反射, 只在它存在时用)。
        // 他们那套: RTSCamera.Logic.RTSCameraLogic.Instance.SwitchFreeCameraLogic.SwitchCamera(false)
        private static bool _askedRtsMod;
        private static float _rtsWait;

        private static bool TryAskRtsModFreeCamera()
        {
            try
            {
                var logicType = AccessTools.TypeByName("RTSCamera.Logic.RTSCameraLogic");
                if (logicType == null) return false;
                var inst = AccessTools.Field(logicType, "Instance")?.GetValue(null);
                if (inst == null) return false;
                var sw = AccessTools.Field(logicType, "SwitchFreeCameraLogic")?.GetValue(inst);
                if (sw == null) return false;

                var swType = sw.GetType();
                // 已经在自由相机里就别再切(他们那个是开关, 会切回去)
                try
                {
                    var p = AccessTools.Property(swType, "IsSpectatorCamera");
                    if (p != null && (bool)p.GetValue(sw)) { DLog.Force("RTS桥: 已经是自由相机"); return true; }
                }
                catch { }

                var mi = AccessTools.Method(swType, "SwitchCamera", new[] { typeof(bool) });
                if (mi == null) { DLog.Force("RTS桥: 找不到 SwitchCamera"); return false; }
                mi.Invoke(sw, new object[] { false });
                DLog.Force("RTS桥: 已请求 RTS Camera 切到自由相机");
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("RTS桥异常: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
                return false;
            }
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

        // ---- 玩家角色: 隐身 + 无敌 + 拉到地图边缘 ----
        private static Vec3 _parkSpot;
        private static bool _parkLogged;
        private static bool _parkDone;   // 玩家是否已经到边缘(相机传送要等它先完成)

        // 战场边缘: 取"软边界"离战场中心最远的顶点, 再往里收 10 米(不越界)
        private static Vec3 FindBattleEdge(Mission mission)
        {
            try
            {
                var scene = mission.Scene;
                if (scene != null)
                {
                    int n = scene.GetSoftBoundaryVertexCount();
                    if (n > 0)
                    {
                        Vec2 best = Vec2.Zero;
                        float bestD = -1f;
                        for (int i = 0; i < n; i++)
                        {
                            var v = scene.GetSoftBoundaryVertex(i);
                            float d = v.X * v.X + v.Y * v.Y;
                            if (d > bestD) { bestD = d; best = v; }
                        }
                        if (bestD > 1f)
                        {
                            float len = (float)Math.Sqrt(bestD);
                            float k = Math.Max(0f, len - 10f) / len;
                            float x = best.X * k;
                            float y = best.Y * k;
                            float z = 20f;
                            try
                            {
                                float gh = scene.GetGroundHeightAtPosition(new Vec3(x, y, 200f, 1f), (BodyFlags)0);
                                if (gh > 0.05f && gh < 9998f) z = gh;
                            }
                            catch { }
                            return new Vec3(x, y, z + 2f, 1f);
                        }
                    }
                }
            }
            catch { }
            try
            {
                Vec3 min, max;
                mission.Scene.GetBoundingBox(out min, out max);
                return new Vec3(min.x + 30f, min.y + 30f, max.z, 1f);
            }
            catch { }
            return Vec3.Zero;
        }

        // 玩家角色与坐骑: 隐身 + 无敌 + 停在战场边缘(原版/他们的 mod 可能把角色挪回去, 所以每帧维持)
        private static void ParkPlayerAgent(Mission mission, Agent agent)
        {
            if (agent == null) return;
            try { if (agent.AgentVisuals != null) agent.AgentVisuals.SetVisible(false); } catch { }
            try
            {
                var mount = agent.MountAgent;
                if (mount != null && mount.AgentVisuals != null) mount.AgentVisuals.SetVisible(false);
            }
            catch { }
            try { if (agent.HealthLimit < 100000f) agent.HealthLimit = 100000f; } catch { }
            try { if (agent.Health < 90000f) agent.Health = 100000f; } catch { }

            try
            {
                if (_parkSpot == Vec3.Zero) _parkSpot = FindBattleEdge(mission);
                if (_parkSpot != Vec3.Zero)
                {
                    var p = agent.Position;
                    float dx = p.x - _parkSpot.x;
                    float dy = p.y - _parkSpot.y;
                    if (dx * dx + dy * dy > 25f)   // 离边缘超过 5 米就拉过去
                    {
                        try { agent.TeleportToPosition(_parkSpot); } catch { }
                        try
                        {
                            var mount = agent.MountAgent;
                            if (mount != null) mount.TeleportToPosition(_parkSpot);
                        }
                        catch { }
                        if (!_parkLogged)
                        {
                            _parkLogged = true;
                            DLog.Force("战场: 玩家与坐骑已隐身并移到地图边缘 ("
                                + ((int)_parkSpot.x) + "," + ((int)_parkSpot.y) + ")");
                        }
                    }
                    else
                    {
                        _parkDone = true;   // 已经到边缘
                    }
                }
                else
                {
                    _parkDone = true;   // 找不到边缘就别卡着相机
                }
            }
            catch { }
        }

        // 我方"大军"中心: 最大编制(人数最多)的平均位置, 再抬高 45 米
        private static Vec3 GetArmyCenterAbove(Mission mission)
        {
            try
            {
                var team = mission.PlayerTeam;
                if (team != null)
                {
                    int bestCount = 0;
                    Vec2 best = Vec2.Zero;
                    foreach (var f in team.FormationsIncludingSpecialAndEmpty)
                    {
                        if (f == null) continue;
                        int c = f.CountOfUnits;
                        if (c > bestCount) { bestCount = c; best = f.CachedAveragePosition; }
                    }
                    if (bestCount > 0)
                    {
                        float z = 30f;
                        try
                        {
                            float gh = mission.Scene.GetGroundHeightAtPosition(new Vec3(best.X, best.Y, 200f, 1f), (BodyFlags)0);
                            if (gh > 0.05f && gh < 9998f) z = gh;
                        }
                        catch { }
                        return new Vec3(best.X, best.Y, z + 45f, 1f);
                    }
                }
            }
            catch { }
            return Vec3.Zero;
        }

        // 请求 RTS Camera 把相机飞到大军上方(用他们自己的 RequestCameraGoTo)
        private static bool _askedCameraGoTo;
        private static float _goToWait;

        private static bool TryAskRtsModCameraGoTo(Mission mission)
        {
            try
            {
                var pos = GetArmyCenterAbove(mission);
                if (pos == Vec3.Zero) return false;   // 编制还没就绪, 下帧再试

                var mgrType = AccessTools.TypeByName("MissionLibrary.Controller.Camera.ACameraControllerManager");
                if (mgrType == null) return false;
                var mgr = AccessTools.Method(mgrType, "Get")?.Invoke(null, null);
                if (mgr == null) return false;
                var inst = AccessTools.Property(mgrType, "Instance")?.GetValue(mgr);
                if (inst == null) return false;

                var mi = AccessTools.Method(inst.GetType(), "RequestCameraGoTo", new[] { typeof(Vec3), typeof(Vec3) });
                if (mi == null) mi = AccessTools.Method(inst.GetType(), "RequestCameraGoTo", new[] { typeof(Vec3) });
                if (mi == null) { DLog.Force("RTS桥: 找不到 RequestCameraGoTo"); return false; }

                if (mi.GetParameters().Length == 2)
                    mi.Invoke(inst, new object[] { pos, Vec3.Zero });
                else
                    mi.Invoke(inst, new object[] { pos });
                DLog.Force("RTS桥: 相机已传送到大军上方 (" + ((int)pos.x) + "," + ((int)pos.y) + "," + ((int)pos.z) + ")");
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("RTS桥(相机传送)异常: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
                return false;
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

            // RTS 在场: 相机交给他们, 但玩家角色/坐骑的隐身 + 挪到地图边缘仍由我们做
            // (挪到边缘后, 敌方 AI 的索敌范围够不到, 就不会来锁玩家)。
            if (RtsModPresent)
            {
                try { ParkPlayerAgent(mission, mission.MainAgent); } catch { }
                // 顺序要求: 玩家先到边缘, 再切相机/移动相机(否则相机会先跟着玩家飞过去)
                if (!_parkDone)
                {
                    _goToWait += realDt;
                    if (_goToWait < 3f) return;   // 最多等 3 秒, 免得卡死
                }
                if (Active && !_askedRtsMod)
                {
                    if (TryAskRtsModFreeCamera()) _askedRtsMod = true;
                }
                // 玩家到边缘后再把相机传送到大军上方(编制就绪前每帧重试, 最多 5 秒)
                if (Active && !_askedCameraGoTo)
                {
                    if (TryAskRtsModCameraGoTo(mission)) _askedCameraGoTo = true;
                    else if (_goToWait > 5f) _askedCameraGoTo = true;
                }
                return;
            }

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

            // ---- 玩家角色: 完全照搬 RTS 的做法 ----
            // 关键: 不要把角色设成 AI(那样原版会拒绝"下达命令"), 而是保持 Player 控制,
            // 但禁用 MissionMainAgentController(玩家输入控制器) -> 角色不响应 WASD/WASD 归相机。
            var agent = mission.MainAgent;
            if (agent != null)
            {
                try
                {
                    var ctrl = mission.GetMissionBehavior<MissionMainAgentController>();
                    if (ctrl != null)
                    {
                        ctrl.CustomLookDir = Vec3.Zero;
                        ctrl.Disable();
                        try { ctrl.InteractionComponent?.ClearFocus(); } catch { }
                    }
                }
                catch { }

                if (!_agentPrepared)
                {
                    try { agent.SetCanLeadFormationsRemotely(true); } catch { }       // 允许远程指挥
                    try { if (mission.PlayerTeam != null) mission.PlayerTeam.GeneralAgent = agent; } catch { }
                    try
                    {
                        if (_isPlayerAgentAddedProp == null)
                            _isPlayerAgentAddedProp = AccessTools.Property(typeof(MissionScreen), "IsPlayerAgentAdded");
                        if (_isPlayerAgentAddedProp != null && _isPlayerAgentAddedProp.CanWrite)
                            _isPlayerAgentAddedProp.SetValue(screen, true);
                    }
                    catch { }
                    _agentPrepared = true;
                    DLog.Force("RTS相机: 已接管(禁用玩家控制器/允许远程指挥/角色隐身)");
                }

                try { if (agent.AgentVisuals != null) agent.AgentVisuals.SetVisible(false); } catch { }
                // 坐骑一起隐身(否则"人没了马还在跑")
                try
                {
                    var mount = agent.MountAgent;
                    if (mount != null && mount.AgentVisuals != null) mount.AgentVisuals.SetVisible(false);
                }
                catch { }
                try { if (agent.HealthLimit < 100000f) agent.HealthLimit = 100000f; } catch { }
                try { if (agent.Health < 90000f) agent.Health = 100000f; } catch { }
                // 隐身 + 挪到地图边缘(敌方 AI 够不到就不会锁玩家)
                try { ParkPlayerAgent(mission, agent); } catch { }
            }

            // ---- 初始化: 俯视的上帝视角(在角色上方 40 米) ----
            if (!_inited)
            {
                Vec3 p = Vec3.Zero;
                try { p = agent != null ? agent.Position : (screen.CombatCamera != null ? screen.CombatCamera.Frame.origin : Vec3.Zero); } catch { }
                _camPos = new Vec3(p.x, p.y, p.z + 40f, 1f);
                _bearing = 0f;
                _elevation = -0.55f;    // 俯角
                _inited = true;
                DLog.Force("RTS相机: 视角已初始化(俯视)");
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

            // ---- 键位移动: 用原版自己的移动轴 MovementAxisX/Y(= 玩家绑定的 WASD) ----
            float ix = 0f, iy = 0f, iz = 0f;
            try
            {
                if (ic != null)
                {
                    ix = ic.GetGameKeyAxis("MovementAxisX");
                    iy = ic.GetGameKeyAxis("MovementAxisY");
                    float scroll = ic.GetDeltaMouseScroll();
                    if (scroll > 0f) iz = 1f;
                    else if (scroll < 0f) iz = -1f;
                }
            }
            catch { }

            float speed = MoveSpeed;
            try { if (ic != null && ic.GetGameKeyAxis("MovementAxisY") != 0f) speed = MoveSpeed; } catch { }

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
