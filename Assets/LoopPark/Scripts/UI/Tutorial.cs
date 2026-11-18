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
                ui.ShowTip("点击下方的车辆，把它开上环路", 999);
                return;
            }
            if (d.id == 2) Once("rule_push", "停进已满的车列时，最下面的车会被挤回环路", 4.5f);
            bool hasHidden = false, hasFence = false;
            foreach (var c in d.columns)
            {
                if (c.fence) hasFence = true;
                foreach (var s in c.cars) if (s.h) hasHidden = true;
            }
            foreach (var l in d.lanes) foreach (var s in l.cars) if (s.h) hasHidden = true;
            bool hasKey = false, hasCurtain = false;
            foreach (var c in d.columns) { if (c.cv > 0) hasCurtain = true; foreach (var s in c.cars) if (s.k) hasKey = true; }
            if (hasKey && Once("key", "带钥匙的车离开车列时会掉出钥匙，钥匙能打开队首上锁的车", 4.5f)) return;
            if (hasCurtain && Once("curtain", "帘子下的车列要消除足够数量的对应颜色车辆才会打开", 4.5f)) return;
            bool hasColored = false, hasGarage = false, hasIce = false, hasElevator = false, hasGen = false;
            foreach (var c in d.columns)
            {
                if (c.colored) hasColored = true;
                if (c.garage) hasGarage = true;
                if (c.ice > 0) hasIce = true;
                if (c.levels.Count > 0) hasElevator = true;
            }
            foreach (var l in d.lanes) if (l.gn > 0) hasGen = true;
            if (hasGarage && Once("garage", "车库：消除一列与车库标记同色的车，车库就会打开", 4.5f)) return;
            if (hasColored && Once("colored", "彩色车道只停对应颜色的车，这种颜色的车也不会停进普通空车位", 4.5f)) return;
            if (hasIce && Once("ice", "冰冻车位：每消除一列，冰层减 1，冰层归零才能停车", 4.5f)) return;
            if (hasElevator && Once("elevator", "电梯车位：清空后下面一层的车会升上来", 4.5f)) return;
            if (hasGen && Once("generator", "车道里的车库每发出一辆车，就会再开出一辆", 4.5f)) return;
            bool hasLink = false;
            foreach (var l in d.lanes) foreach (var s in l.cars) if (s.l != 0) hasLink = true;
            if (hasLink && Once("link", "绑在一起的两辆车必须同时排到队首，才能一起发车", 4.5f)) return;
            if (hasFence && Once("fence", "带黄黑栅栏的车列只能消除一次，之后会关闭", 4.5f)) return;
            if (hasHidden && Once("hidden", "问号车：移动到车列两端或排到队首时才会显示颜色", 4.5f)) return;
            if (d.difficulty > 0 && Once("hard", "困难关卡！卡住时可以使用下方的道具", 4f)) return;
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
                    ui.ShowTip("再点一辆车试试", 999);
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
                            ui.ShowTip("车辆沿环路行驶，自动停进顶部同色的车列", 999);
                            step = 1;
                            handRefresh = 1.2f;
                            game.TutorialLane = 99; // 等待期间禁止点击
                        }
                        else
                        {
                            ui.ShowTip("把一列停满同一种颜色就能消除！", 4f);
                            step = 2;
                            active = false;
                        }
                        break;
                }
            }

            switch (id)
            {
                case "push":
                    Once("rule_push", "被挤出的车会回到环路，继续寻找同色车列", 3.5f);
                    break;
                case "lockblocked":
                    ui.ShowTip("上锁的车需要钥匙：让带钥匙的车离开车列", 2.5f);
                    break;
                case "linkblocked":
                    ui.ShowTip("绑在一起的车要等搭档也排到队首才能出发", 2.5f);
                    break;
                case "loopfull":
                    Once("loopfull", $"环路最多容纳 {game.LoopLimit} 辆车！满了又没车能停就会失败", 4f);
                    break;
                case "clear":
                    if (Once("first_clear", "太棒了！消除所有车辆即可过关", 3f)) { }
                    break;
            }
        }
    }
}
