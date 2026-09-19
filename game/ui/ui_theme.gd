extends Object
class_name UITheme
## UI 共用:CJK 占位字体(Godot 默认字体无中文字形)。
## 字体文件为本机系统字体副本,仅开发期占位,发布前需换可分发字体。

const CJK_FONT_PATH := "res://assets/fonts/cjk_fallback.ttf"

static var _cjk_font: Font = null

static func cjk_font() -> Font:
	if _cjk_font == null and ResourceLoader.exists(CJK_FONT_PATH):
		_cjk_font = load(CJK_FONT_PATH)
	return _cjk_font

## 给 Label3D 应用 CJK 字体(存在时)
static func apply_label3d(label: Label3D) -> void:
	var f := cjk_font()
	if f != null:
		label.font = f
