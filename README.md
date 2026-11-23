# iWebClassroom

Plataforma de gestão de sala de aula para redes Windows. Uma escola executa um servidor central e instala o mesmo agente nos computadores de professores e alunos.

## Estado atual

Este repositório contém um MVP funcional com:

- consola web do professor para listar e selecionar computadores;
- inscrição de agentes com código temporário e token individual;
- ligação em tempo real por SignalR, com reconexão automática;
- abertura de aplicações permitidas por uma lista no servidor **e** outra lista local no agente;
- envio de mensagens aos alunos;
- espelhamento visível do ecrã do professor para os alunos da mesma sala;
- registo de auditoria dos comandos, sem guardar o conteúdo das mensagens;
- atualização automática do agente e do servidor por pacote MSI assinado, SHA-256 e certificado Authenticode fixado localmente;
- scripts de publicação e instalação Windows;
- pipeline GitHub Actions para compilar e produzir artefactos Windows.

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
$env:Security__RequireHttps = "false"
dotnet run --project src/iWebClassroom.Server --urls http://localhost:7080
```

Abra `http://localhost:7080` para aceder à consola. Noutro terminal:

```powershell
dotnet run --project src/iWebClassroom.Agent
```

O agente só aceita HTTP em `localhost`. Numa escola, o endereço tem obrigatoriamente de usar HTTPS.

## Publicação Windows

```powershell
./scripts/build-release.ps1 -Version 1.0.0
```

O resultado fica em `artifacts/`. Execute os instaladores PowerShell numa consola elevada:

```powershell
./scripts/install-server.ps1 `
  -SourcePath ./artifacts/server `
  -AdminToken "<token administrativo>" `
  -EnrollmentCode "<código de inscrição>"

./scripts/install-agent.ps1 -SourcePath ./artifacts/agent
```

O servidor fica numa tarefa agendada executada como `SYSTEM`; o agente arranca na sessão interativa de cada utilizador. Em produção, coloque IIS, ARR ou outro proxy HTTPS à frente de `http://127.0.0.1:7080`, com WebSockets ativos.

## Atualizações automáticas

As atualizações estão desativadas até existir um certificado de assinatura de código da organização. Para as ativar:

1. assine os MSI do agente e do servidor com Authenticode;
2. publique os MSI e os manifestos `agent-stable.json` e `server-stable.json` num GitHub Release;
3. configure a impressão digital do certificado em `update.json` no agente e em `appsettings.Local.json` no servidor;
4. disponibilize o manifesto do agente na pasta `releases` do servidor.

O certificado é fixado em cada instalação. Alterar apenas o manifesto remoto não permite instalar software assinado por outro certificado.

Consulte [arquitetura](docs/architecture.md), [segurança](docs/security.md) e [implantação](docs/deployment.md).
