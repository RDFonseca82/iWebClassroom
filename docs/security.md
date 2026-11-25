# Modelo de segurança

O iWebClassroom controla computadores de menores e deve ser tratado como software administrativo sensível.

## Controlos já implementados

- HTTPS obrigatório no agente depois do arranque autenticado do certificado local;
- autoridade local por servidor, certificados com SAN para todas as interfaces e renovação automática;
- segredo administrativo e código de inscrição nunca incluídos no repositório;
- comparação de segredos em tempo constante;
- tokens de agente aleatórios, com hash no servidor e DPAPI no cliente;
- código de professor aleatório e de utilização única, guardado apenas como hash, válido durante 10 minutos, limitado a cinco tentativas e separado do token administrativo;
- comandos tipados, sem shell remoto;
- lista de aplicações autorizadas validada no servidor e no agente;
- sessões exclusivas por sala, comandos limitados aos seus membros e publicação de ecrã limitada ao posto `Teacher` escolhido;
- limite de tamanho e frequência dos frames;
- auditoria de tipo, data e destinos dos comandos;
- atualizações por HTTPS, SHA-256, Authenticode e certificado fixado localmente;
- credenciais técnicas separadas por servidor, trocadas por um token opaco guardado com DPAPI ao nível da máquina;
- palavra-passe de sincronização apenas como hash adaptativo no portal e nunca persistida no servidor local;
- autenticação administrativa normal através da cloud, seguida de uma sessão local aleatória em memória com validade de oito horas;
- token administrativo persistente reservado para recuperação local quando a cloud não está disponível;
- licença cloud armazenada localmente sem guardar as credenciais que a obtiveram;
- associação do token ao `instance_uid`, rotação e suspensão individual de cada servidor;
- indicador visível enquanto o professor partilha o ecrã.

## Limitações do MVP

- a confiança inicial da autoridade local usa o código de inscrição na rede da escola; ambientes com requisitos elevados devem distribuir a raiz por GPO/Intune ou usar AD CS;
- a credencial do agente é protegida ao nível da máquina mas a aplicação ainda é executada na sessão do utilizador; um serviço Windows separado reforçará o isolamento local;
- o registo de agentes está num ficheiro JSON, adequado a uma escola pequena, mas não a alta disponibilidade;
- não existe ainda revogação imediata de uma ligação SignalR já autenticada;
- os frames passam pelo servidor e não têm cifragem aplicação-a-aplicação adicional ao TLS;
- a política de retenção do ficheiro de auditoria tem de ser definida pela escola;
- instaladores MSI e certificado de produção têm de ser criados antes de ativar as atualizações.

## Requisitos de produção

- para ambientes geridos, certificado TLS emitido pela PKI da escola ou por autoridade pública e nome DNS estável, em substituição da autoridade local automática;
- tokens com pelo menos 32 bytes aleatórios e rotação regular do código de inscrição;
- configuração e monitorização do serviço SMTP, com SPF, DKIM e DMARC para o domínio de envio;
- firewall a permitir apenas 443 dos segmentos necessários;
- conta de serviço dedicada, cópias de segurança de `ProgramData\iWebClassroom` e ACL restrita;
- certificado Authenticode guardado em HSM ou serviço de assinatura, nunca em ficheiro no repositório;
- avaliação de impacto de proteção de dados e informação clara a professores, alunos e encarregados de educação;
- teste piloto numa sala antes de implantação global.
