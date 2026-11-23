"""LoopPark 关卡转换 + 可解性校验工具（离线，不依赖 Unity）。

用途
----
1. 把原版游戏解码出来的关卡（Car Loop Parking Game/Assets/Resources/DecodedLevels/Level_XXXX.json）
   转换成 LoopPark 的 LevelData JSON（Assets/LoopPark/Resources/Levels/level_XXXX.json）。
2. 用和 Assets/LoopPark/Scripts/Logic/PuzzleSim.cs 完全一致的规则做结构校验与通关搜索，
   保证写进工程的关卡是可玩的。

原版字段语义（来自反编译模型）：
    LevelData.pf  = PlayfieldData：cp=消除所需同色车数，tl=列深（物理格数），sd[]=车列
    SlotData      ：d、t=1 表示清空后锁定，sot=[1]=集装箱/栅栏 2=彩色车道 3=冰 4=帘子，
                    lc=锁定颜色，ic、p=网格坐标，se[]=列内车（自顶向下），elid/elv=电梯
    SlotElementData：et、c=颜色(1..15)、h=问号车、k=带钥匙、coc=帘子色
    UnitInventoryData.u[] = 底部发车道：co=颜色、h=问号车、l=绑带标记、p=(x 车道, y 行)、cn=搭档位置
    ui.c[] = 车道顺序：o=order、uc=解锁数

转换规则：
    * 颜色统一 -1 归一化到 LoopPark Palette 的 0..14。
    * 列内 se 是自顶向下，LoopPark 的 cars[0] 是最下方（出口端），所以要倒序。
    * 车道内同一 p.x 的车按 p.y 从大到小（y 越大越靠后），p 缺省视为 (0,0)。
    * 绑带车：原版用 cn 指向搭档所在格子，转换时给配对的两辆车同一个递增编号 l。
"""

from __future__ import annotations

import glob
import json
import os
import sys
from collections import defaultdict

KEY_BIT = 100
LANE_ORDER_OFFSET = 0


# --------------------------------------------------------------------------
# 源数据读取
# --------------------------------------------------------------------------
def load_source(path: str) -> dict:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def _pos(node: dict) -> tuple[int, int]:
    p = node.get("p") or {}
    return int(p.get("x", 0)), int(p.get("y", 0))


def _slot_cars(slot: dict) -> list[dict]:
    """列内 se 自顶向下 → LoopPark 需要自底向上。"""
    cars = []
    for se in reversed(slot.get("se") or []):
        cars.append(
            {
                "c": int(se.get("c", 0)) - 1,
                "h": bool(se.get("h", False)),
                "l": 0,
                "k": bool(se.get("k", False)),
                "lk": False,
            }
        )
    return cars


def _obstacles(slot: dict) -> list[int]:
    return [int(x) for x in (slot.get("sot") or [])]


def build_links(units: list[dict]) -> dict[int, int]:
    """把原版 cn（搭档所在格子）配对成 l 编号；返回 id(unit) → 编号。"""
    index = {}
    for i, u in enumerate(units):
        index[_pos(u)] = i
    links: dict[int, int] = {}
    next_id = 1
    for i, u in enumerate(units):
        if i in links:
            continue
        cn = u.get("cn") or {}
        cx, cy = int(cn.get("x", -1)), int(cn.get("y", 0))
        if cx < 0:
            continue
        j = index.get((cx, cy))
        if j is None or j == i:
            continue
        # 原版既可能双向记录，也可能只记录一次；两种都收敛成一对。
        links[i] = next_id
        links[j] = next_id
        next_id += 1
    return links


