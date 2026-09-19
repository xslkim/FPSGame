extends Object
class_name FxHelper
## 特效共用静态助手(独立无依赖,避免 fire_system ⇄ gun_base 编译环)。

## 程序化径向渐变贴图(枪口火焰 / Flash 光标 / 血花占位)
static func make_radial_texture(color: Color, size := 64) -> ImageTexture:
	var img := Image.create(size, size, false, Image.FORMAT_RGBA8)
	var c := size / 2.0
	for y in size:
		for x in size:
			var dist := Vector2(x - c + 0.5, y - c + 0.5).length() / c
			var a := clampf(1.0 - dist, 0.0, 1.0)
			img.set_pixel(x, y, Color(color.r, color.g, color.b, a * color.a))
	return ImageTexture.create_from_image(img)
