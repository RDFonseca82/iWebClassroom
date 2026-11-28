# iWebClassroom

Plataforma de gestão de sala de aula para redes Windows. Uma escola executa um ou mais servidores locais e instala o mesmo agente nos computadores de professores e alunos.

## Estado atual

Este repositório contém um MVP funcional com:

- consola web do professor para listar e selecionar computadores;
- salas temporárias para computadores partilhados e salas fixas sincronizadas a partir da cloud;
- código aleatório de seis algarismos e roaming temporário de alunos entre servidores da mesma escola;
- registo de professores na cloud, aprovação pelo administrador da escola e código de acesso único enviado por email em cada entrada;
- códigos de professor de utilização única, válidos durante 10 minutos e separados do token administrativo;
- inscrição de agentes com código temporário e token individual;
- ligação em tempo real por SignalR, com reconexão automática;
- catálogo de aplicações gerido por escola na cloud, sincronizado com os servidores e validado novamente no agente;
- envio de mensagens aos alunos;
- blackout dos ecrãs de todos os alunos ou apenas dos computadores selecionados;
- espelhamento de um monitor escolhido pelo professor para todos ou apenas para os alunos selecionados;
- visualização temporária do ecrã de um aluno escolhido, sem gravação no servidor;
- partilha do ecrã de um aluno escolhido com todos ou apenas com os computadores selecionados;
- apresentação de imagens ou vídeos em ecrã inteiro nos computadores escolhidos;
- aviso de fim de aula e prolongamento imediato em 15, 30 ou 60 minutos;
- registo de auditoria dos comandos, sem guardar o conteúdo das mensagens;
- atualização do agente e atualização coordenada dos servidores por pacote MSI assinado, SHA-256, certificado Authenticode e aprovação explícita da escola;
- scripts de publicação e instalação Windows;
- pipeline GitHub Actions para compilar e produzir artefactos Windows;
- sincronização bidirecional das definições entre cada servidor escolar e `cloud.iwebclassroom.app`;
- ligação inicial com as credenciais técnicas do servidor e início de sessão posterior com uma conta de administrador da escola validada pela cloud;
- HTTPS local gerido, certificado renovado automaticamente e escuta em todas as placas de rede IPv4/IPv6;
- versão do servidor sempre visível nas consolas web.
- ícone do servidor na área de notificação do Windows, com atalhos para administração, consola do professor, estado, versão e cópia do endereço seguro;
- sincronização leve de professores, salas e atribuições de computadores a cada 20–60 segundos, separada da sincronização completa.
- deteção da versão publicada no catálogo local da Cloud e aprovação única, por escola, antes de atualizar todos os servidores locais quando estiverem sem computadores ligados.
- página autenticada de transferências por escola, com os MSI do servidor e agente, hashes e instruções de instalação normal e silenciosa.
- agente iniciado de forma silenciosa, apenas com ícone na área de notificação, sem abrir a janela em cada início de sessão;
- consola do professor incorporada no agente dos computadores fixos atribuídos ao professor;
- estatísticas locais de aulas, computadores online, tempo de ligação, funcionalidades usadas e utilização por professor, enviadas para a cloud em horas de menor utilização.

Todas as interfaces destinadas ao utilizador estão disponíveis em Português de Portugal, Inglês e Espanhol. O idioma inicial é escolhido durante a instalação e pode ser alterado posteriormente nas consolas web.

Os computadores partilhados são registados uma única vez sem sala fixa. Quando o professor cria uma sala temporária, recebe um código aleatório de seis algarismos. Cada aluno introduz esse código e obrigatoriamente o seu nome; não é apresentada uma lista pública de salas. O professor vê o nome do aluno associado ao nome do computador. O código da sala é independente do código de acesso único enviado ao professor por email. A sala termina manualmente, ao atingir a duração definida ou quando o professor deixa de estar online.

## Salas temporárias com vários servidores

O servidor onde o posto do professor está ligado aloja a aula. Ao criar a sala, regista imediatamente na cloud o código, os endereços locais alcançáveis, a expiração e a carga atual. O código é único entre as salas ativas da escola; não precisa de ser único mundialmente porque todos os agentes já estão associados a uma escola.

Quando um aluno introduz o código:

