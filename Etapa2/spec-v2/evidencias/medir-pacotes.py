"""Mede o orçamento dos pacotes de UT-3 e verifica o fecho de referências (00 §2.2, §2.3).
Uso (a partir de spec-v2/): python evidencias/medir-pacotes.py
Sai com código 1 se algum pacote passar de 60 KiB ou se alguma referência (ENUM, ENT, IF, RG, ERR,
PRS, EP, EV, EXT, AUD ou secções de 10, 20, 30, 40, 70) citada no texto do pacote não estiver
definida dentro do mesmo pacote (exceto citações que são estritamente etiquetas de evidência ou registo)."""
import re, sys, os
sys.stdout.reconfigure(encoding='utf-8')

KB = 1024
LIMITE = 60 * KB
def b(s): return len(s.encode('utf-8'))
def rd(p): return open(p, encoding='utf-8').read()

def secoes(t):
    """Divide por títulos ## a ####; cada secção = título + texto até ao título seguinte (sem filhos)."""
    partes = re.split(r'(?m)^(#{2,4} .*)$', t)
    out = {'head': partes[0]}
    for i in range(1, len(partes), 2):
        out[partes[i].strip()] = partes[i] + partes[i + 1]
    return out

def uma(s, prefixo):
    ks = [k for k in s if k.startswith(prefixo)]
    assert len(ks) == 1, (prefixo, ks)
    return s[ks[0]]

# ---------- documentos base ----------
t10 = rd('10-nucleo.md'); base10 = t10[:t10.index('## Extras')]; s10 = secoes(t10[t10.index('## Extras'):])
t30 = rd('30-dados.md'); s30 = secoes(t30)
comum30 = s30['head'] + uma(s30, '## 1.') + uma(s30, '## 2.') + uma(s30, '## 3.')
t40 = rd('40-contratos.md')
c11 = t40[t40.index('### 1.1'):t40.index('## 2.')]
c13 = t40[t40.index('## 1.'):t40.index('### 1.1')] + t40[t40.index('## 2.'):t40.index('## 4.')]
c1 = t40[t40.index('## 1.'):t40.index('### 1.1')]
sec4 = t40[t40.index('## 4.'):t40.index('## 5.')]
sec5 = t40[t40.index('## 5.'):t40.index('## 6.')]
sec6 = t40[t40.index('## 6.'):t40.index('## 7.')]
ROW = re.compile(r'^\| ((?:EP|EV|IF)-\d\d(?:\.\d+)?) \|')
TP = re.compile(r'^\| `([A-Za-z]+)(?:<T>)?` \| `M')
linhas4, tipos, resto4 = {}, {}, []
for l in sec4.split('\n'):
    m, mt = ROW.match(l), TP.match(l)
    if m: linhas4[m.group(1)] = l + '\n'
    elif mt: tipos[mt.group(1)] = tipos.get(mt.group(1), '') + l + '\n'
    else: resto4.append(l)
intro4 = '\n'.join(resto4)
linhas5, resto5 = {}, []
for l in sec5.split('\n'):
    m = re.match(r'^\| (EP-\d\d) \|', l)
    if m: linhas5[m.group(1)] = l + '\n'
    else: resto5.append(l)
intro5 = '\n'.join(resto5)
t70 = rd('70-ambiente-teste.md'); comum70 = t70[:t70.index('## 2.')]; s70_2 = t70[t70.index('## 2.'):t70.index('## 3.')]
s20 = secoes(rd('20-sistema-existente.md'))

# ---------- 90-registo (A-62) ----------
t90 = rd('90-registo.md')
ut3_linhas_90 = {}
in_ut3 = False
for l in t90.splitlines():
    if '**Entradas para as UT-3**' in l:
        in_ut3 = True
        continue
    if in_ut3:
        if l.startswith('## '):
            break
        m = re.match(r'^\|\s*(UT-3-\d\d)\s*\|\s*(.*?)\s*\|$', l)
        if m:
            ut3_linhas_90[m.group(1)] = l + '\n'

prs_linhas_90 = {}
in_prs22 = False
for l in t90.splitlines():
    if '### 2.2 PRS de um só módulo' in l:
        in_prs22 = True
        continue
    if in_prs22:
        if l.startswith('## ') or l.startswith('IDs livres'):
            break
        m = re.match(r'^\|\s*(PRS-\d\d)\s*\|', l)
        if m:
            prs_linhas_90[m.group(1)] = l + '\n'

