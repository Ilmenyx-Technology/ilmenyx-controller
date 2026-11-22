# Conquistas em cópias com emulador (Goldberg)

O Goldberg só grava uma conquista se ela estiver definida em `steam_settings/achievements.json`, ao lado da `steam_api.dll` ou `steam_api64.dll`.
Algumas cópias deixam essa pasta na raiz do jogo ou com o formato de objeto, que o emulador ignora; o jogo chama `SetAchievement`, nada é salvo e o Ilmenyx não tem o que notificar.

Ao abrir um jogo de Windows pelo Ilmenyx, depois de obter a lista de conquistas (cache local da Steam ou consulta pública, que segue a opção de uso online), o app:

- procura `steam_api.dll` e `steam_api64.dll` até 4 níveis dentro da pasta do jogo, sem seguir links, e só considera as que contêm o texto `steam_settings` (marca da família Goldberg; lê no máximo 16 MB);
- ao lado de cada uma, se não existir `steam_settings/achievements.json`, cria a pasta, o arquivo (formato de lista) e, se faltar, `steam_appid.txt`;
- nunca sobrescreve nem apaga arquivo existente; uma lista já existente, mesmo em formato inválido, é deixada como está.

O jogo lê essas configurações ao iniciar. O app espera até 5 s pela lista antes de abrir o jogo; se não ficar pronta, a correção vale na próxima abertura (a mensagem avisa).
Os arquivos criados ficam registrados no perfil do jogo e o botão **Desfazer** remove só eles (e a pasta, se ficar vazia).
