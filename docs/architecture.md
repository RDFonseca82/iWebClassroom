# Arquitetura

## Componentes

```text
Gestão central <── HTTPS/token ──> Servidores da escola
                    diretório/carga       │
Professor ── HTTPS ──> Consola web ───────┤
                              │
                              │ SignalR sobre WebSocket (TLS)
                    ┌─────────┴─────────┐
                    │                   │
             Agente Professor     Agentes Alunos
             captura o ecrã       mostram ecrã/abrem apps
```

O servidor mantém a identidade permanente do computador, a sala predefinida, a função e o estado de presença dos agentes. Os tokens são guardados apenas como hash SHA-256 no servidor. No agente, o token é cifrado com DPAPI ao nível da máquina e guardado em `ProgramData`, para continuar disponível quando muda o utilizador Windows.

### Sincronização central

1. O portal cria uma identidade técnica exclusiva para cada servidor, mesmo quando a escola tem vários.
2. O administrador introduz esse utilizador e palavra-passe no painel local; o servidor troca-os por um token opaco através de HTTPS e executa imediatamente a sincronização inicial.
3. O token fica cifrado com DPAPI ao nível da máquina; a palavra-passe não é guardada.
4. A sincronização completa devolve definições e licença offline a cada cinco minutos; a última licença recebida fica guardada localmente.
5. Um pedido separado e condicional, executado aproximadamente a cada 45 segundos, recebe apenas professores, PIN, administradores e salas. Com `ETag`, uma escola sem alterações recebe apenas `304 Not Modified`.
6. O painel usa uma sessão administrativa aleatória em memória, válida por oito horas, sem expor o token cloud ao navegador.
7. O servidor envia a revisão conhecida, o hash da configuração e as alterações locais.
8. O portal incrementa a revisão ao aceitar uma alteração. Uma revisão central mais recente prevalece sobre uma alteração local desatualizada.
9. A indisponibilidade da Internet não bloqueia aulas nem agentes; a sincronização volta a tentar em segundo plano e o token administrativo local permite acesso de recuperação.
10. A sincronização completa inclui a versão de servidor aprovada ao nível da escola. Não existir aprovação equivale a não existir atualização autorizada.

### Diretório multi-servidor de salas temporárias

1. O servidor do professor pede à cloud um código aleatório de seis algarismos e publica os seus endereços HTTPS locais.
2. Um heartbeat de 20 segundos mantém a rota da sala, a expiração e a carga do servidor. Rotas sem heartbeat são removidas.
3. O agente tenta primeiro o código no servidor habitual. Só consulta a cloud quando a sala não é local.
4. A cloud resolve o código apenas dentro da escola do servidor que fez o pedido e emite um bilhete cifrado de dois minutos, vinculado ao computador e ao servidor de destino.
5. O servidor de destino valida o bilhete na cloud, cria credenciais temporárias para o agente e associa o nome do aluno à aula.
6. O agente liga o SignalR diretamente ao destino, sem substituir a identidade persistente do servidor habitual.
7. O tráfego da aula não atravessa a cloud. Se a cloud cair depois da entrada, a sessão continua; sem cloud não é possível iniciar uma nova entrada entre servidores.

O portal guarda somente rotas ativas em `active_room_routes`. O administrador define por servidor se aceita novas aulas e o limite simultâneo. Aulas em curso são fixas ao servidor e nunca são migradas automaticamente.

### Atualização coordenada dos servidores

1. O portal lê a última release do catálogo local da Cloud e apresenta-a sem a aprovar automaticamente.
2. Um administrador aprova a versão para a escola inteira; a aprovação fica em `schools.settings.server_update_approval`.
3. Todos os servidores recebem o mesmo manifesto e versão na sincronização completa.
4. O worker local exige uma versão superior à instalada, zero agentes online, zero aulas ativas, HTTPS, SHA-256 correto e assinatura Authenticode coincidente.
5. Servidores ocupados aguardam e repetem a avaliação a cada 30 segundos. Não existe migração nem interrupção de aulas para atualizar.
6. Depois do MSI e reinício, o servidor reporta a nova versão à cloud. A aprovação pode permanecer registada: servidores já atualizados ignoram-na.
7. O servidor autentica-se na API Cloud, que valida a aprovação e fornece o manifesto e o MSI guardados no portal. O funcionamento normal não depende da disponibilidade do GitHub nem de um token GitHub.

## Fluxos principais

### Inscrição

1. O técnico introduz URL, código de inscrição, sala, nome e função no agente.
2. O servidor valida o código e emite um identificador e token aleatórios.
3. O agente protege o token com DPAPI ao nível da máquina e liga-se ao hub SignalR.
4. Uma nova inscrição da mesma máquina roda o token anterior.

### Sessão de aula

1. O professor autentica-se com um PIN próprio, escolhe a sala e o posto de professor e carrega em **Iniciar aula**.
2. O servidor cria uma sessão com duração limitada e impede outra sessão simultânea na mesma sala ou no mesmo posto de professor.
3. Todos os agentes `Student` cuja sala predefinida corresponde à sala são membros da sessão, incluindo equipamentos que se liguem mais tarde.
4. O servidor adiciona as ligações SignalR ao grupo `lesson:<id>` e limita os comandos aos membros da sessão.
5. Ao terminar ou expirar, a associação temporária desaparece; a identidade e a sala predefinida dos computadores não mudam.

### Abrir aplicação

1. O professor seleciona computadores e uma aplicação no painel.
2. O servidor valida o identificador contra a sua lista autorizada.
3. O agente recebe apenas o identificador, resolve-o no seu `apps.json` e exige um caminho absoluto existente.
4. Não existe comando de shell remoto nem envio de argumentos livres.

### Espelhamento

1. O servidor pede ao agente com função `Teacher` para iniciar a captura.
2. O agente captura o ambiente de trabalho virtual, reduz para 1280 px e codifica JPEG.
3. O servidor limita o tamanho e a cadência e transmite apenas ao grupo da sessão de aula ativa.
4. Os alunos veem uma janela explícita, maximizada, com indicação de partilha.

## Evolução prevista

- contas individuais de professores, SSO Microsoft Entra ID e permissões por turma;
- WebRTC com SFU para maior fluidez, áudio e menor consumo do servidor;
- inventário de aplicações por escola e grupos dinâmicos;
- serviço Windows separado da aplicação interativa para reforçar a proteção local das credenciais e executar políticas privilegiadas;
- telemetria operacional, retenção configurável e exportação de auditoria;
- MSI WiX assinados e promoção `dev` → `pilot` → `stable`.