prs_globais = set(re.findall(r'(?m)^\|\s*(PRS-\d\d)\s*\|', t90[t90.index('### 2.1'):t90.index('### 2.2')]))
if '### 9.' in t10:
    prs_globais |= set(re.findall(r'(?m)^\|\s*(PRS-\d\d)\s*\|', t10[t10.index('### 9.'):]))

EP_CATALOG = {f'EP-{i:02d}' for i in range(1, 11)}
EV_CATALOG = {f'EV-{i:02d}' for i in range(1, 5)}

BRIEF = 2 * KB
def dono(*ifs): return [k for k in linhas4 if any(k == i or k.startswith(i + '.') for i in ifs)]

# ---------- composição (00 §2.2) ----------
# (extras 10, secções 30, itens 40, convenções 40, §6 de 40, §2 de 70, EXT de 20, PRS de 90 §2.2)
P = {
 'M-00': (['### 3.7', '### 3.9'], ['ENT-09', 'ENUM-08', '## 4.'], dono('IF-03', 'IF-04', 'IF-05', 'IF-08'), '13', False, False, ['EXT-02'], []),
 'M-01': ([], ['ENT-04', 'ENUM-04', 'ENUM-04 T', 'AUD.M01'], ['EP-01', 'EV-03', 'IF-01.1', 'IF-01.4', 'IF-03.1', 'IF-04.2', 'IF-06.1', 'IF-07.3', 'IF-09.1', 'IF-09.6'] + dono('IF-02'), '13', False, False, [], ['PRS-30', 'PRS-31', 'PRS-32']),
 'M-02': ([], ['ENT-08', 'ENUM-05', 'ENUM-05 T', 'AUD.M02'], ['EP-03', 'EV-04', 'IF-03.1', 'IF-04.2', 'IF-06.1', 'IF-07.5', 'IF-08.1', 'IF-09.1', 'IF-09.2', 'IF-09.3', 'IF-09.4', 'IF-09.5'] + dono('IF-01'), '13', False, False, [], ['PRS-33', 'PRS-34', 'PRS-45']),
 'M-03': ([], ['ENUM-03', 'ENUM-06', 'ENUM-06 T', 'AUD.M03'], ['EP-02', 'IF-01.2', 'IF-03.1', 'IF-04.1', 'IF-06.1', 'IF-08.1', 'IF-09.1', 'IF-09.2', 'IF-09.3', 'IF-09.4', 'IF-09.5', 'IF-09.8'], '13', False, False, [], ['PRS-05']),
 'M-04': ([], ['ENT-01', 'ENT-02', 'ENT-03', 'ENUM-01', 'ENUM-02', 'ENUM-03', 'AUD.M04'], ['EP-06', 'EV-01', 'IF-03.1', 'IF-07.1', 'IF-08.1', 'IF-08.2', 'IF-12.1'] + dono('IF-10'), '13', False, False, [], ['PRS-36', 'PRS-37']),
 'M-05': ([], ['ENT-02', 'ENT-03', 'ENT-04', 'ENT-05', 'ENT-06', 'ENT-07', 'ENUM-01', 'AUD.M05'], ['EP-07', 'IF-03.1', 'IF-08.1', 'IF-08.2', 'IF-11.3'] + dono('IF-12'), '13', False, False, [], ['PRS-38']),
 'M-06': ([], ['ENT-04', 'ENUM-03', 'ENUM-04', 'ENUM-07', 'AUD.M06', '## 6.'], ['EP-05', 'IF-03.1', 'IF-06.1', 'IF-08.1', 'IF-09.1', 'IF-09.2', 'IF-09.3', 'IF-09.4', 'IF-09.8'], '13', False, False, [], ['PRS-08', 'PRS-40', 'PRS-41']),
 'M-07': (['### 3.7'], [], ['EP-10', 'IF-05.1'] + dono('IF-06'), '13', False, False, [], ['PRS-44']),
 'M-08': (['### 3.8'], ['ENT-01', 'ENT-02', 'ENT-03', 'ENT-04', 'ENT-05', 'ENT-06', 'ENT-07', 'ENUM-01', 'ENUM-03', 'ENUM-04', '## 4.', '## 6.'], ['IF-04.5', 'IF-04.6', 'IF-11.3'] + dono('IF-09'), '1', False, False, ['EXT-02'], ['PRS-05']),
 'M-09': ([], ['ENUM-06', 'AUD.M09'], ['EP-04', 'IF-01.3', 'IF-03.1', 'IF-04.1', 'IF-08.1'], '13', False, False, [], []),
 'M-10': ([], ['ENT-02', 'ENT-04', 'ENT-05', 'ENUM-04', 'ENUM-04 T', 'AUD.M10'], ['EP-09', 'EV-03', 'IF-01.4', 'IF-01.6', 'IF-02.1', 'IF-03.1', 'IF-04.2', 'IF-07.4', 'IF-08.1', 'IF-09.7', 'IF-12.2'], '13', False, False, [], ['PRS-37', 'PRS-39']),
 'M-11': (['### 3.7', '### 3.11'], ['ENT-%02d' % i for i in range(1, 11)] + ['## 5.', '## 6.'], ['IF-08.1', 'IF-11.1', 'IF-11.2'], '0', False, True, ['EXT-01'], []),
 'M-12': ([], ['ENT-02', 'ENT-05', 'ENUM-01', 'ENUM-01 T', 'AUD.M12'], ['EP-08', 'EV-02', 'IF-01.5', 'IF-03.1', 'IF-04.3', 'IF-04.4', 'IF-07.2', 'IF-08.1', 'IF-10.1'], '13', False, False, [], ['PRS-36', 'PRS-37']),
 'M-13': (['### 3.10', '### 5.'], ['ENT-03', 'ENT-05', 'ENT-07', '## 5.', '### 5.1'], ['IF-08.1', 'IF-08.2'] + dono('IF-11'), '1', False, False, ['EXT-01'], []),
 'M-14': (['### 3.7', '### 5.'], ['ENT-10', 'ENUM-03', 'ENUM-06', 'ENUM-09', 'ENUM-09 T', 'ENUM-10'], ['EV-01', 'EV-02', 'EV-03', 'EV-04', 'IF-05.1', 'IF-08.1'] + dono('IF-07'), '1', True, False, ['EXT-03'], ['PRS-09', 'PRS-42', 'PRS-43']),
}

