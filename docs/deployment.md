# Implantação numa escola

## Servidor

1. Reserve preferencialmente um nome DNS, por exemplo `classroom.escola.local`; também pode usar qualquer IP estável do servidor.
2. Publique o servidor com `scripts/build-release.ps1` ou descarregue o MSI da release.
3. Instale o MSI como administrador e escolha o idioma. Para implantação silenciosa use `msiexec /i <ficheiro.msi> /qn INSTALLLANGUAGE=pt-PT /norestart`.
4. Confirme que as portas TCP 7080 e 7443 estão acessíveis nas VLAN necessárias. O MSI cria regras da Firewall do Windows limitadas à sub-rede local.
5. Proteja a pasta `C:\ProgramData\iWebClassroom\Server` e faça cópias de segurança.
6. Guarde o token administrativo de recuperação existente em `C:\ProgramData\iWebClassroom\Server\bootstrap-secrets.json`. Os professores entram com um código único enviado por email após aprovação no portal cloud.
7. Abra `https://<nome-ou-ip>:7443` e confirme `/health`. O aviso inicial desaparece automaticamente nos perfis Windows onde o agente concluiu o primeiro registo.
8. No portal central, registe o servidor e guarde o utilizador e a palavra-passe técnicos apresentados uma única vez.
9. Abra `/admin.html` e introduza o utilizador e a palavra-passe técnicos apenas na ligação inicial. Depois disso, entre com uma conta de administrador da escola registada na cloud. Nenhuma das palavras-passe é guardada localmente.
10. Termine e volte a iniciar a sessão Windows, ou execute `iWebClassroom.Server.Tray.exe`, para mostrar o ícone do servidor junto ao relógio.

Os administradores da escola podem obter os dois MSI e copiar os comandos de instalação em **Portal Cloud → Escola → Transferências**. Os downloads exigem uma sessão autenticada com acesso à escola.

### Aprovar uma atualização do servidor

1. Produza o MSI assinado e `server-stable.json`, depois execute `scripts/publish-portal-release.ps1` para os copiar para o catálogo do portal Cloud.
2. Confirme na área **Servidores** do portal que aparece a nova versão.
3. Configure a impressão digital Authenticode na configuração sincronizada dos servidores.
4. Selecione **Aprovar para todos**. Não é necessário aprovar servidor a servidor.
5. Aguarde: cada servidor sincroniza a aprovação e instala quando não tiver agentes online nem aulas ativas.
6. Confirme no portal que todos reportam a versão aprovada. Um servidor que permaneça numa versão anterior está ocupado, offline ou sem certificado de assinatura configurado.

A aprovação utiliza o JSON `schools.settings`, não requer migração SQL nem token GitHub. O portal guarda o catálogo e os MSI do servidor e do agente em `storage/app/iwebclassroom/releases`; esta pasta deve ser preservada entre deployments e incluída na estratégia de backup.

### Reinstalar ou substituir o servidor

O portal associa cada credencial técnica a uma única instalação através do `instance_uid`. Antes de ligar uma reinstalação ou um servidor de substituição, abra a ficha do servidor no portal central e escolha **Permitir nova instalação** em **Credenciais e acesso**. A operação invalida o token anterior, mas mantém o utilizador e a palavra-passe técnicos. Depois, repita a ligação em `/admin.html`. Não é necessária uma migração SQL.

## Agentes

1. Publique o agente e o atualizador para `win-x64`.
2. Implante o MSI por GPO, Intune, Configuration Manager ou manualmente. Na instalação assistida, escolha o idioma e introduza a URL HTTPS do servidor local.
3. Para instalação silenciosa use `msiexec /i <ficheiro.msi> /qn SERVERURL="https://classroom.escola.local:7443" INSTALLLANGUAGE=pt-PT /norestart`. Os idiomas válidos são `pt-PT`, `en-US` e `es-ES`; `SERVERURL` é obrigatório numa primeira instalação silenciosa.
4. No primeiro início, a URL já aparece preenchida. Introduza o código de inscrição, a sala predefinida e a função do posto. O agente obtém a autoridade local pela porta 7080, instala-a no perfil Windows e continua por HTTPS na porta 7443. Num computador partilhado, cada aluno indica o seu nome e o código de seis algarismos da sala. Para roaming, a porta 7443 de todos os servidores deve estar acessível a partir de todas as VLAN dos alunos.
5. Depois de todos os computadores estarem inscritos, rode ou desative temporariamente o código de inscrição.
6. Confirme que a identidade partilhada foi criada em `C:\ProgramData\iWebClassroom\Agent\agent.json`.

Numa sala fixa, use a mesma sala predefinida em todos os postos de aluno e marque apenas o computador do professor como `Teacher`. Num portátil de professor que circule entre salas, a sala predefinida é apenas informativa: a consola permite escolhê-lo como professor de qualquer sala.

## Portas

| Origem | Destino | Porta | Uso |
| --- | --- | --- | --- |
| Agentes no primeiro registo | Servidor local | TCP 7080 | obtenção autenticada da autoridade local e redirecionamento HTTPS |
| Professores/alunos | Servidor local | TCP 7443 | painel, API e WebSocket SignalR por HTTPS |
| Servidor | `cloud.iwebclassroom.app` | TCP 443 | autenticação, sincronização, atualizações aprovadas, diretório de salas, bilhetes de roaming e heartbeat de carga |

Não é necessária qualquer ligação iniciada do servidor para os computadores dos alunos.

Antes de instalar a versão multi-servidor, execute no SQL Server do portal `database/sql/2026-11-25_active_room_routes_sqlserver.sql` ou aplique a migration Laravel equivalente. Sem esta tabela, a criação local de salas continua em modo degradado, mas a resolução de códigos entre servidores não funciona.

Antes de ativar o acesso de professores por email, execute também `database/sql/2026-11-25_teacher_access_codes_sqlserver.sql` ou aplique a migration Laravel equivalente. Configure o SMTP no `.env` e execute `php artisan config:clear` (ou volte a criar a cache com `php artisan config:cache`) após a publicação.

## Dados

- `agents.json`: identidades, hashes dos tokens, sala e última presença;
- `lessons.json`: sessões de aula ainda ativas e respetiva expiração;
- `audit.jsonl`: identificador, tipo, destinos e data de cada comando;
- `releases/*.json`: manifestos de atualização fornecidos aos agentes.
- `cloud-sync-state.json`: identidade cloud, revisão e token cifrado com DPAPI;
- `cloud-settings.json`: definições sincronizadas, sem palavras-passe nem tokens.
- `cloud-license.json`: última licença offline recebida na sincronização cloud.

Por omissão estes dados ficam sob o diretório de conteúdo. Na instalação, devem ser redirecionados para `C:\ProgramData\iWebClassroom\Server` em `appsettings.Local.json`.
