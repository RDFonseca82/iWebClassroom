# iWebClassroom

Plataforma de gestão de sala de aula para redes Windows. Uma escola executa um servidor central e instala o mesmo agente nos computadores de professores e alunos.

## Estado atual

Este repositório contém um MVP funcional com:

- consola web do professor para listar e selecionar computadores;
- salas temporárias para computadores partilhados e salas fixas sincronizadas a partir da cloud;
- registo de professores na cloud, aprovação pelo administrador da escola e PIN pessoal de seis algarismos;
- PIN de professor separado do token administrativo;
- inscrição de agentes com código temporário e token individual;
- ligação em tempo real por SignalR, com reconexão automática;
- abertura de aplicações permitidas por uma lista no servidor **e** outra lista local no agente;
- envio de mensagens aos alunos;
- espelhamento visível do ecrã do professor para os alunos da mesma sala;
- registo de auditoria dos comandos, sem guardar o conteúdo das mensagens;
- atualização automática do agente e do servidor por pacote MSI assinado, SHA-256 e certificado Authenticode fixado localmente;
- scripts de publicação e instalação Windows;
- pipeline GitHub Actions para compilar e produzir artefactos Windows;
- sincronização bidirecional das definições entre cada servidor escolar e `cloud.iwebclassroom.app`;
- início de sessão administrativo com as credenciais técnicas do servidor, obtenção automática do token e receção da licença cloud.

Todas as interfaces destinadas ao utilizador estão disponíveis em Português de Portugal, Inglês e Espanhol. O idioma inicial é escolhido durante a instalação e pode ser alterado posteriormente nas consolas web.

Os computadores partilhados são registados uma única vez sem sala fixa. Quando o professor cria uma sala temporária, os alunos escolhem-na na lista e introduzem o PIN temporário mostrado pelo professor. Esse PIN é diferente do PIN pessoal do professor. A sala termina manualmente, ao atingir a duração definida ou quando o professor deixa de estar online.

Nas salas fixas, o administrador cria a sala no portal cloud e associa-a a um servidor local. Os agentes fixos usam o código dessa sala e ligam-se sempre à mesma sala. Só pode existir um professor ativo por sala e cada professor só pode dirigir uma aula de cada vez.

O espelhamento do MVP usa JPEG a aproximadamente 4 imagens por segundo. É adequado para demonstrações de aplicações e explicações; WebRTC será necessário para vídeo fluido e áudio.

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
- ativar WebSockets no proxy IIS/ARR e garantir baixa latência entre o servidor e as VLAN dos alunos;
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
$env:IWEBCLASSROOM_TEACHER_PIN = "246810"
$env:Security__RequireHttps = "false"
dotnet run --project src/iWebClassroom.Server --urls http://localhost:7080
```

Abra `http://localhost:7080` para aceder à consola. Noutro terminal:

```powershell
dotnet run --project src/iWebClassroom.Agent
```

O agente só aceita HTTP em `localhost`. Numa escola, o endereço tem obrigatoriamente de usar HTTPS.

## Ligação à gestão central

Cada escola pode ter vários servidores. No portal central, registe cada servidor com um utilizador técnico e uma palavra-passe próprios. No servidor local:

1. abra `https://<servidor>/admin.html`;
2. introduza o utilizador e a palavra-passe técnicos mostrados pelo portal quando o servidor foi criado;
3. aguarde pela autenticação, obtenção do token, licença e definições;
4. confirme que o estado passa a **Sincronizado** e que a licença apresentada é a esperada.

A palavra-passe nunca é guardada no servidor local. A API devolve um token individual que fica cifrado com DPAPI ao nível da máquina em `cloud-sync-state.json`; a licença offline fica em `cloud-license.json`. O painel recebe uma sessão administrativa local temporária de oito horas. A cada 60 segundos são enviados inventário, versões e alterações locais, e recebidas as definições centrais e uma licença atualizada. Se os dois lados alterarem a mesma configuração, vence a revisão central mais recente e o conflito fica assinalado no painel local.

O token de `bootstrap-secrets.json` permanece disponível no ecrã em **Acesso local de recuperação**, para permitir a administração quando a cloud estiver indisponível.

Se o servidor for reinstalado, substituído ou perder o seu diretório `ProgramData`, o portal central mantém a associação segura à instalação anterior. Nesse caso:

1. abra a ficha desse servidor em `cloud.iwebclassroom.app`;
2. na área **Credenciais e acesso**, escolha **Permitir nova instalação** e confirme;
3. volte à administração do servidor local e use o mesmo utilizador e palavra-passe técnicos.

Esta operação invalida imediatamente o token da instalação anterior, limpa a identidade e os contadores reportados por essa instalação e mantém a palavra-passe técnica. Não é necessária qualquer alteração à base de dados.

São sincronizados HTTPS, limites do espelhamento, duração predefinida das aulas, aplicações permitidas e política de atualização. Segredos, caminhos locais e identidade dos agentes nunca são enviados como configuração.