# Tetos dos módulos (ajustados para manter total <= 59.7 KiB, A-110)
ESTIMADO = {'M-00': 12, 'M-01': 12, 'M-02': 10, 'M-03': 14, 'M-04': 14, 'M-05': 10, 'M-06': 13.5, 'M-07': 20, 'M-08': 11.5,
            'M-09': 20, 'M-10': 10, 'M-11': 7, 'M-12': 14, 'M-13': 16, 'M-14': 17.5}

def texto30(nome):
    if nome.startswith('ENUM-') and nome.endswith(' T'):
        return uma(s30, '#### %s · transições' % nome[:7])
    if nome.startswith('AUD.M'):
        return uma(s30, '#### ' + nome + ' ')
    if nome.startswith('#'):
        return uma(s30, nome)
    return uma(s30, '### ' + nome + ' ')

def montar(m):
    ex10, it30, it40, conv, com6, com70_2, ext, prs90 = P[m]
    partes = {'10': base10 + ''.join(uma(s10, h) for h in ex10),
              '30': comum30 + ''.join(texto30(n) for n in it30)}
    ids = set(it40)
    for k in list(ids):
        if '.' in k: ids.add(k.split('.')[0])
    t40p = ({'13': c13, '1': c1, '0': ''}[conv]) + (c11 if m == 'M-00' else '') + intro4 + ''.join(linhas4[i] for i in sorted(ids))
    eps = sorted(i for i in ids if i in linhas5)
    if eps: t40p += intro5 + ''.join(linhas5[i] for i in eps)
    if com6: t40p += sec6
    # tipos partilhados: fecho pelos nomes citados
    incl, mudou = set(), True
    while mudou:
        mudou = False
        corpo = t40p + partes['10'] + partes['30'] + ''.join(tipos[x] for x in incl)
        for nome in tipos:
            if nome not in incl and re.search(r'\b%s\b' % nome, corpo):
                incl.add(nome); mudou = True
    partes['40'] = t40p + ''.join(tipos[x] for x in sorted(incl))
    partes['70'] = comum70 + (s70_2 if com70_2 else '')
    partes['20'] = ''.join(uma(s20, '## ' + e + ' ') for e in ext)
    # 90 §1 e §2.2
    ut_key = 'UT-3-' + m[2:]
    t90p = ut3_linhas_90.get(ut_key, '') + ''.join(prs_linhas_90[p] for p in prs90)
    partes['90'] = t90p
    return partes

