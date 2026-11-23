import json, os
from collections import Counter

DST = r"D:\Game\ParkingLoop\LoopPark\Assets\LoopPark\Resources\Levels"

updated = 0
bad = []
for lv in range(1, 61):
    p = os.path.join(DST, f"level_{lv:04d}.json")
    d = json.load(open(p, encoding="utf-8"))
    cap = d["capacity"]
    max_col = max((len(c["cars"]) for c in d["columns"]), default=0)
    need_slot = max(cap, max_col) + 1

    if d.get("slotCapacity", 0) < need_slot:
        d["slotCapacity"] = need_slot
        json.dump(d, open(p, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
        updated += 1

    # 终检
    tot = Counter()
    errs = []
    for col in d["columns"]:
        if len(col["cars"]) >= d["slotCapacity"]:
            errs.append(f"满列({len(col['cars'])}/{d['slotCapacity']})")
        for c in col["cars"]: tot[c["c"]] += 1
    for lane in d["lanes"]:
        for c in lane["cars"]: tot[c["c"]] += 1
    bm = {k: v for k, v in tot.items() if v % cap != 0}
    if bm: errs.append(f"颜色非{cap}倍数:{bm}")
    if errs:
        bad.append(lv)
        print(f"L{lv:02d} 异常: {', '.join(errs)}")

print(f"已补齐 {updated} 关的 slotCapacity")
print(f"=== 前 60 关终检: 通过 {60 - len(bad)}/60 | 异常 {len(bad)} 关 ===")