# Arquitetura

## Componentes

```text
Professor ── HTTPS ──> Consola web / Servidor da escola
                              │
                              │ SignalR sobre WebSocket (TLS)
                    ┌─────────┴─────────┐
                    │                   │
             Agente Professor     Agentes Alunos
             captura o ecrã       mostram ecrã/abrem apps
```

O servidor mantém a identidade, sala, função e estado de presença dos agentes. Os tokens são guardados apenas como hash SHA-256 no servidor. No agente, o token é cifrado com DPAPI para o utilizador Windows atual.

## Fluxos principais

### Inscrição

1. O técnico introduz URL, código de inscrição, sala, nome e função no agente.
2. O servidor valida o código e emite um identificador e token aleatórios.
3. O agente protege o token com DPAPI e liga-se ao hub SignalR.
4. Uma nova inscrição da mesma máquina roda o token anterior.

### Abrir aplicação

1. O professor seleciona computadores e uma aplicação no painel.
2. O servidor valida o identificador contra a sua lista autorizada.
3. O agente recebe apenas o identificador, resolve-o no seu `apps.json` e exige um caminho absoluto existente.
4. Não existe comando de shell remoto nem envio de argumentos livres.

### Espelhamento

1. O servidor pede ao agente com função `Teacher` para iniciar a captura.
2. O agente captura o ambiente de trabalho virtual, reduz para 1280 px e codifica JPEG.
3. O servidor limita o tamanho e a cadência e transmite apenas ao grupo da sala.
4. Os alunos veem uma janela explícita, maximizada, com indicação de partilha.

## Evolução prevista

- contas individuais de professores, SSO Microsoft Entra ID e permissões por turma;
- WebRTC com SFU para maior fluidez, áudio e menor consumo do servidor;
- inventário de aplicações por escola e grupos dinâmicos;
- serviço Windows separado para instalação, políticas e ações privilegiadas;
- telemetria operacional, retenção configurável e exportação de auditoria;
- MSI WiX assinados e promoção `dev` → `pilot` → `stable`.
