using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 领土数据: 定居点位置 + 所属王国(国界线与政治地图上色共用)
    internal static class TerritoryData
    {
        private static readonly List<Kingdom> _kingdoms = new List<Kingdom>();
        private static readonly List<Settlement> _settlements = new List<Settlement>();   // 与 _xs/_ys 同序, 点击归属查询用
        private static readonly List<float> _xs = new List<float>();
        private static readonly List<float> _ys = new List<float>();
        private static readonly List<int> _ks = new List<int>();
        private static readonly List<uint> _colors = new List<uint>();   // 每个定居点自己的旗帜主色(和城市头牌一致)
        private static readonly List<byte> _cr = new List<byte>();       // 预转换好的 RGB 字节(避免每像素做字符串转换)
        private static readonly List<byte> _cg = new List<byte>();
        private static readonly List<byte> _cb = new List<byte>();
        private static volatile bool _built;

        // 空间网格加速(每个格子记录落在里面的定居点索引), 避免每次查询都全表扫描
        private const float GridCell = 40f;
        private static readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();

        internal static float MinX, MaxX, MinY, MaxY;

        internal static void MarkDirty() { _built = false; }

        internal static int PointCount { get { return _xs.Count; } }
        internal static int KingdomCount { get { return _kingdoms.Count; } }
        internal static float PointX(int i) { return _xs[i]; }
        internal static float PointY(int i) { return _ys[i]; }
        internal static Kingdom PointKingdom(int i) { return _kingdoms[_ks[i]]; }
        internal static uint PointColor(int i) { return i >= 0 && i < _colors.Count ? _colors[i] : 0u; }

        // 领地格子对应的定居点
        internal static Settlement PointSettlement(int i)
        {
            return i >= 0 && i < _settlements.Count ? _settlements[i] : null;
        }

        // 某个世界坐标落在哪个定居点的领地里(和地图上色/边界线用的是同一份数据: 点在哪块地就是哪个定居点)
        // 返回该定居点; 不在任何领地(比如没人要的荒地/无主)时返回 null
        internal static Settlement SettlementAt(float x, float y)
        {
            int idx = OwnerPointAt(x, y);
            return idx >= 0 ? _settlements[idx] : null;
        }

        // 预转换的 RGB(每个定居点只转一次, 贴图填充时直接用)
        internal static bool PointRgb(int i, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (i < 0 || i >= _cr.Count) return false;
            r = _cr[i]; g = _cg[i]; b = _cb[i];
            return true;
        }

        internal static bool Ready
        {
            get { Ensure(); return _kingdoms.Count >= 2 && _xs.Count >= 2; }
        }

        internal static void Ensure()
        {
            if (_built) return;
            _built = true;
            _kingdoms.Clear(); _settlements.Clear(); _xs.Clear(); _ys.Clear(); _ks.Clear(); _colors.Clear();
            _cr.Clear(); _cg.Clear(); _cb.Clear();
            _grid.Clear();
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            try
            {
                foreach (var s in Settlement.All)
                {
                    try
                    {
                        if (s == null || s.IsHideout) continue;
                        if (!s.IsTown && !s.IsCastle && !s.IsVillage) continue;
                        var k = s.OwnerClan != null ? s.OwnerClan.Kingdom : null;
                        if (k == null) continue;
                        int idx = _kingdoms.IndexOf(k);
                        if (idx < 0) { _kingdoms.Add(k); idx = _kingdoms.Count - 1; }
                        var p = s.Position;
                        _settlements.Add(s);
                        _xs.Add(p.X); _ys.Add(p.Y); _ks.Add(idx);
                        // 该定居点自己的旗帜主色(城市头牌上显示的就是这个), 顺便预转成 RGB 字节
                        uint col = 0u;
                        byte cr = 0, cg = 0, cb = 0;
                        try
                        {
                            var banner = s.Banner;
                            if (banner != null) col = banner.GetPrimaryColor();
                            else if (k.Banner != null) col = k.Banner.GetPrimaryColor();
                            if (col != 0u)
                            {
                                string hex = TaleWorlds.Library.Color.UIntToColorString(col);
                                if (hex != null && hex.Length >= 6)
                                {
                                    cr = Convert.ToByte(hex.Substring(0, 2), 16);
                                    cg = Convert.ToByte(hex.Substring(2, 2), 16);
                                    cb = Convert.ToByte(hex.Substring(4, 2), 16);
                                }
                            }
                        }
                        catch { }
                        _colors.Add(col);
                        _cr.Add(cr); _cg.Add(cg); _cb.Add(cb);
                        if (p.X < minX) minX = p.X;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.Y > maxY) maxY = p.Y;
                    }
                    catch { }
                }
            }
            catch { }
            MinX = minX; MaxX = maxX; MinY = minY; MaxY = maxY;

            // 建空间网格
            try
            {
                for (int i = 0; i < _xs.Count; i++)
                {
                    long key = CellKey(_xs[i], _ys[i]);
                    List<int> list;
                    if (!_grid.TryGetValue(key, out list)) { list = new List<int>(4); _grid[key] = list; }
                    list.Add(i);
                }
            }
            catch { }
        }

        private static long CellKey(float x, float y)
        {
            long ix = (long)Math.Floor(x / GridCell);
            long iy = (long)Math.Floor(y / GridCell);
            return (ix << 32) ^ (iy & 0xFFFFFFFFL);
        }

        // 最近定居点所属王国(Voronoi 归属); 先用 3x3 网格找, 找不到再扩大/全表
        internal static Kingdom OwnerAt(float x, float y)
        {
            int idx = OwnerPointAt(x, y);
            return idx >= 0 ? _kingdoms[_ks[idx]] : null;
        }

        // 最近定居点的索引(-1 = 无)
        internal static int OwnerPointAt(float x, float y)
        {
            Ensure();
            if (_xs.Count == 0) return -1;
            int best = -1;
            float bestD = float.MaxValue;

            long cx = (long)Math.Floor(x / GridCell);
            long cy = (long)Math.Floor(y / GridCell);
            for (int ring = 1; ring <= 4; ring++)
            {
                for (long gx = cx - ring; gx <= cx + ring; gx++)
                {
                    for (long gy = cy - ring; gy <= cy + ring; gy++)
                    {
                        // 只查新增的一圈
                        if (ring > 1 && gx > cx - ring + 1 && gx < cx + ring && gy > cy - ring + 1 && gy < cy + ring)
                            continue;
                        List<int> list;
                        if (!_grid.TryGetValue((gx << 32) ^ (gy & 0xFFFFFFFFL), out list)) continue;
                        for (int k = 0; k < list.Count; k++)
                        {
                            int i = list[k];
                            float dx = _xs[i] - x;
                            float dy = _ys[i] - y;
                            float d = dx * dx + dy * dy;
                            if (d < bestD) { bestD = d; best = i; }
                        }
                    }
                }
                if (best >= 0) break;
            }

            if (best < 0)
            {
                for (int i = 0; i < _xs.Count; i++)
                {
                    float dx = _xs[i] - x;
                    float dy = _ys[i] - y;
                    float d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            return best;
        }
    }
}
