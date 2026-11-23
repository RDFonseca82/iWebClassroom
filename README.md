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

Todas as interfaces destinadas ao utilizador estão disponíveis em Português de Portugal, Inglês e Espanhol. O agente escolhe o idioma do Windows; a consola web deteta o navegador e permite alteração manual.

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
msiexec.exe /i iWebClassroom-Server-0.2.0-win-x64-pt-PT.msi /qn /norestart
msiexec.exe /i iWebClassroom-Agent-0.2.0-win-x64-pt-PT.msi /qn /norestart
```

O MSI do servidor instala o serviço `iWebClassroom.Server`, iniciado automaticamente em `http://127.0.0.1:7080`, e protege a pasta `C:\ProgramData\iWebClassroom\Server`. No primeiro arranque são criados tokens aleatórios em:

```text
C:\ProgramData\iWebClassroom\Server\bootstrap-secrets.json
```

Esse ficheiro só pode ser lido por `SYSTEM` e pelos administradores locais. O servidor deve ser publicado através de IIS/ARR ou outro proxy HTTPS com WebSockets ativos.

O MSI do agente instala em `C:\Program Files\iWebClassroom\Agent`, cria uma entrada no menu Iniciar e configura o arranque para todos os utilizadores.

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
