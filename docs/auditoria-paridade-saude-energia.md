# Auditoria de paridade — DiskHealth e BatteryInfo (RESET EXPLODE → Ilmenyx)

Data: 2026-10-22. Upstream lido somente leitura em `upstream/ResetExplode` @ `ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6`
(`DiskHealth.cs`, `BatteryInfo.cs`); submódulo não alterado. Ilmenyx @ `2b96a0b`: `SmartctlDiagnostic.cs`,
`SmartctlReportImport.cs`, `KernelDiagnosticSensors.cs`, `HwmonParser.cs`, `LinuxHardwareInfoProvider.cs`, `Sensors.cs`.
Método: leitura de código dos dois lados e comparação de comportamento. **Nenhum teste de execução comparativa** (o
original é WPF/Windows e não executa aqui: WIN-01). Afirmações marcadas *[inferido]* não foram verificadas em execução.

## 1. DiskHealth

### O que o original faz (fato, do código)
- Fontes Windows: `Win32_DiskDrive` (modelo, serial, firmware, tamanho, PNP), `MSFT_PhysicalDisk` (saúde 0/1/2/5, tipo de mídia,
  barramento), `MSFT_StorageReliabilityCounter` (temperatura, desgaste, horas ligado, erros, ciclos), `MSStorageDriver_FailurePredictStatus`
  (falha prevista), letras de unidade e, como complemento, sensores LibreHardwareMonitor (temperatura, desgaste, total lido/gravado).
- **Veredito por disco** (SAUDÁVEL / ATENÇÃO / CRÍTICO / SEM DADOS) com textos prontos: falha prevista ou saúde Windows 2 → crítico;
  desgaste ≥95 crítico, ≥80 atenção; temperatura SSD/NVMe 70/85 °C e HDD 55/65 °C; erros não corrigidos → atenção.
- Métricas exibidas: temperatura (e máxima), desgaste/vida restante, horas ligado, ciclos, erros leitura/escrita, total lido/gravado, falha
  prevista, saúde segundo o Windows, firmware. Cabeçalho com tipo, barramento, tamanho decimal e unidades (letras).
- Exibe **modelo e serial** no cartão; descarta valores ≥1e12 como "não suportado".

### O que o Ilmenyx cobre hoje (fato)
| Item do original | Ilmenyx | Estado |
|---|---|---|
| Inventário de discos e tamanho | `/sys/block/*/size` → `DiskInfo(Device, Capacity)` | Coberto (só nome e capacidade) |
| Falha prevista / S.M.A.R.T. geral | `smartctl --health` → `reportedPassed` booleano; também por importação offline | **Parcial**: só o booleano, sem veredito |
| Temperatura | hwmon (`nvme`, outros drivers validados) em Sensores | Coberto como leitura bruta, **não associada ao disco/cartão** |
| Desgaste, horas ligado, erros, ciclos, total gravado/lido | não lidos | **Lacuna** |
| Veredito SAUDÁVEL/ATENÇÃO/CRÍTICO e limiares | ausente por regra | **Divergência deliberada** (AGENTS: sem saúde/score) |
| Modelo, serial, firmware | não exportados | **Divergência deliberada** (privacidade) |
| Tipo de mídia (HDD/SSD) e barramento (NVMe/SATA/USB) | não coletados | Lacuna *[viável via `/sys/block/*/queue/rotational` e nome do dispositivo; não implementado]* |
| Letras de unidade | sem equivalente direto (pontos de montagem) | Lacuna; exige decisão de privacidade (caminhos) |
| Saúde segundo o Windows (`HealthStatus`) | sem equivalente Linux | Não portável |

### Observações de risco ao comparar
- `reportedPassed` (smartctl) **não é equivalente** a `PredictFailure` do Windows nem à saúde composta do original; mapear um ao outro como
  "crítico" seria inferência. O original é uma heurística própria; o Ilmenyx só repassa o relato do firmware.
- O parser atual valida apenas `smart_status.passed` e o código de saída; atributos ATA (`ata_smart_attributes`) e o log NVMe
  (`nvme_smart_health_information_log`) existem no JSON do smartctl *[inferido do formato conhecido; confirmar no relatório NVMe real já usado
  em `tools/Ilmenyx.SmartReportValidation`]* mas **não são lidos**.
- Unidades: o original usa decimal (1000) em `FmtSize`; o Ilmenyx apresenta GiB (1024) no dashboard. Diferença de apresentação, não de dado.

## 2. BatteryInfo

### O que o original faz (fato, do código)
- Fontes Windows: `Win32_Battery`, `BatteryStaticData`, `BatteryFullChargedCapacity`, `BatteryCycleCount`, `BatteryStatus`, plano de energia
  (`Win32_PowerPlan`) e sensores de potência LibreHardwareMonitor (CPU/GPU, até 6).