O portal cloud é mantido no seu projeto de implantação próprio. Este repositório contém apenas o cliente da API no servidor Windows; as alterações de base de dados e do portal são entregues diretamente nesse projeto.

## Instaladores Windows

Cada release produz dois MSI self-contained, sem necessidade de instalar previamente o .NET:

- `iWebClassroom-Agent-<versão>-win-x64.msi`
- `iWebClassroom-Server-<versão>-win-x64.msi`

O instalador apresenta Português de Portugal, Inglês e Espanhol na mesma interface. No servidor, a escolha define o idioma inicial das consolas. No agente, além do idioma, é pedido o endereço HTTPS do servidor local e esse valor aparece já preenchido no primeiro registo do computador.

Instalação silenciosa:

```powershell
msiexec.exe /i iWebClassroom-Server-0.6.0-win-x64.msi /qn INSTALLLANGUAGE=pt-PT /norestart
msiexec.exe /i iWebClassroom-Agent-0.6.0-win-x64.msi /qn SERVERURL="https://classroom.escola.local" INSTALLLANGUAGE=pt-PT ASSIGNMENTMODE=Shared /norestart
msiexec.exe /i iWebClassroom-Agent-0.6.0-win-x64.msi /qn SERVERURL="https://classroom.escola.local" INSTALLLANGUAGE=pt-PT ASSIGNMENTMODE=Fixed CLASSROOMID="SALA-101" /norestart
```

`INSTALLLANGUAGE` aceita `pt-PT`, `en-US` ou `es-ES`. `SERVERURL` é obrigatório numa primeira instalação silenciosa do agente. `ASSIGNMENTMODE` aceita `Fixed` ou `Shared`; no modo fixo, `CLASSROOMID` deve corresponder ao código criado no portal cloud. Estes parâmetros podem ser distribuídos por GPO, Intune ou Microsoft Configuration Manager.

O MSI do servidor instala o serviço `iWebClassroom.Server`, iniciado automaticamente em `http://127.0.0.1:7080`, e protege a pasta `C:\ProgramData\iWebClassroom\Server`. No primeiro arranque são criados o token administrativo, o código de inscrição e um PIN de professor de seis algarismos em:

```text
C:\ProgramData\iWebClassroom\Server\bootstrap-secrets.json
C:\ProgramData\iWebClassroom\Server\teacher-access.json
```

Esse ficheiro só pode ser lido por `SYSTEM` e pelos administradores locais. O servidor deve ser publicado através de IIS/ARR ou outro proxy HTTPS com WebSockets ativos.

O MSI do agente instala em `C:\Program Files\iWebClassroom\Agent`, cria uma entrada no menu Iniciar e configura o arranque para todos os utilizadores. A URL e o idioma são guardados em `HKLM\SOFTWARE\iWebClassroom\Agent`. A identidade partilhada do computador fica em `C:\ProgramData\iWebClassroom\Agent\agent.json`; versões anteriores são migradas automaticamente a partir do perfil do primeiro utilizador que executar a atualização.

## Utilização diária

### Computadores partilhados

1. O técnico instala cada agente com o modo **Computador partilhado**; não é necessário configurar uma turma ou sala permanente.
2. O professor abre a consola local, introduz o seu PIN pessoal aprovado e cria uma sala temporária com nome e duração.
3. O professor mostra aos alunos o PIN temporário da sala.
4. Em cada computador, o aluno escolhe a sala disponível, introduz o PIN temporário e entra.
5. A sala termina quando o professor a encerra, quando expira ou quando o professor deixa de estar online.

### Salas fixas

1. O administrador cria a sala fixa no portal cloud, atribui-a ao servidor local e define um código, por exemplo `SALA-101`.
2. O técnico instala ou configura os agentes fixos com esse mesmo código.
3. O professor abre a consola, introduz o seu PIN pessoal e escolhe a sala e o seu posto.
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

Os projetos de instalação usam WiX Toolset 7.0.1. O ficheiro de projeto aceita a EULA `wix7`; antes de distribuir comercialmente, a organização deve rever os termos [OSMF do WiX](https://docs.firegiant.com/wix/osmf/).

## Atualizações automáticas

As atualizações estão desativadas até existir um certificado de assinatura de código da organização. Para as ativar:

1. assine os MSI do agente e do servidor com Authenticode;
2. publique os MSI e os manifestos `agent-stable.json` e `server-stable.json` num GitHub Release;
3. configure a impressão digital do certificado em `update.json` no agente e em `appsettings.Local.json` no servidor;
4. disponibilize o manifesto do agente na pasta `releases` do servidor.

O certificado é fixado em cada instalação. Alterar apenas o manifesto remoto não permite instalar software assinado por outro certificado.

Consulte [arquitetura](docs/architecture.md), [segurança](docs/security.md) e [implantação](docs/deployment.md).