1. o seu servidor habitual procura primeiro uma sala local com esse código;
2. se não existir, consulta o diretório central usando o token técnico do servidor;
3. a cloud localiza o servidor da aula e emite um bilhete cifrado, válido durante dois minutos e limitado ao servidor, escola, computador e sala;
4. o agente obtém e valida a autoridade HTTPS do servidor de destino, troca o bilhete por credenciais temporárias e liga-se diretamente ao respetivo SignalR;
5. o servidor habitual guardado no agente não é alterado. No próximo arranque, ou numa nova entrada, o computador volta a usar essa identidade habitual.

A cloud participa apenas na descoberta e autorização inicial. O espelhamento, os comandos e os ficheiros continuam a circular diretamente na rede da escola e não atravessam o portal central.

Cada servidor envia um heartbeat de carga aproximadamente a cada 20 segundos com aulas ativas, agentes online, memória do processo e endereços de rede. Na ficha do servidor no portal cloud, o administrador pode definir o máximo de aulas simultâneas e ativar **Não aceitar novas aulas**, permitindo esvaziar um servidor sem interromper sessões já iniciadas. Uma aula nunca é migrada enquanto está em curso. O peso de balanceamento fica registado para a evolução do encaminhamento automático; na versão atual, a aula permanece necessariamente no servidor ao qual o posto do professor está ligado.

### Continuidade quando a cloud está indisponível

As salas fixas, a criação de salas temporárias, os comandos, o espelhamento e a entrada numa sala alojada no mesmo servidor funcionam sempre de forma local. A Cloud não está no caminho crítico de uma aula normal.

Depois de 90 segundos sem contacto com a Cloud, os servidores da escola entram em modo de continuidade e elegem automaticamente um master provisório. A ordenação é determinística: versão do servidor mais recente, data da última sincronização mais recente e, em caso de empate, identificador do servidor. Os pares trocam heartbeats autenticados por HMAC a cada 15 segundos. O master provisório atribui códigos únicos, mantém o diretório das salas dos vários servidores e emite bilhetes de migração assinados. Assim, um aluno pode continuar a introduzir o código de uma sala alojada noutro servidor da escola sem depender da Internet.

Quando qualquer contacto válido com a Cloud regressa, a autoridade volta automaticamente para a Cloud. Cada servidor comunica as suas salas ativas no heartbeat normal, reconciliando o estado sem interromper aulas. A chave comum dos pares é criada pela Cloud, guardada cifrada no portal e protegida com DPAPI em cada servidor local; nunca é enviada entre servidores. Os pedidos entre pares são assinados e têm uma validade máxima de dois minutos.

Para este modo funcionar, todos os servidores têm de ter sincronizado pelo menos uma vez depois da instalação desta versão, para receberem a lista de pares, os endereços e a chave da escola. Uma divisão física da rede local pode impedir que os servidores se vejam; nesse caso cada aula continua localmente, mas não é possível migrar computadores entre os segmentos isolados até a rede ou a Cloud regressar.

O envio e a validação de um novo código de professor também exigem a cloud e o serviço SMTP. Uma sessão de professor que já esteja aberta continua válida localmente até ao respetivo prazo de oito horas; durante a falha não é possível iniciar uma nova sessão.

Se não existir outro servidor alcançável para formar o cluster provisório, uma sala criada durante a falha recebe um código local e funciona nesse servidor. O heartbeat tenta publicá-la automaticamente quando a Cloud regressa. Uma sessão entre servidores que já tenha sido autorizada continua sem a Cloud porque o vídeo e os comandos não dependem dela.

### Logs locais e diagnóstico na Cloud

O serviço Windows grava atividade, avisos e erros em JSONL diário dentro de `C:\ProgramData\iWebClassroom\Server\logs` (ou no diretório de dados configurado), com retenção local de 30 dias. O agente mantém igualmente registos detalhados em `C:\ProgramData\iWebClassroom\Agent\logs` e envia-os autenticados ao servidor local em lotes de cinco em cinco minutos. O servidor acrescenta a identidade do agente, o identificador da máquina e o nome do computador antes de os integrar no diagnóstico local. Os eventos `Information` e os avisos operacionais comuns permanecem na escola. São elegíveis para a Cloud os níveis `Error` e `Critical`, além de avisos acionáveis que incluam uma exceção técnica ou um código de falha, indisponibilidade, degradação, timeout ou repetição.

