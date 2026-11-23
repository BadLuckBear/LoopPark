using System;
using System.Collections.Generic;

namespace LoopPark
{
    /// <summary>
    /// 离散规则模拟（用于关卡生成与可解性验证、自动演示）。
    /// 规则：
    ///  1. 点击发车道最前面的车 → 车开上环路（环路车数 &lt; 上限才能点）。
    ///  2. 环路逆时针行驶，经过车列顶部入口时：最上方的车同色 → 驶入；
    ///     车列为空 → 只有在场上没有顶部同色的列时才驶入（避免同色车被拆散）。
    ///     实际游戏里车从右往左依次经过车列入口，所以这里取“最右侧可进入的列”。
    ///  3. 驶入已满的列 → 最下方的车被挤出，回到环路。
    ///  4. 列内达到实际列深度的同色车辆 → 消除；普通列清空后可再次使用，锁定竖列关闭。
    ///  5. 绑带车：两辆都在队首才能一起发车。
    ///  6. 钥匙锁：带钥匙的车离开车列（挤出或消除）时钥匙进入钥匙栈；
    ///     上锁的车排到队首时自动消耗一把钥匙开锁，没开锁前不能发车。
    ///  7. 帘子：被帘子盖住的列不能进车；每消除一辆帘子颜色的车，帘子数字 -1，归零后打开。
    ///  8. 彩色车道：只收该颜色；该颜色被预留，不能进普通空列。
    ///  9. 集装箱：盖住整列；某颜色的列消除时，同色集装箱全部打开。没有颜色的提取栅栏在任意一次消除后打开。
    /// 10. 冰车道：不能进车；任意一列消除，其它冰车道冰层 -1，归零后可用。
    /// 11. 电梯：列被消除后，下一层整列升上来。
    /// 12. 全部车辆消除 → 胜利；无车可发且环路上没有车能进列 → 失败。
    /// </summary>
    public class PuzzleSim
    {
        public const int KeyBit = 100;
        public static int Col(int v) => v % KeyBit;
        public static bool HasKey(int v) => v >= KeyBit;

        public int cap, slotCap, limit;
        public List<int>[] cols;          // 颜色 (+100 表示带钥匙)
        public bool[] fence, closed;
        public int[] curtainColor, curtainLeft; // curtainLeft > 0 表示被帘子盖住
        public int[] laneColor;                 // 彩色车道颜色，-1 = 普通
        public int[] garageColor;               // 集装箱锁色，-1 = 无颜色/已打开
        public bool[] garageAnyColor;           // 提取栅栏未记录颜色时，任意一次消除即可打开
        public int[] ice;                       // 冰层数
        public List<int>[][] levels;            // 电梯：剩余楼层（每层一组车）
        public int[] levelIdx;
        public int[][] lanes;
        public int[][] laneLinks;
        public bool[][] laneLocked;
        public int[] laneIdx;
        public bool[] frontUnlocked;
        public int keys;
        public List<int> loop = new List<int>();
        public int remaining;
        public int clears;

