# Validação da versão 0.1.0

## Verificação automatizada

- Build da aplicação WPF com .NET 10, warnings tratados como erros.
- Harness sem dependências externas: regras de elegibilidade, plano expirado, identidade alterada, journal inválido/schema futuro e concorrência de persistência.
- Integração Windows: análise, quarentena/restore/purga, arquivo ocupado/readonly, hardlink/symlink e troca de ancestral, limite de espaço, APIs de energia em leitura, identidade de processo e prioridade em filho de teste.
- Smoke da distribuição self-contained: inicialização WPF e renderização de cinco telas; a análise usa uma fixture artificial, nunca os temporários reais do runner.
- Instalador por usuário, distribuição portátil e hashes SHA-256.

Resultados e screenshots são anexados pelo workflow `Windows build and validation` como artefatos. A release identifica o commit correspondente. Consulte a execução vinculada à release para contagens finais.

## Pendências de homologação

O GitHub Actions usa Windows de servidor em seu runner e não comprova funcionamento em todas as builds do Windows 11. Ainda exigem teste físico: troca/restauração de plano OEM real, AC/DC, recuperação após queda de energia/reboot, watchdog sob falhas sistêmicas, instalação/remoção interativas, Narrator/alto contraste/DPI e compatibilidade com jogos e anti-cheats.

Nenhum resultado de FPS, frametime, temperatura ou ganho de desempenho foi medido nesta entrega. A primeira distribuição é uma pré-release para validação, não um produto certificado para uso irrestrito.
