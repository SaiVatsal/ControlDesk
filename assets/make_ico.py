import zlib
import struct
import math
import os

def create_png(width, height, pixel_func):
    """
    Creates a valid 32-bit RGBA PNG file in bytes using pure Python standard library.
    pixel_func(x, y) -> (r, g, b, a) with values 0-255
    """
    # Scanlines with filter byte 0 (None)
    raw_data = bytearray()
    for y in range(height):
        raw_data.append(0)  # filter type 0
        for x in range(width):
            r, g, b, a = pixel_func(x, y)
            raw_data.extend([int(r) & 0xFF, int(g) & 0xFF, int(b) & 0xFF, int(a) & 0xFF])

    # Compress scanlines with zlib
    compressed = zlib.compress(bytes(raw_data), level=9)

    def make_chunk(chunk_type, data):
        length = struct.pack(">I", len(data))
        crc = struct.pack(">I", zlib.crc32(chunk_type + data) & 0xFFFFFFFF)
        return length + chunk_type + data + crc

    # PNG Signature
    png = b"\x89PNG\r\n\x1a\n"
    # IHDR: width, height, 8 bit depth, color type 6 (RGBA), compression 0, filter 0, interlace 0
    ihdr_data = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    png += make_chunk(b"IHDR", ihdr_data)
    # IDAT
    png += make_chunk(b"IDAT", compressed)
    # IEND
    png += make_chunk(b"IEND", b"")
    return png

def build_icon_pixels(width, height):
    """
    Draws a sleek, modern ControlDesk emblem:
    - Rounded squircle background with vertical deep indigo/blue gradient
    - Subtle metallic/cyan border highlight
    - Stylized desktop monitor outline with stand
    - Glowing remote cursor / connection beam in cyan/white
    """
    # Pre-render into pixel grid
    pixels = [[(0, 0, 0, 0) for _ in range(width)] for _ in range(height)]

    cx = width / 2.0
    cy = height / 2.0
    scale = width / 256.0

    # Corner radius for squircle
    radius = 52.0 * scale
    pad = 12.0 * scale
    bw = 232.0 * scale
    bh = 232.0 * scale

    # Subsampling for smooth anti-aliased edges (2x2 super-sampling)
    for y in range(height):
        for x in range(width):
            sub_samples = []
            for sy in [0.25, 0.75]:
                for sx in [0.25, 0.75]:
                    px = x + sx
                    py = y + sy

                    # Distance to squircle box
                    dx = max(pad + radius - px, 0.0, px - (pad + bw - radius))
                    dy = max(pad + radius - py, 0.0, py - (pad + bh - radius))
                    dist = math.sqrt(dx * dx + dy * dy)
                    inside_bg = dist <= radius

                    if not inside_bg:
                        sub_samples.append((0, 0, 0, 0))
                        continue

                    # Background gradient: vibrant tech blue to deep navy
                    t_y = (py - pad) / bh
                    # Start: (0, 114, 230) -> End: (10, 30, 80)
                    r = int(0 * (1 - t_y) + 10 * t_y)
                    g = int(120 * (1 - t_y) + 32 * t_y)
                    b = int(235 * (1 - t_y) + 95 * t_y)

                    # Inner border highlight (1.5 px inset)
                    in_dist = radius - dist
                    border_inset_x = min(px - pad, pad + bw - px)
                    border_inset_y = min(py - pad, pad + bh - py)
                    min_edge = min(border_inset_x, border_inset_y, in_dist)
                    if min_edge < 2.5 * scale:
                        # Cyan edge glow
                        r = int(r * 0.4 + 60 * 0.6)
                        g = int(g * 0.4 + 180 * 0.6)
                        b = int(b * 0.4 + 255 * 0.6)

                    # Monitor screen rectangle:
                    # Top-left (48, 48), width 160, height 112, rx = 12
                    mx = 48.0 * scale
                    my = 48.0 * scale
                    mw = 160.0 * scale
                    mh = 112.0 * scale
                    m_rad = 12.0 * scale

                    mdx = max(mx + m_rad - px, 0.0, px - (mx + mw - m_rad))
                    mdy = max(my + m_rad - py, 0.0, py - (my + mh - m_rad))
                    m_dist = math.sqrt(mdx * mdx + mdy * mdy)
                    inside_monitor = m_dist <= m_rad

                    # Monitor bezel outline
                    im_pad = 8.0 * scale
                    im_dx = max(mx + im_pad + m_rad*0.7 - px, 0.0, px - (mx + mw - im_pad - m_rad*0.7))
                    im_dy = max(my + im_pad + m_rad*0.7 - py, 0.0, py - (my + mh - im_pad - m_rad*0.7))
                    inside_inner_screen = math.sqrt(im_dx * im_dx + im_dy * im_dy) <= (m_rad * 0.7)

                    # Monitor Stand
                    # Neck: (118, 160) to (138, 185)
                    # Base: (90, 185) to (166, 195), rx = 5
                    stand_neck = (118 * scale <= px <= 138 * scale) and (158 * scale <= py <= 186 * scale)
                    stand_base = (88 * scale <= px <= 168 * scale) and (184 * scale <= py <= 194 * scale)

                    if inside_monitor:
                        if inside_inner_screen:
                            # Screen area: Dark sleek glass with dark gradient
                            sc_ty = (py - (my + im_pad)) / (mh - 2 * im_pad)
                            r = int(12 * (1 - sc_ty) + 20 * sc_ty)
                            g = int(22 * (1 - sc_ty) + 35 * sc_ty)
                            b = int(45 * (1 - sc_ty) + 65 * sc_ty)

                            # Inside Screen Content:
                            # 1. Stylized Remote Desktop Connection Icon (Two interconnected glowing nodes / cursor)
                            # Pointer arrow:
                            # Tip at (105, 82), Base Left (95, 122), Center Inset (107, 114), Base Right (122, 116)
                            # Simplified: glowing cursor or signal waves
                            # Let's draw a glowing remote pointer arrow at center:
                            cur_x = (px - 100 * scale) / scale
                            cur_y = (py - 72 * scale) / scale
                            # Arrow points from (0,0) down-right to (30, 20)
                            # Vector check for arrow triangle
                            in_arrow = False
                            if 0 <= cur_x <= 36 and 0 <= cur_y <= 42:
                                # Arrow tip at (6, 4), right at (32, 28), notch at (20, 26), bottom at (12, 38)
                                # Let's test point in arrow polygon:
                                poly = [(6, 4), (32, 26), (22, 26), (28, 40), (22, 42), (16, 28), (6, 36)]
                                # Ray casting
                                inside_p = False
                                n = len(poly)
                                for i in range(n):
                                    x1, y1 = poly[i]
                                    x2, y2 = poly[(i + 1) % n]
                                    if ((y1 > cur_y) != (y2 > cur_y)) and (cur_x < (x2 - x1) * (cur_y - y1) / (y2 - y1) + x1):
                                        inside_p = not inside_p
                                in_arrow = inside_p

                            if in_arrow:
                                # Glowing electric cyan/white
                                r, g, b = 240, 250, 255
                            else:
                                # Signal Waves (broadcast arcs) behind arrow:
                                # Arcs centered at (152, 95) with radius 16, 26, 36
                                arc_dx = px - 150 * scale
                                arc_dy = py - 96 * scale
                                arc_d = math.sqrt(arc_dx * arc_dx + arc_dy * arc_dy)
                                # Angle check (-75 to 75 deg)
                                angle = math.atan2(arc_dy, arc_dx)
                                if abs(angle) < 1.1:
                                    for target_r in [14 * scale, 25 * scale, 36 * scale]:
                                        if abs(arc_d - target_r) < 2.2 * scale:
                                            # Cyan waves
                                            r = int(r * 0.2 + 0 * 0.8)
                                            g = int(g * 0.2 + 210 * 0.8)
                                            b = int(b * 0.2 + 255 * 0.8)
                        else:
                            # Monitor Bezel: Elegant brushed metallic silver/white
                            r, g, b = 230, 235, 245
                    elif stand_neck or stand_base:
                        # Stand: metallic silver
                        r, g, b = 210, 220, 235

                    sub_samples.append((r, g, b, 255))

            # Average sub-samples
            avg_a = sum(s[3] for s in sub_samples) / 4.0
            if avg_a < 1.0:
                pixels[y][x] = (0, 0, 0, 0)
            else:
                avg_r = sum(s[0] for s in sub_samples) / 4.0
                avg_g = sum(s[1] for s in sub_samples) / 4.0
                avg_b = sum(s[2] for s in sub_samples) / 4.0
                pixels[y][x] = (int(avg_r), int(avg_g), int(avg_b), int(avg_a))

    return pixels