# Regex para identificadores (A-63)
IDRE = re.compile(r'\b(ENUM-\d\d|ENT-\d\d|IF-\d\d(?:\.\d+)?|RG-\d\d|ERR-[A-Z]+-\d{3}|PRS-\d+|EP-\d\d|EV-\d\d|EXT-\d\d|L-\d\d|AUD\.M\d\d|D-\d\d|CR-\d\d)\b')
REF_SECS = re.compile(r'`(00|10|20|30|40|60|70|80|90)`\s*§\s*(\d+(?:\.\d+)?)')
REF_INT = re.compile(r'(?<!`[0-9]{2}` )(?<!IB )(?<!Base )(?<!E1 )(?<!9110 )(?<!RFC 9457 )(?<!RFC )(?<!ISO )§(\d+(?:\.\d+)?)')

# Lista explícita e curta de citações que são puramente etiquetas de evidência / decisão / registo (A-63, CR-16)
def eh_etiqueta(r):
    # CR-16 (40 §7): PRS-nn e D-nn em texto partilhado são etiquetas de proveniência (o script NÃO exige a sua definição no pacote)
    if re.match(r'^PRS-\d+$', r): return True
    if re.match(r'^D-\d\d$', r): return True   # Decisões do PO (D-01 a D-19 em 90 §3)
    if re.match(r'^CR-\d\d$', r): return True  # Change requests (CR-01 a CR-16 em 90 §7)
    if re.match(r'^L-\d\d$', r): return True   # Lacunas de especificação (L-01 a L-06 em 20 §3 / 90 §5)
    if r in {'EXT-04', 'EXT-05'}: return True  # Consumidores externos que só consomem (sem contrato interno chamado)
    return False

# Exceções de secções que são puramente etiquetas de evidência ou histórico externo ao pacote (A-100)
EXCECOES_SECS = {
    ('90', '8'),  # Evidência de verificação de pacotes NuGet em 10 §3.11 / 90 §8
}

def extrair_secoes(doc_text):
    secs = set()
    for h in re.findall(r'(?m)^#{2,4}\s+(\d+(?:\.\d+)?)[ .]', doc_text):
        secs.add(h)
        if '.' in h: secs.add(h.split('.')[0])
    return secs

def falhas_de(partes, m):
    texto = ''.join(partes.values())
    sem_meta = lambda s: '\n'.join(l for l in s.split('\n') if not l.startswith('>'))
    
    definidos = set(re.findall(r'(?m)^### (ENUM-\d\d|ENT-\d\d) ', texto))
    definidos |= set(re.findall(r'(?m)^\| ((?:IF|RG)-\d\d(?:\.\d+)?) \|', texto))
    definidos |= set(re.findall(r'(?m)^\| `(ERR-[A-Z]+-\d{3})` \|', texto))
    definidos |= set(re.findall(r'(?m)^#### (AUD\.M\d\d) ', texto))
    definidos |= set(re.findall(r'(?m)^## (EXT-\d\d) ', texto))
    definidos |= set(re.findall(r'(?m)^\|\s*(PRS-\d+)\s*\|', texto))
    
    sec_defs = {d: extrair_secoes(partes[d]) for d in ['10', '20', '30', '40', '70']}
    sec_defs['00'] = set()
    sec_defs['60'] = set()
    sec_defs['80'] = set()
    sec_90 = set()
    ut_key = 'UT-3-' + m[2:]
    if ut3_linhas_90.get(ut_key, ''):
        sec_90.add('1')
    if any(p in prs_linhas_90 for p in P[m][7]):
        sec_90.add('2')
        sec_90.add('2.2')
    sec_defs['90'] = sec_90

    secs_completas = {'30': extrair_secoes(t30), '40': extrair_secoes(t40), '70': extrair_secoes(t70)}
    
    falta = set()
    for doc, s in partes.items():
        s = sem_meta(s)
        for r in IDRE.findall(s):
            if eh_etiqueta(r):
                continue
            if r.startswith('EP-'):
                if r not in EP_CATALOG:
                    falta.add(r)
            elif r.startswith('EV-'):
                if r not in EV_CATALOG:
                    falta.add(r)
            elif r.startswith('PRS-'):
                if r not in definidos and r not in prs_globais:
                    falta.add(r)
            elif r not in definidos:
                falta.add(r)
        
        # Referências de secções
        for target_doc, target_sec in REF_SECS.findall(s):
            if (target_doc, target_sec) in EXCECOES_SECS:
                continue
            if target_sec not in sec_defs.get(target_doc, set()):
                falta.add(f"`{target_doc}` §{target_sec}")
                
        if doc == '10':
            for target_sec in REF_INT.findall(s):
                if target_sec not in sec_defs['10']:
                    falta.add(f"`10` §{target_sec}")
        elif doc in {'30', '40', '70'}:
            for target_sec in REF_INT.findall(s):
                if target_sec not in secs_completas[doc]:
                    falta.add(f"`{doc}` §{target_sec}")
                    
    return sorted(falta)