        public static PuzzleSim From(LevelData d)
        {
            // cp is the number of matching cars removed at once. slotCapacity is the
            // physical number of cars a column can hold before ejecting its bottom car.
            var s = new PuzzleSim { cap = d.capacity, slotCap = d.EffectiveCapacity, limit = d.loopLimit };
            int n = d.columns.Count;
            s.cols = new List<int>[n];
            s.fence = new bool[n];
            s.closed = new bool[n];
            s.curtainColor = new int[n];
            s.curtainLeft = new int[n];
            s.laneColor = new int[n];
            s.garageColor = new int[n];
            s.garageAnyColor = new bool[n];
            s.ice = new int[n];
            s.levels = new List<int>[n][];
            s.levelIdx = new int[n];
            for (int i = 0; i < n; i++)
            {
                var cs = d.columns[i];
                s.cols[i] = new List<int>();
                foreach (var c in cs.cars) s.cols[i].Add(c.c + (c.k ? KeyBit : 0));
                s.fence[i] = cs.LocksAfterClear;
                s.curtainColor[i] = cs.cc;
                s.curtainLeft[i] = cs.cv;
                s.laneColor[i] = cs.colored ? cs.lcol : -1;
                s.garageAnyColor[i] = cs.IsContainer && cs.GetContainerColor() < 0;
                s.garageColor[i] = cs.IsContainer ? cs.GetContainerColor() : -1;
                s.ice[i] = cs.ice;
                s.levels[i] = new List<int>[cs.levels.Count];
                for (int k = 0; k < cs.levels.Count; k++)
                {
                    s.levels[i][k] = new List<int>();
                    foreach (var c in cs.levels[k].cars) s.levels[i][k].Add(c.c + (c.k ? KeyBit : 0));
                }
            }
            int L = d.lanes.Count;
            s.lanes = new int[L][];
            s.laneLinks = new int[L][];
            s.laneLocked = new bool[L][];
            s.laneIdx = new int[L];
            s.frontUnlocked = new bool[L];
            for (int i = 0; i < L; i++)
            {
                var cars = d.lanes[i].cars;
                s.lanes[i] = new int[cars.Count];
                s.laneLinks[i] = new int[cars.Count];
                s.laneLocked[i] = new bool[cars.Count];
                for (int k = 0; k < cars.Count; k++)
                {
                    s.lanes[i][k] = cars[k].c;
                    s.laneLinks[i][k] = cars[k].l;
                    s.laneLocked[i][k] = cars[k].lk;
                }
            }
            s.remaining = d.TotalCars();
            s.Unlock();
            return s;
        }

        public PuzzleSim Copy()
        {
            var s = (PuzzleSim)MemberwiseClone();
            s.cols = new List<int>[cols.Length];
            for (int i = 0; i < cols.Length; i++) s.cols[i] = new List<int>(cols[i]);
            s.closed = (bool[])closed.Clone();
            s.curtainLeft = (int[])curtainLeft.Clone();
            s.garageColor = (int[])garageColor.Clone();
            s.garageAnyColor = (bool[])garageAnyColor.Clone();
            s.ice = (int[])ice.Clone();
            s.levelIdx = (int[])levelIdx.Clone();
            s.laneIdx = (int[])laneIdx.Clone();
            s.frontUnlocked = (bool[])frontUnlocked.Clone();
            s.loop = new List<int>(loop);
            return s;
        }

        public bool Won => remaining == 0;

        public bool Covered(int j) => curtainLeft[j] > 0 || garageColor[j] >= 0 || garageAnyColor[j] || ice[j] > 0;

        /// <summary>该颜色是否被某条可用的彩色车道预留。</summary>
        public bool Reserved(int color)
        {
            for (int j = 0; j < cols.Length; j++)
                if (laneColor[j] == color && !closed[j]) return true;
            return false;
        }
        public bool LaneHasCar(int l) => laneIdx[l] < lanes[l].Length;
        public int LaneFront(int l) => LaneHasCar(l) ? lanes[l][laneIdx[l]] : -1;
        public int FrontLink(int l) => LaneHasCar(l) ? laneLinks[l][laneIdx[l]] : 0;
        public bool FrontLocked(int l) => LaneHasCar(l) && laneLocked[l][laneIdx[l]] && !frontUnlocked[l];

        /// <summary>绑带搭档所在车道（搭档必须也在队首），没有返回 -1。</summary>
        public int PartnerLane(int l)
        {
            int lk = FrontLink(l);
            if (lk == 0) return -1;
            for (int m = 0; m < lanes.Length; m++)
                if (m != l && FrontLink(m) == lk) return m;
            return -1;
        }

        public bool CanTap(int l)
        {
            if (!LaneHasCar(l) || FrontLocked(l)) return false;
            if (FrontLink(l) == 0) return loop.Count < limit;
            int pl = PartnerLane(l);
            return pl >= 0 && !FrontLocked(pl) && loop.Count + 2 <= limit;
        }

        void Advance(int l)
        {
            loop.Add(lanes[l][laneIdx[l]]);
            laneIdx[l]++;
            frontUnlocked[l] = false;
        }

        /// <summary>发车（不结算）。绑带车会连同搭档一起发出。</summary>
        public void TapRaw(int l)
        {
            int pl = PartnerLane(l);
            Advance(l);
            if (pl >= 0) Advance(pl);
            Unlock();
        }