def convert_level(src: dict, level_id: int) -> dict:
    pf = src.get("pf") or {}
    ui = src.get("ui") or {}
    columns_src = sorted(pf.get("sd") or [], key=lambda s: (_pos(s)[1], _pos(s)[0]))

    columns = []
    for slot in columns_src:
        sot = _obstacles(slot)
        lock_after_clear = int(slot.get("t", 0)) == 1
        lock_color_src = int(slot.get("lc", 0))
        curtain = slot.get("cd") or {}
        elevator = []
        for floor in slot.get("elv") or []:
            elevator.append({"cars": _slot_cars(floor)})
        columns.append(
            {
                "fence": False,
                "lockAfterClear": lock_after_clear,
                "container": 1 in sot,
                # 原版集装箱没有单独颜色字段，lc 是锁定列的颜色提示。
                "containerColor": -1,
                "lockColor": lock_color_src - 1 if lock_after_clear and lock_color_src > 0 else -1,
                "cv": int(curtain.get("v", 0)),
                "cc": int(curtain.get("coc", 0)) - 1 if curtain.get("coc") else 0,
                "colored": 2 in sot,
                "lcol": int(slot.get("lc", 0)) - 1 if 2 in sot and slot.get("lc") else -1,
                "garage": False,
                "gcol": 0,
                "ice": int(slot.get("ic", 0)) if 3 in sot else 0,
                "levels": elevator,
                "cars": _slot_cars(slot),
            }
        )

    units = list(ui.get("u") or [])
    links = build_links(units)
    by_lane: dict[int, list[tuple[int, dict]]] = defaultdict(list)
    for i, u in enumerate(units):
        by_lane[_pos(u)[0]].append((i, u))

    order = {int(c.get("o", i)): i for i, c in enumerate(ui.get("c") or [])}
    lane_keys = sorted(by_lane.keys(), key=lambda x: (order.get(x, x), x))

    lanes = []
    for x in lane_keys:
        items = by_lane[x]
        items.sort(key=lambda item: -_pos(item[1])[1])
        cars = []
        for i, u in items:
            cars.append(
                {
                    "c": int(u.get("co", 0)) - 1,
                    "h": bool(u.get("h", False)),
                    "l": links.get(i, 0),
                    "k": False,
                    "lk": bool(u.get("l", False)),
                }
            )
        lanes.append({"cars": cars, "gi": 0, "gn": 0})

    return {
        "id": level_id,
        "difficulty": int(src.get("ld", 0)),
        "capacity": int(pf.get("cp", 4)),
        "slotCapacity": int(pf.get("tl", pf.get("cp", 4))),
        "loopLimit": 5,
        "columns": columns,
        "lanes": lanes,
        "winRate": 1.0,
        "seed": 0,
    }