if '--tabela' in sys.argv:
    print('| Módulo | Extras de `10` | Secções de `30` | Linhas de `40` §4 (itens e operações) | `40` | `70` §2 | `20` | `90` §2.2 | Teto do ficheiro (KiB) |')
    print('|---|---|---|---|---|---|---|---|---|')
    for m, (ex10, it30, it40, conv, com6, com70_2, ext, prs90) in P.items():
        partes = montar(m); fixo = sum(b(v) for v in partes.values()) + BRIEF
        conv_txt = {'13': '§1, §2, §3' + (', §1.1' if m == 'M-00' else ''), '1': '§1', '0': 'Nenhuma'}[conv] + (', §6' if com6 else '')
        prs_txt = ', '.join(prs90) or 'Nenhum'
        print('| %s | %s | %s | %s | %s | %s | %s | %s | %d |' % (
              m, ', '.join(h.replace('### ', '§').rstrip('.') for h in ex10) or 'Nenhum',
              ', '.join(x.replace('### ', '§').replace('## ', '§').rstrip('.').replace(' T', ' (transições)') for x in it30) or 'Nenhuma',
              ', '.join(sorted(set(it40))), conv_txt, 'Sim' if com70_2 else 'Não', ', '.join(ext) or 'Nenhuma', prs_txt, int(60 - fixo / KB)))
    sys.exit(0)

def gerar_pacote_texto(m):
    ex10, it30, it40, conv, com6, com70_2, ext, prs90 = P[m]
    partes = montar(m)
    tam = {k: b(v) for k, v in partes.items()}
    tam_total = sum(tam.values())
    
    sec_10 = "Núcleo base (§1-§2, §3 comum, §4, §6-§10)" + (f" + extras {', '.join(h.replace('### ', '§').rstrip('.') for h in ex10)}" if ex10 else "")
    sec_30 = "Comum (§1, §2, §3)" + (f" + {', '.join(x.replace('### ', '§').replace('## ', '§').rstrip('.').replace(' T', ' (transições)') for x in it30)}" if it30 else "")
    conv_txt = {'13': '§1, §2, §3' + (', §1.1' if m == 'M-00' else ''), '1': '§1', '0': 'Nenhuma'}[conv] + (', §6' if com6 else '')
    sec_40 = f"{conv_txt} + operações/itens de §4 ({', '.join(sorted(set(it40)))}) + tipos partilhados"
    sec_70 = "Comum §1" + (" + §2" if com70_2 else "")
    sec_20 = ', '.join(ext) if ext else 'Nenhuma'
    ut_key = 'UT-3-' + m[2:]
    sec_90 = f"{ut_key} (§1)" + (f" + {', '.join(prs90)} (§2.2)" if prs90 else " + PRS (§2.2): Nenhum")
    
    cabecalho = f"""# Pacote de Contexto — {m}

| Ficheiro de origem | Secções incluídas | Tamanho (KiB) |
|---|---|---|
| `10-nucleo.md` | {sec_10} | {tam['10'] / KB:.1f} |
| `30-dados.md` | {sec_30} | {tam['30'] / KB:.1f} |
| `40-contratos.md` | {sec_40} | {tam['40'] / KB:.1f} |
| `70-ambiente-teste.md` | {sec_70} | {tam['70'] / KB:.1f} |
| `20-sistema-existente.md` | {sec_20} | {tam['20'] / KB:.1f} |
| `90-registo.md` | {sec_90} | {tam['90'] / KB:.1f} |
| **Total contexto** | **Todas as origens** | **{tam_total / KB:.1f}** |

<!-- FIM DO CABECALHO -->

"""
    corpo = partes['10'] + partes['30'] + partes['40'] + partes['70'] + partes['20'] + partes['90']
    return cabecalho, corpo

