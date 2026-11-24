# iWebClassroom

Plataforma de gestão de sala de aula para redes Windows. Uma escola executa um servidor central e instala o mesmo agente nos computadores de professores e alunos.

## Estado atual

Este repositório contém um MVP funcional com:

- consola web do professor para listar e selecionar computadores;
- sessões temporárias de aula por sala, iniciadas com um clique e com associação automática dos postos de aluno;
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
- sincronização bidirecional das definições entre cada servidor escolar e `cloud.iwebclassroom.app`.

Todas as interfaces destinadas ao utilizador estão disponíveis em Português de Portugal, Inglês e Espanhol. O agente escolhe o idioma do Windows; a consola web deteta o navegador e permite alteração manual.

Os computadores partilhados são registados uma única vez. A identidade e a sala predefinida pertencem ao computador, não ao utilizador Windows. Quando o professor inicia uma aula, todos os postos de aluno dessa sala são associados à sessão temporária; terminar a aula remove essa associação sem alterar a configuração dos equipamentos.

O espelhamento do MVP usa JPEG a aproximadamente 4 imagens por segundo. É adequado para demonstrações de aplicações e explicações; WebRTC será necessário para vídeo fluido e áudio.

## Estrutura

| Projeto | Função |
| --- | --- |
| `iWebClassroom.Server` | API, painel web, SignalR, auditoria, catálogo de atualizações e atualizador do servidor |
| `iWebClassroom.Agent` | aplicação WPF interativa para professor ou aluno |
| `iWebClassroom.Contracts` | contratos partilhados e protocolo de comandos |
| `iWebClassroom.Updater` | instalador auxiliar que valida e aplica atualizações assinadas |

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
2. autentique-se com o token administrativo de `bootstrap-secrets.json`;
3. introduza as credenciais técnicas mostradas pelo portal;
4. confirme que o estado passa a **Sincronizado**.

A palavra-passe é usada uma única vez. A API devolve um token individual que fica cifrado com DPAPI ao nível da máquina em `cloud-sync-state.json`. A cada 60 segundos são enviados inventário, versões e alterações locais, e recebidas as definições centrais. Se os dois lados alterarem a mesma configuração, vence a revisão central mais recente e o conflito fica assinalado no painel local.

São sincronizados HTTPS, limites do espelhamento, duração predefinida das aulas, aplicações permitidas e política de atualização. Segredos, caminhos locais e identidade dos agentes nunca são enviados como configuração.

O portal cloud é mantido no seu projeto de implantação próprio. Este repositório contém apenas o cliente da API no servidor Windows; as alterações de base de dados e do portal são entregues diretamente nesse projeto.

## Instaladores Windows

Cada release produz seis MSI self-contained, sem necessidade de instalar previamente o .NET:

- `iWebClassroom-Agent-<versão>-win-x64-pt-PT.msi`
- `iWebClassroom-Agent-<versão>-win-x64-en-US.msi`
- `iWebClassroom-Agent-<versão>-win-x64-es-ES.msi`
- `iWebClassroom-Server-<versão>-win-x64-pt-PT.msi`
- `iWebClassroom-Server-<versão>-win-x64-en-US.msi`
- `iWebClassroom-Server-<versão>-win-x64-es-ES.msi`

Instalação silenciosa:

```powershell
msiexec.exe /i iWebClassroom-Server-0.3.0-win-x64-pt-PT.msi /qn /norestart
msiexec.exe /i iWebClassroom-Agent-0.3.0-win-x64-pt-PT.msi /qn /norestart
```

O MSI do servidor instala o serviço `iWebClassroom.Server`, iniciado automaticamente em `http://127.0.0.1:7080`, e protege a pasta `C:\ProgramData\iWebClassroom\Server`. No primeiro arranque são criados o token administrativo, o código de inscrição e um PIN de professor de seis algarismos em:

```text
C:\ProgramData\iWebClassroom\Server\bootstrap-secrets.json
C:\ProgramData\iWebClassroom\Server\teacher-access.json
```

Esse ficheiro só pode ser lido por `SYSTEM` e pelos administradores locais. O servidor deve ser publicado através de IIS/ARR ou outro proxy HTTPS com WebSockets ativos.

O MSI do agente instala em `C:\Program Files\iWebClassroom\Agent`, cria uma entrada no menu Iniciar e configura o arranque para todos os utilizadores. A identidade partilhada do computador fica em `C:\ProgramData\iWebClassroom\Agent\agent.json`; versões anteriores são migradas automaticamente a partir do perfil do primeiro utilizador que executar a atualização.

## Utilização diária numa sala partilhada

1. O técnico inscreve cada posto uma única vez, indicando a respetiva sala predefinida e se é um posto de aluno ou de professor.
2. O professor abre a consola, introduz o PIN de professor e escolhe a sala e o seu posto.
3. Ao carregar em **Iniciar aula**, todos os postos de aluno dessa sala ficam associados e os que estiverem online são selecionados automaticamente.
4. A sessão termina manualmente ou ao atingir a duração escolhida. Uma sala e um posto de professor só podem participar numa aula ativa de cada vez.

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
