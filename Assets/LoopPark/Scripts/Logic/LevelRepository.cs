using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    /// <summary>关卡来源：优先读取 Resources/Levels/level_XXXX.json（可手工编辑），否则运行时按关号生成。</summary>
    public static class LevelRepository
    {
        public const int MaxLevel = 60;
        static readonly Dictionary<int, LevelData> cache = new Dictionary<int, LevelData>();

        public static string ResourcePath(int n) => $"Levels/level_{n:D4}";

        public static LevelData Get(int n)
        {
            if (n < 1 || n > MaxLevel) throw new ArgumentOutOfRangeException(nameof(n));
            if (cache.TryGetValue(n, out var d)) return d.Clone();
            var ta = Resources.Load<TextAsset>(ResourcePath(n));
            if (ta != null)
            {
                d = JsonUtility.FromJson<LevelData>(ta.text);
                d.id = n;
            }
            else
            {
                d = LevelGenerator.Generate(n, 80, 30);
            }
            cache[n] = d;
            return d.Clone();
        }
    }
}