        /// <summary>有钥匙时，按车道顺序给队首上锁的车开锁。</summary>
        public void Unlock()
        {
            for (int l = 0; l < lanes.Length && keys > 0; l++)
            {
                if (!FrontLocked(l)) continue;
                keys--;
                frontUnlocked[l] = true;
            }
        }

        public bool AnyTap()
        {
            for (int i = 0; i < lanes.Length; i++) if (CanTap(i)) return true;
            return false;
        }

        /// <summary>优先进入顶部同色的列（最右侧）；没有同色列时才进入空列。</summary>
        public int Target(int color)
        {
            for (int j = cols.Length - 1; j >= 0; j--)
            {
                if (closed[j] || Covered(j)) continue;
                if (laneColor[j] >= 0) { if (laneColor[j] == color) return j; continue; }
                var c = cols[j];
                if (c.Count > 0 && Col(c[c.Count - 1]) == color) return j;
            }
            if (Reserved(color)) return -1;
            for (int j = cols.Length - 1; j >= 0; j--)
                if (!closed[j] && !Covered(j) && laneColor[j] < 0 && cols[j].Count == 0) return j;
            return -1;
        }

        public bool Stuck => !Won && !AnyTap();

        public void Tap(int l)
        {
            TapRaw(l);
            Resolve();
        }

