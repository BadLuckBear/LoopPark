import json
from collections import Counter
from pathlib import Path

src = Path(r"D:/Game/ParkingLoop/Car Loop Parking Game/Assets/Resources/DecodedLevels")

for n in range(1, 61):
    path = src / f"Level_{n:04d}.json"
    if not path.exists():
        print(f"{n:02d} missing")
        continue
    data = json.loads(path.read_text(encoding="utf-8"))
    pf = data.get("pf") or {}
    slots = pf.get("sd") or []
    units = (data.get("ui") or {}).get("u") or []
    counts = Counter()
    for slot in slots:
        for car in slot.get("se") or []:
            counts[int(car.get("c", 0))] += 1
    for unit in units:
        counts[int(unit.get("co", 0))] += 1
    capacities = [max(int(pf.get("cp", 0)), len(slot.get("se") or []), 1) for slot in slots]
    cp = int(pf.get("cp", 0))
    tl = int(pf.get("tl", 0) or 5)
    divisible = all(v % cap == 0 for v in counts.values() for cap in set(capacities)) if capacities else False
    print(f"{n:02d} cp={cp} tl={tl} slots={len(slots)} units={len(units)} cars={sum(counts.values())} caps={capacities} colors={dict(sorted(counts.items()))} divisible_by_slot={divisible}")