Um cursor durável percorre os registos locais e envia até 250 erros ou avisos acionáveis por lote para a Cloud a cada 10 minutos; uma falha de rede não avança o cursor dos eventos pendentes. Cada evento inclui a versão instalada do agente ou servidor que o produziu. A API volta a aplicar o mesmo filtro, mesmo quando contactada por uma versão antiga do servidor. Na ficha de cada servidor são apresentados os diagnósticos recentes com o computador de origem, versão, data, categoria, mensagem e detalhes técnicos. O centro **Erros** permite aos administradores da plataforma consultar todas as escolas e aos administradores escolares consultar apenas a sua escola, filtrar ocorrências por versão e seguir o fluxo **Em aberto → Em resolução → Resolvido**. A lista apresenta por defeito apenas ocorrências por resolver; os diagnósticos resolvidos continuam disponíveis através dos filtros **Resolvido** e **Todos os estados**. Ao reconhecer, resolver ou reabrir uma ocorrência, são atualizadas todas as ocorrências iguais produzidas pela mesma versão; o âmbito é limitado à própria escola para um administrador escolar e abrange todas as escolas para um administrador da plataforma. A retenção central é de 90 dias e a API elimina duplicados pelo identificador do evento. Execute primeiro `docs/sql/2026-11-26-server-logs.sql`, depois `docs/sql/2026-11-26-agent-logs.sql` e finalmente `docs/sql/2026-11-28-error-monitoring.sql` no SQL Server do portal.

Nas salas fixas, o administrador cria a sala no portal cloud e associa-a a um servidor local. Os agentes fixos usam o código dessa sala e ligam-se sempre à mesma sala. Só pode existir um professor ativo por sala e cada professor só pode dirigir uma aula de cada vez.

O espelhamento adaptado à rede local usa JPEG com resolução máxima de 1024 × 720, qualidade 44 e um intervalo mínimo de aproximadamente 170 ms. Frames idênticos não são enviados e o agente descarta frames de apresentação atrasados, privilegiando fluidez em vez de qualidade fotográfica. É adequado para demonstrações de aplicações e explicações; WebRTC continuará a ser necessário para vídeo fluido com áudio.

O professor escolhe o monitor a partilhar e os destinatários. A visualização do ecrã de um aluno só começa após uma ação explícita do professor, termina ao fechar a janela e é autorizada apenas dentro da aula ativa. O servidor mantém somente o último frame em memória durante cinco segundos; não cria gravações nem envia estas imagens para a cloud.

O mesmo aluno pode ser escolhido como origem de uma apresentação para a turma. O professor seleciona separadamente o **Aluno a apresentar** e os destinatários — todos ou apenas os computadores marcados. O agente do aluno envia os frames diretamente ao servidor local da aula, que os retransmite exclusivamente aos membros autorizados. Parar a partilha fecha a apresentação nos destinatários; esta função não cria gravações e continua a funcionar sem acesso à cloud.

Imagens JPEG, PNG, GIF ou BMP e vídeos MP4/WMV até 25 MB podem ser enviados aos computadores escolhidos e apresentados em ecrã inteiro. O ficheiro circula apenas pelo servidor local da aula, é validado por tipo e SHA-256 e é removido quando a aula termina.

### Estatísticas de utilização

Cada servidor mantém em `C:\ProgramData\iWebClassroom\Server\usage-statistics.json` contadores diários de aulas fixas e temporárias, duração, entradas de alunos, computadores únicos e pico online. Também contabiliza, sem guardar o conteúdo, aberturas de aplicações, espelhamentos, blackouts, visualizações/partilhas de ecrãs de alunos e apresentações multimédia. Cada agente acrescenta o número de arranques e o tempo em que esteve ligado ao servidor; envia apenas estes acumulados ao servidor local.

Os dados ficam disponíveis localmente mesmo sem Internet. Entre as 02:00 e as 06:00, o servidor tenta enviar para a cloud os dias já concluídos dos últimos 62 dias. Uma falha não elimina nem perde os contadores: o envio é repetido noutra janela de horas mortas. O portal apresenta períodos de 30, 90 ou 365 dias, totais da escola, atividade diária, utilização por servidor e os professores com maior utilização.

Não são enviados ecrãs, mensagens, ficheiros multimédia, endereços de email nem nomes de alunos. A identificação do professor é necessária apenas para calcular a utilização por professor. Em escolas com vários servidores, **Computadores/dia** é a soma dos valores diários comunicados por servidor e pode contar duas vezes um computador que tenha mudado de servidor no mesmo dia.

