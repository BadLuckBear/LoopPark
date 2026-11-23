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

        [MenuItem("LoopPark/Play Area Layout (Runtime)")]
        public static void Open() => GetWindow<BoardLayoutWindow>("Play area layout");

        void OnGUI()
        {
            var game = GameController.I;
            if (!Application.isPlaying || game == null || game.Level == null)
            {
                EditorGUILayout.HelpBox("Enter Play mode and load a level first.", MessageType.Info);
                return;
            }

            selectedLevel = EditorGUILayout.IntSlider(new GUIContent("Level to edit"), selectedLevel, 1, LevelRepository.MaxLevel);
            if (GUILayout.Button("Load and Edit This Level"))
            {
                if (UIManager.I != null) UIManager.I.StartLevel(selectedLevel);
                else game.StartLevel(LevelRepository.Get(selectedLevel));
                game = GameController.I;
                scale = game.BoardScale;
                position = game.BoardPosition;
                parkingSpacing = game.ParkingSpacing;
                departureSpacing = game.DepartureSpacing;
            }

            if (GUILayout.Button("Read Current Level Layout"))
            {
                selectedLevel = game.Level.id;
                scale = game.BoardScale;
                position = game.BoardPosition;
                parkingSpacing = game.ParkingSpacing;
                departureSpacing = game.DepartureSpacing;
            }

            scale = EditorGUILayout.Slider(new GUIContent("Area scale", "Adjusts the overall play area size."), scale, 0.5f, 2f);
            position = EditorGUILayout.Vector2Field(new GUIContent("Area position", "Adjusts the play area position in world space."), position);
            parkingSpacing = EditorGUILayout.Slider(new GUIContent("Upper parking area car spacing", "Adjusts car spacing in parking columns and on the loop."), parkingSpacing, 0.6f, 1.5f);
            departureSpacing = EditorGUILayout.Slider(new GUIContent("Lower departure area car spacing", "Adjusts car spacing in the lower departure queue."), departureSpacing, 0.6f, 1.5f);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Apply to Running Level"))
                    game.ApplyBoardLayout(scale, position, parkingSpacing, departureSpacing);
            }

            if (GUILayout.Button("Save to Level Asset"))
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
            Debug.Log($"[LoopPark] Saved play area layout for level {data.id}: scale {data.boardScale:0.###}, position {data.boardPosition}, parking spacing {data.parkingSpacing:0.###}, departure spacing {data.departureSpacing:0.###}");
        }
    }
}
