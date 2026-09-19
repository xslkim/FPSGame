using Godot;

namespace FPSGame;

/// <summary>
/// UiTheme:UI 共用 CJK 占位字体(Godot 默认字体无中文字形)。
/// 字体文件为本机系统字体副本,仅开发期占位,发布前需换可分发字体。
/// </summary>
public static class UiTheme
{
    public const string CjkFontPath = "res://assets/fonts/cjk_fallback.ttf";

    private static Font? _cjkFont;

    public static Font? CjkFont()
    {
        if (_cjkFont == null && ResourceLoader.Exists(CjkFontPath))
            _cjkFont = GD.Load<Font>(CjkFontPath);
        return _cjkFont;
    }

    /// <summary>给 Label3D 应用 CJK 字体(存在时)</summary>
    public static void ApplyLabel3D(Label3D label)
    {
        var f = CjkFont();
        if (f != null)
            label.Font = f;
    }
}
