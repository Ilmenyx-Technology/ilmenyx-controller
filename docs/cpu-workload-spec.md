# CPU path tracing — especificação numérica e proveniência

Referência auditada: `upstream/ResetExplode/BenchRenderer.cs` no SHA
`ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6` (MIT, GustavoReset).
Implementação Silex em `Core/Benchmarks`, com estrutura própria de raios/contatos e runner por execução.
Não é extração literal do arquivo original; o mesmo implementador leu a referência, portanto
**não há separação clean-room estrita**. Nenhum código WPF ou driver integra o novo workload.

## Perfis

| Parâmetro | Full experimental | Validação automática |
|---|---|---|
| Identificador | reset-cpu-v0.16-960x540-spp1024 | validation-32x18-spp8 |
| Imagem | 960×540 | 32×18 |
| Amostras/pixel | 1024 | 8 |
| Bucket | 32×32, com bordas recortadas | 8×8, com bordas recortadas |
| Profundidade | 6 | 6 |
| Total de amostras por passada | 530.841.600 | 4.608 |
| Threads | 1–256, escolha explícita | 1–4, padrão 1 |
| Repetição | até completar passada com duração mínima atingida | exatamente uma passada |
| Score | experimental, só se concluído | sempre null |

Buckets ordenados por distância quadrática do centro, com fator vertical 1,6; empates mantêm
ordem de criação (linhas, depois colunas). Todos os workers fazem a mesma imagem completa por passada.
Não adicionar aquecimento: o renderizador CPU original não possui warmup explícito.
`MinimumDuration` de 0 representa uma passada; máximo 600 s e sempre menor que timeout.
Timeout obrigatório positivo, máximo 1200 s; padrão full 120 s e validation 10 s.
Checks de cancelamento cooperativos; não é limite rígido imposto pelo kernel.

## Cena e câmera

Cinco esferas centrais, valores em float32:

| Centro (x,y,z) | Raio | Material | Albedo | Rugosidade |
|---|---|---|---|---|
| 0; 1,1; 0 | 1,1 | vidro | 0,98 | 0 |
| −2,5; 0,9; 0,9 | 0,9 | metal | 0,92 | 0,03 |
| 2,4; 0,8; 0,7 | 0,8 | difuso | 0,85 | 0 |
| 1; 0,45; −1,9 | 0,45 | metal | 0,85 | 0,25 |
| −1,1; 0,35; −2 | 0,35 | difuso | 0,15 | 0 |

14 esferas adicionais: ângulo i/14 × τ, centro `(cos(a)×4,2; 0,28; sin(a)×4,2+1,5)`,
raio 0,28; materiais metal/difuso/vidro em ciclo; albedo 0,9 nos índices pares e 0,3 nos ímpares;
rugosidade 0,1. Lâmpada em (−4;7;−3), raio 2,2, radiância 9.
Chão y=0, limite de interseção t<60; xadrez por floor(x×0,9)+floor(z×0,9), albedos 0,62/0,38.
Câmera em (0;2,1;−7,2), alvo (0;0,75;0), vertical UnitY, FOV vertical 38 graus,
aspect width/height; imagem cresce para baixo. Operações Vector3 e MathF preservam float32.

## Caminhos e RNG

Seed por pixel: `unchecked(uint(y*width+x)*9781 + 0x9E3779B9)`.
Xorshift32 em ordem: xor<<13, xor>>17, xor<<5; uniforme = `(state>>8) / 16777216f`.
Cada amostra consome jitter x e y, normaliza o raio e acumula radiância limitada a 4.
A média por pixel precede exposição/tonemapping. Cada pixel tem stream próprio; agendamento não altera RNG.

