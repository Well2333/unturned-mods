using System.Collections.Generic;

namespace well404.WebPanel
{
    /// <summary>
    /// Chinese translations for this plugin's web-facing strings. English source strings are the
    /// keys; only the <c>zh</c> map is needed (English falls back to the key itself). Registered into
    /// the global translation registry on load. Add another language by adding another map.
    /// </summary>
    internal static class WebPanelI18n
    {
        public const string Zh = "zh";

        public static readonly IReadOnlyDictionary<string, string> ZhTable = new Dictionary<string, string>
        {
            // intro / home
            ["Home"] = "首页",
            ["Commands"] = "指令",

            // admin intro editor module
            ["Server intro"] = "服务器介绍",
            ["Introduction (Markdown)"] = "服务器简介（Markdown）",
            ["Markdown"] = "Markdown 内容",
            ["Shown on the player panel home page. Markdown supported."] = "显示在玩家面板首页，支持 Markdown。",
            ["The welcome text players see on the panel home page (one shared text for all languages)."]
                = "玩家在面板首页看到的欢迎文案（所有语言共用一份）。",
            ["Saved."] = "已保存。",

            // player command help (the /menu command itself)
            ["webpanel.cmd.menu"] = "打开你的网页面板",
            ["webpanel.group"] = "面板",
        };
    }
}
