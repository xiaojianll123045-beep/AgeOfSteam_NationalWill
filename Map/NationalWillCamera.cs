using System;
using System.Reflection;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 视角工具: 读取/设置相机目标(含私有字段 _cameraTarget, 改它=不产生滑动)
    internal static class NationalWillCamera
    {
        private static PropertyInfo _instanceProp;
        private static PropertyInfo _distanceProp;
        private static PropertyInfo _targetDistanceProp;
        private static PropertyInfo _idealTargetProp;
        private static PropertyInfo _processInputProp;
        private static FieldInfo _cameraTargetField;
        private static FieldInfo _lastUsedTargetField;
        private static bool _searched;
        private static bool _done;

        // 只做一次: 成功之前可以重试(地图还没就绪时), 成功之后永不再动镜头
        internal static void CenterOnKingdomOnce(Kingdom kingdom)
        {
            if (_done) return;
            if (CenterOnKingdom(kingdom)) _done = true;
        }

        // v4.65: 新战役重置(修复"开新档视角没到国家中间": _done 是静态字段, 上个战役置位后新档不再居中)
        internal static void ResetForNewCampaign() { _done = false; }

        // 读档有存档视角时禁止居中(避免覆盖已经恢复的视角)
        internal static void MarkDone() { _done = true; }

        // 返回 true 表示处理完成(或无需处理), false 表示还没准备好(国家未解析/地图未就绪), 下次再试
        internal static bool CenterOnKingdom(Kingdom kingdom)
        {
            if (kingdom == null) return false;   // 国家还没解析出来(setup 尚未跑), 不要标记完成
            var view = GetCameraView();
            if (view == null) return false;

            try
            {
                CampaignVec2 center;
                var settlements = kingdom.Settlements;
                if (settlements != null && settlements.Count > 0)
                {
                    Vec2 sum = Vec2.Zero;
                    int n = 0;
                    foreach (var s in settlements)
                    {
                        if (s == null) continue;
                        sum += new Vec2(s.Position.X, s.Position.Y);
                        n++;
                    }
                    center = new CampaignVec2(n > 0 ? sum / n : Vec2.Zero, true);
                }
                else if (kingdom.InitialHomeSettlement != null)
                {
                    center = kingdom.InitialHomeSettlement.Position;
                }
                else
                {
                    return true;
                }

                var target = center.AsVec3() + Vec3.Up;
                SetIdealTarget(view, target);
                SetCameraTarget(view, target);        // 当前位置也一起改 -> 瞬移, 不播放动画
                SetDistance(view, _distanceProp, 150f);
                SetDistance(view, _targetDistanceProp, 150f);
                DLog.Force("默认视角已瞬移到国家版图中心");
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("设置默认视角失败: " + ex.Message);
                return true;
            }
        }

        // 相机平滑飘到某个定居点(只改理想目标点, 让相机自己滑过去, 不瞬移)
        internal static bool FlyTo(TaleWorlds.CampaignSystem.Settlements.Settlement s)
        {
            try
            {
                if (s == null) return false;
                var view = GetCameraView();
                if (view == null) return false;
                var target = s.Position.AsVec3() + Vec3.Up;
                SetIdealTarget(view, target);
                try { if (_targetDistanceProp == null) _targetDistanceProp = AccessTools.Property(typeof(MapCameraView), "TargetCameraDistance"); } catch { }
                if (_targetDistanceProp != null) SetDistance(view, _targetDistanceProp, 90f);
                DLog.Info("相机飘向 " + (s.Name != null ? s.Name.ToString() : s.StringId));
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("相机飘移失败: " + ex.Message);
                return false;
            }
        }

        // v4.100: 相机飘向任意坐标(军队总览"定位")
        internal static bool FlyTo(CampaignVec2 pos)
        {
            try
            {
                var view = GetCameraView();
                if (view == null) return false;
                var target = pos.AsVec3() + Vec3.Up;
                SetIdealTarget(view, target);
                try { if (_targetDistanceProp == null) _targetDistanceProp = AccessTools.Property(typeof(MapCameraView), "TargetCameraDistance"); } catch { }
                if (_targetDistanceProp != null) SetDistance(view, _targetDistanceProp, 60f);
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("相机飘移失败: " + ex.Message);
                return false;
            }
        }

        internal static bool TryGetIdealTarget(MapCameraView view, out Vec3 target)        {
            target = Vec3.Zero;
            try
            {
                if (_idealTargetProp == null) _idealTargetProp = AccessTools.Property(typeof(MapCameraView), "IdealCameraTarget");
                if (_idealTargetProp == null) return false;
                var value = _idealTargetProp.GetValue(view);
                if (value is Vec3 v) { target = v; return true; }
            }
            catch { }
            return false;
        }

        internal static Vec3 GetIdealTarget(MapCameraView view)
        {
            Vec3 t;
            return TryGetIdealTarget(view, out t) ? t : Vec3.Zero;
        }

        internal static void SetIdealTarget(MapCameraView view, Vec3 target)
        {
            try
            {
                if (_idealTargetProp == null) _idealTargetProp = AccessTools.Property(typeof(MapCameraView), "IdealCameraTarget");
                if (_idealTargetProp != null && _idealTargetProp.CanWrite) _idealTargetProp.SetValue(view, target);
                SetLastUsedTarget(view, target);
            }
            catch { }
        }

        // 关键: GetMapCameraInput 里有个"把相机目标拉回 _lastUsedIdealCameraTarget"的逻辑,
        // 开局时它记的是玩家位置 -> 不改它的话, 我们设的目标下一帧就被拉回去(这就是"缓缓回玩家中心")。
        internal static void SetLastUsedTarget(MapCameraView view, Vec3 target)
        {
            try
            {
                if (_lastUsedTargetField == null) _lastUsedTargetField = AccessTools.Field(typeof(MapCameraView), "_lastUsedIdealCameraTarget");
                if (_lastUsedTargetField != null)
                {
                    _lastUsedTargetField.SetValue(view, new CampaignVec2(new Vec2(target.x, target.y), true));
                }
            }
            catch { }
        }

        // _cameraTarget 是私有字段, 改它 = 相机当前所在位置直接跳过去(不产生滑动)
        internal static void SetCameraTarget(MapCameraView view, Vec3 target)
        {
            try
            {
                if (_cameraTargetField == null) _cameraTargetField = AccessTools.Field(typeof(MapCameraView), "_cameraTarget");
                if (_cameraTargetField != null) _cameraTargetField.SetValue(view, target);
            }
            catch { }
        }

        // v4.80: 读回 _cameraTarget, 用于诊断"设置了目标但画面不动"
        internal static Vec3 GetCameraTargetValue(MapCameraView view)
        {
            try
            {
                if (_cameraTargetField == null) _cameraTargetField = AccessTools.Field(typeof(MapCameraView), "_cameraTarget");
                if (_cameraTargetField != null)
                {
                    var v = _cameraTargetField.GetValue(view);
                    if (v is Vec3 t) return t;
                }
            }
            catch { }
            return Vec3.Zero;
        }

        // ProcessCameraInput 的 setter 不可访问, 走反射(拖框选时临时关掉相机输入)
        internal static void SetProcessCameraInput(MapCameraView view, bool enabled)
        {
            try
            {
                if (_processInputProp == null) _processInputProp = AccessTools.Property(typeof(MapCameraView), "ProcessCameraInput");
                if (_processInputProp != null && _processInputProp.CanWrite) _processInputProp.SetValue(view, enabled);
            }
            catch { }
        }

        // 旋转视角: CameraBearing 是 protected, 走反射加角度
        private static PropertyInfo _bearingProp;

        internal static float GetBearing(MapCameraView view)
        {
            try
            {
                if (view == null) return 0f;
                if (_bearingProp == null) _bearingProp = AccessTools.Property(typeof(MapCameraView), "CameraBearing");
                if (_bearingProp == null || !_bearingProp.CanRead) return 0f;
                return (float)_bearingProp.GetValue(view);
            }
            catch { return 0f; }
        }

        internal static void AddBearing(MapCameraView view, float delta)
        {
            try
            {
                if (view == null) return;
                if (_bearingProp == null) _bearingProp = AccessTools.Property(typeof(MapCameraView), "CameraBearing");
                if (_bearingProp == null || !_bearingProp.CanRead || !_bearingProp.CanWrite) return;
                float cur = (float)_bearingProp.GetValue(view);
                _bearingProp.SetValue(view, cur + delta);
            }
            catch { }
        }

        // 存档/读档用: 直接设置朝向与距离
        internal static void SetBearing(MapCameraView view, float value)
        {
            try
            {
                if (view == null) return;
                if (_bearingProp == null) _bearingProp = AccessTools.Property(typeof(MapCameraView), "CameraBearing");
                if (_bearingProp != null && _bearingProp.CanWrite) _bearingProp.SetValue(view, value);
            }
            catch { }
        }

        internal static float GetCameraDistance(MapCameraView view)
        {
            try { return view != null ? view.CameraDistance : 0f; }
            catch { return 0f; }
        }

        internal static void SetCameraDistance(MapCameraView view, float value)
        {
            try
            {
                if (view == null || value <= 0f) return;
                if (_distanceProp == null) _distanceProp = AccessTools.Property(typeof(MapCameraView), "CameraDistance");
                SetDistance(view, _distanceProp, value);
                if (_targetDistanceProp == null) _targetDistanceProp = AccessTools.Property(typeof(MapCameraView), "TargetCameraDistance");
                SetDistance(view, _targetDistanceProp, value);
            }
            catch { }
        }

        private static void SetDistance(MapCameraView view, PropertyInfo prop, float value)
        {
            try { if (prop != null && prop.CanWrite) prop.SetValue(view, value); }
            catch { }
        }

        internal static MapCameraView View
        {
            get { return GetCameraView(); }
        }

        private static MapCameraView GetCameraView()
        {
            try
            {
                if (!_searched)
                {
                    _searched = true;
                    _instanceProp = AccessTools.Property(typeof(MapCameraView), "Instance");
                    _distanceProp = AccessTools.Property(typeof(MapCameraView), "CameraDistance");
                    _targetDistanceProp = AccessTools.Property(typeof(MapCameraView), "TargetCameraDistance");
                    _cameraTargetField = AccessTools.Field(typeof(MapCameraView), "_cameraTarget");
                    _lastUsedTargetField = AccessTools.Field(typeof(MapCameraView), "_lastUsedIdealCameraTarget");
                }
                return _instanceProp != null ? _instanceProp.GetValue(null) as MapCameraView : null;
            }
            catch { return null; }
        }
    }
}