def generate_ico_file(output_path):
    sizes = [256, 48, 32, 24, 16]
    png_images = []

    print(f"Generating icon sizes: {sizes}...")
    for sz in sizes:
        print(f"  Rendering {sz}x{sz}...")
        pixels = build_icon_pixels(sz, sz)
        png_bytes = create_png(sz, sz, lambda x, y: pixels[y][x])
        png_images.append((sz, png_bytes))

    # Assemble ICO file
    num_images = len(png_images)
    header_size = 6
    dir_entry_size = 16
    data_offset = header_size + (dir_entry_size * num_images)

    ico_bytes = bytearray()
    # ICONDIR
    ico_bytes.extend(struct.pack("<HHH", 0, 1, num_images))

    # Calculate offsets
    current_offset = data_offset
    entries = []
    for sz, pdata in png_images:
        w_byte = 0 if sz == 256 else sz
        h_byte = 0 if sz == 256 else sz
        entry = struct.pack("<BBBBHHII", w_byte, h_byte, 0, 0, 1, 32, len(pdata), current_offset)
        entries.append(entry)
        current_offset += len(pdata)

    for e in entries:
        ico_bytes.extend(e)

    for _, pdata in png_images:
        ico_bytes.extend(pdata)

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    with open(output_path, "wb") as f:
        f.write(ico_bytes)
    print(f"Successfully generated ICO: {output_path} ({len(ico_bytes):,} bytes)")

if __name__ == "__main__":
    generate_ico_file("G:/Coding-main/Control Desk/assets/icon.ico")
