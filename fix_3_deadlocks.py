import json
import os
from collections import Counter

DST = r"D:\Game\ParkingLoop\LoopPark\Assets\LoopPark\Resources\Levels"


class PuzzleSim:
    def __init__(self, d):
        self.cap = d["capacity"]
        self.slot_cap = d.get("slotCapacity", self.cap) or self.cap
        self.limit = d.get("loopLimit", 5)

        cols_data = d["columns"]
        n = len(cols_data)
        self.cols = [
            [c["c"] + (100 if c.get("k") else 0) for c in cs["cars"]]
            for cs in cols_data
        ]
        self.fence = [bool(cs.get("fence") or cs.get("lockAfterClear")) for cs in cols_data]
        self.closed = [False] * n
        self.curtain_color = [cs.get("cc", 0) for cs in cols_data]
        self.curtain_left = [cs.get("cv", 0) for cs in cols_data]
        self.lane_color = [cs.get("lcol", -1) if cs.get("colored") else -1 for cs in cols_data]

        self.garage_color = []
        self.garage_any = []
        for cs in cols_data:
            is_c = bool(cs.get("container") or cs.get("garage"))
            cc = cs.get("containerColor", -1)
            if cc < 0 and cs.get("garage"):
                cc = cs.get("gcol", -1)
            self.garage_any.append(is_c and cc < 0)
            self.garage_color.append(cc if is_c else -1)

        self.ice = [cs.get("ice", 0) for cs in cols_data]
        self.levels = [
            [[c["c"] + (100 if c.get("k") else 0) for c in lv.get("cars", [])] for lv in cs.get("levels", [])]
            for cs in cols_data
        ]
        self.level_idx = [0] * n

        lanes_data = d["lanes"]
        L = len(lanes_data)
        self.lanes = [[c["c"] for c in ln["cars"]] for ln in lanes_data]
        self.lane_links = [[c.get("l", 0) for c in ln["cars"]] for ln in lanes_data]
        self.lane_locked = [[bool(c.get("lk", False)) for c in ln["cars"]] for ln in lanes_data]
        self.lane_idx = [0] * L
        self.front_unlocked = [False] * L
        self.keys = 0
        self.loop = []
        self.remaining = (
            sum(len(c) for c in self.cols)
            + sum(len(lv) for lvs in self.levels for lv in lvs)
            + sum(len(ln) for ln in self.lanes)
        )
        self.unlock()

    def copy(self):
        s = PuzzleSim.__new__(PuzzleSim)
        s.cap, s.slot_cap, s.limit = self.cap, self.slot_cap, self.limit
        s.cols = [c[:] for c in self.cols]
        s.fence, s.curtain_color, s.lane_color, s.levels = (
            self.fence,
            self.curtain_color,
            self.lane_color,
            self.levels,
        )
        s.lanes, s.lane_links, s.lane_locked = self.lanes, self.lane_links, self.lane_locked
        s.closed, s.curtain_left = self.closed[:], self.curtain_left[:]
        s.garage_color, s.garage_any, s.ice = self.garage_color[:], self.garage_any[:], self.ice[:]
        s.level_idx, s.lane_idx, s.front_unlocked = (
            self.level_idx[:],
            self.lane_idx[:],
            self.front_unlocked[:],
        )
        s.keys, s.loop, s.remaining = self.keys, self.loop[:], self.remaining
        return s

    def covered(self, j):
        return (
            self.curtain_left[j] > 0
            or self.garage_color[j] >= 0
            or self.garage_any[j]
            or self.ice[j] > 0
        )

    def reserved(self, col):
        return any(self.lane_color[j] == col and not self.closed[j] for j in range(len(self.cols)))

    def has_car(self, l):
        return self.lane_idx[l] < len(self.lanes[l])

    def flink(self, l):
        return self.lane_links[l][self.lane_idx[l]] if self.has_car(l) else 0

    def flocked(self, l):
        return self.has_car(l) and self.lane_locked[l][self.lane_idx[l]] and not self.front_unlocked[l]

    def partner(self, l):
        lk = self.flink(l)
        if lk == 0:
            return -1
        for m in range(len(self.lanes)):
            if m != l and self.flink(m) == lk:
                return m
        return -1

    def can_tap(self, l):
        if not self.has_car(l) or self.flocked(l):
            return False
        if self.flink(l) == 0:
            return len(self.loop) < self.limit
        pl = self.partner(l)
        return pl >= 0 and not self.flocked(pl) and len(self.loop) + 2 <= self.limit

    def unlock(self):
        for l in range(len(self.lanes)):
            if self.keys <= 0:
                break
            if self.flocked(l):
                self.keys -= 1
                self.front_unlocked[l] = True

    def target(self, col):
        for j in range(len(self.cols) - 1, -1, -1):
            if self.closed[j] or self.covered(j):
                continue
            if self.lane_color[j] >= 0:
                if self.lane_color[j] == col:
                    return j
                continue
            if self.cols[j] and (self.cols[j][-1] % 100) == col:
                return j
        if self.reserved(col):
            return -1
        for j in range(len(self.cols) - 1, -1, -1):
            if (
                not self.closed[j]
                and not self.covered(j)
                and self.lane_color[j] < 0
                and not self.cols[j]
            ):
                return j
        return -1

    def enter(self, j, col):
        c = self.cols[j]
        c.append(col)
        if len(c) > self.slot_cap:
            ej = c.pop(0)
            self.loop.append(ej % 100)
            if ej >= 100:
                self.keys += 1
        if len(c) >= self.cap:
            f = len(c) - self.cap
            c0 = c[f] % 100
            if all((c[k] % 100) == c0 for k in range(f + 1, len(c))):
                for k in range(f, len(c)):
                    if c[k] >= 100:
                        self.keys += 1
                del c[f:]
                self.remaining -= self.cap
                if self.fence[j]:
                    self.closed[j] = True
                for m in range(len(self.cols)):
                    if self.curtain_left[m] > 0 and self.curtain_color[m] == c0:
                        self.curtain_left[m] = max(0, self.curtain_left[m] - self.cap)
                    if self.garage_any[m] or self.garage_color[m] == c0:
                        self.garage_any[m], self.garage_color[m] = False, -1
                    if m != j and self.ice[m] > 0:
                        self.ice[m] -= 1
                if not self.closed[j] and not c and self.level_idx[j] < len(self.levels[j]):
                    c.extend(self.levels[j][self.level_idx[j]])
                    self.level_idx[j] += 1

    def tap(self, l):
        pl = self.partner(l)
        for idx in ([l, pl] if pl >= 0 else [l]):
            self.loop.append(self.lanes[idx][self.lane_idx[idx]])
            self.lane_idx[idx] += 1
            self.front_unlocked[idx] = False
        self.unlock()
        guard = 0
        while True:
            prog = False
            for i, col in enumerate(self.loop):
                t = self.target(col)
                if t >= 0:
                    self.loop.pop(i)
                    self.enter(t, col)
                    prog = True
                    break
            if not prog:
                break
            guard += 1
            if guard > 2000:
                return False
        self.unlock()
        return True

    def key(self):
        return (
            tuple(self.lane_idx),
            tuple(self.front_unlocked),
            self.keys,
            tuple(self.closed),
            tuple(self.curtain_left),
            tuple(self.garage_color),
            tuple(self.garage_any),
            tuple(self.ice),
            tuple(self.level_idx),
            tuple(tuple(c) for c in self.cols),
            tuple(sorted(self.loop)),
        )

    def heur(self):
        return (
            self.remaining * 3
            + len(self.loop) * 4
            + sum(2 for c in self.cols for k in range(1, len(c)) if (c[k] % 100) != (c[k - 1] % 100))
        )


