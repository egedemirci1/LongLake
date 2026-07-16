import struct
from pathlib import Path

out_dir = Path(r"D:\LongLake\LongLake\Assets\Art\UI\Portraits")
out_dir.mkdir(parents=True, exist_ok=True)

files = {
    "Ahu": Path(
        r"D:\LongLake\LongLake\Assets\ReadyPlayerMe\Avatars\Ahu\2fac66e374c947c41bc74325c6e3d934\69503453403c000063195f31.glb"
    ),
    "Yaman": Path(
        r"D:\LongLake\LongLake\Assets\ReadyPlayerMe\Avatars\Yaman\2fac66e374c947c41bc74325c6e3d934\695139d3220569853f86a35f.glb"
    ),
}


def extract_jpegs(data: bytes):
    starts = []
    i = 0
    while True:
        i = data.find(b"\xff\xd8\xff", i)
        if i < 0:
            break
        starts.append(i)
        i += 3
    blobs = []
    for s in starts:
        e = data.find(b"\xff\xd9", s + 2)
        if e < 0:
            continue
        blob = data[s : e + 2]
        if len(blob) > 2000:
            blobs.append(blob)
    uniq = {}
    for b in blobs:
        uniq[len(b)] = b
    return sorted(uniq.values(), key=len, reverse=True)


for name, path in files.items():
    data = path.read_bytes()
    blobs = extract_jpegs(data)
    print(name, "jpeg_count", len(blobs), "sizes", [len(b) for b in blobs[:8]])
    for idx, b in enumerate(blobs[:5]):
        p = out_dir / f"{name}_tex{idx}.jpg"
        p.write_bytes(b)
        print(" wrote", p.name, len(b))
