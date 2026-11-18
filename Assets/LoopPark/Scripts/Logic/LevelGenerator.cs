using System;
using System.Collections.Generic;

namespace LoopPark
{
    /// <summary>关卡参数（难度曲线）。</summary>
    public struct LevelParams
    {
        public int columns, capacity, groups, colors, lanes, fences, loopLimit, difficulty;
        public float hidden, minRate, maxRate, links;
        /// <summary>目标绑带对数（>0 时优先于 links 概率）。</summary>
        public int linkPairs;
        /// <summary>钥匙数 / 锁数 / 帘子列数 / 帘子数值。</summary>
        public int keys, locks, curtains, curtainValue;
        /// <summary>彩色车道 / 冰车道(冰层) / 车库 / 电梯(层数) / 车道车库生成器(数量)。</summary>
        public int colored, iced, iceValue, garages, elevators, elevatorLevels, generators, generatorValue;

        public override string ToString() =>
            $"C{columns} cap{capacity} G{groups} colors{colors} lanes{lanes} fence{fences} hidden{hidden:0.00} rate[{minRate:0.00},{maxRate:0.00}]";
    }

    /// <summary>
    /// 程序化关卡生成：随机摆放 → 用自动对局验证可解 → 按“普通玩家通关率”筛选难度。
    /// 同一个关号永远生成同一个关卡（确定性随机种子）。
    /// </summary>
    public static class LevelGenerator
    {
        // ------------------------------------------------------------------
        // 难度曲线表（Editor/Data/difficulty_curve.csv）：每关的列数、容量、车道、车数、
        // 难度档、问号比例、绑带对数、栅栏列数。只含结构参数，车辆摆放由生成器自己构造。
        // ------------------------------------------------------------------
        public static Dictionary<int, LevelParams> Curve;
        public static int CurveMax;

        public static void LoadCurve(string csv)
        {
            Curve = new Dictionary<int, LevelParams>();
            CurveMax = 0;
            var lines = csv.Replace("\r", "").Split('\n');
            if (lines.Length < 2) return;
            var head = new List<string>(lines[0].Split(','));
            int Col(string k) => head.IndexOf(k);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var f = lines[i].Split(',');
                float F(string k) => Col(k) < 0 ? 0f : float.Parse(f[Col(k)], System.Globalization.CultureInfo.InvariantCulture);
                int n = (int)F("level");
                var p = new LevelParams
                {
                    difficulty = (int)F("difficulty"),
                    columns = Math.Max(2, (int)F("columns")),
                    capacity = Math.Max(3, (int)F("capacity")),
                    loopLimit = Math.Max(3, (int)F("loopLimit")),
                    lanes = Math.Max(2, Math.Min(4, (int)F("lanes"))),
                    hidden = F("hidden"),
                    fences = (int)F("fences"),
                };
                int cars = (int)F("cars");
                p.groups = Math.Max(p.columns + 1, (int)Math.Round(cars / (float)p.capacity));
                p.colors = Math.Max(2, Math.Min(Palette.Count, Math.Min(p.groups, (int)F("colors"))));
                int queue = p.groups * p.capacity - p.columns * p.capacity;
                p.linkPairs = (int)F("links");
                p.links = Math.Min(0.7f, p.linkPairs * 2f / Math.Max(1, queue));
                p.fences = Math.Min(p.fences, Math.Max(0, p.columns - 2));
                p.keys = (int)F("keys");
                p.locks = Math.Min(p.keys, (int)F("locks"));
                p.curtains = Math.Min((int)F("curtains"), Math.Max(0, p.columns - 2 - p.fences));
                p.curtainValue = (int)F("curtainValue");
                p.colored = (int)F("colored");
                p.iced = (int)F("iced");
                p.iceValue = Math.Min(6, (int)F("iceValue"));
                p.garages = (int)F("garages");
                p.elevators = (int)F("elevators");
                p.elevatorLevels = (int)F("elevatorLevels");
                p.generators = (int)F("generators");
                p.generatorValue = (int)F("generatorValue");
                SetBand(ref p, n);
                Curve[n] = p;
                CurveMax = Math.Max(CurveMax, n);
            }
        }

