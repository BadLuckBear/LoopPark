import json
from pathlib import Path
src=Path(r'D:/Game/ParkingLoop/Car Loop Parking Game/Assets/Resources/DecodedLevels')
for n in range(1,61):
 d=json.loads((src/f'Level_{n:04d}.json').read_text())
 cs=[]
 for s in d['pf']['sd']:
  cs += [e['c'] for e in s.get('se',[])]
 cs += [u['co'] for u in d['ui']['u']]
 if 4 in cs or 3 in cs or 6 in cs or 7 in cs or 9 in cs or 10 in cs or 11 in cs or 13 in cs:
  print(n, sorted(set(cs)), {c:cs.count(c) for c in sorted(set(cs))})
