"""Build the installer's original satin artwork; requires Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math

HERE = Path(__file__).resolve().parent


def font(size, bold=False):
    path = Path("C:/Windows/Fonts") / ("seguisb.ttf" if bold else "segoeui.ttf")
    return ImageFont.truetype(str(path), size)


def curve(points, steps=180):
    a, b, c, d = points
    return [tuple((1-t)**3*a[j] + 3*(1-t)**2*t*b[j] + 3*(1-t)*t*t*c[j] + t**3*d[j] for j in (0, 1)) for t in (i/steps for i in range(steps+1))]


def create(dark=False):
    width, height = 656, 1256
    image = Image.new("RGB", (width, height))
    pixels = image.load()
    for y in range(height):
        for x in range(width):
            t = .6*y/height + .4*x/width
            start, end = ((12, 23, 46), (28, 65, 112)) if dark else ((23, 68, 133), (39, 104, 187))
            pixels[x, y] = tuple(round(a*(1-t)+b*t) for a, b in zip(start, end))
    draw = ImageDraw.Draw(image)
    for index in range(4):
        top = curve(((-50, 840+index*57), (240, 1060+index*30), (245, 550+index*54), (730, 690+index*50)))
        bottom = curve(((730, 744+index*50), (245, 622+index*54), (240, 1120+index*30), (-50, 902+index*57)))
        overlay = Image.new("RGBA", image.size)
        ImageDraw.Draw(overlay).polygon(top + bottom, fill=(163+index*20, 200+index*13, 255, 20+index*10))
        image = Image.alpha_composite(image.convert("RGBA"), overlay).convert("RGB")
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((68, 78, 196, 206), radius=30, fill=(255, 255, 255))
    draw.text((97, 82), "G", font=font(87, True), fill=(24, 90, 189))
    draw.text((64, 252), "Gaius", font=font(66, True), fill="white")
    draw.text((66, 329), "Office", font=font(55), fill=(220, 235, 255))
    draw.text((68, 446), "A home for", font=font(31), fill=(226, 238, 255))
    draw.text((68, 490), "your next idea.", font=font(31), fill=(226, 238, 255))
    labels = [("Write", (100, 168, 255)), ("Sheets", (89, 204, 146)), ("Slides", (255, 157, 122)), ("PDF", (255, 137, 151)), ("Image", (196, 164, 255))]
    for i, (label, color) in enumerate(labels):
        y = 976+i*39
        draw.rounded_rectangle((69, y+9, 83, y+23), radius=4, fill=color)
        draw.text((100, y), label, font=font(25), fill=(234, 242, 255))
    image.save(HERE / ("wizard-dark.png" if dark else "wizard.png"))


def small():
    image = Image.new("RGBA", (180, 180))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((12, 12, 168, 168), radius=38, fill=(24, 90, 189))
    draw.text((48, 12), "G", font=font(114, True), fill="white")
    image.save(HERE / "wizard-small.png")


if __name__ == "__main__":
    create(); create(True); small()
