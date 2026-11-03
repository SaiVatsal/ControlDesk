#!/usr/bin/env python3
"""
Pure Python SVG to ICO converter.
Renders SVG using basic path parsing and PIL.
No external dependencies except PIL (Pillow).
"""
import sys
import re
import math
import io
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:
    print("Pillow not installed. Run: pip install pillow")
    sys.exit(1)

def parse_svg_paths(svg_content):
    """Extract path data from SVG - simplified parser for our specific SVG."""
    paths = []
    # Find all path elements
    path_pattern = r'<path\s+[^>]*d="([^"]*)"[^>]*>'
    for match in re.finditer(path_pattern, svg_content):
        paths.append(match.group(1))
    return paths

def parse_svg_text(svg_content):
    """Extract text elements from SVG."""
    text_pattern = r'<text\s+[^>]*>([^<]*)</text>'
    match = re.search(text_pattern, svg_content)
    if match:
        return match.group(1)
    return "CD"

def parse_colors(svg_content):
    """Extract gradient colors."""
    # Simple extraction - our SVG uses #0066CC and #003D80 for bg, white for fg
    return {
        'bg_start': '#0066CC',
        'bg_end': '#003D80',
        'fg': '#FFFFFF'
    }

def render_icon(size, colors, text):
    """Render the ControlDesk icon at given size."""
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # Calculate dimensions
    padding = max(1, size // 21)  # ~12/256 ratio
    inner_size = size - 2 * padding
    corner_radius = max(1, int(size * 48 / 256))

    # Draw rounded rectangle background with gradient
    # Since PIL doesn't do gradients easily, we'll draw a solid color with highlight
    bg_color = tuple(int(colors['bg_start'][i:i+2], 16) for i in (1, 3, 5))

    # Draw rounded rect
    draw.rounded_rectangle(
        [padding, padding, size - padding - 1, size - padding - 1],
        radius=corner_radius,
        fill=bg_color + (255,)
    )

    # Inner highlight
    highlight_color = (26, 140, 255, 77)  # #1A8CFF with opacity
    draw.rounded_rectangle(
        [padding + max(1, size//64), padding + max(1, size//64),
         size - padding - max(1, size//64) - 1, size - padding - max(1, size//64) - 1],
        radius=max(1, corner_radius - max(1, size//64)),
        outline=highlight_color,
        width=max(1, size//128)
    )

    # Draw "CD" text
    fg_color = tuple(int(colors['fg'][i:i+2], 16) for i in (1, 3, 5))
    font_size = max(10, int(size * 96 / 256))

    # Try to use a bold font
    try:
        font = ImageFont.truetype("segoeui.ttf", font_size, weight="bold")
    except:
        try:
            font = ImageFont.truetype("arial.ttf", font_size)
        except:
            font = ImageFont.load_default()

    # Center the text
    bbox = draw.textbbox((0, 0), text, font=font)
    text_width = bbox[2] - bbox[0]
    text_height = bbox[3] - bbox[1]
    text_x = (size - text_width) // 2
    text_y = (size - text_height) // 2 + max(1, size // 25)  # slight vertical adjustment

    draw.text((text_x, text_y), text, font=font, fill=fg_color + (255,))

    # Draw connection lines (arcs representing remote connection)
    line_color = (255, 255, 255, 102)  # white with opacity
    line_width = max(1, size // 128)

    # Left arc
    cx1 = padding + max(1, int(size * 60 / 256))
    cy1 = size - padding - max(1, int(size * 66 / 256))
    r1 = max(1, int(size * 20 / 256))
    # Draw arc approximation using multiple lines
    for angle in range(160, 20, -5):
        rad = math.radians(angle)
        x1 = cx1 + r1 * math.cos(rad)
        y1 = cy1 - r1 * math.sin(rad)
        rad2 = math.radians(angle - 5)
        x2 = cx1 + r1 * math.cos(rad2)
        y2 = cy1 - r1 * math.sin(rad2)
        draw.line([x1, y1, x2, y2], fill=line_color, width=line_width)

    # Right arc
    cx2 = size - padding - max(1, int(size * 100 / 256))
    cy2 = cy1
    for angle in range(160, 20, -5):
        rad = math.radians(angle)
        x1 = cx2 + r1 * math.cos(rad)
        y1 = cy2 - r1 * math.sin(rad)
        rad2 = math.radians(angle - 5)
        x2 = cx2 + r1 * math.cos(rad2)
        y2 = cy2 - r1 * math.sin(rad2)
        draw.line([x1, y1, x2, y2], fill=line_color, width=line_width)

    # Bottom connecting arc
    cx3 = size // 2
    cy3 = cy1 + max(1, int(size * 10 / 256))
    r3 = max(1, int(size * 28 / 256))
    for angle in range(180, 360, 5):
        rad = math.radians(angle)
        x1 = cx3 + r3 * math.cos(rad)
        y1 = cy3 - r3 * math.sin(rad)
        rad2 = math.radians(angle + 5)
        x2 = cx3 + r3 * math.cos(rad2)
        y2 = cy3 - r3 * math.sin(rad2)
        draw.line([x1, y1, x2, y2], fill=(255, 255, 255, 64), width=max(1, line_width - 1))

    return img

def svg_to_ico(svg_path: Path, ico_path: Path, sizes=None):
    """Convert our specific SVG to ICO."""
    if sizes is None:
        sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

    svg_content = svg_path.read_text(encoding='utf-8')
    colors = parse_colors(svg_content)
    text = parse_svg_text(svg_content)

    images = []
    for size in sizes:
        img = render_icon(size, colors, text)
        images.append(img)
        print(f"  Rendered {size}x{size}")

    # Save as ICO
    images[0].save(
        ico_path,
        format='ICO',
        sizes=[(img.width, img.height) for img in images],
        append_images=images[1:]
    )
    print(f"Saved ICO: {ico_path} with {len(images)} sizes")

if __name__ == "__main__":
    svg_path = Path("G:/Coding-main/Control Desk/assets/icon.svg")
    ico_path = Path("G:/Coding-main/Control Desk/assets/icon.ico")
    svg_to_ico(svg_path, ico_path)