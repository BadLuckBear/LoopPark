using System.IO;
using UnityEditor;
using UnityEngine;

namespace LoopPark.EditorTools
{
    /// <summary>
    /// Edits the active level board while the game is running and persists the result to its JSON resource.
    /// </summary>
    public sealed class BoardLayoutWindow : EditorWindow
    {
        float scale = 1f;
        Vector2 position;
        float parkingSpacing = 1f;
        float departureSpacing = 1f;
        int selectedLevel = 1;

        [MenuItem("LoopPark/玩法区域布局（运行时）")]
        public static void Open() => GetWindow<BoardLayoutWindow>("玩法区域布局");

        void OnGUI()
        {
            var game = GameController.I;
            if (!Application.isPlaying || game == null || game.Level == null)
            {
                EditorGUILayout.HelpBox("请先运行游戏并进入一个关卡。", MessageType.Info);
                return;
            }

            selectedLevel = EditorGUILayout.IntSlider(new GUIContent("编辑关卡"), selectedLevel, 1, LevelRepository.MaxLevel);
            if (GUILayout.Button("加载并编辑该关卡"))
            {
                if (UIManager.I != null) UIManager.I.StartLevel(selectedLevel);
                else game.StartLevel(LevelRepository.Get(selectedLevel));
                game = GameController.I;
                scale = game.BoardScale;
                position = game.BoardPosition;
                parkingSpacing = game.ParkingSpacing;
                departureSpacing = game.DepartureSpacing;
            }

            if (GUILayout.Button("读取当前关卡布局"))
            {
                selectedLevel = game.Level.id;
                scale = game.BoardScale;
                position = game.BoardPosition;
                parkingSpacing = game.ParkingSpacing;
                departureSpacing = game.DepartureSpacing;
            }

            scale = EditorGUILayout.Slider(new GUIContent("区域缩放", "调整玩法区域整体尺寸。"), scale, 0.5f, 2f);
            position = EditorGUILayout.Vector2Field(new GUIContent("区域位置", "调整玩法区域在世界坐标中的位置。"), position);
            parkingSpacing = EditorGUILayout.Slider(new GUIContent("上方停车区车辆间距", "调整停车列和环道中的车辆间距。"), parkingSpacing, 0.6f, 1.5f);
            departureSpacing = EditorGUILayout.Slider(new GUIContent("下方发车区车辆间距", "调整下方发车队列的车辆间距。"), departureSpacing, 0.6f, 1.5f);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("应用到当前运行关卡"))
                    game.ApplyBoardLayout(scale, position, parkingSpacing, departureSpacing);
            }

            if (GUILayout.Button("保存到关卡资源"))
            {
                // Apply first so the values being saved are also visible in the running game.
                game.ApplyBoardLayout(scale, position, parkingSpacing, departureSpacing);
                Save(game);
            }
        }

        static void Save(GameController game)
        {
            var data = game.Level.Clone();
            data.boardLayoutEnabled = true;
            data.boardScale = game.BoardScale;
            data.boardPosition = game.BoardPosition;
            data.spacingSplitEnabled = true;
            data.parkingSpacing = game.ParkingSpacing;
            data.departureSpacing = game.DepartureSpacing;
            data.vehicleSpacing = data.parkingSpacing;
            string path = $"Assets/LoopPark/Resources/Levels/level_{data.id:D4}.json";
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
            LevelRepository.Invalidate(data.id);
            AssetDatabase.Refresh();
            Debug.Log($"[LoopPark] 已保存关卡 {data.id} 的玩法区域布局：缩放 {data.boardScale:0.###}，位置 {data.boardPosition}，停车区间距 {data.parkingSpacing:0.###}，发车区间距 {data.departureSpacing:0.###}");
        }
    }
}