        static void SetBand(ref LevelParams p, int n)
        {
            if (n <= 10) { p.minRate = 0.5f; p.maxRate = 1f; }
            else if (p.difficulty == 2) { p.minRate = 0.02f; p.maxRate = 0.25f; }
            else if (p.difficulty == 1) { p.minRate = 0.08f; p.maxRate = 0.45f; }
            else { p.minRate = 0.2f; p.maxRate = 0.95f; }
        }

        public static LevelParams ParamsFor(int n)
        {
            if (Curve != null && Curve.Count > 0)
            {
                if (Curve.TryGetValue(n, out var cp)) return cp;
                if (n > CurveMax && CurveMax >= 100)
                {
                    // 超出曲线表：循环最后 100 关的节奏，每轮略微加难
                    int cycle = (n - CurveMax - 1) / 100 + 1;
                    int src = CurveMax - 99 + (n - CurveMax - 1) % 100;
                    if (Curve.TryGetValue(src, out var bp))
                    {
                        bp.groups += cycle;
                        bp.colors = Math.Min(Palette.Count, Math.Min(bp.groups, bp.colors + cycle));
                        bp.hidden = Math.Min(0.4f, bp.hidden + 0.03f * cycle);
                        bp.links = Math.Min(0.7f, bp.links + 0.03f * cycle);
                        if (bp.linkPairs > 0) bp.linkPairs += cycle;
                        // 原版后期才出现的帘子：每 6 关出现一次
                        if (n % 6 == 0 && bp.columns - 2 - bp.fences >= 1)
                        {
                            bp.curtains = 1;
                            bp.curtainValue = bp.capacity * (1 + cycle % 2);
                        }
                        return bp;
                    }
                }
            }
            var p = new LevelParams { loopLimit = 5 };
            p.difficulty = n >= 8 && n % 10 == 0 ? 2 : (n >= 5 && n % 5 == 0 ? 1 : 0);

            if (n == 1) { p.columns = 2; p.capacity = 3; p.groups = 3; p.lanes = 2; }
            else if (n == 2) { p.columns = 2; p.capacity = 3; p.groups = 4; p.lanes = 2; }
            else if (n == 3) { p.columns = 3; p.capacity = 4; p.groups = 4; p.lanes = 2; }
            else
            {
                p.columns = Math.Min(7, 3 + n / 12);
                p.capacity = n < 16 ? 4 : n < 60 ? 5 : (n % 3 == 0 ? 6 : 5);
                if (n >= 120 && n % 4 == 1) p.capacity = 7;
                int extra = 1 + Math.Min(5, n / 8);
                p.groups = p.columns + extra;
                p.lanes = n < 6 ? 2 : 3;
            }

            p.colors = Math.Min(Palette.Count, Math.Min(p.groups, p.columns + 1));
            p.hidden = n < 15 ? 0f : Math.Min(0.3f, 0.08f + (n - 15) * 0.0015f);
            p.links = n < 30 || p.lanes < 2 ? 0f : Math.Min(0.22f, 0.08f + (n - 30) * 0.0008f);
            p.fences = n < 25 ? 0 : Math.Min(p.columns / 2, 1 + (n - 25) / 30);

            // 难度：困难关/超难关 收紧“普通玩家”通关率区间
            if (n <= 10) { p.minRate = 0.5f; p.maxRate = 1f; }
            else if (p.difficulty == 2) { p.minRate = 0.02f; p.maxRate = 0.25f; p.hidden += 0.05f; }
            else if (p.difficulty == 1) { p.minRate = 0.08f; p.maxRate = 0.45f; }
            else { p.minRate = 0.2f; p.maxRate = 0.95f; }
            return p;
        }

