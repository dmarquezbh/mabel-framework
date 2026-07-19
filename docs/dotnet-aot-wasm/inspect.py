import sys
p = sys.argv[1]
b = open(p, "rb").read()
print("size", len(b))
print("magic", b[:4].hex(" "), "version", b[4:8].hex(" "))
kind = "CORE" if b[4:8] == bytes([1,0,0,0]) else ("COMPONENT" if b[4:8] == bytes([0x0d,0,1,0]) else "UNKNOWN")
print("KIND=" + kind)

def uleb(b, i):
    r = s = 0
    while True:
        x = b[i]; i += 1; r |= (x & 0x7f) << s
        if not x & 0x80: break
        s += 7
    return r, i

def nm(b, i):
    n, i = uleb(b, i); return b[i:i+n].decode("utf8", "replace"), i + n

KIND_NAMES = ["func", "table", "mem", "global"]
imports = []; exports = []
i = 8
while i < len(b):
    sid = b[i]; i += 1
    sz, i = uleb(b, i); end = i + sz; j = i
    if sid == 2:
        cnt, j = uleb(b, j)
        for _ in range(cnt):
            m, j = nm(b, j); f, j = nm(b, j); k = b[j]; j += 1
            if k == 0: _, j = uleb(b, j)
            elif k == 1:
                j += 1; fl = b[j]; j += 1; _, j = uleb(b, j)
                if fl == 1: _, j = uleb(b, j)
            elif k == 2:
                fl = b[j]; j += 1; _, j = uleb(b, j)
                if fl == 1: _, j = uleb(b, j)
            elif k == 3: j += 2
            imports.append(m + "::" + f)
    elif sid == 7:
        cnt, j = uleb(b, j)
        for _ in range(cnt):
            n, j = nm(b, j); k = b[j]; j += 1; _, j = uleb(b, j)
            kn = KIND_NAMES[k] if k < 4 else str(k)
            exports.append(n + "(" + kn + ")")
    i = end
mods = sorted(set(x.split("::")[0] for x in imports))
print("IMPORT_MODULES=" + str(mods))
print("IMPORTS_count=" + str(len(imports)))
print("IMPORTS_sample=" + str(imports[:16]))
print("EXPORTS=" + str(exports))
