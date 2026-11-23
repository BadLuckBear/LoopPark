using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 新手引导：
    ///  第 1 关强制引导前两次点击（手指 + 只允许点指定车道），然后讲解停车与消除；
    ///  之后按事件触发一次性提示（挤出、环路满、问号车、栅栏列、困难关）。
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        UIManager ui;
        GameController game;
        int step = -1;
        int guidedTaps;
        bool active;
        float handRefresh;

        public void Init(UIManager u, GameController g)
        {
            ui = u;
            game = g;
            game.OnEvent += OnGameEvent;
        }

        public void Stop()
        {
            active = false;
            step = -1;
            if (game != null) game.TutorialLane = -1;
            ui?.HideHand();
        }

        public void OnLevelStart(LevelData d)
        {
            Stop();
            if (d.id == 1)
            {
                active = true;
                step = 0;
                guidedTaps = 0;
                GuideNextTap();
                ui.ShowTip("Tap a car below to drive it onto the loop", 999);
                return;
            }
            if (d.id == 2) Once("rule_push", "Parking into a full column pushes the bottom car back onto the loop", 4.5f);
            bool hasHidden = false, hasFence = false;
            foreach (var c in d.columns)
            {
                if (c.fence) hasFence = true;
                foreach (var s in c.cars) if (s.h) hasHidden = true;
            }
            foreach (var l in d.lanes) foreach (var s in l.cars) if (s.h) hasHidden = true;
            bool hasKey = false, hasCurtain = false;
            foreach (var c in d.columns) { if (c.cv > 0) hasCurtain = true; foreach (var s in c.cars) if (s.k) hasKey = true; }
            if (hasKey && Once("key", "A key car leaving its column drops a key that unlocks the locked car at the front", 4.5f)) return;
            if (hasCurtain && Once("curtain", "Columns under a curtain open after clearing enough cars of the matching color", 4.5f)) return;
            bool hasColored = false, hasGarage = false, hasIce = false, hasElevator = false, hasGen = false;
            foreach (var c in d.columns)
            {
                if (c.colored) hasColored = true;
                if (c.garage) hasGarage = true;
                if (c.ice > 0) hasIce = true;
                if (c.levels.Count > 0) hasElevator = true;
            }
            foreach (var l in d.lanes) if (l.gn > 0) hasGen = true;
            if (hasGarage && Once("garage", "Garage: clear a column of the color matching the garage marker to open it", 4.5f)) return;
            if (hasColored && Once("colored", "A colored lane only takes cars of its color, and cars of that color never park in normal empty spots", 4.5f)) return;
            if (hasIce && Once("ice", "Ice spot: each cleared column removes 1 ice layer; parking needs zero ice", 4.5f)) return;
            if (hasElevator && Once("elevator", "Elevator spot: cars on the level below rise after it is cleared", 4.5f)) return;
            if (hasGen && Once("generator", "Every car that leaves the garage in a lane spawns another one", 4.5f)) return;
            bool hasLink = false;
            foreach (var l in d.lanes) foreach (var s in l.cars) if (s.l != 0) hasLink = true;
            if (hasLink && Once("link", "Two strapped cars must both reach the front of their lanes to depart together", 4.5f)) return;
            if (hasFence && Once("fence", "A column with a yellow-black fence can only be cleared once, then it closes", 4.5f)) return;
            if (hasHidden && Once("hidden", "Mystery car: color is revealed only at the ends of a column or at the front of the queue", 4.5f)) return;
            if (d.difficulty > 0 && Once("hard", "Hard level! Use the boosters below when stuck", 4f)) return;
        }

        bool Once(string id, string text, float sec)
        {
            if (SaveSystem.TipShown(id)) return false;
            SaveSystem.MarkTip(id);
            EventTrack.GuideOperate(id);
            ui.ShowTip(text, sec);
            return true;
        }

        void GuideNextTap()
        {
            int lane = game.SuggestLane();
            game.TutorialLane = lane;
            if (lane >= 0) ui.ShowHandAtWorld(game.LaneFrontWorld(lane));
        }

        void Update()
        {
            if (!active || game.Level == null) return;
            // 第 1 关：等待车辆停好再引导下一次点击
            if (step == 1)
            {
                handRefresh -= Time.deltaTime;
                if ((handRefresh <= 0 && game.LoopCount == 0) || handRefresh < -3.5f)
                {
                    step = 0;
                    GuideNextTap();
                    ui.ShowTip("Tap one more car to try", 999);
                }
            }
        }

        void OnGameEvent(string id, CarView car)
        {
            if (game.Level == null) return;
            if (active && game.Level.id == 1)
            {
                switch (id)
                {
                    case "tap" when step == 0:
                        guidedTaps++;
                        ui.HideHand();
                        game.TutorialLane = -1;
                        if (guidedTaps == 1)
                        {
                            ui.ShowTip("Cars drive along the loop and park automatically in a matching column at the top", 999);
                            step = 1;
                            handRefresh = 1.2f;
                            game.TutorialLane = 99; // 等待期间禁止点击
                        }
                        else
                        {
                            ui.ShowTip("Fill a column with one color to clear it!", 4f);
                            step = 2;
                            active = false;
                        }
                        break;
                }
            }

            switch (id)
            {
                case "push":
                    Once("rule_push", "A pushed-out car returns to the loop and keeps looking for a matching column", 3.5f);
                    break;
                case "lockblocked":
                    ui.ShowTip("A locked car needs a key: drive the key car out of its column", 2.5f);
                    break;
                case "linkblocked":
                    ui.ShowTip("Strapped cars wait until their partner also reaches the front of its lane", 2.5f);
                    break;
                case "loopfull":
                    Once("loopfull", $"The loop holds at most {game.LoopLimit} cars! If it fills and nothing can park, you lose", 4f);
                    break;
                case "clear":
                    if (Once("first_clear", "Great! Clear all cars to win", 3f)) { }
                    break;
            }
        }
    }
}