        public static LevelData Generate(int n, int maxAttempts = 220, int playouts = 60)
        {
            var p = ParamsFor(n);
            LevelData best = null;
            float bestDist = float.MaxValue;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (watch.Elapsed.TotalSeconds > 20 && best != null) break;
                if (watch.Elapsed.TotalSeconds > 30) break;
                int seed = unchecked(n * 7919 + attempt * 104729);
                var rng = new Random(seed);
                // 打乱程度：先尝试完全随机，失败后逐步降低（越整齐越容易）
                float mix = attempt % 3 == 2 ? 0.6f + 0.4f * (float)rng.NextDouble() : 1f;
                float noise = p.difficulty == 2 ? 0.6f : p.difficulty == 1 ? 0.45f : 0.25f;
                var d = BuildConstructive(n, p, rng, noise);
                if (d == null) continue;
                d.seed = seed;
                var vr = new Random(seed ^ 0x5bd1e995);

                int wins = 0;
                for (int i = 0; i < playouts; i++) if (PuzzleSim.Playout(d, vr, 1)) wins++;
                d.winRate = wins / (float)playouts;

                float dist = d.winRate < p.minRate ? p.minRate - d.winRate : d.winRate > p.maxRate ? d.winRate - p.maxRate : 0f;
                if (dist < bestDist) { bestDist = dist; best = d; }
                if (dist <= 0f) break;
            }
            if (best == null)
            {
                // 兜底：去掉问号和栅栏，挑一个强策略可解的
                for (int attempt = 0; attempt < 900 && best == null; attempt++)
                {
                    var rng = new Random(n * 31 + attempt);
                    var q = p;
                    q.hidden = 0;
                    if (attempt >= 150) q.fences = 0;
                    if (attempt >= 300) q.groups = Math.Max(q.columns + 1, p.groups - 1);
                    if (attempt >= 600) q.groups = Math.Max(q.columns, p.groups - 2);
                    if (attempt >= 300) { q.colored /= 2; q.garages /= 2; q.iced /= 2; q.elevators = 0; q.generators = 0; }
                    if (attempt >= 600) { q.colored = 0; q.garages = 0; q.iced = 0; q.curtains = 0; q.keys = 0; q.locks = 0; q.linkPairs = 0; q.links = 0; }
                    var d = BuildConstructive(n, q, rng, 0f);
                    if (d != null) { best = d; best.seed = n * 31 + attempt; best.winRate = -1; }
                }
            }
            return best;
        }

        static LevelData Build(int n, LevelParams p, Random rng, float mix)
        {
            var d = new LevelData
            {
                id = n,
                difficulty = p.difficulty,
                capacity = p.capacity,
                loopLimit = p.loopLimit,
            };

            // 颜色组：前 colors 个组颜色各不相同，之后的组重复已有颜色
            var palette = new List<int>();
            for (int i = 0; i < Palette.Count; i++) palette.Add(i);
            Shuffle(palette, rng);
            var groupColors = new List<int>();
            for (int g = 0; g < p.groups; g++)
                groupColors.Add(g < p.colors ? palette[g] : palette[rng.Next(p.colors)]);

            var cars = new List<int>();
            foreach (var c in groupColors)
                for (int k = 0; k < p.capacity; k++) cars.Add(c);

            // 先按组整齐排列（组顺序随机），再做 mix 比例的随机交换
            var groupOrder = new List<int>();
            for (int g = 0; g < p.groups; g++) groupOrder.Add(g);
            for (int tries = 0; tries < 50; tries++)
            {
                Shuffle(groupOrder, rng);
                cars.Clear();
                foreach (var g in groupOrder)
                    for (int k = 0; k < p.capacity; k++) cars.Add(groupColors[g]);
                if (mix >= 0.999f) Shuffle(cars, rng);
                else
                {
                    int swaps = Math.Max(2, (int)(cars.Count * mix));
                    for (int sw = 0; sw < swaps; sw++)
                    {
                        int a = rng.Next(cars.Count), b = rng.Next(cars.Count);
                        (cars[a], cars[b]) = (cars[b], cars[a]);
                    }
                }
                FixBoard(cars, p, rng);
                if (BoardOk(cars, p)) break;
            }

            int idx = 0;
            for (int j = 0; j < p.columns; j++)
            {
                var col = new ColumnSpec();
                for (int k = 0; k < p.capacity; k++) col.cars.Add(new CarSpec(cars[idx++], false));
                d.columns.Add(col);
            }

            // 栅栏列：随机挑选，但至少保留两列普通列
            var colIdx = new List<int>();
            for (int j = 0; j < p.columns; j++) colIdx.Add(j);
            Shuffle(colIdx, rng);
            int fences = Math.Min(p.fences, Math.Max(0, p.columns - 2));
            for (int f = 0; f < fences; f++) d.columns[colIdx[f]].fence = true;

            for (int l = 0; l < p.lanes; l++) d.lanes.Add(new LaneSpec());
            int lane = rng.Next(p.lanes);
            while (idx < cars.Count)
            {
                d.lanes[lane].cars.Add(new CarSpec(cars[idx++], false));
                lane = (lane + 1) % p.lanes;
            }

            // 问号车：列中间、发车道非队首
            if (p.hidden > 0)
            {
                foreach (var col in d.columns)
                    for (int k = 1; k < col.cars.Count - 1; k++)
                        if (rng.NextDouble() < p.hidden) col.cars[k].h = true;
                foreach (var l in d.lanes)
                    for (int k = 1; k < l.cars.Count; k++)
                        if (rng.NextDouble() < p.hidden) l.cars[k].h = true;
            }
            return d;
        }

        /// <summary>开局同色满列：与别处不同颜色的车交换一辆。</summary>
        static void FixBoard(List<int> cars, LevelParams p, Random rng)
        {
            for (int j = 0; j < p.columns; j++)
            {
                int start = j * p.capacity;
                bool same = true;
                for (int k = 1; k < p.capacity; k++) if (cars[start + k] != cars[start]) same = false;
                if (!same) continue;
                for (int tries = 0; tries < 100; tries++)
                {
                    int o = rng.Next(cars.Count);
                    if (o >= start && o < start + p.capacity) continue;
                    if (cars[o] == cars[start]) continue;
                    int a = start + rng.Next(p.capacity);
                    (cars[a], cars[o]) = (cars[o], cars[a]);
                    break;
                }
            }
        }

        /// <summary>
        /// 构造式生成：随机摆好停车列后，沿一条“能通关的路线”逐辆决定发车顺序，
        /// 按顺序分配到各车道。保证至少存在一个解；noise 越大路线越曲折（越难）。
        /// </summary>
        static LevelData BuildConstructive(int n, LevelParams p, Random rng, float noise)
        {
            var d = Build(n, p, rng, 1f);
            var pool = new List<int>();
            foreach (var l in d.lanes) foreach (var c in l.cars) pool.Add(c.c);
            foreach (var l in d.lanes) l.cars.Clear();

            // 帘子：挑普通列盖上，颜色选场上数量足够的颜色
            if (p.curtains > 0)
            {
                var cand = new List<int>();
                for (int j = 0; j < d.columns.Count; j++) if (!d.columns[j].fence) cand.Add(j);
                Shuffle(cand, rng);
                var total = new Dictionary<int, int>();
                foreach (var c in pool) { total.TryGetValue(c, out var v); total[c] = v + 1; }
                foreach (var col in d.columns) foreach (var c in col.cars) { total.TryGetValue(c.c, out var v); total[c.c] = v + 1; }
                for (int i = 0; i < p.curtains && i < cand.Count - 1; i++)
                {
                    var col = d.columns[cand[i]];
                    var colors = new List<int>(total.Keys);
                    Shuffle(colors, rng);
                    foreach (var color in colors)
                    {
                        int inCol = col.cars.FindAll(x => x.c == color).Count;
                        int outside = total[color] - inCol;
                        if (outside < p.capacity) continue;
                        col.cc = color;
                        int want = p.curtainValue > 0 ? p.curtainValue : p.capacity;
                        col.cv = Math.Max(1, Math.Min(want, outside));
                        break;
                    }
                }
            }
            // 可用于障碍的列（保留至少 2 个普通列）
            var free = new List<int>();
            for (int j = 0; j < d.columns.Count; j++) if (!d.columns[j].fence && d.columns[j].cv == 0) free.Add(j);
            Shuffle(free, rng);
            int budget = Math.Max(0, free.Count - 2);
            int Take() { if (budget <= 0 || free.Count == 0) return -1; budget--; int j = free[free.Count - 1]; free.RemoveAt(free.Count - 1); return j; }
            var colorTotals = new Dictionary<int, int>();
            foreach (var c in pool) { colorTotals.TryGetValue(c, out var v); colorTotals[c] = v + 1; }
            foreach (var col in d.columns) foreach (var c in col.cars) { colorTotals.TryGetValue(c.c, out var v); colorTotals[c.c] = v + 1; }
            var colorList = new List<int>(colorTotals.Keys);

            // 电梯：从车道车辆里取整层车放到列下面
            for (int e = 0; e < p.elevators; e++)
            {
                int lv = Math.Max(1, p.elevatorLevels);
                if (pool.Count - lv * p.capacity < d.lanes.Count * 2) break;
                int j = Take();
                if (j < 0) break;
                for (int k = 0; k < lv; k++)
                {
                    var level = new LevelCars();
                    for (int m = 0; m < p.capacity; m++)
                    {
                        int pi = rng.Next(pool.Count);
                        level.cars.Add(new CarSpec(pool[pi], false));
                        pool.RemoveAt(pi);
                    }
                    d.columns[j].levels.Add(level);
                }
            }
            // 彩色车道
            var usedLane = new HashSet<int>();
            for (int e = 0; e < p.colored; e++)
            {
                int j = Take();
                if (j < 0) break;
                Shuffle(colorList, rng);
                foreach (var color in colorList)
                    if (!usedLane.Contains(color)) { d.columns[j].colored = true; d.columns[j].lcol = color; usedLane.Add(color); break; }
            }
            // 车库：锁色选别的列里能消除的颜色
            for (int e = 0; e < p.garages; e++)
            {
                int j = Take();
                if (j < 0) break;
                Shuffle(colorList, rng);
                foreach (var color in colorList)
                {
                    int inCol = d.columns[j].cars.FindAll(x => x.c == color).Count;
                    if (colorTotals[color] - inCol >= p.capacity) { d.columns[j].garage = true; d.columns[j].gcol = color; break; }
                }
            }
            // 冰车道
            for (int e = 0; e < p.iced; e++)
            {
                int j = Take();
                if (j < 0) break;
                d.columns[j].ice = Math.Max(1, p.iceValue > 0 ? p.iceValue : 1);
            }

            // 钥匙：放在没被帘子盖住的列里
            if (p.keys > 0)
            {
                var slots = new List<CarSpec>();
                foreach (var col in d.columns) if (col.cv == 0 && !col.garage) foreach (var c in col.cars) if (!c.h) slots.Add(c);
                Shuffle(slots, rng);
                for (int i = 0; i < p.keys && i < slots.Count; i++) slots[i].k = true;
            }

            var sim = PuzzleSim.From(d);
            sim.remaining += pool.Count; // 车道车辆尚未分配，也要计入
            var order = new List<(int c, int link)>();
            var keysBefore = new List<int>();
            int nextLink = 1;
            int pairsLeft = p.linkPairs;
            while (pool.Count > 0)
            {
                var distinct = new List<int>();
                foreach (var c in pool) if (!distinct.Contains(c)) distinct.Add(c);

                // 绑带：一次放两辆
                float pairChance = p.linkPairs > 0 ? Math.Min(1f, pairsLeft * 2.5f / pool.Count) : p.links;
                bool tryPair = pairChance > 0 && pool.Count >= 2 && d.lanes.Count >= 2 && sim.loop.Count + 2 <= sim.limit && rng.NextDouble() < pairChance;
                if (tryPair)
                {
                    var pairs = new List<(int a, int b, float h)>();
                    for (int i = 0; i < distinct.Count; i++)
                    for (int k = i; k < distinct.Count; k++)
                    {
                        int ca = distinct[i], cb = distinct[k];
                        if (ca == cb && pool.FindAll(x => x == ca).Count < 2) continue;
                        var t = sim.Copy();
                        t.loop.Add(ca);
                        t.loop.Add(cb);
                        if (!t.Resolve()) continue;
                        bool last = pool.Count == 2;
                        if (!last && t.loop.Count >= t.limit) continue;
                        if (last && !t.Won) continue;
                        pairs.Add((ca, cb, t.Heuristic() + (float)rng.NextDouble() * 0.5f));
                    }
                    if (pairs.Count > 0)
                    {
                        pairs.Sort((x, y) => x.h.CompareTo(y.h));
                        var pp = rng.NextDouble() < noise ? pairs[rng.Next(pairs.Count)] : pairs[0];
                        pool.Remove(pp.a);
                        pool.Remove(pp.b);
                        keysBefore.Add(sim.keys);
                        keysBefore.Add(sim.keys);
                        order.Add((pp.a, nextLink));
                        order.Add((pp.b, nextLink));
                        nextLink++;
                        pairsLeft--;
                        sim.loop.Add(pp.a);
                        sim.loop.Add(pp.b);
                        sim.Resolve();
                        continue;
                    }
                }

                int bestC = -1;
                float bestH = float.MaxValue;
                var valid = new List<int>();
                foreach (var c in distinct)
                {
                    var t = sim.Copy();
                    t.loop.Add(c);
                    if (!t.Resolve()) continue;
                    bool last = pool.Count == 1;
                    if (!last && t.loop.Count >= t.limit) continue;
                    if (last && !t.Won) continue;
                    valid.Add(c);
                    float h = t.Heuristic() + (float)rng.NextDouble() * 0.5f;
                    if (h < bestH) { bestH = h; bestC = c; }
                }
                if (valid.Count == 0) return null;
                int pick = rng.NextDouble() < noise ? valid[rng.Next(valid.Count)] : bestC;
                pool.Remove(pick);
                keysBefore.Add(sim.keys);
                order.Add((pick, 0));
                sim.loop.Add(pick);
                sim.Resolve();
            }
            if (!sim.Won) return null;

            // 按路线顺序分配车道（同一车道内保持顺序）；绑带两车分到不同车道
            int lanes = d.lanes.Count;
            int prev = -1;
            var taps = new List<int>();
            var lockable = new List<(CarSpec spec, int keysBefore)>();
            for (int i = 0; i < order.Count; i++)
            {
                var (c, link) = order[i];
                int lane = rng.Next(lanes);
                if (lane == prev && rng.NextDouble() < 0.5) lane = rng.Next(lanes);
                if (link != 0 && i + 1 < order.Count && order[i + 1].link == link)
                {
                    int lane2 = (lane + 1 + rng.Next(lanes - 1)) % lanes;
                    d.lanes[lane].cars.Add(new CarSpec(c, false) { l = link });
                    d.lanes[lane2].cars.Add(new CarSpec(order[i + 1].c, false) { l = link });
                    taps.Add(lane);
                    prev = lane2;
                    i++;
                    continue;
                }
                var spec = new CarSpec(c, false);
                d.lanes[lane].cars.Add(spec);
                taps.Add(lane);
                lockable.Add((spec, keysBefore[i]));
                prev = lane;
            }

            // 锁：只锁“轮到它发车时已经有钥匙可用”的车，再用重放验证；不行就逐个撤销
            if (p.locks > 0)
            {
                var locked = new List<CarSpec>();
                int used = 0;
                foreach (var (spec, kb) in lockable)
                {
                    if (used >= p.locks) break;
                    if (kb > used && rng.NextDouble() < 0.6) { spec.lk = true; locked.Add(spec); used++; }
                }
                while (locked.Count > 0 && !PuzzleSim.Replay(d, taps))
                {
                    locked[locked.Count - 1].lk = false;
                    locked.RemoveAt(locked.Count - 1);
                }
            }
            if (!PuzzleSim.Replay(d, taps)) return null;

            // 车道车库生成器：只影响信息（车藏在车库里），不改变规则
            if (p.generators > 0)
            {
                var laneIdx = new List<int>();
                for (int l = 0; l < d.lanes.Count; l++) laneIdx.Add(l);
                Shuffle(laneIdx, rng);
                int made = 0;
                foreach (var l in laneIdx)
                {
                    if (made >= p.generators) break;
                    var lane = d.lanes[l];
                    int gn = Math.Max(2, p.generatorValue);
                    if (lane.cars.Count < gn + 2) continue;
                    int gi = 1 + rng.Next(Math.Max(1, Math.Min(3, lane.cars.Count - gn - 1)));
                    bool bad = false;
                    for (int k = gi; k < gi + gn; k++) if (lane.cars[k].l != 0 || lane.cars[k].lk) bad = true;
                    if (bad) continue;
                    lane.gi = gi;
                    lane.gn = gn;
                    made++;
                }
            }

            // 问号车（车道非队首）
            if (p.hidden > 0)
                foreach (var l in d.lanes)
                    for (int k = 1; k < l.cars.Count; k++)
                        if (l.cars[k].l == 0 && !l.cars[k].lk && rng.NextDouble() < p.hidden) l.cars[k].h = true;
            return d;
        }

        static bool BoardOk(List<int> cars, LevelParams p)
        {
            // 开局不能有已经同色满列的
            int idx = 0;
            for (int j = 0; j < p.columns; j++)
            {
                bool same = true;
                for (int k = 1; k < p.capacity; k++) if (cars[idx + k] != cars[idx]) same = false;
                if (same) return false;
                idx += p.capacity;
            }
            return true;
        }

        static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int k = rng.Next(i + 1);
                (list[i], list[k]) = (list[k], list[i]);
            }
        }
    }
}
