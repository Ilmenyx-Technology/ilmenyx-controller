# Organização e repositório futuro — PENDENTE DE REVISÃO

Em 2026-10-21, git remote -v do repositório principal está vazio. Upstream existe no submódulo
com URL original em .gitmodules. Este checkout não comprova fork na plataforma GitHub. Histórico
local foi preservado; nenhum remoto, organização, issue ou PR foi criado nesta tarefa.

Responsável por todas as decisões abaixo: mantenedor [PENDENTE: designar pessoas].

- [ ] Decidir organização/handle/URL e titularidade; não equiparar conta GitHub a pessoa jurídica.
- [ ] Escolher fork real com vínculo GitHub ou repositório independente com provenance explícita; avaliar estrutura atual com submódulo, sem falsificar rede de forks.
- [ ] Verificar disponibilidade de nomes e conflitos; domínio/handle livre não prova direito de marca.
- [ ] Configurar 2FA, recuperação, poucos owners, menor privilégio e revisão de acesso.
- [ ] Revisar histórico, anexos, logs, paths e secrets antes de publicar; .gitignore não remove material já versionado.
- [ ] Configurar proteção de branch, revisão e Actions com permissões mínimas; nenhum workflow novo criado aqui.
- [ ] Revisar templates, SECURITY/canal privado, política de contribuição e retenção de anexos.
- [ ] Separar release notes/artefatos Ilmenyx de Windows/RESET oficial; não rebatizar .exe original.
- [ ] Aprovar push/tag/release e empacotamento explicitamente; sem automação de publicação por padrão.

Fonte consultada em 2026-10-21: https://docs.github.com/en/pull-requests/reference/forks
Forks têm vínculo próprio na plataforma; branch/submódulo não substituem essa relação.
