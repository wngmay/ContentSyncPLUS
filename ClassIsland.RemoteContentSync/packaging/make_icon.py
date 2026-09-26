"""生成插件图标 icon.png（64x64 RGBA）。无第三方依赖，直接手工写 PNG。"""
import struct, zlib, math

SIZE = 64
CX = CY = (SIZE - 1) / 2.0
BG = (15, 108, 189, 255)      # 圆底蓝
FG = (255, 255, 255, 255)     # 白色箭头环
RADIUS_OUT = 28.0
RADIUS_IN = 21.0
R_BG = 30.0
AA = 1.2  # 抗锯齿软边宽度


def smoothstep(sample, edge):
    t = (sample - edge + AA / 2.0) / AA
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def coverage_ring(x, y, r_in, r_out, ang_from, ang_to):
    dx, dy = x - CX, y - CY
    r = math.hypot(dx, dy)
    ang = math.atan2(dy, dx) % (2 * math.pi)
    c = smoothstep(-r, -r_out) * smoothstep(r, r_in)
    if c <= 0.0:
        return 0.0
    # 角度范围裁剪（ang_from -> ang_to 顺时针）
    span = (ang_to - ang_from) % (2 * math.pi)
    pos = (ang - ang_from) % (2 * math.pi)
    edge = AA / max(r, 1.0)
    front = 1.0 if pos > edge else max(0.0, pos / edge)
    back = 1.0 if (span - pos) > edge else max(0.0, (span - pos) / edge)
    return c * front * back


def inside_tri(px, py, pts):
    def sign(ax, ay, bx, by, cx2, cy2):
        return (bx - ax) * (cy2 - ay) - (by - ay) * (cx2 - ax)

    d1 = sign(px, py, pts[0][0], pts[0][1], pts[1][0], pts[1][1])
    d2 = sign(px, py, pts[1][0], pts[1][1], pts[2][0], pts[2][1])
    d3 = sign(px, py, pts[2][0], pts[2][1], pts[0][0], pts[0][1])
    neg = d1 < 0 or d2 < 0 or d3 < 0
    pos = d1 > 0 or d2 > 0 or d3 > 0
    return not (neg and pos)


START = math.radians(-140.0)   # 环起点
END = math.radians(170.0)      # 环终点

def blend(fg, bg, a):
    if a >= 1.0:
        return list(fg)
    a = max(0.0, min(1.0, a))
    out = [int(round(fg[i] * a + bg[i] * (1 - a))) for i in range(3)]
    out.append(int(round(min(255, fg[3] * a + bg[3] * (1 - a)))))
    return out


rows = []
for py in range(SIZE):
    row = bytearray()
    for px in range(SIZE):
        x, y = px + 0.5, py + 0.5

        # 圆形底
        a_bg = smoothstep(math.hypot(x - CX, y - CY), -R_BG)
        img = [BG[0], BG[1], BG[2], int(round(BG[3] * a_bg))]

        # 白色箭头环
        a_ring = coverage_ring(x, y, RADIUS_IN, RADIUS_OUT, START, END)
        if a_ring > 0.0:
            img = blend(FG, img, a_ring)

        # 箭头三角（位于环起点处）
        ang = START
        tx = CX + RADIUS_OUT * math.cos(ang)
        ty = CY + RADIUS_OUT * math.sin(ang)
        perp = ang + math.pi / 2
        tri = [
            (tx + 8.0 * math.cos(perp), ty + 8.0 * math.sin(perp)),
            (tx - 8.0 * math.cos(perp), ty - 8.0 * math.sin(perp)),
            (tx + 11.0 * math.cos(ang), ty + 11.0 * math.sin(ang)),
        ]
        if inside_tri(x, y, tri):
            img = list(FG)

        row += bytes(img)
    rows.append(row)


def write_png(path, size, rows):
    raw = b"".join(b"\x00" + bytes(r) for r in rows)

    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


write_png("icon.png", SIZE, rows)
print("icon.png written")