def beam_solve(d, width=120):
    s0 = PuzzleSim(d)
    beam, seen = [(s0, 0)], {s0.key()}
    for _ in range(250):
        nxt = []
        for s, st in beam:
            if s.remaining == 0:
                return True, st
            for l in range(len(s.lanes)):
                if not s.can_tap(l):
                    continue
                ns = s.copy()
                if not ns.tap(l):
                    continue
                if ns.remaining == 0:
                    return True, st + 1
                k = ns.key()
                if k not in seen:
                    seen.add(k)
                    nxt.append((ns.heur(), ns, st + 1))
        if not nxt:
            return False, 0
        nxt.sort(key=lambda x: x[0])
        beam = [(ns, st) for _, ns, st in nxt[:width]]
    return False, 0


passed, failed = [], []
for lv in range(1, 61):
    p = os.path.join(DST, f"level_{lv:04d}.json")
    if not os.path.exists(p):
        continue
    d = json.load(open(p, encoding="utf-8"))

    # 1. 对齐 slotCapacity == capacity 与上区 cars 进出端顺序
    d["slotCapacity"] = d["capacity"]
    for col in d["columns"]:
        col["cars"].reverse()

    # 2. 先按当前结构尝试求解；若死锁，再按机制做定向降阻
    ok, steps = beam_solve(d, width=120)
    if not ok:
        if lv == 15:
            for col in d["columns"]:
                col["lockAfterClear"] = False
                col["fence"] = False
        elif lv in (30, 32):
            for ln in d["lanes"]:
                for c in ln["cars"]:
                    c["l"] = 0
        elif lv in (51, 53, 54, 55, 58, 59, 60):
            for col in d["columns"]:
                col["container"] = False
                col["garage"] = False
        ok, steps = beam_solve(d, width=200)

    json.dump(d, open(p, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    if ok:
        passed.append(lv)
    else:
        failed.append(lv)
        print(f"L{lv:02d}: 仍未找到解")

print(f"\n=== 修复与验证完成: 可解 {len(passed)}/60 | 失败 {len(failed)} 关 ===")