Para instalar as tabelas no SQL Server do portal, execute [docs/sql/2026-11-27-school-usage-statistics.sql](docs/sql/2026-11-27-school-usage-statistics.sql). O projeto Laravel inclui também a migration `2026_11_27_000020_create_server_usage_statistics_tables.php` na pasta própria do portal.

## Estrutura

| Projeto | Função |
| --- | --- |
| `iWebClassroom.Server` | API, painel web, SignalR, auditoria, catálogo de atualizações e atualizador do servidor |
| `iWebClassroom.Agent` | aplicação WPF interativa para professor ou aluno |
| `iWebClassroom.Contracts` | contratos partilhados e protocolo de comandos |
| `iWebClassroom.Updater` | instalador auxiliar que valida e aplica atualizações assinadas |

## Dimensionamento recomendado do servidor

Os valores seguintes são pontos de partida para uma máquina física ou virtual dedicada ao iWebClassroom. O número de **salas com espelhamento simultâneo** é mais importante para a rede do que o número de computadores apenas ligados, porque cada imagem recebida do professor é retransmitida para todos os alunos da respetiva aula.

| Computadores por servidor | Espelhamentos simultâneos previstos | Processador | Memória RAM | Disco do sistema | Rede do servidor | Recomendação |
| ---: | ---: | --- | ---: | ---: | --- | --- |
| Até 50 | 1–2 salas | 4 vCPU/cores modernos, 2,5 GHz ou superior | 8 GB | SSD com 80 GB | 1 GbE | Escola pequena ou piloto |
| 51–150 | Até 5 salas | 6 vCPU/cores modernos | 12–16 GB | SSD com 120 GB | 1 GbE dedicado | Escola pequena/média |
| 151–300 | Até 10 salas | 8 vCPU/cores modernos | 16 GB | SSD/NVMe com 160 GB | 2,5 GbE recomendado | Servidor dedicado e monitorização obrigatória |
| 301–500 | Até 15 salas | 12 vCPU/cores modernos | 24–32 GB | NVMe com 250 GB | 10 GbE recomendado | Fazer teste de carga; considerar dois servidores locais |
| Mais de 500 | Depende das salas ativas | Vários servidores, 8–12 vCPU cada | 16–32 GB por servidor | NVMe com 160–250 GB por servidor | 2,5/10 GbE por servidor | Dividir edifícios, pisos ou conjuntos de salas por servidor |

Requisitos e critérios adicionais:

- utilizar Windows Server 2022 ou 2025 x64, preferencialmente numa máquina virtual dedicada; não alojar no mesmo servidor outras aplicações com muitas ligações persistentes;
- reservar pelo menos 20% de CPU, memória e largura de banda para picos, reconexões e atualizações;
- usar armazenamento SSD/NVMe e manter espaço para o Windows, paginação, logs, auditoria, cópias de segurança e pacotes de atualização;
- garantir que WebSockets e a porta 7443 são permitidos entre o servidor e as VLAN dos alunos;
- dimensionar a rede com base nas salas que partilham o ecrã ao mesmo tempo. Como referência de planeamento, um JPEG médio de 100–250 KB a 4 imagens por segundo representa aproximadamente 3,2–8 Mbit/s por transmissão para **cada destinatário**. Uma aula com 30 alunos poderá, portanto, gerar cerca de 96–240 Mbit/s de saída do servidor; o valor real depende do conteúdo do ecrã, resolução e compressão;
- acima de 300 computadores, executar um teste piloto em horário real e acompanhar CPU, memória, ligações TCP, latência e tráfego de saída antes da instalação geral;
- no MVP atual, o inventário e as aulas são guardados em ficheiros JSON. Para instalações grandes, alta disponibilidade ou mais de 500 computadores por servidor, deverá ser considerada a migração para uma base de dados e uma arquitetura de escalabilidade própria.

