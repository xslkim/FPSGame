using Godot;

namespace FPSGame;

/// <summary>
/// SpriteSwap 按钮(对应 Unity Button Transition=SpriteSwap,Highlighted=Pressed=Selected 同图):
/// 透明 Button 承担输入/焦点/键盘导航,面子节点按 常态/悬停 换贴图。
/// patch 参数 &gt;0 时面子为 NinePatchRect(对应 Unity Sliced + spriteBorder),否则 TextureRect(Simple)。
/// 默认 MouseFilter=Ignore:指针交互走"瞄准+扳机"路径(鼠标模拟光枪),悬停视觉由焦点承担(照菜单)。
/// 无 _h 贴图的按钮(如原作无 Button 组件的关卡按钮)normal=hover 传同一张图即可。
/// </summary>
public partial class UiSwapButton : Button
{
    private Texture2D _normal = null!;
    private Texture2D _hover = null!;
    private Control _face = null!;

    public static UiSwapButton Create(string name, string normalPath, string hoverPath,
        int patchL = 0, int patchT = 0, int patchR = 0, int patchB = 0)
    {
        var b = new UiSwapButton
        {
            Name = name,
            Flat = true,
            ClipContents = false,
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b._normal = GD.Load<Texture2D>(normalPath);
        b._hover = GD.Load<Texture2D>(hoverPath);
        if (patchL > 0 || patchT > 0 || patchR > 0 || patchB > 0)
        {
            b._face = new NinePatchRect
            {
                Name = "Face",
                Texture = b._normal,
                PatchMarginLeft = patchL,
                PatchMarginTop = patchT,
                PatchMarginRight = patchR,
                PatchMarginBottom = patchB,
                MouseFilter = MouseFilterEnum.Ignore,
            };
        }
        else
        {
            b._face = new TextureRect
            {
                Name = "Face",
                Texture = b._normal,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = MouseFilterEnum.Ignore,
            };
        }
        b._face.SetAnchorsPreset(LayoutPreset.FullRect);
        b.AddChild(b._face);
        b.AddToGroup(MessageBox.Group);
        b.MouseEntered += b.RefreshFace;
        b.MouseExited += b.RefreshFace;
        b.FocusEntered += b.RefreshFace;
        b.FocusExited += b.RefreshFace;
        b.ButtonDown += b.RefreshFace;
        b.ButtonUp += b.RefreshFace;
        return b;
    }

    /// <summary>面子贴图 = 悬停/按下/焦点 → hover,否则 normal(原作三态同图)</summary>
    private void RefreshFace()
    {
        bool hot = IsHovered() || HasFocus() || GetDrawMode() == DrawMode.Pressed;
        var tex = hot ? _hover : _normal;
        if (_face is NinePatchRect np)
            np.Texture = tex;
        else
            ((TextureRect)_face).Texture = tex;
    }
}
