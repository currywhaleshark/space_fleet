"""Find ship-sized blobs on a concept board and extract a clean silhouette mask from a view crop.
python tools/reference_views.py BOARD.png OUT.png   -> numbered candidate boxes for picking top/side views
"""
import sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage


def background_distance(rgb, border=6):
    """Per-pixel colour distance from the median colour of the crop border (studio backdrop)."""
    a = rgb.astype(np.int32)
    edge = np.concatenate([a[:border].reshape(-1, 3), a[-border:].reshape(-1, 3), a[:, :border].reshape(-1, 3), a[:, -border:].reshape(-1, 3)])
    bg = np.median(edge, axis=0)
    return np.abs(a - bg).max(axis=2)


def silhouette(rgb, threshold=20, close=3, strong=55, texture=5.0):
    """Largest connected non-background region with holes filled (labels, specks and soft shadows dropped).
    Soft drop shadows differ from the backdrop but are smooth; hull surfaces are either strongly different
    or carry panel texture, so weak differences only count where the local texture is high."""
    distance = background_distance(rgb)
    grey = rgb.astype(np.float32).mean(axis=2)
    local = np.sqrt(np.maximum(ndimage.uniform_filter(grey ** 2, 5) - ndimage.uniform_filter(grey, 5) ** 2, 0))
    mask = (distance > strong) | ((distance > threshold) & (local > texture))
    # Neighbouring artwork leaking across the top/bottom edge of the crop shows up as a full-width band.
    band = max(2, mask.shape[0] // 25)
    for rows in (slice(0, band), slice(mask.shape[0] - band, mask.shape[0])):
        wide = mask[rows].mean(axis=1) > .6
        mask[rows][wide] = False
    mask = ndimage.binary_closing(mask, iterations=close)
    labels, n = ndimage.label(mask)
    if n == 0: return mask
    sizes = ndimage.sum(mask, labels, range(1, n + 1))
    main = labels == (int(np.argmax(sizes)) + 1)
    # Keep detached parts that are large and close (e.g. radiators on struts), drop text.
    keep = main.copy(); grown = ndimage.binary_dilation(main, iterations=12)
    for i, size in enumerate(sizes, start=1):
        part = labels == i
        if size > sizes.max() * .01 and (part & grown).any(): keep |= part
    return ndimage.binary_fill_holes(keep)


def candidates(path, threshold=18, min_area=4000):
    rgb = np.asarray(Image.open(path).convert('RGB'))
    mask = ndimage.binary_closing(background_distance(rgb, border=3) > threshold, iterations=4)
    labels, n = ndimage.label(mask)
    boxes = []
    for i, sl in enumerate(ndimage.find_objects(labels), start=1):
        area = int((labels[sl] == i).sum())
        if area >= min_area: boxes.append((sl[1].start, sl[0].start, sl[1].stop, sl[0].stop, area))
    return rgb, boxes


if __name__ == '__main__':
    rgb, boxes = candidates(sys.argv[1])
    image = Image.fromarray(rgb); d = ImageDraw.Draw(image)
    for i, (x0, y0, x1, y1, area) in enumerate(boxes):
        d.rectangle((x0, y0, x1, y1), outline=(255, 60, 60), width=3); d.text((x0 + 4, y0 + 4), str(i), fill=(255, 255, 0))
        print(i, (x0, y0, x1, y1), area)
    image.save(sys.argv[2])