if '--extrair' in sys.argv:
    idx = sys.argv.index('--extrair')
    if len(sys.argv) <= idx + 2:
        print("Uso: python evidencias/medir-pacotes.py --extrair [M-nn|todos] DIR")
        sys.exit(1)
    alvo = sys.argv[idx + 1]
    out_dir = sys.argv[idx + 2]
    
    if alvo == 'todos':
        modulos = list(P.keys())
    elif alvo in P:
        modulos = [alvo]
    else:
        print(f"Erro: módulo desconhecido '{alvo}'. Escolha entre {list(P.keys())} ou 'todos'.")
        sys.exit(1)
        
    os.makedirs(out_dir, exist_ok=True)
    
    print('| Módulo | Fixo (KiB) | Brief (KiB) | Fixo - Brief (KiB) | Gerado sem cab (KiB) | Dif (KiB) | Ficheiro |')
    print('|---|---|---|---|---|---|---|')
    erro_extrair = False
    for m in modulos:
        cab, corpo = gerar_pacote_texto(m)
        fpath = os.path.join(out_dir, f"pacote-{m}.md")
        with open(fpath, 'w', encoding='utf-8') as f:
            f.write(cab + corpo)
            
        partes = montar(m)
        fixo = sum(b(v) for v in partes.values()) + BRIEF
        fixo_sem_brief = (fixo - BRIEF) / KB
        
        with open(fpath, 'r', encoding='utf-8') as f:
            txt = f.read()
        corpo_lido = txt.split('<!-- FIM DO CABECALHO -->\n\n', 1)[1]
        tam_gerado_sem_cab = b(corpo_lido) / KB
        dif = abs(tam_gerado_sem_cab - fixo_sem_brief)
        
        if dif > 0.1:
            erro_extrair = True
        print(f"| {m} | {fixo / KB:.1f} | {BRIEF / KB:.1f} | {fixo_sem_brief:.1f} | {tam_gerado_sem_cab:.1f} | {dif:.2f} | `{fpath}` |")
        
    sys.exit(1 if erro_extrair else 0)

import glob

erro = False
print('nucleo_base %.1f KiB | 40 §1-§3 %.1f | 70 comum %.1f | brief 2.0' % (b(base10) / KB, b(c13) / KB, b(comum70) / KB))
print('modulo | 10 | 30 | 40 | 70 | 20 | 90 | brief | fixo | teto | ficheiro real | pacote real | fecho')
for m in P:
    partes = montar(m)
    tam = {k: b(v) for k, v in partes.items()}
    fixo = sum(tam.values()) + BRIEF
    mfiles = glob.glob(f'50-modulos/{m}-*.md')
    if mfiles:
        mtxt = rd(mfiles[0])
        tam_mod_kb = b(mtxt) / KB
        partes_check = dict(partes)
        partes_check['50'] = mtxt
    else:
        tam_mod_kb = float(ESTIMADO[m])
        partes_check = partes
    pacote = fixo / KB + tam_mod_kb
    falta = falhas_de(partes_check, m)
    ok = round(pacote, 1) <= 59.7 and tam_mod_kb <= ESTIMADO[m] and not falta
    erro |= not ok
    print('%s | %.1f | %.1f | %.1f | %.1f | %.1f | %.1f | 2.0 | %.1f | %.1f | %.2f (<=%.1f) | %.1f | %s' % (
        m, tam['10'] / KB, tam['30'] / KB, tam['40'] / KB, tam['70'] / KB, tam['20'] / KB, tam['90'] / KB, fixo / KB,
        60 - fixo / KB, tam_mod_kb, ESTIMADO[m], pacote, 'OK' if not falta else 'FALTA: ' + ', '.join(falta)))
print('RESULTADO:', 'FALHA' if erro else 'OK')
sys.exit(1 if erro else 0)
