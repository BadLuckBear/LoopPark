using System.IO;
using LoopPark;
using UnityEditor;
using UnityEngine;

static class UiVerificationCapture
{
    [MenuItem("LoopPark/Verify UI/Home Settings")]
    static void CaptureHomeSettings()
    {
        var view = Object.FindFirstObjectByType<HomeView>();
        if (!view || !view.settingsButton) return;
        view.settingsButton.onClick.Invoke();
        CaptureNextFrame("Logs/ui_home_settings.png");
    }

    [MenuItem("LoopPark/Verify UI/Home Language Menu")]
    static void CaptureHomeLanguageMenu()
    {
        var view = Object.FindFirstObjectByType<HomeView>();
        if (!view || !view.languageButton) return;
        if (view.settingsPanel && !view.settingsPanel.activeSelf) view.settingsButton.onClick.Invoke();
        view.languageButton.onClick.Invoke();
        CaptureNextFrame("Logs/ui_home_language.png");
    }

    static void CaptureNextFrame(string path)
    {
        Directory.CreateDirectory("Logs");
        EditorApplication.delayCall += () => ScreenCapture.CaptureScreenshot(path);
    }
}
