import os, sys, re

path = sys.argv[1] if len(sys.argv) > 1 else 'Etapa2/spec-v2/50-modulos/M-03-troca-contexto.md'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

lines = text.splitlines()

# Termos proibidos do guia E1 03 §2.3
proibidos = [
    r'\badequado\b', r'\bapropriado\b', r'\bconforme necess[aá]rio\b',
    r'\bse aplic[aá]vel\b', r'\bconforme o caso\b', r'\betc\b',
    r'\be/ou\b', r'\btemporariamente\b', r'\bem breve\b',
    r'\bidealmente\b', r'\bsempre que poss[ií]vel\b', r'\bpreferencialmente\b',
    r'\bgeralmente\b', r'\bnormalmente\b', r'\btipicamente\b',
    r'\bfacilmente\b', r'\brapidamente\b', r'\bintuitivo\b',
    r'\brobusto\b', r'\beficiente\b', r'\bperform[aá]tico\b',
    r'\bescal[aá]vel\b', r'\bamig[aá]vel\b', r'\bseguro\b',
    r'\btbd\b', r'\btodo\b', r'\bto be defined\b'
]

placeholders = [r'<<.*?>>', r'\[\.\.\.\]']

errors = []
for i, line in enumerate(lines, 1):
    for p in proibidos:
        m = re.search(p, line, re.IGNORECASE)
        if m:
            errors.append(f'Linha {i}: termo proibido "{m.group(0)}" -> {line.strip()}')
    for pl in placeholders:
        m = re.search(pl, line)
        if m:
            errors.append(f'Linha {i}: placeholder "{m.group(0)}" -> {line.strip()}')

print(f'Ficheiro: {path}')
print(f'Bytes: {len(text.encode("utf-8"))} ({len(text.encode("utf-8"))/1024:.2f} KiB)')
print(f'Linhas: {len(lines)}')
print(f'Erros de lint: {len(errors)}')
for e in errors:
    print('  ' + e)
