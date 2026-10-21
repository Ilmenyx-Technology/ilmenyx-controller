# Decisões de identidade — IMPLEMENTADO / pendências

Ilmenyx é o nome de produto, assinatura by Bismuth Technology. Não é sufixado por Audit.
Identidade textual provisória, sem logo/paleta/fonte oficial. Versão local 0.10.0, sem lançamento.

| Identificador | Decisão | Estado / risco |
|---|---|---|
| Solução/projetos/namespaces novos | Ilmenyx.slnx, Ilmenyx.Contracts/Core/Platform.Linux/Desktop/Cli/Tests | IMPLEMENTADO; validar referências/friend assembly/testes |
| CLI local | AssemblyName/apphost ilmenyx, PackageId Ilmenyx.Cli | IMPLEMENTADO; não instalado globalmente; nenhum conflito encontrado no PATH consultado |
| Desktop | Ilmenyx.Desktop; título Ilmenyx | IMPLEMENTADO; GUI separada de CLI; sem pacote remoto |
| Company/Product | Bismuth Technology / Ilmenyx | Crédito mantenedor, não prova de titularidade jurídica |
| Ambiente gráfico | ILMENYX_DISPLAY_BACKEND, fallback SILEX_DISPLAY_BACKEND | IMPLEMENTADO; nome atual tem precedência, incluindo vazio/erro; não altera configuração do sistema |
| JSON/schema/workload/hash/score | Manter IDs silex-*, reset-*, schemas e versões metodológicas | Compatibilidade dos dados e fixtures; mudança comercial não muda matemática |
| Exportações novas/temporários do writer | ilmenyx- / .ilmenyx- | IMPLEMENTADO; nenhum dado existente renomeado/excluído |
| Helper SDK | /tmp/silex-dotnet, /tmp/silex-nuget, /tmp/silex-cli | Manter sessão existente; não é identidade pública do app |
| Histórico, branch, evidências e LICENSE | Manter nomes Silex onde históricos | Preservar autoria/proveniência/resultados; sem reescrita Git |
| WPF/RESET/upstream | Manter integralmente | SHA ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6, fora da solução nova |
| Configs/XDG/cache de usuário | Não existe persistência própria no código atual | Sem migração especulativa, sem tocar dados de usuários |
| .desktop/AppStream/Flatpak/domínio/app ID reverso/URLs da org | [PENDENTE: decisão do mantenedor] | Não existem no port; não inventar domínio/infraestrutura |
| Aliases de binários anteriores | Não criar | Nenhum artefato publicado identificado; relatórios antigos continuam legíveis |

README fornece comandos atuais. Docs técnicos anteriores registram passos históricos; seus caminhos
Silex.Audit.* podem ser interpretados pelo mapa acima, não prometem execução sem adaptação.
O caminho físico do workspace e branch não são nome de produto e permanecem intocados.
Não atribuir ao fork local relação GitHub que ainda não existe; sem remote no repositório principal.