- **Desgaste** = `1 − capacidade atual ÷ capacidade de projeto`; veredito: ≥50 % DESGASTADA (crítico), ≥30 % ATENÇÃO, ciclos ≥1000 → ATENÇÃO,
  ≥800 ciclos nota de fim de vida; carga <20 % fora da tomada → aviso. Ciclos 0 tratado como "não informado".
- Métricas: carga, estado, autonomia estimada, capacidades (Wh), desgaste, ciclos, tensão, potência de carga/consumo, fabricante, modelo, química.
- Cartão "Energia" separado: fonte (tomada/bateria/PC de mesa), plano ativo e dica de plano Alto desempenho (acoplada à aba RESET OTIMIZA).

### O que o Ilmenyx cobre hoje (fato)
| Item do original | Ilmenyx | Estado |
|---|---|---|
| Carga %, tensão, corrente, potência | `power_supply`: `capacity`, `voltage_now`, `current_now`, `power_now` com unidades convertidas | Coberto (bruto) |
| Capacidades de projeto e atual | `energy_full(_design)` (Wh) / `charge_full(_design)` (Ah) | Coberto (bruto); **desgaste % não calculado** |
| Ciclos | `cycle_count` | Coberto (bruto) |
| Estado (carregando/descarregando), tomada | `Status`, `online`, `present` | Coberto; sem textos amigáveis |
| Desgaste, autonomia, veredito | ausentes ("sem cálculo de desgaste, autonomia, saúde") | **Divergência deliberada** hoje |
| Fabricante, modelo, química | omitidos ("sem identificadores de fabricante/modelo/serial") | **Divergência deliberada** (privacidade) |
| Plano de energia | sem equivalente (Linux: power-profiles-daemon/TLP) | Lacuna, não implementado |
| Consumo por sensores (CPU/GPU, W) | hwmon `power*_input` (W) em Sensores; RAPL não verificado | Parcial *[inferido]* |
| Cartão de PC de mesa sem bateria | `PowerSupplyInfo` vazio/ausente; sem cartão dedicado | Lacuna de apresentação |

### Observações de risco
- Calcular desgaste a partir de `energy_full/energy_full_design` seria matematicamente simples e mais próximo do original, mas **regra do
  projeto proíbe inferir saúde de leitura bruta** e a validação de bateria física é NÃO TESTADA (SENSOR-01). Não foi implementado.
- Unidades/sinais de corrente e potência variam por driver; o original assume o WMI normalizado do Windows. Não há paridade garantida de valores.

## 3. Conclusões

1. **Paridade funcional baixa por escolha de projeto**: o Ilmenyx expõe dados brutos com proveniência; o original entrega vereditos e textos.
2. Onde a diferença **não** é decisão registrada (candidatas a trabalho): atributos SMART além do booleano (temperatura, desgaste/percentual usado,
   horas, erros de mídia, para NVMe/ATA), tipo de mídia (rotacional) e associação de temperatura hwmon ao disco.
3. Onde a divergência é **decisão a confirmar com você** (não implementar sozinho): desgaste de bateria calculado, vereditos/limiares,
   exibição de modelo/serial, plano de energia.
4. Nada foi copiado do original nesta auditoria; qualquer adaptação futura deve manter atribuição MIT e o registro "não clean-room".
5. Sem teste em bateria física, disco SATA ou o app WPF: qualquer implementação nova fica NÃO TESTADA nesses caminhos até haver hardware.

## 4. Próximos passos sugeridos (aguardam decisão)
- A: ler campos adicionais do JSON smartctl (NVMe/ATA), só rótulos e unidades, sem veredito; testar com o relatório NVMe real existente.
- B: tipo de mídia via `/sys/block/*/queue/rotational` e barramento pelo prefixo do nome.
- C: desgaste de bateria como valor derivado **rotulado** (fórmula + fonte), sem veredito, só se você autorizar a exceção à regra atual.


## 5. Atualização 0.26.0 — A e B implementados
- **A (métricas SMART):** implementado como em docs/smart-import.md (valores brutos, sem veredito). Paridade parcial com o DiskHealth do
  original: temperatura, horas, ciclos, desgaste NVMe e erros de mídia; faltam setores ATA além de 5/197/198 e total gravado de ATA
  (unidade de fabricante). Validação só com fixtures; SMART-01 segue NÃO TESTADO.
- **B (tipo e barramento):** `DiskInfo.Kind/Bus` (schema de hardware 1.3) via `/sys/block/*/queue/rotational` e caminho do dispositivo
  (NVMe, SATA/ATA, USB, VirtIO, MMC/SD, virtual); transporte não reconhecido fica indisponível, sem chute. No host: nvme0n1 → não rotacional/NVMe,
  sda → rotacional/SATA, zram0 → virtual. `rotational=0` não prova SSD.
- C (desgaste de bateria) continua aguardando decisão.