# --------------------------------------------------------------------------
# PuzzleSim 的 Python 复刻（规则必须与 C# 保持一致）
# --------------------------------------------------------------------------
class Sim:
    def __init__(self, d: dict):
        self.cap = int(d["capacity"])
        self.slot_cap = int(d.get("slotCapacity") or d["capacity"])
        self.limit = int(d.get("loopLimit", 5))
        cols = d["columns"]
        n = len(cols)
        self.cols = [[car["c"] + (KEY_BIT if car.get("k") else 0) for car in c["cars"]] for c in cols]
        self.fence = [bool(c.get("lockAfterClear") or c.get("fence")) for c in cols]
        self.closed = [False] * n
        self.curtain_color = [int(c.get("cc", 0)) for c in cols]
        self.curtain_left = [int(c.get("cv", 0)) for c in cols]
        self.lane_color = [int(c.get("lcol", -1)) if c.get("colored") else -1 for c in cols]
        is_container = [bool(c.get("container") or c.get("garage")) for c in cols]
        container_color = [int(c.get("containerColor", -1)) if c.get("container") else int(c.get("gcol", 0)) for c in cols]
        self.garage_any = [is_container[i] and container_color[i] < 0 for i in range(n)]
        self.garage_color = [container_color[i] if is_container[i] else -1 for i in range(n)]
        self.ice = [int(c.get("ice", 0)) for c in cols]
        self.levels = [[[car["c"] + (KEY_BIT if car.get("k") else 0) for car in lv["cars"]] for lv in c.get("levels", [])] for c in cols]
        self.level_idx = [0] * n
        lanes = d["lanes"]
        self.lanes = [[car["c"] for car in l["cars"]] for l in lanes]
        self.links = [[int(car.get("l", 0)) for car in l["cars"]] for l in lanes]
        self.locked = [[bool(car.get("lk", False)) for car in l["cars"]] for l in lanes]
        self.idx = [0] * len(lanes)
        self.front_unlocked = [False] * len(lanes)
        self.keys = 0
        self.loop: list[int] = []
        self.remaining = total_cars(d)
        self.clears = 0
        self.unlock()

    def copy(self) -> "Sim":
        s = Sim.__new__(Sim)
        s.__dict__ = {k: (v[:] if isinstance(v, list) else v) for k, v in self.__dict__.items()}
        s.cols = [c[:] for c in self.cols]
        s.closed = self.closed[:]
        s.curtain_left = self.curtain_left[:]
        s.garage_color = self.garage_color[:]
        s.garage_any = self.garage_any[:]
        s.ice = self.ice[:]
        s.level_idx = self.level_idx[:]
        s.idx = self.idx[:]
        s.front_unlocked = self.front_unlocked[:]
        s.levels = [[c[:] for c in lv] for lv in self.levels]
        s.loop = self.loop[:]
        return s

    # ---- 查询 ----
    @property
    def won(self) -> bool:
        return self.remaining == 0

    def covered(self, j: int) -> bool:
        return self.curtain_left[j] > 0 or self.garage_color[j] >= 0 or self.garage_any[j] or self.ice[j] > 0

    def reserved(self, color: int) -> bool:
        return any(self.lane_color[j] == color and not self.closed[j] for j in range(len(self.cols)))

    def lane_has_car(self, l: int) -> bool:
        return self.idx[l] < len(self.lanes[l])

    def lane_front(self, l: int) -> int:
        return self.lanes[l][self.idx[l]] if self.lane_has_car(l) else -1

    def front_link(self, l: int) -> int:
        return self.links[l][self.idx[l]] if self.lane_has_car(l) else 0

    def front_locked(self, l: int) -> bool:
        return self.lane_has_car(l) and self.locked[l][self.idx[l]] and not self.front_unlocked[l]

    def partner_lane(self, l: int) -> int:
        lk = self.front_link(l)
        if lk == 0:
            return -1
        for m in range(len(self.lanes)):
            if m != l and self.front_link(m) == lk:
                return m
        return -1

    def can_tap(self, l: int) -> bool:
        if not self.lane_has_car(l) or self.front_locked(l):
            return False
        if self.front_link(l) == 0:
            return len(self.loop) < self.limit
        pl = self.partner_lane(l)
        return pl >= 0 and not self.front_locked(pl) and len(self.loop) + 2 <= self.limit

    # ---- 操作 ----
    def advance(self, l: int) -> None:
        self.loop.append(self.lanes[l][self.idx[l]])
        self.idx[l] += 1
        self.front_unlocked[l] = False

    def tap_raw(self, l: int) -> None:
        pl = self.partner_lane(l)
        self.advance(l)
        if pl >= 0:
            self.advance(pl)
        self.unlock()

    def unlock(self) -> None:
        for l in range(len(self.lanes)):
            if self.keys <= 0:
                break
            if self.front_locked(l):
                self.keys -= 1
                self.front_unlocked[l] = True

    def any_tap(self) -> bool:
        return any(self.can_tap(l) for l in range(len(self.lanes)))

    def target(self, color: int) -> int:
        for j in range(len(self.cols) - 1, -1, -1):
            if self.closed[j] or self.covered(j):
                continue
            if self.lane_color[j] >= 0:
                if self.lane_color[j] == color:
                    return j
                continue
            c = self.cols[j]
            if c and c[-1] % KEY_BIT == color:
                return j
        if self.reserved(color):
            return -1
        for j in range(len(self.cols) - 1, -1, -1):
            if not self.closed[j] and not self.covered(j) and self.lane_color[j] < 0 and not self.cols[j]:
                return j
        return -1

    @property
    def stuck(self) -> bool:
        return not self.won and not self.any_tap()

    def tap(self, l: int) -> None:
        self.tap_raw(l)
        self.resolve()

    def resolve(self) -> bool:
        guard = 0
        progress = True
        while progress:
            progress = False
            for i in range(len(self.loop)):
                t = self.target(self.loop[i])
                if t < 0:
                    continue
                color = self.loop.pop(i)
                self.enter(t, color)
                progress = True
                break
            guard += 1
            if guard > 2000:
                return False
        self.unlock()
        return True

    def enter(self, j: int, color: int) -> None:
        c = self.cols[j]
        c.append(color)
        if len(c) > self.slot_cap:
            ej = c.pop(0)
            self.loop.append(ej % KEY_BIT)
            if ej >= KEY_BIT:
                self.keys += 1
        if len(c) >= self.cap:
            first = len(c) - self.cap
            c0 = c[first] % KEY_BIT
            if all(x % KEY_BIT == c0 for x in c[first:]):
                for x in c[first:]:
                    if x >= KEY_BIT:
                        self.keys += 1
                del c[first:]
                self.remaining -= self.cap
                self.clears += 1
                if self.fence[j]:
                    self.closed[j] = True
                for m in range(len(self.cols)):
                    if self.curtain_left[m] > 0 and self.curtain_color[m] == c0:
                        self.curtain_left[m] = max(0, self.curtain_left[m] - self.cap)
                    if self.garage_any[m] or self.garage_color[m] == c0:
                        self.garage_any[m] = False
                        self.garage_color[m] = -1
                    if m != j and self.ice[m] > 0:
                        self.ice[m] -= 1
                if not self.closed[j] and not c and self.level_idx[j] < len(self.levels[j]):
                    c.extend(self.levels[j][self.level_idx[j]])
                    self.level_idx[j] += 1

    def key(self) -> str:
        parts = []
        for l in range(len(self.lanes)):
            parts.append(f"{self.idx[l]}{'u' if self.front_unlocked[l] else ','}")
        parts.append(f"|{self.keys}|")
        for j in range(len(self.cols)):
            seg = []
            if self.closed[j]:
                seg.append("X")
            if self.curtain_left[j] > 0:
                seg.append(f"C{self.curtain_left[j]}")
            if self.garage_color[j] >= 0 or self.garage_any[j]:
                seg.append("G")
            if self.ice[j] > 0:
                seg.append(f"I{self.ice[j]}")
            if self.level_idx[j] > 0:
                seg.append(f"E{self.level_idx[j]}")
            seg.extend(f"{chr(97 + (x % KEY_BIT))}{'k' if x >= KEY_BIT else ''}" for x in self.cols[j])
            parts.append("".join(seg) + "/")
        parts.append("".join(chr(97 + c) for c in sorted(self.loop)))
        return "".join(parts)

    def heuristic(self) -> float:
        h = self.remaining * 3.0 + len(self.loop) * 4.0
        for j, c in enumerate(self.cols):
            for k in range(1, len(c)):
                if c[k] % KEY_BIT != c[k - 1] % KEY_BIT:
                    h += 2.0
            h += self.curtain_left[j] * 0.5 + self.ice[j] * 0.5 + (3.0 if self.garage_color[j] >= 0 else 0.0)
        for l in range(len(self.lanes)):
            if self.front_locked(l):
                h += 6.0
        return h


