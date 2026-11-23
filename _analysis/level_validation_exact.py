"""Validate source levels with the reconstructed LoopPuzzle rules.

The validator intentionally models the discrete state transitions used by
``RebuiltGameplay/LoopPuzzle.cs``. A search step is either sending one legal
lane car or advancing the road by one checkpoint with ``Tick``.
"""

from __future__ import annotations

import json
import sys
from functools import lru_cache
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Car:
    color: int
    slot_index: int = -1
    laps: int = 0


@dataclass(frozen=True)
class State:
    sent: tuple[bool, ...]
    slots: tuple[tuple[int, ...], ...]
    cars: tuple[Car, ...]
    status: str = "playing"


class Puzzle:
    """Small value-type simulation of the runtime LoopPuzzle behavior."""

    def __init__(self, source: dict):
        self.capacity = int(source["pf"].get("tl") or 5)
        self.units = tuple(source["ui"].get("u") or [])
        self.slot_count = len(source["pf"].get("sd") or [])
        self.slot_capacities = tuple(
            max(1, max(int(source["pf"].get("cp") or 0), len(slot.get("se") or [])))
            for slot in source["pf"].get("sd") or []
        )
        self.initial_slots = tuple(
            tuple(int(element.get("c", 0)) for element in slot.get("se") or [])
            for slot in source["pf"].get("sd") or []
        )

    def initial(self) -> State:
        return State(
            sent=tuple(False for _ in self.units),
            slots=self.initial_slots,
            cars=(),
        )

    def can_send(self, state: State, index: int, ignore_front: bool = False) -> bool:
        if state.status != "playing" or index < 0 or index >= len(self.units):
            return False
        if state.sent[index] or bool(self.units[index].get("l")):
            return False
        if len(state.cars) >= self.capacity:
            return False
        if ignore_front:
            return True
        position = self.units[index].get("p") or {}
        x = int(position.get("x", 0))
        y = int(position.get("y", 0))
        return not any(
            i != index
            and not state.sent[i]
            and int((self.units[i].get("p") or {}).get("x", 0)) == x
            and int((self.units[i].get("p") or {}).get("y", 0)) > y
            for i in range(len(self.units))
        )

    def send(self, state: State, index: int) -> State | None:
        if not self.can_send(state, index):
            return None
        sent = list(state.sent)
        sent[index] = True
        cars = state.cars + (Car(int(self.units[index].get("co", 0))),)
        return State(tuple(sent), state.slots, cars, state.status)

    def tick(self, state: State) -> State:
        if state.status != "playing":
            return state
        slots = [list(slot) for slot in state.slots]
        cars = list(state.cars)
        for index in range(len(cars) - 1, -1, -1):
            car = cars[index]
            car = Car(car.color, car.slot_index + 1, car.laps)
            if car.slot_index == self.slot_count:
                cars[index] = Car(car.color, -1, car.laps + 1)
                continue
            slot_index = self.slot_count - 1 - car.slot_index
            slot = slots[slot_index]
            if slot and slot[-1] != car.color:
                cars[index] = car
                continue
            if not slot and self.has_other_matching_slot(slots, slot_index, car.color):
                cars[index] = car
                continue

            was_full = len(slot) == self.slot_capacities[slot_index]
            slot.append(car.color)
            cars.pop(index)
            if was_full:
                evicted = slot.pop(0)
                cars.append(Car(evicted, -1, 0))
            if len(slot) == self.slot_capacities[slot_index] and all(color == slot[0] for color in slot):
                slot.clear()
            elif not was_full and len(slot) == self.slot_capacities[slot_index]:
                evicted = slot.pop(0)
                cars.append(Car(evicted, -1, 0))
            cars = [Car(rider.color, rider.slot_index, 0) for rider in cars]

        next_state = State(state.sent, tuple(tuple(slot) for slot in slots), tuple(cars), state.status)
        return self.with_result(next_state)

    @staticmethod
    def has_other_matching_slot(slots: list[list[int]], excluded: int, color: int) -> bool:
        return any(
            i != excluded and slot and slot[-1] == color
            for i, slot in enumerate(slots)
        )

    def with_result(self, state: State) -> State:
        if not state.cars and all(state.sent) and all(not slot for slot in state.slots):
            return State(state.sent, state.slots, state.cars, "won")
        if any(self.can_send(state, i) for i in range(len(self.units))):
            return state
        if not state.cars:
            return State(state.sent, state.slots, state.cars, "lost")
        if all(car.laps > 0 for car in state.cars):
            return State(state.sent, state.slots, state.cars, "lost")
        return state


def search(source: dict, max_states: int = 500_000) -> tuple[list[str] | None, int, str]:
    """Depth-first search with bounded consecutive road ticks.

    Once every car has passed a full loop without parking, another identical
    wait cannot create a new opportunity. The runtime marks that situation as
    a deadlock when no lane car can be sent, so the bound removes redundant
    timing cycles while preserving useful send/tick interleavings.
    """
    puzzle = Puzzle(source)
    start = puzzle.with_result(puzzle.initial())
    seen: set[State] = set()
    nodes = 0
    max_depth = max(64, len(puzzle.units) * (puzzle.slot_count + 3) * 4)

    def visit(state: State, path: list[str], consecutive_ticks: int) -> list[str] | None:
        nonlocal nodes
        nodes += 1
        if nodes > max_states or len(path) > max_depth:
            return None
        if state.status == "won":
            return path
        if state.status != "playing":
            return None
        key = (state, consecutive_ticks)
        if key in seen:
            return None
        seen.add(key)

        # Try sending the front-most lane cars before spending another road tick.
        for index in range(len(puzzle.units)):
            next_state = puzzle.send(state, index)
            if next_state is None:
                continue
            result = visit(next_state, path + [f"send:{index}"], 0)
            if result is not None:
                return result

        if consecutive_ticks < puzzle.slot_count + 1:
            next_state = puzzle.tick(state)
            result = visit(next_state, path + ["tick"], consecutive_ticks + 1)
            if result is not None:
                return result
        return None

    solution = visit(start, [], 0)
    reason = "won" if solution is not None else ("state-limit" if nodes > max_states else "exhausted")
    return solution, min(nodes, max_states + 1), reason


def main() -> int:
    source_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(
        r"D:/Game/ParkingLoop/Car Loop Parking Game/Assets/Resources/DecodedLevels"
    )
    count = int(sys.argv[2]) if len(sys.argv) > 2 else 60
    for level_id in range(1, count + 1):
        path = source_dir / f"Level_{level_id:04d}.json"
        source = json.loads(path.read_text(encoding="utf-8"))
        solution, states, result = search(source)
        steps = len(solution) if solution else 0
        print(f"level={level_id} result={result} states={states} steps={steps}")
        if solution:
            print("  " + " ".join(solution))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