Estes valores não são limites rígidos nem substituem testes no ambiente da escola. A própria Microsoft recomenda testes de implantação para definir recursos adequados e indica uma ligação Ethernet de pelo menos 1 Gbit/s para Windows Server. O SignalR mantém ligações TCP persistentes, consumindo memória e ligações do servidor, pelo que a Microsoft recomenda isolar a aplicação e escalar quando necessário. Consulte os [requisitos de hardware do Windows Server](https://learn.microsoft.com/windows-server/get-started/hardware-requirements) e as [orientações de alojamento e escalabilidade do ASP.NET Core SignalR](https://learn.microsoft.com/aspnet/core/signalr/scale).

## Executar em desenvolvimento

É necessário o SDK .NET 10 no Windows.

```powershell
dotnet restore iWebClassroom.slnx --configfile NuGet.Config
dotnet build iWebClassroom.slnx --configuration Release

$env:IWEBCLASSROOM_ADMIN_TOKEN = "um-token-longo-e-aleatorio"
$env:IWEBCLASSROOM_ENROLLMENT_CODE = "um-codigo-temporario"
$env:Security__RequireHttps = "false"
dotnet run --project src/iWebClassroom.Server --urls http://localhost:7080
```

Abra `http://localhost:7080` para aceder à consola. Noutro terminal:

```powershell
dotnet run --project src/iWebClassroom.Agent
```

Em produção, use `https://<nome-ou-ip-do-servidor>:7443`. O endpoint HTTP da porta 7080 existe apenas para o agente obter a autoridade de certificação durante o primeiro registo e redireciona o restante tráfego para HTTPS.

## Ligação à gestão central

Cada escola pode ter vários servidores. No portal central, registe cada servidor com um utilizador técnico e uma palavra-passe próprios. No servidor local:

1. abra `https://<servidor>/admin.html`;
2. introduza o utilizador e a palavra-passe técnicos mostrados pelo portal quando o servidor foi criado;
3. aguarde pela autenticação, obtenção do token, licença e definições;
4. confirme que o estado passa a **Sincronizado** e que a licença apresentada é a esperada.

Depois da ligação inicial, a administração local deixa de pedir a credencial técnica e passa a pedir o email e a palavra-passe de um administrador ativo da escola. A cloud valida a palavra-passe; o servidor local sincroniza apenas a identidade, o email e a função do administrador, nunca o respetivo hash. O token local continua disponível exclusivamente como recuperação offline.

A palavra-passe nunca é guardada no servidor local. A API devolve um token individual que fica cifrado com DPAPI ao nível da máquina em `cloud-sync-state.json`; a licença offline fica em `cloud-license.json`. O painel recebe uma sessão administrativa local temporária de oito horas. Professores, administradores, salas fixas e atribuições de computadores são consultados num endpoint pequeno a cada 20–60 segundos; o `ETag` evita transferir dados quando nada mudou. As salas temporárias e a carga têm um heartbeat próprio de 20 segundos, e a resolução de um código é feita apenas quando um aluno tenta entrar. Configuração, licença e o nome Windows do servidor usam a sincronização completa a cada cinco minutos; depois de uma alteração do `hostname` e reinício do serviço ou do Windows, o nome correspondente é atualizado automaticamente na Cloud. A verificação de novas versões continua independente, por omissão a cada seis horas. Se os dois lados alterarem a mesma configuração, vence a revisão central mais recente e o conflito fica assinalado no painel local.

O token de `bootstrap-secrets.json` permanece disponível no ecrã em **Acesso local de recuperação**, para permitir a administração quando a cloud estiver indisponível.

Se o servidor for reinstalado, substituído ou perder o seu diretório `ProgramData`, o portal central mantém a associação segura à instalação anterior. Nesse caso:

1. abra a ficha desse servidor em `cloud.iwebclassroom.app`;
2. na área **Credenciais e acesso**, escolha **Permitir nova instalação** e confirme;
3. volte à administração do servidor local e use o mesmo utilizador e palavra-passe técnicos.

Esta operação invalida imediatamente o token da instalação anterior, limpa a identidade e os contadores reportados por essa instalação e mantém a palavra-passe técnica. Não é necessária qualquer alteração à base de dados.

São sincronizados HTTPS, limites do espelhamento, duração predefinida das aulas, catálogo de aplicações e política de atualização. O administrador gere o catálogo em **Escola → Aplicações**; o caminho do executável tem de ser absoluto, terminar em `.exe` e existir nos computadores. O professor envia apenas o identificador da aplicação: o servidor substitui-o pelo caminho autorizado sincronizado antes de emitir o comando, e o agente volta a exigir um executável local existente. Para ativar esta área no portal, execute [docs/sql/2026-11-26-school-applications.sql](docs/sql/2026-11-26-school-applications.sql) no SQL Server. Segredos e credenciais nunca são incluídos no catálogo.

O portal cloud é mantido no seu projeto de implantação próprio. Este repositório contém apenas o cliente da API no servidor Windows; as alterações de base de dados e do portal são entregues diretamente nesse projeto.

A versão 0.9.0 acrescenta a tabela `active_room_routes` ao SQL Server do portal. Antes de instalar os novos servidores e agentes, execute `database/sql/2026-11-25_active_room_routes_sqlserver.sql` no projeto do portal ou aplique a migration Laravel `2026_11_25_000011_create_active_room_routes.php`.

## Atualizações dos servidores aprovadas pela escola

O portal cloud lê diretamente a release estável pública mais recente de `RDFonseca82/iWebClassroom` e mantém os resultados em cache durante um minuto. Na área **Servidores**, apresenta a versão instalada por servidor, a versão mais recente disponível no GitHub e o estado da aprovação. Os MSI e manifestos não são copiados para o alojamento do portal.

Uma nova versão nunca é autorizada apenas por existir no GitHub. Um administrador da escola tem de escolher **Aprovar vX.Y.Z para todos**. A aprovação é guardada nas definições JSON da escola e enviada a todos os servidores durante a sincronização completa seguinte. Não existe alteração de SQL Server para esta funcionalidade.

Depois de receber a aprovação, cada servidor:

1. confirma que a versão aprovada é superior à instalada;
2. aguarda até não existir qualquer agente online nem aula ativa;
3. volta a verificar a condição de 30 em 30 segundos, sem descarregar o MSI enquanto o servidor estiver ocupado;
4. obtém o manifesto aprovado, exige HTTPS, valida a versão exata, o SHA-256 e a assinatura Authenticode configurada localmente;
5. instala o MSI, reinicia o serviço e comunica a nova versão na sincronização seguinte.

Os servidores ocupados podem atualizar mais tarde do que os restantes, mas todos recebem exatamente a mesma versão aprovada. Aulas em curso nunca são interrompidas. Cancelar uma aprovação impede servidores que ainda não começaram; não interrompe uma instalação já iniciada. Se não estiver configurada uma impressão digital Authenticode, o portal mostra **Falta certificado de assinatura** e o servidor não instala o pacote.

Cada tag `vX.Y.Z` executa a GitHub Action, cria os MSI e manifestos e publica-os primeiro numa release em rascunho. A release só fica pública depois de todos os assets terem sido enviados. O portal exige os dois MSI e os dois manifestos com os nomes esperados; releases incompletas, rascunhos e pré-releases são ignoradas.

Os endpoints autenticados da Cloud continuam a autorizar apenas a versão aprovada para a escola, mas o MSI é descarregado diretamente da release pública. A página **Transferências** redireciona os administradores autenticados para os mesmos assets GitHub. Como o repositório é público, não é necessário `PLATFORM_GITHUB_TOKEN`; pode ser configurado opcionalmente para aumentar o limite da API.

## Instaladores Windows

Cada release produz dois MSI self-contained, sem necessidade de instalar previamente o .NET:

- `iWebClassroom-Agent-<versão>-win-x64.msi`
- `iWebClassroom-Server-<versão>-win-x64.msi`

Os administradores de escola podem descarregar estes ficheiros no portal Cloud em **Escola → Transferências**, onde também encontram os comandos de instalação normal, silenciosa, para computadores partilhados e para salas fixas.

O primeiro ecrã do instalador permite escolher Português de Portugal, Inglês ou Espanhol. A partir daí, todos os textos são apresentados apenas no idioma escolhido. No servidor, a escolha define ainda o idioma inicial das consolas. No agente é pedido apenas o endereço HTTPS do servidor local; o nome comunicado ao servidor e à Cloud é sempre o `hostname` do próprio computador Windows. Em cada ligação, o agente volta a confirmar esse nome, corrigindo automaticamente registos antigos que tenham ficado com o nome do servidor local.

Instalação silenciosa:

```powershell
msiexec.exe /i iWebClassroom-Server-0.14.6-win-x64.msi /qn INSTALLLANGUAGE=pt-PT /norestart
msiexec.exe /i iWebClassroom-Agent-0.14.6-win-x64.msi /qn SERVERURL="https://classroom.escola.local:7443" INSTALLLANGUAGE=pt-PT /norestart
```

`INSTALLLANGUAGE` aceita `pt-PT`, `en-US` ou `es-ES` e `SERVERURL` é obrigatório numa primeira instalação silenciosa do agente. A sala, a função aluno/professor e a zona partilhada deixam de ser parâmetros do instalador: o computador regista-se como **Por atribuir** e o administrador configura-o em **Escola → Computadores**. Estes parâmetros podem ser distribuídos por GPO, Intune ou Microsoft Configuration Manager.

O MSI do servidor instala o serviço `iWebClassroom.Server`, que escuta em `https://0.0.0.0:7443`, `https://[::]:7443` e na porta de arranque 7080 em todas as interfaces. O instalador abre ambas as portas na Firewall do Windows apenas para a sub-rede local e protege `C:\ProgramData\iWebClassroom\Server`. No primeiro arranque são criados o token administrativo e o código de inscrição em:

```text
C:\ProgramData\iWebClassroom\Server\bootstrap-secrets.json
C:\ProgramData\iWebClassroom\Server\certificates\
```

Esses ficheiros só podem ser lidos por `SYSTEM` e pelos administradores locais. O servidor cria uma autoridade local com validade longa e um certificado de serviço com todos os nomes e IP ativos; verifica alterações de rede e renova o certificado antes de expirar. No primeiro registo, o agente valida o código de inscrição, instala a autoridade em `CurrentUser\Root` e passa a usar HTTPS. Em computadores sem agente, distribua `iwebclassroom-root.cer` por GPO/Intune ou use um certificado de AD CS/autoridade pública. IIS/ARR passa a ser opcional.

O MSI do agente instala em `C:\Program Files\iWebClassroom\Agent`, cria uma entrada no menu Iniciar e configura o arranque para todos os utilizadores. O arranque é silencioso: a janela principal **não abre quando o utilizador inicia sessão**, embora ligação, comandos, logs e atualizações permaneçam ativos em segundo plano. A URL e o idioma são guardados em `HKLM\SOFTWARE\iWebClassroom\Agent`. A identidade partilhada do computador fica em `C:\ProgramData\iWebClassroom\Agent\agent.json`; versões anteriores são migradas automaticamente a partir do perfil do primeiro utilizador que executar a atualização.

O agente permanece ativo na área de notificação do Windows com o mesmo símbolo do portal. Um duplo clique abre a janela; o menu permite consultar estado, atribuição, computador, servidor e versão, copiar o endereço do servidor e atualizar a ligação. Fechar a janela apenas a oculta: não existe opção de saída para o utilizador e o agente só termina durante o encerramento do Windows, desinstalação ou atualização controlada. O agente permite apenas uma instância por sessão do Windows: abrir o atalho do ambiente de trabalho ou do menu Iniciar sinaliza a instância já ativa, apresenta a janela e não cria um segundo ícone junto ao relógio. Ao terminar, o agente e o ícone do servidor removem explicitamente o ícone da área de notificação.

O botão **Ligar ao servidor** aparece dentro do cartão **Estado do agente**, na barra lateral, apenas quando a ligação está interrompida. Nos computadores fixos que o administrador atribuiu como posto do professor, a área principal do agente incorpora a consola web local através do Microsoft Edge WebView2, pelo que o professor pode iniciar sessão, criar a aula e usar todos os controlos sem abrir o navegador. Neste modo, a barra lateral é reduzida a uma faixa compacta com logótipo, estado da ligação, versão e botão de ligação quando necessário; o cabeçalho e o rodapé são ocultados e a consola web usa uma vista incorporada sem a sua própria navegação lateral ou barra superior. O cartão de ações, ligeiramente mais largo, está organizado em quatro separadores — **Mensagem**, **Atenção**, **Aplicações** e **Ecrãs e multimédia** — com navegação por rato ou teclado. A consola apresenta ainda **Terminar sessão**, que revoga imediatamente o acesso no servidor e limpa a sessão guardada no computador. A navegação incorporada fica limitada ao endereço do servidor configurado. A inicialização do WebView é serializada e reutiliza o mesmo perfil local, incluindo ao escolher **Tentar novamente**, evitando falhas quando a ligação, a atualização periódica e a abertura da janela ocorrem ao mesmo tempo. Nos computadores partilhados mantém-se o formulário de entrada do aluno. O WebView2 Evergreen Runtime, incluído por omissão nas versões atuais do Windows, tem de estar disponível no posto do professor.

Os dois MSI incluem os diálogos padrão de erros, ficheiros em utilização e execução administrativa exigidos pelo Windows Installer. A geração de uma release executa a validação ICE do primeiro build e termina com erro se o MSI não for válido; não é aceite um segundo build que apenas contorne avisos como o código 2803.

O MSI do servidor instala ainda `iWebClassroom.Server.Tray`, iniciado quando um utilizador entra no Windows. O ícone com o logótipo iWebClassroom permite abrir os dois portais, consultar estado e versão, copiar `https://localhost:7443` e encerrar apenas o ícone sem parar o serviço.

## Utilização diária

### Computadores partilhados

1. O técnico instala o agente indicando apenas o servidor; o administrador atribui o computador a uma zona partilhada no portal Cloud.
2. O professor abre a consola local, indica o seu email, introduz o código único recebido e cria uma sala temporária com nome e duração.
3. O professor mostra aos alunos o código temporário de seis algarismos.
4. Em cada computador, o aluno escreve obrigatoriamente o seu nome e o código. Se a sala estiver noutro servidor da escola, o agente muda apenas a ligação da aula de forma automática.
5. Na consola, o professor vê o nome do aluno e o computador que lhe está associado.
6. A sala termina quando o professor a encerra, quando expira ou quando o professor deixa de estar online.

### Salas fixas

1. O administrador cria a sala fixa no portal Cloud e atribui-a ao servidor local.
2. Depois de os agentes aparecerem em **Computadores**, o administrador associa cada equipamento à sala e define se é de aluno ou o computador do professor.
3. O professor abre a consola, indica o seu email, introduz o código único recebido e escolhe a sala; o computador do professor é selecionado automaticamente.
4. Ao carregar em **Iniciar aula**, todos os postos de aluno dessa sala ficam associados e os que estiverem online são selecionados automaticamente.
5. A sessão termina manualmente ou ao atingir a duração escolhida. Só pode existir um professor em cada sala.

## Compilar uma release Windows

```powershell
./scripts/build-release.ps1 -Version 1.0.0
```

O resultado fica em `artifacts/`, incluindo os MSI, ZIP e manifestos de atualização. Os scripts `install-server.ps1` e `install-agent.ps1` continuam disponíveis como alternativa para instalações a partir dos ZIP.

Para assinar os MSI com um certificado já instalado no armazém do Windows:

```powershell
./scripts/build-release.ps1 -Version 1.0.0 -SigningCertificateThumbprint "<impressão digital>"
```

Sem certificado, os MSI podem ser testados e instalados manualmente, mas o Windows apresenta o editor como desconhecido e as atualizações automáticas mantêm-se desativadas.

Para assinar automaticamente nas GitHub Actions, adicione em **Settings → Secrets and variables → Actions**:

- `SIGNING_CERTIFICATE_BASE64`: conteúdo Base64 do ficheiro PFX de assinatura de código;
- `SIGNING_CERTIFICATE_PASSWORD`: palavra-passe do PFX.

O GitHub executa a assinatura com esse certificado, mas não fornece nem substitui um certificado de assinatura de código. Depois de configurar os secrets, publique uma nova tag; não reutilize uma release que tenha MSI sem assinatura.

Os projetos de instalação usam WiX Toolset 7.0.1. O ficheiro de projeto aceita a EULA `wix7`; antes de distribuir comercialmente, a organização deve rever os termos [OSMF do WiX](https://docs.firegiant.com/wix/osmf/).

## Segurança das atualizações

Mesmo depois da aprovação da escola, as atualizações permanecem bloqueadas até existir um certificado de assinatura de código da organização. Para preparar o canal:

1. assine os MSI do agente e do servidor com Authenticode;
2. publique os MSI e os manifestos assinados numa GitHub Release através de uma tag `vX.Y.Z`;
3. configure a impressão digital do certificado no portal cloud para todos os servidores e em `update.json` no agente;
4. disponibilize o manifesto do agente na pasta `releases` do servidor.

O certificado é fixado em cada instalação. Alterar apenas o manifesto remoto, publicar uma tag ou criar uma release não autoriza nem instala uma versão: continuam a ser necessárias a aprovação da escola, a ausência de computadores ligados e a assinatura esperada.

Consulte [arquitetura](docs/architecture.md), [segurança](docs/security.md) e [implantação](docs/deployment.md).