def total_cars(d: dict) -> int:
    n = 0
    for c in d["columns"]:
        n += len(c["cars"]) + sum(len(lv["cars"]) for lv in c.get("levels", []))
    for l in d["lanes"]:
        n += len(l["cars"])
    return n


def beam_solve(d: dict, width: int = 200, max_nodes: int = 200_000):
    """束搜索：返回点击序列或 None。规则与 PuzzleSim.BeamSolve 相同。"""
    start = Sim(d)
    beam = [(start, [])]
    seen = set()
    expanded = 0
    max_depth = sum(len(l) for l in start.lanes)
    for _ in range(max_depth + 1):
        nxt = []
        for s, path in beam:
            for l in range(len(s.lanes)):
                if not s.can_tap(l):
                    continue
                c = s.copy()
                c.tap_raw(l)
                if not c.resolve():
                    continue
                expanded += 1
                np = path + [l]
                if c.won:
                    return np, expanded
                if not c.any_tap():
                    continue
                k = c.key()
                if k in seen:
                    continue
                seen.add(k)
                nxt.append((c.heuristic(), c, np))
                if expanded > max_nodes:
                    return None, expanded
        if not nxt:
            return None, expanded
        nxt.sort(key=lambda x: x[0])
        beam = [(s, p) for _, s, p in nxt[:width]]
    return None, expanded


# --------------------------------------------------------------------------
# 结构校验（对应 LevelData.Validate）
# --------------------------------------------------------------------------
def validate(d: dict) -> str | None:
    if d["capacity"] < 2:
        return "capacity < 2"
    slot = d.get("slotCapacity") or d["capacity"]
    if slot < d["capacity"]:
        return "slotCapacity < capacity"
    if not d["columns"]:
        return "no columns"
    if any(len(c["cars"]) > slot for c in d["columns"]):
        return "column over capacity"
    key_count = sum(1 for c in d["columns"] for car in c["cars"] if car.get("k"))
    lock_count = sum(1 for l in d["lanes"] for car in l["cars"] if car.get("lk"))
    if lock_count > key_count:
        return f"locks {lock_count} > keys {key_count}"
    links: dict[int, list[int]] = defaultdict(list)
    for li, lane in enumerate(d["lanes"]):
        for car in lane["cars"]:
            if car.get("l"):
                links[car["l"]].append(li)
    for link_id, lanes in links.items():
        if len(lanes) != 2 or lanes[0] == lanes[1]:
            return f"link {link_id} invalid"
    return None


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------
def main(argv: list[str]) -> int:
    default_src = r"D:/Game/ParkingLoop/Car Loop Parking Game/Assets/Resources/DecodedLevels"
    default_dst = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                              "Assets", "LoopPark", "Resources", "Levels")
    src_dir = argv[1] if len(argv) > 1 else default_src
    dst_dir = argv[2] if len(argv) > 2 else default_dst
    count = int(argv[3]) if len(argv) > 3 else 60
    write = "--write" in argv

    problems = []
    for n in range(1, count + 1):
        src_path = os.path.join(src_dir, f"Level_{n:04d}.json")
        if not os.path.exists(src_path):
            problems.append((n, f"missing source {src_path}", None))
            continue
        data = convert_level(load_source(src_path), n)
        err = validate(data)
        if err:
            problems.append((n, "validate: " + err, None))
        if write:
            with open(os.path.join(dst_dir, f"level_{n:04d}.json"), "w", encoding="utf-8", newline="\n") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
                f.write("\n")
    print(f"converted {count} levels -> {dst_dir} (write={write})")
    print(f"structural problems: {len(problems)}")
    for p in problems[:20]:
        print(" ", p)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
