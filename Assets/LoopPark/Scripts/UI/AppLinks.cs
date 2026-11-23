using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 集中存放对外跳转地址，避免各 UI 组件里散落魔法字符串。
    /// 设置面板（首页 HomeController 与关卡内 UIManager.ShowPause）以及后续新增的
    /// 法务/客服入口都从这里取常量，改域名只需改一处。
    /// </summary>
    public static class AppLinks
    {
        /// <summary>隐私政策网页（Google Play / App Store 均要求可访问的在线地址）。</summary>
        public const string PrivacyPolicy = "https://ycarm.com/privacy/";

        /// <summary>客服邮箱（保留 mailto，方便玩家直接写信反馈）。</summary>
        public const string SupportMailto = "mailto:support@looppark.game?subject=LoopPark%20Support";

        /// <summary>在系统浏览器中打开隐私政策。跳转失败时只丢给 OpenURL，不打断当前界面。</summary>
        public static void OpenPrivacyPolicy() => Application.OpenURL(PrivacyPolicy);

        /// <summary>打开客服邮件。</summary>
        public static void OpenSupportMail() => Application.OpenURL(SupportMailto);
    }
}
