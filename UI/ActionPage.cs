using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    internal static class ActionPage
    {
        private static readonly Dictionary<string, PanelVMBase> _vms = new Dictionary<string, PanelVMBase>();
        internal static string MovieOf(string key) { if (key == "lobby") return "FeudalLobby"; if (key == "parly") return "FeudalParliament"; return "FeudalChest"; }
        internal static float Width { get { return 680f; } }
        internal static void Open(string key)
        {
            try
            {
                if (PanelScreen.IsOpen(key)) { PanelVMBase v0 = null; _vms.TryGetValue(key, out v0); if (v0 != null) { Refresh(v0); RegisterSpots(key, v0); } return; }
                PanelVMBase vm = null;
                if (key == "lobby") vm = new LobbyVM(delegate { Close(key); });
                else if (key == "parly") vm = new ParliamentVM(delegate { Close(key); });
                else vm = new ChestVM(delegate { Close(key); });
                vm.OpenPanelAnim(Width);
                PanelScreen.OpenPanel(key, MovieOf(key), vm, null, delegate (float dt) { vm.TickAnim(dt); OnTick(dt, vm); RegisterSpots(key, vm); }, null);
                _vms[key] = vm;
                RegisterSpots(key, vm);
            }
            catch (Exception ex) { DLog.Force("Open action page failed: " + ex.Message); }
        }
        internal static void Close(string key)
        {
            try { PanelScreen.ClosePanel(key); } catch { }
        }
        internal static void Refresh(PanelVMBase vm)
        {
            try { if (vm is LobbyVM) ((LobbyVM)vm).Refresh(); else if (vm is ParliamentVM) ((ParliamentVM)vm).Refresh(); else if (vm is ChestVM) ((ChestVM)vm).Refresh(); } catch { }
        }
        private static float _acc;
        private static void OnTick(float dt, PanelVMBase vm)
        {
            try { _acc += dt; if (_acc < 3f) return; _acc = 0f; Refresh(vm); } catch { }
        }
        private static MBBindingList<ActRowVM> RowsOf(PanelVMBase vm)
        {
            try { if (vm is LobbyVM) return ((LobbyVM)vm).Rows; if (vm is ParliamentVM) return ((ParliamentVM)vm).Rows; if (vm is ChestVM) return ((ChestVM)vm).Rows; } catch { } return null;
        }
        private static void Click(PanelVMBase vm, int idx)
        {
            try { if (vm is LobbyVM) ((LobbyVM)vm).Click(idx); else if (vm is ParliamentVM) ((ParliamentVM)vm).Click(idx); else if (vm is ChestVM) ((ChestVM)vm).Click(idx); } catch { }
        }
        private static void RegisterSpots(string key, PanelVMBase vm)
        {
            try
            {
                if (vm == null) return;
                PanelScreen.ClearSpots();
                var rows = RowsOf(vm);
                int n = rows == null ? 0 : rows.Count;
                float lim = PanelScreen.ScreenHeight() - 26f - 42f;
                float y = 136f;
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    float h = 58f;
                    try { h = 34f + rows[i].Body.Split((char)10).Length * 24f; } catch { }
                    if (y >= lim) break;
                    Spot(26f, y, 628f, Math.Min(h, lim - y), delegate { Click(vm, idx); });
                    y += h + 10f;
                }
                Spot(Width - 60f, 20f, 60f, 58f, delegate { Close(key); });
                Spot(30f, lim, 122f, 42f, delegate { Close(key); });
            }
            catch (Exception ex) { DLog.Force("Action spots failed: " + ex.Message); }
        }
        private static void Spot(float x, float y, float w, float h, Action act)
        {
            PanelScreen.AddSpotRaw(PanelScreen.PanelX + x, y, w, h, act);
        }
    }
}