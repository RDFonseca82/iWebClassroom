# Implantação numa escola

## Servidor

1. Reserve um nome DNS, por exemplo `classroom.escola.local`.
2. Publique o servidor com `scripts/build-release.ps1` ou descarregue o artefacto da pipeline.
3. Execute `scripts/install-server.ps1` como administrador.
4. Configure IIS/ARR como proxy inverso HTTPS para `http://127.0.0.1:7080` e ative WebSockets.
5. Proteja a pasta `C:\ProgramData\iWebClassroom\Server` e faça cópias de segurança.
6. Abra a consola por HTTPS e confirme `/health`.

## Agentes

1. Publique o agente e o atualizador para `win-x64`.
2. Execute `scripts/install-agent.ps1` por GPO, Intune, Configuration Manager ou manualmente.
3. No primeiro início, introduza a URL HTTPS, o código de inscrição, a sala e a função.
4. Depois de todos os computadores estarem inscritos, rode ou desative temporariamente o código de inscrição.

## Portas

| Origem | Destino | Porta | Uso |
| --- | --- | --- | --- |
| Professores/alunos | Servidor/proxy | TCP 443 | painel, API e WebSocket SignalR |
| Servidor | GitHub Releases | TCP 443 | atualizações, se ativas |

Não é necessária qualquer ligação iniciada do servidor para os computadores dos alunos.

## Dados

- `agents.json`: identidades, hashes dos tokens, sala e última presença;
- `audit.jsonl`: identificador, tipo, destinos e data de cada comando;
- `releases/*.json`: manifestos de atualização fornecidos aos agentes.

Por omissão estes dados ficam sob o diretório de conteúdo. Na instalação, devem ser redirecionados para `C:\ProgramData\iWebClassroom\Server` em `appsettings.Local.json`.