Interseção esfera: equação quadrática com direção normalizada, primeiro t próximo >1e−4,
senão t distante; só aceitar t menor que a melhor interseção. Plano considera dy<−1e−6.
Raio secundário deslocado 1e−3 na normal orientada. Céu = 0,06 + 0,22×max(0,dy).
Difuso: amostra direta no cone subtendido pela lâmpada, teste de sombra e peso
9×cosIncidência×2×(1−cosCone); depois hemisfério cosseno ponderado com duas amostras.
Metal: reflexão acrescida de rugosidade × vetor aleatório por rejeição dentro da esfera unitária;
rejeitar direção voltada para dentro. Vidro: índice 1,5, Schlick com expoente 5,
reflexão total interna não consome o sorteio de Fresnel; refração normalizada.
Roleta russa desde bounce=3: q=max(0,05;1−throughput); rejeitar se RNG<q, senão dividir por 1−q.
Bases ortogonais usam UnitY quando |nx|>0,9 e UnitX caso contrário, com ordem de Cross preservada.

Tonemapping: exposição ×1,1; razão ACES aproximada com coeficientes 2,51/0,03/2,43/0,59/0,14;
clamp [0,1], potência 1/2,2, ×255+0,5 e truncamento inteiro. Saída cinza BGRA32 alfa 255.
Hash SHA256 dos bytes BGRA little-endian em ordem de linhas; hashing fora da medição.

## Referência reduzida reproduzível

`./scripts/generate-cpu-reference.sh` (modo padrão `reduced`) verifica o SHA e a ausência de alterações em BenchRenderer.cs,
copia a fonte apenas para diretório temporário próprio, altera exclusivamente as quatro constantes
width/height/spp/bucket, compila sem WPF e compara execuções 1/4 threads. O harness recusa as
constantes completas antes de qualquer chamada Run. O diretório temporário é removido ao final.
O gerador não integra a solução nem roda durante build/test padrão.
`tools/CpuReference` é template para esse diretório isolado, não projeto compilável diretamente na árvore.

Fixture de teste gerada: `tests/Silex.Audit.Tests/Fixtures/cpu-reference.bgra` (2.304 bytes),
com metadados adjacentes e atribuição MIT. Hash observado no CachyOS x64/.NET 10.0.0:
`4a2ff589647b89851f86e20f15d093b8d61dff21f75c9fd9e5b11b8a02d8e830`.
O teste Silex compara **todos os pixels** e o hash, não usa seu próprio output como referência.
Diferenças em outro JIT/ISA devem ser investigadas; não atualizar o golden apenas para fazer o teste passar.

## O que esse teste não prova

Equivalência de imagem reduzida não prova igualdade em todos os pixels/amostras do perfil completo,
performance equivalente, bit-exatidão em Windows/.NET 8 ou comparação válida de score.
Workload completo e duração sustentada: NÃO EXECUTADOS. Não há limites térmicos implementados.


## Corpus complementar: pixels na resolução original

Modo `./scripts/generate-cpu-reference.sh original-pixels`: o mesmo SHA é verificado, a fonte original
é copiada para temporário **sem alteração de constantes ou código**, e o harness invoca por reflexão
apenas RenderBucket para 16 coordenadas fixas, em buckets 1×1. Nunca chama Run nesse modo.
16 × 1024 = 16.384 amostras no oracle; nenhuma imagem completa é renderizada.
Os pontos incluem cantos, centro, posições laterais e bordas de bucket. A cobertura é amostral;
não se afirma que todos os materiais/interações sejam cobertos por essas posições.

Fixture cpu-original-pixels.json contém coordenadas/BGRA, dimensões/SPP, versão da fonte e atribuição.
Hash dos 16 valores BGRA little-endian na ordem do fixture:
`a1b6d7aed6fad8729e62a09a4bf4a3111c9683f65ace706ba0ab2ccc57568622`.
Testes Silex usam o mesmo CpuPathScene do runner, com câmera/seed em 960×540 e 1024 spp,
e paralelismo máximo 1/4. O teste limita explicitamente a 16 pontos e 10 s, sem score.
Constantes e imagem reduzida anterior continuam preservadas; versões de algoritmo e medição não mudaram.

O harness valida os dois modos e não permite full genérico. Template tem TreatWarningsAsErrors=true;
comparações de constantes passam por método para evitar warning de código inatingível ao compilar
um modo com as constantes do outro. Nenhum warning foi suprimido por configuração.
