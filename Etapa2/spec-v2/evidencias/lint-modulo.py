import os, sys, re, glob

sys.stdout.reconfigure(encoding='utf-8')

# Lista completa de termos proibidos (E1 03 §2.3 + adjetivos/advérbios vagos)
proibidos = [
    r'\badequado[as]?\b', r'\bapropriado[as]?\b', r'\br[aá]pido[as]?\b', r'\brapidamente\b',
    r'\beficiente[s]?\b', r'\bseguro[as]?\b', r'\brobusto[as]?\b', r'\bflex[ií]vel\b', r'\bflex[ií]veis\b',
    r'\bintuitivo[as]?\b', r'\bamig[aá]vel\b', r'\bamig[aá]veis\b', r'\bnormalmente\b', r'\bgeralmente\b',
    r'\btipicamente\b', r'\bfacilmente\b', r'\bperform[aá]tico[as]?\b', r'\bescal[aá]vel\b', r'\bescal[aá]veis\b',
    r'\bquando aplic[aá]vel\b', r'\bse aplic[aá]vel\b', r'\bse necess[aá]rio\b', r'\bconforme necess[aá]rio\b',
    r'\bconforme o caso\b', r'\betc\b', r'\bentre outros\b', r'\be/ou\b', r'\bsimilar a\b',
    r'\btemporariamente\b', r'\bem breve\b', r'\bidealmente\b', r'\bsempre que poss[ií]vel\b',
    r'\bpreferencialmente\b', r'\bopcionalmente\b', r'\btbd\b', r'\btodo\b', r'\ba definir\b', r'\bto be defined\b'
]

placeholders = [r'<<.*?>>', r'\[\.\.\.\]']

def lint_file(path):
    with open(path, 'r', encoding='utf-8') as f:
        text = f.read()
    lines = text.splitlines()
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
    return len(errors)

if __name__ == '__main__':
    args = sys.argv[1:]
    if not args:
        paths = ['Etapa2/spec-v2/50-modulos/M-03-troca-contexto.md']
    elif args == ['--todos']:
        base_dir = 'Etapa2/spec-v2/50-modulos' if os.path.isdir('Etapa2/spec-v2/50-modulos') else '50-modulos'
        paths = sorted(glob.glob(os.path.join(base_dir, 'M-*.md')))
    else:
        paths = args

    total_errors = sum(lint_file(p) for p in paths)
    sys.exit(1 if total_errors > 0 else 0)