        /// <returns>false = 出现死循环（车辆互相挤出无限循环）。</returns>
            public bool Resolve()
        {
            int guard = 0;
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int i = 0; i < loop.Count; i++)
                {
                    int t = Target(loop[i]);
                    if (t < 0) continue;
                    int color = loop[i];
                    loop.RemoveAt(i);
                    Enter(t, color);
                    progress = true;
                    break;
                }
                if (++guard > 2000) return false;
            }
            Unlock();
            return true;
        }

        void Enter(int j, int color)
        {
            var c = cols[j];
            c.Add(color);
            if (c.Count > slotCap)
            {
                int ej = c[0];
                c.RemoveAt(0);
                loop.Add(Col(ej));
                if (HasKey(ej)) keys++;
            }
            if (c.Count >= cap)
            {
                int first = c.Count - cap;
                int c0 = Col(c[first]);
                bool same = true;
                for (int k = first + 1; k < c.Count; k++) if (Col(c[k]) != c0) { same = false; break; }
                if (same)
                {
                    for (int k = first; k < c.Count; k++) if (HasKey(c[k])) keys++;
                    c.RemoveRange(first, cap);
                    remaining -= cap;
                    clears++;
                    if (fence[j]) closed[j] = true;
                    for (int m = 0; m < cols.Length; m++)
                    {
                        if (curtainLeft[m] > 0 && curtainColor[m] == c0)
                            curtainLeft[m] = Math.Max(0, curtainLeft[m] - cap);
                        if (garageAnyColor[m] || garageColor[m] == c0)
                        {
                            garageAnyColor[m] = false;
                            garageColor[m] = -1;
                        }
                        if (m != j && ice[m] > 0) ice[m]--;
                    }
                    // 电梯：下一层升上来
                    if (!closed[j] && c.Count == 0 && levelIdx[j] < levels[j].Length)
                    {
                        c.AddRange(levels[j][levelIdx[j]]);
                        levelIdx[j]++;
                    }
                }
            }
        }

        /// <summary>评估一次点击的好坏（越大越好），用于启发式策略与提示。</summary>
        public float ScoreTap(int l)
        {
            var s = Copy();
            int before = s.remaining;
            s.Tap(l);
            if (s.Won) return 1000;
            float score = (before - s.remaining) * 10f;
            score -= s.loop.Count * 3f;
            if (!s.AnyTap()) score -= 500;
            int front = LaneFront(l);
            if (Target(front) >= 0) score += 4;
            return score;
        }

        public string Key()
        {
            var sb = new System.Text.StringBuilder();
            for (int l = 0; l < laneIdx.Length; l++) sb.Append(laneIdx[l]).Append(frontUnlocked[l] ? 'u' : ',');
            sb.Append('|').Append(keys).Append('|');
            for (int j = 0; j < cols.Length; j++)
            {
                if (closed[j]) sb.Append('X');
                if (curtainLeft[j] > 0) sb.Append('C').Append(curtainLeft[j]);
                if (garageColor[j] >= 0 || garageAnyColor[j]) sb.Append('G');
                if (ice[j] > 0) sb.Append('I').Append(ice[j]);
                if (levelIdx[j] > 0) sb.Append('E').Append(levelIdx[j]);
                foreach (var c in cols[j]) sb.Append((char)('a' + Col(c))).Append(HasKey(c) ? "k" : "");
                sb.Append('/');
            }
            var lp = new List<int>(loop);
            lp.Sort();
            foreach (var c in lp) sb.Append((char)('a' + c));
            return sb.ToString();
        }

        public float Heuristic()
        {
            float h = remaining * 3f + loop.Count * 4f;
            for (int j = 0; j < cols.Length; j++)
            {
                var c = cols[j];
                for (int k = 1; k < c.Count; k++) if (Col(c[k]) != Col(c[k - 1])) h += 2f;
                h += curtainLeft[j] * 0.5f + ice[j] * 0.5f + (garageColor[j] >= 0 ? 3f : 0f);
            }
            for (int l = 0; l < lanes.Length; l++) if (FrontLocked(l)) h += 6f;
            return h;
        }

        /// <summary>束搜索求解：返回是否可解，solution 为车道点击序列。</summary>
        public static bool BeamSolve(LevelData d, int width, out List<int> solution)
        {
            solution = null;
            var start = From(d);
            var beam = new List<(PuzzleSim s, List<int> path)> { (start, new List<int>()) };
            var seen = new HashSet<string>();
            int maxDepth = 0;
            foreach (var l in start.lanes) maxDepth += l.Length;
            var next = new List<(float h, PuzzleSim s, List<int> path)>();
            for (int depth = 0; depth <= maxDepth; depth++)
            {
                next.Clear();
                foreach (var (s, path) in beam)
                {
                    for (int l = 0; l < s.lanes.Length; l++)
                    {
                        if (!s.CanTap(l)) continue;
                        var c = s.Copy();
                        c.TapRaw(l);
                        if (!c.Resolve()) continue;
                        var np = new List<int>(path) { l };
                        if (c.Won) { solution = np; return true; }
                        if (!c.AnyTap()) continue;
                        if (!seen.Add(c.Key())) continue;
                        next.Add((c.Heuristic(), c, np));
                    }
                }
                if (next.Count == 0) return false;
                next.Sort((a, b) => a.h.CompareTo(b.h));
                beam = new List<(PuzzleSim, List<int>)>();
                for (int i = 0; i < next.Count && i < width; i++) beam.Add((next[i].s, next[i].path));
            }
            return false;
        }

        /// <summary>按给定的车道点击顺序重放，检查每一步都合法且最终通关。</summary>
        public static bool Replay(LevelData d, List<int> taps)
        {
            var s = From(d);
            foreach (var l in taps)
            {
                if (!s.CanTap(l)) return false;
                s.TapRaw(l);
                if (!s.Resolve()) return false;
            }
            return s.Won;
        }

        /// <summary>
        /// 自动对局。skill 0 = 随机乱点；1 = 普通玩家（偏好能直接停进去的车）；2 = 一步前瞻贪心。
        /// </summary>
        public static bool Playout(LevelData d, Random rng, int skill, int maxSteps = 2000)
        {
            var s = From(d);
            var cand = new List<int>();
            var weights = new List<float>();
            for (int step = 0; step < maxSteps; step++)
            {
                if (s.Won) return true;
                cand.Clear();
                weights.Clear();
                for (int l = 0; l < s.lanes.Length; l++)
                {
                    if (!s.CanTap(l)) continue;
                    cand.Add(l);
                    float w = 1f;
                    if (skill == 1) w = s.Target(s.LaneFront(l)) >= 0 ? 4f : 1f;
                    else if (skill >= 2) w = (float)Math.Exp(Math.Max(-20, Math.Min(20, s.ScoreTap(l) * 0.35f)));
                    weights.Add(w);
                }
                if (cand.Count == 0) return false;
                float sum = 0;
                foreach (var w in weights) sum += w;
                float r = (float)rng.NextDouble() * sum;
                int pick = cand[cand.Count - 1];
                for (int i = 0; i < cand.Count; i++)
                {
                    r -= weights[i];
                    if (r <= 0) { pick = cand[i]; break; }
                }
                s.TapRaw(pick);
                if (!s.Resolve()) return false;
            }
            return s.Won;
        }
    }
}
