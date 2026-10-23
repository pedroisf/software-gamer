# Software Gamer

Projeto de otimização de jogos para Windows 11, organizado em três módulos:

- **GPN — Gamers Private Network:** roteamento do tráfego de jogos, condicionado à validação de desempenho e compatibilidade.
- **System Cleaner:** análise e limpeza seletiva de arquivos dispensáveis, com consentimento e proteção dos dados pessoais.
- **OS & Gaming Booster:** ajustes mensuráveis, opcionais e reversíveis.

## Estado do projeto

Fase de especificação e planejamento. **A aplicação ainda não foi implementada.** Este repositório estabelece a referência técnica para as provas de conceito e o desenvolvimento até a produção.

A [especificação técnica](ESPECIFICACAO_TECNICA.md) contém arquitetura, diagramas, contratos, modelo de dados, APIs, segurança, testes, roadmap, estimativas e decisões pendentes. Fontes consultadas em 23/10/2026.

## Direção tecnológica

C#/.NET 10 e WPF para interface e serviço; worker de rede isolado em Rust; WFP e WireGuard como candidatos sujeitos às provas de conceito descritas na especificação. Componentes de kernel exigem validação, assinatura e homologação próprias.

## Etapas previstas

1. Validar captura por processo, túnel, MTU, IPC e recuperação de falhas.
2. Entregar o MVP local: interface, limpeza seletiva, perfis e restauração.
3. Entregar a GPN fechada com um caminho por sessão e catálogo restrito de jogos.
4. Executar beta, testes de compatibilidade, revisão de segurança e validação de custos.
5. Preparar produção com atualização autenticada, observabilidade e suporte.
6. Avaliar multipath somente após comprovação de benefício e custo.

## Princípios

- Menor privilégio, APIs documentadas e consentimento informado.
- Registrar o estado anterior de configurações e oferecer restauração individual e global.
- Preservar credenciais, sessões, saves e configurações pessoais.
- Não injetar código em jogos, manipular sua memória, evadir anti-cheat ou desativar segurança.
- Não prometer redução universal de ping, eliminação de perda ou aumento garantido de FPS.
- Separar métricas medidas, estimativas e dados indisponíveis.

Os critérios de conclusão de cada fase estão na seção 11 da especificação. Mudanças de arquitetura devem atualizar as decisões e os critérios de validação antes da implementação correspondente.
