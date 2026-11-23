using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    /// <summary>一辆车：颜色索引 + 是否隐藏（问号车）。</summary>
    [Serializable]
    public class CarSpec
    {
        public int c;
        public bool h;
        /// <summary>绑带编号：0 = 无；同编号的两辆车（在不同车道）必须同时在队首才能一起发车。</summary>
        public int l;
        /// <summary>停车列里的车：带钥匙。离开车列（挤出或消除）时钥匙进入钥匙栈。</summary>
        public bool k;
        /// <summary>发车道里的车：上锁。排到队首时消耗一把钥匙开锁，开锁前不能发车。</summary>
        public bool lk;
        public CarSpec() { }
        public CarSpec(int color, bool hidden) { c = color; h = hidden; }
    }

    /// <summary>停车列。cars[0] = 最下方（出口端），最后一个 = 最上方（入口端）。</summary>
    [Serializable]
    public class ColumnSpec
    {
        /// <summary>兼容旧配置的栅栏标记。新配置应使用 container。</summary>
        public bool fence;
        /// <summary>清空后永久锁定的竖列，对应提取数据中的 pf.sd[].t。</summary>
        public bool lockAfterClear;
        /// <summary>集装箱列，对应提取数据中的 pf.sd[].sot 包含 1。</summary>
        public bool container;
        /// <summary>集装箱解锁颜色。-1 表示提取数据没有记录颜色。</summary>
        public int containerColor = -1;
        /// <summary>提取数据中锁定竖列的颜色提示，当前仅保留给编辑器和调试显示。</summary>
        public int lockColor = -1;
        /// <summary>帘子剩余数（0 = 没有帘子）。每消除一辆帘子颜色 cc 的车 -1，归零打开。</summary>
        public int cv;
        /// <summary>帘子颜色。</summary>
        public int cc;
        /// <summary>彩色车道：只收 lcol 颜色的车。</summary>
        public bool colored;
        public int lcol;
        /// <summary>车库：盖住整列，消除一列 gcol 颜色的车后打开。</summary>
        public bool garage;
        public int gcol;
        /// <summary>冰层数（0 = 无冰）。任意其它列消除一次 -1。</summary>
        public int ice;
        /// <summary>电梯：该列下面还有的楼层，清空后依次升上来。</summary>
        public List<LevelCars> levels = new List<LevelCars>();
        public List<CarSpec> cars = new List<CarSpec>();

        /// <summary>兼容旧生成器与新提取配置的集装箱判断。</summary>
        public bool IsContainer => container || garage;

        /// <summary>返回集装箱解锁颜色，兼容旧字段 gcol。</summary>
        public int GetContainerColor() => container ? containerColor : gcol;

        /// <summary>返回清空后锁定状态，兼容旧字段 fence。</summary>
        public bool LocksAfterClear => lockAfterClear || fence;
    }

    [Serializable]
    public class LevelCars
    {
        public List<CarSpec> cars = new List<CarSpec>();
    }

    /// <summary>底部发车道。cars[0] = 最前面（可点击）。</summary>
    [Serializable]
    public class LaneSpec
    {
        public List<CarSpec> cars = new List<CarSpec>();
        /// <summary>车库生成器：从第 gi 辆（队首为 0）开始的 gn 辆车藏在车库里，本车道每发一辆吐出一辆。</summary>
        public int gi;
        public int gn;
    }

    [Serializable]
    public class LevelData
    {
        public int id;
        // Per-level board layout authored by the runtime layout tool.
        public bool boardLayoutEnabled;
        public float boardScale = 1f;
        public Vector2 boardPosition;
        // Legacy shared spacing value kept for levels saved before the split controls.
        public float vehicleSpacing = 1f;
        // Enables separate upper parking and lower departure spacing values.
        public bool spacingSplitEnabled;
        public float parkingSpacing = 1f;
        public float departureSpacing = 1f;
        /// <summary>0 普通 1 困难 2 超难</summary>
        public int difficulty;
        /// <summary>提取数据中的容量 cp，作为关卡元数据保留。</summary>
        public int capacity = 4;
        /// <summary>提取关卡的实际列深度。缺省为 capacity，兼容项目旧关卡。</summary>
        public int slotCapacity;
        /// <summary>环路最多容纳的车数。</summary>
        public int loopLimit = 5;
        public List<ColumnSpec> columns = new List<ColumnSpec>();
        public List<LaneSpec> lanes = new List<LaneSpec>();
        /// <summary>生成器统计：普通玩家策略的通关率（仅供参考）。</summary>
        public float winRate;
        public int seed;

        public LevelData Clone() => JsonUtility.FromJson<LevelData>(JsonUtility.ToJson(this));

        public int TotalCars()
        {
            int n = 0;
            foreach (var c in columns) { n += c.cars.Count; foreach (var lv in c.levels) n += lv.cars.Count; }
            foreach (var l in lanes) n += l.cars.Count;
            return n;
        }

        /// <summary>返回当前关卡用于停车列布局和物理满位判断的实际列深度。</summary>
        public int EffectiveCapacity => slotCapacity > 0 ? slotCapacity : capacity;

        public string Validate()
        {
            if (capacity < 2) return "capacity < 2";
            if (EffectiveCapacity < capacity) return "slotCapacity < capacity";
            if (columns.Count == 0) return "no columns";
            var cnt = new Dictionary<int, int>();
            void Add(CarSpec s) { cnt.TryGetValue(s.c, out var v); cnt[s.c] = v + 1; }
            foreach (var c in columns)
            {
                if (c.cars.Count > EffectiveCapacity) return "column over capacity";
                c.cars.ForEach(Add);
                foreach (var lv in c.levels) lv.cars.ForEach(Add);
            }
            foreach (var l in lanes) l.cars.ForEach(Add);
            var links = new Dictionary<int, List<int>>();
            for (int li = 0; li < lanes.Count; li++)
                foreach (var s in lanes[li].cars)
                    if (s.l != 0)
                    {
                        if (!links.TryGetValue(s.l, out var ls)) links[s.l] = ls = new List<int>();
                        ls.Add(li);
                    }
            int keyCount = 0, lockCount = 0;
            foreach (var c in columns) foreach (var s in c.cars) if (s.k) keyCount++;
            foreach (var l in lanes) foreach (var s in l.cars) if (s.lk) lockCount++;
            if (lockCount > keyCount) return $"locks {lockCount} > keys {keyCount}";
            foreach (var kv in links)
                if (kv.Value.Count != 2 || kv.Value[0] == kv.Value[1]) return $"link {kv.Key} invalid";
            // Extracted levels can start with one extra visible car per column,
            // so their total color counts are not required to be divisible by cp.
            if (slotCapacity <= 0)
                foreach (var kv in cnt)
                    if (kv.Value % capacity != 0) return $"color {kv.Key} count {kv.Value} not multiple of {capacity}";
            return null;
        }
    }
}
