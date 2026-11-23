# Modelo de segurança

O iWebClassroom controla computadores de menores e deve ser tratado como software administrativo sensível.

## Controlos já implementados

- HTTPS obrigatório no agente, exceto em `localhost` para desenvolvimento;
- segredo administrativo e código de inscrição nunca incluídos no repositório;
- comparação de segredos em tempo constante;
- tokens de agente aleatórios, com hash no servidor e DPAPI no cliente;
- comandos tipados, sem shell remoto;
- lista de aplicações autorizadas validada no servidor e no agente;
- isolamento dos fluxos de ecrã por sala e publicação limitada a agentes `Teacher`;
- limite de tamanho e frequência dos frames;
- auditoria de tipo, data e destinos dos comandos;
- atualizações por HTTPS, SHA-256, Authenticode e certificado fixado localmente;
- indicador visível enquanto o professor partilha o ecrã.

## Limitações do MVP

- o painel usa um token administrativo partilhado, não contas individuais;
- o registo de agentes está num ficheiro JSON, adequado a uma escola pequena, mas não a alta disponibilidade;
- não existe ainda revogação imediata de uma ligação SignalR já autenticada;
- os frames passam pelo servidor e não têm cifragem aplicação-a-aplicação adicional ao TLS;
- a política de retenção do ficheiro de auditoria tem de ser definida pela escola;
- instaladores MSI e certificado de produção têm de ser criados antes de ativar as atualizações.

## Requisitos de produção

- certificado TLS emitido pela PKI da escola ou por autoridade pública e nome DNS estável;
- tokens com pelo menos 32 bytes aleatórios e rotação regular do código de inscrição;
- firewall a permitir apenas 443 dos segmentos necessários;
- conta de serviço dedicada, cópias de segurança de `ProgramData\iWebClassroom` e ACL restrita;
- certificado Authenticode guardado em HSM ou serviço de assinatura, nunca em ficheiro no repositório;
- avaliação de impacto de proteção de dados e informação clara a professores, alunos e encarregados de educação;
- teste piloto numa sala antes de implantação global.
