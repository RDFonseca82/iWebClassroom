# Implantação numa escola

## Servidor

1. Reserve um nome DNS, por exemplo `classroom.escola.local`.
2. Publique o servidor com `scripts/build-release.ps1` ou descarregue o MSI da release.
3. Instale o MSI do idioma pretendido como administrador. Para implantação silenciosa use `msiexec /i <ficheiro.msi> /qn /norestart`.
4. Configure IIS/ARR como proxy inverso HTTPS para `http://127.0.0.1:7080` e ative WebSockets.
5. Proteja a pasta `C:\ProgramData\iWebClassroom\Server` e faça cópias de segurança.
6. Leia as credenciais administrativas em `C:\ProgramData\iWebClassroom\Server\bootstrap-secrets.json` e o PIN de professor em `teacher-access.json` na mesma pasta.
7. Abra a consola por HTTPS e confirme `/health`.
8. No portal central, registe o servidor e guarde o utilizador e a palavra-passe técnicos apresentados uma única vez.
9. Abra `/admin.html` no servidor, introduza o token administrativo e ligue-o à cloud com essas credenciais.

## Agentes

1. Publique o agente e o atualizador para `win-x64`.
2. Implante o MSI localizado por GPO, Intune, Configuration Manager ou manualmente.
3. No primeiro início, introduza a URL HTTPS, o código de inscrição, a sala predefinida e a função do posto. Esta configuração é feita uma única vez por computador, independentemente dos utilizadores Windows que o utilizem.
4. Depois de todos os computadores estarem inscritos, rode ou desative temporariamente o código de inscrição.
5. Confirme que a identidade partilhada foi criada em `C:\ProgramData\iWebClassroom\Agent\agent.json`.

Numa sala fixa, use a mesma sala predefinida em todos os postos de aluno e marque apenas o computador do professor como `Teacher`. Num portátil de professor que circule entre salas, a sala predefinida é apenas informativa: a consola permite escolhê-lo como professor de qualquer sala.

## Portas

| Origem | Destino | Porta | Uso |
| --- | --- | --- | --- |
| Professores/alunos | Servidor/proxy | TCP 443 | painel, API e WebSocket SignalR |
| Servidor | GitHub Releases | TCP 443 | atualizações, se ativas |
| Servidor | `cloud.iwebclassroom.app` | TCP 443 | autenticação e sincronização central |

Não é necessária qualquer ligação iniciada do servidor para os computadores dos alunos.

## Dados

- `agents.json`: identidades, hashes dos tokens, sala e última presença;
- `lessons.json`: sessões de aula ainda ativas e respetiva expiração;
- `audit.jsonl`: identificador, tipo, destinos e data de cada comando;
- `releases/*.json`: manifestos de atualização fornecidos aos agentes.
- `cloud-sync-state.json`: identidade cloud, revisão e token cifrado com DPAPI;
- `cloud-settings.json`: definições sincronizadas, sem palavras-passe nem tokens.

Por omissão estes dados ficam sob o diretório de conteúdo. Na instalação, devem ser redirecionados para `C:\ProgramData\iWebClassroom\Server` em `appsettings.Local.json`.
