"""Independently inspect UI-generated files with PyMuPDF and Pillow."""
from pathlib import Path
import json
import sys

import pymupdf
from PIL import Image, ImageChops

root = Path(sys.argv[1]).resolve()
fixture = Path(__file__).resolve().parents[1] / "fixtures" / "gradient.png"
results = {}
for name, page_count in {"word.pdf": 1, "excel.pdf": 2, "slides.pdf": 1, "image.pdf": 1}.items():
    with pymupdf.open(root / name) as document:
        assert len(document) == page_count, f"{name}: wrong page count"
        for index, page in enumerate(document):
            assert page.get_images(), f"{name}: page {index + 1} has no rendered content"
            pixmap = page.get_pixmap(matrix=pymupdf.Matrix(.6, .6), alpha=False)
            image = Image.frombytes("RGB", [pixmap.width, pixmap.height], pixmap.samples)
            assert ImageChops.difference(image, Image.new("RGB", image.size, "white")).getbbox(), f"{name}: blank page"
            if index == 0:
                image.save(root / (Path(name).stem + "-verified.png"))
        results[name] = {"pages": len(document), "bytes": (root / name).stat().st_size}

with Image.open(fixture) as source:
    expected_size = source.size
for name, expected_format in {"image-copy.png": "PNG", "image-copy.jpg": "JPEG", "image-copy.bmp": "BMP"}.items():
    with Image.open(root / name) as image:
        assert image.format == expected_format, f"{name}: incorrect file encoding"
        assert image.size == expected_size, f"{name}: dimensions changed"
        image.load()
        results[name] = {"format": image.format, "size": image.size, "bytes": (root / name).stat().st_size}
with Image.open(root / "compressed.jpg") as image:
    assert image.format == "JPEG" and image.width <= expected_size[0] and image.height <= expected_size[1]
    image.load()
    results["compressed.jpg"] = {"size": image.size, "bytes": (root / "compressed.jpg").stat().st_size}
(root / "verified-files.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print("PASS: independent PDF page/content and PNG/JPEG/BMP encoding/dimension checks")
